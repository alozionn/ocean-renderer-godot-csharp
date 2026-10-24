// Foam composite: field lookup (near/far), hex-tiled detail texture, Crest-style lace erosion, veil, relief.
// Expects the foam uniforms declared by ocean.gdshader.

struct FoamSample {
	float coverage; // final foam opacity
	float white;    // lace part (opaque foam)
	float density;  // field density plus edge foam
	float fresh;    // field freshness
	vec2 relief;    // lace gradient in grid space
};

vec2 foam_field_lookup(vec2 grid) {
	vec2 uvn = (grid - foam_near_rect.xy) * foam_near_rect.w;
	float wn = 1.0 - smoothstep(0.5, 0.95, max(abs(2.0 * uvn.x - 1.0), abs(2.0 * uvn.y - 1.0)));
	vec2 near = wn > 0.0 ? textureLod(foam_near, uvn, 0.0).rg : vec2(0.0);
	if (wn >= 0.999) return near;
	vec2 uvf = (grid - foam_far_rect.xy) * foam_far_rect.w;
	float wf = 1.0 - smoothstep(0.8, 1.0, max(abs(2.0 * uvf.x - 1.0), abs(2.0 * uvf.y - 1.0)));
	vec2 far = textureLod(foam_far, uvf, 0.0).rg * wf;
	return mix(far, near, wn);
}

// Hex tiling (Mikkelsen 2022): three randomly rotated/offset copies blended by barycentric weights.
// Exact integer hash (PCG). A sin-based hash is not: the same vertex is reached from three code paths, and one ulp
// of a large sin() argument gives a different random copy on each side of a triangle edge (hard seams).
uint hex_pcg(uint x) {
	uint state = x * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

vec2 hex_hash(ivec2 p) {
	uint h = hex_pcg(uint(p.x) + hex_pcg(uint(p.y)));
	return vec2(float(h >> 8u), float(hex_pcg(h) >> 8u)) / 16777216.0;
}

float hex_copy(vec2 uv, ivec2 vertex, vec2 gx, vec2 gy, float density, bool soft) {
	vec2 h = hex_hash(vertex);
	float angle = (h.x * 2.0 - 1.0) * radians(foam_hex_rotation);
	mat2 rot = mat2(vec2(cos(angle), sin(angle)), vec2(-sin(angle), cos(angle)));
	vec2 v = vec2(vertex);
	vec2 centre = vec2(v.x + 0.5 * v.y, 0.866025404 * v.y) / (3.46410162 * density);
	vec2 p = rot * (uv - centre) + centre + h;
	// Only the sharp crest texture needs anisotropic filtering (~0.05 ms at 1080p); the blurred tail doesn't.
	return soft ? textureGrad(foam_detail_soft, p, rot * gx, rot * gy).r : textureGrad(foam_detail, p, rot * gx, rot * gy).r;
}

float foam_hex(vec2 uv, vec2 gx, vec2 gy, float blur, bool soft) {
	float scale = exp2(blur);
	gx *= scale;
	gy *= scale;
	float density = foam_hex_density;
	vec2 st = uv * 3.46410162 * density;
	vec2 skewed = vec2(st.x - 0.57735027 * st.y, 1.15470054 * st.y);
	ivec2 base = ivec2(floor(skewed));
	vec3 t = vec3(fract(skewed), 0.0);
	t.z = 1.0 - t.x - t.y;
	float s = step(0.0, -t.z);
	float s2 = 2.0 * s - 1.0;
	vec3 w = vec3(-t.z * s2, s - t.y * s2, s - t.x * s2);
	ivec2 v1 = base + ivec2(int(s), int(s));
	ivec2 v2 = base + ivec2(int(s), 1 - int(s));
	ivec2 v3 = base + ivec2(1 - int(s), int(s));
	vec3 c = vec3(hex_copy(uv, v1, gx, gy, density, soft), hex_copy(uv, v2, gx, gy, density, soft), hex_copy(uv, v3, gx, gy, density, soft));
	// Sharpened barycentric weights. (A luminance-weighted blend was tried: it made the foam harder and less wispy.)
	w = pow(max(w, vec3(0.0)), vec3(foam_hex_contrast));
	return dot(w, c) / max(w.x + w.y + w.z, 1e-5);
}

// Crest-style erosion: thin foam keeps only the brightest parts of the detail. Softer as the foam ages.
float foam_lace(float body, float detail, float softness) {
	float t = mix(1.0 + softness, -softness, body * foam_max_cover);
	return smoothstep(t - softness, t + softness, detail) * mix(foam_dense_shade, 1.0, detail);
}

// Grid xz -> detail uv: rotate into the streak frame, stretch along it, tile.
mat2 foam_uv_matrix(float streak_angle) {
	float c = cos(streak_angle), s = sin(streak_angle);
	// rows: along / (stretch * tile), across / tile
	return mat2(vec2(c / (foam_detail_stretch * foam_detail_tile), -s / foam_detail_tile),
		vec2(s / (foam_detail_stretch * foam_detail_tile), c / foam_detail_tile));
}

FoamSample foam_composite(vec2 grid, vec2 grid_dx, vec2 grid_dy, float dist, float edge, bool relief) {
	FoamSample f;
	vec2 field = foam_field_lookup(grid) * exp(-dist / foam_fade_range);
	f.density = field.r + edge;
	f.fresh = field.g + 0.5 * edge;
	float body = smoothstep(foam_trail_low, foam_trail_high, f.density);
	float cap = smoothstep(foam_cap_low, foam_cap_high, f.fresh);
	mat2 m = foam_uv_matrix(foam_streak_angle);
	vec2 uv = m * grid;
	vec2 gx = m * grid_dx;
	vec2 gy = m * grid_dy;
	float lod = smoothstep(foam_lod_start, foam_lod_end, dist);
	float foam = body * foam_far_opacity;
	f.white = 0.0;
	f.relief = vec2(0.0);
	if (body + cap > 0.001 && lod < 0.999) {
		// Sea of Thieves: sharp foam at the crest, blending to a lower-frequency version as the foam ages (the field also
		// blurs it out over time). The tail is the same texture at the same place, only blurred, so it stays coherent with
		// the crest it came from. Age: how much of the density is no longer fresh, starting at foam_age_start.
		float age = smoothstep(foam_age_start, 1.0, clamp(1.0 - f.fresh / max(f.density, 1e-3), 0.0, 1.0));
		float d1 = foam_hex(uv, gx, gy, foam_detail_blur, false);
		float d_tail = foam_hex(uv, gx, gy, foam_detail_blur + foam_tail_blur, true);
		float tail_mix = mix(foam_detail_mix, 1.0, age);
		float softness = mix(foam_softness, foam_age_softness, age);
		float detail = mix(d1, d_tail, tail_mix);
		float cover = max(body, cap);
		float lace = foam_lace(cover, detail, softness);
		float white = lace * mix(foam_trail_opacity, 1.0, cap) * mix(1.0, foam_tail_opacity, age);
		float veil = body * foam_veil * clamp(d_tail * 1.6, 0.0, 1.0);
		foam = mix(max(white, veil), foam, lod);
		f.white = white * (1.0 - lod);
		if (relief && age < 0.999) {
			const float EPS = 0.01;
			float lx = foam_lace(cover, mix(foam_hex(uv + vec2(EPS, 0.0), gx, gy, foam_detail_blur, false), d_tail, tail_mix), softness);
			float ly = foam_lace(cover, mix(foam_hex(uv + vec2(0.0, EPS), gx, gy, foam_detail_blur, false), d_tail, tail_mix), softness);
			vec2 g_tex = vec2(lx - lace, ly - lace) / EPS;
			f.relief = transpose(m) * g_tex * (1.0 - lod) * (1.0 - age);
		}
	}
	f.coverage = clamp(foam, 0.0, 1.0);
	return f;
}
