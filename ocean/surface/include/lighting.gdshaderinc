// Water lighting: Atlas rough-surface Fresnel, height-correlated Smith GGX, Atlas subsurface scatter, horizon clamp.

const float PI_W = 3.14159265358979;
const float WATER_F0 = 0.02;

// Rough-surface Fresnel (Atlas, GDC 2019): drops the grazing peak as the surface gets rougher.
float fresnel_rough(float cos_theta, float alpha) {
	float c = clamp(1.0 - cos_theta, 0.0, 1.0);
	return WATER_F0 + (1.0 - WATER_F0) * pow(c, 5.0 * exp(-2.69 * alpha)) / (1.0 + 22.7 * pow(alpha, 1.5));
}

float smith_lambda(float cos_theta, float alpha) {
	float c2 = max(cos_theta * cos_theta, 1e-4);
	return (-1.0 + sqrt(1.0 + alpha * alpha * (1.0 / c2 - 1.0))) * 0.5;
}

float ggx_d(float n_dot_h, float alpha) {
	float a2 = alpha * alpha;
	float d = n_dot_h * n_dot_h * (a2 - 1.0) + 1.0;
	return a2 / (PI_W * d * d);
}

// No black facets: if the reflected view ray dips below min_y, return the normal that reflects it to the same azimuth
// at height min_y (the half vector of view and that direction). Continuous: at r.y == min_y it is n itself.
vec3 horizon_clamp(vec3 n, vec3 v, float min_y) {
	vec3 r = reflect(-v, n);
	if (r.y >= min_y) return n;
	vec2 flat_dir = r.xz / max(length(r.xz), 1e-5);
	float c = sqrt(1.0 - min_y * min_y);
	return normalize(v + vec3(flat_dir.x * c, min_y, flat_dir.y * c));
}
