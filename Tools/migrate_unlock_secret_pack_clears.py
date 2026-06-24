#!/usr/bin/env python3
"""
Retroactively complete pack-unlock solo chapters that were only partially cleared.

Before the single-win pack unlock patch, chapters with both loaner and own-deck
options required two wins (RENTAL_CLEAR + MYDECK_CLEAR) before unlock_secret fired.
This script promotes those partial clears to COMPLETE and unlocks the shop packs.
"""
import json
import re
import shutil
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
SOLO_JSON = DATA_ROOT / "Solo.json"
SHOP_JSON = DATA_ROOT / "Shop.json"
PLAYERS_DIR = DATA_ROOT / "Players"

OPEN = 0
RENTAL_CLEAR = 1
MYDECK_CLEAR = 2
COMPLETE = 3


def load_json_with_comments(path: Path):
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//.*?$", "", text, flags=re.M)
    return json.loads(text)


def load_solo_chapters():
    solo = load_json_with_comments(SOLO_JSON)["res"][0][1]["Master"]["Solo"]
    unlock_by_chapter: dict[int, list[int]] = {}
    for gate_chapters in solo["chapter"].values():
        for chapter_id, chapter in gate_chapters.items():
            secrets = chapter.get("unlock_secret")
            if secrets:
                unlock_by_chapter[int(chapter_id)] = list(secrets)
    return unlock_by_chapter


def load_pack_shop_ids():
    shop = load_json_with_comments(SHOP_JSON)
    pack_to_shop: dict[int, str] = {}
    for shop_id, entry in shop["PackShop"].items():
        pack_id = entry.get("packId")
        if pack_id is not None:
            pack_to_shop[int(pack_id)] = shop_id
    return pack_to_shop


def ensure_shop_unlock(shop_state: dict, shop_id: str, now: int):
    items = shop_state.setdefault("ShopItems", {})
    entry = items.setdefault(shop_id, {})
    if not entry.get("Unlocked"):
        entry["Unlocked"] = True
        entry["UnlockedTime"] = now
        entry.setdefault("IsNew", True)
        entry.setdefault("PurchaseCount", 0)
        entry.setdefault("UnlockedPurchaseCount", 0)
        return True
    return False


def migrate_player(path: Path, unlock_by_chapter: dict[int, list[int]], pack_to_shop: dict[int, str], apply: bool):
    player = load_json_with_comments(path)
    solo_chapters = player.get("SoloChapters") or {}
    shop_state = player.setdefault("ShopState", {"ShopItems": {}})
    shop_state.setdefault("ShopItems", {})

    changes: list[str] = []
    now = int(time.time())

    for gate_id, chapters in solo_chapters.items():
        for chapter_id, status in list(chapters.items()):
            cid = int(chapter_id)
            if status not in (RENTAL_CLEAR, MYDECK_CLEAR):
                continue
            pack_ids = unlock_by_chapter.get(cid)
            if not pack_ids:
                continue
            changes.append(f"chapter {cid}: status {status} -> {COMPLETE}")
            if apply:
                chapters[chapter_id] = COMPLETE
            for pack_id in pack_ids:
                shop_id = pack_to_shop.get(pack_id)
                if not shop_id:
                    changes.append(f"  missing shop mapping for packId {pack_id}")
                    continue
                if apply and ensure_shop_unlock(shop_state, shop_id, now):
                    changes.append(f"  unlocked shop pack {shop_id} (packId {pack_id})")

    if apply and changes:
        path.write_text(json.dumps(player, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return changes


def main():
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Write migrated player saves")
    args = parser.parse_args()

    unlock_by_chapter = load_solo_chapters()
    pack_to_shop = load_pack_shop_ids()

    player_files = sorted(PLAYERS_DIR.glob("*/Player.json"))
    if not player_files:
        print("No player saves found.")
        return 0

    total_changes = 0
    for player_path in player_files:
        if args.apply:
            backup = player_path.with_name(
                f"Player.json.bak-unlock-secret-{datetime.now().strftime('%Y%m%d-%H%M%S')}"
            )
            shutil.copy2(player_path, backup)
        changes = migrate_player(player_path, unlock_by_chapter, pack_to_shop, args.apply)
        if changes:
            print(f"{player_path.parent.name}:")
            for line in changes:
                print(f"  {line}")
            total_changes += len(changes)

    if total_changes == 0:
        print("No partial pack-unlock chapters found.")
    elif not args.apply:
        print("\nDry run only. Re-run with --apply to update player saves.")
    else:
        print("\nMigration applied. Restart YgoMaster.exe.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())