shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, specular_disabled, diffuse_lambert_wrap;

// A spray puff: a camera-facing quad textured with the foam detail (wispy droplets), soft at the rim, lit by the sun
// with a strong back-light so spray glows against a low sun. Drawn after the water (render_priority 1).

uniform sampler2D foam_detail : filter_linear_mipmap, repeat_enable; // set by C#: the ocean's foam detail texture
uniform vec3 spray_color : source_color = vec3(0.92, 0.94, 0.95);
uniform float softness = 0.6;

varying vec2 v_offset;

void vertex() {
	// Each puff reads its own patch of the detail texture.
	v_offset = vec2(fract(INSTANCE_CUSTOM.z * 3.17), fract(INSTANCE_CUSTOM.y * 5.31));
	// Billboard, keeping the particle's scale; rotated by the random angle in INSTANCE_CUSTOM.z.
	float s = length(MODEL_MATRIX[0].xyz);
	float a = INSTANCE_CUSTOM.z;
	mat4 face = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	mat4 spin = mat4(vec4(cos(a), -sin(a), 0.0, 0.0), vec4(sin(a), cos(a), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * face * spin * mat4(vec4(s, 0.0, 0.0, 0.0), vec4(0.0, s, 0.0, 0.0), vec4(0.0, 0.0, s, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	// Soft falloff all the way from the centre, eroded by the lace: wisps and droplets, never a disc.
	float falloff = pow(clamp(1.0 - length(c), 0.0, 1.0), 1.0 + 2.0 * softness);
	float lace = texture(foam_detail, UV * 0.35 + v_offset).r;
	float a = falloff * smoothstep(0.25, 0.85, lace) * COLOR.a * 1.6;
	ALBEDO = spray_color;
	BACKLIGHT = spray_color * 0.6;
	ROUGHNESS = 1.0;
	ALPHA = clamp(a, 0.0, 1.0);
}
