#!/usr/bin/env python3
import argparse
import importlib.util
import json
import re
import shutil
from collections import defaultdict
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
DEFAULT_REQUIEM = Path("/tmp/requiem-parsed.json")
DEFAULT_DECKS = Path("/tmp/requiem-extract-core/decks")
DEFAULT_LOTD_YDK_IDS = Path("/tmp/Lotd-src/Lotd/YdkIds.txt")

STORY_IMPORTER_PATH = Path(__file__).with_name("import_requiem_optional_duels.py")
spec = importlib.util.spec_from_file_location("requiem_story_importer", STORY_IMPORTER_PATH)
story = importlib.util.module_from_spec(spec)
spec.loader.exec_module(story)

SERIES_TO_CHALLENGE_GATE = {
    0: 1111,
    1: 1112,
    2: 1113,
    3: 1114,
    4: 1115,
    5: 1116,
    -1: 1112,
}

BASE_CHALLENGE_MAX = {
    1111: 11110024,
    1112: 11120027,
    1113: 11130029,
    1114: 11140028,
    1115: 11150031,
    1116: 11160019,
}

GATE_TEMPLATES = {
    1111: 11110001,
    1112: 11120001,
    1113: 11130001,
    1114: 11140001,
    1115: 11150001,
    1116: 11160001,
}

SERIES_LABELS = {
    0: "Yu-Gi-Oh!",
    1: "GX",
    2: "5D's",
    3: "ZEXAL",
    4: "ARC-V",
    5: "VRAINS",
    -1: "GX",
}

DECK_FILE_ALIASES = {
    "requiem137_chazz_hard": "requiem136_chazz_hard",
}


def select_requiem_challenge_decks(decks):
    return sorted(
        [deck for deck in decks if re.match(r"^requiem\d+_.*_hard$", deck.get("file", ""))],
        key=lambda deck: deck["id"],
    )


def challenge_gate_for_series(series):
    if series not in SERIES_TO_CHALLENGE_GATE:
        raise ValueError(f"Unsupported Requiem challenge series {series}")
    return SERIES_TO_CHALLENGE_GATE[series]


def resolve_deck_file(deck_file, decks_dir=DEFAULT_DECKS):
    path = decks_dir / f"{deck_file}.ydc"
    if path.exists():
        return path
    alias = DECK_FILE_ALIASES.get(deck_file)
    if alias:
        return decks_dir / f"{alias}.ydc"
    return path


def empty_deck():
    return {
        "Main": {"CardIds": [], "Rare": []},
        "Extra": {"CardIds": [], "Rare": []},
        "Side": {"CardIds": [], "Rare": []},
    }


def deck_json(deck):
    return {
        section: {
            "CardIds": cards,
            "Rare": [1] * len(cards),
        }
        for section, cards in deck.items()
    }


def build_challenge_duel(chapter_id, template, opponent_name, opponent_deck):
    duel = {
        "Duel": {
            **{key: value for key, value in template.items() if key != "Deck"},
            "Deck": [empty_deck(), deck_json(opponent_deck)],
            "chapter": chapter_id,
            "name": ["", opponent_name],
        }
    }
    return duel


def load_solo():
    data = json.loads((DATA_ROOT / "Solo.json").read_text())
    return data, data["res"][0][1]["Master"]["Solo"]


def validate_required_paths(args):
    required = [
        args.requiem_json,
        args.decks_dir,
        args.lotd_ydk_ids,
        DATA_ROOT / "Solo.json",
        DATA_ROOT / "YdkIds.txt",
        DATA_ROOT / "CardList.json",
        DATA_ROOT / "ClientData" / "IDS" / "IDS_SOLO.txt",
    ]
    missing = [str(path) for path in required if not Path(path).exists()]
    if missing:
        raise FileNotFoundError("Missing required files:\n" + "\n".join(missing))


def next_ids_by_gate(solo):
    return {gate: max(int(chapter_id) for chapter_id in solo["chapter"][str(gate)]) + 1 for gate in BASE_CHALLENGE_MAX}


def build_description(deck_meta, char_meta):
    label = SERIES_LABELS.get(deck_meta["series"], "Link Evolution")
    return (
        f"Requiem Challenge: {char_meta['name']}\n\n"
        f"Face {char_meta['name']} with the Requiem challenge Deck \"{deck_meta['name']}\". "
        f"This optional {label} challenge uses your own Deck against a harder opponent list from the Requiem mod.\n\n"
        f"Challenge Deck: {deck_meta['name']}"
    )


def build_import(args):
    validate_required_paths(args)
    requiem = json.loads(args.requiem_json.read_text())
    chars_by_id = {char["id"]: char for char in requiem["chars"]}
    challenge_decks = select_requiem_challenge_decks(requiem["decks"])
    le_to_passcode = {le_id: passcode for passcode, le_id in story.load_pair_map(args.lotd_ydk_ids).items()}
    passcode_to_md = story.load_pair_map(DATA_ROOT / "YdkIds.txt")
    card_list = set(json.loads((DATA_ROOT / "CardList.json").read_text()))
    solo_data, solo = load_solo()
    next_ids = next_ids_by_gate(solo)
    added = []
    missing_cards = []
    substitutions = []

    for deck_meta in challenge_decks:
        gate = challenge_gate_for_series(deck_meta["series"])
        chapter_id = next_ids[gate]
        next_ids[gate] += 1
        char_meta = chars_by_id[deck_meta["owner"]]
        raw_deck_path = resolve_deck_file(deck_meta["file"], args.decks_dir)
        raw_deck = story.parse_ydc(raw_deck_path)
        raw_passcodes = [
            le_to_passcode.get(card)
            for section_cards in raw_deck.values()
            for card in section_cards
        ]
        substitutions.extend(
            {
                "deck_id": deck_meta["id"],
                "chapter_id": chapter_id,
                "deck_file": deck_meta["file"],
                "source_deck_file": raw_deck_path.stem,
                "from_passcode": passcode,
                "to_passcode": story.PASSCODE_SUBSTITUTIONS[passcode],
            }
            for passcode in raw_passcodes
            if passcode in story.PASSCODE_SUBSTITUTIONS
        )
        converted, missing = story.convert_deck(raw_deck, le_to_passcode, passcode_to_md)
        missing.extend(
            {"le_id": int(card), "passcode": None, "reason": "missing_cardlist"}
            for section_cards in converted.values()
            for card in section_cards
            if str(card) not in card_list
        )
        if missing:
            missing_cards.append(
                {
                    "deck_id": deck_meta["id"],
                    "chapter_id": chapter_id,
                    "deck_file": deck_meta["file"],
                    "missing": missing,
                }
            )

        template_id = GATE_TEMPLATES[gate]
        template = json.loads((DATA_ROOT / "SoloDuels" / f"{template_id}.json").read_text())["Duel"]
        added.append(
            {
                "deck_id": deck_meta["id"],
                "chapter_id": chapter_id,
                "gate": gate,
                "title": deck_meta["name"],
                "character": char_meta["name"],
                "character_code": char_meta["code"],
                "deck_file": deck_meta["file"],
                "source_deck_file": raw_deck_path.stem,
                "chapter": {
                    "parent_chapter": chapter_id - 1 if chapter_id - 1 >= BASE_CHALLENGE_MAX[gate] else 0,
                    "unlock_id": 0,
                    "npc_id": 1,
                    "begin_sn": "",
                    "set_id": 0,
                    "mydeck_set_id": 2,
                },
                "duel_json": build_challenge_duel(chapter_id, template, char_meta["name"], converted),
                "ids_text": build_description(deck_meta, char_meta),
            }
        )

    return {
        "solo_data": solo_data,
        "solo": solo,
        "added": added,
        "missing_cards": missing_cards,
        "substitutions": substitutions,
    }


def backup_files(paths):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_dir = DATA_ROOT / "Backups" / f"requiem-challenge-decks-{stamp}"
    backup_dir.mkdir(parents=True, exist_ok=False)
    for path in paths:
        rel = path.relative_to(ROOT)
        dest = backup_dir / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, dest)
    return backup_dir


def apply_import(plan, force=False):
    ids_path = DATA_ROOT / "ClientData" / "IDS" / "IDS_SOLO.txt"
    solo_path = DATA_ROOT / "Solo.json"
    duel_dir = DATA_ROOT / "SoloDuels"
    if plan["missing_cards"]:
        raise RuntimeError("Refusing to apply: at least one Requiem challenge deck has unmapped cards")

    existing_ids_text = ids_path.read_text()
    collisions = []
    for item in plan["added"]:
        chapter_id = str(item["chapter_id"])
        if (duel_dir / f"{chapter_id}.json").exists():
            collisions.append(str(duel_dir / f"{chapter_id}.json"))
        if f"[IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION]" in existing_ids_text:
            collisions.append(f"IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION")
    if collisions and not force:
        raise RuntimeError("Refusing to overwrite existing challenge import targets:\n" + "\n".join(collisions))

    backup_dir = backup_files([solo_path, ids_path])
    grouped = defaultdict(list)
    for item in plan["added"]:
        grouped[item["gate"]].append(item)
        (duel_dir / f"{item['chapter_id']}.json").write_text(json.dumps(item["duel_json"], indent=2) + "\n")

    for gate, items in grouped.items():
        chapters = plan["solo"]["chapter"][str(gate)]
        for item in items:
            chapters[str(item["chapter_id"])] = item["chapter"]

    solo_path.write_text(json.dumps(plan["solo_data"], indent=2) + "\n")
    with ids_path.open("a") as handle:
        if not existing_ids_text.endswith("\n"):
            handle.write("\n")
        handle.write("\n")
        for item in plan["added"]:
            handle.write(f"[IDS_SOLO.CHAPTER{item['chapter_id']}_EXPLANATION]\n")
            handle.write(item["ids_text"])
            handle.write("\n")
    return backup_dir


def write_report(plan, report_path):
    payload = {
        "added_count": len(plan["added"]),
        "added": [
            {
                key: item[key]
                for key in ("deck_id", "chapter_id", "gate", "title", "character", "character_code", "deck_file", "source_deck_file")
            }
            for item in plan["added"]
        ],
        "missing_cards": plan["missing_cards"],
        "substitutions": plan["substitutions"],
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(payload, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description="Import Requiem challenge decks into YgoMaster LE challenge gates.")
    parser.add_argument("--requiem-json", type=Path, default=DEFAULT_REQUIEM)
    parser.add_argument("--decks-dir", type=Path, default=DEFAULT_DECKS)
    parser.add_argument("--lotd-ydk-ids", type=Path, default=DEFAULT_LOTD_YDK_IDS)
    parser.add_argument("--report", type=Path, default=DATA_ROOT / "Backups" / "requiem-challenge-decks-plan-report.json")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    plan = build_import(args)
    write_report(plan, args.report)
    print(f"Prepared {len(plan['added'])} Requiem challenge decks")
    print(f"Missing card groups: {len(plan['missing_cards'])}")
    print(f"Card substitutions: {len(plan['substitutions'])}")
    print(f"Report: {args.report}")
    if args.apply:
        backup_dir = apply_import(plan, force=args.force)
        print(f"Applied import. Backup: {backup_dir}")
    else:
        print("Dry run only. Re-run with --apply to write challenge chapters.")


if __name__ == "__main__":
    main()
