# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BubbleBuffs is a Unity mod for **Pathfinder: Wrath of the Righteous** that adds automated buff casting routines to the spellbook UI. Players configure which buffs to cast on which party members, then execute them with HUD buttons. Built with C#/.NET Framework 4.81, Harmony patches, and Unity UI. Distributed via [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195).

## Build

```bash
~/.dotnet/dotnet build BubbleBuffs/BubbleBuffs.csproj -p:SolutionDir=$(pwd)/
```

> **Note:** `dotnet` is not on PATH — always use `~/.dotnet/dotnet`.

**Setup:** The build requires the game's managed DLLs. The csproj references them via `$(WrathInstallDir)/Wrath_Data/Managed/`. This is resolved in order:
1. `GamePath.props` in repo root (auto-generated or manual)
2. Auto-detection from `Player.log` (Windows only, uses `findstr`)

For Linux dev, create `GamePath.props` manually or symlink game DLLs:
```xml
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
  <PropertyGroup>
    <WrathInstallDir>/path/to/game/or/symlink</WrathInstallDir>
  </PropertyGroup>
</Project>
```

The build uses `BepInEx.AssemblyPublicizer.MSBuild` to access private/internal game fields (marked with `Publicize="true"` in csproj). Publicized DLLs go to `obj/Debug/publicized/`.

Output: `BubbleBuffs/bin/Debug/BubbleBuffs.dll` + assets copied to output dir. The build target also creates a zip for distribution.

## Gotchas

- **`-p:SolutionDir` required on Linux**: Without it, `GamePath.props` import fails silently and all 1400+ DLL references break. Always pass it.
- **`findstr` warnings**: The csproj auto-detection target uses Windows `findstr`. On Linux this produces a harmless warning — ignore it.
- **WidgetPaths version selection**: `Main.Load()` selects a `WidgetPaths` class based on `gameVersion.Major/Minor`. If the game updates, UI element paths may break. Check `UIHelpers.cs` for the hierarchy.
- **EnhancedInventory interop**: Mod loads after `EnhancedInventory` (see `Info.json`). `TryFixEILayout()` adjusts UI positioning when EI is present.
- **Publicizer scope**: Only DLLs with `Publicize="true"` in csproj have private fields accessible. If you get CS0122 on a game field, check whether the source DLL is publicized.

## Debug Keybinds (DEBUG builds only)

- **Shift+I** — Reinstall UI + recalculate buffs (hot-reload during development)
- **Shift+B** — Reload the entire mod
- **Shift+R** — Debug helper (currently adds a test item)

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

### UI Structure

`BubbleBuffer.cs` (~2800 lines) contains most UI code. Key patterns:

- **HUD buttons**: Created in `TryInstallUI()` via local `AddButton()` function. Uses `ButtonSprites.Load("name", size)` which loads `Assets/icons/{name}_normal.png`, `_hover.png`, `_down.png`.
- **Buff window**: Created by `BubbleBuffSpellbookController.CreateWindow()`. Uses `ButtonGroup<T>` for tab groups, `Portrait` class for caster portraits.
- **UI hierarchy access**: `UIHelpers.StaticRoot` → `Game.Instance.UI.Canvas.transform`. `UIHelpers.SpellbookScreen` finds the spellbook via version-specific `WidgetPaths`.
- **Game version compat**: `WidgetPaths_1_0` through `WidgetPaths_2_0` in `UIHelpers.cs` handle different UI paths across game versions.

### Enums

- **`BuffGroup`**: `Long`, `Important`, `Quick` — the three buff categories users assign buffs to
- **`Category`**: `Buff`, `Ability`, `Equipment` — tab filter categories
- **`BuffSourceType`**: `Spell`, `Scroll`, `Potion`, `Equipment` — how a buff can be provided

### Localization

JSON files in `Config/` (en_GB, de_DE, fr_FR, ru_RU, zh_CN) are embedded resources. Access via `"key".i8()` extension method. English (`en_GB.json`) is the fallback. When adding new UI text, add keys to all locale files.

### Asset Loading

- **Mod sprites**: `AssetLoader.LoadInternal("icons", "file.png", Vector2Int)` loads from `Assets/icons/`
- **Game blueprints**: `Resources.GetBlueprint<T>(guid)` loads game data (spells, items, features)
- **Asset bundles**: `AssetLoader.AddBundle("bundlename")` loads Unity asset bundles from mod directory

### Item Types (Equipment Scan)

- **`UsableItemType.Wand`**: In player inventory (not QuickSlots). Has `Charges` property. Use UMD logic like scrolls (class list check + UMD fallback). DC = 20 + CasterLevel.
- **`UsableItemType.Scroll/Potion`**: In player inventory. Stack count = credits. Consumed via `Inventory.Remove()`.
- **QuickSlot items** (`dude.Body.QuickSlots`): Equipment like rods, special items. Many have `Ability = null` (metamagic rods). Charges consumed via `SourceItem.Charges--`.
- **Equipped item abilities** (`dude.Abilities.RawFacts` with `SourceItem != null`): Staves and worn items. Filtered by `!(Blueprint is BlueprintItemEquipmentUsable)` to avoid double-counting QuickSlot items.

### Save System

Per-save JSON at `{ModPath}UserSettings/bubblebuff-{GameId}.json`. Contains buff assignments, caster priorities, source preferences, and global settings. Serialized via Newtonsoft.Json.

## Credit System (Buff Availability)

- **`BubbleBuff.Validate()`** builds `ActualCastQueue` AND consumes credits via `ChargeCredits()`. By the time `BuffExecutor.Execute()` runs, `credits.Value` is already decremented. Do NOT re-check `AvailableCredits` in Execute — it will always be 0 for single-charge items.
- **`AddBuff()` merges providers by `BuffKey`**: If the same spell exists from multiple sources (spellbook + wand + scroll), they share one `BubbleBuff` entry. The `Category` is set only on first creation — later providers inherit the existing category. Wand spells that already exist as regular buffs won't appear in the Equipment tab.

## Unity UI Layout Patterns

- **`MakeButton()` breaks layout groups**: Sets point-anchors `(0.5, 0.5)` → zero size in HLG/VLG. Always reset anchors to stretch `(0,0)→(1,1)` after calling `MakeButton()`.
- **`childControlHeight=true` + `childForceExpandHeight=false`**: Correct combo for LayoutElement-driven sizing. `childControl=false` ignores LayoutElement entirely (children collapse to RectTransform default = 0). `childForceExpand=true` stretches beyond preferred size.
- **`buttonPrefab` is designed for full-width text buttons**: Has internal Image/decoration layers that look broken at small sizes (<60px). Don't use as icon buttons.
- **`buttonPrefab` minimum height ~38px**: Below this threshold, internal decoration layers become invisible/transparent. Always set `preferredHeight >= 38` on rows containing buttonPrefab instances.
- **`layoutPriority` on LayoutElement**: Higher priority means the parent LayoutGroup uses THIS element's preferred/flexible values instead of calculating from children. Use `layoutPriority = 3` on row LayoutElements to override buttonPrefab's internal preferred sizes.
- **Anchor-based children inside VLG sections**: VLG with `childControlWidth/Height = true` controls the section's RectTransform. Anchor-based grandchildren position relative to the section rect — this works correctly. But inner LayoutGroups can fight with anchors; prefer one approach per container.
- **`UIHelpers.Create()` / `AddTo()`**: Uses `SetParent(parent)` without `false` — can cause positioning bugs. Use `SetParent(parent, false)` when positioning matters.

## Code Style

- K&R brace style (opening brace on same line): `csharp_new_line_before_open_brace = none`
- 4-space indentation
- `var` when type is apparent, explicit type otherwise
- Game's private fields accessed via publicizer (e.g., `PartyView.m_Hide`, `button.m_CommonLayer`)
