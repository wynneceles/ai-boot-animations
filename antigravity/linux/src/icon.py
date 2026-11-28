# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

from pathlib import Path

import cairo


def draw_sparkle_surface(size: int) -> cairo.ImageSurface:
    """Draw the blue-to-purple Gemini 4-point star matching macOS/Windows icons."""
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    ctx = cairo.Context(surface)
    ctx.set_antialias(cairo.ANTIALIAS_BEST)

    scale = size / 32.0
    ctx.scale(scale, scale)

    c = 16.0
    r = 15.0
    q = 1.5

    ctx.new_path()
    ctx.move_to(c, c - r)
    ctx.curve_to(c + q, c - q, c + q, c - q, c + r, c)
    ctx.curve_to(c + q, c + q, c + q, c + q, c, c + r)
    ctx.curve_to(c - q, c + q, c - q, c + q, c - r, c)
    ctx.curve_to(c - q, c - q, c - q, c - q, c, c - r)
    ctx.close_path()

    grad = cairo.LinearGradient(2.0, 30.0, 30.0, 2.0)
    grad.add_color_stop_rgba(0.0, 66 / 255.0, 133 / 255.0, 244 / 255.0, 1.0)
    grad.add_color_stop_rgba(1.0, 180 / 255.0, 140 / 255.0, 255 / 255.0, 1.0)
    ctx.set_source(grad)
    ctx.fill()

    surface.flush()
    return surface


def bgra_to_sni_argb(bgra: bytes) -> bytes:
    """Convert Cairo host-endian premultiplied ARGB32 to SNI network-order unpremultiplied ARGB32."""
    import sys

    out = bytearray(len(bgra))
    for i in range(0, len(bgra), 4):
        if sys.byteorder == "big":
            a, r, g, b = bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]
        else:
            b, g, r, a = bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]
        if 0 < a < 255:
            r = min(255, (r * 255 + (a >> 1)) // a)
            g = min(255, (g * 255 + (a >> 1)) // a)
            b = min(255, (b * 255 + (a >> 1)) // a)
        out[i] = a
        out[i + 1] = r
        out[i + 2] = g
        out[i + 3] = b
    return bytes(out)


def render_sni_pixmaps(sizes: tuple[int, ...] = (22, 32, 48)) -> list[tuple[int, int, bytes]]:
    result: list[tuple[int, int, bytes]] = []
    for sz in sizes:
        surf = draw_sparkle_surface(sz)
        result.append((sz, sz, bgra_to_sni_argb(bytes(surf.get_data()))))
    return result


def write_png(dest: Path, size: int = 128) -> Path:
    dest.parent.mkdir(parents=True, exist_ok=True)
    surf = draw_sparkle_surface(size)
    surf.write_to_png(str(dest))
    return dest


def render_gdk_texture(size: int = 28):
    import gi

    gi.require_version("Gdk", "4.0")
    from gi.repository import Gdk, GLib

    surf = draw_sparkle_surface(size)
    raw = bytes(surf.get_data())
    gbytes = GLib.Bytes.new(raw)
    return Gdk.MemoryTexture.new(
        size,
        size,
        Gdk.MemoryFormat.B8G8R8A8_PREMULTIPLIED,
        gbytes,
        surf.get_stride(),
    )
