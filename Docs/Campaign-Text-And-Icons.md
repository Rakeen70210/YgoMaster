# Campaign Text And Duelist Icons Notes

This document records what was learned while editing the Link Evolution-style campaign in this local YgoMaster setup. It is intended as a future runbook for restoring or extending campaign story text and duel-specific character icons.

## Scope

Working folder:

```text
/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster
```

Main goal:

```text
Make Link Evolution campaign duel pages show story/detail text and character-specific duel visuals instead of only generic duel labels/icons.
```

## Runtime Files

These files matter for the campaign detail panel and icons:

```text
YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt
YgoMaster/Data/Solo.json
YgoMaster/Data/SoloDuels/*.json
YgoMaster/Data/ClientData/LinkEvolution/CharNames.json
YgoMaster/Data/ClientData/LinkEvolution/UISettings.json
YgoMaster/Data/ClientData/LinkEvolution/chars.png
YgoMaster/Data/ClientData/LinkEvolution/chars_mask.png
YgoMaster/Data/ClientData/LinkEvolution/chars.dfymoo
```

### `IDS_SOLO.txt`

`YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt` controls the visible campaign text strings.

For the first Duelist Kingdom duel, the restored detail text includes:

```text
Yugi and Joey begin their journey to Duelist Kingdom with a friendly duel. It is a chance for Joey to test what Yugi has taught him before the tournament begins.

Yugi Muto vs. Joey Wheeler
```

The shorter stock/current text only showed:

```text
Yugi Muto vs. Joey Wheeler
```

So, if the story paragraph disappears, check this file first.

### `YgoMaster/Data/SoloDuels/*.json`

The campaign duel JSON files control duel-specific data such as decks, names, sleeves, mats, and the two small duel icon item IDs.

Example for `YgoMaster/Data/SoloDuels/11010001.json` after restoration:

```json
"icon": [
  1010100,
  1010029
]
```

This changed the duel from generic/default icon values toward duel-specific character icon item IDs.

Important: one backup copy of `11010048.json` was zero bytes and invalid. Do not restore from:

```text
YgoMaster/Data/Backups/campaign-detail-20260620/state-before-detail-rollback-after-launch-fail/11010048.json
YgoMaster/Data/Backups/campaign-duelist-icons-20260620/11010048.json
```

Known valid sources for `11010048.json` were:

```text
YgoMaster/Data/Backups/campaign-duelist-icons-20260620/state-before-runtime-rollback-after-launch-fail/11010048.json
YgoMaster/Data/Backups/reapply-campaign-text-icons-20260620/current-SoloDuels-before/11010048.json
YgoMaster/Data/Backups/launch-isolation-stock-data-20260620/SoloDuels/11010048.json
```

When restoring icons, prefer the runtime rollback snapshot for `11010048.json` because it contains icon-edited values:

```json
"icon": [
  1012001,
  1010027
]
```

### `YgoMaster/Data/Solo.json`

`YgoMaster/Data/Solo.json` controls campaign gate/chapter metadata. It is where the custom duel portrait mappings live.

The important fields are:

```json
"p1_img": "yugimuto",
"p2_img": "joeywheeler"
```

For the reverse duel:

```json
"p1_img": "joeywheeler",
"p2_img": "yugimuto"
```

After restoration, `YgoMaster/Data/Solo.json` had 366 `p1_img` / `p2_img` mappings.

Important implementation detail: the chapter ID is not stored as a `chapter_id` property. It is the object key, for example:

```json
"11010001": {
  "parent_chapter": 0,
  "unlock_id": 0,
  "npc_id": 1,
  "begin_sn": "",
  "set_id": 1,
  "mydeck_set_id": 1,
  "p1_img": "yugimuto",
  "p2_img": "joeywheeler"
}
```

So any merge script must merge by the object property key, not by `chapter_id`.

### `YgoMaster/Data/ClientData/LinkEvolution`

This folder provides the Link Evolution visual novel/campaign UI settings and character art lookup.

The custom portrait runtime files that were restored:

```text
YgoMaster/Data/ClientData/LinkEvolution/chars.png
YgoMaster/Data/ClientData/LinkEvolution/chars_mask.png
YgoMaster/Data/ClientData/LinkEvolution/chars.dfymoo
```

These were restored from:

```text
YgoMaster/Data/Backups/campaign-duelist-icons-20260620/disabled-custom-portrait-runtime/
```

The restored file sizes were:

```text
chars.dfymoo     6274
chars.png        11463580
chars_mask.png   1025
```

`CharNames.json` maps portrait keys to display names, for example `yugimuto`, `joeywheeler`, `yamiyugi`, etc.

`UISettings.json` controls visual layout such as title/body font settings, transforms, character offsets, and chapter-select portrait behavior.

## Backup Sources

The most useful backups created during this work were:

```text
YgoMaster/Data/Backups/campaign-detail-20260620/
YgoMaster/Data/Backups/campaign-detail-20260620/state-before-detail-rollback-after-launch-fail/
YgoMaster/Data/Backups/campaign-duelist-icons-20260620/
YgoMaster/Data/Backups/campaign-duelist-icons-20260620/disabled-custom-portrait-runtime/
YgoMaster/Data/Backups/campaign-duelist-icons-20260620/state-before-runtime-rollback-after-launch-fail/
YgoMaster/Data/Backups/reapply-campaign-text-icons-20260620/
YgoMaster/Data/Backups/launch-isolation-stock-data-20260620/
```

Use these meanings:

```text
campaign-detail-20260620/state-before-detail-rollback-after-launch-fail
  Contains expanded IDS_SOLO text and most campaign duel JSON files with icon changes.

campaign-duelist-icons-20260620/Solo.json.before-disable-p1-p2-after-launch-fail.bak
  Contains 366 p1_img/p2_img mappings.

campaign-duelist-icons-20260620/disabled-custom-portrait-runtime
  Contains custom chars.png, chars_mask.png, and chars.dfymoo.

reapply-campaign-text-icons-20260620
  Contains backups made immediately before reapplying the text/icons.
```

The smaller `IDS_SOLO.txt.bak` in `campaign-detail-20260620` is the pre-detail version, not the expanded version.

## Restoration Steps

Back up current runtime files first:

```bash
mkdir -p YgoMaster/Data/Backups/reapply-campaign-text-icons-YYYYMMDD
cp YgoMaster/Data/Solo.json YgoMaster/Data/Backups/reapply-campaign-text-icons-YYYYMMDD/Solo.json.before
cp YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt YgoMaster/Data/Backups/reapply-campaign-text-icons-YYYYMMDD/IDS_SOLO.txt.before
cp -a YgoMaster/Data/ClientData/LinkEvolution YgoMaster/Data/Backups/reapply-campaign-text-icons-YYYYMMDD/LinkEvolution.before
mkdir -p YgoMaster/Data/Backups/reapply-campaign-text-icons-YYYYMMDD/current-SoloDuels-before
for f in YgoMaster/Data/Backups/campaign-detail-20260620/state-before-detail-rollback-after-launch-fail/*.json; do
  b=$(basename "$f")
  [ -f "YgoMaster/Data/SoloDuels/$b" ] && cp "YgoMaster/Data/SoloDuels/$b" "YgoMaster/Data/Backups/reapply-campaign-text-icons-YYYYMMDD/current-SoloDuels-before/$b"
done
```

Restore expanded campaign text:

```bash
cp YgoMaster/Data/Backups/campaign-detail-20260620/state-before-detail-rollback-after-launch-fail/IDS_SOLO.txt YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt
```

Restore campaign duel JSON files:

```bash
cp YgoMaster/Data/Backups/campaign-detail-20260620/state-before-detail-rollback-after-launch-fail/*.json YgoMaster/Data/SoloDuels/
```

Then fix the known bad `11010048.json` case:

```bash
cp YgoMaster/Data/Backups/campaign-duelist-icons-20260620/state-before-runtime-rollback-after-launch-fail/11010048.json YgoMaster/Data/SoloDuels/11010048.json
```

Restore custom portrait atlas files:

```bash
cp YgoMaster/Data/Backups/campaign-duelist-icons-20260620/disabled-custom-portrait-runtime/chars.png YgoMaster/Data/ClientData/LinkEvolution/chars.png
cp YgoMaster/Data/Backups/campaign-duelist-icons-20260620/disabled-custom-portrait-runtime/chars_mask.png YgoMaster/Data/ClientData/LinkEvolution/chars_mask.png
cp YgoMaster/Data/Backups/campaign-duelist-icons-20260620/disabled-custom-portrait-runtime/chars.dfymoo YgoMaster/Data/ClientData/LinkEvolution/chars.dfymoo
```

Merge the `p1_img` / `p2_img` mappings into the current `YgoMaster/Data/Solo.json`:

```bash
node - <<'NODE'
const fs = require('fs');

const currentPath = 'YgoMaster/Data/Solo.json';
const srcPath = 'YgoMaster/Data/Backups/campaign-duelist-icons-20260620/Solo.json.before-disable-p1-p2-after-launch-fail.bak';

const current = JSON.parse(fs.readFileSync(currentPath, 'utf8'));
const src = JSON.parse(fs.readFileSync(srcPath, 'utf8'));
const byKey = new Map();

function collect(o) {
  if (Array.isArray(o)) return o.forEach(collect);
  if (!o || typeof o !== 'object') return;
  for (const [k, v] of Object.entries(o)) {
    if (v && typeof v === 'object' && !Array.isArray(v) && (v.p1_img || v.p2_img)) {
      byKey.set(k, { p1_img: v.p1_img, p2_img: v.p2_img });
    }
    collect(v);
  }
}

function apply(o) {
  if (Array.isArray(o)) return o.forEach(apply);
  if (!o || typeof o !== 'object') return;
  for (const [k, v] of Object.entries(o)) {
    const vals = byKey.get(k);
    if (vals && v && typeof v === 'object' && !Array.isArray(v)) {
      if (vals.p1_img) v.p1_img = vals.p1_img;
      if (vals.p2_img) v.p2_img = vals.p2_img;
    }
    apply(v);
  }
}

collect(src);
apply(current);
fs.writeFileSync(currentPath, JSON.stringify(current, null, 2));
console.log(`merged ${byKey.size} source p1_img/p2_img mappings`);
NODE
```

## Validation Commands

Check that the restored campaign text is present:

```bash
rg -n 'Yugi and Joey begin|Yugi Muto vs\\. Joey Wheeler' YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt
```

Check that `Solo.json` has portrait mappings:

```bash
rg -n 'p1_img|p2_img' YgoMaster/Data/Solo.json | head -20
```

Count the mappings:

```bash
node - <<'NODE'
const fs = require('fs');
const solo = JSON.parse(fs.readFileSync('YgoMaster/Data/Solo.json', 'utf8'));
let count = 0;
function walk(o) {
  if (Array.isArray(o)) return o.forEach(walk);
  if (!o || typeof o !== 'object') return;
  if (o.p1_img || o.p2_img) count++;
  Object.values(o).forEach(walk);
}
walk(solo);
console.log(count);
NODE
```

Expected count after the restore:

```text
366
```

Validate touched JSON files:

```bash
node -e "const fs=require('fs'); const files=['YgoMaster/Data/Solo.json',...fs.readdirSync('YgoMaster/Data/Backups/campaign-detail-20260620/state-before-detail-rollback-after-launch-fail').filter(f=>f.endsWith('.json')).map(f=>'YgoMaster/Data/SoloDuels/'+f)]; let bad=[]; for(const f of files){try{JSON.parse(fs.readFileSync(f,'utf8'))}catch(e){bad.push([f,e.message])}} console.log('validated json files', files.length, 'bad', bad.length); if(bad.length){for(const b of bad) console.log(b[0], b[1]); process.exit(1)}"
```

Expected result:

```text
validated json files 66 bad 0
```

Check that custom atlas files exist:

```bash
find YgoMaster/Data/ClientData/LinkEvolution -maxdepth 1 -type f -printf '%f %s\n' | sort
```

Expected custom runtime files include:

```text
chars.dfymoo
chars.png
chars_mask.png
```

## Launch Verification

The current working launcher uses:

```text
GE-Proton10-33
```

in:

```text
YgoMasterLaunch.sh
```

The line should be:

```bash
proton="${steam_root}/compatibilitytools.d/GE-Proton10-33/proton"
```

`GE-Proton10-34` caused unreliable launch/injection behavior in this setup. Valve Proton 10.0 also hit a vanilla Master Duel `coremessaging.dll.DllGetActivationFactory` issue.

Launch and verify:

```bash
./YgoMasterLaunch.sh
sleep 8
ps -ef | rg -i 'masterduel|YgoMaster|MonoRun|GE-Proton10-33|wineserver|xalia'
```

Healthy process signs:

```text
MonoRun.exe YgoMaster.exe
masterduel.exe
GE-Proton10-33/files/bin/wineserver
```

Check for new crash folders:

```bash
ls -lt ~/.local/share/Steam/steamapps/compatdata/2900371502/pfx/drive_c/users/steamuser/AppYgoMaster/Data/Local/Temp/'Konami Digital Entertainment Co., Ltd_'/masterduel/Crashes | head
```

No new folder should appear after the latest launch.

Also check the forwarders:

```bash
systemctl --user status ygomaster-forward-4989.service ygomaster-forward-4988.service --no-pager
```

Both should be active when the launcher has run.

## Lessons Learned

1. Campaign detail text and duel icons are separate systems.

   The large story text is in `IDS_SOLO.txt`. The small duel item icons are in `YgoMaster/Data/SoloDuels/*.json`. The portrait art mappings are in `YgoMaster/Data/Solo.json`.

2. `p1_img` / `p2_img` do not automatically appear unless both sides exist.

   The chapter entries in `YgoMaster/Data/Solo.json` need `p1_img` and `p2_img`, and the matching image keys must exist in the Link Evolution portrait atlas/data.

3. Do not overwrite all of `Solo.json` unless necessary.

   `Solo.json` can contain local progression or unlock edits. Merge the `p1_img` / `p2_img` fields by chapter key instead of replacing the whole file.

4. Validate JSON before launching.

   A zero-byte `11010048.json` backup caused a bad restore candidate. Always parse `YgoMaster/Data/Solo.json` plus touched `YgoMaster/Data/SoloDuels/*.json`.

5. Some YgoMaster data files support comments/trailing commas, so strict JSON checks are best limited to files known to be strict JSON.

   `YgoMaster/Data/Solo.json` and `YgoMaster/Data/SoloDuels/*.json` parsed cleanly with Node. Some other project data files can intentionally fail strict `JSON.parse`.

6. Proton problems can look like data problems.

   The campaign text was not the root cause of the launch failure. The stable launcher path for this machine ended up being `GE-Proton10-33`.

7. Process checks must run outside the sandbox when possible.

   Some earlier process checks missed host-side Proton processes. Use normal host process checks when verifying `masterduel.exe` and `MonoRun.exe`.

8. The game needs a restart to load restored campaign assets.

   After restoring text/icon files, stop `masterduel.exe`, `MonoRun.exe`, and Wine processes, then relaunch through `YgoMasterLaunch.sh`.

## Current Verified State

As of this note:

```text
YgoMaster/Data/ClientData/IDS/IDS_SOLO.txt has expanded story text.
YgoMaster/Data/Solo.json has 366 p1_img/p2_img mappings.
YgoMaster/Data/ClientData/LinkEvolution has chars.png, chars_mask.png, and chars.dfymoo.
YgoMaster/Data/SoloDuels campaign files validated cleanly.
YgoMasterLaunch.sh uses GE-Proton10-33.
Launch verification showed masterduel.exe and MonoRun.exe YgoMaster.exe running.
No new Unity crash folder appeared after the final launch.
```
