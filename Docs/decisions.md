# ygomaster decisions

## 2026-06-29: LLM opponent architecture boundary

**Decision:** For [[YGOMASTER-LLM-001]], use a thin YgoMaster adapter plus an external loopback broker for an LLM-controlled opponent. Do not start by rewriting Konami's native CPU AI, patching `duel.dll` internals, or relying on guessed memory offsets.

**Why:** The current checkout already exposes duel command, phase, dialog, list, and state query surfaces through `YgoMasterClient/DuelDll.cs`, `YgoMasterClient/DuelDll.ProxyFunctions.cs`, and `YgoMasterServer/Pvp.cs`. Those surfaces can support state extraction, legal-action enumeration, and validated action commit with much lower version risk than native-memory injection.

**Rejected for first milestone:** direct remote API calls from the game process, storing API keys in client config, memory-offset hijacking, headless-client control before a basic commit path works, and any live/ranked automation.
