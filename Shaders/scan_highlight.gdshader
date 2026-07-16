shader_type canvas_item;

// Player.ScanHighlightLayerBit's own render, sampled here as a plain silhouette mask (alpha >
// 0.5 = inside the currently scan-mode-highlighted object) — fully transparent everywhere else,
// so this overlay draws nothing at all while no target is set.
uniform sampler2D mask_texture : filter_nearest;

uniform vec4 outline_color : source_color = vec4(0.3, 0.9, 1.0, 1.0);

// Placeholder/tunable — how many texels out from the silhouette's own edge the border sits.
uniform float outline_thickness_px : hint_range(1.0, 6.0) = 2.0;

// Placeholder/tunable — full pulse cycles per second.
uniform float pulse_speed : hint_range(0.5, 5.0) = 2.0;

void fragment()
{
	vec2 texel = outline_thickness_px / vec2(textureSize(mask_texture, 0));

	float center = texture(mask_texture, UV).a;
	float north = texture(mask_texture, UV + vec2(0.0, texel.y)).a;
	float south = texture(mask_texture, UV - vec2(0.0, texel.y)).a;
	float east = texture(mask_texture, UV + vec2(texel.x, 0.0)).a;
	float west = texture(mask_texture, UV - vec2(texel.x, 0.0)).a;
	float max_neighbor = max(max(north, south), max(east, west));

	// An edge pixel is just outside the silhouette but has a neighbor just inside it — draws a
	// clean border hugging the outside of the shape rather than eating into it.
	float edge = step(0.5, max_neighbor) - step(0.5, center);
	edge = clamp(edge, 0.0, 1.0);

	float pulse = 0.6 + 0.4 * sin(TIME * pulse_speed * TAU);
	COLOR = vec4(outline_color.rgb, edge * outline_color.a * pulse);
}
