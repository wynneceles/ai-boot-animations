# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import ctypes
import ctypes.util
import math
from pathlib import Path

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Gdk", "4.0")
gi.require_version("GdkPixbuf", "2.0")
gi.require_version("Pango", "1.0")
gi.require_version("PangoCairo", "1.0")
from gi.repository import Gdk, GdkPixbuf, GLib, Gtk, Pango, PangoCairo  # noqa: E402

from logger import log  # noqa: E402
from media import find_asset_file  # noqa: E402

_FONT_LOADED = False
_FONT_FAMILY = "Georgia"
_CAIRO_LIBS: tuple[ctypes.CDLL, ctypes.CDLL, ctypes.CDLL, ctypes.CDLL] | None = None

_CAIRO_FORMAT_ARGB32 = 0
_CAIRO_LINE_JOIN_ROUND = 1


def _load_c_libs():
    global _CAIRO_LIBS
    if _CAIRO_LIBS is not None:
        return _CAIRO_LIBS

    libcairo = ctypes.CDLL(ctypes.util.find_library("cairo"))
    libpc = ctypes.CDLL(ctypes.util.find_library("pangocairo-1.0"))
    libpango = ctypes.CDLL(ctypes.util.find_library("pango-1.0"))
    libgobj = ctypes.CDLL(ctypes.util.find_library("gobject-2.0"))

    libcairo.cairo_image_surface_create.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
    libcairo.cairo_image_surface_create.restype = ctypes.c_void_p
    libcairo.cairo_image_surface_create_from_png.argtypes = [ctypes.c_char_p]
    libcairo.cairo_image_surface_create_from_png.restype = ctypes.c_void_p
    libcairo.cairo_image_surface_get_width.argtypes = [ctypes.c_void_p]
    libcairo.cairo_image_surface_get_width.restype = ctypes.c_int
    libcairo.cairo_image_surface_get_height.argtypes = [ctypes.c_void_p]
    libcairo.cairo_image_surface_get_height.restype = ctypes.c_int
    libcairo.cairo_image_surface_get_stride.argtypes = [ctypes.c_void_p]
    libcairo.cairo_image_surface_get_stride.restype = ctypes.c_int
    libcairo.cairo_image_surface_get_data.argtypes = [ctypes.c_void_p]
    libcairo.cairo_image_surface_get_data.restype = ctypes.POINTER(ctypes.c_ubyte)
    libcairo.cairo_surface_flush.argtypes = [ctypes.c_void_p]
    libcairo.cairo_surface_mark_dirty.argtypes = [ctypes.c_void_p]
    libcairo.cairo_surface_destroy.argtypes = [ctypes.c_void_p]
    libcairo.cairo_surface_write_to_png.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
    libcairo.cairo_surface_write_to_png.restype = ctypes.c_int

    libcairo.cairo_create.argtypes = [ctypes.c_void_p]
    libcairo.cairo_create.restype = ctypes.c_void_p
    libcairo.cairo_destroy.argtypes = [ctypes.c_void_p]
    libcairo.cairo_save.argtypes = [ctypes.c_void_p]
    libcairo.cairo_restore.argtypes = [ctypes.c_void_p]
    libcairo.cairo_translate.argtypes = [ctypes.c_void_p, ctypes.c_double, ctypes.c_double]
    libcairo.cairo_set_source_rgba.argtypes = [
        ctypes.c_void_p,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
        ctypes.c_double,
    ]
    libcairo.cairo_set_line_width.argtypes = [ctypes.c_void_p, ctypes.c_double]
    libcairo.cairo_set_line_join.argtypes = [ctypes.c_void_p, ctypes.c_int]
    libcairo.cairo_stroke_preserve.argtypes = [ctypes.c_void_p]
    libcairo.cairo_fill.argtypes = [ctypes.c_void_p]
    libcairo.cairo_set_source_surface.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_double, ctypes.c_double]
    libcairo.cairo_paint.argtypes = [ctypes.c_void_p]

    libpc.pango_cairo_create_layout.argtypes = [ctypes.c_void_p]
    libpc.pango_cairo_create_layout.restype = ctypes.c_void_p
    libpc.pango_cairo_layout_path.argtypes = [ctypes.c_void_p, ctypes.c_void_p]

    libpango.pango_font_description_from_string.argtypes = [ctypes.c_char_p]
    libpango.pango_font_description_from_string.restype = ctypes.c_void_p
    libpango.pango_font_description_free.argtypes = [ctypes.c_void_p]
    libpango.pango_layout_set_font_description.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    libpango.pango_layout_set_alignment.argtypes = [ctypes.c_void_p, ctypes.c_int]
    libpango.pango_layout_set_width.argtypes = [ctypes.c_void_p, ctypes.c_int]
    libpango.pango_layout_set_text.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]

    class _PangoRectangle(ctypes.Structure):
        _fields_ = [("x", ctypes.c_int), ("y", ctypes.c_int), ("width", ctypes.c_int), ("height", ctypes.c_int)]

    libpango._PangoRectangle = _PangoRectangle
    libpango.pango_layout_get_pixel_extents.argtypes = [
        ctypes.c_void_p,
        ctypes.POINTER(_PangoRectangle),
        ctypes.POINTER(_PangoRectangle),
    ]

    libgobj.g_object_unref.argtypes = [ctypes.c_void_p]

    _CAIRO_LIBS = (libcairo, libpc, libpango, libgobj)
    return _CAIRO_LIBS


def ensure_subtitle_font() -> str:
    global _FONT_LOADED, _FONT_FAMILY
    if _FONT_LOADED:
        return _FONT_FAMILY
    _FONT_LOADED = True
    ttf = find_asset_file("LibreBodoni-Italic.ttf")
    if ttf is not None:
        try:
            lib_name = ctypes.util.find_library("fontconfig")
            if lib_name:
                fc = ctypes.CDLL(lib_name)
                fc.FcConfigAppFontAddFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
                fc.FcConfigAppFontAddFile.restype = ctypes.c_int
                if fc.FcConfigAppFontAddFile(None, str(ttf).encode("utf-8")):
                    _FONT_FAMILY = "Libre Bodoni"
                    return _FONT_FAMILY
        except Exception as exc:
            log(f"字体载入失败，回退 Georgia：{exc}")
    font_map = PangoCairo.font_map_get_default()
    names = {fam.get_name().lower() for fam in font_map.list_families()}
    for candidate in ("Libre Bodoni", "Bodoni 72", "Georgia", "Serif"):
        if candidate.lower() in names:
            _FONT_FAMILY = candidate
            break
    return _FONT_FAMILY


def compute_font_size(width: float, height: float, width_ratio: float) -> float:
    return max(18.0, min(width * width_ratio / 15.5, height * 0.09))


def _draw_subtitle_c(cr: int, width: int, height: int, text: str, width_ratio: float) -> None:
    if not text or width < 10 or height < 10:
        return
    libcairo, libpc, libpango, libgobj = _load_c_libs()
    family = ensure_subtitle_font()
    size = compute_font_size(float(width), float(height), width_ratio)

    layout = libpc.pango_cairo_create_layout(cr)
    desc = libpango.pango_font_description_from_string(f"{family} Italic {size:.2f}px".encode("utf-8"))
    try:
        libpango.pango_layout_set_font_description(layout, desc)
        libpango.pango_layout_set_alignment(layout, int(Pango.Alignment.CENTER))
        max_w = max(40.0, width - 40.0)
        libpango.pango_layout_set_width(layout, int(max_w * Pango.SCALE))
        text_bytes = text.encode("utf-8")
        libpango.pango_layout_set_text(layout, text_bytes, len(text_bytes))

        ink = libpango._PangoRectangle()
        logical = libpango._PangoRectangle()
        libpango.pango_layout_get_pixel_extents(layout, ctypes.byref(ink), ctypes.byref(logical))
        text_h = max(logical.height, ink.height)
        x = (width - max_w) / 2.0
        y = height * 0.85 - text_h / 2.0

        # Multi-ring soft dark shadow
        blur = max(1.5, size * 0.12)
        for ring in range(3):
            radius = blur * (ring + 1) / 3.0
            ring_alpha = 0.28 / (ring + 1)
            libcairo.cairo_set_source_rgba(cr, 0.0, 0.0, 0.0, ring_alpha)
            for angle_idx in range(8):
                ang = angle_idx * (math.pi / 4.0)
                libcairo.cairo_save(cr)
                libcairo.cairo_translate(cr, x + math.cos(ang) * radius, y + math.sin(ang) * radius)
                libpc.pango_cairo_layout_path(cr, layout)
                libcairo.cairo_fill(cr)
                libcairo.cairo_restore(cr)

        # Black outline stroke + pure white fill
        libcairo.cairo_save(cr)
        libcairo.cairo_translate(cr, x, y)
        libpc.pango_cairo_layout_path(cr, layout)
        libcairo.cairo_set_source_rgba(cr, 0.0, 0.0, 0.0, 0.9)
        libcairo.cairo_set_line_width(cr, max(2.0, size * 0.1))
        libcairo.cairo_set_line_join(cr, _CAIRO_LINE_JOIN_ROUND)
        libcairo.cairo_stroke_preserve(cr)
        libcairo.cairo_set_source_rgba(cr, 1.0, 1.0, 1.0, 1.0)
        libcairo.cairo_fill(cr)
        libcairo.cairo_restore(cr)
    finally:
        libpango.pango_font_description_free(desc)
        libgobj.g_object_unref(layout)


def render_subtitle_texture(width: int, height: int, text: str, width_ratio: float = 0.6) -> Gdk.MemoryTexture | None:
    if not text or width < 10 or height < 10:
        return None
    libcairo, _libpc, _libpango, _libgobj = _load_c_libs()
    surf = libcairo.cairo_image_surface_create(_CAIRO_FORMAT_ARGB32, width, height)
    cr = libcairo.cairo_create(surf)
    try:
        _draw_subtitle_c(cr, width, height, text, width_ratio)
        libcairo.cairo_surface_flush(surf)
        stride = libcairo.cairo_image_surface_get_stride(surf)
        data_ptr = libcairo.cairo_image_surface_get_data(surf)
        raw = ctypes.string_at(data_ptr, stride * height)
        return Gdk.MemoryTexture.new(
            width,
            height,
            Gdk.MemoryFormat.B8G8R8A8_PREMULTIPLIED,
            GLib.Bytes.new(raw),
            stride,
        )
    finally:
        libcairo.cairo_destroy(cr)
        libcairo.cairo_surface_destroy(surf)


class SubtitleOverlay(Gtk.Picture):
    """GPU-friendly subtitle layer rendered via PangoCairo without requiring python3-gi-cairo."""

    def __init__(self, width_ratio: float = 0.6) -> None:
        super().__init__()
        self._text = ""
        self._alpha = 0.0
        self._tex_key: tuple[str, int, int, float] | None = None
        self.width_ratio = width_ratio
        self.set_can_target(False)
        self.set_can_focus(False)
        self.set_can_shrink(True)
        self.set_content_fit(Gtk.ContentFit.FILL)
        self.set_opacity(0.0)
        self._last_w = 0
        self._last_h = 0

    def set_viewport_size(self, width: int, height: int) -> None:
        if width != self._last_w or height != self._last_h:
            self._last_w = width
            self._last_h = height
            self._refresh_texture()

    def set_text(self, text: str) -> None:
        if text != self._text:
            self._text = text
            self._refresh_texture()

    def set_alpha(self, alpha: float) -> None:
        alpha = max(0.0, min(1.0, alpha))
        if abs(alpha - self._alpha) > 0.002:
            self._alpha = alpha
            self.set_opacity(alpha)

    @property
    def alpha(self) -> float:
        return self._alpha

    def _refresh_texture(self) -> None:
        w = self._last_w or self.get_width()
        h = self._last_h or self.get_height()
        if not self._text or w < 10 or h < 10:
            self.set_paintable(None)
            self._tex_key = None
            return
        key = (self._text, int(w), int(h), round(self.width_ratio, 3))
        if key == self._tex_key:
            return
        self._tex_key = key
        tex = render_subtitle_texture(int(w), int(h), self._text, self.width_ratio)
        self.set_paintable(tex)


def render_subtitle_to_png(bg_path: Path, out_path: Path, text: str, width_ratio: float = 0.6) -> None:
    libcairo, _libpc, _libpango, _libgobj = _load_c_libs()
    pixbuf = GdkPixbuf.Pixbuf.new_from_file(str(bg_path))
    w = pixbuf.get_width()
    h = pixbuf.get_height()
    surf = libcairo.cairo_image_surface_create(_CAIRO_FORMAT_ARGB32, w, h)
    cr = libcairo.cairo_create(surf)
    try:
        has_alpha = pixbuf.get_has_alpha()
        n_channels = pixbuf.get_n_channels()
        rowstride = pixbuf.get_rowstride()
        pixels = pixbuf.get_pixels()
        bg_surf = libcairo.cairo_image_surface_create(_CAIRO_FORMAT_ARGB32, w, h)
        try:
            dst_stride = libcairo.cairo_image_surface_get_stride(bg_surf)
            dst_ptr = libcairo.cairo_image_surface_get_data(bg_surf)
            buf = bytearray(dst_stride * h)
            for row in range(h):
                src_off = row * rowstride
                dst_off = row * dst_stride
                for col in range(w):
                    si = src_off + col * n_channels
                    di = dst_off + col * 4
                    r = pixels[si]
                    g = pixels[si + 1]
                    b = pixels[si + 2]
                    a = pixels[si + 3] if has_alpha else 255
                    if a != 255:
                        b = (b * a) // 255
                        g = (g * a) // 255
                        r = (r * a) // 255
                    buf[di] = b
                    buf[di + 1] = g
                    buf[di + 2] = r
                    buf[di + 3] = a
            ctypes.memmove(dst_ptr, bytes(buf), len(buf))
            libcairo.cairo_surface_mark_dirty(bg_surf)
            libcairo.cairo_set_source_surface(cr, bg_surf, 0.0, 0.0)
            libcairo.cairo_paint(cr)
        finally:
            libcairo.cairo_surface_destroy(bg_surf)

        _draw_subtitle_c(cr, w, h, text, width_ratio)
        libcairo.cairo_surface_flush(surf)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        rc = libcairo.cairo_surface_write_to_png(surf, str(out_path).encode("utf-8"))
        if rc != 0:
            raise RuntimeError(f"cairo_surface_write_to_png failed with status {rc}")
        log(f"字幕渲染到 {out_path} 字体={ensure_subtitle_font()}")
    finally:
        libcairo.cairo_destroy(cr)
        libcairo.cairo_surface_destroy(surf)
