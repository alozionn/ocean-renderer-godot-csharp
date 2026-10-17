#[compute]
#version 450

// Time evolution + inverse FFT (Stockham radix-2, in place in groupshared memory).
// Pass 0, dispatch (1, N, 2): evolve h0 to t while loading each row, transform the row; z picks packed pair A or B.
// Pass 1, dispatch (N, 1, 1): transform pair A's column, then pair B's, then assemble the outputs.
// Packed pairs (two real fields per complex transform):
//   A = (Dx + i Dz, Dy + i dDy/dx), B = (dDy/dz + i dDx/dx, dDz/dz + i dDx/dz)
layout(local_size_x = 256) in;

layout(set = 0, binding = 0, rgba32f) restrict readonly uniform image2D h0_image;
layout(set = 0, binding = 1, rgba32f) restrict uniform image2D spectrum_a;
layout(set = 0, binding = 2, rgba32f) restrict uniform image2D spectrum_b;
layout(set = 0, binding = 3, rgba16f) restrict writeonly uniform image2D displacement_image;
layout(set = 0, binding = 4, rgba16f) restrict writeonly uniform image2D slope_image;
layout(set = 0, binding = 5, rgba16f) restrict writeonly uniform image2D partials_image;

layout(push_constant, std430) uniform Params {
	int size;
	int pass_index;
	float time;
	float patch_size;
	float height_scale;
	float choppiness;
	float pad0;
	float pad1;
} p;

const float G = 9.81;
const float PI = 3.14159265358979;
const int THREADS = 256;
const int MAX_PER_THREAD = 4; // N <= 1024

shared vec4 line[1024];

vec2 cmul(vec2 a, vec2 b) { return vec2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }
vec2 mul_i(vec2 a) { return vec2(-a.y, a.x); }

void sync() {
	memoryBarrierShared();
	barrier();
}

void inverse_fft(int tid) {
	int half_n = p.size / 2;
	for (int span = 1; span < p.size; span <<= 1) {
		vec4 u[2], v[2];
		for (int i = 0; i < 2; i++) {
			int j = tid + THREADS * i;
			if (j < half_n) {
				u[i] = line[j];
				v[i] = line[j + half_n];
			}
		}
		sync();
		for (int i = 0; i < 2; i++) {
			int j = tid + THREADS * i;
			if (j < half_n) {
				int k = j & (span - 1);
				float angle = PI * float(k) / float(span);
				vec2 w = vec2(cos(angle), sin(angle));
				vec4 vw = vec4(cmul(v[i].xy, w), cmul(v[i].zw, w));
				int dst = ((j - k) << 1) + k;
				line[dst] = u[i] + vw;
				line[dst + span] = u[i] - vw;
			}
		}
		sync();
	}
}

vec4 packed_pair(ivec2 id, int pair) {
	vec4 s = imageLoad(h0_image, id);
	ivec2 m = id - ivec2(greaterThanEqual(id, ivec2(p.size / 2))) * p.size;
	vec2 k = 2.0 * PI * vec2(m) / p.patch_size;
	float kl = length(k);
	float phase = sqrt(G * kl) * p.time;
	vec2 e = vec2(cos(phase), sin(phase));
	vec2 h = cmul(s.xy, e) + cmul(s.zw, vec2(e.x, -e.y));
	vec2 u = kl > 1e-6 ? k / kl : vec2(0.0);
	if (pair == 0)
		return vec4(mul_i(u.x * h) - u.y * h, h - k.x * h);
	return vec4(mul_i(k.y * h - k.x * u.x * h), -k.y * u.y * h + mul_i(-k.x * u.y * h));
}

void main() {
	int tid = int(gl_LocalInvocationID.x);
	int per_thread = p.size / THREADS;
	if (p.pass_index == 0) {
		int row = int(gl_WorkGroupID.y);
		int pair = int(gl_WorkGroupID.z);
		for (int i = 0; i < per_thread; i++) {
			int x = tid + THREADS * i;
			line[x] = packed_pair(ivec2(x, row), pair);
		}
		sync();
		inverse_fft(tid);
		for (int i = 0; i < per_thread; i++) {
			int x = tid + THREADS * i;
			if (pair == 0) imageStore(spectrum_a, ivec2(x, row), line[x]);
			else imageStore(spectrum_b, ivec2(x, row), line[x]);
		}
		return;
	}

	int column = int(gl_WorkGroupID.x);
	vec4 a[MAX_PER_THREAD];
	for (int i = 0; i < per_thread; i++) line[tid + THREADS * i] = imageLoad(spectrum_a, ivec2(column, tid + THREADS * i));
	sync();
	inverse_fft(tid);
	for (int i = 0; i < per_thread; i++) a[i] = line[tid + THREADS * i];
	sync();
	for (int i = 0; i < per_thread; i++) line[tid + THREADS * i] = imageLoad(spectrum_b, ivec2(column, tid + THREADS * i));
	sync();
	inverse_fft(tid);

	float h = p.height_scale;
	float l = p.choppiness * h;
	for (int i = 0; i < per_thread; i++) {
		int y = tid + THREADS * i;
		vec4 b = line[y];
		// a = (Dx, Dz, Dy, dDy/dx), b = (dDy/dz, dDx/dx, dDz/dz, dDx/dz)
		float jxx = 1.0 + l * b.y;
		float jzz = 1.0 + l * b.z;
		float jxz = l * b.w;
		float j = jxx * jzz - jxz * jxz;
		vec2 slope = h * vec2(a[i].w, b.x) / max(vec2(jxx, jzz), vec2(0.25));
		ivec2 id = ivec2(column, y);
		imageStore(displacement_image, id, vec4(l * a[i].x, h * a[i].z, l * a[i].y, j));
		imageStore(slope_image, id, vec4(slope, 0.0, dot(slope, slope)));
		imageStore(partials_image, id, vec4(jxx - 1.0, jzz - 1.0, jxz, 0.0));
	}
}
