#!/usr/bin/env python3
"""Validate LE extraRewards match opponent decks in SoloDuels."""
import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
SOLO_JSON = DATA_ROOT / "Solo.json"
SOLO_DUELS = DATA_ROOT / "SoloDuels"
CARD_LIST = DATA_ROOT / "CardList.json"

LE_GATES = [1100, *range(1101, 1107), *range(1111, 1117), 1121]
EXPECTED_WIN_ENTRIES = 3
EXPECTED_RATE = 100
EXPECTED_CARD_OWNED_LIMIT = 3


def load_solo():
    data = json.loads(SOLO_JSON.read_text(encoding="utf-8"))
    return data["res"][0][1]["Master"]["Solo"]


def expected_opponent_ids(chapter_id: int) -> list[int]:
    duel = json.loads((SOLO_DUELS / f"{chapter_id}.json").read_text(encoding="utf-8"))["Duel"]
    opponent = duel["Deck"][1]
    ids = []
    for zone in ("Main", "Extra", "Side"):
        ids.extend(opponent.get(zone, {}).get("CardIds", []))
    return sorted(set(ids))


def validate():
    solo = load_solo()
    cardlist = set(json.loads(CARD_LIST.read_text(encoding="utf-8")).keys())
    errors = []
    checked = 0

    for gate in LE_GATES:
        gate_key = str(gate)
        if gate_key not in solo["chapter"]:
            errors.append(f"missing gate {gate_key} in Solo.json")
            continue
        for chapter_id, chapter in solo["chapter"][gate_key].items():
            checked += 1
            cid = int(chapter_id)
            extra = chapter.get("extraRewards")
            if extra is None:
                errors.append(f"{chapter_id}: missing extraRewards")
                continue
            win = extra.get("win")
            if not isinstance(win, list) or len(win) != EXPECTED_WIN_ENTRIES:
                errors.append(
                    f"{chapter_id}: expected {EXPECTED_WIN_ENTRIES} win entries, got "
                    f"{len(win) if isinstance(win, list) else type(win).__name__}"
                )
                continue
            expected_ids = expected_opponent_ids(cid)
            for index, entry in enumerate(win):
                if entry.get("type") != "Card":
                    errors.append(f"{chapter_id}: win[{index}] type is not Card")
                if entry.get("rate") != EXPECTED_RATE:
                    errors.append(f"{chapter_id}: win[{index}] rate is not {EXPECTED_RATE}")
                if entry.get("cardOwnedLimit") != EXPECTED_CARD_OWNED_LIMIT:
                    errors.append(
                        f"{chapter_id}: win[{index}] cardOwnedLimit is not {EXPECTED_CARD_OWNED_LIMIT}"
                    )
                ids = entry.get("ids")
                if ids != expected_ids:
                    errors.append(f"{chapter_id}: win[{index}] ids do not match opponent deck")
                if ids and len(ids) != len(set(ids)):
                    errors.append(f"{chapter_id}: win[{index}] ids are not deduplicated")
                for card_id in ids or []:
                    if str(card_id) not in cardlist:
                        errors.append(f"{chapter_id}: card {card_id} missing from CardList.json")

    if errors:
        for err in errors[:20]:
            print(f"ERROR: {err}", file=sys.stderr)
        if len(errors) > 20:
            print(f"ERROR: ... and {len(errors) - 20} more", file=sys.stderr)
        raise SystemExit(1)

    print(f"LE enemy deck reward validation passed ({checked} chapters)")


if __name__ == "__main__":
    try:
        validate()
    except AssertionError as exc:
        print(f"LE enemy deck reward validation failed: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc