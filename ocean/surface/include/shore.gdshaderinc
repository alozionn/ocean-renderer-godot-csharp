// Shore: what the ocean knows about the ground under it (Ocean.SetSeaFloor), and the behaviour built on it: waves calm
// in shallow water, enclosed lakes stay quiet, surf lines roll towards the beach. Without a sea floor everything here
// reports deep open sea, so the ocean looks as it did.
// Expects the shore uniforms declared by the including shader (ocean.gdshader declares them in its "shore" group).

uniform sampler2D sea_floor : filter_linear, repeat_disable; // r: water depth (m), g: signed distance to shore (m), b: open sea
uniform vec4 sea_floor_rect = vec4(0.0); // xy: world xz of the texture's corner; zw: 1 / its world size. z = 0: no sea floor.
uniform float shore_time = 0.0;           // simulation time (s), so surf restarts with the waves

struct ShoreSample {
	float depth;    // water depth (m); negative on land
	float distance; // to the open sea's shoreline (m); negative on land and in lakes
	float open;     // 1 open sea, 0 land or enclosed lake
};

ShoreSample shore_at(vec2 xz) {
	ShoreSample s = ShoreSample(1e4, 1e4, 1.0);
	if (sea_floor_rect.z <= 0.0) return s;
	vec2 uv = (xz - sea_floor_rect.xy) * sea_floor_rect.zw;
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return s;
	vec3 t = textureLod(sea_floor, uv, 0.0).rgb;
	return ShoreSample(t.r, t.g, t.b);
}

// Per-cascade wave strength (swell, chop, ripples). Long waves feel the bottom first; enclosed water keeps only a
// fraction of every cascade.
vec3 shore_calm(ShoreSample s) {
	vec3 full = vec3(shallow_swell_depth, shallow_chop_depth, shallow_ripple_depth);
	vec3 a = smoothstep(vec3(0.0), full, vec3(s.depth));
	return a * mix(lake_waves, 1.0, s.open);
}

float shore_hash(vec2 p) {
	p = fract(p * vec2(0.1031, 0.1030));
	p += dot(p, p.yx + 33.33);
	return fract((p.x + p.y) * p.x);
}

float shore_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = p - i;
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(shore_hash(i), shore_hash(i + vec2(1.0, 0.0)), f.x), mix(shore_hash(i + vec2(0.0, 1.0)), shore_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// Surf lines: bands travelling down the distance field towards the shore, brightest just behind the leading (shoreward) edge,
// trailing off seaward, broken up along the coast by noise. 0 outside the surf zone and in lakes.
float surf_lines(ShoreSample s, vec2 xz) {
	if (s.open < 0.01 || s.distance > surf_zone) return 0.0;
	float zone = 1.0 - smoothstep(0.35 * surf_zone, surf_zone, s.distance);
	float warp = shore_noise(xz * 0.025) * 0.6 * surf_spacing;
	float phase = fract((s.distance + warp + shore_time * surf_speed) / surf_spacing);
	// Soft leading edge: a hard one reads as a crease in the water, not foam.
	float line = pow(1.0 - phase, surf_sharpness) * smoothstep(0.0, 0.06, phase);
	float patchy = smoothstep(0.2, 0.7, shore_noise(xz * 0.06 + vec2(shore_time * 0.05, 0.0)));
	return line * zone * mix(0.35, 1.0, patchy) * s.open;
}
