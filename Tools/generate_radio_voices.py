"""
Offline CPU TTS generator for PROTOCOL ZERO tactical radio lines using Kokoro-82M ONNX.
Outputs normalized 24kHz mono 16-bit PCM WAV clips under Assets/Resources/VoiceRadio/
named by deterministic FNV-1a hashes matching StoryDirector.GetVoiceClipKey().
"""

from __future__ import annotations

import os
import re
import urllib.request
from pathlib import Path
from typing import Dict, List, Tuple

import numpy as np
import soundfile as sf
from kokoro_onnx import Kokoro

REPO_ROOT = Path(__file__).resolve().parent.parent
STORY_PATH = REPO_ROOT / "Assets" / "Scripts" / "Core" / "StoryDirector.cs"
OUTPUT_DIR = REPO_ROOT / "Assets" / "Resources" / "VoiceRadio"

CACHE_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "kokoro-onnx"
MODEL_PATH = CACHE_DIR / "kokoro-v1.0.int8.onnx"
VOICES_PATH = CACHE_DIR / "voices-v1.0.bin"

MODEL_URL = (
    "https://github.com/thewh1teagle/kokoro-onnx/releases/download/"
    "model-files-v1.0/kokoro-v1.0.int8.onnx"
)
VOICES_URL = (
    "https://github.com/thewh1teagle/kokoro-onnx/releases/download/"
    "model-files-v1.0/voices-v1.0.bin"
)

VOICE_PROFILES: Dict[str, Tuple[str, float]] = {
    "VANCE": ("am_michael", 1.06),
    "MAJ. ROURKE": ("am_fenrir", 1.02),
    "KOZLOWSKI": ("am_adam", 1.07),
    "CPL. REYES": ("af_sarah", 1.08),
    "DIR. CHEN": ("bm_lewis", 1.02),
    "AEGIS-SATCOM": ("am_eric", 1.05),
    "CAPT. DRAGIC": ("bm_george", 1.01),
    "DIR. KASTNER": ("bf_emma", 1.03),
    "VOLKOV": ("bm_george", 1.02),
    "COL. RADEK": ("bm_daniel", 1.00),
    "VALKYRIE-6": ("af_nicole", 1.08),
    "TRAUMA-NET": ("af_heart", 1.12),
    "TAC-NET": ("am_eric", 1.07),
    "WRAITH-2": ("am_adam", 1.07),
}


def fnv1a_32_key(callsign: str, body: str) -> str:
    c = (callsign or "").strip()
    b = (body or "").strip()
    data = f"{c}|{b}".encode("utf-8")
    h = 0x811C9DC5
    for byte in data:
        h ^= byte
        h = (h * 0x01000193) & 0xFFFFFFFF

    slug = re.sub(r"[^a-z0-9]+", "_", c.lower()).strip("_") or "radio"
    return f"vo_{slug}_{h:08x}"


def normalize_tts_speech(text: str) -> str:
    t = text
    # Remove keycap brackets like [E], [Q], [Z/X/C], [E/CLOSE] for natural spoken cadence
    t = re.sub(r"\s*\[[^\]]+\]", "", t)
    t = t.replace("—", ", ").replace("--", ", ")
    replacements = [
        ("C4", "C-4"),
        ("K4-09", "K four zero nine"),
        ("ROE-9", "R O E nine"),
        ("ROE", "rules of engagement"),
        ("IFF", "I F F"),
        ("QRF", "Q R F"),
        ("EW", "electronic warfare"),
        ("K.I.A.", "K I A"),
        ("KIA", "K I A"),
        ("HVT", "high value target"),
        ("ATAK", "A-TAK"),
        ("BPM", "B P M"),
        ("Cpl.", "Corporal"),
        ("CPL.", "Corporal"),
        ("Maj.", "Major"),
        ("MAJ.", "Major"),
        ("Col.", "Colonel"),
        ("COL.", "Colonel"),
        ("Capt.", "Captain"),
        ("CAPT.", "Captain"),
        ("Dir.", "Director"),
        ("DIR.", "Director"),
        ("155mm", "155 millimeter"),
        ("154 MHz", "154 megahertz"),
        ("AEON", "Aeon"),
        ("SATCOM", "sat-com"),
        ("JSOC", "J-sock"),
        ("HELIOS", "Helios"),
        ("Valkyrie-6", "Valkyrie six"),
        ("VALKYRIE-6", "Valkyrie six"),
        ("Nomad-1", "Nomad one"),
        ("NOMAD-1", "Nomad one"),
        ("Ironwood-1", "Ironwood one"),
        ("Ironwood-One", "Ironwood one"),
        ("Wraith-2", "Wraith two"),
        ("Spark-3", "Spark three"),
        ("AEGIS-ACTUAL", "Aegis actual"),
        ("APEX-01", "Apex zero one"),
        ("KESTREL-01", "Kestrel zero one"),
        ("Goliath-02", "Goliath zero two"),
        ("Warden-01", "Warden zero one"),
        ("Phantom-04", "Phantom zero four"),
        ("Vanguard-09", "Vanguard zero nine"),
        ("Reaper-04 ISR", "Reaper zero four I S R"),
        ("ISR", "I S R"),
        ("Train #774", "Train seven seven four"),
        ("#774", "seven seven four"),
        ("0s", "zero seconds"),
        ("20s", "twenty seconds"),
    ]
    for src, dst in replacements:
        t = t.replace(src, dst)
    t = re.sub(r"\s{2,}", " ", t).strip()
    return t


def normalize_voice_callsign(speaker: str) -> str:
    s = (speaker or "").strip().upper()
    if "VANCE" in s:
        return "VANCE"
    if "ROURKE" in s:
        return "MAJ. ROURKE"
    if "KOZLOWSKI" in s:
        return "KOZLOWSKI"
    if "WRAITH-2" in s:
        return "WRAITH-2"
    if "REYES" in s:
        return "CPL. REYES"
    if "CHEN" in s:
        return "DIR. CHEN"
    if "AEGIS-SATCOM" in s or "SATCOM" in s:
        return "AEGIS-SATCOM"
    if "DRAGIC" in s:
        return "CAPT. DRAGIC"
    if "KASTNER" in s:
        return "DIR. KASTNER"
    if "VOLKOV" in s:
        return "VOLKOV"
    if "RADEK" in s:
        return "COL. RADEK"
    if "VALKYRIE" in s:
        return "VALKYRIE-6"
    if "TRAUMA" in s:
        return "TRAUMA-NET"
    return "TAC-NET"


def extract_story_lines() -> List[Tuple[str, str]]:
    text = STORY_PATH.read_text(encoding="utf-8")
    lines: List[Tuple[str, str]] = []

    # 1. All literal RadioLine("CALLSIGN", "CHANNEL", "BODY", ...)
    for m in re.finditer(
        r'new\s+RadioLine\(\s*"([^"]+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]+)"',
        text,
    ):
        lines.append((m.group(1), m.group(3)))

    # 2. All direct string-literal Transmit("CALLSIGN", "CHANNEL", "BODY", ...)
    for m in re.finditer(
        r'Transmit\(\s*"([^"]+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]+)"',
        text,
    ):
        lines.append((m.group(1), m.group(3)))

    # 3. All literal CutsceneBeat(..., "SPEAKER", "CAPTION", ...)
    for m in re.finditer(
        r'new\s+CutsceneBeat\((?:(?!new\s+CutsceneBeat)[^;])+?,\s*"([^"]+)"\s*,\s*"([^"]+)"',
        text,
        re.DOTALL,
    ):
        lines.append((normalize_voice_callsign(m.group(1)), m.group(2)))

    # 4. External caller string literals from SquadDirector & PlayerController
    lines.extend(
        [
            ("WRAITH-2", "Reaper-04 ISR Drone now sweeping hostile transponders on ATAK grid."),
            ("TAC-NET", "Reaper-04 ISR Drone now sweeping hostile transponders on ATAK grid."),
            ("WRAITH-2", "155mm Battery cleared for Fire Mission [F]!"),
            ("TAC-NET", "155mm Battery cleared for Fire Mission [F]!"),
            ("VANCE", "Closing diamond formation on Lead."),
            ("VANCE", "Locking perimeter firing sectors."),
            ("VANCE", "Stacking up, weapons free ahead."),
        ]
    )

    # Deduplicate preserving order
    seen = set()
    unique: List[Tuple[str, str]] = []
    for callsign, body in lines:
        key = fnv1a_32_key(callsign, body)
        if key not in seen:
            seen.add(key)
            unique.append((callsign, body))
    return unique


def download_if_missing(url: str, dst: Path) -> None:
    if dst.exists() and dst.stat().st_size > 1024:
        return
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_suffix(dst.suffix + ".part")
    print(f"Downloading {url} -> {dst} ...")
    urllib.request.urlretrieve(url, tmp)
    tmp.replace(dst)


def trim_and_normalize(samples: np.ndarray, threshold: float = 0.012) -> np.ndarray:
    arr = np.asarray(samples, dtype=np.float32).flatten()
    if arr.size == 0:
        return arr
    above = np.where(np.abs(arr) > threshold)[0]
    if above.size > 0:
        start = max(0, int(above[0]) - 800)
        end = min(arr.size, int(above[-1]) + 1200)
        arr = arr[start:end]
    peak = float(np.max(np.abs(arr))) if arr.size else 0.0
    if peak > 1e-4:
        arr = arr * (0.84 / peak)
    return np.clip(arr, -0.98, 0.98)


def main() -> None:
    download_if_missing(MODEL_URL, MODEL_PATH)
    download_if_missing(VOICES_URL, VOICES_PATH)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    kokoro = Kokoro(str(MODEL_PATH), str(VOICES_PATH))
    lines = extract_story_lines()
    valid_filenames = {f"{fnv1a_32_key(c, b)}.wav" for c, b in lines}

    removed = 0
    for existing in list(OUTPUT_DIR.glob("vo_*.wav")):
        if existing.name not in valid_filenames:
            existing.unlink(missing_ok=True)
            meta = existing.with_suffix(".wav.meta")
            if meta.exists():
                meta.unlink(missing_ok=True)
            removed += 1

    print(f"Found {len(lines)} unique tactical radio lines (pruned {removed} obsolete). Synthesizing...")

    generated = 0
    for idx, (callsign, body) in enumerate(lines, start=1):
        key = fnv1a_32_key(callsign, body)
        out_path = OUTPUT_DIR / f"{key}.wav"
        if out_path.exists() and out_path.stat().st_size > 1024:
            continue

        voice, speed = VOICE_PROFILES.get(callsign.upper(), ("am_michael", 1.05))
        spoken = normalize_tts_speech(body)
        samples, sr = kokoro.create(spoken, voice=voice, speed=speed, lang="en-us")
        clean = trim_and_normalize(samples)
        sf.write(str(out_path), clean, sr, subtype="PCM_U8")
        generated += 1
        print(f"[{idx}/{len(lines)}] {key}.wav ({callsign} / {voice})")

    print(f"Done. Generated {generated} new WAV files in {OUTPUT_DIR}")


if __name__ == "__main__":
    main()
