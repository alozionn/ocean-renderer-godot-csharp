// Shore: what the ocean knows about the ground under it (Ocean.SetSeaFloor) and the stationary bodies in it
// (Ocean.AddBody), and the behaviour built on it: waves calm in shallow water and in the lee of bodies, enclosed lakes
// stay quiet, and the water runs up the beach and drains back (swash). Shore foam is not drawn here: the foam field
// injects it where crests meet waterlines or break in the shallows (foam_field.glsl). Without a sea floor or bodies
// everything here reports deep open sea, so the ocean looks as it did.
// Expects the shore uniforms declared by the including shader (ocean.gdshader declares them in its "shore" group).

uniform sampler2D sea_floor : filter_linear, repeat_disable; // r: water depth (m), g: signed distance to shore (m), b: open sea
uniform vec4 sea_floor_rect = vec4(0.0); // xy: world xz of the texture's corner; zw: 1 / its world size. z = 0: no sea floor.
// Bodies (Core.Shore.BodyLayer): fine pages around them, holding the sea floor with the bodies folded in, plus lee shelter.
uniform sampler2D body_pages : filter_linear, repeat_disable; // atlas: rgba = depth, distance, open, shelter
uniform sampler2D body_index : filter_nearest, repeat_disable; // r: page number + 1 per index cell, 0: no page
uniform vec4 body_rect = vec4(0.0); // xy: world xz of index cell (0, 0)'s corner; z: page size (m), 0: no bodies; w: pages per atlas row
uniform float body_samples = 65.0;  // samples per page side
uniform float shore_time = 0.0;           // simulation time (s), so swash restarts with the waves

const vec3 LEE_CALM = vec3(1.0, 0.8, 0.3); // share of each cascade a body's lee takes (Core.Sampling.ShoreCalm)

struct ShoreSample {
	float depth;      // water depth over the ground or a body (m); negative on land and in bodies
	float distance;   // to the open sea's shoreline or a body's waterline (m); negative on land, in bodies and in lakes
	float open;       // 1 open sea, 0 land or enclosed lake
	float shelter;    // 0-1: lee of a body
	float wave_depth; // water depth over the terrain alone (m): calms the waves (a rock or reef breaks a swell, it does not calm it)
};

ShoreSample shore_at(vec2 xz) {
	ShoreSample s = ShoreSample(1e4, 1e4, 1.0, 0.0, 1e4);
	if (sea_floor_rect.z > 0.0) {
		vec2 uv = (xz - sea_floor_rect.xy) * sea_floor_rect.zw;
		if (uv.x >= 0.0 && uv.y >= 0.0 && uv.x <= 1.0 && uv.y <= 1.0) {
			vec3 t = textureLod(sea_floor, uv, 0.0).rgb;
			s = ShoreSample(t.r, t.g, t.b, 0.0, t.r);
		}
	}
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
				s = ShoreSample(t.r, t.g, t.b, t.a, s.wave_depth);
			}
		}
	}
	return s;
}

// Per-cascade wave strength (swell, chop, ripples). Long waves feel the bottom first; enclosed water keeps only a
// fraction of every cascade; the lee of a body loses most of its swell and chop.
vec3 shore_calm(ShoreSample s) {
	vec3 full = vec3(shallow_swell_depth, shallow_chop_depth, shallow_ripple_depth);
	vec3 a = smoothstep(vec3(0.0), full, vec3(s.wave_depth));
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

// Swash: the water runs up the beach (or a rock's foot) and drains back. Each stretch of coast runs on its own clock
// (slow noise in the phase) and each run-up has its own strength, so the shore ebbs and flows unevenly. Returns the
// run-up (0-1) that lifts the surface near the waterline. Only over beaches and grounded bodies: water deeper than ~3 m
// at the waterline (a floating hull, a sheer wall) gets none; a body's lee gets less.
float swash(ShoreSample s, vec2 xz) {
	if (swash_height <= 0.0 || s.distance > swash_width || s.distance < -2.0 * swash_width) return 0.0;
	// Land, or open sea; not a lake. Shallow at the waterline.
	float where = max(s.open, step(s.depth, 0.0)) * (1.0 - smoothstep(1.0, 3.0, s.depth))
		* (1.0 - smoothstep(0.0, swash_width, s.distance)) * smoothstep(-2.0 * swash_width, -swash_width, s.distance)
		* (1.0 - s.shelter);
	if (where <= 0.0) return 0.0;
	float x = shore_time / swash_period + 2.0 * shore_noise(xz / swash_patch) + 0.6 * shore_noise(xz * 0.07 + 11.0);
	float cycle = floor(x), phase = fract(x);
	// A new strength each cycle, switched while the water is down (phase 0).
	float strength = mix(0.2, 1.0, shore_noise(xz / swash_patch + vec2(cycle * 7.31, cycle * 3.17)));
	float up = smoothstep(0.0, 0.18, phase) * (1.0 - smoothstep(0.25, 1.0, phase));
	return up * strength * where;
}
