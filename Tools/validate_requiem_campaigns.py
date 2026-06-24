#!/usr/bin/env python3
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
LE_STORY_GATES = ("1101", "1102", "1103", "1104", "1105", "1106")
LE_CHALLENGE_GATES = ("1111", "1112", "1113", "1114", "1115", "1116")
EXPECTED_STORY_COUNTS = {
    1101: 17,
    1102: 7,
    1103: 9,
    1104: 3,
    1105: 3,
    1106: 1,
}
DM_AFTERSTORY_CHAIN = [11010063, 11010081, 11010077, 11010078, 11010079, 11010080]
DM_REVERSE_BASE = 11010100
STORY_TEXT_MIN_LENGTH = 180
STORY_TEXT_MAX_LENGTH = 520
STORY_PLACEHOLDER_PHRASES = (
    "This Requiem bonus duel",
    "Play the Requiem bonus duel",
    "swaps the loaner decks",
    "wider cast",
)
MISSING_PROFILE_ICONS = {
    1000008,
}
DM_EXPECTED_FORWARD_SPINE = [
    "11010001",
    "11010003",
    "11010005",
    "11010065",
    "11010007",
    "11010009",
    "11010011",
    "11010013",
    "11010015",
    "11010017",
    "11010066",
    "11010019",
    "11010021",
    "11010023",
    "11010025",
    "11010027",
    "11010029",
    "11010031",
    "11010033",
    "11010067",
    "11010035",
    "11010068",
    "11010037",
    "11010039",
    "11010041",
    "11010069",
    "11010070",
    "11010043",
    "11010045",
    "11010071",
    "11010047",
    "11010072",
    "11010073",
    "11010074",
    "11010049",
    "11010051",
    "11010053",
    "11010055",
    "11010057",
    "11010059",
    "11010061",
    "11010075",
    "11010076",
    "11010063",
    "11010081",
    "11010077",
    "11010078",
    "11010079",
    "11010080",
]
EXPECTED_FORWARD_SPINES = {
    "1101": DM_EXPECTED_FORWARD_SPINE,
    "1102": [
        "11020001",
        "11020003",
        "11020005",
        "11020007",
        "11020009",
        "11020011",
        "11020013",
        "11020015",
        "11020017",
        "11020019",
        "11020021",
        "11020023",
        "11020025",
        "11020027",
        "11020029",
        "11020065",
        "11020068",
        "11020031",
        "11020033",
        "11020035",
        "11020037",
        "11020070",
        "11020039",
        "11020041",
        "11020043",
        "11020045",
        "11020047",
        "11020049",
        "11020051",
        "11020066",
        "11020053",
        "11020055",
        "11020057",
        "11020059",
        "11020069",
        "11020067",
        "11020071",
        "11020061",
        "11020063",
    ],
    "1103": [
        "11030001",
        "11030003",
        "11030005",
        "11030007",
        "11030009",
        "11030011",
        "11030065",
        "11030013",
        "11030066",
        "11030015",
        "11030017",
        "11030019",
        "11030021",
        "11030023",
        "11030067",
        "11030025",
        "11030027",
        "11030029",
        "11030031",
        "11030033",
        "11030035",
        "11030037",
        "11030069",
        "11030070",
        "11030039",
        "11030071",
        "11030072",
        "11030073",
        "11030041",
        "11030043",
        "11030045",
        "11030047",
        "11030049",
        "11030051",
        "11030068",
        "11030053",
        "11030055",
        "11030057",
        "11030059",
        "11030061",
        "11030063",
    ],
    "1104": [
        "11040001",
        "11040003",
        "11040005",
        "11040007",
        "11040009",
        "11040011",
        "11040013",
        "11040015",
        "11040017",
        "11040019",
        "11040021",
        "11040023",
        "11040025",
        "11040027",
        "11040053",
        "11040054",
        "11040029",
        "11040031",
        "11040033",
        "11040035",
        "11040037",
        "11040039",
        "11040041",
        "11040055",
        "11040043",
        "11040045",
        "11040047",
        "11040049",
        "11040051",
    ],
    "1105": [
        "11050001",
        "11050003",
        "11050005",
        "11050007",
        "11050009",
        "11050011",
        "11050013",
        "11050015",
        "11050017",
        "11050019",
        "11050021",
        "11050023",
        "11050025",
        "11050027",
        "11050029",
        "11050031",
        "11050033",
        "11050035",
        "11050037",
        "11050039",
        "11050041",
        "11050043",
        "11050045",
        "11050047",
        "11050069",
        "11050049",
        "11050051",
        "11050053",
        "11050055",
        "11050057",
        "11050059",
        "11050061",
        "11050067",
        "11050068",
        "11050063",
        "11050065",
    ],
    "1106": [
        "11060001",
        "11060003",
        "11060005",
        "11060007",
        "11060009",
        "11060011",
        "11060013",
        "11060015",
        "11060017",
        "11060019",
        "11060021",
        "11060023",
        "11060025",
        "11060027",
        "11060029",
        "11060031",
        "11060057",
        "11060033",
        "11060035",
        "11060037",
        "11060039",
        "11060041",
        "11060043",
        "11060045",
        "11060047",
        "11060049",
        "11060051",
        "11060053",
        "11060055",
    ],
}


def load_solo():
    data = json.loads((DATA_ROOT / "Solo.json").read_text())
    return data["res"][0][1]["Master"]["Solo"]


def load_report():
    report_path = ROOT / "Tools" / "manifests" / "requiem-optional-duels-report.json"
    return json.loads(report_path.read_text())


def load_char_names():
    path = DATA_ROOT / "ClientData" / "LinkEvolution" / "CharNames.json"
    return set(re.findall(r'"([^"]+)"\s*:', path.read_text()))


def parse_ids_entries(ids_text):
    entries = {}
    current = None
    buffer = []
    header_pattern = re.compile(r"^\[(IDS_SOLO\.CHAPTER(\d+)_(TITLE|EXPLANATION))\]$")
    for line in ids_text.splitlines():
        match = header_pattern.match(line)
        if match:
            if current is not None:
                entries[current] = "\n".join(buffer).strip()
            current = match.group(1)
            buffer = []
        elif line.startswith("[") and current is not None:
            entries[current] = "\n".join(buffer).strip()
            current = None
            buffer = []
        elif current is not None:
            buffer.append(line)
    if current is not None:
        entries[current] = "\n".join(buffer).strip()
    return entries


def assert_no_cycles(solo, gate):
    chapters = solo["chapter"][gate]
    for chapter_id in chapters:
        seen = []
        current = chapter_id
        while current and str(current) in chapters:
            current = str(current)
            if current in seen:
                cycle = seen[seen.index(current):] + [current]
                raise AssertionError(f"gate {gate} has parent cycle: {' -> '.join(cycle)}")
            seen.append(current)
            current = chapters[current].get("parent_chapter", 0)


def validate_requiem_layout_compactness(solo, gate, added_ids):
    chapters = solo["chapter"][gate]
    children = defaultdict(list)
    for chapter_id, chapter in chapters.items():
        parent = str(chapter.get("parent_chapter", 0))
        if parent in chapters:
            children[parent].append(chapter_id)
    crowded = [
        (parent, child_ids)
        for parent, child_ids in children.items()
        if len(child_ids) > 2 and any(child_id in added_ids for child_id in child_ids)
    ]
    if crowded:
        parent, child_ids = crowded[0]
        raise AssertionError(f"gate {gate} has crowded Requiem layout at {parent}: {child_ids}")


def is_reverse_of(chapter, forward):
    return (
        chapter.get("p1_img")
        and chapter.get("p2_img")
        and chapter.get("p1_img") == forward.get("p2_img")
        and chapter.get("p2_img") == forward.get("p1_img")
    )


def validate_three_row_layout(solo, gate):
    chapters = solo["chapter"][gate]
    spine = []
    reverse_nodes = []
    for chapter_id, chapter in chapters.items():
        parent = str(chapter.get("parent_chapter", 0))
        if parent in chapters and is_reverse_of(chapter, chapters[parent]):
            reverse_nodes.append(chapter_id)
        else:
            spine.append(chapter_id)

    for index, chapter_id in enumerate(spine):
        expected_parent = 0 if index == 0 else int(spine[index - 1])
        actual_parent = chapters[chapter_id].get("parent_chapter", 0)
        if actual_parent != expected_parent:
            raise AssertionError(
                f"gate {gate} spine break at {chapter_id}: actual={actual_parent} expected={expected_parent}"
            )

    for chapter_id in reverse_nodes:
        parent = str(chapters[chapter_id].get("parent_chapter", 0))
        if parent not in spine:
            raise AssertionError(f"gate {gate} reverse node {chapter_id} is not parented to the main spine")

    expected_spine = EXPECTED_FORWARD_SPINES.get(gate)
    if expected_spine is not None and spine != expected_spine:
        raise AssertionError(f"gate {gate} forward spine order mismatch: actual={spine} expected={expected_spine}")


def validate_story_text_quality(solo, ids_text):
    entries = parse_ids_entries(ids_text)
    headers = re.findall(r"^\[(IDS_SOLO\.CHAPTER\d+_EXPLANATION)\]$", ids_text, flags=re.MULTILINE)
    duplicates = [header for header, count in Counter(headers).items() if count > 1]
    if duplicates:
        raise AssertionError(f"duplicate IDS explanation headers: {duplicates[:10]}")

    for gate in LE_STORY_GATES:
        for chapter_id in solo["chapter"][gate]:
            key = f"IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION"
            explanation = entries.get(key)
            if not explanation:
                raise AssertionError(f"missing parsed IDS explanation for story chapter {chapter_id}")
            length = len(explanation)
            if length < STORY_TEXT_MIN_LENGTH:
                raise AssertionError(f"{chapter_id} explanation is too short: {length}")
            if length > STORY_TEXT_MAX_LENGTH:
                raise AssertionError(f"{chapter_id} explanation is too long: {length}")
            if any(ord(char) > 127 for char in explanation):
                raise AssertionError(f"{chapter_id} explanation contains non-ASCII characters")
            for phrase in STORY_PLACEHOLDER_PHRASES:
                if phrase in explanation:
                    raise AssertionError(f"{chapter_id} explanation still contains placeholder phrase: {phrase}")


def validate_reverse_duel_pair(solo, ids_text, gate, forward_id, reverse_id):
    chapters = solo["chapter"][str(gate)]
    forward_key = str(forward_id)
    reverse_key = str(reverse_id)
    if forward_key not in chapters:
        raise AssertionError(f"missing forward chapter {forward_id} for reverse {reverse_id}")
    if reverse_key not in chapters:
        raise AssertionError(f"missing Requiem reverse chapter {reverse_id} for {forward_id}")

    forward = chapters[forward_key]
    reverse = chapters[reverse_key]
    if reverse.get("parent_chapter") != int(forward_id):
        raise AssertionError(f"reverse {reverse_id} should parent to forward {forward_id}")
    if reverse.get("p1_img") != forward.get("p2_img") or reverse.get("p2_img") != forward.get("p1_img"):
        raise AssertionError(f"reverse {reverse_id} does not swap portraits from {forward_id}")
    if not (DATA_ROOT / "SoloDuels" / f"{reverse_id}.json").exists():
        raise AssertionError(f"missing duel file for Requiem reverse chapter {reverse_id}")
    if f"[IDS_SOLO.CHAPTER{reverse_id}_EXPLANATION]" not in ids_text:
        raise AssertionError(f"missing IDS explanation for Requiem reverse chapter {reverse_id}")

    forward_duel = json.loads((DATA_ROOT / "SoloDuels" / f"{forward_id}.json").read_text())["Duel"]
    reverse_duel = json.loads((DATA_ROOT / "SoloDuels" / f"{reverse_id}.json").read_text())["Duel"]
    if reverse_duel.get("chapter") != int(reverse_id):
        raise AssertionError(f"reverse duel file {reverse_id} has wrong chapter")
    if reverse_duel.get("name") != list(reversed(forward_duel.get("name", []))):
        raise AssertionError(f"reverse duel file {reverse_id} does not swap names")
    if reverse_duel.get("Deck") != list(reversed(forward_duel.get("Deck", []))):
        raise AssertionError(f"reverse duel file {reverse_id} does not swap decks")


def validate_no_missing_profile_icons(solo):
    item_id_path = DATA_ROOT / "ItemID.json"
    if not item_id_path.exists():
        return
    text = item_id_path.read_text(encoding="utf-8")
    text_clean = re.sub(r"//.*?$", "", text, flags=re.M)
    text_clean = re.sub(r",\s*([\]}])", r"\1", text_clean)
    item_ids = json.loads(text_clean)
    valid_icons = set(item_ids.get("ICON", []))
    valid_icons.add(0)

    for gate in LE_STORY_GATES + LE_CHALLENGE_GATES:
        for chapter_id in solo["chapter"][gate]:
            duel_path = DATA_ROOT / "SoloDuels" / f"{chapter_id}.json"
            if not duel_path.exists():
                continue
            duel = json.loads(duel_path.read_text()).get("Duel")
            if not duel:
                continue
            bad_icons = [icon for icon in duel.get("icon", []) if icon not in valid_icons]
            if bad_icons:
                raise AssertionError(f"{chapter_id} references invalid profile icons: {bad_icons}")


def validate_story_duels():
    solo = load_solo()
    report = load_report()
    char_names = load_char_names()
    ids_text = (DATA_ROOT / "ClientData" / "IDS" / "IDS_SOLO.txt").read_text()
    added = report["added"]

    if report["added_count"] != 40 or len(added) != 40:
        raise AssertionError(f"expected 40 story duels, got added_count={report['added_count']} len={len(added)}")

    validate_story_text_quality(solo, ids_text)

    counts = Counter(item["gate"] for item in added)
    if dict(counts) != EXPECTED_STORY_COUNTS:
        raise AssertionError(f"unexpected story gate counts: {dict(counts)}")

    by_gate = defaultdict(list)
    for item in added:
        by_gate[str(item["gate"])].append(item)

    for gate in LE_STORY_GATES:
        chapters = solo["chapter"][gate]
        assert_no_cycles(solo, gate)
        validate_three_row_layout(solo, gate)
        gate_items = by_gate[gate]
        expected_ids = {str(item["chapter_id"]) for item in gate_items}
        actual_order = [chapter_id for chapter_id in chapters if chapter_id in expected_ids]
        expected_spine = EXPECTED_FORWARD_SPINES.get(gate, [])
        expected_order = [chapter_id for chapter_id in expected_spine if chapter_id in expected_ids]
        if actual_order != expected_order:
            raise AssertionError(f"gate {gate} Requiem order mismatch: actual={actual_order} expected={expected_order}")

        for item in gate_items:
            chapter_id = str(item["chapter_id"])
            if chapter_id not in chapters:
                raise AssertionError(f"missing Requiem chapter {chapter_id} in gate {gate}")
            if not (DATA_ROOT / "SoloDuels" / f"{chapter_id}.json").exists():
                raise AssertionError(f"missing duel file for Requiem chapter {chapter_id}")
            if f"[IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION]" not in ids_text:
                raise AssertionError(f"missing IDS explanation for Requiem chapter {chapter_id}")

            chapter = chapters[chapter_id]
            for image_key in ("p1_img", "p2_img"):
                image = chapter.get(image_key)
                if image not in char_names:
                    raise AssertionError(f"{chapter_id} references missing portrait key {image}")

            if item.get("layout_mode") == "main_story_spine":
                expected_parent = chapter.get("parent_chapter")
            elif item.get("dm_afterstory_tail"):
                expected_parent = item["parent_after_dm_tail"]
            else:
                expected_parent = item.get(
                    "layout_parent_after_compact",
                    item.get("parent_after_cycle_fix", item.get("parent_after_reconcile")),
                )
            actual_parent = chapter.get("parent_chapter")
            if actual_parent != expected_parent:
                raise AssertionError(
                    f"{chapter_id} parent mismatch: actual={actual_parent} expected={expected_parent}"
                )

    dm_chapters = solo["chapter"]["1101"]
    for previous_id, chapter_id in zip(DM_AFTERSTORY_CHAIN, DM_AFTERSTORY_CHAIN[1:]):
        parent = dm_chapters[str(chapter_id)]["parent_chapter"]
        if parent != previous_id:
            raise AssertionError(f"DM after-story chain broke at {chapter_id}: parent={parent}")
    for gate in ("1001", "1101"):
        if solo["gate"][gate]["clear_chapter"] != 11010080:
            raise AssertionError(f"gate {gate} should clear on 11010080")

    dm_items = sorted(by_gate["1101"], key=lambda item: item["guide_order"])
    for item in dm_items:
        forward_id = item["chapter_id"]
        reverse_id = DM_REVERSE_BASE + (item["guide_order"] - 1)
        validate_reverse_duel_pair(solo, ids_text, "1101", forward_id, reverse_id)

    non_dm_reverses = report.get("non_dm_reverse_duels", [])
    if len(non_dm_reverses) != 23:
        raise AssertionError(f"expected 23 non-DM Requiem reverse duels, got {len(non_dm_reverses)}")
    for item in non_dm_reverses:
        validate_reverse_duel_pair(
            solo,
            ids_text,
            item["gate"],
            item["forward_chapter_id"],
            item["reverse_chapter_id"],
        )


def main():
    validate_story_duels()
    solo = load_solo()
    validate_no_missing_profile_icons(solo)
    for gate in LE_CHALLENGE_GATES:
        assert_no_cycles(solo, gate)
    print("Requiem campaign validation passed")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as exc:
        print(f"Requiem campaign validation failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
