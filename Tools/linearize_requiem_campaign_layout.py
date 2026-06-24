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
LE_STORY_GATES = ("1101", "1102", "1103", "1104", "1105", "1106")
FORWARD_STORY_ORDER_BY_GATE = {
    "1101": [
        "11010001", "11010003", "11010005", "11010065", "11010007", "11010009",
        "11010011", "11010013", "11010015", "11010017", "11010066", "11010019",
        "11010021", "11010023", "11010025", "11010027", "11010029", "11010031",
        "11010033", "11010067", "11010035", "11010068", "11010037", "11010039",
        "11010041", "11010069", "11010070", "11010043", "11010045", "11010071",
        "11010047", "11010072", "11010073", "11010074", "11010049", "11010051",
        "11010053", "11010055", "11010057", "11010059", "11010061", "11010075",
        "11010076", "11010063", "11010081", "11010077", "11010078", "11010079",
        "11010080",
    ],
    "1102": [
        "11020001", "11020003", "11020005", "11020007", "11020009", "11020011",
        "11020013", "11020015", "11020017", "11020019", "11020021", "11020023",
        "11020025", "11020027", "11020029", "11020065", "11020068", "11020031",
        "11020033", "11020035", "11020037", "11020070", "11020039", "11020041",
        "11020043", "11020045", "11020047", "11020049", "11020051", "11020066",
        "11020053", "11020055", "11020057", "11020059", "11020069", "11020067",
        "11020071", "11020061", "11020063",
    ],
    "1103": [
        "11030001", "11030003", "11030005", "11030007", "11030009", "11030011",
        "11030065", "11030013", "11030066", "11030015", "11030017", "11030019",
        "11030021", "11030023", "11030067", "11030025", "11030027", "11030029",
        "11030031", "11030033", "11030035", "11030037", "11030069", "11030070",
        "11030039", "11030071", "11030072", "11030073", "11030041", "11030043",
        "11030045", "11030047", "11030049", "11030051", "11030068", "11030053",
        "11030055", "11030057", "11030059", "11030061", "11030063",
    ],
    "1104": [
        "11040001", "11040003", "11040005", "11040007", "11040009", "11040011",
        "11040013", "11040015", "11040017", "11040019", "11040021", "11040023",
        "11040025", "11040027", "11040053", "11040054", "11040029", "11040031",
        "11040033", "11040035", "11040037", "11040039", "11040041", "11040055",
        "11040043", "11040045", "11040047", "11040049", "11040051",
    ],
    "1105": [
        "11050001", "11050003", "11050005", "11050007", "11050009", "11050011",
        "11050013", "11050015", "11050017", "11050019", "11050021", "11050023",
        "11050025", "11050027", "11050029", "11050031", "11050033", "11050035",
        "11050037", "11050039", "11050041", "11050043", "11050045", "11050047",
        "11050069", "11050049", "11050051", "11050053", "11050055", "11050057",
        "11050059", "11050061", "11050067", "11050068", "11050063", "11050065",
    ],
    "1106": [
        "11060001", "11060003", "11060005", "11060007", "11060009", "11060011",
        "11060013", "11060015", "11060017", "11060019", "11060021", "11060023",
        "11060025", "11060027", "11060029", "11060031", "11060057", "11060033",
        "11060035", "11060037", "11060039", "11060041", "11060043", "11060045",
        "11060047", "11060049", "11060051", "11060053", "11060055",
    ],
}


def load_solo(path=SOLO_PATH):
    data = json.loads(path.read_text())
    return data, data["res"][0][1]["Master"]["Solo"]


def load_report(path=REPORT_PATH):
    return json.loads(path.read_text())


def is_reverse_of(chapter, forward):
    return (
        chapter.get("p1_img")
        and chapter.get("p2_img")
        and chapter.get("p1_img") == forward.get("p2_img")
        and chapter.get("p2_img") == forward.get("p1_img")
    )


def find_forward_for_reverse(chapter_id, chapter, forward_items):
    parent = str(chapter.get("parent_chapter", 0))
    if parent in forward_items and is_reverse_of(chapter, forward_items[parent]):
        return parent
    return None


def linearize_gate(chapters, forward_order=None):
    original_items = list(chapters.items())
    forward_items = {}
    reverse_by_forward = defaultdict(list)

    for chapter_id, chapter in original_items:
        forward_id = find_forward_for_reverse(chapter_id, chapter, forward_items)
        if forward_id is None:
            forward_items[chapter_id] = chapter
        else:
            reverse_by_forward[forward_id].append((chapter_id, chapter))

    if forward_order is not None:
        missing = [chapter_id for chapter_id in forward_order if chapter_id not in forward_items]
        extras = [chapter_id for chapter_id in forward_items if chapter_id not in forward_order]
        if missing or extras:
            raise RuntimeError(f"forward order mismatch: missing={missing} extras={extras}")
        forward_items = {chapter_id: forward_items[chapter_id] for chapter_id in forward_order}

    previous_forward = None
    rebuilt = {}
    for forward_id, forward in forward_items.items():
        new_parent = int(previous_forward) if previous_forward is not None else 0
        forward["parent_chapter"] = new_parent
        rebuilt[forward_id] = forward

        for reverse_id, reverse in reverse_by_forward.get(forward_id, []):
            reverse["parent_chapter"] = int(forward_id)
            rebuilt[reverse_id] = reverse

        previous_forward = forward_id

    chapters.clear()
    chapters.update(rebuilt)


def backup_files(paths):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_dir = DATA_ROOT / "Backups" / f"requiem-three-row-layout-before-{stamp}"
    backup_dir.mkdir(parents=True, exist_ok=False)
    for path in paths:
        rel = path.relative_to(ROOT)
        dest = backup_dir / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, dest)
    return backup_dir


def annotate_report(report):
    for item in report.get("added", []):
        item.pop("layout_parent_after_compact", None)
        item["layout_mode"] = "main_story_spine"


def main():
    parser = argparse.ArgumentParser(description="Force LE story campaigns into a three-row map layout.")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    solo_data, solo = load_solo()
    report = load_report()
    before = {}
    for gate in LE_STORY_GATES:
        before[gate] = {
            chapter_id: chapter.get("parent_chapter", 0)
            for chapter_id, chapter in solo["chapter"][gate].items()
        }
        forward_order = FORWARD_STORY_ORDER_BY_GATE.get(gate)
        linearize_gate(solo["chapter"][gate], forward_order)
    annotate_report(report)

    changes = []
    for gate in LE_STORY_GATES:
        for chapter_id, chapter in solo["chapter"][gate].items():
            old_parent = before[gate].get(chapter_id)
            new_parent = chapter.get("parent_chapter", 0)
            if old_parent != new_parent:
                changes.append((chapter_id, old_parent, new_parent))

    print(f"parent changes: {len(changes)}")
    for chapter_id, old_parent, new_parent in changes[:80]:
        print(f"{chapter_id}: {old_parent} -> {new_parent}")
    if len(changes) > 80:
        print(f"... {len(changes) - 80} more")

    if not args.apply:
        print("Dry run only. Re-run with --apply to write Solo.json and the Requiem report.")
        return

    backup_dir = backup_files([SOLO_PATH, REPORT_PATH])
    SOLO_PATH.write_text(json.dumps(solo_data, indent=2) + "\n")
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n")
    print(f"backed up originals to {backup_dir}")


if __name__ == "__main__":
    main()
