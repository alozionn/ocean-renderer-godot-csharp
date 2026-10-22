shader_type spatial;

// Height colour bands and water caustics, ported from Mushrook's terrain.gdshader. Band edges are in band units;
// band_scale (metres per unit, set by IslandTerrain) maps world height onto them. The underwater bands are muddy sand
// on purpose: the water's own scatter colours them.
uniform float band_scale = 3.0;
uniform float roughness = 0.95;
uniform float sea_level = 0.0;

group_uniforms caustics;
uniform sampler2D caustic_texture : repeat_enable, filter_linear_mipmap; // set by IslandTerrain
uniform float caustic_scale = 0.05;     // texture repeats per metre
uniform float caustic_intensity = 3.0;
uniform float caustic_sharpness = 2.0;  // power on each layer: the texture is mid-grey, light should focus into thin lines
uniform float caustic_fade_in = 0.6;    // metres below sea level where they reach full strength
uniform float caustic_fade_depth = 12.0; // metres over which they fade out with depth
uniform float caustic_speed = 1.0;
group_uniforms;

const vec3 deep_water = vec3(0.35, 0.30, 0.20);
const vec3 water = vec3(0.55, 0.50, 0.35);
const vec3 sand = vec3(0.76, 0.70, 0.50);
const vec3 grass = vec3(0.3, 0.7, 0.2);
const vec3 hill = vec3(0.6, 0.5, 0.3);
const vec3 mountain = vec3(0.5, 0.5, 0.5);
const vec3 mountain_top = vec3(0.9, 0.9, 0.9);

varying vec3 v_world;

vec3 color_from_height(float height) {
	if (height <= -2.0) return deep_water;
	else if (height <= 0.0) return mix(deep_water, water, (height + 2.0) / 2.0);
	else if (height <= 1.0) return mix(water, sand, height);
	else if (height <= 3.0) return mix(sand, grass, (height - 1.0) / 2.0);
	else if (height <= 5.0) return mix(grass, hill, (height - 3.0) / 2.0);
	else if (height <= 8.0) return mix(hill, mountain, (height - 5.0) / 3.0);
	else return mix(mountain, mountain_top, clamp((height - 8.0) / 4.0, 0.0, 1.0));
}

vec2 rotate_uv(vec2 uv, float angle) {
	float s = sin(angle);
	float c = cos(angle);
	return vec2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Mushrook's colours read as display values; decode the land so it keeps its saturation under this scene's sun and
	// ACES. The seabed stays as authored (light sand): decoded it turns dark brown and the shallows read as deep water.
	float h = v_world.y / band_scale;
	vec3 band = color_from_height(h);
	vec3 albedo = mix(band, pow(band, vec3(2.2)), smoothstep(0.0, 1.0, h));

	// Two scrolling, rotated layers multiplied: only where bright cells cross does light focus.
	float depth = sea_level - v_world.y;
	float mask = smoothstep(0.0, caustic_fade_in, depth) * exp(-max(depth, 0.0) / caustic_fade_depth);
	if (mask > 0.001) {
		float t = TIME * caustic_speed;
		vec2 uv1 = v_world.xz * caustic_scale + t * vec2(0.02, 0.01);
		vec2 uv2 = rotate_uv(v_world.xz * caustic_scale * 1.2, 0.4) + t * vec2(-0.01, 0.03);
		float caustic = pow(texture(caustic_texture, uv1).r * texture(caustic_texture, uv2).r, caustic_sharpness) * caustic_intensity;
		albedo += vec3(caustic) * mask;
	}
	ALBEDO = albedo;
	ROUGHNESS = roughness;
}
