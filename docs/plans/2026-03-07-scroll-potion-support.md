# Scroll & Potion Buff Support — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add scrolls and potions as automatic buff sources with configurable priority, UMD handling, and per-buff source controls.

**Architecture:** Extend `BuffProvider` with a `SourceType` field. Add a new inventory scanning block in `RecalculateAvailableBuffs`. Modify `SortProviders()` to respect source priority ordering. Extend `BuffExecutor` with UMD retry logic and item consumption. Add UI controls for source toggles, priority dropdown, and UMD settings.

**Tech Stack:** C# / Unity / Kingmaker modding API / Harmony patches

---

### Task 1: Add Enums and Extend Data Structures

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:1875-1886` (enums)
- Modify: `BubbleBuffs/BubbleBuff.cs:325-433` (BuffProvider)
- Modify: `BubbleBuffs/SaveState.cs:20-73` (SavedBufferState, SavedBuffState)

**Step 1: Add `BuffSourceType` and `UmdMode` enums**

In `BubbleBuffs/BubbleBuffer.cs`, after the `Category` enum (line 1880):

```csharp
public enum BuffSourceType {
    Spell,
    Scroll,
    Potion
}

public enum UmdMode {
    SafeOnly,
    AllowIfPossible,
    AlwaysTry
}

public enum SourcePriority {
    SpellsScrollsPotions = 0,
    SpellsPotionsScrolls = 1,
    ScrollsSpellsPotions = 2,
    ScrollsPotionsSpells = 3,
    PotionsSpellsScrolls = 4,
    PotionsScrollsSpells = 5,
}
```

**Step 2: Extend `BuffProvider`**

In `BubbleBuffs/BubbleBuff.cs`, add fields to `BuffProvider` (after line 343):

```csharp
public BuffSourceType SourceType = BuffSourceType.Spell;
public ItemEntity SourceItem;
```

**Step 3: Extend `SavedBufferState`**

In `BubbleBuffs/SaveState.cs`, add to `SavedBufferState` (after line 31):

```csharp
[JsonProperty]
public SourcePriority GlobalSourcePriority = SourcePriority.SpellsScrollsPotions;
[JsonProperty]
public int UmdRetries = 3;
[JsonProperty]
public UmdMode UmdMode = UmdMode.AllowIfPossible;
[JsonProperty]
public bool ScrollsEnabled = true;
[JsonProperty]
public bool PotionsEnabled = true;
```

**Step 4: Extend `SavedBuffState`**

In `BubbleBuffs/SaveState.cs`, add to `SavedBuffState` (after line 69):

```csharp
[JsonProperty]
public int SourcePriorityOverride = -1; // -1 = use global default
[JsonProperty]
public int ScrollCap = -1; // -1 = no limit
[JsonProperty]
public int PotionCap = -1; // -1 = no limit
[JsonProperty]
public bool UseSpells = true;
[JsonProperty]
public bool UseScrolls = true;
[JsonProperty]
public bool UsePotions = true;
```

**Step 5: Commit**

```bash
git add BubbleBuffs/BubbleBuffer.cs BubbleBuffs/BubbleBuff.cs BubbleBuffs/SaveState.cs
git commit -m "feat: add data structures for scroll/potion buff support"
```

---

### Task 2: Add Localization Keys

**Files:**
- Modify: `BubbleBuffs/Config/en_GB.json`
- Modify: `BubbleBuffs/Config/de_DE.json`

**Step 1: Add English keys**

Add these entries to `BubbleBuffs/Config/en_GB.json` before the closing `}`:

```json
"setting-scrolls-enabled": "Enable scroll usage",
"setting-potions-enabled": "Enable potion usage",
"setting-umd-retries": "UMD max retries",
"setting-umd-mode": "UMD scroll usage mode",
"setting-source-priority": "Source priority",
"umd.safeonly": "Safe casters only",
"umd.allowifpossible": "Allow if DC reachable",
"umd.alwaystry": "Always try",
"priority.spells-scrolls-potions": "Spells > Scrolls > Potions",
"priority.spells-potions-scrolls": "Spells > Potions > Scrolls",
"priority.scrolls-spells-potions": "Scrolls > Spells > Potions",
"priority.scrolls-potions-spells": "Scrolls > Potions > Spells",
"priority.potions-spells-scrolls": "Potions > Spells > Scrolls",
"priority.potions-scrolls-spells": "Potions > Scrolls > Spells",
"priority.useglobal": "Use Global Default",
"source.spell": "Spell",
"source.scroll": "Scroll",
"source.potion": "Potion",
"use.spells": "Use Spells",
"use.scrolls": "Use Scrolls",
"use.potions": "Use Potions",
"log.umd-failed": "UMD check failed",
"log.umd-retries-exhausted": "UMD retries exhausted",
"log.last-item-consumed": "Last item consumed",
"limitscrolls": "Limit scrolls to",
"limitpotions": "Limit potions to"
```

**Step 2: Add German keys**

Add corresponding German translations to `BubbleBuffs/Config/de_DE.json`. Check existing file first for format.

**Step 3: Commit**

```bash
git add BubbleBuffs/Config/en_GB.json BubbleBuffs/Config/de_DE.json
git commit -m "feat: add localization keys for scroll/potion support"
```

---

### Task 3: Inventory Scanning in RecalculateAvailableBuffs

**Files:**
- Modify: `BubbleBuffs/BufferState.cs:136-161` (after abilities block)

**Step 1: Add inventory scanning block**

After the abilities `try/catch` block (line 161), add a new `try/catch` block:

```csharp
try {
    if (SavedState.ScrollsEnabled || SavedState.PotionsEnabled) {
        // Group usable items by blueprint to share credits
        var usableItems = Game.Instance.Player.Inventory
            .Where(item => item.Blueprint is BlueprintItemEquipmentUsable usable
                && (usable.Type == UsableItemType.Scroll || usable.Type == UsableItemType.Potion))
            .GroupBy(item => item.Blueprint)
            .ToList();

        foreach (var itemGroup in usableItems) {
            var blueprint = (BlueprintItemEquipmentUsable)itemGroup.Key;
            var spell = blueprint.Ability;
            var isScroll = blueprint.Type == UsableItemType.Scroll;
            var isPotion = blueprint.Type == UsableItemType.Potion;

            if (isScroll && !SavedState.ScrollsEnabled) continue;
            if (isPotion && !SavedState.PotionsEnabled) continue;

            // Count total items of this type
            int totalCount = itemGroup.Sum(item => item.Count);
            var sharedCredits = new ReactiveProperty<int>(totalCount);

            var category = Category.Consumable;

            if (isPotion) {
                // Any party member can drink a potion — self-cast only
                for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                    UnitEntityData dude = Group[characterIndex];
                    var abilityData = new AbilityData(spell, dude);

                    AddBuff(dude: dude,
                            book: null,
                            spell: abilityData,
                            baseSpell: null,
                            credits: sharedCredits,
                            newCredit: false,
                            creditClamp: 1, // self-cast only
                            charIndex: characterIndex,
                            archmageArmor: false,
                            category: category,
                            sourceType: BuffSourceType.Potion,
                            sourceItem: itemGroup.First());
                }
            } else if (isScroll) {
                // Scrolls: check which characters can use them
                int scrollCasterLevel = blueprint.CasterLevel;
                int scrollDC = 20 + scrollCasterLevel;

                for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                    UnitEntityData dude = Group[characterIndex];

                    bool canUse = false;

                    // Check if spell is on character's class list
                    bool onClassList = dude.Spellbooks.Any(book =>
                        book.Blueprint.SpellList?.Contains(spell) == true);

                    if (onClassList) {
                        canUse = true;
                    } else if (SavedState.UmdMode != UmdMode.SafeOnly) {
                        // Check UMD skill
                        var umdStat = dude.Stats.SkillUseMagicDevice;
                        int umdBonus = umdStat.ModifiedValue;

                        if (SavedState.UmdMode == UmdMode.AlwaysTry) {
                            canUse = umdBonus > 0; // has UMD ranks
                        } else {
                            // AllowIfPossible: check if DC is reachable
                            canUse = (umdBonus + 20) >= scrollDC;
                        }
                    }

                    if (!canUse) continue;

                    var abilityData = new AbilityData(spell, dude);

                    AddBuff(dude: dude,
                            book: null,
                            spell: abilityData,
                            baseSpell: null,
                            credits: sharedCredits,
                            newCredit: false,
                            creditClamp: int.MaxValue,
                            charIndex: characterIndex,
                            archmageArmor: false,
                            category: category,
                            sourceType: BuffSourceType.Scroll,
                            sourceItem: itemGroup.First());
                }
            }
        }
    }
} catch (Exception ex) {
    Main.Error(ex, "finding scrolls/potions");
}
```

**Step 2: Add `sourceType` and `sourceItem` parameters to `AddBuff`**

In `BubbleBuffs/BufferState.cs:318`, extend the `AddBuff` signature:

```csharp
public void AddBuff(UnitEntityData dude, Spellbook book, AbilityData spell, AbilityData baseSpell,
    IReactiveProperty<int> credits, bool newCredit, int creditClamp, int charIndex,
    bool archmageArmor = false, Category category = Category.Spell,
    BuffSourceType sourceType = BuffSourceType.Spell, ItemEntity sourceItem = null)
```

Pass `sourceType` and `sourceItem` through to `buff.AddProvider()`.

**Step 3: Extend `BubbleBuff.AddProvider` and `BuffProvider` constructor**

In `BubbleBuffs/BubbleBuff.cs:140`, add `sourceType` and `sourceItem` parameters to `AddProvider`:

```csharp
public void AddProvider(UnitEntityData provider, Spellbook book, AbilityData spell,
    AbilityData baseSpell, IReactiveProperty<int> credits, bool newCredit, int creditClamp,
    int u, BuffSourceType sourceType = BuffSourceType.Spell, ItemEntity sourceItem = null)
```

In the `BuffProvider` instantiation (line 153), set the new fields:

```csharp
var providerHandle = new BuffProvider(credits) {
    who = provider,
    spent = 0,
    clamp = creditClamp,
    book = book,
    spell = spell,
    baseSpell = baseSpell,
    CharacterIndex = u,
    ArchmageArmor = this.Key.Archmage,
    SourceType = sourceType,
    SourceItem = sourceItem
};
```

**Step 4: Add necessary using directives**

In `BufferState.cs`, add:
```csharp
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Items;
```

**Step 5: Build and verify no compile errors**

Run: `dotnet build` (or the project's build command)

**Step 6: Commit**

```bash
git add BubbleBuffs/BufferState.cs BubbleBuffs/BubbleBuff.cs
git commit -m "feat: scan inventory for scrolls/potions in RecalculateAvailableBuffs"
```

---

### Task 4: Source Priority Sorting

**Files:**
- Modify: `BubbleBuffs/BubbleBuff.cs:290-306` (SortProviders)
- Modify: `BubbleBuffs/BubbleBuff.cs:48-137` (BubbleBuff class)

**Step 1: Add priority helper**

Add a static helper method to `BubbleBuff` or a utility class:

```csharp
public static int[] GetSourceOrder(SourcePriority priority) {
    // Returns array where index = BuffSourceType, value = sort weight (lower = higher priority)
    return priority switch {
        SourcePriority.SpellsScrollsPotions => new[] { 0, 1, 2 },
        SourcePriority.SpellsPotionsScrolls => new[] { 0, 2, 1 },
        SourcePriority.ScrollsSpellsPotions => new[] { 1, 0, 2 },
        SourcePriority.ScrollsPotionsSpells => new[] { 2, 0, 1 },
        SourcePriority.PotionsSpellsScrolls => new[] { 1, 2, 0 },
        SourcePriority.PotionsScrollsSpells => new[] { 2, 1, 0 },
        _ => new[] { 0, 1, 2 }
    };
}
```

**Step 2: Add `SourcePriorityOverride` field to `BubbleBuff`**

```csharp
public int SourcePriorityOverride = -1; // -1 = use global
```

Initialize from save in `InitialiseFromSave`:
```csharp
SourcePriorityOverride = fromSave.SourcePriorityOverride;
```

**Step 3: Modify `SortProviders()`**

Replace the existing `SortProviders()` method (line 290):

```csharp
internal void SortProviders() {
    // Determine effective priority
    var globalPriority = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState?.GlobalSourcePriority
        ?? SourcePriority.SpellsScrollsPotions;
    var effectivePriority = SourcePriorityOverride >= 0
        ? (SourcePriority)SourcePriorityOverride
        : globalPriority;
    var sourceOrder = GetSourceOrder(effectivePriority);

    CasterQueue.Sort((a, b) => {
        // First sort by source type priority
        int aSourceWeight = sourceOrder[(int)a.SourceType];
        int bSourceWeight = sourceOrder[(int)b.SourceType];
        if (aSourceWeight != bSourceWeight)
            return aSourceWeight - bSourceWeight;

        // Then existing priority logic
        if (a.Priority == b.Priority) {
            int aScore = 0;
            int bScore = 0;

            if (!a.SelfCastOnly)
                aScore += 10_000;
            if (!b.SelfCastOnly)
                bScore += 10_000;

            return aScore - bScore;
        } else {
            return a.Priority - b.Priority;
        }
    });
}
```

**Step 4: Save SourcePriorityOverride**

In `BufferState.Save()` (line 234), inside `updateSavedBuff`:
```csharp
save.SourcePriorityOverride = buff.SourcePriorityOverride;
save.UseSpells = buff.UseSpells;
save.UseScrolls = buff.UseScrolls;
save.UsePotions = buff.UsePotions;
```

**Step 5: Build and verify**

**Step 6: Commit**

```bash
git add BubbleBuffs/BubbleBuff.cs BubbleBuffs/BufferState.cs
git commit -m "feat: implement source priority sorting for spell/scroll/potion providers"
```

---

### Task 5: Source Enable/Disable Filtering

**Files:**
- Modify: `BubbleBuffs/BubbleBuff.cs:48` (BubbleBuff class)
- Modify: `BubbleBuffs/BubbleBuff.cs:245-277` (Validate method)

**Step 1: Add per-buff source toggles**

Add to `BubbleBuff` class:
```csharp
public bool UseSpells = true;
public bool UseScrolls = true;
public bool UsePotions = true;
```

Initialize from save in `InitialiseFromSave`:
```csharp
UseSpells = fromSave.UseSpells;
UseScrolls = fromSave.UseScrolls;
UsePotions = fromSave.UsePotions;
```

**Step 2: Filter providers in Validate()**

In the `Validate()` method (line 248), add a source type check at the start of the caster loop:

```csharp
for (int n = 0; n < CasterQueue.Count; n++) {
    var caster = CasterQueue[n];

    // Skip disabled source types
    if (caster.SourceType == BuffSourceType.Spell && !UseSpells) continue;
    if (caster.SourceType == BuffSourceType.Scroll && !UseScrolls) continue;
    if (caster.SourceType == BuffSourceType.Potion && !UsePotions) continue;

    // ... existing credit check logic
```

**Step 3: Disable class-specific features for item providers**

In the `BuffExecutor.Execute()` method (`BuffExecutor.cs`), when creating `CastTask` objects, force feature flags to false for item providers:

```csharp
var task = new CastTask {
    // ... existing fields ...
    PowerfulChange = caster.SourceType == BuffSourceType.Spell && caster.PowerfulChange,
    ShareTransmutation = caster.SourceType == BuffSourceType.Spell && caster.ShareTransmutation,
    ReservoirCLBuff = caster.SourceType == BuffSourceType.Spell && caster.ReservoirCLBuff,
    AzataZippyMagic = caster.SourceType == BuffSourceType.Spell && caster.AzataZippyMagic,
    // ... rest ...
};
```

**Step 4: Commit**

```bash
git add BubbleBuffs/BubbleBuff.cs BubbleBuffs/BuffExecutor.cs
git commit -m "feat: add source enable/disable filtering and disable class features for items"
```

---

### Task 6: UMD Check and Retry Logic in Casting

**Files:**
- Modify: `BubbleBuffs/BuffExecutor.cs:64-219`
- Modify: `BubbleBuffs/BubbleBuff.cs:325` (BuffProvider)
- Modify: `BubbleBuffs/InstantExecutionEngine.cs`

**Step 1: Add UMD check helper to BuffProvider**

Add to `BuffProvider` class:

```csharp
public bool RequiresUmdCheck {
    get {
        if (SourceType != BuffSourceType.Scroll) return false;
        // Check if spell is on character's class spell list
        return !who.Spellbooks.Any(b => b.Blueprint.SpellList?.Contains(spell.Blueprint) == true);
    }
}

public int ScrollDC {
    get {
        if (SourceItem?.Blueprint is BlueprintItemEquipmentUsable usable)
            return 20 + usable.CasterLevel;
        return 25; // fallback
    }
}

public bool TryUmdCheck() {
    if (!RequiresUmdCheck) return true;

    var umdBonus = who.Stats.SkillUseMagicDevice.ModifiedValue;
    var roll = UnityEngine.Random.Range(1, 21); // d20
    var total = roll + umdBonus;
    Main.Verbose($"UMD Check: {who.CharacterName} rolled {roll} + {umdBonus} = {total} vs DC {ScrollDC}");
    return total >= ScrollDC;
}
```

**Step 2: Add `SourceType` to `CastTask`**

In `BuffExecutor.cs:222`, add:

```csharp
public BuffSourceType SourceType;
public ItemEntity SourceItem;
```

Set these when creating CastTask in `Execute()`:

```csharp
var task = new CastTask {
    // ... existing ...
    SourceType = caster.SourceType,
    SourceItem = caster.SourceItem,
};
```

**Step 3: Add UMD retry logic in `BuffExecutor.Execute()`**

Before creating the CastTask (around line 170), add UMD check:

```csharp
// UMD check for scroll usage
if (caster.SourceType == BuffSourceType.Scroll && caster.RequiresUmdCheck) {
    int maxRetries = State.SavedState.UmdRetries;
    bool passed = false;
    for (int retry = 0; retry < maxRetries; retry++) {
        if (caster.TryUmdCheck()) {
            passed = true;
            break;
        }
    }
    if (!passed) {
        Main.Verbose($"UMD retries exhausted for {caster.who.CharacterName} using {buff.Name}");
        if (badResult == null)
            badResult = tooltip.AddBad(buff);
        badResult.messages.Add($"  [{caster.who.CharacterName}] => [{Bubble.GroupById[target].CharacterName}], {"log.umd-retries-exhausted".i8()}");
        thisBuffBad++;
        continue;
    }
}
```

**Step 4: Add "last item" warning**

After the CastTask is added to the tasks list, check if this was the last item:

```csharp
if (caster.SourceType != BuffSourceType.Spell && caster.SourceItem != null) {
    var remainingCredits = caster.AvailableCredits - 1; // about to be consumed
    if (remainingCredits <= 0) {
        Main.Log($"{"log.last-item-consumed".i8()}: {buff.Name} ({caster.SourceType})");
    }
}
```

**Step 5: Commit**

```bash
git add BubbleBuffs/BuffExecutor.cs BubbleBuffs/BubbleBuff.cs BubbleBuffs/InstantExecutionEngine.cs
git commit -m "feat: add UMD check/retry logic and last-item warning for scroll/potion casting"
```

---

### Task 7: Global Settings UI

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:564-634` (MakeSettings method)

**Step 1: Add scroll/potion toggles to settings panel**

In `MakeSettings()`, after the existing toggles (line 623), add:

```csharp
{
    var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-scrolls-enabled".i8());
    toggle.isOn = state.SavedState.ScrollsEnabled;
    toggle.onValueChanged.AddListener(enabled => {
        state.SavedState.ScrollsEnabled = enabled;
        state.InputDirty = true;
        state.Save(true);
    });
}

{
    var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-potions-enabled".i8());
    toggle.isOn = state.SavedState.PotionsEnabled;
    toggle.onValueChanged.AddListener(enabled => {
        state.SavedState.PotionsEnabled = enabled;
        state.InputDirty = true;
        state.Save(true);
    });
}
```

**Step 2: Add UMD retries slider**

After the toggles, add a slider for UMD retries. Use existing `BubbleSettings.MakeSliderFloat` if available, or create an integer slider:

```csharp
{
    // UMD Retries slider (1-20)
    var labelObj = GameObject.Instantiate(togglePrefab, panel.transform);
    labelObj.DestroyComponents<ToggleWorkaround>();
    labelObj.DestroyChildren("Background");
    var label = labelObj.GetComponentInChildren<TextMeshProUGUI>();
    label.text = $"{"setting-umd-retries".i8()}: {state.SavedState.UmdRetries}";
    labelObj.SetActive(true);

    // Add spinner buttons for UMD retries
    var upButton = MakeButton("+", panel.transform);
    var downButton = MakeButton("-", panel.transform);

    upButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
        if (state.SavedState.UmdRetries < 20) {
            state.SavedState.UmdRetries++;
            label.text = $"{"setting-umd-retries".i8()}: {state.SavedState.UmdRetries}";
            state.Save(true);
        }
    });
    downButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
        if (state.SavedState.UmdRetries > 1) {
            state.SavedState.UmdRetries--;
            label.text = $"{"setting-umd-retries".i8()}: {state.SavedState.UmdRetries}";
            state.Save(true);
        }
    });
}
```

**Step 3: Add UMD mode cycle button**

```csharp
{
    string GetUmdModeText() => state.SavedState.UmdMode switch {
        UmdMode.SafeOnly => "umd.safeonly".i8(),
        UmdMode.AllowIfPossible => "umd.allowifpossible".i8(),
        UmdMode.AlwaysTry => "umd.alwaystry".i8(),
        _ => "?"
    };

    var umdLabel = GameObject.Instantiate(togglePrefab, panel.transform);
    umdLabel.DestroyComponents<ToggleWorkaround>();
    umdLabel.DestroyChildren("Background");
    var umdText = umdLabel.GetComponentInChildren<TextMeshProUGUI>();
    umdText.text = $"{"setting-umd-mode".i8()}: {GetUmdModeText()}";
    umdLabel.SetActive(true);

    var cycleButton = MakeButton(">", panel.transform);
    cycleButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
        state.SavedState.UmdMode = (UmdMode)(((int)state.SavedState.UmdMode + 1) % 3);
        umdText.text = $"{"setting-umd-mode".i8()}: {GetUmdModeText()}";
        state.InputDirty = true;
        state.Save(true);
    });
}
```

**Step 4: Add source priority cycle button**

```csharp
{
    string[] priorityKeys = {
        "priority.spells-scrolls-potions",
        "priority.spells-potions-scrolls",
        "priority.scrolls-spells-potions",
        "priority.scrolls-potions-spells",
        "priority.potions-spells-scrolls",
        "priority.potions-scrolls-spells"
    };

    var prioLabel = GameObject.Instantiate(togglePrefab, panel.transform);
    prioLabel.DestroyComponents<ToggleWorkaround>();
    prioLabel.DestroyChildren("Background");
    var prioText = prioLabel.GetComponentInChildren<TextMeshProUGUI>();
    prioText.text = $"{"setting-source-priority".i8()}: {priorityKeys[(int)state.SavedState.GlobalSourcePriority].i8()}";
    prioLabel.SetActive(true);

    var prioCycleButton = MakeButton(">", panel.transform);
    prioCycleButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
        state.SavedState.GlobalSourcePriority = (SourcePriority)(((int)state.SavedState.GlobalSourcePriority + 1) % 6);
        prioText.text = $"{"setting-source-priority".i8()}: {priorityKeys[(int)state.SavedState.GlobalSourcePriority].i8()}";
        state.InputDirty = true;
        state.Save(true);
    });
}
```

**Step 5: Increase settings panel size**

The settings panel size is defined at line 582. Increase the height to accommodate new controls:

```csharp
panel.Rect().sizeDelta = new Vector2(100, 200); // was (100, 100)
```

**Step 6: Commit**

```bash
git add BubbleBuffs/BubbleBuffer.cs
git commit -m "feat: add global settings UI for scroll/potion configuration"
```

---

### Task 8: Per-Buff Source Controls in Details View

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:814-998` (MakeDetailsView)

**Step 1: Add source toggles to the caster popout**

In `MakeDetailsView()`, after the existing caster toggle options (around the area where ShareTransmutation/PowerfulChange toggles are created in the caster popout), add source enable/disable toggles.

Find the section where caster popout toggles are created and add after them:

```csharp
// Source type toggles (only shown when buff has multiple source types)
var useSpellsToggle = MakeSpellPopoutToggle("use.spells".i8());
var useScrollsToggle = MakeSpellPopoutToggle("use.scrolls".i8());
var usePotionsToggle = MakeSpellPopoutToggle("use.potions".i8());
```

**Step 2: Wire up the toggles in UpdateDetailsView**

In the `UpdateDetailsView` action, add logic to show/hide and bind source toggles based on available source types for the current buff:

```csharp
var buff = view.Selected;
if (buff != null) {
    bool hasScrollProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Scroll);
    bool hasPotionProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Potion);
    bool hasSpellProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Spell);

    useSpellsToggle.toggle.gameObject.SetActive(hasSpellProviders);
    useScrollsToggle.toggle.gameObject.SetActive(hasScrollProviders);
    usePotionsToggle.toggle.gameObject.SetActive(hasPotionProviders);

    if (hasSpellProviders) {
        useSpellsToggle.toggle.isOn = buff.UseSpells;
        // bind onValueChanged
    }
    // similar for scrolls and potions
}
```

**Step 3: Add source type icon to caster portraits**

In the caster portrait rendering code, add a label or icon indicating source type:

```csharp
// In the caster portrait text/label area
string sourceLabel = caster.SourceType switch {
    BuffSourceType.Scroll => $" [{"source.scroll".i8()}]",
    BuffSourceType.Potion => $" [{"source.potion".i8()}]",
    _ => ""
};
portrait.Text.text = $"{caster.who.CharacterName}{sourceLabel} x{caster.AvailableCredits}";
```

**Step 4: Commit**

```bash
git add BubbleBuffs/BubbleBuffer.cs
git commit -m "feat: add per-buff source toggles and source type labels in details view"
```

---

### Task 9: Item Consumption in Execution Engines

**Files:**
- Modify: `BubbleBuffs/InstantExecutionEngine.cs`
- Modify: `BubbleBuffs/AnimatedExecutionEngine.cs`
- Modify: `BubbleBuffs/Handlers/EngineCastingHandler.cs`

**Step 1: Handle item consumption in EngineCastingHandler**

In `EngineCastingHandler`, modify `OnBeforeEventAboutToTrigger` to handle item-based casts:

```csharp
// In OnBeforeEventAboutToTrigger, after existing logic:
if (_castTask.SourceType != BuffSourceType.Spell && _castTask.SourceItem != null) {
    // Item will be consumed by the game's casting system
    // No additional handling needed as UsableItem.Spend() is called by the engine
}
```

**Step 2: Disable spellbook features for item casts**

In `EngineCastingHandler` constructor, skip spellbook-specific operations for item casts:

```csharp
if (_castTask.SourceType == BuffSourceType.Spell) {
    SetAllRetentions();
    ModifyCasterLevel();
}

// Always remove spell resistance (applies to all source types)
RemoveSpellResistance();
```

**Step 3: Skip spell slot operations for items**

In `IncreaseSpellSlotsAvailable` and related methods, add early returns for non-spell sources:

```csharp
// At the top of IncreaseSpellSlotsAvailable:
if (_castTask.SourceType != BuffSourceType.Spell) return;
```

**Step 4: Commit**

```bash
git add BubbleBuffs/InstantExecutionEngine.cs BubbleBuffs/AnimatedExecutionEngine.cs BubbleBuffs/Handlers/EngineCastingHandler.cs
git commit -m "feat: handle item consumption and skip spellbook features for item-based casts"
```

---

### Task 10: BufferState.Save() and InitialiseFromSave() Updates

**Files:**
- Modify: `BubbleBuffs/BufferState.cs:233-286` (Save method)
- Modify: `BubbleBuffs/BubbleBuff.cs:178-199` (InitialiseFromSave)

**Step 1: Save new per-buff fields**

In `updateSavedBuff` (inside `Save()`), add:

```csharp
save.SourcePriorityOverride = buff.SourcePriorityOverride;
save.ScrollCap = buff.ScrollCap;
save.PotionCap = buff.PotionCap;
save.UseSpells = buff.UseSpells;
save.UseScrolls = buff.UseScrolls;
save.UsePotions = buff.UsePotions;
```

**Step 2: Load in InitialiseFromSave**

In `BubbleBuff.InitialiseFromSave()`, add:

```csharp
SourcePriorityOverride = state.SourcePriorityOverride;
UseSpells = state.UseSpells;
UseScrolls = state.UseScrolls;
UsePotions = state.UsePotions;
```

**Step 3: Add missing fields to BubbleBuff**

```csharp
public int ScrollCap = -1;
public int PotionCap = -1;
```

**Step 4: Commit**

```bash
git add BubbleBuffs/BufferState.cs BubbleBuffs/BubbleBuff.cs
git commit -m "feat: persist scroll/potion settings in save state"
```

---

### Task 11: Integration Testing and Polish

**Files:**
- All modified files

**Step 1: Build the project**

```bash
dotnet build
```

Fix any compile errors.

**Step 2: Manual testing checklist**

Test in-game (load a save with spellcasters and scrolls/potions in inventory):

- [ ] Scrolls appear in buff list under "Consumables" category
- [ ] Potions appear in buff list under "Consumables" category
- [ ] Source priority dropdown works in settings
- [ ] UMD retries slider works
- [ ] UMD mode cycling works
- [ ] Per-buff source toggles show/hide correctly
- [ ] Scroll casting works for class-list characters
- [ ] Scroll casting with UMD check works
- [ ] UMD retry limit is respected
- [ ] Potion self-cast works
- [ ] Item is consumed after successful cast
- [ ] "Last item" warning appears in log
- [ ] Azata/ShareTransmutation/PowerfulChange are disabled for item providers
- [ ] Caster labels show source type icon
- [ ] Settings persist across save/load
- [ ] Source priority override per-buff works

**Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete scroll & potion buff support"
```

---

## Task Dependencies

```
Task 1 (Data Structures) ──┬── Task 2 (Localization)
                            │
                            ├── Task 3 (Inventory Scanning) ── Task 4 (Priority Sorting)
                            │                                         │
                            │                                   Task 5 (Source Filtering)
                            │                                         │
                            │                                   Task 6 (UMD + Casting)
                            │                                         │
                            ├── Task 7 (Global Settings UI) ──────────┤
                            │                                         │
                            ├── Task 8 (Per-Buff UI) ─────────────────┤
                            │                                         │
                            └── Task 9 (Execution Engines) ── Task 10 (Save/Load)
                                                                      │
                                                                Task 11 (Testing)
```

Tasks 1-2 can be parallelized. Tasks 7-8 can be parallelized after Task 1. Task 9 can run in parallel with Tasks 4-6.
