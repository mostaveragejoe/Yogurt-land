"""Cartoon style presets appended to every visual prompt."""

STYLES: dict[str, str] = {
    "rubber_hose": (
        "1930s rubber-hose cartoon style, black and white with film grain, "
        "bouncy noodle limbs, pie-cut eyes, vintage animation cel look"
    ),
    "saturday_morning": (
        "1980s Saturday-morning cartoon style, bold outlines, flat saturated "
        "colors, slightly grainy broadcast look, hand-painted backgrounds"
    ),
    "anime": (
        "modern anime style, expressive large eyes, dynamic action lines, "
        "cel shading, dramatic lighting, detailed backgrounds"
    ),
    "flat_modern": (
        "modern flat vector cartoon style, minimal geometric shapes, pastel "
        "palette, clean thick outlines, trendy 2D motion-design look"
    ),
    "claymation": (
        "stop-motion claymation style, visible thumbprints in plasticine, "
        "miniature set with shallow depth of field, warm practical lighting"
    ),
    "pixel": (
        "retro 16-bit pixel art style, limited palette, chunky dithering, "
        "nostalgic video-game aesthetic"
    ),
}


def style_prompt(key: str) -> str:
    try:
        return STYLES[key]
    except KeyError:
        raise KeyError(f"Unknown style '{key}'. Available: {', '.join(STYLES)}") from None
