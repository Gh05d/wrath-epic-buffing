# Reserve Companion Configuration

**Date:** 2026-04-06
**Status:** Draft

## Problem

Players with large rosters (25+ characters including mercenaries) can only configure buffs for the active party. Reserve characters must be configured after being swapped in, which is tedious when rotating frequently. The UI should allow configuring buff assignments for all recruited companions, not just the active party.

## Design

### Data Model

New property `Bubble.ConfigGroup` (List<UnitEntityData>):
- **Toggle inactive (default):** identical to `Bubble.Group` (active party + in-game pets)
- **Toggle active:** active party + pets, then reserve characters from `Game.Instance.Player.AllCharacters` (filtered to exclude current party members) + their pets (without `IsInGame` filter)

New bool `Bubble.ShowReserve` — transient UI state, defaults to `false`. Not persisted.

`Bubble.GroupById` is built from `ConfigGroup` so that save/load can resolve UniqueIds for both active and reserve characters.

### Spell Scanning

`RecalculateAvailableBuffs` receives `ConfigGroup` instead of `Group`. This scans spellbooks, abilities, and inventory eligibility for reserve characters, so the UI shows which buffs they have available.

**Execution remains unchanged.** `BuffExecutor.Execute()` works with `Bubble.Group` (active party only). Credits for reserve characters are not validated or consumed — that happens when the character joins the active party and `Recalculate` runs again.

**CasterQueue filtering:** `Validate()` / `ActualCastQueue` must filter out casters not in `Bubble.Group`. A reserve character may appear as a provider in a CasterQueue (e.g., they know Haste), but must not be selected as caster during execution since they're not in the scene.

### UI — Portrait Row & Scrolling

The existing portrait row becomes scrollable (Unity ScrollRect + Viewport). This benefits all parties, not just reserve mode.

Order in the row:
1. Active party members + their pets (as before)
2. Visual separator (vertical divider line)
3. Reserve characters + their pets

Separator and reserve block are only visible when `ShowReserve = true`.

**Visual distinction for reserve characters:**
- Semi-transparent overlay on reserve portraits (dimmed appearance)
- Vertical separator line between active party and reserve block

**Toggle button** placed near the existing HUD buttons (gear/settings area). Toggling triggers a window rebuild (`CreateWindow`) since the portrait array size changes.

### Save/Load

No changes to the save format. `SavedBuffState.Wanted` stores UniqueIds which are independent of party membership. When a reserve character is configured, their UniqueId is stored in `Wanted`. On load, it resolves regardless of active/reserve status.

**Load path adjustment:** `BubbleBuffSpellbookController`'s state restoration currently iterates `Bubble.Group`. This must use `ConfigGroup` instead, so reserve character configs display correctly when the toggle is active.

### Localization

New UI strings added to all locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`):

| Key | en_GB | de_DE |
|-----|-------|-------|
| Toggle label/tooltip | Show Reserve | Reserve anzeigen |
| Reserve portrait tooltip | Not in active party | Nicht in aktiver Gruppe |

French, Russian, and Chinese get English fallback text.

## Edge Cases

- **Reserve chars as casters:** Filtered out of `ActualCastQueue` at execution time. They can be providers in the CasterQueue for configuration purposes but are skipped when casting.
- **Pets of reserve chars:** Included in `ConfigGroup` without `IsInGame` filter. Their UnitEntityData is in memory and scannable. Same visual treatment (overlay + separator) as their owners.
- **Party size changes during config:** Window rebuild on toggle ensures portrait array matches current `ConfigGroup` size. Existing bounds guards (`i < targets.Length`) protect against race conditions.
- **Save migration:** Not needed. Existing UniqueId-based save format already supports characters not currently in the party.
