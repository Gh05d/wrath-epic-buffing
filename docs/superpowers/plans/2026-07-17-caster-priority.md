# Caster Priority (Manual Caster Rank) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users rank casters (globally per character + per-buff override) so a preferred caster casts a shared buff first, overriding the prepared/spontaneous heuristic.

**Architecture:** A per-unit global rank dict on `SavedBufferState` plus a nullable per-caster override on `SavedCasterState`/`BuffProvider`; effective rank = `override ?? global ?? 0` becomes a new sort key in `BubbleBuff.SortProviders()` between source type and the heuristic. UI is two −/+ rows in the existing caster popout. Spec: `docs/superpowers/specs/2026-07-17-caster-priority-design.md`.

**Tech Stack:** C#/.NET Framework 4.8.1 Unity mod (Harmony/UMM). No automated test infra — verification is compile + Steam Deck smoke test.

## Global Constraints

- Build: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo` from repo root. Expected: `Build succeeded.` (`findstr` warnings are harmless on Linux.)
- K&R braces (opening brace same line), 4-space indent, `var` when type is apparent.
- Higher rank casts **earlier**. Rank sorts **below** active-before-reserve and source type, **above** the prepared/spontaneous heuristic.
- Defaults (`0` / `null`) everywhere ⇒ behavior identical to today. No save migration.
- Every new locale key goes into ALL five `Config/*.json` files. A key missing from `en_GB.json` crashes the game. BOM: `en_GB`/`de_DE` have UTF-8 BOM, `fr_FR`/`ru_RU`/`zh_CN` don't — preserve each file's state.
- Commit directly on `master` (user preference). Commits end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- `docs/` is gitignored — plan/spec commits need `git add -f`.

---

### Task 1: Data model, persistence, sort integration

**Files:**
- Modify: `BuffIt2TheLimit/SaveState.cs` (SavedBufferState ~line 44, SavedCasterState ~line 78, delete dead field ~line 103)
- Modify: `BuffIt2TheLimit/BubbleBuff.cs` (SortProviders ~552, Priority getter ~679, Banned/CustomCap block ~658, InitialiseFromSave ~242)
- Modify: `BuffIt2TheLimit/BufferState.cs` (updateSavedBuff ~679, retention checks ~700)

**Interfaces:**
- Consumes: existing `BuffProvider.Banned`/`CustomCap` persistence pattern; `GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState` (already used in `SortProviders`).
- Produces (Task 3 relies on these exact names):
  - `SavedBufferState.CasterRanks` — `Dictionary<string, int>` (unit UniqueId → rank, only non-zero entries)
  - `SavedCasterState.PriorityOverride` — `int?` (null = inherit global)
  - `BuffProvider.PriorityOverride` — `int?`
  - `BuffProvider.EffectiveRank(Dictionary<string, int> globalRanks)` — `int`
  - `BuffProvider.HeuristicPriority` — `int` (renamed from `Priority`)

- [ ] **Step 1: SaveState.cs — add fields, delete dead field**

In `SavedBufferState`, after the `GlobalSourcePriority` property (line ~44):

```csharp
        [JsonProperty]
        public SourcePriority GlobalSourcePriority = SourcePriority.SpellsScrollsPotions;
        [JsonProperty]
        // Global caster rank per unit UniqueId — higher rank casts earlier.
        // Only non-zero entries are stored; a missing unit means rank 0.
        public Dictionary<string, int> CasterRanks = new();
```

In `SavedCasterState`, after `Cap` (line ~78):

```csharp
        [JsonProperty]
        public int Cap = -1;
        // null = inherit the global CasterRanks value for this unit.
        [JsonProperty]
        public int? PriorityOverride;
```

In `SavedBuffState`, DELETE the dead, never-read field (lines 103-104; removal needs no migration — unknown JSON fields are ignored on load):

```csharp
        [JsonProperty]
        public List<string> CasterPriority;
```

- [ ] **Step 2: BubbleBuff.cs — BuffProvider field, rename, EffectiveRank**

In `BuffProvider`, extend the Banned/CustomCap block (line ~658):

```csharp
        public bool Banned = false;
        public int CustomCap = -1;
        // Manual caster rank override for this buff — null inherits the global
        // per-unit rank (SavedBufferState.CasterRanks). Higher rank casts earlier.
        public int? PriorityOverride;
```

Rename the heuristic getter (line ~679) — signature line only, body unchanged:

```csharp
        public int HeuristicPriority {
            get {
                if (book == null)
                    return 0;

                if (book.Blueprint.Spontaneous) {
                    return 100 - book.CasterLevel;
                } else {
                    return 0 - book.CasterLevel;
                }
            }
        }
```

Add directly below it:

```csharp
        // Effective manual rank: per-buff override wins, else the global
        // per-unit rank, else 0. Higher casts earlier (SortProviders).
        public int EffectiveRank(Dictionary<string, int> globalRanks) =>
            PriorityOverride ?? (globalRanks != null && globalRanks.TryGetValue(who.UniqueId, out var rank) ? rank : 0);
```

- [ ] **Step 3: BubbleBuff.cs — SortProviders sort key**

In `SortProviders()` (line ~552), fetch the ranks dict next to the existing SavedState access:

```csharp
            var globalPriority = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState?.GlobalSourcePriority
                ?? SourcePriority.SpellsScrollsPotions;
            var casterRanks = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState?.CasterRanks;
```

Insert the rank comparison after the source-weight comparison (after `return aSourceWeight - bSourceWeight;`, line ~576) and rename the heuristic usages:

```csharp
                if (aSourceWeight != bSourceWeight)
                    return aSourceWeight - bSourceWeight;

                // Manual caster rank — user override, higher casts earlier. Sits
                // below active/reserve and source type (a rank must not pull a
                // reserve companion or a scroll ahead), above the heuristic.
                int aRank = a.EffectiveRank(casterRanks);
                int bRank = b.EffectiveRank(casterRanks);
                if (aRank != bRank)
                    return bRank - aRank;

                if (a.HeuristicPriority == b.HeuristicPriority) {
                    int aScore = 0;
                    int bScore = 0;

                    if (!a.SelfCastOnly)
                        aScore += 10_000;
                    if (!b.SelfCastOnly)
                        bScore += 10_000;

                    return aScore - bScore;
                } else {
                    return a.HeuristicPriority - b.HeuristicPriority;
                }
```

(`a.Priority`/`b.Priority` had exactly two usage lines, both here — verified via grep; nothing else references the getter.)

- [ ] **Step 4: BubbleBuff.cs — load in InitialiseFromSave**

In the caster loop (line ~242), add one line:

```csharp
            foreach (var caster in CasterQueue) {
                if (state.Casters.TryGetValue(caster.Key, out var casterState)) {
                    caster.Banned = casterState.Banned;
                    caster.CustomCap = casterState.Cap;
                    caster.PriorityOverride = casterState.PriorityOverride;
                    caster.ShareTransmutation = casterState.ShareTransmutation;
```

- [ ] **Step 5: BufferState.cs — save + retention**

In `updateSavedBuff`'s caster loop (line ~679), add one line:

```csharp
                    state.Banned = caster.Banned;
                    state.Cap = caster.CustomCap;
                    state.PriorityOverride = caster.PriorityOverride;
                    state.ShareTransmutation = caster.ShareTransmutation;
```

Extend BOTH retention checks (lines ~700-707) — without this, an override on a buff with nothing `wanted` evaporates on save:

```csharp
            bool hasCasterConfig(BubbleBuff buff) => buff.CasterQueue.Any(c =>
                c.Banned || c.CustomCap != -1 || c.PriorityOverride != null
                || c.ShareTransmutation || c.PowerfulChange
                || c.ReservoirCLBuff || c.AzataZippyMagic);
            // For retention, check the SAVED dict, not the current queue: it also holds
            // config for providers temporarily absent (scrolls depleted, caster benched).
            bool hasSavedCasterConfig(SavedBuffState save) => save.Casters.Values.Any(c =>
                c.Banned || c.Cap != -1 || c.PriorityOverride != null
                || c.ShareTransmutation || c.PowerfulChange
                || c.ReservoirCLBuff || c.UseAzataZippyMagic);
```

- [ ] **Step 6: Build**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded.` If `Priority` was referenced anywhere missed by the rename, this fails with CS1061 — fix the call site to `HeuristicPriority`.

- [ ] **Step 7: Commit**

```bash
git add BuffIt2TheLimit/SaveState.cs BuffIt2TheLimit/BubbleBuff.cs BuffIt2TheLimit/BufferState.cs
git commit -m "feat(model): manual caster rank — global dict + per-buff override, sort integration

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Localization keys (all five locales)

**Files:**
- Modify: `BuffIt2TheLimit/Config/en_GB.json`, `de_DE.json`, `fr_FR.json`, `ru_RU.json`, `zh_CN.json` (insert after the `"nolimit"` line in each)
- Create (temp): `<scratchpad>/add_rank_keys.py`

**Interfaces:**
- Produces locale keys Task 3 references via `.i8()`: `caster.rank.global`, `caster.rank.buff`, `caster.rank-tooltip`

- [ ] **Step 1: Write the insertion script** (to the session scratchpad directory, not the repo)

```python
import json

VALUES = {
    "en_GB": {
        "caster.rank.global": "Priority (all buffs)",
        "caster.rank.buff": "Priority (this buff)",
        "caster.rank-tooltip": "Higher priority casts earlier. Default order when priorities are equal: active party before reserve, spells before scrolls and potions, then prepared casters before spontaneous ones (higher caster level first). 'This buff' overrides 'all buffs' for this spell only — a grey value follows the all-buffs priority.",
    },
    "de_DE": {
        "caster.rank.global": "Priorität (alle Buffs)",
        "caster.rank.buff": "Priorität (dieser Buff)",
        "caster.rank-tooltip": "Höhere Priorität castet zuerst. Standardreihenfolge bei gleicher Priorität: aktive Gruppe vor Reserve, Zauber vor Schriftrollen und Tränken, dann vorbereitete vor spontanen Castern (höhere Zauberstufe zuerst). 'Dieser Buff' übersteuert 'alle Buffs' nur für diesen Zauber — ein grauer Wert folgt der Alle-Buffs-Priorität.",
    },
    "fr_FR": {
        "caster.rank.global": "Priorité (tous les buffs)",
        "caster.rank.buff": "Priorité (ce buff)",
        "caster.rank-tooltip": "Une priorité plus élevée lance en premier. Ordre par défaut à priorité égale : groupe actif avant la réserve, sorts avant parchemins et potions, puis lanceurs à préparation avant les spontanés (niveau de lanceur le plus élevé d'abord). « Ce buff » remplace « tous les buffs » pour ce sort uniquement — une valeur grise suit la priorité globale.",
    },
    "ru_RU": {
        "caster.rank.global": "Приоритет (все баффы)",
        "caster.rank.buff": "Приоритет (этот бафф)",
        "caster.rank-tooltip": "Более высокий приоритет кастует раньше. Порядок по умолчанию при равном приоритете: активная группа раньше резерва, заклинания раньше свитков и зелий, затем подготовленные заклинатели раньше спонтанных (сначала более высокий уровень заклинателя). «Этот бафф» переопределяет «все баффы» только для этого заклинания — серое значение наследует общий приоритет.",
    },
    "zh_CN": {
        "caster.rank.global": "优先级（所有增益）",
        "caster.rank.buff": "优先级（此增益）",
        "caster.rank-tooltip": "优先级越高越先施放。优先级相同时的默认顺序：现役队伍先于后备，法术先于卷轴和药水，然后准备施法者先于自发施法者（施法者等级高的优先）。“此增益”仅对该法术覆盖“所有增益”——灰色数值表示沿用所有增益的优先级。",
    },
}

for locale, entries in VALUES.items():
    path = f"BuffIt2TheLimit/Config/{locale}.json"
    raw = open(path, "rb").read()
    has_bom = raw.startswith(b"\xef\xbb\xbf")
    lines = raw.decode("utf-8-sig").splitlines(keepends=True)
    out = []
    inserted = False
    for line in lines:
        out.append(line)
        if not inserted and '"nolimit"' in line:
            indent = line[:len(line) - len(line.lstrip())]
            for k, v in entries.items():
                out.append(f"{indent}{json.dumps(k, ensure_ascii=False)}: {json.dumps(v, ensure_ascii=False)},\n")
            inserted = True
    assert inserted, f"{path}: anchor \"nolimit\" not found"
    new = "".join(out)
    json.loads(new)  # validate before writing
    data = new.encode("utf-8")
    if has_bom:
        data = b"\xef\xbb\xbf" + data
    open(path, "wb").write(data)
    print(f"{path}: 3 keys inserted, BOM={'yes' if has_bom else 'no'}")
```

- [ ] **Step 2: Run it from the repo root**

Run: `python3 <scratchpad>/add_rank_keys.py`
Expected: five `... 3 keys inserted, BOM=...` lines — `BOM=yes` for en_GB/de_DE, `BOM=no` for fr_FR/ru_RU/zh_CN. The script self-validates JSON before writing.

- [ ] **Step 3: Verify BOM state unchanged**

Run: `for f in BuffIt2TheLimit/Config/*.json; do printf "%s: " "$f"; head -c3 "$f" | od -An -tx1; done`
Expected: `ef bb bf` for en_GB/de_DE only; `7b 0a 20` (plain `{`) for the rest.

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/Config/en_GB.json BuffIt2TheLimit/Config/de_DE.json BuffIt2TheLimit/Config/fr_FR.json BuffIt2TheLimit/Config/ru_RU.json BuffIt2TheLimit/Config/zh_CN.json
git commit -m "feat(i18n): caster priority row labels + tooltip in all five locales

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: UI — rank rows in the caster popout

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs` — two insertion points: after the cap −/+ construction block (after `var increaseCustomCapButton = increaseCustomCap.GetComponent<OwlcatButton>();`, line ~1695), and inside `UpdateDetailsView`'s bound branch (after the `azataZippyMagicLabel.color = ...` line, ~2277).

**Interfaces:**
- Consumes (Task 1): `BuffProvider.PriorityOverride` (`int?`), `SavedBufferState.CasterRanks` (`Dictionary<string, int>`); (Task 2): locale keys `caster.rank.global`, `caster.rank.buff`, `caster.rank-tooltip`.
- Consumes (existing): `MakeLabel(string)`, `TryGetSelectedProvider(out BubbleBuff, out BuffProvider)`, `capChangeScale`, `capLabel`, `expandButtonPrefab`, `togglePrefab`, `SelectedCaster` (`ReactiveProperty<int>`), `defaultLabelColor`, `state` (`BufferState`), `TooltipHelper.SetTooltip` + `TooltipTemplateSimple` (both already used in this file).
- Produces: nothing consumed by later tasks.

**Placement note:** the popout is a 1-column `GridLayoutGroup` — row order = sibling order. New rows are created after the cap block (so `capChangeScale` etc. are definitely assigned at every call site) and then moved below the Limit-casts row via `SetSiblingIndex`. The rank rows must NOT be created between line 1652 and 1663 — `capChangeScale` (line 1675) wouldn't be assigned yet and local-function capture would fail CS0165.

- [ ] **Step 1: Insert construction + handlers after line ~1695**

```csharp
            // Caster priority (manual rank) — global per-unit rank plus a per-buff
            // override. Slotted directly below the Limit-casts row via sibling
            // index (the popout grid lays out children in sibling order).
            (OwlcatButton, TextMeshProUGUI, OwlcatButton) MakeRankRow(GameObject row) {
                var dec = GameObject.Instantiate(expandButtonPrefab, row.transform);
                dec.Rect().localScale = new Vector3(capChangeScale, capChangeScale, capChangeScale);
                dec.Rect().pivot = new Vector2(.5f, .5f);
                dec.Rect().SetRotate2D(90);
                dec.Rect().anchoredPosition = Vector2.zero;
                dec.SetActive(true);

                var valueLabel = GameObject.Instantiate(togglePrefab.GetComponentInChildren<TextMeshProUGUI>().gameObject, row.transform);
                var valueText = valueLabel.GetComponent<TextMeshProUGUI>();
                valueText.text = "0";

                var inc = GameObject.Instantiate(expandButtonPrefab, row.transform);
                inc.Rect().pivot = new Vector2(.5f, .5f);
                inc.Rect().localScale = new Vector3(capChangeScale, capChangeScale, capChangeScale);
                inc.Rect().SetRotate2D(-90);
                inc.Rect().anchoredPosition = Vector2.zero;
                inc.SetActive(true);

                var decButton = dec.GetComponent<OwlcatButton>();
                var incButton = inc.GetComponent<OwlcatButton>();
                decButton.Interactable = true;
                incButton.Interactable = true;
                return (decButton, valueText, incButton);
            }

            var rankGlobalRow = MakeLabel("  " + "caster.rank.global".i8());
            var rankGlobalLabelText = rankGlobalRow.GetComponentInChildren<TextMeshProUGUI>();
            var (decreaseGlobalRank, globalRankValueText, increaseGlobalRank) = MakeRankRow(rankGlobalRow);

            var rankBuffRow = MakeLabel("  " + "caster.rank.buff".i8());
            var rankBuffLabelText = rankBuffRow.GetComponentInChildren<TextMeshProUGUI>();
            var (decreaseBuffRank, buffRankValueText, increaseBuffRank) = MakeRankRow(rankBuffRow);

            int capRowIndex = capLabel.transform.GetSiblingIndex();
            rankGlobalRow.transform.SetSiblingIndex(capRowIndex + 1);
            rankBuffRow.transform.SetSiblingIndex(capRowIndex + 2);

            rankGlobalLabelText.raycastTarget = true;
            TooltipHelper.SetTooltip(rankGlobalLabelText, new TooltipTemplateSimple(
                "caster.rank.global".i8(), "caster.rank-tooltip".i8()));
            rankBuffLabelText.raycastTarget = true;
            TooltipHelper.SetTooltip(rankBuffLabelText, new TooltipTemplateSimple(
                "caster.rank.buff".i8(), "caster.rank-tooltip".i8()));

            int GetGlobalRank(string unitId) {
                var ranks = state.SavedState.CasterRanks;
                return ranks != null && ranks.TryGetValue(unitId, out var r) ? r : 0;
            }

            // Re-sort after a rank change and keep the popout on the same provider:
            // SortProviders reorders CasterQueue in place and SelectedCaster is a raw
            // index, so re-resolve by object identity BEFORE Recalculate refreshes the
            // UI. (Contrast: the source-priority override closes the popout instead.)
            // IndexOf returns -1 if a concurrent rescan rebuilt the queue — the
            // UpdateDetailsView binding branch range-guards, so that degrades to an
            // unbound (empty) popout, same exposure as the existing AdjustCap path.
            void ResortKeepingSelection(BubbleBuff buff, BuffProvider caster, bool allBuffs) {
                if (allBuffs) {
                    foreach (var b in state.BuffList)
                        b.SortProviders();
                } else {
                    buff.SortProviders();
                }
                SelectedCaster.Value = buff.CasterQueue.IndexOf(caster);
                state.Recalculate(true);
            }

            void AdjustGlobalRank(int delta) {
                if (!TryGetSelectedProvider(out var buff, out var caster)) return;
                var savedState = state.SavedState;
                savedState.CasterRanks ??= new Dictionary<string, int>();
                var unitId = caster.who.UniqueId;
                int next = GetGlobalRank(unitId) + delta;
                if (next == 0)
                    savedState.CasterRanks.Remove(unitId); // only non-zero entries stored
                else
                    savedState.CasterRanks[unitId] = next;
                // Global rank affects every buff's caster order, not just the open one.
                ResortKeepingSelection(buff, caster, allBuffs: true);
            }

            void AdjustBuffRank(int delta) {
                if (!TryGetSelectedProvider(out var buff, out var caster)) return;
                int globalRank = GetGlobalRank(caster.who.UniqueId);
                int next = (caster.PriorityOverride ?? globalRank) + delta;
                // Landing back on the inherited value clears the override — same
                // sentinel-reset idea as CustomCap snapping to -1 at MaxCap.
                caster.PriorityOverride = next == globalRank ? (int?)null : next;
                ResortKeepingSelection(buff, caster, allBuffs: false);
            }

            decreaseGlobalRank.OnLeftClick.AddListener(() => AdjustGlobalRank(-1));
            increaseGlobalRank.OnLeftClick.AddListener(() => AdjustGlobalRank(1));
            decreaseBuffRank.OnLeftClick.AddListener(() => AdjustBuffRank(-1));
            increaseBuffRank.OnLeftClick.AddListener(() => AdjustBuffRank(1));
```

- [ ] **Step 2: Bind values in `UpdateDetailsView`**

Inside the `if (SelectedCaster.Value >= 0 && ... casterPopout.activeSelf)` branch, insert after the `azataZippyMagicLabel.color = ...` line (~2277), before the closing `} else {`:

```csharp
                    // Caster rank rows — hidden for activatables/songs: SortProviders
                    // early-returns for IsActivatable, so a rank would silently do
                    // nothing there (toggles are per-unit self, songs use scan order).
                    bool rankApplies = !buff.IsActivatable;
                    rankGlobalRow.SetActive(rankApplies);
                    rankBuffRow.SetActive(rankApplies);
                    if (rankApplies) {
                        int globalRank = GetGlobalRank(who.who.UniqueId);
                        globalRankValueText.text = globalRank.ToString("+#;-#;0");
                        if (who.PriorityOverride.HasValue) {
                            buffRankValueText.text = who.PriorityOverride.Value.ToString("+#;-#;0");
                            buffRankValueText.color = defaultLabelColor;
                        } else {
                            // Inheriting — show the effective value de-emphasized.
                            buffRankValueText.text = globalRank.ToString("+#;-#;0");
                            buffRankValueText.color = Color.gray;
                        }
                    }
```

(`who` here is the selected `BuffProvider`; `who.who` is the unit — existing naming in this block. The `"+#;-#;0"` format renders `+2` / `-1` / `0`.)

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat(ui): caster priority rows (global + per-buff override) in caster popout

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Deploy, deck smoke test, gotcha note

**Files:**
- Modify: `claude-context/gotchas-ui.md` (append one bullet)

**Interfaces:** none — verification and documentation only.

- [ ] **Step 1: Deploy to the Steam Deck**

Run: `./deploy.sh`
Expected: build + two `scp` transfers succeed. If the Deck is unreachable ("no route to host"), it is likely suspended — ask the user to wake it, don't fall back.
Gotcha check: compare `ls -l BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll` mtime against the source edits — `dotnet build` has skipped rebuilds on mtime misses before; `touch BuffIt2TheLimit/BubbleBuffer.cs` and rebuild if stale.

- [ ] **Step 2: User smoke test** (needs the user at the game — hand them this checklist, from the spec)

1. Two casters share a buff → default order unchanged (prepared/higher CL first).
2. Set "Priority (all buffs)" on the lower-priority caster → order flips for ALL shared buffs.
3. Set "Priority (this buff)" contradicting the global → override wins for that buff only; value greys out again when stepped back onto the global value.
4. Save, reload → ranks persist; an override on an otherwise-unconfigured buff persists.
5. Adjust a rank with the popout open → popout still shows the same caster; hover the row labels → tooltip shows the default-order text.
6. Rank on a reserve companion does NOT pull it ahead of the active party; a scroll provider does not outrank spells.

- [ ] **Step 3: Check logs after the test**

Run the `/check-logs` skill (tails the Deck `Player.log` for mod exceptions). Expected: no `BuffIt2TheLimit` exceptions.

- [ ] **Step 4: Append gotcha bullet to `claude-context/gotchas-ui.md`** (in "Bullet Gotchas")

```markdown
- **Rank rows re-sort while the popout stays open**: the caster-priority −/+ handlers call `SortProviders` and then re-resolve `SelectedCaster` by object identity (`CasterQueue.IndexOf(caster)`) before `Recalculate(true)` — `ResortKeepingSelection` in the popout block. Contrast with the source-priority override, which closes the popout (`SelectedCaster.Value = -1; HideCasterPopout()`). New controls that reorder CasterQueue must pick one of these two patterns; a raw stale index binds the popout to the wrong provider.
```

- [ ] **Step 5: Commit**

```bash
git add claude-context/gotchas-ui.md
git commit -m "docs(gotchas): popout re-sort patterns — re-resolve selection vs close

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## After the plan

Release is NOT part of this plan — run `/release minor` separately once the smoke test passes (csproj must still hold the pre-bump version; working tree must be clean).
