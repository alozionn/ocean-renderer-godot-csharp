shader_type spatial;
render_mode world_vertex_coords, blend_mix, depth_draw_always, cull_back, shadows_disabled;

#include "include/sampling.gdshaderinc"
#include "include/lighting.gdshaderinc"

// Set by C# (SurfaceUniforms.cs).
uniform sampler2D displacement_0 : filter_linear, repeat_enable;
uniform sampler2D displacement_1 : filter_linear, repeat_enable;
uniform sampler2D displacement_2 : filter_linear, repeat_enable;
uniform sampler2D slope_0 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D slope_1 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D slope_2 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 patch_sizes = vec3(400.0, 73.0, 13.7);
uniform vec3 texel_sizes = vec3(0.78125, 0.142578, 0.026758);
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D foam_near : filter_linear, repeat_disable;
uniform sampler2D foam_far : filter_linear, repeat_disable;
uniform vec4 foam_near_rect = vec4(-128.0, -128.0, 256.0, 0.00390625);
uniform vec4 foam_far_rect = vec4(-1024.0, -1024.0, 2048.0, 0.00048828);
uniform sampler2D foam_detail : filter_linear_mipmap, repeat_enable;
uniform float foam_streak_angle = 0.349;

group_uniforms geometry;
uniform float fade_swell : hint_range(100.0, 8000.0, 10.0) = 3000.0;
uniform float fade_chop : hint_range(50.0, 4000.0, 10.0) = 800.0;
uniform float fade_ripples : hint_range(10.0, 1000.0, 5.0) = 200.0;

group_uniforms colour;
uniform vec3 shallow_color : source_color = vec3(0.12, 0.2, 0.24);
uniform vec3 deep_color : source_color = vec3(0.03, 0.05, 0.07);
uniform vec3 body_tint : source_color = vec3(1.0);
uniform float depth_scale : hint_range(0.5, 50.0, 0.1) = 6.0;
uniform vec3 height_tint : source_color = vec3(0.1, 0.22, 0.25);
uniform float height_tint_strength : hint_range(0.0, 1.0, 0.01) = 0.1;
uniform vec3 water_transmittance : source_color = vec3(0.35, 0.75, 0.8);
uniform float absorption_distance : hint_range(0.1, 40.0, 0.1) = 4.0;
uniform float refraction_strength : hint_range(0.0, 0.5, 0.005) = 0.04;

group_uniforms surface;
uniform float normal_swell : hint_range(0.0, 3.0, 0.01) = 1.0;
uniform float normal_chop : hint_range(0.0, 3.0, 0.01) = 0.8;
uniform float normal_ripples : hint_range(0.0, 3.0, 0.01) = 0.45;
uniform float roughness : hint_range(0.01, 1.0, 0.01) = 0.12;
uniform float variance_roughness : hint_range(0.0, 4.0, 0.01) = 1.0;
uniform float specular_strength : hint_range(0.0, 4.0, 0.01) = 1.0;
uniform float horizon_min : hint_range(0.0, 0.3, 0.005) = 0.03;

group_uniforms distance;
uniform float bicubic_distance : hint_range(0.0, 300.0, 1.0) = 60.0;
uniform float normal_fade_floor : hint_range(0.0, 1.0, 0.01) = 0.12;
uniform float normal_fade_range : hint_range(10.0, 3000.0, 5.0) = 220.0;
uniform float chop_fade : hint_range(50.0, 4000.0, 10.0) = 600.0;
uniform float ripple_fade : hint_range(10.0, 1000.0, 5.0) = 120.0;

group_uniforms scattering;
uniform vec3 sss_color : source_color = vec3(0.08, 0.22, 0.24);
uniform float sss_crest : hint_range(0.0, 10.0, 0.01) = 2.0;
uniform float sss_view : hint_range(0.0, 4.0, 0.01) = 0.4;
uniform float sss_sun : hint_range(0.0, 2.0, 0.01) = 0.1;
uniform float sss_bubbles : hint_range(0.0, 2.0, 0.01) = 0.35;
uniform vec3 bubble_color : source_color = vec3(0.6, 0.66, 0.66);

group_uniforms foam;
uniform vec3 foam_color : source_color = vec3(0.82, 0.84, 0.84);
uniform float foam_trail_low : hint_range(0.0, 1.0, 0.005) = 0.03;
uniform float foam_trail_high : hint_range(0.0, 1.0, 0.005) = 0.45;
uniform float foam_cap_low : hint_range(0.0, 1.0, 0.005) = 0.1;
uniform float foam_cap_high : hint_range(0.0, 1.0, 0.005) = 0.45;
uniform float foam_detail_tile : hint_range(1.0, 100.0, 0.1) = 14.0;
uniform float foam_detail_stretch : hint_range(1.0, 6.0, 0.05) = 2.2;
uniform float foam_detail_blur : hint_range(0.0, 4.0, 0.05) = 1.2;
uniform float foam_detail_mix : hint_range(0.0, 1.0, 0.01) = 0.35;
uniform float foam_softness : hint_range(0.01, 1.0, 0.01) = 0.3;
uniform float foam_max_cover : hint_range(0.0, 1.0, 0.01) = 0.6;
uniform float foam_dense_shade : hint_range(0.0, 1.0, 0.01) = 0.45;
uniform float foam_trail_opacity : hint_range(0.0, 1.0, 0.01) = 0.7;
uniform float foam_veil : hint_range(0.0, 1.0, 0.01) = 0.25;
uniform float foam_hex_rotation : hint_range(0.0, 180.0, 1.0) = 180.0;
uniform float foam_hex_density : hint_range(0.25, 4.0, 0.05) = 1.0;
uniform float foam_hex_contrast : hint_range(1.0, 8.0, 0.1) = 3.0;
uniform float foam_far_opacity : hint_range(0.0, 1.0, 0.01) = 0.35;
uniform float foam_lod_start : hint_range(0.0, 1000.0, 5.0) = 120.0;
uniform float foam_lod_end : hint_range(0.0, 2000.0, 5.0) = 220.0;
uniform float foam_fade_range : hint_range(10.0, 3000.0, 5.0) = 220.0;
uniform float foam_roughness : hint_range(0.0, 1.0, 0.01) = 0.8;
uniform float foam_flatten : hint_range(0.0, 1.0, 0.01) = 0.6;
uniform float foam_relief : hint_range(0.0, 2.0, 0.01) = 0.3;
uniform float foam_wrap : hint_range(0.0, 1.0, 0.01) = 0.35;
uniform vec3 milk_color : source_color = vec3(0.5, 0.55, 0.56);
uniform float milk_strength : hint_range(0.0, 1.0, 0.01) = 0.15;
uniform float milk_range : hint_range(0.05, 5.0, 0.05) = 1.5;
uniform float shore_foam_distance : hint_range(0.0, 5.0, 0.05) = 0.8;
uniform float shore_fade_distance : hint_range(0.01, 3.0, 0.01) = 0.3;
group_uniforms;

#include "include/foam_composite.gdshaderinc"

varying vec2 v_grid;
varying float v_height;
varying float v_alpha;
varying float v_bubbles;
varying float v_foam;
varying float v_white;
varying vec3 v_foam_diffuse;

// Summed cascade displacement at an undisplaced grid position, each cascade faded out with distance.
vec3 displacement_at(vec2 grid, float dist) {
	vec3 far = vec3(fade_swell, fade_chop, fade_ripples);
	vec3 fade = 1.0 - smoothstep(0.25 * far, far, vec3(dist));
	return textureLod(displacement_0, grid / patch_sizes.x, 0.0).xyz * fade.x
		+ textureLod(displacement_1, grid / patch_sizes.y, 0.0).xyz * fade.y
		+ textureLod(displacement_2, grid / patch_sizes.z, 0.0).xyz * fade.z;
}

void vertex() {
	v_grid = VERTEX.xz;
	vec3 d = displacement_at(v_grid, length(VERTEX.xz - CAMERA_POSITION_WORLD.xz));
	VERTEX += d;
	v_height = d.y;
}

void fragment() {
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 grid = v_grid;
	// Screen gradients of the grid before any branching (foam detail sampling needs them).
	vec2 grid_dx = dFdx(grid);
	vec2 grid_dy = dFdy(grid);
	vec3 to_camera = CAMERA_POSITION_WORLD - world;
	float dist = length(to_camera);
	vec3 v = to_camera / dist;

	// Depth: water thickness along the view ray and the shore depth below the surface.
	float surface_depth = -VERTEX.z;
	float raw_depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec3 floor_view = view_position(SCREEN_UV, raw_depth, INV_PROJECTION_MATRIX);
	float raw = -floor_view.z;
	float thickness0 = min(max(raw - surface_depth, 0.0), 1e4);
	float shore_depth = raw > 1e4 ? 1e4 : max(world.y - (INV_VIEW_MATRIX * vec4(floor_view, 1.0)).y, 0.0);
	float shore = 1.0 - smoothstep(0.0, shore_foam_distance, shore_depth);

	// Normals: bicubic up close, filtered beyond; strengths calm with distance.
	float b = 1.0 - smoothstep(0.7 * bicubic_distance, bicubic_distance, dist);
	vec3 size = patch_sizes / texel_sizes;
	vec4 s0 = sample_slope(slope_0, grid / patch_sizes.x, size.x, b);
	vec4 s1 = sample_slope(slope_1, grid / patch_sizes.y, size.y, b);
	vec4 s2 = sample_slope(slope_2, grid / patch_sizes.z, size.z, b);
	float calm = mix(normal_fade_floor, 1.0, exp(-dist / normal_fade_range));
	vec3 strength = vec3(normal_swell, normal_chop, normal_ripples);
	vec3 calmed = strength * calm * vec3(1.0,
		1.0 - smoothstep(0.25 * chop_fade, chop_fade, dist),
		1.0 - smoothstep(0.25 * ripple_fade, ripple_fade, dist));
	vec2 slope = s0.xy * calmed.x + s1.xy * calmed.y + s2.xy * calmed.z;

	// Foam: flatten the water normal under it, then tilt by the lace relief.
	FoamSample foam = foam_composite(grid, grid_dx, grid_dy, dist, shore, b > 0.0);
	slope = slope * (1.0 - foam.coverage * foam_flatten) + foam.relief * foam_relief * b;
	vec3 n = normalize(vec3(-slope.x, 1.0, -slope.y));
	n = horizon_clamp(n, v, horizon_min);

	// LEAN variance with un-calmed strengths, only beyond the bicubic range.
	float variance = (1.0 - b) * variance_roughness * dot(strength * strength,
		vec3(slope_variance(s0), slope_variance(s1), slope_variance(s2)));
	float r2 = roughness * roughness;
	v_alpha = sqrt(r2 * r2 + variance);
	v_foam = foam.coverage;
	v_white = foam.white;
	v_bubbles = smoothstep(0.0, 0.5, foam.fresh) * (1.0 - foam.coverage);

	// Refraction: offset in view space, rejected when the sample lies in front of the surface.
	vec3 n_view = (VIEW_MATRIX * vec4(n, 0.0)).xyz;
	vec3 refr_view = VERTEX + vec3(n_view.xy, 0.0) * refraction_strength * min(thickness0, 10.0);
	vec4 clip = PROJECTION_MATRIX * vec4(refr_view, 1.0);
	vec2 refr_uv = clamp(clip.xy / clip.w * 0.5 + 0.5, vec2(0.0), vec2(1.0));
	float refr_depth = -view_position(refr_uv, textureLod(depth_tex, refr_uv, 0.0).r, INV_PROJECTION_MATRIX).z;
	if (refr_depth < surface_depth) {
		refr_uv = SCREEN_UV;
		refr_depth = raw;
	}
	float thickness = min(max(refr_depth - surface_depth, 0.0), 1e4);
	vec3 refracted = textureLod(screen_tex, refr_uv, 0.0).rgb;

	// Colour: the body stays dark; sky reflection, fog and sun do the colouring.
	vec3 scatter = mix(shallow_color, deep_color, 1.0 - exp(-thickness / depth_scale)) * body_tint;
	scatter = mix(scatter, height_tint, smoothstep(0.0, 2.5, v_height) * height_tint_strength);
	scatter = mix(scatter, milk_color, smoothstep(0.0, milk_range, foam.density) * milk_strength);
	vec3 transmittance = pow(water_transmittance, vec3(thickness / absorption_distance));
	float fresnel = fresnel_rough(max(dot(n, v), 0.0), v_alpha);
	vec3 foam_albedo = foam_color * mix(0.85, 1.0, clamp(v_height / 1.5 + 0.5, 0.0, 1.0));

	NORMAL = n_view;
	ALBEDO = mix(scatter * (1.0 - transmittance) * (1.0 - fresnel), foam_albedo, foam.coverage);
	// The engine multiplies DIFFUSE_LIGHT by ALBEDO; this weight leaves exactly coverage * foam_albedo lit.
	v_foam_diffuse = foam.coverage * foam_albedo / max(ALBEDO, vec3(1e-4));
	EMISSION = refracted * transmittance * (1.0 - fresnel) * (1.0 - foam.coverage);
	SPECULAR = 0.354 * (1.0 - smoothstep(0.0, 0.4, foam.coverage));
	ROUGHNESS = mix(sqrt(v_alpha), foam_roughness, foam.coverage);
	ALPHA = smoothstep(0.0, shore_fade_distance, shore_depth);
}

void light() {
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI_W;
	vec3 h = normalize(VIEW + LIGHT);
	float n_dot_l = max(dot(NORMAL, LIGHT), 0.0);
	float n_dot_v = max(dot(NORMAL, VIEW), 1e-4);
	float lambda_l = smith_lambda(n_dot_l, v_alpha);
	float g = 1.0 / (1.0 + smith_lambda(n_dot_v, v_alpha) + lambda_l);
	float f = fresnel_rough(max(dot(VIEW, h), 0.0), v_alpha);
	float spec = ggx_d(max(dot(NORMAL, h), 0.0), v_alpha) * g * f / (4.0 * n_dot_v);
	float water = 1.0 - v_foam;
	SPECULAR_LIGHT += spec * radiance * specular_strength * water;

	// Atlas subsurface scatter: crest towards the sun, view-facing, sun-facing, bubbles.
	float crest = max(v_height, 0.0) * 0.6;
	float k1 = sss_crest * crest * pow(clamp(dot(LIGHT, -VIEW), 0.0, 1.0), 4.0) * pow(clamp(0.5 - 0.5 * dot(LIGHT, NORMAL), 0.0, 1.0), 3.0);
	float k2 = sss_view * pow(clamp(dot(VIEW, NORMAL), 0.0, 1.0), 2.0);
	float k3 = sss_sun * n_dot_l;
	float fresnel_v = fresnel_rough(n_dot_v, v_alpha);
	SPECULAR_LIGHT += (((k1 + k2) / (1.0 + lambda_l) + k3) * sss_color + sss_bubbles * v_bubbles * bubble_color) * radiance * (1.0 - fresnel_v) * water;

	// Foam: wrapped Lambert (times ALBEDO by the engine), thin veil transmits.
	float wrap = max((dot(NORMAL, LIGHT) + foam_wrap) / (1.0 + foam_wrap), 0.0);
	DIFFUSE_LIGHT += wrap * radiance * v_foam_diffuse;
	SPECULAR_LIGHT += sss_color * (1.0 - v_white) * v_foam * radiance;
}
