#!/usr/bin/env python3
import argparse
import json
import shutil
from collections import defaultdict
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
SOLO_PATH = DATA_ROOT / "Solo.json"
REPORT_PATH = ROOT / "Tools" / "manifests" / "requiem-optional-duels-report.json"


def load_solo(path=SOLO_PATH):
    data = json.loads(path.read_text())
    return data, data["res"][0][1]["Master"]["Solo"]


def load_report(path=REPORT_PATH):
    return json.loads(path.read_text())


def child_map(chapters):
    children = defaultdict(list)
    for chapter_id, chapter in chapters.items():
        parent = str(chapter.get("parent_chapter", 0))
        if parent in chapters:
            children[parent].append(chapter_id)
    return children


def max_child_count(chapters):
    children = child_map(chapters)
    return max((len(items) for items in children.values()), default=0)


def has_descendant(chapters, chapter_id, possible_descendant):
    current = str(possible_descendant)
    seen = set()
    while current in chapters and current not in seen:
        seen.add(current)
        parent = str(chapters[current].get("parent_chapter", 0))
        if parent == str(chapter_id):
            return True
        current = parent
    return False


def choose_compact_parent(chapters, chapter_id, current_parent, children):
    ordered = list(chapters)
    chapter_id = str(chapter_id)
    current_parent = str(current_parent)
    try:
        parent_index = ordered.index(current_parent)
        chapter_index = ordered.index(chapter_id)
    except ValueError:
        return None

    sibling_candidates = [child for child in children[current_parent] if child != chapter_id]
    candidates = sibling_candidates + ordered[chapter_index + 1 :] + ordered[parent_index + 1 : chapter_index]

    for candidate in candidates:
        if candidate == chapter_id:
            continue
        if has_descendant(chapters, chapter_id, candidate):
            continue
        if len(children[candidate]) < 2:
            return int(candidate)
    return None


def compact_requiem_layout(solo, report):
    changes = []
    added_by_gate = defaultdict(list)
    for item in report.get("added", []):
        added_by_gate[str(item["gate"])].append(item)

    for gate, items in added_by_gate.items():
        chapters = solo["chapter"][gate]
        added_ids = {str(item["chapter_id"]) for item in items}
        children = child_map(chapters)

        for item in items:
            chapter_id = str(item["chapter_id"])
            if chapter_id not in chapters:
                continue
            current_parent = str(chapters[chapter_id].get("parent_chapter", 0))
            if current_parent not in chapters:
                continue
            if len(children[current_parent]) <= 2:
                item.pop("layout_parent_after_compact", None)
                continue

            new_parent = choose_compact_parent(chapters, chapter_id, current_parent, children)
            if new_parent is None:
                continue

            children[current_parent].remove(chapter_id)
            children[str(new_parent)].append(chapter_id)
            chapters[chapter_id]["parent_chapter"] = new_parent
            item["layout_parent_after_compact"] = new_parent
            changes.append({"chapter_id": int(chapter_id), "from": int(current_parent), "to": new_parent})

        unresolved = [
            chapter_id
            for chapter_id, child_ids in children.items()
            if len(child_ids) > 2 and any(child_id in added_ids for child_id in child_ids)
        ]
        if unresolved:
            raise RuntimeError(f"gate {gate} still has Requiem nodes on crowded parents: {unresolved}")

    return changes


def backup_files(paths):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_dir = DATA_ROOT / "Backups" / f"requiem-layout-compact-before-{stamp}"
    backup_dir.mkdir(parents=True, exist_ok=False)
    for path in paths:
        rel = path.relative_to(ROOT)
        dest = backup_dir / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, dest)
    return backup_dir


def main():
    parser = argparse.ArgumentParser(description="Compact Requiem campaign graph branches to avoid off-screen rows.")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    solo_data, solo = load_solo()
    report = load_report()
    changes = compact_requiem_layout(solo, report)

    print(f"layout parent changes: {len(changes)}")
    for change in changes:
        print(f"{change['chapter_id']}: {change['from']} -> {change['to']}")

    if not args.apply:
        print("Dry run only. Re-run with --apply to write Solo.json and the Requiem report.")
        return

    backup_dir = backup_files([SOLO_PATH, REPORT_PATH])
    SOLO_PATH.write_text(json.dumps(solo_data, indent=2) + "\n")
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n")
    print(f"backed up originals to {backup_dir}")


if __name__ == "__main__":
    main()
