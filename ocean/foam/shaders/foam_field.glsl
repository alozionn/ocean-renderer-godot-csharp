#[compute]
#version 450

// Camera-local foam field. R = density (accumulated, slow decay, spreads: trails),
// G = freshness (same injection, fast decay, barely spreads: whitecaps). G <= R.
// Injection comes from the Jacobian of the summed cascade displacement, built from per-cascade partials, and from the
// shore: where a crest breaks over shallow ground. The field's decay and spread
// then shape that foam like any other, so shore foam comes and goes with the waves instead of being drawn as a band.
layout(local_size_x = 8, local_size_y = 8) in;

layout(set = 0, binding = 0) uniform sampler2D history; // linear, clamp to transparent-black border
layout(set = 0, binding = 1, rg16f) restrict writeonly uniform image2D field_out;
layout(set = 0, binding = 2) uniform sampler2D partials0;
layout(set = 0, binding = 3) uniform sampler2D partials1;
layout(set = 0, binding = 4) uniform sampler2D partials2;
layout(set = 0, binding = 5) uniform sampler2D displacement0;
layout(set = 0, binding = 6) uniform sampler2D displacement1;
layout(set = 0, binding = 7) uniform sampler2D displacement2;

// The shore data the surface reads (shore.gdshaderinc), and the shore foam settings (Surface.ShoreTextures).
layout(set = 1, binding = 0) uniform sampler2D sea_floor;  // rgba: depth, distance, open, -
layout(set = 1, binding = 1) uniform sampler2D body_pages; // rgba: depth, distance, open, shelter
layout(set = 1, binding = 2) uniform sampler2D body_index; // r: page number + 1
layout(set = 1, binding = 3, std140) uniform Shore {
	vec4 sea_floor_rect; // xy corner, zw 1 / size; z = 0: no sea floor
	vec4 body_rect;      // xy corner, z page size (0: no bodies), w pages per atlas row
	vec4 calm_depth;     // shallow swell, chop, ripple depth (m), lake waves
	vec4 near_shore;     // x shore_waves (wave floor near the waterline), y its reach inland (m), z wash depth (m), w wash gain
	vec4 breaking;       // x breaking ratio, y gain, z samples per page side, w 1 if any shore data
} sh;

layout(push_constant, std430) uniform Params {
	vec2 origin; // world xz of texel (0, 0)'s corner
	float texel_size;
	float dt;
	ivec2 shift; // integer camera move in texels since the last tick
	int size;
	float bias;
	float gain;
	float inject_rate;
	float decay;
	float fresh_decay;
	vec2 streak; // streak direction times the along-tap spacing, in texels
	float k_along;
	float k_across;
	vec4 patch_size;
	vec4 weight;
	vec4 lod;
} p;

vec2 hist(vec2 pc, vec2 offset) {
	return texture(history, (pc + vec2(p.shift) + offset) / float(p.size)).rg;
}

// shore_at in shore.gdshaderinc: (depth, distance, open, shelter) from a body page if one is there, else the sea floor,
// else deep open sea; wave_depth: the terrain's depth alone, which calms the waves.
vec4 shore_at(vec2 xz, out float wave_depth) {
	vec4 s = vec4(1e4, 1e4, 1.0, 0.0);
	if (sh.sea_floor_rect.z > 0.0) {
		vec2 uv = (xz - sh.sea_floor_rect.xy) * sh.sea_floor_rect.zw;
		if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0))))
			s = vec4(textureLod(sea_floor, uv, 0.0).rgb, 0.0);
	}
	wave_depth = s.x;
	if (sh.body_rect.z > 0.0) {
		vec2 cell = (xz - sh.body_rect.xy) / sh.body_rect.z;
		ivec2 c = ivec2(floor(cell));
		ivec2 n = textureSize(body_index, 0);
		if (c.x >= 0 && c.y >= 0 && c.x < n.x && c.y < n.y) {
			int page = int(texelFetch(body_index, c, 0).r + 0.5) - 1;
			if (page >= 0) {
				int row = int(sh.body_rect.w);
				float samples = sh.breaking.z;
				vec2 corner = vec2(float(page % row), float(page / row)) * samples;
				s = textureLod(body_pages, (corner + 0.5 + fract(cell) * (samples - 1.0)) / vec2(textureSize(body_pages, 0)), 0.0);
			}
		}
	}
	return s;
}

// x: foam the shore makes here this tick (0-1): crests breaking over shallow ground, and the wash at the moving water's
// edge on beaches. Edge foam where the water meets anything solid is drawn by the surface from the depth buffer.
// y: how much of the open-sea (Jacobian) foam belongs here: the chop calm, so whitecaps do not form over sand.
vec2 shore_foam(vec2 world) {
	if (sh.breaking.w <= 0.0) return vec2(0.0, 1.0);
	float wave_depth;
	vec4 s = shore_at(world, wave_depth);
	float depth = s.x, distance = s.y, open = s.z, shelter = s.w;
	// shore_calm in shore.gdshaderinc, with the near-shore floor (near_shore.x shore_waves, near_shore.y its reach inland).
	vec3 calm = max(smoothstep(vec3(0.0), sh.calm_depth.xyz, vec3(wave_depth)), vec3(sh.near_shore.x * smoothstep(-sh.near_shore.y, 0.0, distance)))
		* mix(1.0, sh.calm_depth.w, (1.0 - open) * smoothstep(0.0, 1.0, wave_depth)) * (1.0 - shelter * vec3(1.0, 0.8, 0.3));
	// Too deep to break: nothing more to do (most texels).
	if (depth > 8.0) return vec2(0.0, calm.y);
	vec3 d0 = textureLod(displacement0, world / p.patch_size.x, p.lod.x).xyz;
	vec3 d1 = textureLod(displacement1, world / p.patch_size.y, p.lod.y).xyz;
	vec3 d2 = textureLod(displacement2, world / p.patch_size.z, p.lod.z).xyz;
	float here = d0.y * calm.x + d1.y * calm.y + d2.y * calm.z;
	float breaking = smoothstep(0.15, 0.6, sh.breaking.x * 2.0 * max(here, 0.0) - depth) * step(0.0, depth) * open * sh.breaking.y;
	// Wash: the thin film of water at the moving edge (the terrain's depth plus the wave height here, so it follows the
	// water up the beach and back), stronger while the water is high, none in lakes. Joins the breaking foam to the edge.
	float level = wave_depth + here;
	float lake = (1.0 - open) * smoothstep(0.0, 1.0, wave_depth);
	float wash = (1.0 - smoothstep(0.0, sh.near_shore.z, level)) * step(0.0, level) * mix(0.5, 1.0, smoothstep(-0.2, 0.3, here)) * (1.0 - lake) * sh.near_shore.w;
	return vec2(clamp(max(breaking, wash), 0.0, 1.0), calm.y);
}

void main() {
	ivec2 id = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(id, ivec2(p.size)))) return;
	vec2 pc = vec2(id) + 0.5;
	vec2 world = p.origin + pc * p.texel_size;

	vec3 s = textureLod(partials0, world / p.patch_size.x, p.lod.x).xyz * p.weight.x
		+ textureLod(partials1, world / p.patch_size.y, p.lod.y).xyz * p.weight.y
		+ textureLod(partials2, world / p.patch_size.z, p.lod.z).xyz * p.weight.z;
	float jacobian = (1.0 + s.x) * (1.0 + s.y) - s.z * s.z;
	vec2 shore = shore_foam(world);
	float injected = max(clamp((p.bias - jacobian) * p.gain, 0.0, 1.0) * shore.y, shore.x);

	vec2 along = p.streak;
	vec2 dir = normalize(p.streak);
	vec2 across = vec2(-dir.y, dir.x);
	vec2 c = hist(pc, vec2(0.0));
	vec2 la = hist(pc, along) + hist(pc, -along) - 2.0 * c;
	vec2 lc = hist(pc, across) + hist(pc, -across) - 2.0 * c;
	vec2 spread = max(c + vec2(p.k_along, 0.2 * p.k_along) * la + vec2(p.k_across, 0.2 * p.k_across) * lc, vec2(0.0));

	float density = min(spread.x * exp(-p.decay * p.dt) + injected * p.inject_rate * p.dt, 1.0);
	float fresh = min(spread.y * exp(-p.fresh_decay * p.dt) + injected * p.inject_rate * 2.0 * p.dt, density);
	imageStore(field_out, id, vec4(density, fresh, 0.0, 0.0));
}
