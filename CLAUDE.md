# Buff It 2 The Limit

## Overview

Buff It 2 The Limit (formerly BubbleBuffs) is a Unity mod for **Pathfinder: Wrath of the Righteous** that adds automated buff casting routines to the spellbook UI. Players configure which buffs to cast on which party members, then execute them with HUD buttons. Built with C#/.NET Framework 4.81, Harmony patches, and Unity UI. Distributed via [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948).

## Build

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
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

Output: `BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll` + assets copied to output dir. The build target also creates a zip for distribution.

**Release build** (for distribution — excludes debug keybinds):
```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo
```

## Deploy

```bash
./deploy.sh
```

Builds and SCPs `BuffIt2TheLimit.dll` + `Info.json` to Steam Deck mod directory. Requires `deck-direct` SSH alias. Always deploy both — UMM reads the version from `Info.json`, not the DLL.

## Versioning

Version must be updated in **three** files simultaneously:
1. `BuffIt2TheLimit/BuffIt2TheLimit.csproj` — `<Version>` (controls ZIP filename)
2. `BuffIt2TheLimit/Info.json` — `"Version"` (UMM reads this)
3. `Repository.json` — `"Version"` + `"DownloadUrl"` (UMM auto-update)

Use `/release` skill to handle this automatically.

## Release

Use `/release minor|patch|major` — the skill handles version bump, build, tag, push, and GitHub release. Nexus Mods upload is automated via GitHub Action on release publish. See `.claude/commands/release.md`.
- **Release notes in English** — even though user communicates in German, all release notes (GitHub + Nexus) must be in English.

## Gotchas

- **`-p:SolutionDir` required on Linux**: Without it, `GamePath.props` import fails silently and all 1400+ DLL references break. Always pass it.
- **`findstr` warnings**: The csproj auto-detection target uses Windows `findstr`. On Linux this produces a harmless warning — ignore it.
- **Upstream PRs often forked from old versions**: The upstream repo (factubsio/BubbleBuffs) is inactive. External PRs target `fork` (Gh05d) and are typically based on pre-fork commits, missing our additions (mass spell logic, extend rod, etc.). Rebase onto master before reviewing — this resolves most "missing feature" issues. After rebase+merge, GitHub won't auto-close the PR (different SHAs) — close manually with `gh pr close`.
- **`.NET Framework 4.8.1` missing APIs**: `Dictionary.GetValueOrDefault()` doesn't exist. Use `TryGetValue` instead. Other missing APIs: `Index`/`Range` syntax, `IAsyncEnumerable`, `Span<T>` in many contexts.
- **Newtonsoft.Json version is old (game-bundled)**: No generic `JsonConverter<T>`. Use non-generic `JsonConverter` base class with `CanConvert(Type)` override instead.
- **`[JsonConverter]` on collections applies to the collection, not elements**: To serialize `HashSet<SomeEnum>` as string names, use `[JsonProperty(ItemConverterType = typeof(StringEnumConverter))]`, NOT `[JsonConverter(typeof(StringEnumConverter))]` — the latter tries to convert the entire HashSet as an enum and crashes at runtime.
- **WidgetPaths version selection**: `Main.Load()` selects a `WidgetPaths` class based on `gameVersion.Major/Minor`. If the game updates, UI element paths may break. Check `UIHelpers.cs` for the hierarchy.
- **EnhancedInventory interop**: Mod loads after `EnhancedInventory` (see `Info.json`). `TryFixEILayout()` adjusts UI positioning when EI is present.
- **Publicizer scope**: Only DLLs with `Publicize="true"` in csproj have private fields accessible. If you get CS0122 on a game field, check whether the source DLL is publicized.
- **`Main.Log()` vs `Main.Verbose()`**: `Main.Log()` fires unconditionally, `Main.Verbose()` respects debug flags. Use `Verbose` for per-item scan output. `Log` for important one-time messages only. Diagnostic logging with `Main.Log` in hot paths (RecalculateAvailableBuffs) causes unnecessary inventory iterations every recalculation.
- **Worktrees need `GamePath.props` + `GameInstall/`**: Both are gitignored. Copy `GamePath.props` from main repo and symlink `GameInstall/` into the worktree, otherwise build fails with 1400+ errors.
- **`MetamagicData.MetamagicMask` has private set**: Use `Add(Metamagic)` to set flags, `Clear()` to reset. Default constructor is parameterless `new MetamagicData()`. No parameterized constructors exist.
- **`sourceControlObj` visibility**: Controlled by `sourceCount > 1 || hasSpellProviders` in `UpdateDetailsView`. Hidden entirely for songs (sourceCount=0, no spell providers) and equipment-only buffs. Any UI control that must be visible for ALL buff types (e.g., combat start checkbox) must be parented outside `sourceControlObj` — use `spellInfoSection` with `ignoreLayout = true` instead.
- **Nexus Mods upload**: Automated via GitHub Action (`.github/workflows/nexus-upload.yml`) on release publish. No manual upload needed. The `/release` skill handles the full flow.
- **Nexus Mods changelogs**: Use plain text, not BBCode — the release/change notes field is a simple table.
- **Nexus Mods description**: Uses BBCode formatting (`[b]`, `[size]`, `[url]`), not Markdown.
- **Nexus Mods version display**: The mod-page version is a separate field NOT updated by file uploads. Must be updated manually on the Nexus Edit page after each release. Tracked in [Nexus-Mods/upload-action#11](https://github.com/Nexus-Mods/upload-action/issues/11).
- **`BuffProvider.SelfCastOnly` is a computed property** (not a settable field): Returns `true` for `SourceType == Potion` or `spell.TargetAnchor == Owner`. To change self-only logic, modify the property getter in `BubbleBuff.cs`, don't try to set it.
- **Caster portrait index ≠ CasterQueue index**: `BufferView.casterPortraitMap` maps portrait indices to CasterQueue indices after deduplication. Always use the map when translating portrait clicks to CasterQueue entries.
- **`BubbleBuff.SavedState` is never assigned**: The `SavedState` field on `BubbleBuff` is always null at runtime. The save system reads/writes via `BufferState.Save()` which copies from `buff.UseSpells`/etc fields directly. UI code must read from `buff.UseSpells`, NOT `buff.SavedState?.UseSpells` (which always falls back to default). Toggle handlers should write to `buff.UseSpells` directly.
- **`BubbleBuff.Spell` is null for songs**: Songs use `ActivatableSource` instead. All code paths touching `Spell`, `Spell.Blueprint`, `Spell.Name`, `Spell.IsMetamagicked()` etc. must null-guard or check `IsSong` first. Key locations: `BindBuffToView`, `BuffProvider.CanTarget`, `UpdateDetailsView`, `MetaMagicFlags`, `Name`/`NameMeta` properties.
- **`BuffProvider.CanTarget` crashes when `spell` is null**: Songs and any future source type with `spell = null` must early-return from `CanTarget()`. The `SelfCastOnly` property also accesses `spell.TargetAnchor` — guard with `SourceType` check before accessing `spell`.
- **`ilspycmd` stack overflows on large classes** (e.g., `UnitEntityData`). Use smaller part classes instead (e.g., `UnitPartPetMaster`). Publicized DLLs at `BuffIt2TheLimit/obj/Debug/publicized/Assembly-CSharp.dll` work better than originals. IL mode (`-il` flag) avoids some type-resolution stack overflows that C# decompilation hits.
- **`docs/` directory is gitignored**: Use `git add -f` when committing spec/plan files.
- **`RuleCastSpell` clones `AbilityParams` in constructor**: `Context = spell.CreateExecutionContext(target)` → `m_Params = @params.Clone()` runs BEFORE `OnBeforeEventAboutToTrigger`. Modifying `SpellToCast.MetamagicData` in the handler alone won't affect duration/DC calculations — must also set `Context.m_Params.Metamagic`.
- **Blueprint action tree extraction**: To check what actions a spell uses: `ssh deck-direct "unzip -p '<game-path>/blueprints.zip' '<blueprint-path>.jbp'"` piped through `python3 -m json.tool`. Parse `Components[0].Actions.Actions` recursively for the action types. The `blueprints.zip` is at the game install root, not in subdirectories.
- **Weapon enchantment spells use `EnhanceWeapon` action**: Magic Weapon, Keen Edge, Magic Fang and similar spells all use `Conditional` → `EnhanceWeapon` + `ContextActionApplyBuff`. Handled by `EnhanceWeaponEffect` in `IBeneficialEffect.cs`. If a new weapon enchantment spell is reported missing, check for this pattern first.
- **`UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData**: Equipment/scroll/potion items use `new AbilityData(blueprint, caster)` which isn't in the caster's ability list. The game's command system silently rejects it. `AnimatedExecutionEngine` falls back to `Rulebook.Trigger` for non-spell CastTasks. `InstantExecutionEngine` always uses `Rulebook.Trigger` so it works for all source types.
- **Verify deploys by comparing DLL timestamps**: After `./deploy.sh`, compare local `BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll` vs `ssh deck-direct "ls -la '<game-path>/Mods/BuffIt2TheLimit/BuffIt2TheLimit.dll'"`. File size and date must match. Game must be restarted to load the new DLL.
- **Debug Unity UI positions with `GetWorldCorners()`**: When UI elements appear mispositioned, add `Main.Log` calls with `RectTransform.GetWorldCorners()` to compare actual pixel positions at runtime. Anchor math from code is error-prone — verify empirically via Player.log.
- **`Game.Instance.Player.RemoteCompanions`** is the correct API for benched-but-available companions. `AllCharacters` includes dead, ex-companions, and summons. `RemoteCompanions` includes pets — filter with `unit.Get<UnitPartPet>() != null`.
- **`IsInGame` semantics**: `False` = benched/remote companion (not in scene), `True` = in scene (active party + summons). Counterintuitive — benched companions are NOT "in game".
- **`spell.CanTarget()` rejects out-of-scene units**: The game's ability targeting system requires targets to be in the current scene. For config-only targeting (reserve companions), bypass this check.
- **Don't destroy UI elements during their click callbacks**: `GameObject.Destroy` (deferred) and `DestroyImmediate` both corrupt Unity's EventSystem mid-click. Let `ShowBuffWindow` handle rebuilds via its size-mismatch detection instead.
- **`OwlcatButton` via `AddComponent` doesn't render**: Needs internal layer structure. Use `MakeButton()` with `buttonPrefab` (static field set in `CreateWindow`) for game-styled buttons.
- **ScrollRect needs raycast target for wheel events**: Add transparent `Image` with `raycastTarget = true` to the Viewport. Without it, only drag-scroll works.
- **`GlobalBubbleBuffer.Buttons` references go stale after save/load**: The `Buttons` list contains OwlcatButton references that become destroyed Unity objects when the UI is reinstalled. Always null-guard individual buttons in `ForEach` lambdas. In EventBus handlers, separate UI operations from game logic in distinct try-catch blocks so a stale button doesn't block other functionality.
- **`ActivatableAbility.IsOn = true` does NOT start the ability**: Setting `IsOn` only flips the `m_IsOn` flag and calls `OnDidTurnOn()`. The ability won't actually activate (apply buffs, consume resources) until `TryStart()` is called. Always call `if (!activatable.IsStarted) activatable.TryStart();` after setting `IsOn = true`.
- **No per-round EventBus events in RTWP mode**: `ITurnBasedModeHandler.HandleRoundStarted` only fires in turn-based mode. `CombatController.StartRound()` and `TickTime()` are also turn-based only. For RTWP round tracking, use `Game.Instance.Player.GameTime` (1 round = 6 seconds) checked from `MonoBehaviour.Update()`.
- **Prefer manual Harmony patching for private methods**: `[HarmonyPatch]` attribute on publicized-private methods can silently fail. Use `AccessTools.Method(type, "MethodName")` + `harmony.Patch()` with explicit success/failure logging for reliable patching.

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
| `ShortcutBinding` | `ShortcutBinding.cs` | Readonly struct for keyboard shortcuts with modifier keys (Ctrl/Shift/Alt) + backward-compatible JSON converter |
| `BubbleBuffGlobalController` | `BuffExecutor.cs` | MonoBehaviour handling shortcut capture/execution and spell casting coroutines |

### UI Structure

`BubbleBuffer.cs` (~3000 lines) contains most UI code. Key patterns:

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

JSON files in `Config/` (en_GB, de_DE, fr_FR, ru_RU, zh_CN) are embedded resources. Access via `"key".i8()` extension method. English (`en_GB.json`) is the fallback. When adding new UI text, add keys to all locale files.
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

## Credit System (Buff Availability)

- **`BubbleBuff.Validate()`** builds `ActualCastQueue` AND consumes credits via `ChargeCredits()`. By the time `BuffExecutor.Execute()` runs, `credits.Value` is already decremented. Do NOT re-check `AvailableCredits` in Execute — it will always be 0 for single-charge items.
- **`AddBuff()` merges providers by `BuffKey`**: If the same spell exists from multiple sources (spellbook + wand + scroll), they share one `BubbleBuff` entry. The `Category` is set only on first creation — later providers inherit the existing category. Wand spells that already exist as regular buffs won't appear in the Equipment tab.
- **`BubbleBuff.IsMass`**: Set in `BufferState.AddBuff()` via `spell.Blueprint.IsMass()`. Mass/communal spells (detected via `AbilityTargetsAround`, `ContextActionPartyMembers`, or name ending in "Communal") use `ValidateMass()` — one credit, one CastTask, all wanted targets marked as given.
- **`ValidateMass()` target selection**: Iterates `wanted` targets to find one the caster can reach — do NOT use `HashSet.First()` (non-deterministic ordering).

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
