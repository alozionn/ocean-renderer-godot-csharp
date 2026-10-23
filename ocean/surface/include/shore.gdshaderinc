// Shore: what the ocean knows about the ground under it (Ocean.SetSeaFloor) and the stationary bodies in it
// (Ocean.AddBody), and the behaviour built on it: waves calm in shallow water and in the lee of bodies, enclosed lakes
// stay quiet, surf rolls towards the beach in broken sets and the water runs up and drains back (swash). Without a sea
// floor or bodies everything here reports deep open sea, so the ocean looks as it did.
// Expects the shore uniforms declared by the including shader (ocean.gdshader declares them in its "shore" group).

uniform sampler2D sea_floor : filter_linear, repeat_disable; // r: water depth (m), g: signed distance to shore (m), b: open sea
uniform vec4 sea_floor_rect = vec4(0.0); // xy: world xz of the texture's corner; zw: 1 / its world size. z = 0: no sea floor.
// Bodies (Core.Shore.BodyLayer): fine pages around them, holding the sea floor with the bodies folded in, plus lee shelter.
uniform sampler2D body_pages : filter_linear, repeat_disable; // atlas: rgba = depth, distance, open, shelter
uniform sampler2D body_index : filter_nearest, repeat_disable; // r: page number + 1 per index cell, 0: no page
uniform vec4 body_rect = vec4(0.0); // xy: world xz of index cell (0, 0)'s corner; z: page size (m), 0: no bodies; w: pages per atlas row
uniform float body_samples = 65.0;  // samples per page side
uniform float shore_time = 0.0;           // simulation time (s), so surf restarts with the waves
uniform vec2 shore_wave_dir = vec2(-1.0, 0.0); // the way the swell travels (world xz, unit): against cascade 0's wind vector

const vec3 LEE_CALM = vec3(1.0, 0.8, 0.3); // share of each cascade a body's lee takes (Core.Sampling.ShoreCalm)

struct ShoreSample {
	float depth;    // water depth (m); negative on land
	float distance; // to the open sea's shoreline or a body's waterline (m); negative on land, in bodies and in lakes
	float open;     // 1 open sea, 0 land or enclosed lake
	float shelter;  // 0-1: lee of a body
};

ShoreSample shore_at(vec2 xz) {
	if (body_rect.z > 0.0) {
		vec2 cell = (xz - body_rect.xy) / body_rect.z;
		ivec2 c = ivec2(floor(cell));
		ivec2 size = textureSize(body_index, 0);
		if (c.x >= 0 && c.y >= 0 && c.x < size.x && c.y < size.y) {
			int page = int(texelFetch(body_index, c, 0).r + 0.5) - 1;
			if (page >= 0) {
				int row = int(body_rect.w);
				vec2 corner = vec2(float(page % row), float(page / row)) * body_samples;
				vec2 uv = (corner + 0.5 + fract(cell) * (body_samples - 1.0)) / vec2(textureSize(body_pages, 0));
				vec4 t = textureLod(body_pages, uv, 0.0);
				return ShoreSample(t.r, t.g, t.b, t.a);
			}
		}
	}
	ShoreSample s = ShoreSample(1e4, 1e4, 1.0, 0.0);
	if (sea_floor_rect.z <= 0.0) return s;
	vec2 uv = (xz - sea_floor_rect.xy) * sea_floor_rect.zw;
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return s;
	vec3 t = textureLod(sea_floor, uv, 0.0).rgb;
	return ShoreSample(t.r, t.g, t.b, 0.0);
}

// Per-cascade wave strength (swell, chop, ripples). Long waves feel the bottom first; enclosed water keeps only a
// fraction of every cascade; the lee of a body loses most of its swell and chop.
vec3 shore_calm(ShoreSample s) {
	vec3 full = vec3(shallow_swell_depth, shallow_chop_depth, shallow_ripple_depth);
	vec3 a = smoothstep(vec3(0.0), full, vec3(s.depth));
	return a * mix(lake_waves, 1.0, s.open) * (1.0 - s.shelter * LEE_CALM);
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

// How squarely this stretch of coast faces the incoming swell: 1 windward, -1 lee. The distance field rises at one
// metre per metre away from the shore, so its change over a step upwind is the cosine. One extra lookup, only where
// surf or swash can be (0 elsewhere).
float shore_facing(ShoreSample s, vec2 xz) {
	const float h = 2.0;
	if (s.distance > max(surf_zone, swash_width) || s.distance < -2.0 * swash_width) return 0.0;
	return clamp((shore_at(xz - shore_wave_dir * h).distance - s.distance) / h, -1.0, 1.0);
}

// A wave front of the surf: where it is (phase 0 at its shoreward leading edge, rising behind it) and how strong this
// front is on this stretch of coast. Fronts follow the distance field towards the shore, bent by slow noise so they are
// not rings; each front lights up only some stretches (surf_gaps), different ones from front to front, and the coast
// facing the swell gets more of them (surf_windward).
struct SurfFront {
	float phase;
	float strength;
};

SurfFront surf_front(ShoreSample s, float distance, vec2 xz, float facing) {
	float warp = (1.1 * shore_noise(xz * 0.011) + 0.35 * shore_noise(xz * 0.037 + 17.0)) * surf_spacing;
	float x = (distance + warp + shore_time * surf_speed) / surf_spacing;
	float front = floor(x);
	float along = shore_noise(xz / surf_patch + vec2(front * 7.31, front * 3.17));
	// Sets: every few fronts a stronger one.
	float set = 0.7 + 0.3 * sin(front * 2.39996 + 6.2831 * shore_noise(xz * 0.004 + 3.0));
	float strength = smoothstep(surf_gaps, surf_gaps + 0.25, along) * set;
	strength *= mix(1.0, smoothstep(-0.9, 0.6, facing), surf_windward) * (1.0 - s.shelter);
	return SurfFront(fract(x), strength);
}

// Surf lines: fronts brightest just behind the leading (shoreward) edge, trailing off seaward, fading in only over the
// inner part of the surf zone. 0 outside it, on land, in bodies and in lakes.
float surf_lines(ShoreSample s, vec2 xz, float facing) {
	if (s.open < 0.01 || s.distance > surf_zone || s.distance < 0.0) return 0.0;
	SurfFront f = surf_front(s, s.distance, xz, facing);
	float zone = 1.0 - smoothstep(0.15 * surf_zone, surf_zone, s.distance);
	// Soft leading edge: a hard one reads as a crease in the water, not foam.
	float line = pow(1.0 - f.phase, surf_sharpness) * smoothstep(0.0, 0.06, f.phase);
	return line * zone * f.strength * s.open;
}

// Swash: when a front reaches the shore the water runs up the beach (or a rock's foot) and drains back until the next
// one. x: run-up (0-1) for lifting the surface near the waterline; y: how fresh the uprush is, for its foam. Only over
// beaches and grounded bodies: water deeper than ~3 m at the waterline (a floating hull, a sheer wall) gets none.
vec2 swash(ShoreSample s, vec2 xz, float facing) {
	if (swash_height <= 0.0 || s.distance > swash_width || s.distance < -2.0 * swash_width) return vec2(0.0);
	// Land, or open sea; not a lake. Shallow at the waterline.
	float where = max(s.open, step(s.depth, 0.0)) * (1.0 - smoothstep(1.0, 3.0, s.depth))
		* (1.0 - smoothstep(0.0, swash_width, s.distance)) * smoothstep(-2.0 * swash_width, -swash_width, s.distance);
	if (where <= 0.0) return vec2(0.0);
	// The front that last reached the waterline (distance 0) here, and the time since, as a share of the period.
	SurfFront f = surf_front(s, 0.0, xz, facing);
	float up = smoothstep(0.0, 0.18, f.phase) * (1.0 - smoothstep(0.25, 1.0, f.phase));
	float strength = mix(0.25, 1.0, f.strength) * where;
	return vec2(up, (1.0 - smoothstep(0.0, 0.45, f.phase)) * smoothstep(0.0, 0.05, f.phase)) * strength;
}
