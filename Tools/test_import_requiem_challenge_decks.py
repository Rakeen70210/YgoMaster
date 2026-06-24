import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("import_requiem_challenge_decks.py")
spec = importlib.util.spec_from_file_location("import_requiem_challenge_decks", MODULE_PATH)
challenge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(challenge)


class RequiemChallengeImportTests(unittest.TestCase):
    def test_select_requiem_challenge_decks_uses_only_requiem_hard_files(self):
        decks = [
            {"id": 1, "series": 0, "file": "1classic_hard_arkana"},
            {"id": 626, "series": 0, "file": "requiem81_noah_hard"},
            {"id": 625, "series": 0, "file": "requiem80_grace"},
            {"id": 684, "series": 1, "file": "requiem138_don_hard"},
        ]

        selected = challenge.select_requiem_challenge_decks(decks)

        self.assertEqual([d["id"] for d in selected], [626, 684])

    def test_challenge_gate_for_series_routes_chazz_fallback_to_gx(self):
        self.assertEqual(challenge.challenge_gate_for_series(0), 1111)
        self.assertEqual(challenge.challenge_gate_for_series(5), 1116)
        self.assertEqual(challenge.challenge_gate_for_series(-1), 1112)

    def test_resolve_deck_file_uses_known_requiem_alias(self):
        self.assertEqual(
            challenge.resolve_deck_file("requiem137_chazz_hard").name,
            "requiem136_chazz_hard.ydc",
        )

    def test_build_challenge_duel_keeps_player_deck_empty(self):
        duel = challenge.build_challenge_duel(
            chapter_id=11110025,
            template={
                "Deck": [{"Main": {"CardIds": [1], "Rare": [1]}, "Extra": {"CardIds": [], "Rare": []}, "Side": {"CardIds": [], "Rare": []}}],
                "chapter": 11110001,
                "name": ["", "Arkana"],
                "icon": [0, 1010028],
            },
            opponent_name="Noah Kaiba",
            opponent_deck={"Main": [100], "Extra": [200], "Side": []},
        )

        self.assertEqual(duel["Duel"]["chapter"], 11110025)
        self.assertEqual(duel["Duel"]["name"], ["", "Noah Kaiba"])
        self.assertEqual(duel["Duel"]["Deck"][0]["Main"]["CardIds"], [])
        self.assertEqual(duel["Duel"]["Deck"][1]["Main"]["CardIds"], [100])


if __name__ == "__main__":
    unittest.main()
