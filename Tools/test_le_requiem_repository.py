#!/usr/bin/env python3
import json
import re
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"
DATA = DATA_ROOT
LE_GATES = ("1100", "1101", "1102", "1103", "1104", "1105", "1106",
            "1111", "1112", "1113", "1114", "1115", "1116", "1121")
STORY_GATES = ("1101", "1102", "1103", "1104", "1105", "1106")
CHALLENGE_GATES = ("1111", "1112", "1113", "1114", "1115", "1116")


def load_json_with_comments(path):
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//.*?$", "", text, flags=re.M)
    text = re.sub(r",\s*([}\]])", r"\1", text)
    return json.loads(text)


class LeRequiemRepositoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        payload = load_json_with_comments(DATA / "Solo.json")
        cls.solo = payload["res"][0][1]["Master"]["Solo"]
        cls.ids = (DATA / "ClientData" / "IDS" / "IDS_SOLO.txt").read_text(
            encoding="utf-8"
        )
        item_ids = load_json_with_comments(DATA / "ItemID.json")
        cls.valid_icons = set(item_ids.get("ICON", [])) | {0}

    def test_complete_le_and_requiem_chapter_counts(self):
        expected = {
            "1100": 5,
            "1101": 98,
            "1102": 78,
            "1103": 82,
            "1104": 58,
            "1105": 72,
            "1106": 58,
            "1111": 42,
            "1112": 43,
            "1113": 43,
            "1114": 31,
            "1115": 37,
            "1116": 21,
            "1121": 2,
        }
        actual = {gate: len(self.solo["chapter"][gate]) for gate in LE_GATES}
        self.assertEqual(actual, expected)

    def test_every_le_chapter_has_duel_data_and_text(self):
        for gate in LE_GATES:
            for chapter_id in self.solo["chapter"][gate]:
                self.assertTrue(
                    (DATA / "SoloDuels" / f"{chapter_id}.json").is_file(),
                    chapter_id,
                )
                self.assertIn(
                    f"[IDS_SOLO.CHAPTER{chapter_id}_EXPLANATION]",
                    self.ids,
                    chapter_id,
                )

    def test_story_duel_profile_icons_exist(self):
        for gate in STORY_GATES:
            for chapter_id in self.solo["chapter"][gate]:
                duel = json.loads(
                    (DATA / "SoloDuels" / f"{chapter_id}.json").read_text(
                        encoding="utf-8"
                    )
                )["Duel"]
                invalid = [
                    icon for icon in duel.get("icon", [])
                    if icon not in self.valid_icons
                ]
                self.assertEqual(invalid, [], chapter_id)

    def test_all_le_duels_have_three_enemy_deck_rewards(self):
        for gate in LE_GATES:
            for chapter_id, chapter in self.solo["chapter"][gate].items():
                rewards = chapter.get("extraRewards", {}).get("win", [])
                self.assertEqual(len(rewards), 3, chapter_id)
                for reward in rewards:
                    self.assertEqual(reward.get("type"), "Card", chapter_id)
                    self.assertEqual(reward.get("rate"), 100, chapter_id)
                    self.assertEqual(reward.get("cardOwnedLimit"), 3, chapter_id)

    def test_required_link_evolution_client_assets_are_present(self):
        required = (
            "ClientData/LinkEvolution/chars.png",
            "ClientData/LinkEvolution/chars_mask.png",
            "ClientData/LinkEvolution/chars.dfymoo",
            "ClientData/LinkEvolution/CharNames.json",
            "ClientData/LinkEvolution/UISettings.json",
        )
        for relative_path in required:
            self.assertTrue((DATA / relative_path).is_file(), relative_path)

    def test_private_and_generated_files_are_not_tracked_payload(self):
        tracked = subprocess.check_output(
            ["git", "ls-files"], cwd=ROOT, text=True
        )
        forbidden_prefixes = (
            "YgoMaster/Data/Players/",
            "YgoMaster/Data/Backups/",
            "Tools/__pycache__/",
        )
        for prefix in forbidden_prefixes:
            self.assertNotIn(prefix, tracked)


if __name__ == "__main__":
    unittest.main()
