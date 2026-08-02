# NS4 native semantic evidence

NS4 is intentionally resolved as `NS4_INFEASIBLE`. The bounded evidence pass
did not prove the native meanings required to decode a candidate into a safe
visible attack record, so this directory contains only the fail-closed
manifest and its validator. It contains no decoder vectors and no production
trace.

## Pinned input

- Binary: `../masterduel_Data/Plugins/x86_64/duel.dll`
- Size: `19,443,200` bytes
- SHA-256: `97bd4d136e39b0872e4a8a9171632f1f0bd9bb04d69af08e836c56e684d43c44`
- Image base: `0x180000000`
- Manifest canonical SHA-256: `56410eb64c666845d4b1f4ad4407c8d9fd2fc8fc0689d7e17cfa1034db2c2787`

## Reproduction

The temporary evidence directory used for this pass was:
`/tmp/ygomaster-ns4-ktTX8vsC`.

The full disassembly and PE listing were generated with:

```text
llvm-objdump -d --x86-asm-syntax=intel ../masterduel_Data/Plugins/x86_64/duel.dll > /tmp/ygomaster-ns4-ktTX8vsC/duel.disassembly.txt
llvm-objdump -p ../masterduel_Data/Plugins/x86_64/duel.dll > /tmp/ygomaster-ns4-ktTX8vsC/duel.pe.txt
```

Their hashes were:

```text
1b4a5f82d62ce74346295923e1a9b788bdcf551504087e00d6b77475adcc0efe  duel.disassembly.txt
e929852d69fe30d55e6bdd3f3a123f9aff6497fc56fd081453b40983f540391d  duel.pe.txt
```

The four mandatory bounded windows were extracted without changing the binary:

```text
window 1: 0x18063a380-0x18063a5b9, materializer, action-family/instance/target fields
window 2: 0x18002b3e0-0x18002b600, direct helper called by the materializer, field meaning
window 3: 0x1805cc460-0x1805ccb50, candidate consumer, action-work consumer meaning
window 4: 0x1805d2610-0x1805d3200, asynchronous CPU action driver, action-work dispatch meaning
```

Before inspecting the named API implementations, the following six additional
windows were admitted against the plan's eight-window ceiling. Each initial
stop was the permitted `0x2000`-byte cap. Four export entries are jump thunks;
their target implementations were followed in the same admitted window. The
retained artifacts are shortened to the first return, with the complete direct
`DLL_DuelGetCardTurn` body retained through the next export.

```text
additional window 1: 0x180013280-0x180015280, export thunk 0x180eaa340 jumps here, DLL_DuelGetAttackTargetMask / target-domain evidence
additional window 2: 0x1800a7320-0x1800a9320, export thunk 0x180ea9e10 jumps here, DLL_DuelGetCardBasicVal / public-stat query evidence
additional window 3: 0x180599e50-0x18059be50, export thunk 0x180eaa3c0 jumps here, DLL_DuelGetCardFace / visibility-query evidence
additional window 4: 0x180eaa380-0x180eac380, export RVA 0xeaa380, DLL_DuelGetCardTurn / stance-domain evidence
additional window 5: 0x180599d00-0x18059bd00, export thunk 0x180eaa370 jumps here, DLL_DuelGetCardUniqueID / instance-query evidence
additional window 6: 0x180ea9d70-0x180eabd70, export RVA 0xea9d70, DLL_DuelGetLP / public-LP query evidence
```

The retained implementation ranges are:

```text
0x180013280-0x180013329  DLL_DuelGetAttackTargetMask target implementation
0x1800a7320-0x1800a77e2  DLL_DuelGetCardBasicVal target implementation
0x180599e50-0x180599e89  DLL_DuelGetCardFace target implementation
0x180eaa380-0x180eaa3c0  DLL_DuelGetCardTurn direct implementation
0x180599d00-0x180599d3d  DLL_DuelGetCardUniqueID target implementation
0x180ea9d70-0x180ea9d8f  DLL_DuelGetLP direct implementation
```

The resulting byte-window and bounded-reference hashes were:

```text
547a67e1ba25c9268113d05b496e82d82b4fa32434abfe6b315d107377d98a06  materializer.txt
85053b458a368c8b6a4ad37ad526ef46f827614b2b2af0743a69097659030d5b  materializer-helper.txt
f7a656a71b7d15d6c2cae8c7745cc118c15ea575170b2f2299597ca6dd6375f7  candidate-state.txt
a8a39af06f8bb892ab433bddc2864b63a23d3fb601d3a210f5ecdac773ad0619  action-driver.txt
94e8addec389c01cb72f8191a2f9afa7bb8a0188de9c270b447cdd060ed9f0d2  known-xrefs.txt
299913b78e1d3e6733f546e1313486ff57476d24264487220de41877cba9e605  named-api-exports.txt
db9180d5d3064fd2833a4840367d0d0e0ddb8b06f81aaf527fb465f0ec792c0d  attack-target-mask-implementation.txt
9fb7ed7e6fa2bbd1aa7bcd9fceadd1b0bed5d4ddfdd19dde68dd84c6fceeb0ce  card-basic-val-implementation.txt
ae978572138a8617117cbf0e942081b9f8817d10069f903fde938444bdd2f12e  card-face-implementation.txt
fe3fd0644cbd5e074bb77b4fac8369a478cb5332fe57088fa40a6a30e0ddaf25  card-turn-implementation.txt
b1c2dc467acfd4ad6a21fc2f482807c20e38f045a08fc2893abe37aa88591ccd  card-unique-id-implementation.txt
72595dc31a2290328ddabbfafaad851d3d53ae906f91b2956a0ae02e22957a8b  lp-implementation.txt
```

The corresponding raw PE byte-window SHA-256 values are normative:

```text
8a575aad9fbbacf37533e1c68826812a95ba51efd3fba1939172611e5c53e691  0x180013280-0x180013329
c1b87cd7c4725633fff1b4e4e684709d11be05f23e1ab17fb363545ef2a95108  0x1800a7320-0x1800a77e2
b76e20b39bf78dec0be8dd9e96b344c2f56fa6b74997f8456870c99b645bf94f  0x180599e50-0x180599e89
a46e7d5516cc1e9dfe81f80618760e4d1a7a71bcaf6cb06ba1e39d5688e44861  0x180eaa380-0x180eaa3c0
79704116d474f30c69cf3bb98efb31703c88f258cdcb8e1641697285b1aac233  0x180599d00-0x180599d3d
966c797576850cb291998bc25b2ebdcfcf7d29b3bd4c99cfb7ef0a28ed24821d  0x180ea9d70-0x180ea9d8f
```

The search stayed within the mandatory materializer/helper/consumer/driver
windows, their bounded shared-work references, the six named API exports and
their admitted implementations, and the existing wrapper sources. Six of the
eight permitted additional windows were used. No other disassembly window,
network lookup, decompiler, symbol download, runtime process, or duel was used.
The temporary files are recoverable under `/tmp` and are not checked in.

## Evidence conclusion

All six required rows remain `unproven` in `manifest.json`:

1. `action_family`: the materializer copies compact fields to action work, but
   the bounded consumer/driver evidence does not provide a complete,
   independently proven direct/monster/non-attack discriminator.
2. `attacker_instance_id`: no bounded producer/consumer mapping proves an
   attacker unique-instance field.
3. `target_or_direct`: no bounded mapping proves a target unique-instance
   field together with a direct/no-target marker. The named attack-target-mask
   implementation constructs a zone/direct mask but does not bind any compact
   candidate field to a target unique ID or direct marker.
4. `battle_position`: the bounded evidence does not establish the complete raw
   `DLL_DuelGetCardTurn` attack/defense domain at this boundary. The named API
   returns a raw byte; its implementation does not name the complete values.
5. `public_state_apis`: the existing wrappers expose individual public facts,
   and the named implementations expose LP, face, raw turn, compact unique ID,
   and basic-value mechanics, but this pass cannot prove the complete safe
   read-only query set at the native callback boundary without identity reads.
6. `boundary_timing`: the frozen NS2 trace establishes the prior capture seam,
   but the bounded static call chain does not independently prove that every
   required public query is initialized and safe there.

The native observations remain observations, not meanings. In particular, no
meaning was assigned to the compact words or auxiliary values from the frozen
NS2 rows.

## Result and authority

The non-strict analyzer reports `NS4_INFEASIBLE` and exits `0`; the strict
`--require-static-evidence-gate` and `--require-ns4-gate` modes exit `1`.
`production_evidence`, deployment, trace enablement, next-duel, and rerank
authority all remain `false`. Because a required semantic row is unproven, the
plan stops before decoder vectors, C# integration, client hook changes, or a
future semantic-trace analyzer.
