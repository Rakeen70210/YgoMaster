import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("compact_requiem_campaign_layout.py")


spec = importlib.util.spec_from_file_location("compact_requiem_campaign_layout", MODULE_PATH)
compact = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compact)


class CompactRequiemCampaignLayoutTests(unittest.TestCase):
    def test_reparents_added_duels_when_anchor_already_has_two_children(self):
        solo = {
            "chapter": {
                "1101": {
                    "11010001": {"parent_chapter": 0},
                    "11010002": {"parent_chapter": 11010001},
                    "11010003": {"parent_chapter": 11010001},
                    "11010065": {"parent_chapter": 11010001},
                }
            }
        }
        report = {
            "added": [
                {
                    "chapter_id": 11010065,
                    "gate": 1101,
                    "anchor_id": 11010001,
                    "parent_after_reconcile": 11010001,
                }
            ]
        }

        changes = compact.compact_requiem_layout(solo, report)

        self.assertEqual(changes, [{"chapter_id": 11010065, "from": 11010001, "to": 11010002}])
        self.assertEqual(solo["chapter"]["1101"]["11010065"]["parent_chapter"], 11010002)
        self.assertEqual(report["added"][0]["layout_parent_after_compact"], 11010002)
        self.assertLessEqual(compact.max_child_count(solo["chapter"]["1101"]), 2)


if __name__ == "__main__":
    unittest.main()
