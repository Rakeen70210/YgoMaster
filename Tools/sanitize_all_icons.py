#!/usr/bin/env python3
"""
Sanitize all invalid icon IDs in SoloDuels JSON files.
Replaces any ID in the 'icon' array that is not in the ItemID.json 'ICON' list (and is not 0) with 0.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
SOLO_DUELS_DIR = DATA_ROOT / "SoloDuels"
ITEM_ID_PATH = DATA_ROOT / "ItemID.json"

def load_json_with_comments(path: Path):
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//.*?$", "", text, flags=re.M)
    text = re.sub(r",\s*([\]}])", r"\1", text)
    return json.loads(text)

def main():
    if not ITEM_ID_PATH.exists():
        print(f"Error: {ITEM_ID_PATH} does not exist.")
        return 1

    item_ids = load_json_with_comments(ITEM_ID_PATH)
    valid_icons = set(item_ids.get("ICON", []))
    valid_icons.add(0)

    count_files_fixed = 0
    count_icons_fixed = 0

    for json_path in sorted(SOLO_DUELS_DIR.glob("*.json")):
        try:
            with open(json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            print(f"Error reading {json_path.name}: {e}")
            continue

        duel = data.get("Duel")
        if not duel:
            continue

        icons = duel.get("icon")
        if not icons:
            continue

        modified = False
        new_icons = []
        for idx, icon in enumerate(icons):
            if icon not in valid_icons:
                print(f"Sanitizing invalid icon {icon} in {json_path.name} at index {idx}")
                new_icons.append(0)
                count_icons_fixed += 1
                modified = True
            else:
                new_icons.append(icon)

        if modified:
            duel["icon"] = new_icons
            with open(json_path, "w", encoding="utf-8") as f:
                json.dump(data, f, indent=2, ensure_ascii=False)
            count_files_fixed += 1

    print(f"\nSanitized {count_icons_fixed} invalid icons across {count_files_fixed} files.")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
