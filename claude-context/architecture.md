# Architecture Reference

Load when: first time in this codebase, or when you need to understand the big picture (lifecycle, data flow, class roles, save format).

## Architecture

### Mod Lifecycle

- **`Main.cs`** — Entry point via UnityModManager. `Load()` initializes Harmony patches, localization, asset bundles, and installs `GlobalBubbleBuffer`.
- **`GlobalBubbleBuffer`** (in `BubbleBuffer.cs`) — Singleton managing the entire mod. `Install()` subscribes to EventBus. `TryInstallUI()` builds the HUD buttons and wires up the spellbook controller.

### Core Data Flow

```
BufferState.RecalculateAvailableBuffs()  →  scans party spellbooks/inventory
    → creates BubbleBuff entries (one per unique spell+metamagic combo)
    → each BubbleBuff has a CasterQueue of BuffProviders (who can cast it)

BuffExecutor.Execute(BuffGroup)  →  iterates matching buffs
    → checks existing buffs, slot availability, UMD, arcanist pool
    → creates CastTask list
    → hands off to BubbleBuffGlobalController.CastSpells()

EngineCastingHandler  →  handles the actual spell casting via game's ability system
    → applies Powerful Change, Share Transmutation, Reservoir CL buffs
    → manages spell slot consumption and item charges

BuffExecutor.ExecuteCombatStart()  →  triggered by EventBus on combat enter
    → activates activatables (songs, judgments) directly via IsOn/TryStart
    → casts spell-based buffs via AnimatedExecutionEngine or InstantExecutionEngine
    → engine choice controlled by SkipAnimationsOnCombatStart setting
```

### Key Classes

| Class | File | Purpose |
|---|---|---|
| `GlobalBubbleBuffer` | `BubbleBuffer.cs` | Singleton, UI installation, HUD buttons, EventBus subscriber |
| `BubbleBuffSpellbookController` | `BubbleBuffer.cs` | MonoBehaviour on spellbook screen, manages buff window lifecycle, save/load |
| `BufferState` | `BufferState.cs` | Scans party for available buffs, manages buff list, recalculation |
| `BubbleBuff` | `BubbleBuff.cs` | Single buff entry with wanted/given targets, caster queue, source priorities |
| `BuffExecutor` | `BuffExecutor.cs` | Executes buff casting for a BuffGroup, creates CastTasks |
| `EngineCastingHandler` | `Handlers/EngineCastingHandler.cs` | Handles actual spell casting through game's ability system |
| `SavedBufferState` / `SavedBuffState` | `SaveState.cs` | JSON-serialized per-save configuration |
| `ShortcutBinding` | `ShortcutBinding.cs` | Readonly struct for keyboard shortcuts with modifier keys (Ctrl/Shift/Alt) + backward-compatible JSON converter |
| `BubbleBuffGlobalController` | `BuffExecutor.cs` | MonoBehaviour handling shortcut capture/execution and spell casting coroutines |
| `AnimatedExecutionEngine` | `AnimatedExecutionEngine.cs` | Cast spells with full game animations via `UnitUseAbility` commands |
| `InstantExecutionEngine` | `InstantExecutionEngine.cs` | Cast spells instantly via `Rulebook.Trigger` in batches of 8 |

### UI Structure

`BubbleBuffer.cs` (~3700 lines) contains most UI code. Key patterns:

- **HUD buttons**: Created in `TryInstallUI()` via local `AddButton()` function. Uses `ButtonSprites.Load("name", size)` which loads `Assets/icons/{name}_normal.png`, `_hover.png`, `_down.png`.
- **Buff window**: Created by `BubbleBuffSpellbookController.CreateWindow()`. Uses `ButtonGroup<T>` for tab groups, `Portrait` class for caster portraits.
- **UI hierarchy access**: `UIHelpers.StaticRoot` → `Game.Instance.UI.Canvas.transform`. `UIHelpers.SpellbookScreen` finds the spellbook via version-specific `WidgetPaths`.
- **Game version compat**: `WidgetPaths_1_0` through `WidgetPaths_2_0` in `UIHelpers.cs` handle different UI paths across game versions.
- **Overlay UI elements must share the same parent**: The gear/settings button is on the window root (`content` in `CreateWindow`). Any UI that should align with it (e.g., group checkboxes) must also be parented to the window root (`content.parent` inside `MakeDetailsView`), not to flow-layout sections like `actionBarSection`. Different parents = different coordinate systems.

### Enums

- **`BuffGroup`**: `Long`, `Important`, `Quick` — the three buff categories users assign buffs to
- **`Category`**: `Buff`, `Ability`, `Equipment`, `Song` — tab filter categories
- **`BuffSourceType`**: `Spell`, `Scroll`, `Potion`, `Equipment`, `Song` — how a buff can be provided
- **`SourcePriority`**: Only permutes Spell/Scroll/Potion order. Equipment always sorts last in `GetSourceOrder()` (intentional — equipment items are valuable, used as fallback).

### Localization

JSON files in `Config/` (en_GB, de_DE, fr_FR, ru_RU, zh_CN) are embedded resources. Access via `"key".i8()` extension method. English (`en_GB.json`) is the fallback. When adding new UI text, add keys to `en_GB.json` and `de_DE.json` only — the other locales are incomplete and fall back to EN automatically.
- **Locale files vary in completeness**: `en_GB` and `de_DE` have all keys. `fr_FR`, `ru_RU`, `zh_CN` are shorter and missing many keys added after the initial fork. Don't assume same line numbers or key presence across locales.
- **Technical UI terms stay English in de_DE**: Gaming/UI terms like "shortcut", "buff group" names (Normal, Quick, Important) should not be translated into German — English reads more naturally in this context.

### Asset Loading

- **Mod sprites**: `AssetLoader.LoadInternal("icons", "file.png", Vector2Int)` loads from `Assets/icons/`
- **Game blueprints**: `Resources.GetBlueprint<T>(guid)` loads game data (spells, items, features)
- **Asset bundles**: `AssetLoader.AddBundle("bundlename")` loads Unity asset bundles from mod directory

### Item Types (Equipment Scan)

- **`UsableItemType.Wand`**: In player inventory (not QuickSlots). Has `Charges` property. Uses `CanUseItemWithUmd()` shared helper (class list check + UMD fallback). DC = 20 + CasterLevel.
- **`CanUseItemWithUmd(dude, spell, dc)`**: Shared method in `BufferState.cs` for scroll/wand eligibility. Checks class spell list first, then UMD based on `SavedState.UmdMode`. Reuse for any new item types needing UMD.
- **`UsableItemType.Scroll/Potion`**: In player inventory. Stack count = credits. Consumed via `Inventory.Remove()`.
- **QuickSlot items** (`dude.Body.QuickSlots`): Equipment like rods, special items. Many have `Ability = null` (metamagic rods). Charges consumed via `SourceItem.Charges--`.
- **Equipped item abilities** (`dude.Abilities.RawFacts` with `SourceItem != null`): Staves and worn items. Filtered by `!(Blueprint is BlueprintItemEquipmentUsable)` to avoid double-counting QuickSlot items.
- **`Kingmaker.Items.ItemStatHelper`**: Canonical game-side accessor for per-item stats. `GetCasterLevel(ItemEntity)` / `GetSpellLevel(ItemEntity)` / `GetDC(ItemEntity)` check `CraftedItemPart` first, fall back to blueprint, and (for `GetDC`) apply ScrollMastery/WandMastery/EldritchWandMastery feats. Mirror this pattern for any runtime item-stat read — reading `blueprint.X` alone misses crafted overrides AND mastery feats. Mastery-feat DC handling is currently not replicated in the mod's scroll/wand DC (`20 + effectiveCL`); may need attention if players report UMD checks being too hard with mastery feats active.

### ActivatableAbility API (Songs/Performances)

- **Scanning**: `dude.ActivatableAbilities.RawFacts` iterates all activatable abilities. Filter by `blueprint.Group` — `BardicPerformance` (1) for Bard/Skald, `AzataMythicPerformance` (28) for Azata.
- **Activation**: `activatable.IsOn = true` (calls `SetIsOn(true, null)` internally). Pre-check: `!activatable.IsOn && activatable.IsAvailable`.
- **Resource check**: `activatable.IsAvailable` (combined), `activatable.IsAvailableByResources`, `activatable.ResourceCount` (remaining rounds).
- **Mutual exclusivity**: Bard and Azata use separate `ActivatableAbilityGroup` values — they CAN run simultaneously. Within each group, only one per character.
- **Tooltip**: Use `new TooltipTemplateActivatableAbility(activatable)` from `Kingmaker.UI.MVVM._VM.Tooltip.Templates` — the game's native tooltip for activatable abilities.
- **Songs bypass `AddBuff()`**: `GetBeneficialBuffs()` requires `AbilityEffectRunAction` which activatable abilities don't have. Use dedicated `AddSong()` method that constructs `BubbleBuff` directly.

### Pet/Companion API

- **Pet access**: `unit.Get<UnitPartPetMaster>()?.Pets` → `List<EntityPartRef<UnitEntityData, UnitPartPet>>`. Each ref: `.Entity` (UnitEntityData), `.EntityPart` (UnitPartPet with `.Type`, `.Master`). Namespace: `Kingmaker.UnitLogic.Parts`.
- **`Bubble.Group`** includes pets via `Bubble.RefreshGroup()` — cached field rebuilt from `ActualGroup` + all pets. Single source of truth for scanning, targeting, portraits, save/load.
- **Portrait array `view.targets` is fixed at window creation**: Any loop iterating `Bubble.Group.Count` and indexing `targets[]` needs `&& i < targets.Length` bounds guard.

### Save System

Per-save JSON at `{ModPath}UserSettings/bi2tl-{GameId}.json`. Contains buff assignments, caster priorities, source preferences, and global settings. Serialized via Newtonsoft.Json.
