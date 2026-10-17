shader_type spatial;

// A composition anchor: a low, faceted rock island with a flattish top and a wet band at the waterline.
uniform float noise_scale = 0.07;
uniform float amplitude = 9.0;
uniform float flat_top = 13.5;
uniform vec3 albedo : source_color = vec3(0.22, 0.2, 0.18);
uniform vec3 strata_color : source_color = vec3(0.34, 0.31, 0.27);
uniform float wet_band = 1.5;
uniform float facets : hint_range(0.0, 1.0) = 0.7;

varying vec3 v_world;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float value_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = x - i;
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	return value_noise(p) * 0.57 + value_noise(p * 2.03) * 0.29 + value_noise(p * 4.01) * 0.14;
}

void vertex() {
	VERTEX += NORMAL * (fbm(VERTEX * noise_scale) - 0.5) * 2.0 * amplitude;
	if (VERTEX.y > flat_top) VERTEX.y = flat_top + (VERTEX.y - flat_top) * 0.25;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 flat_n = normalize(cross(dFdy(VERTEX), dFdx(VERTEX)));
	NORMAL = normalize(mix(NORMAL, flat_n, facets));
	float strata = smoothstep(0.55, 0.7, value_noise(vec3(v_world.x * 0.05, v_world.y * 0.9, v_world.z * 0.05)));
	vec3 c = mix(albedo, strata_color, strata);
	float wet = 1.0 - smoothstep(0.0, wet_band, abs(v_world.y));
	ALBEDO = c * mix(1.0, 0.45, wet);
	ROUGHNESS = mix(0.9, 0.5, wet);
}
