# Activatable Abilities & Expanded Ability Tab

**Date:** 2026-04-09
**Status:** Approved

## Problem

The Ability tab is missing many class abilities. Two categories are affected:

1. **ActivatableAbilities** (toggles with resource costs) — Inquisitor Judgments, Barbarian Rage, Shaman Spirit Weapon, Aeon Gaze, Demon Aspects, etc. Currently only Bardic/Azata Performances are handled (as "Songs"). The remaining ~50 ActivatableAbilityGroups are ignored.

2. **Regular abilities** that fail the `GetBeneficialBuffs()` filter — e.g., Dimension Strike (Magus). These exist in `dude.Abilities.RawFacts` but get rejected because they lack detectable `ContextActionApplyBuff` actions.

## Solution

Generalize the Song system into an ActivatableAbility system. Songs become a special case. Add resource-based ActivatableAbilities to the Ability tab. Relax the beneficial effect filter for regular abilities.

## Design

### Data Model

**BubbleBuff changes:**
- `IsSong` becomes a computed property: `IsSong => IsActivatable && Category == Category.Song`
- New field `IsActivatable` (bool) — true for all ActivatableAbility-based entries
- `ActivatableSource` is now set for all activatables, not just songs
- New field `ActivatableGroup` (ActivatableAbilityGroup) — stores group for mutual exclusivity

**BuffSourceType:**
- New value `Activatable` — for resource-based ActivatableAbilities that aren't songs
- `Song` stays for Bardic/Azata Performances

**Category:**
- No changes — new activatables use `Category.Ability`, songs keep `Category.Song`

**SavedState:**
- New `ActivatablesEnabled` setting (bool, default true)

### Scanning (BufferState.RecalculateAvailableBuffs)

**ActivatableAbility scan:**

Iterate `dude.ActivatableAbilities.RawFacts` for each party member:

- Check `blueprint.GetComponent<ActivatableAbilityResourceLogic>()`:
  - **Has ResourceLogic** → include
  - **No ResourceLogic** → skip (Power Attack, Wings, Combat Styles, etc.)
- Group in `PerformanceGroups` (BardicPerformance, AzataMythicPerformance) → `AddActivatable(..., Category.Song)`
- Otherwise → `AddActivatable(..., Category.Ability)`
- Credits from `activatable.ResourceCount`

**`AddSong()` renamed to `AddActivatable()`:**

Signature: `AddActivatable(UnitEntityData dude, ActivatableAbility activatable, int charIndex, Category category)`

Same logic as current `AddSong` but sets `Category` from parameter, `BuffSourceType.Song` for songs, `BuffSourceType.Activatable` for others.

**Regular ability filter relaxation:**

In the existing `dude.Abilities.RawFacts` scan:

- For `Category.Ability`: skip the heal/damage early-return in `GetBeneficialBuffs()`
- Abilities with ONLY damage/heal actions and NO buff application → still filtered out
- Mixed abilities (buff + damage, summons) → pass through
- Fallback: if `GetBeneficialBuffs()` returns empty but `spell.TargetAnchor == Owner` → include anyway (catches self-buff abilities like Dimension Strike that lack detectable ContextActionApplyBuff)

### Execution

**Phase 0 — Activatable Activation (generalized):**

Current song phase extends to all `IsActivatable` buffs:

- Iterate `State.BuffList` where `b.IsActivatable && b.InGroups.Contains(buffGroup) && b.Fulfilled > 0`
- **Mutual exclusivity:** Track activated groups per caster (`Dictionary<string, HashSet<ActivatableAbilityGroup>>`). If caster+group already has an activation → skip.
  - Iterate BuffList in reverse so the last-selected entry wins
- Activation: `activatable.IsOn = true` + `TryStart()`
- Round-limit deactivation reused from song system for all activatables

**Phase 1 — Regular abilities:**

No changes. Abilities like Dimension Strike flow through the normal CastTask path as `BuffSourceType.Spell`.

**Deactivation:**

Same `MonoBehaviour.Update()` mechanism tracking `Game.Instance.Player.GameTime`. Applies to all activatables, not just songs.

### UI

**Ability tab — mixed content:**

Two rendering modes based on `buff.IsActivatable`:
- `IsActivatable == true` → Song-style rendering (per-character toggle, round-limit slider)
- `IsActivatable == false` → Standard buff rendering (target portraits, group assignment)

**Mutual exclusivity in UI:**

When toggling an ActivatableAbility for a character: if another ability in the same `ActivatableAbilityGroup` is already toggled for that character → auto-untoggle the old one. Radio-button behavior per character+group.

**Settings:**

New toggle: "Activatable Abilities" (`SavedState.ActivatablesEnabled`, default true). Controls non-song activatables in the Ability tab only. Existing `SongsEnabled` remains separate and unchanged.

No new tabs. No new HUD buttons.

### Affected Files

| File | Changes |
|---|---|
| `BubbleBuff.cs` | `IsActivatable` field, `IsSong` → computed, `ActivatableGroup` field |
| `BubbleBuffer.cs` | `BuffSourceType.Activatable` enum value, UI rendering branch for activatables in Ability tab, mutual exclusivity toggle logic, settings toggle |
| `BufferState.cs` | `AddSong` → `AddActivatable`, ActivatableAbility scan with ResourceLogic filter, `PerformanceGroups` rename, relaxed beneficial filter for regular abilities |
| `BuffExecutor.cs` | Phase 0 generalized for all activatables, mutual exclusivity tracking, reverse iteration |
| `SaveState.cs` | `ActivatablesEnabled` field |
| `ExtentionMethods.cs` | `GetBeneficialBuffs()` damage/heal filter bypass for abilities, self-target fallback |
| `Config/*.json` | New localization keys for "Activatable Abilities" setting |

### Out of Scope

- Dropdown/grouped UI for weapon enchantment choices (future enhancement if users request it)
- Non-resource-based toggles (Power Attack, Wings, etc.)
- Changes to Song tab behavior
