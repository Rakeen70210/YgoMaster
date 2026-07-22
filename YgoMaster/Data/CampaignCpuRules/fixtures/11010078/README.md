# 11010078 PR3 golden contract

Mined from live `pr3_capture_shadow` duel (2026-07-21).

| Case | Fixture | Expected |
|------|---------|----------|
| G1 | `g1_opening_a_main1.json` | `RuleCommit` / `g1-opening-special-or-action` / `Command\|SummonSp\|12485\|13\|1|` (Kaiser Vorse Raider) |
| G2 | `g2_opening_a_without_top_summon_sp.json` | `RuleCommit` / same rule prefer[1] / `Command\|Summon\|12292\|13\|2|` (Sage with Eyes of Blue) |
| G3 | `g3_move_only_no_match.json` | `FallbackNative` / `no_match` |
| G4 | `g4_non_main_run_dialog.json` | non-Main → NativeLease policy (`RunDialog`) |
| G5 | `g5_deck_fingerprint_mismatch.json` | gate inactive on hash mismatch |
| G6 | `g6_identical_observation_twice.json` | identical obs → same identity + rule_id |

Stable identity uses `kind|command_or_phase|card_id|position|index_or_phase|target_scope` — never transient `action_id`.

Raw capture: `raw/pr3_capture_20260721T235038_campaign_cpu_decision.json`.
