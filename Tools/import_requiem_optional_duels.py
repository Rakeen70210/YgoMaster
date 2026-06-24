#!/usr/bin/env python3
import argparse
import json
import re
import shutil
import struct
from collections import defaultdict
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
DEFAULT_REQUIEM = Path("/tmp/requiem-parsed.json")
DEFAULT_DECKS = Path("/tmp/requiem-extract-core/decks")
DEFAULT_LOTD_YDK_IDS = Path("/tmp/Lotd-src/Lotd/YdkIds.txt")

SERIES_TO_GATE = {
    0: 1101,
    1: 1102,
    2: 1103,
    3: 1104,
    4: 1105,
    5: 1106,
}

GATE_TEMPLATES = {
    1101: 11010001,
    1102: 11020001,
    1103: 11030001,
    1104: 11040001,
    1105: 11050001,
    1106: 11060001,
}

SERIES_NAMES = {
    0: "Yu-Gi-Oh!",
    1: "GX",
    2: "5D's",
    3: "ZEXAL",
    4: "ARC-V",
    5: "VRAINS",
}

PASSCODE_SUBSTITUTIONS = {
    # Requiem / Link Evolution cards that are not present in this Master Duel data.
    # Values are close Master Duel-available cards used to preserve deck size.
    18807108: 29267084,  # Spellbinding Circle -> Shadow Spell
    58074572: 84257639,  # Mooyan Curry -> Dian Keto the Cure Master
    38723936: 43434803,  # Question -> The Shallow Grave
    7165085: 17449108,  # Bait Doll -> Nobleman of Extermination
    24096228: 29228529,  # Double Spell -> Spell Reproduction
    62966332: 7153114,  # Convulsion of Nature -> Field Barrier
    43228023: 23995346,  # Blue-Eyes Alternative Ultimate Dragon -> Blue-Eyes Ultimate Dragon
    18491580: 88264978,  # Red-Eyes Alternative Black Dragon -> Red-Eyes Darkness Metal Dragon
    79613121: 30208479,  # Magician of Black Chaos MAX -> Magician of Black Chaos
}

MISSING_PROFILE_ICON_REPLACEMENTS = {
    # Ancient Gear Wyvern profile icon is referenced by stock GX solo duels but
    # this install does not have Images/ProfileIcon/ProfileIcon1000008.
    1000008: 0,
}


def parse_ydc(path):
    data = path.read_bytes()
    if len(data) < 10:
        raise ValueError(f"{path} is too small to be a YDC deck")
    pos = 8
    sections = {}
    for name in ("Main", "Extra", "Side"):
        if pos + 2 > len(data):
            raise ValueError(f"{path} ended before {name} count")
        count = struct.unpack_from("<H", data, pos)[0]
        pos += 2
        end = pos + count * 2
        if end > len(data):
            raise ValueError(f"{path} ended inside {name} cards")
        sections[name] = list(struct.unpack_from(f"<{count}H", data, pos)) if count else []
        pos = end
    return sections


def load_pair_map(path):
    mapping = {}
    for raw in path.read_text().splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        left, right = int(parts[0]), int(parts[1])
        mapping.setdefault(left, right)
    return mapping


def convert_deck(deck, le_to_passcode, passcode_to_md):
    converted = {"Main": [], "Extra": [], "Side": []}
    missing = []
    for section, cards in deck.items():
        for le_id in cards:
            passcode = le_to_passcode.get(le_id)
            if passcode is None:
                missing.append({"le_id": le_id, "passcode": None, "reason": "missing_passcode"})
                continue
            md_id = passcode_to_md.get(passcode)
            if md_id is None and passcode in PASSCODE_SUBSTITUTIONS:
                md_id = passcode_to_md.get(PASSCODE_SUBSTITUTIONS[passcode])
            if md_id is None:
                missing.append({"le_id": le_id, "passcode": passcode, "reason": "missing_md_id"})
                continue
            converted[section].append(md_id)
    return converted, missing


def to_deck_json(deck):
    return {
        section: {
            "CardIds": cards,
            "Rare": [1] * len(cards),
        }
        for section, cards in deck.items()
    }


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


def insert_after_anchor(chapters, anchor_id, new_id, new_chapter):
    rebuilt = {}
    inserted = False
    for chapter_id, chapter in chapters.items():
        rebuilt[chapter_id] = chapter
        if chapter_id == anchor_id:
            rebuilt[new_id] = new_chapter
            inserted = True
    if not inserted:
        rebuilt[new_id] = new_chapter
    chapters.clear()
    chapters.update(rebuilt)


def parse_char_names(path):
    return set(re.findall(r'"([^"]+)"\s*:', path.read_text()))


def clean_bonus_title(name):
    return re.sub(r"^Bonus-\s*", "", name).strip()


STORY_SETUP_BY_DUEL_ID = {
    186: "Mokuba lashes out during Duelist Kingdom while Pegasus's schemes put his brother and KaibaCorp in danger. Yugi has to get past Mokuba's stolen cards and reach the scared kid underneath the bravado.",
    187: "Yugi and Joey are trapped in the Paradox Brothers' underground maze on the way to Pegasus's castle. Break through their riddle-filled tag strategy before Gate Guardian blocks the only path forward.",
    188: "Duke Devlin tries to humble Joey after blaming Yugi's circle for Pegasus's fall from grace. Joey must answer Duke's dice-driven style and prove he can win without being carried by luck.",
    189: "Joey enters Battle City against Espa Roba, a fake psychic whose brothers secretly scout his opponent's hand. Expose the trick, survive Jinzo, and keep Joey's tournament hopes alive.",
    190: "Marik's masked Rare Hunters force Yugi and Kaiba into a deadly rooftop tag duel. Yugi has to work with his rival while Lumis and Umbra's masks cut off tribute summons.",
    191: "Marik brainwashes Joey and chains both friends into a duel where the loser will sink into the harbor. Yugi has to survive without hurting Joey and break the control over his best friend.",
    192: "Mai faces Yami Marik in the Battle City finals and is dragged into a Shadow Game that steals her memories. Marik turns the duel into torture while The Winged Dragon of Ra waits above them.",
    193: "Yami Bakura and the spirit of Marik's better self challenge Yami Marik in a battle of dark souls. Marik fights to keep control of the Millennium Rod and silence anyone who knows his weakness.",
    194: "Noah Kaiba tries to trap Yugi's friends in the virtual world and prove he deserves Kaiba's legacy. Yugi must carry Seto's cards into the fight and break Noah's godlike control.",
    195: "Joey reaches Yami Marik in the Battle City semifinals and enters a Shadow Game that punishes every attack. Marik wants to crush Joey's body and spirit before Yugi can face him.",
    196: "Zigfried von Schroeder targets Kaiba's tournament to settle a family grudge against KaibaCorp. Kaiba answers the sabotage directly and turns the duel into a corporate war with cards.",
    197: "Leon faces Yugi in the KC Grand Championship finals while Zigfried's illegal Golden Castle of Stromberg corrupts the match. Yugi must save Leon from a victory built as sabotage.",
    198: "Aigami, also known as Diva, uses the Quantum Cube to target Yugi's friends while chasing revenge for Shadi. Yugi must face Cubic monsters and stop his classmate's plan from erasing people into another dimension.",
    199: "Kaiba rebuilds the Millennium Puzzle hoping to reach Atem, but Yugi stands before him as his own duelist. Their duel proves whether Kaiba can accept a future without the Pharaoh.",
    200: "Shadi's connection to the Millennium Items brings him into contact with Solomon Muto's long history of games, travel, and ancient secrets. The duel imagines an old guardian testing Yugi's family at the game shop.",
    201: "Rebecca Hawkins enters the game shop as a young prodigy with a sharp temper and a powerful Deck. Tristan gets pulled into the chaos and has to prove heart can matter against raw talent.",
    202: "Crowler and Bonaparte duel over whether the Slifer Red dorm should be torn down. Crowler's pride in Duel Academy clashes with Bonaparte's plans, turning school politics into a real fight for the students' home.",
    203: "Axel Brodie confronts the Supreme King in the alternate dimension where Jaden's darkness has taken command. This version lets the Supreme King answer Axel's courage with Evil HERO power.",
    204: "Atticus Rhodes faces Yusuke Fujiwara as Darkness reaches back into Duel Academy's past. The duel centers on old friendship, lost identity, and the danger hidden inside Clear World.",
    205: "Fonda Fontaine steps out of the infirmary role and into a duel against Sarina's mirror tactics. Her healing theme becomes a way to protect Duel Academy's students beyond the nurse's office.",
    206: "Jaden's Winged Kuriboh spirit faces Professor Banner's alchemy-themed strategy in a playful spirit duel. It is instinct, loyalty, and card-spirit charm against careful classroom science.",
    207: "Jasmine and Mindy turn the Obelisk Blue girls' dorm into a friendly rivalry match. The duel gives Alexis's closest friends a chance to show their own style away from the main spotlight.",
    208: "Two unusual threats from GX's supernatural side collide in this extra matchup. Don Zaloog's Dark Scorpions try to steal the pace from Abidos the Third's ancient royal pride.",
    209: "Carly Carmine gets caught between journalism and danger when Trudge is turned against her. Her Fortune Ladies have to keep Public Security pressure away long enough to uncover the truth.",
    210: "Crow Hogan corners Lazar while searching for answers about the threats facing Satellite and New Domino City. Lazar's clownish act hides a slippery strategy built to waste Crow's time.",
    211: "Lester targets Luna during a dangerous Duel Board encounter tied to Iliaster's plan for the Signers. Luna has to protect her bond with Ancient Fairy Dragon from a Meklord hunter.",
    212: "Jakob brings Iliaster's heaviest pressure against Crow and the future the Signers are fighting to protect. Crow's Blackwings must answer Meklord Emperor Granel and the timeline Jakob wants to impose.",
    213: "Mina Simington and Blister meet in a street-level duel away from the Signers' biggest battles. Public Security polish runs into Blister's practical city experience.",
    214: "Rally Dawson and Tenzen Yanagi get a rare moment in the spotlight in this side duel. Rally's scrappy Satellite cards meet Yanagi's eccentric historical treasures.",
    215: "Paradox's time-traveling Malefic monsters threaten the history that connects generations of duelists. The Vagabond steps in as a silent stand-in for that legacy.",
    216: "Misty Tredwell and Carly Carmine cross paths before tragedy fully consumes them. This duel frames two future Dark Signers around fate, investigation, and the shadows closing in.",
    217: "Misty and Carly meet again after both have fallen under the Dark Signer curse. Earthbound Immortals turn their personal pain into a duel between two doomed favorites of the shadows.",
    218: "Rio Kastle faces Lotus in a duel built around beauty, thorns, and control. Rio's icy confidence has to cut through floral traps before the field blooms against her.",
    219: "Rio and Tori get a friendly match that highlights the support network around Yuma. Tori's optimism meets Rio's cool precision in a duel more about pride than survival.",
    220: "Bronk is pulled into a duel against Marin, Rio's Barian identity. The match brings ordinary loyalty face to face with the hidden Barian world surrounding Yuma's friends.",
    221: "Henrietta Akaba confronts Leo Akaba over the family and dimensions left broken by his plans. This duel turns the Akaba family's private fracture into a public card battle.",
    222: "Ray Akaba's sacrifice is central to stopping Z-ARC, while Riley carries the burden of what comes after. Their duel reflects protection, fear, and the hope that the next generation can hold the darkness back.",
    223: "Grace and Gloria Tyler settle their Amazoness rivalry in a sister duel. Grace's agility and Gloria's force turn family teamwork into a contest for bragging rights.",
    224: "Tour Guide faces The Vagrant in a final extra duel outside the main VRAINS conflict. Underworld fiends meet a wandering opponent, giving the bonus campaign a last playful challenge.",
    225: "This after-story duel pits Sera's dimensional powers against an ancient threat tied to the Pyramid of Light. Use her Cubic-style avatars to hold back Anubis before old darkness rises again.",
}


def build_description(duel, pchar, ochar, pdeck, odeck):
    title = clean_bonus_title(duel["name"])
    p_name = pchar["name"]
    o_name = ochar["name"]
    story_setup = STORY_SETUP_BY_DUEL_ID.get(duel["id"])
    if story_setup is None:
        story_setup = (
            f"{p_name} brings \"{pdeck['name']}\" against {o_name}'s \"{odeck['name']}\" "
            f"in an optional {SERIES_NAMES[duel['series']]} story duel."
        )
    return (
        f"{title}\n\n"
        f"{story_setup}\n\n"
        f"{p_name} vs. {o_name}"
    )


def load_solo(path):
    data = json.loads(path.read_text())
    solo = data["res"][0][1]["Master"]["Solo"]
    return data, solo


def validate_required_paths(args):
    required = [
        args.requiem_json,
        args.decks_dir,
        args.lotd_ydk_ids,
        DATA_ROOT / "Solo.json",
        DATA_ROOT / "YdkIds.txt",
        DATA_ROOT / "CardList.json",
        DATA_ROOT / "ClientData" / "IDS" / "IDS_SOLO.txt",
        DATA_ROOT / "ClientData" / "LinkEvolution" / "CharNames.json",
    ]
    missing = [str(path) for path in required if not Path(path).exists()]
    if missing:
        raise FileNotFoundError("Missing required files:\n" + "\n".join(missing))


def get_bonus_duels(requiem):
    duels = [d for d in requiem["duels"] if 186 <= d["id"] <= 225]
    duels.sort(key=lambda d: (d["series"], d["display"], d["id"]))
    return duels


def next_ids_by_gate(solo):
    result = {}
    for gate in SERIES_TO_GATE.values():
        existing = [int(chapter_id) for chapter_id in solo["chapter"][str(gate)]]
        result[gate] = max(existing) + 1
    return result


def build_import(args):
    validate_required_paths(args)
    requiem = json.loads(args.requiem_json.read_text())
    decks_by_id = {d["id"]: d for d in requiem["decks"]}
    chars_by_id = {c["id"]: c for c in requiem["chars"]}
    bonus_duels = get_bonus_duels(requiem)

    le_to_passcode = {le_id: passcode for passcode, le_id in load_pair_map(args.lotd_ydk_ids).items()}
    passcode_to_md = load_pair_map(DATA_ROOT / "YdkIds.txt")
    card_list = set(json.loads((DATA_ROOT / "CardList.json").read_text()))
    portrait_codes = parse_char_names(DATA_ROOT / "ClientData" / "LinkEvolution" / "CharNames.json")

    solo_data, solo = load_solo(DATA_ROOT / "Solo.json")
    next_ids = next_ids_by_gate(solo)
    added = []
    missing_cards = []
    missing_portraits = []
    substitutions = []

    for duel in bonus_duels:
        gate = SERIES_TO_GATE[duel["series"]]
        chapter_id = next_ids[gate]
        next_ids[gate] += 1
        pchar = chars_by_id[duel["pchar"]]
        ochar = chars_by_id[duel["ochar"]]
        pdeck = decks_by_id[duel["pdeck"]]
        odeck = decks_by_id[duel["odeck"]]

        deck_entries = []
        for role, deck_meta in (("player", pdeck), ("opponent", odeck)):
            ydc_path = args.decks_dir / f"{deck_meta['file']}.ydc"
            raw_deck = parse_ydc(ydc_path)
            raw_passcodes = [
                le_to_passcode.get(card)
                for section_cards in raw_deck.values()
                for card in section_cards
            ]
            substitutions.extend(
                {
                    "duel_id": duel["id"],
                    "chapter_id": chapter_id,
                    "role": role,
                    "deck_file": deck_meta["file"],
                    "from_passcode": passcode,
                    "to_passcode": PASSCODE_SUBSTITUTIONS[passcode],
                }
                for passcode in raw_passcodes
                if passcode in PASSCODE_SUBSTITUTIONS
            )
            converted, missing = convert_deck(raw_deck, le_to_passcode, passcode_to_md)
            missing.extend(
                {"le_id": int(card), "passcode": None, "reason": "missing_cardlist"}
                for section_cards in converted.values()
                for card in section_cards
                if str(card) not in card_list
            )
            if missing:
                missing_cards.append(
                    {
                        "duel_id": duel["id"],
                        "chapter_id": chapter_id,
                        "role": role,
                        "deck_file": deck_meta["file"],
                        "missing": missing,
                    }
                )
            deck_entries.append(to_deck_json(converted))

        for char in (pchar, ochar):
            if char["code"] not in portrait_codes:
                missing_portraits.append(
                    {
                        "duel_id": duel["id"],
                        "chapter_id": chapter_id,
                        "code": char["code"],
                        "name": char["name"],
                    }
                )

        anchor_index = max(1, min(int(duel["display"]), len(solo["chapter"][str(gate)])))
        anchor_id = f"{gate}{anchor_index:04d}"
        template_id = GATE_TEMPLATES[gate]
        template = json.loads((DATA_ROOT / "SoloDuels" / f"{template_id}.json").read_text())["Duel"]
        duel_json = {
            "Duel": {
                **{k: v for k, v in template.items() if k != "Deck"},
                "Deck": deck_entries,
                "chapter": chapter_id,
                "name": [pchar["name"], ochar["name"]],
            }
        }
        duel_json["Duel"]["icon"] = sanitize_icons(duel_json["Duel"].get("icon", []))
        chapter = {
            "parent_chapter": int(anchor_id),
            "unlock_id": 0,
            "npc_id": 1,
            "begin_sn": "",
            "set_id": 1,
            "mydeck_set_id": 1,
            "p1_img": pchar["code"],
            "p2_img": ochar["code"],
        }
        added.append(
            {
                "requiem_duel_id": duel["id"],
                "chapter_id": chapter_id,
                "gate": gate,
                "anchor_id": int(anchor_id),
                "title": clean_bonus_title(duel["name"]),
                "matchup": f"{pchar['name']} vs. {ochar['name']}",
                "pdeck": pdeck["file"],
                "odeck": odeck["file"],
                "chapter": chapter,
                "duel_json": duel_json,
                "ids_text": build_description(duel, pchar, ochar, pdeck, odeck),
            }
        )

    return {
        "solo_data": solo_data,
        "solo": solo,
        "added": added,
        "missing_cards": missing_cards,
        "missing_portraits": missing_portraits,
        "substitutions": substitutions,
    }


def backup_files(paths):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_dir = DATA_ROOT / "Backups" / f"requiem-optional-duels-{stamp}"
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
        raise RuntimeError("Refusing to apply: at least one Requiem deck has unmapped cards")

    collisions = []
    existing_ids_text = ids_path.read_text()
    for item in plan["added"]:
        chapter_id = str(item["chapter_id"])
        if (duel_dir / f"{chapter_id}.json").exists():
            collisions.append(str(duel_dir / f"{chapter_id}.json"))
        if f"[IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION]" in existing_ids_text:
            collisions.append(f"IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION")
    if collisions and not force:
        raise RuntimeError("Refusing to overwrite existing Requiem import targets:\n" + "\n".join(collisions))

    backup_dir = backup_files([solo_path, ids_path])

    grouped = defaultdict(list)
    for item in plan["added"]:
        grouped[item["gate"]].append(item)
        (duel_dir / f"{item['chapter_id']}.json").write_text(
            json.dumps(item["duel_json"], indent=2) + "\n"
        )

    for gate, items in grouped.items():
        chapters = plan["solo"]["chapter"][str(gate)]
        by_anchor = defaultdict(list)
        for item in items:
            by_anchor[str(item["anchor_id"])].append(item)
        rebuilt = {}
        for chapter_id, chapter in chapters.items():
            rebuilt[chapter_id] = chapter
            for item in by_anchor.get(chapter_id, []):
                rebuilt[str(item["chapter_id"])] = item["chapter"]
        for anchor, anchor_items in by_anchor.items():
            if anchor not in chapters:
                for item in anchor_items:
                    rebuilt[str(item["chapter_id"])] = item["chapter"]
        chapters.clear()
        chapters.update(rebuilt)

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
                for key in (
                    "requiem_duel_id",
                    "chapter_id",
                    "gate",
                    "anchor_id",
                    "title",
                    "matchup",
                    "pdeck",
                    "odeck",
                )
            }
            for item in plan["added"]
        ],
        "missing_cards": plan["missing_cards"],
        "missing_portraits": plan["missing_portraits"],
        "substitutions": plan["substitutions"],
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(payload, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description="Import Requiem bonus story duels into YgoMaster LE campaigns.")
    parser.add_argument("--requiem-json", type=Path, default=DEFAULT_REQUIEM)
    parser.add_argument("--decks-dir", type=Path, default=DEFAULT_DECKS)
    parser.add_argument("--lotd-ydk-ids", type=Path, default=DEFAULT_LOTD_YDK_IDS)
    parser.add_argument("--report", type=Path, default=DATA_ROOT / "Backups" / "requiem-optional-duels-plan-report.json")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    plan = build_import(args)
    write_report(plan, args.report)
    print(f"Prepared {len(plan['added'])} Requiem bonus duels")
    print(f"Missing card groups: {len(plan['missing_cards'])}")
    print(f"Missing portrait references: {len(plan['missing_portraits'])}")
    print(f"Card substitutions: {len(plan['substitutions'])}")
    print(f"Report: {args.report}")
    if args.apply:
        backup_dir = apply_import(plan, force=args.force)
        print(f"Applied import. Backup: {backup_dir}")
    else:
        print("Dry run only. Re-run with --apply to write Data/Solo.json, IDS text, and SoloDuels files.")


if __name__ == "__main__":
    main()
