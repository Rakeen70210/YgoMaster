# YgoMaster Skill Library

Reference skills for maintaining this project — written so a new engineer or an AI session with no prior context can debug, extend, validate, and advance YgoMaster at the standard the project has held so far.

**Start every session with [ygomaster-map](ygomaster-map/SKILL.md).** It holds the project map, the traps that waste whole sessions (link-compiled sources, old-style csproj, no CI, the two-machine split, stale docs), and routes to the right skill below.

## Index

| Skill | Load when |
|---|---|
| [ygomaster-map](ygomaster-map/SKILL.md) | Starting any task; orienting; deciding blast radius; judging doc trustworthiness |
| [llm-broker-pipeline](llm-broker-pipeline/SKILL.md) | Working on the LLM opponent: broker, legal actions, `run_effect_seq`, validation, commit, fallback |
| [debugging-llm-decisions](debugging-llm-decisions/SKILL.md) | Broker actions not committing; `llm_broker_rejected`/`commit_skipped`; duel hangs; bad decisions |
| [validating-changes](validating-changes/SKILL.md) | Before declaring anything done; choosing which builds/tests to run (there is no CI) |
| [extending-broker-actions](extending-broker-actions/SKILL.md) | Adding action kinds, schema fields, public state, providers to the broker |
| [hooking-il2cpp](hooking-il2cpp/SKILL.md) | Adding/fixing client hooks; duel.dll calls; crashes inside masterduel.exe; thread/GC issues |
| [recovering-from-game-updates](recovering-from-game-updates/SKILL.md) | Master Duel updated and something broke; importing fresh game data |
| [editing-campaign-content](editing-campaign-content/SKILL.md) | Solo.json, SoloDuels, LE/Requiem duels, campaign text, portraits, import/validation tools |
| [extending-server-acts](extending-server-acts/SKILL.md) | Server endpoints, player data, shop/solo logic, persistence, server threading |
| [running-the-runtime](running-the-runtime/SKILL.md) | Build/deploy/launch; Proton; two-client PvP setup; ports; process hygiene |

## Conventions for maintaining this library

- Skills reference **file paths and symbol names**, not line numbers (they rot). If a skill contradicts the code, the code wins — fix the skill in the same commit as the change that invalidated it.
- Descriptions state *when to load the skill*, not what it contains.
- When you finish a piece of work whose absence from these skills cost you real time, add the missing fact to the most specific skill — keep each one loadable on its own.
- Deeper history and rationale live in the Obsidian vault only: `/home/rakeenhuq/Onedrive/Obsidian Vault/projects/ygomaster/` (`work/YGOMASTER-LLM-00N…`, `decisions.md`). Skills and operational `Docs/*.md` link there rather than duplicating.
