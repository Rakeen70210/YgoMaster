from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]


class LeCombinedSourceTests(unittest.TestCase):
    def assert_source_contains(self, relative_path, *markers):
        source = (ROOT / relative_path).read_text(encoding="utf-8")
        for marker in markers:
            with self.subTest(path=relative_path, marker=marker):
                self.assertIn(marker, source)

    def test_solo_campaign_extensions_are_present(self):
        self.assert_source_contains(
            "YgoMasterServer/Acts/Act_Solo.cs",
            "IsLeChapter",
            "IsMysteryDuelChapter",
            "ItemID.Category.PACK_TICKET",
            "DecksByShopItemId",
            "UpgradeLoanerDeckFinish",
        )

    def test_duel_extensions_are_present(self):
        self.assert_source_contains(
            "YgoMasterServer/Acts/Act_Duel.cs",
            "UpgradeLoanerDeckFinish",
            "UpgradeCpuDeckFinish",
            "RandomiseLEChapterBattlefield",
            "UsePlayerBattlefieldForLEChapter",
        )

    def test_shop_extensions_are_present(self):
        self.assert_source_contains(
            "YgoMasterServer/Infos/ShopInfo.cs",
            "DecksByShopItemId",
            "BundlesByShopItemId",
            "MinPackRarityRates",
            "MinPackStyleRates",
            "AltArtRate",
        )
        self.assert_source_contains(
            "YgoMasterServer/GameServer.State.cs",
            "UnlockSecrets(Player player)",
            '"UpgradeLoanerDeckToOwnedAltArts"',
            '"RandomiseLEChapterBattlefield"',
            '"UsePlayerBattlefieldForLEChapter"',
            '"minPackRate"',
            '"altArtRate"',
        )

    def test_duel_settings_support_campaign_overrides(self):
        self.assert_source_contains(
            "YgoMasterServer/DuelSettings.cs",
            "SetP2ItemValue",
            "GetP1ItemValue",
            "SetDeck",
        )


if __name__ == "__main__":
    unittest.main()
