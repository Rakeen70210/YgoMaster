#!/usr/bin/env python3
"""
Inject per-chapter extraRewards into Data/Solo.json for Link Evolution solo duels.

Each LE win grants up to 3 random cards from the opponent deck (Deck[1] in SoloDuels),
including repeat wins on already-completed chapters. Cards you already own 3+ copies
of are excluded from the drop pool (cardOwnedLimit: 3).

Pipeline order (run this LAST after any tool that rewrites Solo.json):
  import_requiem_* / linearize_requiem_campaign_layout.py
    -> inject_le_enemy_deck_rewards.py --apply
    -> validate_le_enemy_deck_rewards.py
    -> restart YgoMaster.exe
"""
import argparse
import json
import shutil
import sys
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
SOLO_JSON = DATA_ROOT / "Solo.json"
SOLO_DUELS = DATA_ROOT / "SoloDuels"

LE_GATES = [1100, *range(1101, 1107), *range(1111, 1117), 1121]
CARD_REWARD_COUNT = 3
CARD_REWARD_RATE = 100
CARD_OWNED_LIMIT = 3


def load_solo_payload():
    data = json.loads(SOLO_JSON.read_text(encoding="utf-8"))
    return data, data["res"][0][1]["Master"]["Solo"]


def save_solo_payload(data):
    SOLO_JSON.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def opponent_card_ids(chapter_id: int) -> list[int]:
    duel_path = SOLO_DUELS / f"{chapter_id}.json"
    if not duel_path.exists():
        raise FileNotFoundError(f"missing SoloDuels file for chapter {chapter_id}: {duel_path}")
    duel = json.loads(duel_path.read_text(encoding="utf-8"))["Duel"]
    decks = duel.get("Deck", [])
    if len(decks) < 2:
        raise ValueError(f"chapter {chapter_id}: expected 2 decks, found {len(decks)}")
    opponent = decks[1]
    ids: list[int] = []
    for zone in ("Main", "Extra", "Side"):
        zone_data = opponent.get(zone, {})
        ids.extend(zone_data.get("CardIds", []))
    unique = sorted(set(ids))
    if not unique:
        raise ValueError(f"chapter {chapter_id}: opponent deck has no card IDs")
    return unique


def build_extra_rewards(card_ids: list[int]) -> dict:
    entry = {
        "type": "Card",
        "ids": card_ids,
        "rate": CARD_REWARD_RATE,
        "cardOwnedLimit": CARD_OWNED_LIMIT,
    }
    return {"win": [dict(entry) for _ in range(CARD_REWARD_COUNT)]}


def iter_le_chapters(solo: dict):
    chapters = solo["chapter"]
    for gate in LE_GATES:
        gate_key = str(gate)
        if gate_key not in chapters:
            continue
        for chapter_id in chapters[gate_key]:
            yield int(chapter_id), chapters[gate_key][chapter_id]


def inject(dry_run: bool) -> dict:
    data, solo = load_solo_payload()
    stats = {
        "updated": 0,
        "unchanged": 0,
        "errors": [],
        "gates": LE_GATES,
    }

    for chapter_id, chapter in iter_le_chapters(solo):
        try:
            card_ids = opponent_card_ids(chapter_id)
            desired = build_extra_rewards(card_ids)
            current = chapter.get("extraRewards")
            if current == desired:
                stats["unchanged"] += 1
                continue
            if not dry_run:
                chapter["extraRewards"] = desired
            stats["updated"] += 1
        except Exception as exc:
            stats["errors"].append(f"{chapter_id}: {exc}")

    if stats["errors"]:
        for line in stats["errors"]:
            print(f"ERROR: {line}", file=sys.stderr)
        raise SystemExit(1)

    if dry_run:
        print(
            f"Dry run: would update {stats['updated']} chapters, "
            f"{stats['unchanged']} already correct"
        )
        return stats

    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = SOLO_JSON.with_name(f"Solo.json.bak-enemy-rewards-{stamp}")
    shutil.copy2(SOLO_JSON, backup)
    save_solo_payload(data)
    print(f"Updated {stats['updated']} chapters ({stats['unchanged']} unchanged)")
    print(f"Backup: {backup}")
    return stats


def main():
    parser = argparse.ArgumentParser(
        description="Inject LE enemy-deck card rewards into Solo.json extraRewards."
    )
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--dry-run", action="store_true", help="Report changes without writing")
    group.add_argument("--apply", action="store_true", help="Write Solo.json (creates backup)")
    args = parser.parse_args()
    inject(dry_run=args.dry_run)


if __name__ == "__main__":
    main()