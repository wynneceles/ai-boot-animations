# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from logger import APP_SLUG, log


@dataclass
class Subtitle:
    text: str
    start: float
    end: float


def _default_subtitles() -> list[Subtitle]:
    return [
        Subtitle("Where twin stars align, my blades carve a new horizon.", 2.68, 6.86),
        Subtitle("Ready to rewrite the universe?", 8.00, 10.20),
    ]


def _clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, float(value)))


def _as_bool(data: dict[str, Any], key: str, fallback: bool) -> bool:
    val = data.get(key)
    return val if isinstance(val, bool) else fallback


def _as_float(data: dict[str, Any], key: str, fallback: float) -> float:
    val = data.get(key)
    if isinstance(val, (int, float)) and not isinstance(val, bool):
        return float(val)
    if isinstance(val, str):
        try:
            return float(val)
        except ValueError:
            pass
    return fallback


@dataclass
class Config:
    play_intro: bool = True
    loop_background: bool = True
    fade_start_seconds: float = 12.93
    fade_duration_seconds: float = 1.2
    residual_alpha: float = 0.25
    intro_volume: float = 1.0
    show_subtitles: bool = False
    subtitle_fade_seconds: float = 0.5
    subtitle_width_ratio: float = 0.6
    subtitles: list[Subtitle] = field(default_factory=_default_subtitles)

    @staticmethod
    def config_dir() -> Path:
        base = Path(os.environ.get("XDG_CONFIG_HOME") or (Path.home() / ".config"))
        return base / APP_SLUG

    @staticmethod
    def file_path() -> Path:
        return Config.config_dir() / "config.json"

    @classmethod
    def load(cls) -> "Config":
        cfg = cls()
        path = cls.file_path()
        try:
            if path.exists():
                raw = json.loads(path.read_text(encoding="utf-8"))
                if isinstance(raw, dict):
                    legacy_enabled = raw.get("enabled") if isinstance(raw.get("enabled"), bool) else None
                    cfg.play_intro = _as_bool(
                        raw,
                        "playIntro",
                        legacy_enabled if legacy_enabled is not None else cfg.play_intro,
                    )
                    cfg.loop_background = _as_bool(raw, "loopBackground", cfg.loop_background)
                    cfg.fade_start_seconds = max(0.0, _as_float(raw, "fadeStartSeconds", cfg.fade_start_seconds))
                    cfg.fade_duration_seconds = max(0.05, _as_float(raw, "fadeDurationSeconds", cfg.fade_duration_seconds))
                    cfg.residual_alpha = _clamp(_as_float(raw, "residualAlpha", cfg.residual_alpha), 0.0, 1.0)
                    cfg.intro_volume = _clamp(_as_float(raw, "introVolume", cfg.intro_volume), 0.0, 1.0)
                    cfg.show_subtitles = _as_bool(raw, "showSubtitles", cfg.show_subtitles)
                    cfg.subtitle_fade_seconds = max(0.01, _as_float(raw, "subtitleFadeSeconds", cfg.subtitle_fade_seconds))
                    cfg.subtitle_width_ratio = _clamp(
                        _as_float(raw, "subtitleWidthRatio", cfg.subtitle_width_ratio), 0.1, 0.95
                    )
                    subs_raw = raw.get("subtitles")
                    if isinstance(subs_raw, list):
                        parsed: list[Subtitle] = []
                        for item in subs_raw:
                            if not isinstance(item, dict) or "text" not in item:
                                continue
                            parsed.append(
                                Subtitle(
                                    text=str(item["text"]),
                                    start=_as_float(item, "start", 0.0),
                                    end=_as_float(item, "end", 0.0),
                                )
                            )
                        cfg.subtitles = parsed
        except Exception as exc:
            log(f"设置文件读取失败，使用默认值：{exc}")
        cfg.save()
        return cfg

    def to_dict(self) -> dict[str, Any]:
        return {
            "fadeDurationSeconds": round(self.fade_duration_seconds, 3),
            "fadeStartSeconds": round(self.fade_start_seconds, 3),
            "introVolume": round(self.intro_volume, 3),
            "loopBackground": bool(self.loop_background),
            "playIntro": bool(self.play_intro),
            "residualAlpha": round(self.residual_alpha, 3),
            "showSubtitles": bool(self.show_subtitles),
            "subtitleFadeSeconds": round(self.subtitle_fade_seconds, 3),
            "subtitleWidthRatio": round(self.subtitle_width_ratio, 3),
            "subtitles": [
                {"end": round(s.end, 3), "start": round(s.start, 3), "text": s.text}
                for s in self.subtitles
            ],
        }

    def save(self) -> None:
        path = self.file_path()
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            text = json.dumps(self.to_dict(), indent=2, sort_keys=True, ensure_ascii=False) + "\n"
            tmp = path.with_suffix(".json.tmp")
            tmp.write_text(text, encoding="utf-8")
            tmp.replace(path)
        except Exception as exc:
            log(f"设置文件写入失败：{exc}")
