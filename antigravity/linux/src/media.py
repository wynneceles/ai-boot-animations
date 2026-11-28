# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

from logger import APP_SLUG, log


@dataclass
class ResolvedMedia:
    intro_path: Path
    loop_path: Path
    transcoded: bool


def linux_root() -> Path:
    return Path(__file__).resolve().parent.parent


def data_dir() -> Path:
    base = Path(os.environ.get("XDG_DATA_HOME") or (Path.home() / ".local" / "share"))
    return base / APP_SLUG


def cache_dir() -> Path:
    base = Path(os.environ.get("XDG_CACHE_HOME") or (Path.home() / ".cache"))
    return base / APP_SLUG / "media"


def find_asset_file(filename: str) -> Path | None:
    candidates = [
        linux_root() / "assets" / filename,
        data_dir() / "assets" / filename,
        linux_root().parent / "assets" / filename,
        linux_root().parent / "windows" / "assets" / filename,
    ]
    for path in candidates:
        if path.is_file():
            return path
    return None


def _registry_has_decoder(media_type_prefix: str) -> bool:
    import gi

    gi.require_version("Gst", "1.0")
    from gi.repository import Gst

    Gst.init(None)
    reg = Gst.Registry.get()
    for factory in reg.get_feature_list(Gst.ElementFactory):
        klass = factory.get_metadata("klass") or ""
        if "Decoder" not in klass:
            continue
        for tpl in factory.get_static_pad_templates():
            if tpl.direction != Gst.PadDirection.SINK:
                continue
            caps = tpl.get_caps()
            if caps and media_type_prefix in caps.to_string():
                return True
    return False


def gst_can_decode_mp4() -> bool:
    import gi

    gi.require_version("Gst", "1.0")
    from gi.repository import Gst

    Gst.init(None)
    if not Gst.ElementFactory.find("qtdemux"):
        return False
    has_hevc = _registry_has_decoder("video/x-h265")
    has_aac = _registry_has_decoder("audio/mpeg")
    return has_hevc and has_aac


def gst_can_decode_webm() -> bool:
    import gi

    gi.require_version("Gst", "1.0")
    from gi.repository import Gst

    Gst.init(None)
    return bool(
        Gst.ElementFactory.find("matroskademux")
        and _registry_has_decoder("video/x-vp9")
        and _registry_has_decoder("audio/x-opus")
    )


def _probe_python_imageio_ffmpeg(py_exe: str) -> str | None:
    try:
        out = subprocess.check_output(
            [py_exe, "-c", "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())"],
            stderr=subprocess.DEVNULL,
            text=True,
            timeout=5,
        ).strip()
        if out and Path(out).is_file():
            return out
    except Exception:
        pass
    return None


def find_ffmpeg() -> str | None:
    env_bin = os.environ.get("AGBA_FFMPEG")
    if env_bin and Path(env_bin).is_file():
        return env_bin
    which_bin = shutil.which("ffmpeg")
    if which_bin:
        return which_bin
    for fixed in ("/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg"):
        if Path(fixed).is_file():
            return fixed
    # Fallback: probe Python installations (e.g., anaconda/miniconda/venv) that ship imageio_ffmpeg.
    py_candidates = [
        sys.executable,
        shutil.which("python3") or "",
        str(Path.home() / "anaconda3" / "bin" / "python3"),
        str(Path.home() / "miniconda3" / "bin" / "python3"),
        str(Path.home() / "miniforge3" / "bin" / "python3"),
        str(Path.home() / "mambaforge" / "bin" / "python3"),
    ]
    seen: set[str] = set()
    for py in py_candidates:
        if not py or py in seen or not Path(py).is_file():
            continue
        seen.add(py)
        found = _probe_python_imageio_ffmpeg(py)
        if found:
            return found
    return None


def _file_sig(path: Path) -> dict[str, int]:
    st = path.stat()
    return {"path": str(path.resolve()), "size": st.st_size, "mtime_ns": st.st_mtime_ns}


def _transcode_to_webm(ffmpeg_exe: str, src: Path, dst: Path, with_audio: bool) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_suffix(".tmp.webm")
    if tmp.exists():
        tmp.unlink()
    cmd = [
        ffmpeg_exe,
        "-v",
        "error",
        "-y",
        "-i",
        str(src),
        "-c:v",
        "libvpx-vp9",
        "-deadline",
        "good",
        "-cpu-used",
        "5",
        "-row-mt",
        "1",
        "-threads",
        "0",
        "-crf",
        "26",
        "-b:v",
        "0",
        "-pix_fmt",
        "yuv420p",
        "-g",
        "30",
    ]
    if with_audio:
        cmd += ["-c:a", "libopus", "-b:a", "128k"]
    else:
        cmd += ["-an"]
    cmd.append(str(tmp))
    log(f"正在本地转码为兼容格式：{src.name} -> {dst}")
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        if tmp.exists():
            tmp.unlink()
        raise RuntimeError(f"ffmpeg 转码失败 ({src.name}): {proc.stderr.strip()}")
    tmp.replace(dst)


def resolve_media(
    intro_override: Path | None = None,
    loop_override: Path | None = None,
    output_dir: Path | None = None,
) -> ResolvedMedia:
    intro_src = intro_override or find_asset_file("intro.mp4")
    loop_src = loop_override or find_asset_file("loop.mp4")
    if not intro_src or not loop_src:
        raise FileNotFoundError(
            "找不到 intro.mp4 或 loop.mp4，请确认已将视频放入 assets/ 目录或通过 --intro/--loop 指定。"
        )

    if intro_override is None and loop_override is None and gst_can_decode_mp4():
        return ResolvedMedia(intro_path=intro_src, loop_path=loop_src, transcoded=False)

    # Check if caller explicitly passed webm files or native mp4 works for overrides
    if (
        intro_src.suffix.lower() == ".webm"
        and loop_src.suffix.lower() == ".webm"
    ) or gst_can_decode_mp4():
        return ResolvedMedia(intro_path=intro_src, loop_path=loop_src, transcoded=False)

    if not gst_can_decode_webm():
        raise RuntimeError(
            "当前 GStreamer 缺少视频解码器（HEVC/VP9）。请安装 gstreamer1.0-libav 或 gstreamer1.0-plugins-good。"
        )

    target_dir = output_dir or cache_dir()
    target_dir.mkdir(parents=True, exist_ok=True)
    intro_webm = target_dir / "intro.webm"
    loop_webm = target_dir / "loop.webm"
    meta_file = target_dir / "media-meta.json"

    expected_meta = {"intro": _file_sig(intro_src), "loop": _file_sig(loop_src)}
    current_meta = None
    if meta_file.is_file():
        try:
            current_meta = json.loads(meta_file.read_text(encoding="utf-8"))
        except Exception:
            current_meta = None

    if (
        current_meta == expected_meta
        and intro_webm.is_file()
        and loop_webm.is_file()
        and intro_webm.stat().st_size > 0
        and loop_webm.stat().st_size > 0
    ):
        return ResolvedMedia(intro_path=intro_webm, loop_path=loop_webm, transcoded=True)

    # Also accept pre-generated webm alongside intro_src when its metadata matches expected_meta
    sibling_intro = intro_src.with_suffix(".webm")
    sibling_loop = loop_src.with_suffix(".webm")
    sibling_meta_file = intro_src.parent / "media-meta.json"
    if (
        sibling_meta_file.is_file()
        and sibling_intro.is_file()
        and sibling_loop.is_file()
        and sibling_intro.stat().st_size > 0
        and sibling_loop.stat().st_size > 0
    ):
        try:
            if json.loads(sibling_meta_file.read_text(encoding="utf-8")) == expected_meta:
                return ResolvedMedia(intro_path=sibling_intro, loop_path=sibling_loop, transcoded=True)
        except Exception:
            pass

    ffmpeg_exe = find_ffmpeg()
    if not ffmpeg_exe:
        raise RuntimeError(
            "系统 GStreamer 缺少 HEVC/AAC 解码插件，且未找到 ffmpeg 进行一次性本地转换。\n"
            "请任选其一：\n"
            "  Ubuntu/Debian: sudo apt install gstreamer1.0-libav gstreamer1.0-plugins-bad\n"
            "  或者安装 ffmpeg: sudo apt install ffmpeg"
        )

    _transcode_to_webm(ffmpeg_exe, intro_src, intro_webm, with_audio=True)
    _transcode_to_webm(ffmpeg_exe, loop_src, loop_webm, with_audio=False)
    meta_file.write_text(json.dumps(expected_meta, indent=2), encoding="utf-8")
    return ResolvedMedia(intro_path=intro_webm, loop_path=loop_webm, transcoded=True)


def main() -> int:
    parser = argparse.ArgumentParser(description="Prepare compatible media assets for Linux playback.")
    parser.add_argument("--prepare", action="store_true", help="Ensure playable media exists locally")
    parser.add_argument("--intro", type=Path, default=None)
    parser.add_argument("--loop", type=Path, default=None)
    parser.add_argument("--out-dir", type=Path, default=None)
    args = parser.parse_args()
    res = resolve_media(args.intro, args.loop, args.out_dir)
    print(json.dumps({"intro": str(res.intro_path), "loop": str(res.loop_path), "transcoded": res.transcoded}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
