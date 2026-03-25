# Song/Performance Support Design

## Overview

Add support for Bard/Skald performances and Azata songs to Buff It 2 The Limit. Songs are `ActivatableAbility` toggles — a different mechanism than spell casting. The mod will activate them as part of buff routines, with a dedicated UI tab and resource checking.

## Requirements

- **Scope:** Bard/Skald performances (Inspire Courage, Inspire Competence, Inspire Heroics, Inspire Greatness, Dirge of Doom, Frightening Tune) + Azata songs (Song of Heroic Resolve, Song of Broken Chains, Song of Defiance, Song of the Second Breath)
- **Behavior:** Activate only — songs stay on until the player manually deactivates them
- **Resource check:** Verify remaining rounds before activating; skip if none available
- **UI:** Dedicated "Songs" tab, separate from Buffs/Abilities/Equipment
- **BuffGroup assignment:** Songs are assignable to Long/Important/Quick like normal buffs

## Design

### 1. Scanning

New phase in `BufferState.RecalculateAvailableBuffs()` after the existing three phases (Spellbooks, Abilities, Items):

**Phase 4: Activatable Performances**
- Iterate `dude.ActivatableAbilities.Enumerable` (or `.RawFacts`)
- Filter on Bard/Skald performances and Azata songs via `BlueprintActivatableAbility.Group` property (e.g., `ActivatableAbilityGroup.BardicPerformance`) or known feature blueprints
- For each match: create a `BubbleBuff` entry via a dedicated `AddSong()` method (NOT `AddBuff()` — see integration notes)
- Credits = remaining rounds from the performance resource (`AbilityResourceLogic` on the blueprint, queried via `AbilityResource.GetAmount()`)
- `SelfCastOnly = true` — performances activate on the caster, effect is party-wide
- No merging with existing spell entries (songs are never simultaneously available as spells)

**Important: Songs bypass `AddBuff()`.** The existing `AddBuff()` method calls `GetBeneficialBuffs()` which requires `AbilityEffectRunAction` on the blueprint — a component that `BlueprintActivatableAbility` does not have. Songs apply buffs through `ActivatableAbilityBuff` components instead. A dedicated `AddSong()` method constructs `BubbleBuff` entries directly, populating `BuffsApplied` from the activatable ability's buff component.

### 2. Data Model

**New enum values:**
- `Category.Song` — after `Equipment`
- `BuffSourceType.Song` — after `Equipment`

**BuffKey extension:**
- New constructor overload accepting `BlueprintActivatableAbility` GUID (metamagic is always 0 for songs). Songs are keyed by blueprint GUID only.

**BubbleBuff extensions:**
- `ActivatableAbilityData ActivatableSource` — reference to the activatable ability (analogous to `AbilityData SpellToCast` for spells)
- `IsSong` — computed property, `true` when `SourceType == Song`
- Songs always have exactly one caster (no CasterQueue with fallbacks)
- Song constructor: second constructor (or factory method) that accepts `ActivatableAbilityData` instead of `AbilityData`. Populates `Name` from the activatable blueprint. `Spell` field is null for songs — all `Spell`-dependent accessors must null-guard.

**SaveState extensions:**
- `SavedBufferState`: new toggle `SongsEnabled` (default `true`, with `[DefaultValue(true)]` attribute for backward-compatible deserialization of old saves)
- No per-buff `UseSongs` toggle — redundant since song buffs always have `SourceType == Song`. The global `SongsEnabled` toggle is sufficient.
- No new caster-specific fields needed — songs have only one possible caster

**BuffProvider:**
- New provider type for songs. Credits use a simple `rounds > 0` boolean rather than the `ReactiveProperty<int>` credit system, since performances consume rounds per combat round (not per activation). UI shows "X rounds remaining" as informational, not as a consumable count.

### 3. Execution

New path in `BuffExecutor.Execute()`:
- Song activation happens at the **top** of `Execute()`, before the CastTask iteration loop. Songs are NOT added to `List<CastTask>`.
- When `buff.IsSong`: check `activatableAbility.IsOn` to detect "already active" (do NOT use `BuffsApplied.IsPresent` — performance buffs may be keyed differently as area effects)
- Pre-checks: (a) not already active (`IsOn`), (b) rounds > 0 available
- Activation API: needs verification via `ilspycmd` decompilation of `ActivatableAbility`. Likely candidates: `unit.ActivatableAbilities.GetFact(blueprint)?.TryStart()` or setting `IsOn = true` with game state updates.
- No `EngineCastingHandler` hook needed — songs don't use metamagic, share transmutation, etc.
- No `AnimatedExecutionEngine`/`InstantExecutionEngine` — songs use their own activation path
- Songs skip `SortProviders()` entirely — only one caster, no source priority sorting. The `GetSourceOrder` array size is not affected.

**Validation:** A dedicated `ValidateSong()` method (not `Validate()`/`ValidateMass()`) that:
- Checks `IsOn` on the activatable ability
- Checks remaining rounds via performance resource
- Marks the single caster as "given" without consuming spell credits

### 4. UI

- New "Songs" tab in the tab bar (alongside Buffs/Abilities/Equipment)
- Per-song display: name, caster portrait, remaining rounds (informational), active/inactive status
- BuffGroup assignment like normal buffs (Long/Important/Quick checkboxes)
- No target selection — songs are always `SelfCastOnly`, effect is automatically party-wide
- Portrait area shows only the single possible caster (no multi-caster dropdown)

### 5. Localization

- New keys in all locale files: tab name ("Songs"), `SongsEnabled` toggle label, tooltip "Remaining rounds: {0}"
- `en_GB` and `de_DE` complete, other locales best-effort

### 6. Mutual Exclusivity

Two separate mutual exclusivity groups exist:
- `ActivatableAbilityGroup.BardicPerformance` (Bard/Skald) — only one active per character
- `ActivatableAbilityGroup.AzataMythicPerformance` (Azata) — only one active per character
- A Bard performance and an Azata song CAN run simultaneously on the same character

If a user enables two performances from the same `ActivatableAbilityGroup` on the same character in the same buff group:
- The game enforces mutual exclusivity — activating a second performance deactivates the first
- The mod should respect this: within a single execution, only activate the first matching song per `ActivatableAbilityGroup` per caster. Log a warning if a second is skipped.

## Out of Scope

- **Skald rage power acceptance/rejection**: Skald's Inspired Rage grants rage powers to allies who can accept or reject. Managing this interaction is out of scope for the initial implementation.
- **Song deactivation management**: Songs stay on until manually deactivated. No auto-off logic.

## Architecture Boundaries

- Song scanning is a self-contained phase in `BufferState` via `AddSong()` — no changes to existing `AddBuff()` or spell/item scanning
- Song execution is a separate branch at the top of `BuffExecutor.Execute()` — no changes to `CastTask`, `EngineCastingHandler`, or execution engines
- Song UI tab follows the same pattern as existing category tabs — no structural UI changes
- Save/load extends existing `SavedBufferState` with `SongsEnabled` field, backward-compatible (`[DefaultValue(true)]`)
- `BubbleBuff.Spell`-dependent code paths must null-guard since songs have `Spell == null`

## Resolved Questions

### Activation API (verified via IL decompilation)

`ActivatableAbility` (namespace `Kingmaker.UnitLogic.ActivatableAbilities`) key members:

| Member | Type | Purpose |
|---|---|---|
| `IsOn` | `bool` property | Whether ability is active. Setter calls `SetIsOn(value, null)` |
| `IsAvailable` | `bool` property | Combined resources + restrictions check |
| `IsAvailableByResources` | `bool` property | Has enough resource charges (via `ActivatableAbilityResourceLogic`) |
| `ResourceCount` | `int` property | Remaining rounds |
| `CanTurnOn()` | `bool` method | Checks all `TurnOnConditions` |
| `AppliedBuff` | `Buff` property | The currently applied buff (`m_AppliedBuff`) |
| `TurnOffImmediately()` | `void` method | Immediate deactivation |

**Activation:** `ability.IsOn = true` (internally calls `SetIsOn(true, null)`).
**Pre-check:** `!ability.IsOn && ability.IsAvailable` (covers resources + restrictions + combat requirements).

### Azata Song Grouping (verified via blueprint inspection)

- Azata songs use `ActivatableAbilityGroup.AzataMythicPerformance` (enum value 28)
- Bard performances use `ActivatableAbilityGroup.BardicPerformance` (enum value 1)
- **Separate groups** — a Bard performance and an Azata song CAN run simultaneously
- Mutual exclusivity is per-group: only one Bard performance OR one Azata song at a time per character
