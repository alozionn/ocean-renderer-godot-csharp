shader_type spatial;
render_mode world_vertex_coords, blend_mix, depth_draw_always, cull_back, shadows_disabled;

#include "include/sampling.gdshaderinc"
#include "include/lighting.gdshaderinc"

// Set by C# (SurfaceUniforms.cs).
uniform sampler2D displacement_0 : filter_linear_mipmap, repeat_enable;
uniform sampler2D displacement_1 : filter_linear_mipmap, repeat_enable;
uniform sampler2D displacement_2 : filter_linear_mipmap, repeat_enable;
uniform sampler2D slope_0 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D slope_1 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D slope_2 : filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 patch_sizes = vec3(400.0, 73.0, 13.7);
uniform vec3 texel_sizes = vec3(0.78125, 0.142578, 0.026758);
uniform float mesh_spacing = 0.25; // RingLayout: inner vertex spacing (m)
uniform float mesh_growth = 0.0281; // RingLayout.Growth(): outer spacing per metre of Chebyshev distance
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D foam_near : filter_linear, repeat_disable;
uniform sampler2D foam_far : filter_linear, repeat_disable;
uniform vec4 foam_near_rect = vec4(-128.0, -128.0, 256.0, 0.00390625);
uniform vec4 foam_far_rect = vec4(-1024.0, -1024.0, 2048.0, 0.00048828);
uniform sampler2D foam_detail : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D foam_detail_soft : filter_linear_mipmap, repeat_enable; // the same texture, for the blurred tail
uniform float foam_streak_angle = 0.349;

group_uniforms geometry;
uniform float fade_swell : hint_range(100.0, 8000.0, 10.0) = 3000.0;
uniform float fade_chop : hint_range(50.0, 4000.0, 10.0) = 800.0;
uniform float fade_ripples : hint_range(10.0, 1000.0, 5.0) = 200.0;
// Displacement mip = log2(vertex spacing / max(texel, inner spacing)) + bias. Without it the outer rings alias and swim as the camera moves.
uniform float displacement_lod_bias : hint_range(-2.0, 2.0, 0.05) = 0.0;

group_uniforms colour;
uniform vec3 shallow_color : source_color = vec3(0.12, 0.2, 0.24);
uniform vec3 deep_color : source_color = vec3(0.03, 0.05, 0.07);
uniform vec3 body_tint : source_color = vec3(1.0);
uniform float depth_scale : hint_range(0.5, 50.0, 0.1) = 6.0;
uniform vec3 height_tint : source_color = vec3(0.1, 0.22, 0.25);
uniform float height_tint_strength : hint_range(0.0, 1.0, 0.01) = 0.1;
uniform vec3 water_transmittance : source_color = vec3(0.35, 0.75, 0.8);
uniform float absorption_distance : hint_range(0.1, 40.0, 0.1) = 10.0;
uniform float refraction_strength : hint_range(0.0, 0.5, 0.005) = 0.04;
// Sea-floor water depth (m) over which refraction grows to full strength. Thin water bends little; a full
// offset there lands on sea bed nearer than the surface, gets rejected, and the switch shows as a jagged line.
uniform float refraction_depth : hint_range(0.1, 20.0, 0.1) = 4.0;

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
uniform float foam_age_softness : hint_range(0.01, 1.0, 0.01) = 0.35;
uniform float foam_age_start : hint_range(0.0, 0.99, 0.01) = 0.6;
uniform float foam_tail_blur : hint_range(0.0, 4.0, 0.05) = 2.0;
uniform float foam_tail_opacity : hint_range(0.0, 1.0, 0.01) = 0.6;
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
uniform float foam_fade_range : hint_range(10.0, 3000.0, 5.0) = 400.0;
uniform float foam_roughness : hint_range(0.0, 1.0, 0.01) = 0.8;
uniform float foam_flatten : hint_range(0.0, 1.0, 0.01) = 0.6;
uniform float foam_relief : hint_range(0.0, 2.0, 0.01) = 0.3;
uniform float foam_wrap : hint_range(0.0, 1.0, 0.01) = 0.35;
uniform vec3 milk_color : source_color = vec3(0.5, 0.55, 0.56);
uniform float milk_strength : hint_range(0.0, 1.0, 0.01) = 0.15;
uniform float milk_range : hint_range(0.05, 5.0, 0.05) = 1.5;
uniform float shore_fade_distance : hint_range(0.01, 3.0, 0.01) = 0.3;

// Only active with a sea floor (Ocean.SetSeaFloor). Depths in metres of water, distances in metres from the shore.
group_uniforms shore;
uniform float shallow_swell_depth : hint_range(0.5, 60.0, 0.5) = 6.0;
uniform float shallow_chop_depth : hint_range(0.1, 20.0, 0.1) = 5.0;
uniform float shallow_ripple_depth : hint_range(0.05, 5.0, 0.05) = 0.6;
uniform float lake_waves : hint_range(0.0, 1.0, 0.01) = 0.15;
// Waves keep at least shore_waves of their strength up to the waterline and shore_waves_land metres inland, so the real
// waterline rises and falls against beaches and rocks. The terrain hides the water under it.
uniform float shore_waves : hint_range(0.0, 1.0, 0.01) = 0.5;
uniform float shore_waves_land : hint_range(0.0, 30.0, 0.5) = 6.0;
// Edge foam: white where the water is less than edge_foam_width metres above anything solid behind it (sand, rock,
// a hull: whatever is in the depth buffer), measured vertically so it keeps its width at any view angle. It follows
// the moving waterline and is textured by the foam lace.
uniform float edge_foam_width : hint_range(0.0, 5.0, 0.05) = 0.8;
uniform float edge_foam_strength : hint_range(0.0, 2.0, 0.01) = 0.9;
// Edge foam on the terrain's own shoreline (0-1). Off by default: there the breaking foam already reaches the beach, and a
// thin line at the very edge, apart from it, looked wrong. Objects in deeper water (rocks, piles, hulls) always get it.
uniform float edge_foam_terrain : hint_range(0.0, 1.0, 0.01) = 0.0;
// Breaking: water shallower than breaking_ratio x the wave height injects foam into the foam field. Lower: a shorter band.
uniform float breaking_ratio : hint_range(0.0, 3.0, 0.01) = 0.9;
uniform float breaking_strength : hint_range(0.0, 2.0, 0.01) = 0.6;
// Wash: foam in the thin film at the moving water's edge on beaches (water under wash_depth metres, counting the waves),
// half as strong in the troughs, full while the water runs up. It joins the breaking foam to the edge and follows it up the sand and back.
uniform float wash_depth : hint_range(0.0, 4.0, 0.01) = 1.6;
uniform float wash_strength : hint_range(0.0, 2.0, 0.01) = 1.0;
uniform vec3 lagoon_color : source_color = vec3(0.04, 0.55, 0.55);
uniform float lagoon_depth : hint_range(0.1, 30.0, 0.1) = 6.0;
uniform float lagoon_strength : hint_range(0.0, 1.0, 0.01) = 0.6;
uniform vec3 sediment_color : source_color = vec3(0.52, 0.47, 0.36);
uniform float sediment_strength : hint_range(0.0, 1.0, 0.01) = 0.12;
uniform float sediment_depth : hint_range(0.1, 20.0, 0.1) = 3.0;
group_uniforms;

#include "include/foam_composite.gdshaderinc"
#include "include/shore.gdshaderinc"

varying vec2 v_grid; // undisplaced world xz: the simulation's texture coordinate, so foam rides with the water
varying float v_height;
varying float v_foam;
varying float v_white;
varying float v_bubbles;

// Summed cascade displacement at an undisplaced grid position, each cascade faded out with distance and filtered to
// the local vertex spacing (the mesh can only show wavelengths of at least two vertices).
vec3 displacement_at(vec2 grid, float dist, float spacing, vec3 calm) {
	vec3 far = vec3(fade_swell, fade_chop, fade_ripples);
	vec3 fade = (1.0 - smoothstep(0.25 * far, far, vec3(dist))) * calm;
	// Inner rings (spacing == mesh_spacing) keep mip 0: they snap to their own spacing, so they never swim.
	vec3 lod = max(log2(spacing / max(texel_sizes, vec3(mesh_spacing))) + displacement_lod_bias, vec3(0.0));
	return textureLod(displacement_0, grid / patch_sizes.x, lod.x).xyz * fade.x
		+ textureLod(displacement_1, grid / patch_sizes.y, lod.y).xyz * fade.y
		+ textureLod(displacement_2, grid / patch_sizes.z, lod.z).xyz * fade.z;
}

void vertex() {
	v_grid = VERTEX.xz;
	vec2 local = VERTEX.xz - MODEL_MATRIX[3].xz; // offset from the (snapped) mesh centre
	float spacing = max(mesh_spacing, max(abs(local.x), abs(local.y)) * mesh_growth);
	ShoreSample sea = shore_at(v_grid);
	vec3 calm = shore_calm(sea);
	vec3 d = displacement_at(v_grid, length(VERTEX.xz - CAMERA_POSITION_WORLD.xz), spacing, calm);
	VERTEX += d;
	v_height = d.y;
}

void fragment() {
	// Screen gradients of the grid before any branching (foam detail sampling needs them).
	vec2 grid_dx = dFdx(v_grid);
	vec2 grid_dy = dFdy(v_grid);
	float dist = length(VERTEX);

	// Normals: bicubic up close, filtered beyond; strengths calm with distance.
	float b = 1.0 - smoothstep(0.7 * bicubic_distance, bicubic_distance, dist);
	vec3 size = patch_sizes / texel_sizes;
	vec4 s0 = sample_slope(slope_0, v_grid / patch_sizes.x, size.x, b);
	vec4 s1 = sample_slope(slope_1, v_grid / patch_sizes.y, size.y, b);
	vec4 s2 = sample_slope(slope_2, v_grid / patch_sizes.z, size.z, b);
	ShoreSample sea = shore_at(v_grid);
	vec3 wave_calm = shore_calm(sea);
	vec3 strength = vec3(normal_swell, normal_chop, normal_ripples) * wave_calm;
	float calm = mix(normal_fade_floor, 1.0, exp(-dist / normal_fade_range));
	vec3 calmed = strength * calm * vec3(1.0,
		1.0 - smoothstep(0.25 * chop_fade, chop_fade, dist),
		1.0 - smoothstep(0.25 * ripple_fade, ripple_fade, dist));
	vec2 slope = s0.xy * calmed.x + s1.xy * calmed.y + s2.xy * calmed.z;
	vec3 normal = normalize(vec3(-slope.x, 1.0, -slope.y));
	// LEAN variance with un-calmed strengths, only beyond the bicubic range (up close it makes dark blotches).
	float variance = (1.0 - b) * variance_roughness * dot(strength * strength,
		vec3(slope_variance(s0), slope_variance(s1), slope_variance(s2)));

	// Water thickness behind this pixel along the refracted ray (screen-space offset), rejecting samples in front.
	// Shore depth is the distance along the view ray, so crests seen against the sky stay opaque.
	float raw_depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec3 straight = view_position(SCREEN_UV, raw_depth, INV_PROJECTION_MATRIX);
	float shore_depth = distance(straight, VERTEX);
	// How far the water is above the solid thing behind it, vertically (m). Only for something solid and near: the sky is
	// on the far plane (reverse-Z depth 0), and a far island behind a crest must not count as contact.
	float above = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).y - (INV_VIEW_MATRIX * vec4(straight, 1.0)).y;
	float solid = step(1e-6, raw_depth) * (1.0 - smoothstep(15.0, 30.0, shore_depth));
	// Refraction bends by the swell and chop only: the ripples (2.7 cm texels) shattered the edge of anything seen under
	// the surface into stair steps.
	vec2 refract_slope = s0.xy * calmed.x + s1.xy * calmed.y;
	vec3 view_normal = (VIEW_MATRIX * vec4(normalize(vec3(-refract_slope.x, 1.0, -refract_slope.y)), 0.0)).xyz;
	// The offset fades out near the screen edges, so the sample never leaves the screen (it showed as a dark bar).
	vec2 edge = smoothstep(vec2(0.0), vec2(0.06), min(SCREEN_UV, 1.0 - SCREEN_UV));
	// Scaled by the sea floor's depth (smooth, also around bodies), not the per-pixel depth behind: that jumps at every
	// silhouette under water (a rock's flank in front of the sea bed), and the bend jumped with it into stair steps.
	vec2 refracted_uv = SCREEN_UV + view_normal.xy * refraction_strength * smoothstep(0.0, refraction_depth, sea.depth) * edge;
	vec3 behind = view_position(refracted_uv, textureLod(depth_tex, refracted_uv, 0.0).r, INV_PROJECTION_MATRIX);
	// A sample in front of the surface (or only just behind it) falls back towards the unbent one, smoothly: a hard
	// per-pixel switch drew stair steps along everything that crosses the waterline.
	float keep = smoothstep(0.0, 0.5, VERTEX.z - behind.z);
	refracted_uv = mix(SCREEN_UV, refracted_uv, keep);
	behind = mix(straight, behind, keep);
	float thickness = distance(behind, VERTEX);

	// Edge foam where the water meets anything solid, from the depth buffer: exact against what is drawn, nothing to
	// register, and it moves with the waterline as the waves rise and fall. Breaking in the shallows comes from the foam
	// field, which also calms its open-sea foam there.
	// Only for something solid under the water (a crest in front of a rock face above it is not contact). Something solid
	// where the terrain says the water is deep is an object; where the terrain itself is that close, it is the beach.
	float object = mix(edge_foam_terrain, 1.0, smoothstep(1.5, 4.0, sea.wave_depth - max(above, 0.0)));
	float edge_foam = (1.0 - smoothstep(0.0, edge_foam_width, max(above, 0.0))) * step(-0.2, above) * solid * object * edge_foam_strength;
	FoamSample foam = foam_composite(v_grid, grid_dx, grid_dy, dist, edge_foam, b > 0.0);
	float milk = smoothstep(0.0, milk_range, foam.density) * milk_strength;

	// Foam flattens the wave normal (but still follows it) and adds the lace's own relief.
	normal = normalize(mix(normal, vec3(0.0, 1.0, 0.0), foam.coverage * foam_flatten));
	vec2 relief = foam.relief * foam_relief * b;
	normal = normalize(normal - vec3(relief.x, 0.0, relief.y) * foam.coverage);
	vec3 view_world = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
	normal = horizon_clamp(normal, view_world, horizon_min);
	NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);

	// Colour: the body stays dark; sky reflection, fog and sun do the colouring.
	vec3 scatter = mix(shallow_color, deep_color, 1.0 - exp(-thickness / depth_scale)) * body_tint;
	scatter = mix(scatter, height_tint, smoothstep(0.0, 2.5, v_height) * height_tint_strength);
	scatter = mix(scatter, milk_color, milk);
	// With a sea floor: turquoise over shallow ground (lagoon), sand stirred up where the open sea is shallow (sediment).
	// Terrain depth only: a body's sloping foot must not paint a sand ring around it.
	float lagoon = lagoon_strength * exp(-max(sea.wave_depth, 0.0) / lagoon_depth);
	// No cut at the coastline (depth 0 or "open sea"): those follow the 4 m terrain cells and drew stairs. Over land the
	// water is hidden anyway.
	float sediment = sediment_strength * (1.0 - smoothstep(0.0, sediment_depth, sea.wave_depth));
	scatter = mix(mix(scatter, lagoon_color, lagoon), sediment_color, sediment);
	vec3 transmittance = pow(water_transmittance, vec3(thickness / absorption_distance)) * (1.0 - milk) * (1.0 - 0.6 * sediment);
	vec3 lagoon_tint = mix(vec3(1.0), lagoon_color * 2.0, lagoon);

	float r2 = roughness * roughness;
	float alpha = sqrt(r2 * r2 + variance);
	float fresnel = fresnel_rough(clamp(dot(NORMAL, VIEW), 0.0, 1.0), alpha);
	vec3 refracted = textureLod(screen_tex, refracted_uv, 0.0).rgb;
	// Foam takes light: a little darker in the troughs, lit in light() with wrapped diffuse.
	vec3 foam_albedo = foam_color * mix(0.85, 1.0, clamp(v_height / 1.5 + 0.5, 0.0, 1.0));
	ALBEDO = mix(scatter * (1.0 - transmittance) * (1.0 - fresnel), foam_albedo, foam.coverage);
	EMISSION = refracted * transmittance * lagoon_tint * (1.0 - fresnel) * (1.0 - foam.coverage);
	ROUGHNESS = mix(sqrt(alpha), foam_roughness, foam.coverage);
	SPECULAR = 0.354 * (1.0 - smoothstep(0.0, 0.4, foam.coverage)); // f0 = 0.16 * 0.354^2 = 0.02
	// Soft edge against anything solid: along the view ray (crests seen against the sky stay opaque), and vertically
	// where something solid is behind, so a gentle beach gets a wide soft waterline instead of the mesh's sawtooth.
	ALPHA = smoothstep(0.0, shore_fade_distance, shore_depth) * mix(1.0, smoothstep(0.0, shore_fade_distance, above), solid * step(0.0, above));
	v_foam = foam.coverage;
	v_white = foam.white;
	v_bubbles = max(milk, foam.coverage);
}

void light() {
	vec3 half_vector = normalize(VIEW + LIGHT);
	float n_dot_l_raw = dot(NORMAL, LIGHT);
	float n_dot_l = clamp(n_dot_l_raw, 0.0, 1.0);
	float n_dot_v = max(dot(NORMAL, VIEW), 1e-3);
	float n_dot_h = clamp(dot(NORMAL, half_vector), 0.0, 1.0);
	vec3 radiance = LIGHT_COLOR * ATTENUATION / PI_W;

	// GGX with height-correlated Smith masking and the rough-surface Fresnel; none under foam.
	float alpha = ROUGHNESS * ROUGHNESS;
	float lambda_l = smith_lambda(max(n_dot_l, 1e-3), alpha);
	float masking = 1.0 / (1.0 + smith_lambda(n_dot_v, alpha) + lambda_l);
	float fresnel = fresnel_rough(n_dot_v, alpha);
	SPECULAR_LIGHT += radiance * ggx_d(n_dot_h, alpha) * masking * fresnel / (4.0 * n_dot_v) * specular_strength * (1.0 - v_foam);

	// Water: Lambert. Foam: wrapped Lambert, so it stays soft and lit on slopes turned slightly away.
	float wrapped = clamp((n_dot_l_raw + foam_wrap) / (1.0 + foam_wrap), 0.0, 1.0);
	DIFFUSE_LIGHT += radiance * mix(n_dot_l, wrapped, v_foam);

	// Atlas scattering. k1: sun through tall crests; k2: body seen face-on; k3: body facing the sun; k4: light
	// bouncing around bubbles in milky water and foam. Thin foam (veil, not lace) lets a little through as well.
	float k1 = sss_crest * max(v_height, 0.0) * 0.6 * pow(clamp(dot(LIGHT, -VIEW), 0.0, 1.0), 4.0)
		* pow(clamp(0.5 - 0.5 * dot(LIGHT, NORMAL), 0.0, 1.0), 3.0);
	float k2 = sss_view * pow(clamp(dot(VIEW, NORMAL), 0.0, 1.0), 2.0);
	float k3 = sss_sun * n_dot_l;
	vec3 body = ((k1 + k2) / (1.0 + lambda_l) + k3) * sss_color * (1.0 - v_foam);
	vec3 bubbles = sss_bubbles * v_bubbles * bubble_color;
	SPECULAR_LIGHT += radiance * ((body + bubbles) * (1.0 - fresnel) + sss_color * (1.0 - v_white) * v_foam);
}
