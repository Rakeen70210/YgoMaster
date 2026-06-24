import argparse
import json
import re
import shutil
from collections import defaultdict
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
SOLO_PATH = DATA_ROOT / "Solo.json"
IDS_PATH = DATA_ROOT / "ClientData" / "IDS" / "IDS_SOLO.txt"
REPORT_PATH = ROOT / "Tools" / "manifests" / "requiem-optional-duels-report.json"
SOLO_DUELS_DIR = DATA_ROOT / "SoloDuels"
NON_DM_GATES = ("1102", "1103", "1104", "1105", "1106")


_VALID_ICONS_CACHE = None

def sanitize_icons(icons):
    global _VALID_ICONS_CACHE
    if _VALID_ICONS_CACHE is None:
        _VALID_ICONS_CACHE = {0}
        item_id_path = DATA_ROOT / "ItemID.json"
        if item_id_path.exists():
            try:
                text = item_id_path.read_text(encoding="utf-8")
                text_clean = re.sub(r"//.*?$", "", text, flags=re.M)
                text_clean = re.sub(r",\s*([\]}])", r"\1", text_clean)
                item_ids = json.loads(text_clean)
                _VALID_ICONS_CACHE.update(item_ids.get("ICON", []))
            except Exception:
                pass
    return [icon if icon in _VALID_ICONS_CACHE else 0 for icon in icons]


def reverse_chapter_id(gate, index):
    return int(f"{gate}01{index - 1:02d}")


def build_reverse_chapter(forward, forward_id):
    reverse = {
        key: value
        for key, value in forward.items()
        if key not in {"parent_chapter", "p1_img", "p2_img", "unlock_secret"}
    }
    reverse["parent_chapter"] = int(forward_id)
    reverse["p1_img"] = forward["p2_img"]
    reverse["p2_img"] = forward["p1_img"]
    return reverse


def build_reverse_duel(forward_duel, reverse_id):
    reverse = json.loads(json.dumps(forward_duel))
    duel = reverse["Duel"]
    duel["chapter"] = int(reverse_id)
    duel["name"] = list(reversed(duel.get("name", [])))
    duel["Deck"] = list(reversed(duel.get("Deck", [])))
    duel["icon"] = sanitize_icons(duel.get("icon", []))
    return reverse


def build_reverse_description(title, player_name, opponent_name):
    return (
        f"{title} (Reverse Duel)\n\n"
        f"Take {player_name}'s side in the alternate version of this story duel. "
        f"The same conflict now turns {opponent_name}'s plan into the obstacle, "
        f"letting the other duelist drive the pace and prove their point from across the field.\n\n"
        f"{player_name} vs. {opponent_name}"
    )


def load_solo(path=SOLO_PATH):
    data = json.loads(path.read_text())
    return data, data["res"][0][1]["Master"]["Solo"]


def insert_after_forward(chapters, forward_id, reverse_id, reverse_chapter):
    rebuilt = {}
    inserted = False
    for chapter_id, chapter in chapters.items():
        rebuilt[chapter_id] = chapter
        if chapter_id == str(forward_id):
            rebuilt[str(reverse_id)] = reverse_chapter
            inserted = True
    if not inserted:
        rebuilt[str(reverse_id)] = reverse_chapter
    chapters.clear()
    chapters.update(rebuilt)


def reverse_exists(chapters, forward_id, forward):
    for chapter_id, chapter in chapters.items():
        if chapter_id == str(forward_id):
            continue
        if (
            chapter.get("parent_chapter") == int(forward_id)
            and chapter.get("p1_img") == forward.get("p2_img")
            and chapter.get("p2_img") == forward.get("p1_img")
        ):
            return int(chapter_id)
    return None


def append_ids_entries(ids_text, entries):
    text = ids_text.rstrip() + "\n"
    for chapter_id, description in entries:
        header = f"[IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION]"
        if header in text:
            continue
        text += f"{header}\n{description}\n"
    return text


def backup_files(paths):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_dir = DATA_ROOT / "Backups" / f"requiem-non-dm-reverses-before-{stamp}"
    backup_dir.mkdir(parents=True, exist_ok=False)
    for path in paths:
        rel = path.relative_to(ROOT)
        dest = backup_dir / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        if path.is_dir():
            shutil.copytree(path, dest)
        else:
            shutil.copy2(path, dest)
    return backup_dir


def build_reverse_additions(solo, report):
    added_by_gate = defaultdict(list)
    for item in report["added"]:
        gate = str(item["gate"])
        if gate in NON_DM_GATES:
            added_by_gate[gate].append(item)

    additions = []
    for gate in NON_DM_GATES:
        chapters = solo["chapter"][gate]
        gate_items = [item for item in added_by_gate[gate] if str(item["chapter_id"]) in chapters]
        gate_items.sort(key=lambda item: list(chapters).index(str(item["chapter_id"])))
        for index, item in enumerate(gate_items, start=1):
            forward_id = int(item["chapter_id"])
            forward = chapters[str(forward_id)]
            existing_id = reverse_exists(chapters, forward_id, forward)
            reverse_id = existing_id or reverse_chapter_id(gate, index)
            while existing_id is None and str(reverse_id) in chapters:
                index += 1
                reverse_id = reverse_chapter_id(gate, index)

            forward_duel_path = SOLO_DUELS_DIR / f"{forward_id}.json"
            forward_duel = json.loads(forward_duel_path.read_text())
            reverse_duel = build_reverse_duel(forward_duel, reverse_id)
            player_name, opponent_name = reverse_duel["Duel"]["name"]
            additions.append(
                {
                    "gate": int(gate),
                    "forward_chapter_id": forward_id,
                    "reverse_chapter_id": reverse_id,
                    "title": item["title"],
                    "matchup": f"{player_name} vs. {opponent_name}",
                    "parent_chapter": forward_id,
                    "chapter": build_reverse_chapter(forward, forward_id),
                    "duel": reverse_duel,
                    "description": build_reverse_description(item["title"], player_name, opponent_name),
                    "already_exists": existing_id is not None,
                }
            )
    return additions


def annotate_report(report, additions):
    existing = {
        (item.get("gate"), item.get("forward_chapter_id"), item.get("reverse_chapter_id"))
        for item in report.get("non_dm_reverse_duels", [])
    }
    report.setdefault("non_dm_reverse_duels", [])
    for addition in additions:
        row = {
            "forward_chapter_id": addition["forward_chapter_id"],
            "reverse_chapter_id": addition["reverse_chapter_id"],
            "gate": addition["gate"],
            "title": addition["title"],
            "matchup": addition["matchup"],
            "parent_chapter": addition["parent_chapter"],
        }
        key = (row["gate"], row["forward_chapter_id"], row["reverse_chapter_id"])
        if key not in existing:
            report["non_dm_reverse_duels"].append(row)
            existing.add(key)
    report["non_dm_reverse_note"] = (
        "Added optional reverse chapters for Requiem story duels in GX, 5D's, ZEXAL, "
        "ARC-V, and VRAINS. Each reverse chapter parents to its forward duel and swaps "
        "p1/p2 portraits, duel names, and loaner decks."
    )


def apply_additions(solo_data, solo, report, additions):
    ids_entries = []
    for addition in additions:
        if addition["already_exists"]:
            continue
        gate = str(addition["gate"])
        reverse_id = addition["reverse_chapter_id"]
        insert_after_forward(
            solo["chapter"][gate],
            addition["forward_chapter_id"],
            reverse_id,
            addition["chapter"],
        )
        (SOLO_DUELS_DIR / f"{reverse_id}.json").write_text(
            json.dumps(addition["duel"], indent=2) + "\n"
        )
        ids_entries.append((reverse_id, addition["description"]))

    IDS_PATH.write_text(append_ids_entries(IDS_PATH.read_text(), ids_entries))
    annotate_report(report, additions)
    SOLO_PATH.write_text(json.dumps(solo_data, indent=2) + "\n")
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description="Add optional reverse duels for non-DM Requiem story inserts.")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    solo_data, solo = load_solo()
    report = json.loads(REPORT_PATH.read_text())
    additions = build_reverse_additions(solo, report)
    pending = [addition for addition in additions if not addition["already_exists"]]

    print(f"non-DM Requiem reverse duels discovered: {len(additions)}")
    print(f"pending new reverse duels: {len(pending)}")
    for addition in pending:
        print(
            f"{addition['reverse_chapter_id']}: {addition['matchup']} "
            f"(parent {addition['forward_chapter_id']})"
        )

    if not args.apply:
        print("Dry run only. Re-run with --apply to write campaign data.")
        return

    backup_dir = backup_files([SOLO_PATH, IDS_PATH, REPORT_PATH, SOLO_DUELS_DIR])
    apply_additions(solo_data, solo, report, additions)
    print(f"backed up originals to {backup_dir}")


if __name__ == "__main__":
    main()
