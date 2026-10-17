#[compute]
#version 450

// 2x2 box downsample of one mip level into the next.
layout(local_size_x = 8, local_size_y = 8) in;

layout(set = 0, binding = 0, rgba16f) restrict readonly uniform image2D src_image;
layout(set = 0, binding = 1, rgba16f) restrict writeonly uniform image2D dst_image;

layout(push_constant, std430) uniform Params {
	ivec2 dst_size;
	float pad0;
	float pad1;
} p;

void main() {
	ivec2 id = ivec2(gl_GlobalInvocationID.xy);
	if (any(greaterThanEqual(id, p.dst_size))) return;
	ivec2 s = id * 2;
	vec4 sum = imageLoad(src_image, s) + imageLoad(src_image, s + ivec2(1, 0))
		+ imageLoad(src_image, s + ivec2(0, 1)) + imageLoad(src_image, s + ivec2(1, 1));
	imageStore(dst_image, id, sum * 0.25);
}
