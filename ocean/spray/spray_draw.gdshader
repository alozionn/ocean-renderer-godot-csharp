shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_disabled, diffuse_lambert_wrap;

// A spray puff, all procedural: an irregular mist body that billows out as it ages, plus distinct droplets that are
// strongest at birth and gone by mid-life. Camera-facing, lit by the sun with a back-light so spray glows against a
// low sun. Drawn after the water (render_priority 1). INSTANCE_CUSTOM: x age 0..1, y size jitter, z angle, w strength.

uniform vec3 spray_color : source_color = vec3(0.92, 0.94, 0.95);
uniform float droplet_cells = 9.0;    // droplet grid across the puff
uniform float droplet_density = 0.4;  // share of grid cells holding a droplet

varying vec2 v_seed;
varying float v_age;

void vertex() {
	v_seed = vec2(fract(INSTANCE_CUSTOM.z * 3.17), fract(INSTANCE_CUSTOM.y * 5.31)) * 61.0;
	v_age = INSTANCE_CUSTOM.x;
	// Billboard, keeping the particle's scale; rotated by the random angle in INSTANCE_CUSTOM.z.
	float s = length(MODEL_MATRIX[0].xyz);
	float a = INSTANCE_CUSTOM.z;
	mat4 face = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	mat4 spin = mat4(vec4(cos(a), -sin(a), 0.0, 0.0), vec4(sin(a), cos(a), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * face * spin * mat4(vec4(s, 0.0, 0.0, 0.0), vec4(0.0, s, 0.0, 0.0), vec4(0.0, 0.0, s, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

vec2 hash22(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	q += dot(q, q.yzx + 33.33);
	return fract((q.xx + q.yz) * q.zy);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = p - i;
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash22(i).x, hash22(i + vec2(1.0, 0.0)).x, f.x), mix(hash22(i + vec2(0.0, 1.0)).x, hash22(i + vec2(1.0, 1.0)).x, f.x), f.y);
}

float fbm(vec2 p) {
	return noise(p) * 0.55 + noise(p * 2.1 + 7.3) * 0.3 + noise(p * 4.3 + 1.9) * 0.15;
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);

	// Mist: a ragged-edged body whose wisps drift as it ages.
	float edge = r + (fbm(c * 1.7 + v_seed) - 0.5) * 0.7;
	float body = 1.0 - smoothstep(0.2, 0.95, edge);
	float wisps = smoothstep(0.3, 0.75, fbm(c * 3.2 + v_seed.yx + v_age * 1.2));
	float mist = body * wisps * mix(0.85, 0.55, v_age);

	// Droplets: at most one per grid cell, mostly towards the centre, gone by mid-life.
	vec2 g = UV * droplet_cells;
	vec2 cell = floor(g);
	vec2 h = hash22(cell + v_seed * 1.37);
	vec2 h2 = hash22(cell.yx + v_seed * 2.11 + 17.0);
	float radius = mix(0.07, 0.16, h2.y);
	float drop = 1.0 - smoothstep(radius * 0.5, radius, length(fract(g) - (0.2 + 0.6 * h)));
	float droplets = drop * step(1.0 - droplet_density, h2.x) * (1.0 - smoothstep(0.35, 0.9, r)) * (1.0 - smoothstep(0.15, 0.55, v_age));

	ALBEDO = spray_color;
	BACKLIGHT = spray_color * 0.6;
	ROUGHNESS = 1.0;
	ALPHA = clamp(max(mist, droplets) * COLOR.a, 0.0, 1.0);
}
