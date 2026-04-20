# Buff It 2 The Limit

## Overview

Buff It 2 The Limit (formerly BubbleBuffs) is a Unity mod for **Pathfinder: Wrath of the Righteous** that adds automated buff casting routines to the spellbook UI. Players configure which buffs to cast on which party members, then execute them with HUD buttons. Built with C#/.NET Framework 4.81, Harmony patches, and Unity UI. Distributed via [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948).

## Build

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

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
- **`UpdateCasterDetails` filters caster portraits**: A caster is added to `distinctCasters` only if they have a non-SelfCastOnly provider OR a Brownfur-eligible personal spell (`SourceType == Spell && spell.IsArcanistSpell && School == Transmutation && HasFact(ShareTransmutationFeature)`). Empty list → `castersHolder` hidden, "Cast on self" label shown. Parties without Brownfur keep that label for personal spells.
- **Share Transmutation scope is engine-enforced**: `ShareTransmutationFeature` (`c4ed8d1a90c93754eacea361653a7d56`, Brownfur Arcanist) only extends arcanist Transmutation personal spells (Ice Body, Form of the Dragon, Enlarge Person). Shield (Abjuration), Mage Armor (Conjuration), Angel Aspect (Mythic) cannot be shared — no mod-side fix possible. The toggle stays grayed for non-eligible spells.
- **`BuffProvider.CanTarget` must respect the `ShareTransmutation` flag**: Both the reserve-target branch and the `ForceShareTransmutation` block have a `SelfCastOnly` guard. Without a `!ShareTransmutation` escape, the toggle has no effect — feature is retained during `spell.CanTarget` but the hardcoded `return targetId == who.UniqueId` overrides the engine's answer.
- **Mass/burst spells target the caster, not an arbitrary wanted ally**: `CastTask.Target` for `buff.IsMass` is set to `new TargetWrapper(caster.who)`. Semantically equivalent (50ft burst around caster blesses everyone in range) but avoids UnitCommand movement-to-target interrupts at combat start, and fixes the confusing "why does Daeran target Arasmes with Bless?" UX issue. See `BuffExecutor.Execute` and `ExecuteCombatStart`.
- **`source-controls-section` needs minHeight ≥ 110**: The section hosts two vertical stacks (`prioSide`: prioLabel + extendRod + combatStart + roundLimit; `toggleSide`: 4 source toggles). With minHeight=30 the containers were only ~56 high, and togglePrefab's fixed sizeDelta pushed the bottom children (Use Equipment, Combat Start) outside the parent bounds — clicks silently failed. Keep `sourceControlsSection` at 110+ so all children fit vertically.
- **`TextMeshProUGUI` competes with `LayoutElement` at default priority**: TMP implements ILayoutElement (priority 1) and can make a LayoutElement's `preferredHeight` be ignored, collapsing the text to 0 height. For text with a fixed target height (e.g. the priority label), set `LayoutElement.layoutPriority = 2` AND `minHeight` to force the LayoutElement to win. Also disable `enableWordWrapping` when the text should stay on one line.
- **Caster portrait index ≠ CasterQueue index**: `BufferView.casterPortraitMap` maps portrait indices to CasterQueue indices after deduplication. Always use the map when translating portrait clicks to CasterQueue entries.
- **`BubbleBuff.SavedState` is never assigned**: The `SavedState` field on `BubbleBuff` is always null at runtime. The save system reads/writes via `BufferState.Save()` which copies from `buff.UseSpells`/etc fields directly. UI code must read from `buff.UseSpells`, NOT `buff.SavedState?.UseSpells` (which always falls back to default). Toggle handlers should write to `buff.UseSpells` directly.
- **`BubbleBuff.Spell` is null for songs**: Songs use `ActivatableSource` instead. All code paths touching `Spell`, `Spell.Blueprint`, `Spell.Name`, `Spell.IsMetamagicked()` etc. must null-guard or check `IsSong` first. Key locations: `BindBuffToView`, `BuffProvider.CanTarget`, `UpdateDetailsView`, `MetaMagicFlags`, `Name`/`NameMeta` properties.
- **`BuffProvider.CanTarget` crashes when `spell` is null**: `AddActivatable()` sets `spell = null` for ALL activatables (songs, class abilities, AND item-backed). `CanTarget` and `SelfCastOnly` now guard `spell == null` as self-cast-only. Any new code accessing `provider.spell` must null-check first.
- **`AddActivatable()` bypasses `GetBeneficialBuffs`**: Unlike `AddBuff()`, activatables are added directly without checking for beneficial effects. Item-backed activatables are filtered by `blueprint.Buff != null`, but some items with buffs (e.g. summoning figurines) still pass — their buff triggers a summon rather than granting stats. Currently accepted as minor noise in the Equipment tab.
- **`ilspycmd` stack overflows on large classes** (e.g., `UnitEntityData`, `AbilityData`, `Spellbook`). Use smaller part classes instead (e.g., `UnitPartPetMaster`). Publicized DLLs at `BuffIt2TheLimit/obj/Debug/publicized/Assembly-CSharp.dll` work better than originals. IL mode (`-il` flag) avoids some type-resolution stack overflows that C# decompilation hits.
- **ilspycmd full-IL dump workaround**: When `-t <type>` stack overflows, dump the whole assembly once: `DOTNET_ROOT=/home/pascal/.dotnet ~/.dotnet/tools/ilspycmd <dll> -il -o /tmp/asmdump`. Then locate the class with `grep -n '^.class.*<FQN>'` and read ranges with `awk 'NR>=X && NR<=Y'`. Faster than retrying per-type decompiles.
- **`new AbilityData(blueprint, caster)` for items drops item caster level**: Creates an AbilityData with no `OverrideCasterLevel`/`OverrideSpellLevel`, so `CalculateParams()` uses the caster's class level. For `BlueprintItemEquipmentUsable` (scrolls/potions/wands/rods), always set both — but **prefer `ItemEntity.Get<CraftedItemPart>()` over `blueprint.CasterLevel`**: crafted items (e.g. a player-crafted CL-5 Mage Armor scroll) carry their per-item CL/SL/DC on `Kingmaker.Craft.CraftedItemPart`. The game's `ItemStatHelper.GetCasterLevel(ItemEntity)` checks CraftedItemPart first and falls back to blueprint — mirror that pattern. Reading `blueprint.CasterLevel` alone collapses crafted items to the blueprint default (usually CL 1), giving 1h Mage Armor instead of 5h.
- **`docs/` directory is gitignored**: Use `git add -f` when committing spec/plan files.
- **`RuleCastSpell` clones `AbilityParams` in constructor**: `Context = spell.CreateExecutionContext(target)` → `m_Params = @params.Clone()` runs BEFORE `OnBeforeEventAboutToTrigger`. Modifying `SpellToCast.MetamagicData` in the handler alone won't affect duration/DC calculations — must also set `Context.m_Params.Metamagic`.
- **`MetamagicData.Clear()` zeros all fields, `Add()` only restores flags**: `Clear()` resets `MetamagicMask`, `SpellLevelCost`, AND `HeightenLevel` to 0. `Add(Metamagic)` only sets flag bits — it does NOT restore `SpellLevelCost` or `HeightenLevel`. When restoring metamagic state after mutation, always restore all three fields. `SpellToCast` is a shared cached reference — corrupting it permanently affects all future `Spellbook.GetSpellLevel()` reads.
- **Blueprint action tree extraction**: To check what actions a spell uses: `ssh deck-direct "unzip -p '<game-path>/blueprints.zip' '<blueprint-path>.jbp'"` piped through `python3 -m json.tool`. Parse `Components[0].Actions.Actions` recursively for the action types. The `blueprints.zip` is at the game install root, not in subdirectories.
- **Magic Deceiver fused spells (MagicHack system)**: Fused spells use template `BlueprintAbility` shells (`MagicHackDefaultSlot1-10`, `MagicHackTouchSlot1-10`) with empty actions/icons. Actual data lives in `AbilityData.MagicHackData` (`Spell1`, `Spell2`, `Name`, `DeliverBlueprint`, `SpellLevel`). The game engine routes `Name`, `Icon`, `CanTargetAlly`, `CanTargetEnemies`, `TargetAnchor` through `GetDeliverBlueprint()` which returns `MagicHackData.DeliverBlueprint` for fused spells. The `MagicDeceiverSpellbook` is `Spontaneous: true, IsArcanist: false, AllSpellsKnown: true` with its own curated spell list (mostly offensive — no standard buffs like Haste/Shield). Fused spells are stored via `Spellbook.AddCustomSpell()`.
- **Archetype spellbooks can override `IsArcanist`**: `MagicDeceiverSpellbook` sets `IsArcanist: false` despite being an Arcanist archetype. Don't assume Arcanist archetypes use the Arcanist scanning branch — always check the spellbook blueprint's flags.
- **Weapon enchantment spells use `EnhanceWeapon` action**: Magic Weapon, Keen Edge, Magic Fang and similar spells all use `Conditional` → `EnhanceWeapon` + `ContextActionApplyBuff`. Handled by `EnhanceWeaponEffect` in `IBeneficialEffect.cs`. If a new weapon enchantment spell is reported missing, check for this pattern first.
- **`UnitUseAbility.CreateCastCommand` rejects synthetic AbilityData**: Equipment/scroll/potion items use `new AbilityData(blueprint, caster)` which isn't in the caster's ability list. The game's command system silently rejects it. `AnimatedExecutionEngine` falls back to `Rulebook.Trigger` for non-spell CastTasks. `InstantExecutionEngine` always uses `Rulebook.Trigger` so it works for all source types.
- **Verify deploys by comparing DLL timestamps**: After `./deploy.sh`, compare local `BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll` vs `ssh deck-direct "ls -la '<game-path>/Mods/BuffIt2TheLimit/BuffIt2TheLimit.dll'"`. File size and date must match. Game must be restarted to load the new DLL.
- **Info.json byte-count is ambiguous**: versions like `1.11.10` and `1.11.11` have the same character count → identical file size. Always `grep Version Info.json`, never compare via `ls -la` alone.
- **UMM hot-reload only works in DEBUG builds**: `[EnableReloading]` on `Main` is `#if DEBUG`. Release builds need a full game restart after `deploy.sh`. Verify what's actually loaded via `grep "Version '" Player.log | tail -1` before drawing conclusions from in-game testing.
- **Debug Unity UI positions with `GetWorldCorners()`**: When UI elements appear mispositioned, add `Main.Log` calls with `RectTransform.GetWorldCorners()` to compare actual pixel positions at runtime. Anchor math from code is error-prone — verify empirically via Player.log. For "why is this child outside its parent" bugs, walk the parent chain with `transform.parent` and log each level's worldW/worldH + anchorMin/Max/sizeDelta — reveals which ancestor clamps the size.
- **Combat-start disrupts `Unit.Commands.Run()` queue**: At combat initialization the game re-orders/cancels pending UnitCommands. `AnimatedExecutionEngine` casts via `CreateCastCommand` → `Commands.Run` — these queued casts can silently drop at combat start, especially when the cast requires move-to-target. Mitigations: target the caster (no movement), use `InstantExecutionEngine` (goes via `Rulebook.Trigger`, bypasses queue), or target a self-centered AoE anchor. The cast log will say `cmd=OK` but the spell never fires.
- **`Game.Instance.Player.RemoteCompanions`** is the correct API for benched-but-available companions. `AllCharacters` includes dead, ex-companions, and summons. `RemoteCompanions` includes pets — filter with `unit.Get<UnitPartPet>() != null`. It's `IEnumerable`, not `ICollection` — use `.Count()` (LINQ), not `.Count` property (CS0428 "cannot convert method group to int").
- **`IsInGame` semantics**: `False` = benched/remote companion (not in scene), `True` = in scene (active party + summons). Counterintuitive — benched companions are NOT "in game".
- **`spell.CanTarget()` rejects out-of-scene units**: The game's ability targeting system requires targets to be in the current scene. For config-only targeting (reserve companions), bypass this check.
- **Don't destroy UI elements during their click callbacks**: `GameObject.Destroy` (deferred) and `DestroyImmediate` both corrupt Unity's EventSystem mid-click. Let `ShowBuffWindow` handle rebuilds via its size-mismatch detection instead.
- **`OwlcatButton` via `AddComponent` doesn't render**: Needs internal layer structure. Use `MakeButton()` with `buttonPrefab` (static field set in `CreateWindow`) for game-styled buttons.
- **ScrollRect needs raycast target for wheel events**: Add transparent `Image` with `raycastTarget = true` to the Viewport. Without it, only drag-scroll works.
- **`GlobalBubbleBuffer.Buttons` references go stale after save/load**: The `Buttons` list contains OwlcatButton references that become destroyed Unity objects when the UI is reinstalled. Always null-guard individual buttons in `ForEach` lambdas. In EventBus handlers, separate UI operations from game logic in distinct try-catch blocks so a stale button doesn't block other functionality.
- **`ActivatableAbility.IsOn = true` does NOT start the ability**: Setting `IsOn` only flips the `m_IsOn` flag and calls `OnDidTurnOn()`. The ability won't actually activate (apply buffs, consume resources) until `TryStart()` is called. Always call `if (!activatable.IsStarted) activatable.TryStart();` after setting `IsOn = true`.
- **No per-round EventBus events in RTWP mode**: `ITurnBasedModeHandler.HandleRoundStarted` only fires in turn-based mode. `CombatController.StartRound()` and `TickTime()` are also turn-based only. For RTWP round tracking, use `Game.Instance.Player.GameTime` (1 round = 6 seconds) checked from `MonoBehaviour.Update()`.
- **`ExecuteCombatStart` runs while `Game.Instance.Player.IsInCombat == true`**: Triggered by `HandlePartyCombatStateChanged(inCombat: true)`, so by the time its casts fire, combat is already registered. Any condition gated on `!IsInCombat` will skip combat-start autocast — gate on an explicit "prebuffing intent" flag instead, or branch on the call site.
- **Two dispatch paths for cast coroutines**: `BuffExecutor.Execute` routes through `BubbleBuffGlobalController.CastSpells(tasks, …)`. `BuffExecutor.ExecuteCombatStart` calls `StartCoroutine(engine.CreateSpellCastRoutine(tasks))` directly and bypasses `CastSpells`. Any wrapper/interception touching all mod-initiated casts must handle both sites.
- **Prefer manual Harmony patching for private methods**: `[HarmonyPatch]` attribute on publicized-private methods can silently fail. Use `AccessTools.Method(type, "MethodName")` + `harmony.Patch()` with explicit success/failure logging for reliable patching.
- **`ActivatableAbility.SourceItem` distinguishes source**: Inherited from `Fact`. Null = class feature (Battle Spirit, Judgments, Rage). Non-null = item-granted (metamagic rods, quivers). Critical filter for the activatable scan.
- **`ActivatableAbilityResourceLogic` = has resource cost**: Component on `BlueprintActivatableAbility`. Presence means the ability consumes resources (rounds/day, charges). Absence means free toggle (Power Attack, Wings, Combat Styles). Filter out free toggles in scanning or the Ability tab drowns in noise.
- **Item-backed activatables bypass the QuickSlot scan**: `BlueprintItemEquipmentUsable` items with `Ability == null` (metamagic rods, ammunition quivers) grant their effect via ActivatableAbility instead of BlueprintAbility. They fall through the QuickSlot scan (skipped because Ability is null) and must be routed via the `ActivatableAbility.SourceItem != null` branch to `AddActivatable(..., Category.Equipment, sourceItem)`.
- **`BlueprintBuff.FxOnStart` fallback in `IsBeneficial`**: Visual-only buffs like MageLightBuff have only `UniqueBuff` (infrastructure) but their actual effect is the `FxOnStart` light asset. `IsBeneficial` must accept buffs with a non-null `FxOnStart.AssetId` even when all components are infrastructure, otherwise Light cantrip and similar get rejected.
- **Diagnostic workflow without unit tests**: Add temporary `Main.Log(...)` calls (not `Main.Verbose` — that requires debug flags), `./deploy.sh`, restart the game, read via `ssh deck-direct "grep '<tag>' '<Player.log path>'"`, then revert to `Main.Verbose` with "state"/"rejection" categories before committing. Tag log lines with unique bracketed prefixes like `[ABILITY-SCAN]` for easy filtering.
- **`AddBuff()` has two separate filter concerns**: `skipDamageFilter` (relaxes `GetBeneficialBuffs` for abilities with damage components) should apply to ALL `Category.Ability` entries. The self-target fallback (accepts abilities with no detected effects) should only apply to `isClassAbility` (`sourceItem == null`). Don't conflate the two — item-backed abilities need the relaxed damage filter but must NOT hit the self-target fallback.
- **`SpellsWithBeneficialBuffs` cache key is GUID-only**: The cache doesn't include `skipDamageFilter` as part of the key. Currently safe because class abilities and item-backed abilities have disjoint GUIDs, but if that ever changes the first scan wins and later scans get a stale result.
- **`Config/*.json` files have UTF-8 BOM**: `python3 -m json.tool` fails with `Unexpected UTF-8 BOM`. Use `python3 -c "import json; json.load(open('<file>', encoding='utf-8-sig'))"` for BOM-aware validation. The C# build also catches structural errors if you prefer to skip the pre-check.
- **`Category.Toggle` = free-toggle activatables without `ActivatableAbilityResourceLogic`** (Power Attack, Shifter Claws, Monk stances, Arcane Strike). Routed via the `else` branch after `hasResourceLogic` in `BufferState.cs` activatable scan. Tab is **always visible** (no `TogglesEnabled` setting — per-ability opt-in already happens via the character portrait grid inside the tab). Round-limit slider hidden via `buff.Category != Category.Toggle` guard in `UpdateDetailsView`. Execution reuses the existing `ExecuteCombatStart()` Phase 0 unchanged (all activatables flow through the same `IsActivatable` filter).
- **Scan category summary log** (`BufferState.cs`, ~line 425): `Main.Log($"Scan complete: {list.Count} buffs (Buff=..., Ability=..., Equipment=..., Song=..., Toggle=...)")`. When adding a new `Category` enum value, extend BOTH the switch counters AND the log format string, otherwise the new category silently vanishes from diagnostics.
- **Variant-parent activatables are unactivatable stubs**: Abilities whose blueprint has a component implementing `IActivatableAbilityConversionsProvider` (`ShiftersFury`, `ActivatableAbilityVariants`, `ActivatableAbilitySet`) act as menu parents. The parent has `ActivationDisable` (`AlwaysFalse()` → `CanTurnOn()` always false → `SetIsOn(true)` is a no-op). The real activators live on per-unit lists (`ShiftersFuryPart.AppliedFacts` for Shifter's Fury, one per wielded natural weapon) — these are real persistent `ActivatableAbility` instances, not ephemeral stubs. Scan-side: `BufferState.cs` skips `ActivatableAbilityVariants`/`ActivatableAbilitySet` parents but admits `ShiftersFury`. Activation-side: `BuffExecutor.ResolveActivationTarget()` routes `ShiftersFury` to `ShiftersFuryPart.AppliedFacts[State.SelectedWeaponIndex]` (fallback index 0); `IsEffectivelyOn()` checks `ShiftersFuryPart.m_IsOn` because the parent's `IsOn` is pinned to false.
- **Don't filter activatables on `IsRuntimeOnly || HiddenInUI` alone**: Mutual-exclusive class features (Chimera Aspect / Greater Chimera Aspect forms, Fiendflesh aspects, Weretouched forms) set both flags but are legitimate user-togglable abilities — blanket-filtering kills them (regression in v1.13.1). Dedup variant-conversions via `ConversionsProvider.GetConversions()` per unit: build a `HashSet<BlueprintGuid>` of every conversion's blueprint guid, then skip any activatable whose guid is in that set. See `BufferState.cs` scan loop.
- **Equipment-activatable charges gate must respect `SpendCharges`**: `srcItem.Charges <= 0` alone falsely skips permanent-toggle items like Crimson Banner (`SpendCharges: false`) — the nominal Charges value is never consumed or refreshed. Gate with `&& (srcItem.Blueprint as BlueprintItemEquipmentUsable)?.SpendCharges != false` so only real charge-consuming items get skipped when empty. See `BufferState.cs` equipment-activatable branch.
- **Phase 0 must activate variant-parents LAST**: Shifter's Fury (parent with `ConversionsProvider`) only has `AppliedFacts` after a form-change activatable (Chimera Aspect, etc.) has populated natural weapons. Order the Phase-0 loop with `.OrderBy(b => b.ActivatableSource?.ConversionsProvider != null ? 1 : 0)` (stable sort) so Aspect toggles fire first, Fury second — both in `BuffExecutor.Execute` and `ExecuteCombatStart`.
- **`[CSD]` log prefix = Combat-Start auto-cast diagnostics**: `grep '\[CSD\]' Player.log` surfaces mod version, engine choice, party composition, and per-caster rejection reasons (`UseSpells=false`, `credits < needed`, `CanTarget=false`, `spell=null`) via `BubbleBuff.DiagnoseCaster()`. See the dedicated "Combat-Start Diagnostics" section below for the full grep workflow.

## Debug Keybinds (DEBUG builds only)

- **Shift+I** — Reinstall UI + recalculate buffs (hot-reload during development)
- **Shift+B** — Reload the entire mod
- **Shift+R** — Debug helper (currently adds a test item)

## Combat-Start Diagnostics

`ExecuteCombatStart` emits structured `[CSD]`-tagged lines on every combat start (release-safe `Main.Log`, not `Verbose`): mod version + `SkipAnim` setting, active/reserve party, per-buff `Marked/Fulfilled/Queue/CasterQueue` counts, per-caster rejection reason when `Fulfilled=0`, Phase0 (activatables) + Phase1 (spells) queue events with caster → target, final engine choice + task count. Grep workflow: `ssh deck-direct "grep -a '\[CSD\]' '<Player.log>' | tail -80"`. `BubbleBuff.DiagnoseCaster(provider)` mirrors `Validate`/`ValidateMass` checks without side effects — extend it when adding new source-type filters so rejections stay grep-able.

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
