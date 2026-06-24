import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("add_requiem_reverse_duels.py")
spec = importlib.util.spec_from_file_location("add_requiem_reverse_duels", MODULE_PATH)
reverse_duels = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reverse_duels)


class AddRequiemReverseDuelsTests(unittest.TestCase):
    def test_reverse_id_uses_gate_specific_100_series(self):
        self.assertEqual(reverse_duels.reverse_chapter_id("1102", 1), 11020100)
        self.assertEqual(reverse_duels.reverse_chapter_id("1106", 23), 11060122)

    def test_build_reverse_chapter_swaps_portraits_and_parents_to_forward(self):
        forward = {
            "parent_chapter": 11020029,
            "unlock_id": 0,
            "npc_id": 1,
            "begin_sn": "",
            "set_id": 1,
            "mydeck_set_id": 1,
            "p1_img": "velliancrowler",
            "p2_img": "bonaparte",
        }

        reverse = reverse_duels.build_reverse_chapter(forward, 11020065)

        self.assertEqual(reverse["parent_chapter"], 11020065)
        self.assertEqual(reverse["p1_img"], "bonaparte")
        self.assertEqual(reverse["p2_img"], "velliancrowler")
        self.assertEqual(reverse["unlock_id"], 0)
        self.assertNotIn("unlock_secret", reverse)

    def test_build_reverse_duel_swaps_names_and_decks(self):
        forward = {
            "Duel": {
                "chapter": 11020065,
                "name": ["Dr. Vellian Crowler", "Vice Chancellor Bonaparte"],
                "Deck": ["crowler deck", "bonaparte deck"],
                "icon": [1000008, 1010093],
            }
        }

        reverse = reverse_duels.build_reverse_duel(forward, 11020100)

        self.assertEqual(reverse["Duel"]["chapter"], 11020100)
        self.assertEqual(reverse["Duel"]["name"], ["Vice Chancellor Bonaparte", "Dr. Vellian Crowler"])
        self.assertEqual(reverse["Duel"]["Deck"], ["bonaparte deck", "crowler deck"])
        self.assertEqual(reverse["Duel"]["icon"], [0, 1010093])

    def test_build_reverse_description_removes_requiem_meta_wording(self):
        description = reverse_duels.build_reverse_description(
            "Dormitory Demolition",
            "Vice Chancellor Bonaparte",
            "Dr. Vellian Crowler",
        )

        self.assertIn("Dormitory Demolition (Reverse Duel)", description)
        self.assertIn("Vice Chancellor Bonaparte vs. Dr. Vellian Crowler", description)
        self.assertNotIn("Requiem", description)
        self.assertNotIn("swaps the loaner decks", description)


if __name__ == "__main__":
    unittest.main()
