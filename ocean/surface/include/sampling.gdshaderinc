// Cascade sampling: bicubic B-spline near the camera, trilinear-anisotropic beyond; slope variance for roughness.

// Cubic B-spline filtering from 4 bilinear taps (mip 0).
vec4 sample_bicubic(sampler2D tex, vec2 uv, float size) {
	vec2 t = uv * size - 0.5;
	vec2 i = floor(t);
	vec2 f = t - i;
	vec2 f2 = f * f;
	vec2 f3 = f2 * f;
	vec2 w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0;
	vec2 w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
	vec2 w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0;
	vec2 w3 = f3 / 6.0;
	vec2 g0 = w0 + w1;
	vec2 g1 = w2 + w3;
	vec2 p0 = (i - 0.5 + w1 / g0) / size;
	vec2 p1 = (i + 1.5 + w3 / g1) / size;
	return g0.y * (g0.x * textureLod(tex, p0, 0.0) + g1.x * textureLod(tex, vec2(p1.x, p0.y), 0.0))
		+ g1.y * (g0.x * textureLod(tex, vec2(p0.x, p1.y), 0.0) + g1.x * textureLod(tex, p1, 0.0));
}

// Slope sample (xy slope, w = mean |slope|^2): bicubic weight b blends between bicubic and the filtered sampler.
vec4 sample_slope(sampler2D tex, vec2 uv, float size, float b) {
	vec4 filtered = texture(tex, uv);
	if (b <= 0.0) return filtered;
	return mix(filtered, sample_bicubic(tex, uv, size), b);
}

// Sub-texel slope variance of a filtered sample (LEAN): E[s^2] - E[s]^2.
float slope_variance(vec4 s) {
	return max(s.w - dot(s.xy, s.xy), 0.0);
}

// View-space position of a screen uv at a given depth-buffer value (reversed Z).
vec3 view_position(vec2 uv, float depth, mat4 inv_projection) {
	vec4 p = inv_projection * vec4(uv * 2.0 - 1.0, depth, 1.0);
	return p.xyz / p.w;
}
