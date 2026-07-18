# YgoMaster — Agent Instructions

Before starting any task in this repo, read `skills/README.md`. It routes to
ten task-specific skills; always load `skills/ygomaster-map/SKILL.md` first
(project map, blast-radius traps, which docs to trust), then the skill matching
your task (debugging, validating, extending, campaign data, hooks, deploys).

## Project memory (vault-only)

Durable work items, decisions, and progress live **only** in the Obsidian vault:

`/home/rakeenhuq/Onedrive/Obsidian Vault/projects/ygomaster/`

- Index: `…/projects/ygomaster/INDEX.md`
- Decisions: `…/projects/ygomaster/decisions.md`
- Work items: `…/projects/ygomaster/work/YGOMASTER-LLM-00N *.md`

Use the `obsidian-project-memory` skill and `vault-retriever` subagent. Wiki-links
like `[[YGOMASTER-LLM-004]]` resolve in the vault (via frontmatter aliases), not
as repo paths. **Do not** recreate repo `Docs/work/`, root `work/`,
`INDEX.md`, `decisions.md`, or `Docs/decisions.md` — those mirrors are
gitignored.

Repo `skills/` and operational `Docs/*.md` (e.g. `Docs/LlmBroker.md`) remain the
source of truth for *how to run/build this codebase*.

Non-negotiables the skills expand on:

- There is no CI. `skills/validating-changes/SKILL.md` is the merge gate — run it.
- `YgoMasterServer/Llm/*.cs` is link-compiled into four csproj files; grep the
  csproj files for any file you touch before judging impact.
- When code and docs disagree, code wins — fix the doc in the same commit.
- Never run a plain `dotnet build` on the game machine; always redirect output
  (`-p:OutputPath=/tmp/ygomaster-build/solution/`).
