#[compute]
#version 450

// Camera-local foam field. R = density (accumulated, slow decay, spreads: trails),
// G = freshness (same injection, fast decay, barely spreads: whitecaps). G <= R.
// Injection comes from the Jacobian of the summed cascade displacement, built from per-cascade partials.
layout(local_size_x = 8, local_size_y = 8) in;

layout(set = 0, binding = 0) uniform sampler2D history; // linear, clamp to transparent-black border
layout(set = 0, binding = 1, rg16f) restrict writeonly uniform image2D field_out;
layout(set = 0, binding = 2) uniform sampler2D partials0;
layout(set = 0, binding = 3) uniform sampler2D partials1;
layout(set = 0, binding = 4) uniform sampler2D partials2;

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

void main() {
	ivec2 id = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(id, ivec2(p.size)))) return;
	vec2 pc = vec2(id) + 0.5;
	vec2 world = p.origin + pc * p.texel_size;

	vec3 s = textureLod(partials0, world / p.patch_size.x, p.lod.x).xyz * p.weight.x
		+ textureLod(partials1, world / p.patch_size.y, p.lod.y).xyz * p.weight.y
		+ textureLod(partials2, world / p.patch_size.z, p.lod.z).xyz * p.weight.z;
	float jacobian = (1.0 + s.x) * (1.0 + s.y) - s.z * s.z;
	float injected = clamp((p.bias - jacobian) * p.gain, 0.0, 1.0);

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
