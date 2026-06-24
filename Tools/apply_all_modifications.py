#!/usr/bin/env python3
"""
Master pipeline runner for Yu-Gi-Oh! Master Duel YgoMaster Requiem + LE modifications.
Executes all setup, import, layout modification, reward injection, and validation scripts
in the correct chronological order to prevent data corruption, parent cycles, or missing rewards.
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_ROOT = ROOT / "YgoMaster" / "Data"

STEPS = [
    # 1. Import optional story duels (rewrites Solo.json)
    ("Tools/import_requiem_optional_duels.py", ["--apply"]),
    # 2. Import challenge decks (rewrites Solo.json)
    ("Tools/import_requiem_challenge_decks.py", ["--apply"]),
    # 3. Linearize map layout to main story spine + top/bottom reverse rows (rewrites Solo.json)
    ("Tools/linearize_requiem_campaign_layout.py", ["--apply"]),
    # 4. Add reverse duels (rewrites Solo.json)
    ("Tools/add_requiem_reverse_duels.py", ["--apply"]),
    # 5. Inject custom LE enemy-deck reward drops (rewrites Solo.json, runs last among data editors)
    ("Tools/inject_le_enemy_deck_rewards.py", ["--apply"]),
    # 6. Validate injected enemy-deck rewards
    ("Tools/validate_le_enemy_deck_rewards.py", []),
    # 7. Validate Requiem campaign structure and text
    ("Tools/validate_requiem_campaigns.py", []),
    # 8. Validate extracted campaign portraits
    ("Tools/validate_campaign_portraits.py", []),
]

def run_step(script_rel_path, args):
    script_path = ROOT / script_rel_path
    cmd = ["python3", str(script_path)] + args
    print(f"\n>>> Running: {' '.join(cmd)}")

    # Run process
    res = subprocess.run(cmd, cwd=str(ROOT))
    if res.returncode != 0:
        print(f"ERROR: Step '{script_rel_path}' failed with exit code {res.returncode}.", file=sys.stderr)
        return False
    return True

def main():
    print("=" * 70)
    print("Starting YgoMaster Requiem & LE Enemy-Deck Reward Setup Pipeline...")
    print("=" * 70)

    for script, args in STEPS:
        if not run_step(script, args):
            print("\nPipeline failed. Please fix the error above and re-run.", file=sys.stderr)
            return 1

    print("\n" + "=" * 70)
    print("SUCCESS: All import, layout, reward injection, and validation steps completed!")
    print("CRITICAL: You must restart YgoMaster.exe for changes to take effect in game.")
    print("=" * 70)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
