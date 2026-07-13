shader_type sky;

// Placeholder/tunable — higher = fewer, sparser stars.
uniform float star_threshold : hint_range(0.9, 0.9999) = 0.997;

float hash13(vec3 p3)
{
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

void sky()
{
	vec3 dir = normalize(EYEDIR);
	vec3 cell = floor(dir * 500.0);
	float n = hash13(cell);
	float star = smoothstep(star_threshold, 1.0, n);
	float twinkle = hash13(cell + 7.0);

	COLOR = vec3(0.01, 0.012, 0.02) + vec3(star * (0.5 + 0.5 * twinkle));
}
