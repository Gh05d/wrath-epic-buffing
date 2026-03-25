# Song/Performance Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Bard/Skald performance and Azata song support as activatable buffs with a dedicated UI tab, resource checking, and BuffGroup assignment.

**Architecture:** Songs are `ActivatableAbility` toggles, fundamentally different from spell casting. We add a new scan phase in `BufferState`, a new `AddSong()` method bypassing the spell-based `AddBuff()`/`GetBeneficialBuffs()` pipeline, a song-specific validation and execution path in `BuffExecutor`, and a new "Songs" category tab in the UI. Songs activate via `ability.IsOn = true` after checking `ability.IsAvailable`.

**Tech Stack:** C#/.NET Framework 4.8.1, Unity UI, HarmonyLib, Newtonsoft.Json

**Spec:** `docs/superpowers/specs/2026-03-25-song-performance-support-design.md`

**Build command:** `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Important references:**
- `ActivatableAbility.IsOn` (bool property) — setter calls `SetIsOn(true, null)` internally
- `ActivatableAbility.IsAvailable` — combined resources + restrictions check
- `ActivatableAbility.ResourceCount` — remaining rounds (int)
- `ActivatableAbilityGroup.BardicPerformance` (1) — Bard/Skald mutual exclusivity
- `ActivatableAbilityGroup.AzataMythicPerformance` (28) — Azata mutual exclusivity
- Separate groups: Bard + Azata can run simultaneously

---

### Task 1: Add Song enums and BuffKey constructor

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:17-48` (BuffKey struct)
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:2587-2604` (Category and BuffSourceType enums — NOTE: these are in BubbleBuffer.cs, not BubbleBuff.cs despite the explore report)

- [ ] **Step 1: Find and verify enum locations**

The enums `Category`, `BuffSourceType`, `BuffGroup`, `SourcePriority` etc. may be in `BubbleBuffer.cs` (the ~3000 line UI file). Grep for `enum Category` and `enum BuffSourceType` to confirm exact file and line.

```bash
grep -n "enum Category\|enum BuffSourceType" BuffIt2TheLimit/BubbleBuffer.cs BuffIt2TheLimit/BubbleBuff.cs
```

- [ ] **Step 2: Add `Song` to `Category` enum**

In the file containing `enum Category`, add `Song` after `Equipment`:

```csharp
public enum Category {
    Buff,
    Ability,
    Equipment,
    Song
}
```

- [ ] **Step 3: Add `Song` to `BuffSourceType` enum**

In the same file, add `Song` after `Equipment`:

```csharp
public enum BuffSourceType {
    Spell,
    Scroll,
    Potion,
    Equipment,
    Song
}
```

- [ ] **Step 4: Add `BuffKey` constructor for activatable abilities**

In `BuffIt2TheLimit/BubbleBuff.cs:17-48`, add a second constructor that takes a `BlueprintGuid` directly (songs have no metamagic, no archmage):

```csharp
public BuffKey(BlueprintGuid blueprintGuid) {
    Guid = blueprintGuid.m_Guid;
    MetamagicMask = 0;
    Archmage = false;
}
```

Add `using Kingmaker.Blueprints;` at the top if not already present (for `BlueprintGuid`).

- [ ] **Step 5: Build to verify no compile errors**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuff.cs BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add Song enum values and BuffKey activatable constructor"
```

---

### Task 2: Extend BubbleBuff for songs

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:49-429` (BubbleBuff class)

- [ ] **Step 1: Add song-specific fields to BubbleBuff**

After the existing `UseExtendRod` field (line 145), add:

```csharp
public bool IsSong;
public Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility ActivatableSource;
```

- [ ] **Step 2: Add song constructor**

After the existing constructor (line 125-133), add a factory-style constructor for songs:

```csharp
public BubbleBuff(Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility activatable) {
    this.ActivatableSource = activatable;
    this.Spell = null;
    this.IsSong = true;
    var blueprint = activatable.Blueprint;
    this.NameLower = blueprint.Name.ToLower();
    this.Key = new BuffKey(blueprint.AssetGuid);
    this.Category = Category.Song;
}
```

- [ ] **Step 3: Add `Icon` property to centralize icon access**

After `Name`/`NameMeta` properties, add:

```csharp
public Sprite Icon => IsSong ? ActivatableSource.Blueprint.Icon : Spell?.Blueprint?.Icon;
```

Add `using UnityEngine;` at the top if not already present (for `Sprite`).

- [ ] **Step 4: Null-guard Spell-dependent properties**

Modify `Name` (line 112):
```csharp
public string Name => IsSong ? ActivatableSource.Blueprint.Name
    : Key.Archmage ? "Archmage Armor"
    : Spell.Name;
```

Modify `NameMeta` (line 113):
```csharp
public string NameMeta => IsSong ? Name : $"{Spell.Name} {MetaMagicFlags}";
```

Modify `MetaMagicFlags` getter (lines 93-108) — add early return at the top:
```csharp
private string MetaMagicFlags {
    get {
        if (IsSong || Metamagics == null)
            return "";
        // ... rest unchanged
    }
}
```

- [ ] **Step 5: Null-guard `BuffProvider.SelfCastOnly`**

In `BuffProvider` class (line 518-520), add `Song` check:

```csharp
public bool SelfCastOnly =>
    SourceType == BuffSourceType.Song ||
    SourceType == BuffSourceType.Potion ||
    spell.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Owner;
```

- [ ] **Step 6: Initialize `BuffsApplied` in song constructor to prevent null crashes**

In the song constructor (Step 2), after setting `Category`, add:

```csharp
this.BuffsApplied = new AbilityCombinedEffects(Array.Empty<AbilityEffectEntry>());
```

This prevents NullReferenceException in `UpdateDetailsView` (line 1721: `buff.BuffsApplied.All.ToArray()`) and other UI code that iterates `BuffsApplied`.

- [ ] **Step 7: Add ValidateSong method**

After `ValidateMass()` (line 358), add:

```csharp
public void ValidateSong() {
    if (ActivatableSource == null) return;
    ActualCastQueue = new List<(string, BuffProvider)>();

    if (ActivatableSource.IsOn) {
        // Already active — mark all wanted as given
        foreach (var target in wanted) {
            given.Add(target);
        }
        return;
    }

    if (!ActivatableSource.IsAvailable) {
        Main.Verbose($"Song {Name}: not available (resources or restrictions)");
        return;
    }

    if (CasterQueue.Count == 0) return;

    var caster = CasterQueue[0];
    // Mark all wanted targets as given (songs are party-wide)
    foreach (var target in wanted) {
        given.Add(target);
    }
    ActualCastQueue.Add((caster.who.UniqueId, caster));
}
```

- [ ] **Step 8: Update Validate() to route songs**

In `Validate()` (line 265), add song routing at the top:

```csharp
public void Validate() {
    if (IsSong) {
        ValidateSong();
        return;
    }
    if (IsMass) {
        // ... existing code
```

- [ ] **Step 9: Guard SortProviders against Song source type**

In `SortProviders()` (line 383-411), songs should skip sorting since they only have one caster. Add at the top:

```csharp
internal void SortProviders() {
    if (IsSong) return;
    // ... rest unchanged
```

- [ ] **Step 10: Build to verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 11: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuff.cs
git commit -m "feat: extend BubbleBuff with song support and ValidateSong"
```

---

### Task 3: Add song scanning in BufferState

**Files:**
- Modify: `BuffIt2TheLimit/BufferState.cs:39-376` (RecalculateAvailableBuffs) and add `AddSong()` method

- [ ] **Step 1: Add using directives**

At the top of `BufferState.cs`, add:

```csharp
using Kingmaker.UnitLogic.ActivatableAbilities;
```

Verify this namespace isn't already imported.

- [ ] **Step 2: Add the `AddSong()` method**

After `AddBuff()` (line 651), before `CanUseItemWithUmd()` (line 654), add:

```csharp
private static readonly HashSet<ActivatableAbilityGroup> SongGroups = new() {
    ActivatableAbilityGroup.BardicPerformance,
    (ActivatableAbilityGroup)28 // AzataMythicPerformance
};

public void AddSong(UnitEntityData dude, ActivatableAbility activatable, int charIndex) {
    var blueprint = activatable.Blueprint;
    var key = new BuffKey(blueprint.AssetGuid);

    if (BuffsByKey.TryGetValue(key, out var existing)) {
        // Song already added (shouldn't happen — each character has unique performances)
        return;
    }

    var buff = new BubbleBuff(activatable);

    // Create a single provider for this song's caster
    var credits = new ReactiveProperty<int>(activatable.ResourceCount);
    var provider = new BuffProvider(credits) {
        who = dude,
        spent = 0,
        clamp = 1, // SelfCastOnly — one activation
        book = null,
        spell = null,
        baseSpell = null,
        CharacterIndex = charIndex,
        SourceType = BuffSourceType.Song,
        SourceItem = null
    };
    buff.CasterQueue.Add(provider);

    BuffsByKey[key] = buff;
}
```

- [ ] **Step 3: Add Phase 4 scan in RecalculateAvailableBuffs**

After the equipment scan try/catch (line 348), before the `BuffList` assignment (line 355), add:

```csharp
try {
    if (SavedState.SongsEnabled) {
        for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
            UnitEntityData dude = Group[characterIndex];
            foreach (var activatable in dude.ActivatableAbilities.RawFacts) {
                var blueprint = activatable.Blueprint;
                if (!SongGroups.Contains(blueprint.Group))
                    continue;

                Main.Verbose($"      Adding song: {blueprint.Name} for {dude.CharacterName}", "state");
                AddSong(dude, activatable, characterIndex);
            }
        }
    }
} catch (Exception ex) {
    Main.Error(ex, "finding songs");
}
```

Note: `dude.ActivatableAbilities` returns an `ActivatableAbilityCollection` which extends `EntityFactsProcessor<ActivatableAbility>`. The `.RawFacts` property returns the list of facts.

- [ ] **Step 4: Build to verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

If `ActivatableAbilityGroup` doesn't contain `AzataMythicPerformance` as a named member, use the cast `(ActivatableAbilityGroup)28` as shown. If it does exist, use the named member.

If `dude.ActivatableAbilities.RawFacts` doesn't compile, try `dude.ActivatableAbilities.Enumerable` or `dude.ActivatableAbilities.m_Facts` (publicized access). Check the type of `dude.ActivatableAbilities` via build errors.

- [ ] **Step 5: Commit**

```bash
git add BuffIt2TheLimit/BufferState.cs
git commit -m "feat: add song scanning phase in RecalculateAvailableBuffs"
```

---

### Task 4: Add SaveState support

**Files:**
- Modify: `BuffIt2TheLimit/SaveState.cs:22-52` (SavedBufferState)
- Modify: `BuffIt2TheLimit/BufferState.cs:439-503` (Save method)
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:188-219` (InitialiseFromSave)

- [ ] **Step 1: Add SongsEnabled to SavedBufferState**

In `SaveState.cs`, after `EquipmentEnabled` (line 45), add:

```csharp
[JsonProperty]
[System.ComponentModel.DefaultValue(true)]
public bool SongsEnabled = true;
```

The `[DefaultValue(true)]` ensures old saves without this field default to `true` on deserialization.

- [ ] **Step 2: Guard song-specific fields in Save()**

In `BufferState.cs`, in the `updateSavedBuff` local function (line 440-476), the caster-specific save logic (lines 458-468) writes `ShareTransmutation`, `PowerfulChange`, etc. These are meaningless for songs but won't crash — song providers have default values for these fields. No change needed here.

However, `save.UseSpells`/`UseScrolls`/etc. (lines 471-475) will be saved for songs too. This is harmless — they'll just have default `true` values. No change needed.

- [ ] **Step 3: Build to verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/SaveState.cs
git commit -m "feat: add SongsEnabled toggle to SavedBufferState"
```

---

### Task 5: Add song execution path in BuffExecutor

**Files:**
- Modify: `BuffIt2TheLimit/BuffExecutor.cs:155-382` (Execute method)

- [ ] **Step 1: Add song activation at the top of Execute()**

In `Execute()`, after the `State.Recalculate(false)` call (line 167) and before the targets/tasks setup (line 170), add a song activation block:

```csharp
// Phase 0: Activate songs before casting buffs
var activatedGroups = new HashSet<(ActivatableAbilityGroup, string)>();
foreach (var songBuff in State.BuffList.Where(b => b.IsSong && b.InGroups.Contains(buffGroup) && b.Fulfilled > 0)) {
    try {
        var activatable = songBuff.ActivatableSource;
        if (activatable == null || activatable.IsOn) {
            Main.Verbose($"Song {songBuff.Name}: already active or null, skipping");
            continue;
        }

        var group = activatable.Blueprint.Group;
        var caster = songBuff.CasterQueue.FirstOrDefault()?.who;
        if (caster == null) continue;

        // Mutual exclusivity: only one song per ActivatableAbilityGroup per caster
        var groupKey = (group, caster.UniqueId);
        if (activatedGroups.Contains(groupKey)) {
            Main.Log($"Song {songBuff.Name}: skipped — another {group} song already activated for {caster.CharacterName}");
            continue;
        }

        if (!activatable.IsAvailable) {
            Main.Verbose($"Song {songBuff.Name}: not available (resources or restrictions)");
            continue;
        }

        Main.Verbose($"Activating song: {songBuff.Name} on {caster.CharacterName}");
        activatable.IsOn = true;
        activatedGroups.Add(groupKey);
    } catch (Exception ex) {
        Main.Error(ex, $"activating song {songBuff.Name}");
    }
}
```

Add `using Kingmaker.UnitLogic.ActivatableAbilities;` at the top of `BuffExecutor.cs` if not already present.

- [ ] **Step 2: Skip songs in the main buff execution loop**

In the existing `foreach` loop (line 187), songs shouldn't create CastTasks. Add a skip at the top:

```csharp
foreach (var buff in State.BuffList.Where(b => b.InGroups.Contains(buffGroup) && b.Fulfilled > 0)) {
    if (buff.IsSong) continue; // Songs handled in Phase 0
    // ... rest unchanged
```

- [ ] **Step 3: Build to verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BuffExecutor.cs
git commit -m "feat: add song activation path in BuffExecutor.Execute"
```

---

### Task 6: Add Songs tab to UI

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:983-988` (category tab creation)
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:2222-2357` (icon loading)

- [ ] **Step 1: Add tabSongsIcon field**

Near line 2224 in `BubbleBuffer.cs`, after the existing tab icon fields:

```csharp
internal static Sprite tabSongsIcon;
```

- [ ] **Step 2: Load the songs tab icon**

After the `tabAbilitiesIcon` loading block (line 2352-2356), add:

```csharp
if (tabSongsIcon == null) {
    try {
        // InspireCourage as the Songs tab icon
        var bp = Resources.GetBlueprint<BlueprintActivatableAbility>("5250c10feed9f8744850fa3b4814e7c0");
        if (bp != null) tabSongsIcon = bp.Icon;
    } catch { }
}
```

Note: `5250c10feed9f8744850fa3b4814e7c0` is `InspireCourageToggleAbility`. If this GUID doesn't work, use any known song blueprint GUID. Add `using Kingmaker.UnitLogic.ActivatableAbilities;` at top if not already present (for `BlueprintActivatableAbility`).

**Fallback:** If `BlueprintActivatableAbility` can't be used with `Resources.GetBlueprint`, use `ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(guid)` instead.

- [ ] **Step 3: Add Songs tab to category buttons**

At line 988, after the Equipment tab, add:

```csharp
CurrentCategory.Add(Category.Song, "cat.Songs".i8(), GlobalBubbleBuffer.tabSongsIcon);
```

- [ ] **Step 4: Fix `BindBuffToView` null crashes for songs**

In `BindBuffToView` (line 2701-2742), the method accesses `buff.Spell.Blueprint.Icon`, `buff.Spell.Blueprint.School`, `buff.Spell.IsMetamagicked()`, and `buff.Spell.Blueprint` for tooltips. All crash for songs where `Spell == null`.

Replace the icon line (2711):
```csharp
view.ChildObject("Icon/IconImage").GetComponent<Image>().sprite = buff.Icon;
```

Add song guard after the icon lines (after line 2713):
```csharp
if (buff.IsSong) {
    view.ChildObject("School/SchoolLabel").GetComponent<TextMeshProUGUI>().text = "";
    view.ChildObject("Metamagic").SetActive(false);
    // No spell tooltip for songs
    return;
}
```

This early-returns for songs before any other `buff.Spell` access.

- [ ] **Step 5: Guard `UpdateDetailsView` for songs**

In `UpdateDetailsView` (around line 1721), `buff.BuffsApplied.All.ToArray()` is called. The song constructor initializes `BuffsApplied` to an empty instance (Task 2, Step 6), so this won't crash — but it will show an empty overwrite-check section. The source toggles (lines 1733-1748) will also show nothing useful for songs since they check for Spell/Scroll/Potion/Equipment providers.

No code change needed — the empty `BuffsApplied` and the fact that song providers have `SourceType.Song` (not matching any existing toggle) means the detail view just shows the portrait and group checkboxes, which is correct.

- [ ] **Step 6: Build to verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 7: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add Songs category tab to buff window UI"
```

---

### Task 7: Add settings toggle for SongsEnabled

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs` (settings panel creation)

- [ ] **Step 1: Find the settings panel creation code**

Search for `setting-equipment-enabled` or `EquipmentEnabled` in `BubbleBuffer.cs` to find where the existing enable/disable toggles are created. The songs toggle should go right after the equipment toggle.

```bash
grep -n "EquipmentEnabled\|setting-equipment" BuffIt2TheLimit/BubbleBuffer.cs
```

- [ ] **Step 2: Add SongsEnabled toggle**

After the `EquipmentEnabled` toggle block (line 749-757), add:

```csharp
{
    var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-songs-enabled".i8());
    toggle.isOn = state.SavedState.SongsEnabled;
    toggle.onValueChanged.AddListener(enabled => {
        state.SavedState.SongsEnabled = enabled;
        state.InputDirty = true;
        state.Save(true);
    });
}
```

This follows the exact same pattern as the equipment toggle above.

- [ ] **Step 3: Build and verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add SongsEnabled toggle to settings panel"
```

---

### Task 8: Add localization keys

**Files:**
- Modify: `BuffIt2TheLimit/Config/en_GB.json`
- Modify: `BuffIt2TheLimit/Config/de_DE.json`
- Modify: `BuffIt2TheLimit/Config/fr_FR.json`
- Modify: `BuffIt2TheLimit/Config/ru_RU.json`
- Modify: `BuffIt2TheLimit/Config/zh_CN.json`

- [ ] **Step 1: Add keys to en_GB.json**

Add at the end (before the closing `}`):

```json
"cat.Songs": "Songs",
"setting-songs-enabled": "Enable song activation",
"song.rounds-remaining": "Rounds remaining: {0}"
```

- [ ] **Step 2: Add keys to de_DE.json**

```json
"cat.Songs": "Songs",
"setting-songs-enabled": "Song-Aktivierung aktivieren",
"song.rounds-remaining": "Verbleibende Runden: {0}"
```

Note: "Songs" stays English per CLAUDE.md convention for technical gaming terms.

- [ ] **Step 3: Add keys to fr_FR.json, ru_RU.json, zh_CN.json**

Add `"cat.Songs": "Songs"`, `"setting-songs-enabled": "Enable song activation"`, and `"song.rounds-remaining": "Rounds remaining: {0}"` (English fallback for incomplete locales).

- [ ] **Step 4: Build to verify (embedded resources)**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 5: Commit**

```bash
git add BuffIt2TheLimit/Config/
git commit -m "feat: add song localization keys to all locales"
```

---

### Task 9: Integration test — deploy and verify in-game

**Files:** None (testing only)

- [ ] **Step 1: Full Release build**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 2: Deploy to Steam Deck**

```bash
./deploy.sh
```

Verify DLL timestamp matches:
```bash
ls -la BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll
ssh deck-direct "ls -la '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/BuffIt2TheLimit/BuffIt2TheLimit.dll'"
```

- [ ] **Step 3: In-game verification checklist**

Start the game on Steam Deck, load a save with a Bard or Azata character. Verify:

1. **Songs tab appears** in the buff window alongside Buffs/Abilities/Equipment
2. **Songs are listed** under the Songs tab — Inspire Courage, etc.
3. **BuffGroup assignment works** — can assign songs to Long/Important/Quick
4. **Target selection** — clicking portraits assigns "wanted" (party-wide, but still needs wanted targets)
5. **Execution** — pressing the buff group button activates the song
6. **Already active** — if song is already on, it's skipped without errors
7. **Resource check** — with 0 rounds remaining, song is skipped
8. **Mutual exclusivity** — enabling two Bard performances in the same group only activates the first
9. **Settings toggle** — SongsEnabled toggle appears and disabling it hides songs from scan
10. **Save/Load** — song assignments persist across save/load

- [ ] **Step 4: Check Player.log for errors**

```bash
ssh deck-direct "grep -i 'error\|exception\|song\|activat' '/home/deck/.local/share/Steam/steamapps/compatdata/1184370/pfx/drive_c/users/steamuser/AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Player.log' | tail -30"
```

- [ ] **Step 5: Commit any fixes from testing**

If issues are found during testing, fix and commit each fix separately.
