# Pet/Companion Caster Support

**Date:** 2026-03-15
**Status:** Draft

## Problem

Buff It 2 The Limit only scans main party members (`ActualGroup`) for available buff spells. Pet and companion units — such as the Hag from Hag of Gyronna (at-will spells) or Aivu the Azata Dragon (full spellbook) — are not recognized as buff casters. Some pets (like Aivu) already appear as targets if the game includes them in `ActualGroup`, but their spells are not consistently surfaced as castable buffs, and pets outside `ActualGroup` (like the Hag) are invisible entirely.

## Goal

All pet/companion units that belong to party members should be treated as first-class participants in the buff system — both as casters (their spells/abilities are scanned) and as targets (they appear in the portrait row and can receive buffs).

## Design

### Approach: Expand `Bubble.Group` to include pets

The mod uses `Bubble.Group` as the single source of truth for which units participate in the buff system. All scanning, targeting, portrait rendering, and save/load flows derive from this list. By expanding it to include pets, every downstream system picks them up automatically.

### Core Change: `Bubble.RefreshGroup()`

**Current state:** `Bubble.Group` is a computed property returning `Game.Instance.SelectionCharacter.ActualGroup` on every access. Two redundant copies exist in `BubbleBuffSpellbookController.Group` (line 457) and `GlobalBubbleBuffer.Group` (line 2495).

**New state:** `Bubble.Group` becomes a cached `List<UnitEntityData>` field. A new `RefreshGroup()` method builds the list:

1. Copy `ActualGroup` into a new list
2. For each unit in `ActualGroup`, iterate `unit.Pets`
3. For each pet, if not already in the list, append it
4. Store the result in `Bubble.Group`

The two redundant `Group` properties in `BubbleBuffSpellbookController` and `GlobalBubbleBuffer` are removed. All call sites use `Bubble.Group` instead.

### Refresh Trigger

`Bubble.RefreshGroup()` is called at the start of `BufferState.Recalculate()`, before the `GroupIsDirty` check. This ensures the cached list is current whenever the mod recalculates. The existing `GroupIsDirty` logic (which compares UniqueIds against the last known group) will detect pet additions/removals as group changes and trigger a full `RecalculateAvailableBuffs`.

### Save/Load Compatibility

No changes needed. The save system uses `UniqueId` strings for buff targets (`SavedBuffState.Wanted`), caster priority (`CasterPriority`), and caster state (`CasterKey`). Pet units have stable UniqueIds across save/load. When a pet is removed (e.g., mythic path change), its orphaned IDs are harmlessly skipped during restore — same behavior as when a regular companion is dismissed.

### UI: Portraits

No changes needed. Target portraits are created in a loop over `Group.Count` with dynamic width scaling (`childControlWidth = true`, `childForceExpandWidth = true`, `AspectRatioFitter`). Additional pets (realistically 1-3 extra portraits for a full party) fit within the existing layout.

### Casting Execution

No changes needed. `CastTask` and `EngineCastingHandler` operate on `UnitEntityData` — the game API treats pets and party members identically for spell casting. Pet-specific behaviors are handled automatically:

- **Personal spells** (TargetAnchor.Owner): `BuffProvider.SelfCastOnly` = true, `CanTarget()` restricts to the pet's own UniqueId
- **At-will abilities** (no resource): Already receive 500 credits in `RecalculateAvailableBuffs` (BufferState.cs line 147)
- **Arcanist/PowerfulChange/ShareTransmutation**: Check caster-specific facts via `AbilityCache.CasterCache`. Pets without these features skip the checks naturally.

### Settings

No new toggle. Pets are always included when present. This matches the user's expectation that pets are normal party participants.

## Files Changed

| File | Change |
|------|--------|
| `BubbleBuffer.cs` (Bubble class, ~3088) | `Group` from property to cached field, add `RefreshGroup()` |
| `BubbleBuffer.cs` (~457) | Remove `BubbleBuffSpellbookController.Group`, replace usages with `Bubble.Group` |
| `BubbleBuffer.cs` (~2495) | Remove `GlobalBubbleBuffer.Group`, replace usages with `Bubble.Group` |
| `BufferState.cs` (~376) | Call `Bubble.RefreshGroup()` at start of `Recalculate()` |

## What Does NOT Change

- `BufferState.RecalculateAvailableBuffs()` — already iterates over the `Group` parameter
- `AddBuff()` filter logic — no pet-specific filtering needed
- Credit system — at-will abilities already handled
- Save/Load — UniqueId-based, pet IDs work transparently
- Portrait creation — iterates `Group.Count` dynamically
- `CastTask` / `EngineCastingHandler` — unit-agnostic
- Localization — no new UI strings

## Estimated Scope

~30-50 lines changed across 2 files. No new files.
