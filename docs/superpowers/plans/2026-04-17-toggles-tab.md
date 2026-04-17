# Toggles Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new opt-in "Toggles" tab (fifth tab, after Songs) that lists free-toggle activatable abilities (Power Attack, Shifter Claws, Arcane Strike, Monk stances, Kinetist wild talents, etc.) and activates them via the existing combat-start / HUD-button pipeline.

**Architecture:** The existing activatable-activation Phase 0 in `BuffExecutor` already iterates all `IsActivatable` buffs — no execution changes needed. We add a new `Category.Toggle` enum value, a scan branch that catches activatables without `ActivatableAbilityResourceLogic`, a new tab button, a settings toggle, localization strings, and one UI guard to hide the round-limit slider for toggles. All other behavior (per-character portraits, BuffGroup assignment, combat-start checkbox, mutual-exclusivity handling) is inherited.

**Tech Stack:** C# / .NET Framework 4.8.1, HarmonyLib, Unity UI, Newtonsoft.Json (game-bundled, older). No unit test framework — verification uses the project's documented diagnostic workflow (Main.Log + deploy + Player.log grep).

**Spec:** `docs/superpowers/specs/2026-04-17-toggles-tab-design.md`

---

## Task 1: Add `Category.Toggle` enum value

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:2881-2886`

- [ ] **Step 1: Add the enum value**

Change the `Category` enum from:

```csharp
    public enum Category {
        Buff,
        Ability,
        Equipment,
        Song
    }
```

to:

```csharp
    public enum Category {
        Buff,
        Ability,
        Equipment,
        Song,
        Toggle
    }
```

Add `Toggle` as the last member. Order matters because any persisted integer-serialized values (none expected here — this enum is not saved, only filtered) would shift otherwise.

- [ ] **Step 2: Verify compile**

Run:
```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`. `findstr` warnings are harmless on Linux (CLAUDE.md).

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add Category.Toggle enum value"
```

---

## Task 2: Add `TogglesEnabled` field to `SavedState`

**Files:**
- Modify: `BuffIt2TheLimit/SaveState.cs:53` (insert after `ActivatablesEnabled`)

- [ ] **Step 1: Add the field**

After the line `public bool ActivatablesEnabled = true;` (currently around line 53), add:

```csharp
        [JsonProperty]
        [System.ComponentModel.DefaultValue(false)]
        public bool TogglesEnabled = false;
```

Default is `false` per the spec — existing saves should not see a new populated tab on load.

- [ ] **Step 2: Verify compile**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/SaveState.cs
git commit -m "feat: add TogglesEnabled to SavedState (default off)"
```

---

## Task 3: Extend activatable scan with Toggle branch

**Files:**
- Modify: `BuffIt2TheLimit/BufferState.cs:394-402` (the activatable branching block)

- [ ] **Step 1: Add the new else branch**

Current code at `BufferState.cs:394-402`:

```csharp
                        if (PerformanceGroups.Contains(blueprint.Group)) {
                            if (!SavedState.SongsEnabled) continue;
                            Main.Verbose($"      Adding song: {blueprint.Name} for {dude.CharacterName}", "state");
                            AddActivatable(dude, activatable, characterIndex, Category.Song);
                        } else if (hasResourceLogic) {
                            if (!SavedState.ActivatablesEnabled) continue;
                            Main.Verbose($"      Adding activatable: {blueprint.Name} (group={blueprint.Group}) for {dude.CharacterName}", "state");
                            AddActivatable(dude, activatable, characterIndex, Category.Ability);
                        }
```

Replace with:

```csharp
                        if (PerformanceGroups.Contains(blueprint.Group)) {
                            if (!SavedState.SongsEnabled) continue;
                            Main.Verbose($"      Adding song: {blueprint.Name} for {dude.CharacterName}", "state");
                            AddActivatable(dude, activatable, characterIndex, Category.Song);
                        } else if (hasResourceLogic) {
                            if (!SavedState.ActivatablesEnabled) continue;
                            Main.Verbose($"      Adding activatable: {blueprint.Name} (group={blueprint.Group}) for {dude.CharacterName}", "state");
                            AddActivatable(dude, activatable, characterIndex, Category.Ability);
                        } else {
                            if (!SavedState.TogglesEnabled) continue;
                            Main.Verbose($"      Adding toggle: {blueprint.Name} (group={blueprint.Group}) for {dude.CharacterName}", "state");
                            AddActivatable(dude, activatable, characterIndex, Category.Toggle);
                        }
```

The new `else` catches activatables where `srcItem == null`, `!PerformanceGroups.Contains(blueprint.Group)`, and `!hasResourceLogic` — exactly the free-toggle set from the spec. The existing `if (srcItem != null)` branch at line 379 already skips item-backed activatables (they go to Equipment), so we're not affecting those.

- [ ] **Step 2: Verify compile**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BufferState.cs
git commit -m "feat: scan free-toggle activatables into Category.Toggle"
```

---

## Task 4: Add tab icon field and loader

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:2508` (add `tabTogglesIcon` field)
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:2642-2647` (add loader block)

- [ ] **Step 1: Declare the sprite field**

After `internal static Sprite tabSongsIcon;` (currently line 2508), add:

```csharp
        internal static Sprite tabTogglesIcon;
```

- [ ] **Step 2: Load the icon**

After the `tabSongsIcon` loader block (currently lines 2642-2647), add:

```csharp
                if (tabTogglesIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintActivatableAbility>("9972f33f977fc724c838e59641b2fca5"); // Power Attack
                        if (bp != null) tabTogglesIcon = bp.Icon;
                    } catch { }
                }
```

The GUID `9972f33f977fc724c838e59641b2fca5` is the best-effort Power Attack BlueprintActivatableAbility. If the lookup fails (wrong GUID, game update), `tabTogglesIcon` stays null and the tab button renders text-only — non-fatal fallback, matches the pattern of the existing icon loaders.

**Optional GUID verification** via the Steam Deck:

```bash
ssh deck-direct "unzip -l '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Wrath_Data/StreamingAssets/blueprints.zip' | grep -i 'powerattack' | head"
```

Expected: a path like `.../PowerAttack.jbp` with an adjacent `.jbp` containing the GUID. If the listing is empty or unreachable, skip — the try/catch in the loader makes this non-blocking.

- [ ] **Step 3: Verify compile**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: load Power Attack icon for Toggles tab"
```

---

## Task 5: Wire up the fifth tab in the category bar

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1041` (add line after existing `Category.Song` registration)

- [ ] **Step 1: Register the tab**

After the line `CurrentCategory.Add(Category.Song, "cat.Songs".i8(), GlobalBubbleBuffer.tabSongsIcon);` (currently line 1041), add:

```csharp
            CurrentCategory.Add(Category.Toggle, "cat.Toggles".i8(), GlobalBubbleBuffer.tabTogglesIcon);
```

Placement: directly after `Category.Song` so Toggles becomes the fifth tab, per the spec.

- [ ] **Step 2: Verify compile**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`. The `"cat.Toggles".i8()` call will resolve to the raw key `cat.Toggles` at this point (no locale entry yet) — that's fine, we add the locale strings in Task 7. It will compile and run; the tab would just show literal `cat.Toggles` until then.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add Toggles tab after Songs in category bar"
```

---

## Task 6: Hide round-limit slider for `Category.Toggle`

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1958`

- [ ] **Step 1: Update the visibility guard**

Current line 1958:

```csharp
                roundLimitObj.SetActive(buff.IsActivatable);
```

Replace with:

```csharp
                roundLimitObj.SetActive(buff.IsActivatable && buff.Category != Category.Toggle);
```

Rationale (spec, Execution section): toggles are meant to stay on permanently; exposing a round-limit would be misleading. Songs and resource-based Abilities keep the slider.

- [ ] **Step 2: Verify compile**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: hide round-limit slider for Toggles"
```

---

## Task 7: Add settings-panel checkbox for `TogglesEnabled`

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:809` (insert new block after the `ActivatablesEnabled` toggle block)

- [ ] **Step 1: Add the toggle block**

After the `setting-activatables-enabled` block (currently ends at line 809 with `}`), insert:

```csharp
            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-toggles-enabled".i8());
                toggle.isOn = state.SavedState.TogglesEnabled;
                toggle.onValueChanged.AddListener(enabled => {
                    state.SavedState.TogglesEnabled = enabled;
                    state.InputDirty = true;
                    state.Save(true);
                });
            }
```

`InputDirty = true` triggers the recalculation on the next buff-window open, so the new tab populates as soon as the user enables the feature.

- [ ] **Step 2: Verify compile**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add Toggles settings checkbox"
```

---

## Task 8: Add localization keys

**Files:**
- Modify: `BuffIt2TheLimit/Config/en_GB.json:110` (insert after `setting-activatables-enabled`)
- Modify: `BuffIt2TheLimit/Config/de_DE.json:102` (insert after `setting-activatables-enabled`)

Per the project convention: only `en_GB` and `de_DE` get new keys; `fr_FR`, `ru_RU`, `zh_CN` fall back to English. Per the German-locale memory rule: keep the technical term "Toggles" in English for `de_DE`.

- [ ] **Step 1: Add English keys**

In `BuffIt2TheLimit/Config/en_GB.json`, after the line `"setting-activatables-enabled": "Enable activatable abilities",` (line 110), insert:

```json
  "cat.Toggles": "Toggles",
  "setting-toggles-enabled": "Enable free-toggle abilities (Power Attack, Shifter Claws, Monk stances, etc.)",
```

- [ ] **Step 2: Add German keys**

In `BuffIt2TheLimit/Config/de_DE.json`, after the line `"setting-activatables-enabled": "Aktivierbare Fähigkeiten aktivieren",` (line 102), insert:

```json
  "cat.Toggles": "Toggles",
  "setting-toggles-enabled": "Free-Toggle-Abilities aktivieren (Power Attack, Shifter Claws, Monk-Stances, etc.)",
```

- [ ] **Step 3: Verify JSON is valid**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && python3 -m json.tool BuffIt2TheLimit/Config/en_GB.json > /dev/null && python3 -m json.tool BuffIt2TheLimit/Config/de_DE.json > /dev/null && echo OK
```
Expected: `OK`. Any other output = malformed JSON; most common mistake is forgetting/adding a trailing comma.

- [ ] **Step 4: Verify the full build still succeeds**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add BuffIt2TheLimit/Config/en_GB.json BuffIt2TheLimit/Config/de_DE.json
git commit -m "i18n: add Toggles tab + settings strings (en_GB, de_DE)"
```

---

## Task 9: Deploy and verify in-game

No unit tests exist for this project (CLAUDE.md: "Diagnostic workflow without unit tests"). Verification is manual on the Steam Deck.

**Files:** None (deployment + log inspection)

- [ ] **Step 1: Deploy to Steam Deck**

```bash
cd /home/pascal/Code/wrath-mods/wrath-epic-buffing && ./deploy.sh
```
Expected: build succeeds, scp to deck succeeds, no errors.

- [ ] **Step 2: Verify DLL landed on the Deck**

```bash
ls -la BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll && \
ssh deck-direct "ls -la '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/BuffIt2TheLimit/BuffIt2TheLimit.dll'"
```
Expected: same file size and timestamp on both sides (CLAUDE.md verification pattern).

- [ ] **Step 3: Restart the game on the Deck and load a save**

Done manually by the user. The game must be fully restarted — UMM loads the DLL once at launch.

- [ ] **Step 4: Enable Toggles in settings**

Open the buff window, open the settings panel (gear button), enable the new "Enable free-toggle abilities" toggle. Close and reopen the buff window.

- [ ] **Step 5: Verify the tab appears and populates**

The buff window now shows five tabs; the fifth tab is Toggles with the Power Attack icon (or text-only if the GUID missed). Click the Toggles tab. Entries should include at minimum: Power Attack (for any martial party member), Monk stances (if any monks in party), Arcane Strike (if any caster has the feat), Shifter Claws (if a Shifter is present), Crane Wing / similar style toggles (if relevant feats are taken).

- [ ] **Step 6: Verify scan log**

```bash
ssh deck-direct "grep 'Adding toggle:' '/home/deck/.local/share/Steam/steamapps/compatdata/1184370/pfx/drive_c/users/steamuser/AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Player.log' | head -20"
```
Expected: lines like `Adding toggle: Power Attack (group=...) for <character>`. If `Main.Verbose` requires debug-flag — enable the "state" verbose category in mod settings first, or add temporary `Main.Log` calls (CLAUDE.md diagnostic workflow).

- [ ] **Step 7: Assign one toggle to Quick + combat-start, trigger a fight, verify activation**

- Pick one character + toggle (e.g., Power Attack on the main martial) in the Toggles tab.
- Tick combat-start checkbox.
- Start a combat encounter.
- Confirm the ability icon in the action bar shows as active (toggle on) for that character.

- [ ] **Step 8: Manually disable the toggle mid-combat, press the Quick HUD button, verify re-activation**

- Mid-combat, click the toggle off in the action bar.
- Press the Quick HUD button.
- Confirm the toggle turns back on (proves the HUD-button path works for toggles, same code as combat-start).

- [ ] **Step 9: Verify existing saves are unaffected**

Load a save that predates this change: the fifth tab should be present but empty (`TogglesEnabled` default is `false`). No errors in Player.log, no behavior change on other tabs.

- [ ] **Step 10: Final commit (if any fixups were needed)**

If any code changes were required during verification, commit them separately with a `fix:` prefix. If none, skip. The implementation is complete.

---

## Out of Scope (reiterated from spec)

- Mid-combat auto-re-enable polling — user re-fires via HUD button
- Curated "known auto-off" list
- Item-backed free-toggle activatables (already in Equipment)
- Separate "Re-enable Toggles" HUD button
- Area-transition auto-reactivation

## Rollback

If verification fails in ways not fixable inline, the feature is behind `TogglesEnabled` (default off), so existing users are unaffected. To fully revert:

```bash
git revert <hash-of-task-1> <hash-of-task-2> ... <hash-of-task-8>
./deploy.sh
```
