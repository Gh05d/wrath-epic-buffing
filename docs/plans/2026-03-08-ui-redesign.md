# UI Redesign & Equipment Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Merge Spells/Consumables tabs into unified "Buffs" tab, add inline source controls, rename labels, add tab icons, and implement Equipment support.

**Architecture:** Modify the Category enum and tab creation to consolidate Spells+Consumables into "Buffs". Move source-type checkboxes from the hidden spell popout into a visible control row above caster portraits. Add source-icon overlays to caster portraits. Rename Items to Equipment and implement quickslot item scanning. Add game-blueprint icons to tab buttons.

**Tech Stack:** C# / Unity UI / Harmony patches / Owlcat WotR API

---

### Task 1: Update Category Enum and Tab Definitions

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:2053-2058` (Category enum)
- Modify: `BubbleBuffs/BubbleBuffer.cs:835-850` (MakeFilters tab creation)
- Modify: `BubbleBuffs/BubbleBuffer.cs:893-894` (RefreshFiltering category check)

**Step 1: Update the Category enum**

Replace the enum at line 2053-2058:

```csharp
public enum Category {
    Buff,
    Ability,
    Equipment,
}
```

**Step 2: Update tab creation in MakeFilters**

Replace lines 838-841 in `MakeFilters()`:

```csharp
CurrentCategory.Add(Category.Buff, "cat.buffs".i8());
CurrentCategory.Add(Category.Ability, "cat.Abilities".i8());
CurrentCategory.Add(Category.Equipment, "cat.Equipment".i8());
```

And update the default at line 850:

```csharp
CurrentCategory.Selected.Value = Category.Buff;
```

**Step 3: Update localization keys**

In `BubbleBuffs/Config/en_GB.json`, replace the category keys:

```json
"cat.spells": "Buffs",
"cat.Abilities": "Abilities",
"cat.Items": "Equipment",
```

Remove `"cat.Consumables"` line. Add new key:

```json
"cat.buffs": "Buffs",
"cat.Equipment": "Equipment",
```

In `BubbleBuffs/Config/de_DE.json`:

```json
"cat.buffs": "Buffs",
"cat.Equipment": "Ausrustung",
```

Remove `"cat.Consumables"` line.

Update the other locale files (fr_FR, ru_RU, zh_CN) similarly with the new keys and removing `cat.Consumables`.

**Step 4: Update category assignments in BufferState**

In `BubbleBuffs/BufferState.cs`:
- Line 203: Change `category: Category.Consumable` to `category: Category.Buff`
- Line 245: Change `category: Category.Consumable` to `category: Category.Buff`
- All spell-based `AddBuff` calls that currently use `Category.Spell`: change to `Category.Buff`

Search for all `Category.Spell` references and update to `Category.Buff`. Search for all `Category.Item` references and update to `Category.Equipment`.

**Step 5: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors. No references to `Category.Spell`, `Category.Consumable`, or `Category.Item` remain.

**Step 6: Commit**

```bash
git add -A
git commit -m "refactor: consolidate tabs - Buffs/Abilities/Equipment"
```

---

### Task 2: Rename Buff Group Labels

**Files:**
- Modify: `BubbleBuffs/Config/en_GB.json`
- Modify: `BubbleBuffs/Config/de_DE.json`
- Modify: `BubbleBuffs/Config/fr_FR.json`
- Modify: `BubbleBuffs/Config/ru_RU.json`
- Modify: `BubbleBuffs/Config/zh_CN.json`
- Modify: `BubbleBuffs/BubbleBuffer.cs:1355-1357` (group button labels)
- Modify: `BubbleBuffs/BubbleBuffer.cs:2431-2433` (MakeSummaryLabel)

**Step 1: Update English localization**

In `en_GB.json`, update these keys:

```json
"group.normal": "Normal Buffs",
"group.important": "Important Buffs",
"group.short": "Quick Buffs",
"group.normal.tooltip.header": "Normal Buffs!",
"group.normal.tooltip.desc": "Try to cast buffs set in the buff window (Normal Buffs)",
"group.important.tooltip.header": "Important Buffs!",
"group.important.tooltip.desc": "Try to cast buffs set in the buff window (Important Buffs)",
"group.short.tooltip.header": "Quick Buffs!",
"group.short.tooltip.desc": "Try to cast buffs set in the buff window (Quick Buffs)",
"group.normal.log": "Normal Buffs!",
"group.important.log": "Important Buffs!",
"group.short.log": "Quick Buffs!",
```

**Step 2: Update German localization**

In `de_DE.json`:

```json
"group.normal": "Normale Buffs",
"group.important": "Wichtige Buffs",
"group.short": "Schnelle Buffs",
"group.normal.tooltip.header": "Normale Buffs!",
"group.normal.tooltip.desc": "Als <b>Normal<b> konfigurierte Buffs einsetzen.",
"group.important.tooltip.header": "Wichtige Buffs!",
"group.important.tooltip.desc": "Als <b>Wichtig<b> konfigurierte Buffs einsetzen.",
"group.short.tooltip.header": "Schnelle Buffs!",
"group.short.tooltip.desc": "Als <b>Schnell<b> konfigurierte Buffs einsetzen.",
"group.normal.log": "Normale Buffs!",
"group.important.log": "Wichtige Buffs!",
"group.short.log": "Schnelle Buffs!",
```

**Step 3: Update other locale files similarly** (fr_FR, ru_RU, zh_CN)

- fr_FR: "Buffs Normaux!", "Buffs Importants!", "Buffs Rapides!"
- ru_RU: Keep existing style but rename Short -> Quick equivalent
- zh_CN: Keep existing style but rename Short -> Quick equivalent

**Step 4: Rename BuffGroup enum value**

In `BubbleBuffs/BubbleBuffer.cs:2060-2064`, rename `Short` to `Quick`:

```csharp
public enum BuffGroup {
    Long,
    Quick,
    Important,
}
```

Then find-and-replace all `BuffGroup.Short` references to `BuffGroup.Quick` across the codebase. Key locations:
- `BubbleBuffer.cs:1357` — button group add
- `BubbleBuffer.cs:1933-1935` — AddButton calls
- `Config/ModSettings.cs:56` — i8 extension
- `BuffExecutor.cs` — Execute calls
- `SaveState.cs` — SavedBuffState

**Step 5: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "refactor: rename buff group labels - Normal/Quick/Important Buffs"
```

---

### Task 3: Add BuffSourceType.Equipment

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:2066-2070` (BuffSourceType enum)
- Modify: `BubbleBuffs/BubbleBuffer.cs:2078+` (SourcePriority enum — add Equipment variants)
- Modify: `BubbleBuffs/SaveState.cs` (add UseEquipment to SavedBuffState)
- Modify: `BubbleBuffs/BubbleBuff.cs` (add UseEquipment property, update SortProviders/Validate)

**Step 1: Extend BuffSourceType**

```csharp
public enum BuffSourceType {
    Spell,
    Scroll,
    Potion,
    Equipment
}
```

**Step 2: Add UseEquipment to SavedBuffState**

In `SaveState.cs`, add after `UsePotions`:

```csharp
[JsonProperty]
public bool UseEquipment = true;
```

**Step 3: Add UseEquipment to BubbleBuff**

In `BubbleBuff.cs`, add the `UseEquipment` property alongside existing `UseSpells`/`UseScrolls`/`UsePotions`. Update `Validate()` to filter by `UseEquipment` for `BuffSourceType.Equipment` providers. Update `InitialiseFromSave()` to load the new field.

**Step 4: Update SortProviders and GetSourceOrder**

In `BubbleBuff.cs`, update `GetSourceOrder()` to return a 4-element weight array including Equipment. For now, Equipment goes after Potions in all priority orderings (we can extend the priority enum later if users want fine control).

**Step 5: Add localization keys**

In `en_GB.json`:
```json
"source.equipment": "Equipment",
"use.equipment": "Use Equipment",
"log.equipment-no-charges": "is out of charges",
"log.equipment-unavailable": "no longer available",
```

In `de_DE.json`:
```json
"source.equipment": "Ausrustung",
"use.equipment": "Ausrustung verwenden",
"log.equipment-no-charges": "hat keine Ladungen mehr",
"log.equipment-unavailable": "nicht mehr verfugbar",
```

**Step 6: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 7: Commit**

```bash
git add -A
git commit -m "feat: add BuffSourceType.Equipment and UseEquipment toggle"
```

---

### Task 4: Move Source Controls Inline Above Caster Portraits

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:1005-1051` (remove source toggles from spell popout)
- Modify: `BubbleBuffs/BubbleBuffer.cs:1260-1345` (caster portrait area — add control row)
- Modify: `BubbleBuffs/BubbleBuffer.cs:1405-1421` (subscriber that updates toggle state)

**Step 1: Remove source toggles from spell popout**

In `MakeDetailsView()`, remove the creation of `useSpellsToggle`, `useScrollsToggle`, `usePotionsToggle` and `prioOverrideLabel`/`prioOverrideCycleButton` from the `spellPopout` section (lines ~1005-1051). Keep the "Ignore effects when checking overwrite" toggles.

**Step 2: Create inline control row above caster portraits**

Find the caster portraits section (around line 1260, look for `castersRect`). Before the caster portrait creation loop, create a new horizontal row:

```csharp
// Source control row above caster portraits
var sourceControlRow = new GameObject("source-controls", typeof(RectTransform));
sourceControlRow.AddTo(detailsRect);
var sourceRowRect = sourceControlRow.Rect();
sourceRowRect.SetAnchor(0.05, 0.95, 0.6, 0.7); // above caster portraits
sourceControlRow.AddComponent<HorizontalLayoutGroup>().EditComponent<HorizontalLayoutGroup>(h => {
    h.childForceExpandWidth = false;
    h.childControlWidth = false;
    h.spacing = 10;
    h.childAlignment = TextAnchor.MiddleLeft;
});

var useSpellsToggle = MakeInlineToggle(togglePrefab, sourceControlRow.transform, "use.spells".i8());
var useScrollsToggle = MakeInlineToggle(togglePrefab, sourceControlRow.transform, "use.scrolls".i8());
var usePotionsToggle = MakeInlineToggle(togglePrefab, sourceControlRow.transform, "use.potions".i8());
var useEquipmentToggle = MakeInlineToggle(togglePrefab, sourceControlRow.transform, "use.equipment".i8());
```

Where `MakeInlineToggle` is a helper similar to `MakeSpellPopoutToggle` but with smaller scale.

**Step 3: Add priority cycle button to the control row**

```csharp
var prioLabel = MakeLabel("setting-source-priority".i8() + ": " + "priority.useglobal".i8(), sourceControlRow.transform);
var prioCycleBtn = MakeButton(">", sourceControlRow.transform);
```

Wire up the same listener logic as the old `prioOverrideCycleButton`.

**Step 4: Wire toggle listeners**

Same logic as before but now referencing the inline toggles:

```csharp
useSpellsToggle.toggle.onValueChanged.AddListener(val => {
    var b = view.Selected;
    if (b != null) { b.UseSpells = val; if (b.SavedState != null) b.SavedState.UseSpells = val; state.Save(); }
});
// ... same for scrolls, potions, equipment
```

**Step 5: Update the buff selection subscriber**

In the `currentSelectedSpell.Subscribe` block (around line 1405), update the toggle visibility and state to use the new inline toggles instead of the old popout ones. Show toggles always if a blueprint exists for that source type (not just when providers exist in the queue).

This requires checking not just `buff.CasterQueue.Any(c => c.SourceType == ...)` but also whether a blueprint exists. For now, keep the existing logic (show if providers exist) and we can enhance the "always show if blueprint exists" behavior in a follow-up.

**Step 6: Hide the source control row when no buff is selected**

```csharp
sourceControlRow.SetActive(hasBuff);
```

**Step 7: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 8: Commit**

```bash
git add -A
git commit -m "feat: move source controls inline above caster portraits"
```

---

### Task 5: Add Source-Type Icon Overlays to Caster Portraits

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:2126-2148` (Portrait class — add overlay image field)
- Modify: `BubbleBuffs/BubbleBuffer.cs:312-404` (CreatePortrait — create overlay child)
- Modify: `BubbleBuffs/BubbleBuffer.cs:2494-2513` (UpdateCasterDetails — set overlay icon)

**Step 1: Add SourceOverlay field to Portrait class**

In the `Portrait` class at line 2126, add:

```csharp
public Image SourceOverlay;
```

**Step 2: Create overlay in CreatePortrait**

After the frame creation (around line 350) in `CreatePortrait`, add a small Image child for the source overlay:

```csharp
var (sourceOverlayObj, sourceOverlayRect) = UIHelpers.Create("source-overlay", pRect);
sourceOverlayRect.anchorMin = new Vector2(0.6f, 0.0f);
sourceOverlayRect.anchorMax = new Vector2(1.0f, 0.3f);
sourceOverlayRect.offsetMin = Vector2.zero;
sourceOverlayRect.offsetMax = Vector2.zero;
portrait.SourceOverlay = sourceOverlayObj.AddComponent<Image>();
portrait.SourceOverlay.preserveAspect = true;
sourceOverlayObj.SetActive(false);
```

**Step 3: Load blueprint icons for overlays**

In `TryInstallUI()` (around line 1809), load icons from known blueprints. We need to find suitable blueprint GUIDs. Add static fields:

```csharp
private static Sprite scrollOverlayIcon;
private static Sprite potionOverlayIcon;
private static Sprite equipmentOverlayIcon;
```

In `TryInstallUI()`:

```csharp
// Load source-type overlay icons from known game blueprints
if (scrollOverlayIcon == null) {
    var scrollBp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("be452dba5acdd9441bb6f45f350f1f6b"); // Scroll of Mage Armor
    if (scrollBp != null) scrollOverlayIcon = scrollBp.Icon;
}
if (potionOverlayIcon == null) {
    var potionBp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("a4093c3baac79f243b8a204e2b1e33e2"); // Potion of Cure Light Wounds
    if (potionBp != null) potionOverlayIcon = potionBp.Icon;
}
if (equipmentOverlayIcon == null) {
    var equipBp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("0e76af02588cad04a8ea5bfebdc9fb40"); // Wand of Magic Missile
    if (equipBp != null) equipmentOverlayIcon = equipBp.Icon;
}
```

Note: Blueprint GUIDs above are examples. The implementer MUST verify these GUIDs exist in the game. If not, search for valid GUIDs by iterating blueprints or checking community blueprint databases. A fallback is to use the spell ability icon from the item's `Ability.Icon` property.

**Step 4: Update UpdateCasterDetails to set overlay**

In `UpdateCasterDetails()` at line 2494, after setting the portrait image, set the overlay:

```csharp
// Set source type overlay
if (who.SourceType == BuffSourceType.Spell) {
    casterPortraits[i].SourceOverlay.gameObject.SetActive(false);
} else {
    casterPortraits[i].SourceOverlay.gameObject.SetActive(true);
    casterPortraits[i].SourceOverlay.sprite = who.SourceType switch {
        BuffSourceType.Scroll => scrollOverlayIcon,
        BuffSourceType.Potion => potionOverlayIcon,
        BuffSourceType.Equipment => equipmentOverlayIcon,
        _ => null
    };
}
```

Also remove the text-based `sourceLabel` from the portrait text (lines 2501-2505) since the icon replaces it:

```csharp
// Remove: string sourceLabel = who.SourceType switch { ... };
// The text now just shows credits and book name, no [Scroll]/[Potion] text
if (who.AvailableCredits < 100)
    casterPortraits[i].Text.text = $"{who.spent}+{who.AvailableCredits}\n<i>{bookName}</i>";
else
    casterPortraits[i].Text.text = $"{"available.atwill".i8()}\n<i>{bookName}</i>";
```

**Step 5: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add source-type icon overlays to caster portraits"
```

---

### Task 6: Add Game Icons to Tab Buttons

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:765-793` (ButtonGroup class — add icon support)
- Modify: `BubbleBuffs/BubbleBuffer.cs:835-841` (MakeFilters — pass icons)
- Modify: `BubbleBuffs/BubbleBuffer.cs:1792+` (TryInstallUI — load tab icons)

**Step 1: Add icon overload to ButtonGroup.Add**

In the `ButtonGroup<T>` class at line 778, add an overloaded `Add` method:

```csharp
public void Add(T value, string title, Sprite icon) {
    var button = MakeButton(title, content);

    // Add icon image before text
    if (icon != null) {
        var iconObj = new GameObject("tab-icon", typeof(RectTransform));
        iconObj.transform.SetParent(button.transform, false);
        iconObj.transform.SetAsFirstSibling();
        var img = iconObj.AddComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        var le = iconObj.AddComponent<LayoutElement>();
        le.preferredWidth = 24;
        le.preferredHeight = 24;
    }

    var selection = GameObject.Instantiate(selectedPrefab, button.transform);
    selection.SetActive(false);

    Selected.Subscribe<T>(s => {
        selection.SetActive(EqualityComparer<T>.Default.Equals(s, value));
    });
    button.GetComponentInChildren<OwlcatButton>().Interactable = true;
    button.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
        Selected.Value = value;
    });
}
```

**Step 2: Load tab icons in TryInstallUI**

Add static sprite fields and load them from known blueprints:

```csharp
private static Sprite tabBuffsIcon;
private static Sprite tabEquipmentIcon;
private static Sprite tabAbilitiesIcon;
```

In `TryInstallUI()`:

```csharp
if (tabBuffsIcon == null) {
    var bp = Resources.GetBlueprint<BlueprintAbility>("9e1ad5d6f87d19e4d8c094b114ab2f51"); // Mage Armor
    if (bp != null) tabBuffsIcon = bp.Icon;
}
if (tabEquipmentIcon == null) {
    var bp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("0e76af02588cad04a8ea5bfebdc9fb40"); // Wand of Magic Missile
    if (bp != null) tabEquipmentIcon = bp.Icon;
}
if (tabAbilitiesIcon == null) {
    var bp = Resources.GetBlueprint<BlueprintAbility>("7bb9eb2042e67bf489c4a7ba8232c6e0"); // Smite Evil
    if (bp != null) tabAbilitiesIcon = bp.Icon;
}
```

Note: Blueprint GUIDs MUST be verified during implementation. The implementer should check community resources or iterate blueprints in-game to find valid GUIDs. The feature should gracefully handle null icons (just show text without icon).

**Step 3: Pass icons in MakeFilters**

Update the tab creation calls:

```csharp
CurrentCategory.Add(Category.Buff, "cat.buffs".i8(), tabBuffsIcon);
CurrentCategory.Add(Category.Ability, "cat.Abilities".i8(), tabAbilitiesIcon);
CurrentCategory.Add(Category.Equipment, "cat.Equipment".i8(), tabEquipmentIcon);
```

**Step 4: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 5: Commit**

```bash
git add -A
git commit -m "feat: add game-blueprint icons to tab buttons"
```

---

### Task 7: Implement Equipment Inventory Scanning

**Files:**
- Modify: `BubbleBuffs/BufferState.cs:165-254` (inventory scanning block — add equipment scanning)
- Modify: `BubbleBuffs/BubbleBuff.cs` (handle Equipment in Validate, feature flag guards)
- Modify: `BubbleBuffs/BuffExecutor.cs` (handle Equipment in CastTask creation, feature flags)
- Modify: `BubbleBuffs/Handlers/EngineCastingHandler.cs` (handle Equipment source in casting)

**Step 1: Add equipment scanning in RecalculateAvailableBuffs**

After the scroll/potion scanning block in `BufferState.cs` (after line 254), add:

```csharp
try {
    // Scan quickslot items for activatable equipment buffs
    for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
        UnitEntityData dude = Group[characterIndex];

        // Check quickslot items
        foreach (var slot in dude.Body.QuickSlots) {
            if (slot.HasItem && slot.Item.Blueprint is BlueprintItemEquipmentUsable usableBp) {
                var spellBlueprint = usableBp.Ability;
                if (spellBlueprint == null) continue;

                // Check if this item grants a beneficial buff
                // Get charges - use item's current charges
                var itemEntity = slot.Item;
                int charges = itemEntity.Charges;
                if (charges <= 0) continue;

                var credits = new ReactiveProperty<int>(charges);
                var abilityData = new AbilityData(spellBlueprint, dude);

                Main.Verbose($"      Adding equipment buff: {spellBlueprint.Name} from {usableBp.Name} for {dude.CharacterName}", "state");

                AddBuff(dude: dude,
                        book: null,
                        spell: abilityData,
                        baseSpell: null,
                        credits: credits,
                        newCredit: true,
                        creditClamp: int.MaxValue,
                        charIndex: characterIndex,
                        archmageArmor: false,
                        category: Category.Equipment,
                        sourceType: BuffSourceType.Equipment,
                        sourceItem: itemEntity);
            }
        }
    }
} catch (Exception ex) {
    Main.Error(ex, "finding equipment buffs");
}
```

Note: The implementer must verify that `ItemEntity.Charges` is the correct property name. If not, check `ItemEntityUsable.Charges` or similar. Also verify `dude.Body.QuickSlots` is the correct API for accessing quickslot items — it may be `dude.Body.QuickSlots` or require iteration over `dude.Body.EquipmentSlots`.

**Step 2: Handle Equipment in BuffExecutor**

In `BuffExecutor.cs`, ensure Equipment source type gets same treatment as Scroll/Potion:
- Feature flags (PowerfulChange, ShareTransmutation, AzataZippyMagic) forced false for Equipment
- No UMD check needed for Equipment

**Step 3: Handle Equipment in EngineCastingHandler**

In `EngineCastingHandler.cs`:
- Equipment sources skip `SetAllRetentions()` and `ModifyCasterLevel()` (same as Scroll/Potion)
- After cast: charge is deducted by the game automatically (no manual `Inventory.Remove`)
- If charges are 0 after cast, log warning

**Step 4: Add log messages for equipment**

In the casting handler, after successful equipment cast:

```csharp
if (castTask.SourceType == BuffSourceType.Equipment && castTask.SourceItem != null) {
    // Check remaining charges
    if (castTask.SourceItem.Charges <= 0) {
        var msg = $"{castTask.SourceItem.Name} {"log.equipment-no-charges".i8()}";
        // Log the warning
    }
}
```

**Step 5: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: implement equipment inventory scanning and casting"
```

---

### Task 8: Update Settings Panel for Equipment Toggle

**Files:**
- Modify: `BubbleBuffs/BubbleBuffer.cs:625-643` (MakeSettings — add equipment toggle)
- Modify: `BubbleBuffs/SaveState.cs` (add EquipmentEnabled to SavedBufferState)

**Step 1: Add EquipmentEnabled to SavedBufferState**

In `SaveState.cs`, add after `PotionsEnabled`:

```csharp
[JsonProperty]
public bool EquipmentEnabled = true;
```

**Step 2: Add settings toggle**

In `MakeSettings()`, after the potions toggle (around line 643), add:

```csharp
{
    var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-equipment-enabled".i8());
    toggle.isOn = state.SavedState.EquipmentEnabled;
    toggle.onValueChanged.AddListener(enabled => {
        state.SavedState.EquipmentEnabled = enabled;
        state.InputDirty = true;
        state.Save(true);
    });
}
```

**Step 3: Add localization key**

In `en_GB.json`:
```json
"setting-equipment-enabled": "Enable equipment usage",
```

In `de_DE.json`:
```json
"setting-equipment-enabled": "Ausrustungsnutzung aktivieren",
```

**Step 4: Gate equipment scanning**

In `BufferState.cs`, wrap the equipment scanning block (from Task 7) with:

```csharp
if (SavedState.EquipmentEnabled) {
    // ... equipment scanning code
}
```

**Step 5: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "feat: add equipment enable/disable setting"
```

---

### Task 9: Add Missing Inventory Log Messages

**Files:**
- Modify: `BubbleBuffs/BuffExecutor.cs` (add log messages for missing items)
- Modify: `BubbleBuffs/Config/en_GB.json`
- Modify: `BubbleBuffs/Config/de_DE.json`

**Step 1: Add localization keys**

In `en_GB.json`:
```json
"log.no-scroll-available": "No scroll of {0} in inventory",
"log.no-potion-available": "No potion of {0} in inventory",
```

In `de_DE.json`:
```json
"log.no-scroll-available": "Keine Schriftrolle von {0} im Inventar",
"log.no-potion-available": "Kein Trank von {0} im Inventar",
```

**Step 2: Add log messages in BuffExecutor**

When a provider is selected but the item count is 0, log a warning before skipping to the next provider:

```csharp
if (caster.SourceType == BuffSourceType.Scroll && caster.AvailableCredits <= 0) {
    var msg = string.Format("log.no-scroll-available".i8(), buff.Name);
    // Add to combat log
    continue; // skip to next provider
}
if (caster.SourceType == BuffSourceType.Potion && caster.AvailableCredits <= 0) {
    var msg = string.Format("log.no-potion-available".i8(), buff.Name);
    // Add to combat log
    continue;
}
if (caster.SourceType == BuffSourceType.Equipment && caster.AvailableCredits <= 0) {
    var msg = string.Format("{0} {1}", caster.SourceItem?.Name ?? buff.Name, "log.equipment-no-charges".i8());
    // Add to combat log
    continue;
}
```

**Step 3: Build and verify**

Run: `dotnet build BubbleBuffs/BubbleBuffs.csproj`
Expected: 0 errors.

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add log messages for missing inventory items"
```

---

### Task 10: Build, Deploy, and Verify

**Step 1: Full build**

```bash
dotnet build BubbleBuffs/BubbleBuffs.csproj
```

Expected: 0 errors.

**Step 2: Deploy to Steam Deck**

```bash
scp BubbleBuffs/bin/Debug/BubbleBuffs.dll "deck-direct:/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/BubbleBuffs/BubbleBuffs.dll"
```

**Step 3: Verify deployment**

```bash
ssh deck-direct "ls -la '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/BubbleBuffs/BubbleBuffs.dll'"
```

**Step 4: Commit any final changes**

```bash
git add -A
git commit -m "chore: final build and deploy"
```

---

## Testing Checklist (Manual, In-Game)

After deploying, test these scenarios:

1. **Tabs:** Only 3 tabs visible: Buffs, Abilities, Equipment
2. **Buffs tab:** Shows all spells, scrolls, and potions merged. No duplicate entries.
3. **Source controls:** When selecting a buff, checkboxes for Spells/Scrolls/Potions/Equipment visible above caster portraits
4. **Priority cycle:** Clicking ">" cycles through priority options
5. **Source overlays:** Caster portraits show small icon overlay for scroll/potion/equipment sources
6. **Tab icons:** Each tab has a small game icon next to the text
7. **Labels:** Summary cards show "Normal Buffs", "Quick Buffs", "Important Buffs"
8. **Log messages:** Casting logs show "Normal Buffs!", "Quick Buffs!", "Important Buffs!"
9. **Equipment:** Quickslot items with buff effects appear in Equipment tab
10. **Equipment casting:** Activating an equipment buff uses a charge
11. **Settings:** Equipment enable/disable toggle works
12. **Missing items:** Log shows warning when scroll/potion enabled but not in inventory
