import importlib.util
import struct
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("import_requiem_optional_duels.py")
spec = importlib.util.spec_from_file_location("import_requiem_optional_duels", MODULE_PATH)
requiem = importlib.util.module_from_spec(spec)
spec.loader.exec_module(requiem)


class RequiemImportTests(unittest.TestCase):
    def test_parse_ydc_reads_main_extra_and_side_sections(self):
        data = bytearray(struct.pack("<II", 0x648C, 0))
        data += struct.pack("<H", 3) + struct.pack("<HHH", 101, 102, 103)
        data += struct.pack("<H", 2) + struct.pack("<HH", 201, 202)
        data += struct.pack("<H", 1) + struct.pack("<H", 301)

        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "deck.ydc"
            path.write_bytes(data)

            self.assertEqual(
                requiem.parse_ydc(path),
                {"Main": [101, 102, 103], "Extra": [201, 202], "Side": [301]},
            )

    def test_convert_deck_preserves_sections_and_reports_missing_ids(self):
        le_to_passcode = {10: 1000, 20: 2000, 30: 3000}
        passcode_to_md = {1000: 9001, 3000: 9003}

        converted, missing = requiem.convert_deck(
            {"Main": [10, 20], "Extra": [30], "Side": []},
            le_to_passcode,
            passcode_to_md,
        )

        self.assertEqual(converted["Main"], [9001])
        self.assertEqual(converted["Extra"], [9003])
        self.assertEqual(converted["Side"], [])
        self.assertEqual(missing, [{"le_id": 20, "passcode": 2000, "reason": "missing_md_id"}])

    def test_convert_deck_uses_explicit_substitutions_for_missing_md_cards(self):
        converted, missing = requiem.convert_deck(
            {"Main": [4355], "Extra": [], "Side": []},
            {4355: 18807108},
            {29267084: 4675},
        )

        self.assertEqual(converted["Main"], [4675])
        self.assertEqual(missing, [])

    def test_insert_after_anchor_keeps_new_chapter_next_to_story_anchor(self):
        chapters = {
            "11010001": {"parent_chapter": 0},
            "11010002": {"parent_chapter": 11010001},
            "11010003": {"parent_chapter": 11010001},
        }

        requiem.insert_after_anchor(
            chapters,
            "11010002",
            "11010065",
            {"parent_chapter": 11010002},
        )

        self.assertEqual(list(chapters), ["11010001", "11010002", "11010065", "11010003"])

    def test_build_description_uses_story_specific_text(self):
        description = requiem.build_description(
            {"id": 186, "name": "Bonus- Everything's Relative", "series": 0},
            {"name": "Yami Yugi"},
            {"name": "Mokuba Kaiba"},
            {"name": "Yugi Deck"},
            {"name": "Mokuba Deck"},
        )

        self.assertIn("Mokuba lashes out during Duelist Kingdom", description)
        self.assertIn("Yami Yugi vs. Mokuba Kaiba", description)
        self.assertNotIn("This Requiem bonus duel", description)
        self.assertNotIn("wider cast", description)
        self.assertNotIn("Bonus:", description)

    def test_sanitize_icons_replaces_missing_profile_icon(self):
        self.assertEqual(requiem.sanitize_icons([1010093, 1000008]), [1010093, 0])


if __name__ == "__main__":
    unittest.main()
