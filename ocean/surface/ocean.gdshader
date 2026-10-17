shader_type spatial;
render_mode world_vertex_coords, blend_mix, depth_draw_always, cull_back, shadows_disabled;

uniform sampler2D displacement_0 : filter_linear, repeat_enable;
uniform sampler2D displacement_1 : filter_linear, repeat_enable;
uniform sampler2D displacement_2 : filter_linear, repeat_enable;
uniform sampler2D slope_0 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D slope_1 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D slope_2 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 patch_sizes = vec3(400.0, 73.0, 13.7);
uniform vec3 texel_sizes = vec3(0.78, 0.14, 0.027);

varying vec2 v_grid;

void vertex() {
	v_grid = VERTEX.xz;
	vec3 d = texture(displacement_0, v_grid / patch_sizes.x).xyz
		+ texture(displacement_1, v_grid / patch_sizes.y).xyz
		+ texture(displacement_2, v_grid / patch_sizes.z).xyz;
	VERTEX += d;
}

void fragment() {
	vec2 s = texture(slope_0, v_grid / patch_sizes.x).xy + texture(slope_1, v_grid / patch_sizes.y).xy
		+ texture(slope_2, v_grid / patch_sizes.z).xy;
	vec3 n = normalize(vec3(-s.x, 1.0, -s.y));
	NORMAL = (VIEW_MATRIX * vec4(n, 0.0)).xyz;
	ALBEDO = vec3(0.03, 0.05, 0.07);
	ROUGHNESS = 0.1;
}
