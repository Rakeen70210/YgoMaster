import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("linearize_requiem_campaign_layout.py")


spec = importlib.util.spec_from_file_location("linearize_requiem_campaign_layout", MODULE_PATH)
linearize = importlib.util.module_from_spec(spec)
spec.loader.exec_module(linearize)


class LinearizeRequiemCampaignLayoutTests(unittest.TestCase):
    def test_linearizes_forward_duels_and_parents_reverse_duels_to_forward(self):
        chapters = {
            "11010001": {"parent_chapter": 0, "p1_img": "a", "p2_img": "b"},
            "11010002": {"parent_chapter": 11010001, "p1_img": "b", "p2_img": "a"},
            "11010003": {"parent_chapter": 11010001, "p1_img": "c", "p2_img": "d"},
            "11010065": {"parent_chapter": 11010002, "p1_img": "e", "p2_img": "f"},
            "11010100": {"parent_chapter": 11010065, "p1_img": "f", "p2_img": "e"},
        }

        linearize.linearize_gate(chapters)

        self.assertEqual(chapters["11010001"]["parent_chapter"], 0)
        self.assertEqual(chapters["11010003"]["parent_chapter"], 11010001)
        self.assertEqual(chapters["11010065"]["parent_chapter"], 11010003)
        self.assertEqual(chapters["11010002"]["parent_chapter"], 11010001)
        self.assertEqual(chapters["11010100"]["parent_chapter"], 11010065)
        self.assertEqual(list(chapters), ["11010001", "11010002", "11010003", "11010065", "11010100"])

    def test_same_matchup_rematches_are_not_treated_as_reverse_without_matching_parent(self):
        chapters = {
            "11010001": {"parent_chapter": 0, "p1_img": "a", "p2_img": "b"},
            "11010002": {"parent_chapter": 11010001, "p1_img": "b", "p2_img": "a"},
            "11010003": {"parent_chapter": 11010001, "p1_img": "c", "p2_img": "d"},
            "11010004": {"parent_chapter": 11010003, "p1_img": "b", "p2_img": "a"},
        }

        linearize.linearize_gate(chapters)

        self.assertEqual(chapters["11010003"]["parent_chapter"], 11010001)
        self.assertEqual(chapters["11010004"]["parent_chapter"], 11010003)
        self.assertEqual(list(chapters), ["11010001", "11010002", "11010003", "11010004"])

    def test_uses_explicit_forward_order_when_provided(self):
        chapters = {
            "11010001": {"parent_chapter": 0, "p1_img": "a", "p2_img": "b"},
            "11010002": {"parent_chapter": 11010001, "p1_img": "b", "p2_img": "a"},
            "11010003": {"parent_chapter": 11010001, "p1_img": "c", "p2_img": "d"},
            "11010004": {"parent_chapter": 11010003, "p1_img": "d", "p2_img": "c"},
            "11010065": {"parent_chapter": 11010003, "p1_img": "e", "p2_img": "f"},
            "11010100": {"parent_chapter": 11010065, "p1_img": "f", "p2_img": "e"},
        }

        linearize.linearize_gate(chapters, ["11010001", "11010065", "11010003"])

        self.assertEqual(chapters["11010001"]["parent_chapter"], 0)
        self.assertEqual(chapters["11010065"]["parent_chapter"], 11010001)
        self.assertEqual(chapters["11010003"]["parent_chapter"], 11010065)
        self.assertEqual(chapters["11010002"]["parent_chapter"], 11010001)
        self.assertEqual(chapters["11010100"]["parent_chapter"], 11010065)
        self.assertEqual(chapters["11010004"]["parent_chapter"], 11010003)
        self.assertEqual(
            list(chapters),
            ["11010001", "11010002", "11010065", "11010100", "11010003", "11010004"],
        )


if __name__ == "__main__":
    unittest.main()
