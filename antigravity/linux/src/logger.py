# Copyright (C) 2026 wynneceles
# This program is free software under the GNU General Public License v3.

from __future__ import annotations

import datetime
import os
import threading
from pathlib import Path

APP_SLUG = "antigravity-boot-animation"
_LOCK = threading.Lock()
_MAX_LOG_BYTES = 2 * 1024 * 1024


def state_dir() -> Path:
    base = Path(os.environ.get("XDG_STATE_HOME") or (Path.home() / ".local" / "state"))
    return base / APP_SLUG


def log_path() -> Path:
    return state_dir() / "log.txt"


def log(msg: str) -> None:
    now = datetime.datetime.now()
    line = f"{now.strftime('%Y-%m-%d %H:%M:%S')}.{now.microsecond // 1000:03d} {msg}\n"
    path = log_path()
    with _LOCK:
        try:
            path.parent.mkdir(parents=True, exist_ok=True)
            if path.exists() and path.stat().st_size > _MAX_LOG_BYTES:
                backup = path.with_suffix(".log.old")
                try:
                    if backup.exists():
                        backup.unlink()
                    path.rename(backup)
                except OSError:
                    pass
            with path.open("a", encoding="utf-8") as fh:
                fh.write(line)
        except OSError:
            pass
