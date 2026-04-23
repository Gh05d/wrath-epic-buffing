# CLAUDE.md Split — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the 266-line `CLAUDE.md` into a short routing index plus five topic-indexed deep docs under a new `claude-context/` directory, without losing any gotcha content.

**Architecture:** `CLAUDE.md` keeps always-relevant sections (Build/Deploy/Release/etc.) and gains a routing table. Everything else migrates verbatim into `claude-context/{architecture,gotchas-ui,gotchas-scanning,gotchas-casting,gotchas-build}.md`. Verification is a grep-based content preservation check over the old file.

**Tech Stack:** Markdown files, `git`, standard POSIX tools (`grep`, `wc`, `sed`, `awk`).

---

## Notation

- **Bullet leading-phrase**: each gotcha bullet in the current CLAUDE.md starts with `- **<bold term>**`. The bold term is the "leading phrase". I list them verbatim so extraction is unambiguous.
- **Source path**: `CLAUDE_OLD` = `/home/pascal/Code/wrath-mods/wrath-epic-buffing/CLAUDE.md` at plan start (we keep the old version on disk only temporarily — no backup copy; `git` has the history).

## File Structure

**Create:**
- `claude-context/architecture.md`
- `claude-context/gotchas-ui.md`
- `claude-context/gotchas-scanning.md`
- `claude-context/gotchas-casting.md`
- `claude-context/gotchas-build.md`

**Modify:**
- `CLAUDE.md` (repo root)

No other files are touched.

---

## Task 1: Pre-flight — snapshot the current CLAUDE.md for verification

**Files:**
- Read: `CLAUDE.md`

- [ ] **Step 1: Snapshot line count and distinctive-phrase list**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
wc -l CLAUDE.md
grep -n '^- \*\*' CLAUDE.md | wc -l
grep -nE '^(#|##|###) ' CLAUDE.md
```

Expected output (for sanity):
- `wc -l`: 266 (or close — tolerate ±2 if someone edited since)
- `grep -n '^- \*\*'` count: ~82 bullets
- Heading list matches the table in the spec

Record the bullet count. Task 8 verifies the sum across topic files equals this minus dedup-drops.

- [ ] **Step 2: No commit yet**

This step produces no artifacts — only informs the engineer. Continue to Task 2.

---

## Task 2: Scaffold `claude-context/` directory

**Files:**
- Create directory: `claude-context/`

- [ ] **Step 1: Create the directory**

Run:
```bash
mkdir -p /home/pascal/Code/wrath-mods/wrath-epic-buffing/claude-context
```

- [ ] **Step 2: Verify it exists and is empty**

Run:
```bash
ls -la /home/pascal/Code/wrath-mods/wrath-epic-buffing/claude-context
```

Expected: empty directory (only `.` and `..`).

- [ ] **Step 3: Confirm not gitignored**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
git check-ignore claude-context/ || echo "NOT IGNORED (good)"
```

Expected: `NOT IGNORED (good)` (or empty output with exit 1 from `check-ignore`, meaning path is not ignored).

- [ ] **Step 4: No commit yet** (committed together at end)

---

## Task 3: Create `claude-context/architecture.md`

**Files:**
- Create: `claude-context/architecture.md`
- Read: `CLAUDE.md` (lines 142–242)

- [ ] **Step 1: Extract lines 142–242 verbatim**

Content is the entire `## Architecture` section including subsections: Mod Lifecycle, Core Data Flow, Key Classes, UI Structure, Enums, Localization, Asset Loading, Item Types (Equipment Scan), ActivatableAbility API (Songs/Performances), Pet/Companion API, Save System.

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
sed -n '142,242p' CLAUDE.md > /tmp/architecture-body.md
wc -l /tmp/architecture-body.md
```

Expected: 101 lines (242 − 142 + 1). Accept ±2 for whitespace.

- [ ] **Step 2: Write the file with a scope header**

Prepend a one-line scope header. Final file structure:

```markdown
# Architecture Reference

Load when: first time in this codebase, or when you need to understand the big picture (lifecycle, data flow, class roles, save format).

<contents of /tmp/architecture-body.md>
```

Concretely:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  echo '# Architecture Reference'
  echo ''
  echo 'Load when: first time in this codebase, or when you need to understand the big picture (lifecycle, data flow, class roles, save format).'
  echo ''
  cat /tmp/architecture-body.md
} > claude-context/architecture.md
```

- [ ] **Step 3: Verify content preservation**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
for phrase in 'GlobalBubbleBuffer' 'BufferState.RecalculateAvailableBuffs' 'EngineCastingHandler' 'UsableItemType.Wand' 'ItemStatHelper' 'BardicPerformance' 'UnitPartPetMaster' 'bi2tl-{GameId}.json'; do
  grep -q "$phrase" claude-context/architecture.md && echo "OK: $phrase" || echo "MISSING: $phrase"
done
```

Expected: all `OK:` lines, no `MISSING:`.

- [ ] **Step 4: No commit yet**

---

## Task 4: Create `claude-context/gotchas-ui.md`

**Files:**
- Create: `claude-context/gotchas-ui.md`
- Read: `CLAUDE.md`

- [ ] **Step 1: Collect the UI gotcha bullets**

Extract these bullets from `CLAUDE.md` (use their leading-phrases to identify):

1. `**sourceControlObj** visibility` (line ~69)
2. `**source-controls-section** needs minHeight ≥ 110` (~79)
3. `**TextMeshProUGUI** competes with LayoutElement` (~80)
4. `**Caster portrait index** ≠ CasterQueue index` (~81)
5. `**Debug Unity UI positions** with GetWorldCorners()` (~100)
6. `**Don't destroy UI elements** during their click callbacks` (~105)
7. `**OwlcatButton** via AddComponent doesn't render` (~106)
8. `**ScrollRect** needs raycast target for wheel events` (~107)
9. `**GlobalBubbleBuffer.Buttons** references go stale after save/load` (~108)

Plus the entire `## Unity UI Layout Patterns` section (lines 251–259).

- [ ] **Step 2: Write the file**

Final structure:

```markdown
# Gotchas — UI (Unity / TextMeshPro / layout)

Load when: editing `BubbleBuffer.cs` UI code, `UIHelpers.cs`, or adding/modifying any Unity layout.

## Bullet Gotchas

- **`sourceControlObj` visibility**: [COPY VERBATIM from CLAUDE.md line ~69]
- **`source-controls-section` needs minHeight ≥ 110**: [COPY VERBATIM ~79]
- **`TextMeshProUGUI` competes with `LayoutElement` at default priority**: [COPY VERBATIM ~80]
- **Caster portrait index ≠ CasterQueue index**: [COPY VERBATIM ~81]
- **Debug Unity UI positions with `GetWorldCorners()`**: [COPY VERBATIM ~100]
- **Don't destroy UI elements during their click callbacks**: [COPY VERBATIM ~105]
- **`OwlcatButton` via `AddComponent` doesn't render**: [COPY VERBATIM ~106]
- **ScrollRect needs raycast target for wheel events**: [COPY VERBATIM ~107]
- **`GlobalBubbleBuffer.Buttons` references go stale after save/load**: [COPY VERBATIM ~108]

## Unity UI Layout Patterns

[COPY VERBATIM body of ## Unity UI Layout Patterns section from CLAUDE.md lines 252–259]
```

Concretely, one way to build it:

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  echo '# Gotchas — UI (Unity / TextMeshPro / layout)'
  echo ''
  echo 'Load when: editing `BubbleBuffer.cs` UI code, `UIHelpers.cs`, or adding/modifying any Unity layout.'
  echo ''
  echo '## Bullet Gotchas'
  echo ''
  grep -E '^- \*\*`sourceControlObj` visibility' CLAUDE.md
  grep -E '^- \*\*`source-controls-section` needs' CLAUDE.md
  grep -E '^- \*\*`TextMeshProUGUI` competes' CLAUDE.md
  grep -E '^- \*\*Caster portrait index' CLAUDE.md
  grep -E '^- \*\*Debug Unity UI positions' CLAUDE.md
  grep -E "^- \\*\\*Don't destroy UI elements" CLAUDE.md
  grep -E '^- \*\*`OwlcatButton` via' CLAUDE.md
  grep -E '^- \*\*ScrollRect needs raycast' CLAUDE.md
  grep -E '^- \*\*`GlobalBubbleBuffer.Buttons` references' CLAUDE.md
  echo ''
  echo '## Unity UI Layout Patterns'
  echo ''
  # Body lines after "## Unity UI Layout Patterns" up to next ## heading
  awk '/^## Unity UI Layout Patterns$/{f=1; next} f && /^## /{f=0} f' CLAUDE.md
} > claude-context/gotchas-ui.md
```

**Important**: verify each `grep -E` in the block above actually prints exactly one line before moving on. If any prints zero lines, the leading-phrase in the original file has a character you didn't escape (e.g. backtick inside the pattern) — adjust the regex.

- [ ] **Step 3: Verify content preservation**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
for phrase in 'sourceControlObj' 'source-controls-section' 'TextMeshProUGUI' 'Caster portrait index' 'GetWorldCorners' 'destroy UI elements' 'OwlcatButton' 'ScrollRect needs raycast' 'GlobalBubbleBuffer.Buttons' 'MakeButton() breaks layout groups' 'childControlHeight'; do
  grep -q "$phrase" claude-context/gotchas-ui.md && echo "OK: $phrase" || echo "MISSING: $phrase"
done
```

Expected: all `OK:`.

- [ ] **Step 4: No commit yet**

---

## Task 5: Create `claude-context/gotchas-scanning.md`

**Files:**
- Create: `claude-context/gotchas-scanning.md`
- Read: `CLAUDE.md`

- [ ] **Step 1: Collect the scanning gotcha bullets**

Bullets to include (leading-phrase fragment — approximate line numbers):

1. `**`BuffProvider.CanTarget` crashes when `spell` is null**` (~84)
2. `**`AddActivatable()` bypasses `GetBeneficialBuffs`**` (~85)
3. `**Magic Deceiver fused spells**` (~93)
4. `**Archetype spellbooks can override `IsArcanist`**` (~94)
5. `**Weapon enchantment spells use `EnhanceWeapon` action**` (~95)
6. `**`Game.Instance.Player.RemoteCompanions`**` (~102)
7. `**`IsInGame` semantics**` (~103)
8. `**`spell.CanTarget()` rejects out-of-scene units**` (~104)
9. `**`ActivatableAbility.SourceItem` distinguishes source**` (~115)
10. `**`ActivatableAbilityResourceLogic` = has resource cost**` (~116)
11. `**Item-backed activatables bypass the QuickSlot scan**` (~117)
12. `**`BlueprintBuff.FxOnStart` fallback in `IsBeneficial`**` (~118)
13. `**`AddBuff()` has two separate filter concerns**` (~120)
14. `**`SpellsWithBeneficialBuffs` cache key is GUID-only**` (~121)
15. `**`Category.Toggle`**` (~123)
16. `**Scan category summary log**` (~124)
17. `**Two distinct ConversionsProvider patterns**` (~125)
18. `**Shifter's Fury activation dispatch**` (~126)
19. `**Don't filter activatables on `IsRuntimeOnly || HiddenInUI` alone**` (~127)
20. `**Equipment-activatable charges gate must respect `SpendCharges`**` (~128)

- [ ] **Step 2: Write the file**

Structure:

```markdown
# Gotchas — Scanning (BufferState / item & activatable discovery)

Load when: editing `BufferState.cs` (scan/discovery logic), adding a new item source, adding a new activatable-ability source, or touching pet/companion iteration.

## Bullet Gotchas

- [20 bullets from Step 1, verbatim, in the order listed]
```

Build it:

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  echo '# Gotchas — Scanning (BufferState / item & activatable discovery)'
  echo ''
  echo 'Load when: editing `BufferState.cs` (scan/discovery logic), adding a new item source, adding a new activatable-ability source, or touching pet/companion iteration.'
  echo ''
  echo '## Bullet Gotchas'
  echo ''
  grep -E '^- \*\*`BuffProvider.CanTarget` crashes' CLAUDE.md
  grep -E '^- \*\*`AddActivatable\(\)` bypasses' CLAUDE.md
  grep -E '^- \*\*Magic Deceiver fused spells' CLAUDE.md
  grep -E '^- \*\*Archetype spellbooks can override' CLAUDE.md
  grep -E '^- \*\*Weapon enchantment spells use' CLAUDE.md
  grep -E '^- \*\*`Game.Instance.Player.RemoteCompanions`' CLAUDE.md
  grep -E '^- \*\*`IsInGame` semantics' CLAUDE.md
  grep -E '^- \*\*`spell.CanTarget\(\)` rejects' CLAUDE.md
  grep -E '^- \*\*`ActivatableAbility.SourceItem` distinguishes' CLAUDE.md
  grep -E '^- \*\*`ActivatableAbilityResourceLogic` = has' CLAUDE.md
  grep -E '^- \*\*Item-backed activatables bypass' CLAUDE.md
  grep -E '^- \*\*`BlueprintBuff.FxOnStart` fallback' CLAUDE.md
  grep -E '^- \*\*`AddBuff\(\)` has two separate' CLAUDE.md
  grep -E '^- \*\*`SpellsWithBeneficialBuffs` cache' CLAUDE.md
  grep -E '^- \*\*`Category.Toggle`' CLAUDE.md
  grep -E '^- \*\*Scan category summary log' CLAUDE.md
  grep -E '^- \*\*Two distinct ConversionsProvider' CLAUDE.md
  grep -E "^- \\*\\*Shifter's Fury activation dispatch" CLAUDE.md
  grep -E "^- \\*\\*Don't filter activatables" CLAUDE.md
  grep -E '^- \*\*Equipment-activatable charges gate' CLAUDE.md
} > claude-context/gotchas-scanning.md
```

Verify each `grep -E` produced exactly one line. If any produced zero or multiple, stop and adjust the regex for that bullet.

- [ ] **Step 3: Verify content preservation**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
for phrase in 'CanTarget.*crashes.*spell' 'AddActivatable.*bypasses.*GetBeneficialBuffs' 'MagicHackDefaultSlot' 'MagicDeceiverSpellbook' 'EnhanceWeapon' 'RemoteCompanions' 'IsInGame' 'out-of-scene' 'ActivatableAbility.SourceItem' 'ActivatableAbilityResourceLogic' 'QuickSlot scan' 'FxOnStart.AssetId' 'skipDamageFilter' 'SpellsWithBeneficialBuffs' 'Category.Toggle' 'Scan complete' 'ActivationDisable' "ShiftersFuryPart" 'IsRuntimeOnly' 'SpendCharges'; do
  grep -qE "$phrase" claude-context/gotchas-scanning.md && echo "OK: $phrase" || echo "MISSING: $phrase"
done
```

Expected: all `OK:`.

- [ ] **Step 4: Verify bullet count**

Run:
```bash
grep -c '^- \*\*' claude-context/gotchas-scanning.md
```

Expected: `20`.

- [ ] **Step 5: No commit yet**

---

## Task 6: Create `claude-context/gotchas-casting.md`

**Files:**
- Create: `claude-context/gotchas-casting.md`
- Read: `CLAUDE.md`

- [ ] **Step 1: Collect the casting gotcha bullets**

Bullets to include (leading-phrase fragment — approximate line numbers):

1. `**`MetamagicData.MetamagicMask` has private set**` (~68)
2. `**`BuffProvider.SelfCastOnly` is a computed property**` (~74)
3. `**`UpdateCasterDetails` filters caster portraits**` (~75)
4. `**Share Transmutation scope is engine-enforced**` (~76)
5. `**`BuffProvider.CanTarget` must respect the `ShareTransmutation` flag**` (~77)
6. `**Mass/burst spells target the caster**` (~78)
7. `**`BubbleBuff.SavedState` is never assigned**` (~82)
8. `**`BubbleBuff.Spell` is null for songs**` (~83)
9. `**`new AbilityData(blueprint, caster)` for items drops item caster level**` (~88)
10. `**`RuleCastSpell` clones `AbilityParams` in constructor**` (~90)
11. `**`MetamagicData.Clear()` zeros all fields, `Add()` only restores flags**` (~91)
12. `**`UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData**` (~96)
13. `**Combat-start disrupts `Unit.Commands.Run()` queue**` (~101)
14. `**`ActivatableAbility.IsOn = true` does NOT start the ability**` (~109)
15. `**No per-round EventBus events in RTWP mode**` (~110)
16. `**`ExecuteCombatStart` runs while `Game.Instance.Player.IsInCombat == true`**` (~111)
17. `**Two dispatch paths for cast coroutines**` (~112)
18. `**Phase 0 ordering — three priority buckets**` (~129)
19. `**`[CSD]` log prefix**` (~130)

Plus the entire `## Combat-Start Diagnostics` section (lines 138–140) and the entire `## Credit System (Buff Availability)` section (lines 244–249).

- [ ] **Step 2: Write the file**

Structure:

```markdown
# Gotchas — Casting (BuffExecutor / EngineCastingHandler / Combat-Start)

Load when: editing `BuffExecutor.cs`, `EngineCastingHandler.cs`, `AnimatedExecutionEngine.cs`, `InstantExecutionEngine.cs`, combat-start flow, or anything involving `CastTask` / metamagic / DC / caster-level math.

## Bullet Gotchas

- [19 bullets from Step 1, verbatim, in the order listed]

## Combat-Start Diagnostics

[Body of ## Combat-Start Diagnostics section from CLAUDE.md, verbatim]

## Credit System (Buff Availability)

[Body of ## Credit System section from CLAUDE.md, verbatim]
```

Build it:

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  echo '# Gotchas — Casting (BuffExecutor / EngineCastingHandler / Combat-Start)'
  echo ''
  echo 'Load when: editing `BuffExecutor.cs`, `EngineCastingHandler.cs`, `AnimatedExecutionEngine.cs`, `InstantExecutionEngine.cs`, combat-start flow, or anything involving `CastTask` / metamagic / DC / caster-level math.'
  echo ''
  echo '## Bullet Gotchas'
  echo ''
  grep -E '^- \*\*`MetamagicData.MetamagicMask` has private set' CLAUDE.md
  grep -E '^- \*\*`BuffProvider.SelfCastOnly` is a computed' CLAUDE.md
  grep -E '^- \*\*`UpdateCasterDetails` filters' CLAUDE.md
  grep -E '^- \*\*Share Transmutation scope' CLAUDE.md
  grep -E '^- \*\*`BuffProvider.CanTarget` must respect' CLAUDE.md
  grep -E '^- \*\*Mass/burst spells target' CLAUDE.md
  grep -E '^- \*\*`BubbleBuff.SavedState` is never' CLAUDE.md
  grep -E '^- \*\*`BubbleBuff.Spell` is null for songs' CLAUDE.md
  grep -E '^- \*\*`new AbilityData\(blueprint, caster\)`' CLAUDE.md
  grep -E '^- \*\*`RuleCastSpell` clones' CLAUDE.md
  grep -E '^- \*\*`MetamagicData.Clear\(\)` zeros' CLAUDE.md
  grep -E '^- \*\*`UnitUseAbility.CreateCastCommand` rejects' CLAUDE.md
  grep -E '^- \*\*Combat-start disrupts' CLAUDE.md
  grep -E '^- \*\*`ActivatableAbility.IsOn = true`' CLAUDE.md
  grep -E '^- \*\*No per-round EventBus' CLAUDE.md
  grep -E '^- \*\*`ExecuteCombatStart` runs while' CLAUDE.md
  grep -E '^- \*\*Two dispatch paths' CLAUDE.md
  grep -E '^- \*\*Phase 0 ordering' CLAUDE.md
  grep -E '^- \*\*`\[CSD\]` log prefix' CLAUDE.md
  echo ''
  echo '## Combat-Start Diagnostics'
  echo ''
  awk '/^## Combat-Start Diagnostics$/{f=1; next} f && /^## /{f=0} f' CLAUDE.md
  echo ''
  echo '## Credit System (Buff Availability)'
  echo ''
  awk '/^## Credit System \(Buff Availability\)$/{f=1; next} f && /^## /{f=0} f' CLAUDE.md
} > claude-context/gotchas-casting.md
```

Verify each `grep -E` produced exactly one line.

- [ ] **Step 3: Verify content preservation**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
for phrase in 'MetamagicMask' 'SelfCastOnly' 'UpdateCasterDetails' 'ShareTransmutationFeature' 'ShareTransmutation flag' 'Mass/burst spells' 'SavedState' "is null for songs" 'CraftedItemPart' 'RuleCastSpell' 'Clear\(\).*zeros' 'CreateCastCommand' "Commands.Run" 'IsOn = true' 'ITurnBasedModeHandler' 'IsInCombat' 'CastSpells' 'Phase 0 ordering' 'CSD' 'ChargeCredits' 'ValidateMass' 'BubbleBuff.DiagnoseCaster'; do
  grep -qE "$phrase" claude-context/gotchas-casting.md && echo "OK: $phrase" || echo "MISSING: $phrase"
done
```

Expected: all `OK:`.

- [ ] **Step 4: Verify bullet count**

Run:
```bash
grep -c '^- \*\*' claude-context/gotchas-casting.md
```

Expected: `23` (19 casting bullets + 4 from Credit System section).

- [ ] **Step 5: No commit yet**

---

## Task 7: Create `claude-context/gotchas-build.md`

**Files:**
- Create: `claude-context/gotchas-build.md`
- Read: `CLAUDE.md`

- [ ] **Step 1: Collect the build/release/Nexus gotcha bullets**

Bullets to include (leading-phrase fragment — approximate line numbers):

1. `**Upstream PRs often forked from old versions**` (~59)
2. `**`.NET Framework 4.8.1` missing APIs**` (~60)
3. `**Newtonsoft.Json version is old**` (~61)
4. `**`[JsonConverter]` on collections**` (~62)
5. `**WidgetPaths version selection**` (~63)
6. `**EnhancedInventory interop**` (~64)
7. `**Publicizer scope**` (~65)
8. `**`Main.Log()` vs `Main.Verbose()`**` (~66)
9. `**Worktrees need `GamePath.props` + `GameInstall/`**` (~67)
10. `**Nexus Mods upload**` (~70)
11. `**Nexus Mods changelogs**` (~71)
12. `**Nexus Mods description**` (~72)
13. `**Nexus Mods version display**` (~73)
14. `**`ilspycmd` stack overflows on large classes**` (~86)
15. `**`ilspycmd` full-IL dump workaround**` (~87)
16. `**`docs/` directory is gitignored**` (~89)
17. `**Blueprint action tree extraction**` (~92)
18. `**Verify deploys by comparing DLL timestamps**` (~97)
19. `**`Info.json` byte-count is ambiguous**` (~98)
20. `**UMM hot-reload only works in DEBUG builds**` (~99)
21. `**Prefer manual Harmony patching for private methods**` (~113)
22. `**Unity coroutines need `using System.Collections;`**` (~114)
23. `**Diagnostic workflow without unit tests**` (~119)
24. `**`Config/*.json` files have UTF-8 BOM**` (~122)

- [ ] **Step 2: Write the file**

Structure:

```markdown
# Gotchas — Build / Deploy / Release / Tooling

Load when: editing the `.csproj`, `deploy.sh`, `GamePath.props`, `Info.json`, release workflow, Nexus upload, or using tools like `ilspycmd` / Harmony patching / UMM hot-reload.

## Bullet Gotchas

- [24 bullets from Step 1, verbatim, in the order listed]
```

Build it (same `{ ... } > file` pattern as previous tasks — I trust the engineer at this point):

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  echo '# Gotchas — Build / Deploy / Release / Tooling'
  echo ''
  echo 'Load when: editing the `.csproj`, `deploy.sh`, `GamePath.props`, `Info.json`, release workflow, Nexus upload, or using tools like `ilspycmd` / Harmony patching / UMM hot-reload.'
  echo ''
  echo '## Bullet Gotchas'
  echo ''
  grep -E '^- \*\*Upstream PRs' CLAUDE.md
  grep -E '^- \*\*`\.NET Framework 4\.8\.1`' CLAUDE.md
  grep -E '^- \*\*Newtonsoft.Json version' CLAUDE.md
  grep -E '^- \*\*`\[JsonConverter\]` on collections' CLAUDE.md
  grep -E '^- \*\*WidgetPaths version selection' CLAUDE.md
  grep -E '^- \*\*EnhancedInventory interop' CLAUDE.md
  grep -E '^- \*\*Publicizer scope' CLAUDE.md
  grep -E '^- \*\*`Main.Log\(\)` vs' CLAUDE.md
  grep -E '^- \*\*Worktrees need' CLAUDE.md
  grep -E '^- \*\*Nexus Mods upload' CLAUDE.md
  grep -E '^- \*\*Nexus Mods changelogs' CLAUDE.md
  grep -E '^- \*\*Nexus Mods description' CLAUDE.md
  grep -E '^- \*\*Nexus Mods version display' CLAUDE.md
  grep -E '^- \*\*`ilspycmd` stack overflows' CLAUDE.md
  grep -E '^- \*\*`ilspycmd` full-IL dump' CLAUDE.md
  grep -E '^- \*\*`docs/` directory is gitignored' CLAUDE.md
  grep -E '^- \*\*Blueprint action tree extraction' CLAUDE.md
  grep -E '^- \*\*Verify deploys by comparing' CLAUDE.md
  grep -E '^- \*\*`Info.json` byte-count' CLAUDE.md
  grep -E '^- \*\*UMM hot-reload only works' CLAUDE.md
  grep -E '^- \*\*Prefer manual Harmony patching' CLAUDE.md
  grep -E '^- \*\*Unity coroutines need' CLAUDE.md
  grep -E '^- \*\*Diagnostic workflow without' CLAUDE.md
  grep -E '^- \*\*`Config/\*.json` files have' CLAUDE.md
} > claude-context/gotchas-build.md
```

Verify each `grep -E` produced exactly one line.

- [ ] **Step 3: Check dedup against parent CLAUDE.md**

Per spec Migration Rule 2, check whether the two `ilspycmd` bullets (14, 15) are already covered by the parent `/home/pascal/Code/wrath-mods/CLAUDE.md`.

Run:
```bash
grep -n ilspycmd /home/pascal/Code/wrath-mods/CLAUDE.md
```

Read the parent bullet. Then read the two project-level bullets in `claude-context/gotchas-build.md`. If parent's text fully subsumes project's (i.e. every fact in project's bullet is already stated in parent's), delete the project bullet from `gotchas-build.md`. If project's bullet adds unique info (e.g. specific class names that overflow, specific workaround commands), keep it.

Expected result based on current state:
- Bullet 14 (`ilspycmd` stack overflows on large classes): parent mentions the general rule (`"NEVER run a full C# decompile"`) but NOT the specific classes that overflow. **Keep the project bullet.**
- Bullet 15 (full-IL dump workaround): parent does not contain this workaround. **Keep.**

Document the decision in the commit message (Task 9).

- [ ] **Step 4: Verify content preservation**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
for phrase in 'factubsio/BubbleBuffs' 'GetValueOrDefault' 'JsonConverter<T>' 'ItemConverterType' 'WidgetPaths' 'EnhancedInventory' 'Publicize=' 'Main.Verbose' 'GamePath.props' 'BBCode' 'upload-action#11' 'UnitEntityData' 'DOTNET_ROOT' 'git add -f' 'blueprints.zip' 'deploy.sh' 'byte-count' 'EnableReloading' 'AccessTools.Method' 'IEnumerator' 'ABILITY-SCAN' 'UTF-8 BOM'; do
  grep -q "$phrase" claude-context/gotchas-build.md && echo "OK: $phrase" || echo "MISSING: $phrase"
done
```

Expected: all `OK:`.

- [ ] **Step 5: Verify bullet count**

Run:
```bash
grep -c '^- \*\*' claude-context/gotchas-build.md
```

Expected: `24` (or `22` if both ilspycmd bullets were dropped as duplicates — unlikely per Step 3).

- [ ] **Step 6: No commit yet**

---

## Task 8: Rewrite `CLAUDE.md` as routing index

**Files:**
- Modify (full rewrite): `CLAUDE.md`

- [ ] **Step 1: Preserve the four sections that stay**

The new `CLAUDE.md` keeps these sections verbatim from the old file:
- `# Buff It 2 The Limit` title + `## Overview`
- `## Build`
- `## Deploy`
- `## Versioning`
- `## Release` (including the English-release-notes bullet)
- `## Debug Keybinds (DEBUG builds only)`
- `## Code Style`

Extract them with `awk` range-matches:

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  sed -n '1,56p' CLAUDE.md                                  # title through end of Release (line 55 is last bullet; 56 is blank)
  echo ''
  awk '/^## Debug Keybinds/{f=1} f && /^## [^D]/ && !/^## Debug/{f=0} f' CLAUDE.md > /tmp/debug-section.md
  # Simpler: grab Debug Keybinds section with the same awk pattern as before
} > /tmp/claude-head.md
```

Actually, use this simpler extraction:

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
# Sections to keep, by heading
awk '/^# Buff It 2 The Limit$/,/^## Release$/' CLAUDE.md > /tmp/claude-top.md
# ^ captures title through end of Release section content... but stops at ## Release heading line
# Instead, capture until the heading AFTER Release (which is ## Gotchas in the old file)
awk '
/^# Buff It 2 The Limit$/{p=1}
p && /^## Gotchas/{p=0}
p
' CLAUDE.md > /tmp/claude-top.md

# Debug Keybinds section body
awk '
/^## Debug Keybinds/{p=1}
p && /^## Combat-Start Diagnostics/{p=0}
p
' CLAUDE.md > /tmp/claude-debug.md

# Code Style section (last section)
awk '
/^## Code Style/{p=1}
p
' CLAUDE.md > /tmp/claude-codestyle.md
```

Verify each temp file has the expected content:
```bash
head -1 /tmp/claude-top.md          # should be: # Buff It 2 The Limit
tail -1 /tmp/claude-top.md          # last non-empty line of ## Release (the English-notes bullet, possibly empty line after)
head -1 /tmp/claude-debug.md        # should be: ## Debug Keybinds (DEBUG builds only)
head -1 /tmp/claude-codestyle.md    # should be: ## Code Style
```

- [ ] **Step 2: Compose the new routing index section**

Write `/tmp/claude-routing.md`:

```bash
cat > /tmp/claude-routing.md << 'EOF'
## Topic Index

Deep docs live in `claude-context/`. Before editing an area, read the matching file:

| Touching... | Read first |
|---|---|
| `BubbleBuffer.cs` UI code, `UIHelpers.cs`, new Unity layouts | `claude-context/gotchas-ui.md` |
| `BufferState.cs` scan/discovery, new item or activatable source | `claude-context/gotchas-scanning.md` |
| `BuffExecutor.cs`, `EngineCastingHandler.cs`, combat-start, casting coroutines | `claude-context/gotchas-casting.md` |
| Build config, release, Nexus upload, UMM, `ilspycmd` | `claude-context/gotchas-build.md` |
| First time in this codebase / broad architecture question | `claude-context/architecture.md` |

**Maintenance rule:** when a new gotcha is discovered, add it to the matching topic file. Update this table only if the routing itself changes.

EOF
```

- [ ] **Step 3: Assemble the new CLAUDE.md**

Order: title → Overview → Build → Deploy → Versioning → Release → **Topic Index** (new) → Debug Keybinds → Code Style.

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
{
  cat /tmp/claude-top.md
  echo ''
  cat /tmp/claude-routing.md
  cat /tmp/claude-debug.md
  echo ''
  cat /tmp/claude-codestyle.md
} > CLAUDE.md.new
```

- [ ] **Step 4: Sanity-check the new CLAUDE.md**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
wc -l CLAUDE.md.new
grep -E '^(#|##|###) ' CLAUDE.md.new
```

Expected:
- Line count: ~70–90 (target ~80 per spec; accept 60–100).
- Headings, in order: `# Buff It 2 The Limit`, `## Overview`, `## Build`, `## Deploy`, `## Versioning`, `## Release`, `## Topic Index`, `## Debug Keybinds (DEBUG builds only)`, `## Code Style`. No other `##`/`###` headings should appear.

If an unexpected heading appears, one of the awk ranges captured too much. Debug by inspecting `/tmp/claude-*.md` files.

- [ ] **Step 5: Swap in the new CLAUDE.md**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
mv CLAUDE.md.new CLAUDE.md
```

- [ ] **Step 6: No commit yet**

---

## Task 9: Global content-preservation verification

**Files:**
- Read: all of `claude-context/*.md`, `CLAUDE.md`, and the git-HEAD version of the old `CLAUDE.md`.

- [ ] **Step 1: Compare distinctive-phrase coverage old vs. new**

Get every bullet leading-phrase from the old file (via git), and confirm each is found in **exactly one** of the new files (or in the new `CLAUDE.md` for the few kept bullets like Release's English-notes and Debug Keybinds' Shift+* triggers).

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing

# Extract every bullet leading-phrase from the pre-rewrite CLAUDE.md (via git HEAD)
git show HEAD:CLAUDE.md | grep -n '^- \*\*' | sed 's/^[0-9]*://' > /tmp/old-bullets.txt
wc -l /tmp/old-bullets.txt

# For each old bullet, check it appears in exactly one of:
#   CLAUDE.md  claude-context/*.md
while IFS= read -r bullet; do
  # Extract the first ~40 non-backtick chars after `- **` as a search key
  key=$(printf '%s' "$bullet" | sed -E 's/^- \*\*//' | cut -c1-40)
  count=$(grep -l -F "$key" CLAUDE.md claude-context/*.md 2>/dev/null | wc -l)
  if [ "$count" != "1" ]; then
    echo "MISMATCH ($count matches): $bullet"
  fi
done < /tmp/old-bullets.txt
```

Expected: no `MISMATCH` lines. Every old bullet should appear in exactly one new file.

If a bullet appears **0 times**: it was lost during migration — go back to the relevant topic-file task and add it.

If a bullet appears **2+ times**: duplication; decide which topic file owns it and remove it from the other(s). (Ignore cross-cutting phrases that appear legitimately as a reference — but real duplicate bullets must be deduplicated.)

- [ ] **Step 2: Confirm CLAUDE.md size target**

Run:
```bash
wc -l CLAUDE.md
wc -c CLAUDE.md
```

Expected: ≤ ~90 lines, ≤ ~13 KB. If over, something leaked through from the old structure — inspect and trim.

- [ ] **Step 3: Confirm topic files exist and are non-trivial**

Run:
```bash
wc -l claude-context/*.md
```

Expected:
- `architecture.md`: ~100 lines
- `gotchas-ui.md`: ~20 lines (9 bullets + headings + Layout Patterns body)
- `gotchas-scanning.md`: ~25 lines (20 bullets + headings)
- `gotchas-casting.md`: ~35 lines (19 bullets + Combat-Start + Credit System)
- `gotchas-build.md`: ~30 lines (24 bullets + headings)

All should be > 10 lines. None should be empty.

- [ ] **Step 4: Spot-check the routing index in new CLAUDE.md points at real files**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
for f in $(grep -oE 'claude-context/[a-z-]+\.md' CLAUDE.md | sort -u); do
  test -f "$f" && echo "OK: $f" || echo "MISSING: $f"
done
```

Expected: 5 `OK:` lines, one per topic file.

---

## Task 10: Commit

**Files:**
- Stage: `CLAUDE.md`, `claude-context/*.md`
- Spec / plan files: `docs/superpowers/specs/2026-04-23-claude-md-split-design.md`, `docs/superpowers/plans/2026-04-23-claude-md-split.md` (use `-f` because `docs/` is gitignored)

- [ ] **Step 1: Review the diff one more time**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
git status
git diff CLAUDE.md | head -80
git status claude-context/
```

Sanity-check that no unexpected file is modified.

- [ ] **Step 2: Stage everything**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
git add CLAUDE.md claude-context/
git add -f docs/superpowers/specs/2026-04-23-claude-md-split-design.md
git add -f docs/superpowers/plans/2026-04-23-claude-md-split.md
git status
```

Expected files staged: `CLAUDE.md` (modified), 5 new `claude-context/*.md` (new), 2 docs files (new, force-added).

- [ ] **Step 3: Commit**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing
git commit -m "$(cat <<'EOF'
docs: split CLAUDE.md into topic-indexed deep docs

CLAUDE.md grew to 266 lines / 40 KB — loaded in every conversation's
context. Most of its bulk was area-specific gotchas that don't apply
to any given edit.

Split into:
- CLAUDE.md — short routing index (~80 lines) with build/deploy/release
  + a Topic Index table pointing at the deep docs below.
- claude-context/architecture.md — big-picture reference.
- claude-context/gotchas-{ui,scanning,casting,build}.md — area-specific
  gotchas, loaded on demand when editing matching files.

Content is verbatim from the previous CLAUDE.md. No gotchas were dropped
as duplicates of the parent wrath-mods/CLAUDE.md (both ilspycmd bullets
add project-specific detail the parent does not cover).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify the commit**

```bash
git log -1 --stat
```

Expected: one commit touching `CLAUDE.md` (−~190 lines), 5 new files under `claude-context/`, 2 new files under `docs/superpowers/`.

---

## Self-Review (plan author's check, pre-execution)

1. **Spec coverage:**
   - Target structure (CLAUDE.md + 5 topic files): Tasks 3–7 create them; Task 8 rewrites CLAUDE.md. ✓
   - Routing table in CLAUDE.md: Task 8 Step 2. ✓
   - Directory choice `claude-context/`: Task 2. ✓
   - Migration Rule 1 (verbatim): all extraction uses `grep -E '^- \*\*...'` or `sed -n 'A,Bp'` / `awk` range — no rewriting. ✓
   - Migration Rule 2 (dedup with parent): Task 7 Step 3. ✓
   - Migration Rule 3 (content preservation check): Task 9 Step 1. ✓
   - Migration Rule 4 (topic file structure): each creation task writes a one-line "Load when..." header. ✓
   - Migration Rule 5 (single commit on master): Task 10. ✓
   - Success criteria line count ≤ ~80: Task 9 Step 2. ✓
   - Success criteria routing coverage: Task 9 Step 4. ✓

2. **Placeholder scan:** No `TBD`/`TODO`; every bash block is runnable; every expected-output is spelled out.

3. **Type/name consistency:** File names `gotchas-ui.md`, `gotchas-scanning.md`, `gotchas-casting.md`, `gotchas-build.md`, `architecture.md` used identically across spec, plan, and the routing table. ✓

4. **Risk: regex brittleness.** Several `grep -E` patterns hand-match leading-phrases. If the bullet text changed between when the plan was written and when it runs, the extraction silently outputs zero lines. Mitigation: each creation task has an explicit "verify each `grep -E` produced exactly one line" instruction. Task 9 Step 1 is the ultimate catch-all — any lost bullet gets flagged there.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-23-claude-md-split.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
