#!/usr/bin/env python3
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageStat


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
LE_CAMPAIGN_GATES = (
    "1101",
    "1102",
    "1103",
    "1104",
    "1105",
    "1106",
    "1111",
    "1112",
    "1113",
    "1114",
    "1115",
    "1116",
)
FORMER_PLACEHOLDER_KEYS = (
    "mokubakaiba",
    "bonaparte",
    "wingedkuriboh",
    "professorbanner",
    "atticusrhodesunmasked",
    "lazar",
    "rallydawson",
    "tenzenyanagi",
    "in4m8",
    "jakob",
    "torimeadows",
    "marin",
    "henriettaakaba",
    "leoakaba",
    "rayakaba",
    "rileyakaba",
    "infn8",
    "shadi",
    "solomonmuto",
    "tristantaylor",
)
EXPECTED_NAMES = {
    "in4m8": "The Vagabond",
    "infn8": "Tour Guide",
}


def load_solo():
    data = json.loads((DATA_ROOT / "Solo.json").read_text())
    return data["res"][0][1]["Master"]["Solo"]


def load_atlas_entries():
    entries = {}
    current_name = None
    for line in (DATA_ROOT / "ClientData" / "LinkEvolution" / "chars.dfymoo").read_text().splitlines():
        if line.startswith("n "):
            current_name = line[2:].strip()
        elif line.startswith("s ") and current_name:
            entries[current_name] = tuple(map(int, line.split()[1:5]))
            current_name = None
    return entries


def load_char_names():
    text = (DATA_ROOT / "ClientData" / "LinkEvolution" / "CharNames.json").read_text()
    return dict(re.findall(r'"([^"]+)"\s*:\s*"([^"]*)"', text))


def used_campaign_portraits(solo):
    used = set()
    for gate in LE_CAMPAIGN_GATES:
        for chapter in solo["chapter"][gate].values():
            for key in (chapter.get("p1_img"), chapter.get("p2_img")):
                if key:
                    used.add(key)
    return used


def looks_like_placeholder(crop):
    quantized = crop.resize((48, 58)).quantize(colors=16)
    colors = quantized.getcolors(48 * 58)
    top_color_ratio = max(count for count, _ in colors) / (48 * 58)
    avg_stddev = sum(ImageStat.Stat(crop).stddev) / 3
    return top_color_ratio > 0.70 or avg_stddev < 18


def validate():
    solo = load_solo()
    entries = load_atlas_entries()
    names = load_char_names()
    used = used_campaign_portraits(solo)
    atlas = Image.open(DATA_ROOT / "ClientData" / "LinkEvolution" / "chars.png").convert("RGB")

    missing_entries = sorted(key for key in used if key not in entries)
    if missing_entries:
        raise AssertionError(f"campaign portraits missing atlas entries: {missing_entries}")

    missing_names = sorted(key for key in used if key not in names)
    if missing_names:
        raise AssertionError(f"campaign portraits missing CharNames entries: {missing_names}")

    for key, expected_name in EXPECTED_NAMES.items():
        if names.get(key) != expected_name:
            raise AssertionError(f"{key} should be named {expected_name!r}, got {names.get(key)!r}")

    placeholder_hits = []
    for key in FORMER_PLACEHOLDER_KEYS:
        if key not in used:
            continue
        x, y, width, height = entries[key]
        crop = atlas.crop((x, y, x + width, y + height))
        if looks_like_placeholder(crop):
            placeholder_hits.append(key)
    if placeholder_hits:
        raise AssertionError(f"former placeholder portraits still look placeholder-like: {placeholder_hits}")


def main():
    validate()
    print("Campaign portrait validation passed")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as exc:
        print(f"Campaign portrait validation failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
