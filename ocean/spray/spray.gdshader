shader_type particles;
render_mode disable_force;

// Sea spray. Each particle, when it (re)starts, picks a random spot around the camera and asks the ocean's own data
// whether spray belongs there: fresh foam in the near foam field (crests, and crests reaching waterlines) or a breaking
// shallow from the sea floor. If not, it stays inactive for this lifetime. All on the GPU; no readback. Set by C#
// (Ocean.cs), the shore uniforms mirrored from the surface material.

uniform sampler2D displacement_0 : filter_linear, repeat_enable;
uniform sampler2D displacement_1 : filter_linear, repeat_enable;
uniform sampler2D displacement_2 : filter_linear, repeat_enable;
uniform vec3 patch_sizes = vec3(400.0, 73.0, 13.7);
uniform sampler2D foam_near : filter_linear, repeat_disable;
uniform vec4 foam_near_rect = vec4(-128.0, -128.0, 256.0, 0.00390625);
uniform vec2 camera_xz = vec2(0.0);
uniform float sea_level = 0.0;
uniform vec2 wind_dir = vec2(1.0, 0.0);

uniform float spray_radius = 160.0;
uniform float spray_threshold = 0.35;
uniform float spray_speed = 4.5;
uniform float spray_wind = 4.0;
uniform float spray_gravity = 0.5;
uniform float spray_size = 1.4;
uniform float spray_opacity = 0.45;
uniform float spray_shore = 1.0;

// Shore (same names and values as ocean.gdshader's "shore" group).
uniform float shallow_swell_depth = 6.0;
uniform float shallow_chop_depth = 5.0;
uniform float shallow_ripple_depth = 0.6;
uniform float lake_waves = 0.15;
uniform float shore_waves = 0.5;
uniform float shore_waves_land = 6.0;
uniform float breaking_ratio = 0.9;

#include "../surface/include/shore.gdshaderinc"

uint spray_pcg(uint x) {
	uint state = x * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

float spray_rand(inout uint seed) {
	seed = spray_pcg(seed);
	return float(seed >> 8u) / 16777216.0;
}

vec3 displacement_at(vec2 xz, vec3 calm) {
	return textureLod(displacement_0, xz / patch_sizes.x, 0.0).xyz * calm.x
		+ textureLod(displacement_1, xz / patch_sizes.y, 0.0).xyz * calm.y
		+ textureLod(displacement_2, xz / patch_sizes.z, 0.0).xyz * calm.z;
}

void start() {
	uint seed = spray_pcg(NUMBER ^ spray_pcg(RANDOM_SEED + uint(INDEX) * 7919u));
	// Denser near the camera (radius uniform, not area): spray far away is too small to see.
	float r = spray_radius * spray_rand(seed);
	float a = 6.2831853 * spray_rand(seed);
	vec2 xz = camera_xz + r * vec2(cos(a), sin(a));

	ShoreSample sea = shore_at(xz);
	vec3 calm = shore_calm(sea);
	vec3 d = displacement_at(xz, calm);

	vec2 fuv = (xz - foam_near_rect.xy) * foam_near_rect.w;
	float fresh = all(greaterThanEqual(fuv, vec2(0.0))) && all(lessThanEqual(fuv, vec2(1.0))) ? textureLod(foam_near, fuv, 0.0).g : 0.0; // already calmed over the shallows
	float crest = smoothstep(spray_threshold, spray_threshold + 0.4, fresh) * step(0.0, d.y);
	float wet = step(0.0, sea.depth);
	float breaking = smoothstep(0.15, 0.6, breaking_ratio * 2.0 * max(d.y, 0.0) - sea.depth) * wet * sea.open * spray_shore;
	float p = clamp(max(crest, breaking), 0.0, 1.0) * wet;

	if (spray_rand(seed) >= p || spray_opacity <= 0.0) {
		ACTIVE = false;
	} else {
		vec3 pos = vec3(xz.x + d.x, sea_level + d.y + 0.2, xz.y + d.z);
		pos.xz += (vec2(spray_rand(seed), spray_rand(seed)) - 0.5) * 1.5;
		TRANSFORM = mat4(vec4(1.0, 0.0, 0.0, 0.0), vec4(0.0, 1.0, 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(pos, 1.0));
		float lift = spray_speed * mix(0.35, 1.0, spray_rand(seed)) * mix(0.6, 1.0, p);
		vec2 side = (vec2(spray_rand(seed), spray_rand(seed)) - 0.5) * 2.0;
		VELOCITY = vec3(wind_dir.x * spray_wind * mix(0.5, 1.2, spray_rand(seed)) + side.x,
			lift, wind_dir.y * spray_wind * mix(0.5, 1.2, spray_rand(seed)) + side.y);
		CUSTOM = vec4(0.0, mix(0.6, 1.4, spray_rand(seed)), 6.2831853 * spray_rand(seed), p);
		COLOR = vec4(1.0, 1.0, 1.0, 0.0);
	}
}

void process() {
	float age = clamp(CUSTOM.x + DELTA / max(LIFETIME, 0.01), 0.0, 1.0);
	CUSTOM.x = age;
	VELOCITY.y -= 9.81 * spray_gravity * DELTA;
	VELOCITY.xz *= exp(-0.6 * DELTA);
	float size = spray_size * CUSTOM.y * mix(0.5, 2.2, sqrt(age));
	TRANSFORM[0].xyz = vec3(size, 0.0, 0.0);
	TRANSFORM[1].xyz = vec3(0.0, size, 0.0);
	TRANSFORM[2].xyz = vec3(0.0, 0.0, size);
	// Quick to appear, then thinning out from the start: spray is a burst, not a cloud that lingers.
	float fade = smoothstep(0.0, 0.05, age) * pow(1.0 - age, 1.6);
	COLOR = vec4(1.0, 1.0, 1.0, spray_opacity * fade * mix(0.5, 1.0, CUSTOM.w));
}
