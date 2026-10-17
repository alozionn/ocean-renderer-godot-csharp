#[compute]
#version 450

// Initial JONSWAP spectrum with Donelan-Banner spreading. Writes (h0(k), conj(h0(-k))) per texel.
layout(local_size_x = 16, local_size_y = 16) in;

layout(set = 0, binding = 0, rgba32f) restrict writeonly uniform image2D h0_image;

layout(push_constant, std430) uniform Params {
	float patch_size;
	float wind_speed;
	float wind_direction; // radians
	float fetch;
	float suppression; // metres
	uint seed;
	int size;
	float pad0;
} p;

const float G = 9.81;
const float PI = 3.14159265358979;

uint hash(uint x) {
	uint state = x * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

vec2 gaussian(ivec2 id) {
	uint h1 = hash(hash(uint(id.x) + hash(uint(id.y) + hash(p.seed))));
	uint h2 = hash(h1);
	float u1 = (float(h1 >> 8u) + 1.0) / 16777217.0;
	float u2 = float(h2 >> 8u) / 16777216.0;
	float r = sqrt(-2.0 * log(u1));
	return r * vec2(cos(2.0 * PI * u2), sin(2.0 * PI * u2));
}

float jonswap(float omega, float wp) {
	float alpha = 0.076 * pow(p.wind_speed * p.wind_speed / (p.fetch * G), 0.22);
	float sigma = omega <= wp ? 0.07 : 0.09;
	float r = exp(-(omega - wp) * (omega - wp) / (2.0 * sigma * sigma * wp * wp));
	return alpha * G * G / pow(omega, 5.0) * exp(-1.25 * pow(wp / omega, 4.0)) * pow(3.3, r);
}

float donelan_banner(float theta, float ratio) {
	float beta;
	if (ratio < 0.95) beta = 2.61 * pow(ratio, 1.3);
	else if (ratio < 1.6) beta = 2.28 * pow(ratio, -1.3);
	else beta = pow(10.0, -0.4 + 0.8393 * exp(-0.567 * log(ratio * ratio)));
	float s = 1.0 / cosh(beta * theta);
	return beta / (2.0 * tanh(beta * PI)) * s * s;
}

vec2 h0(ivec2 id) {
	ivec2 m = id - ivec2(greaterThanEqual(id, ivec2(p.size / 2))) * p.size;
	vec2 k = 2.0 * PI * vec2(m) / p.patch_size;
	float kl = length(k);
	if (kl < 1e-6) return vec2(0.0);
	float omega = sqrt(G * kl);
	float wp = 22.0 * pow(G * G / (p.wind_speed * p.fetch), 1.0 / 3.0);
	float theta = atan(k.y, k.x) - p.wind_direction;
	theta -= 2.0 * PI * floor((theta + PI) / (2.0 * PI));
	float s = jonswap(omega, wp) * donelan_banner(theta, omega / wp);
	s *= G / (2.0 * omega) / kl * exp(-kl * kl * p.suppression * p.suppression);
	float amp = sqrt(2.0 * s) * (2.0 * PI / p.patch_size);
	return gaussian(id) * amp / sqrt(2.0);
}

void main() {
	ivec2 id = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(id, ivec2(p.size)))) return;
	int nyq = p.size / 2;
	if (id.x == nyq || id.y == nyq) {
		imageStore(h0_image, id, vec4(0.0));
		return;
	}
	vec2 a = h0(id);
	vec2 b = h0((ivec2(p.size) - id) % p.size);
	imageStore(h0_image, id, vec4(a, b.x, -b.y));
}
