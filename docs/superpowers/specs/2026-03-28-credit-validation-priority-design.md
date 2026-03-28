# Credit Validation Priority Design

## Problem

Spontaneous casters (e.g., Oracle) share a single credit pool per spell level across all known spells. When `Validate()` runs during `Execute(BuffGroup)`, it validates ALL buffs across all groups, consuming credits for buffs that won't be cast. This means a caster assigned to cast Shield of Faith (Normal group) and Unbreakable Heart (Quick group) has their shared level-1 credits split between both — but only Normal is executed, wasting the Quick allocation.

A previous attempt to filter `Invalidate()`/`Validate()` to only the executing group failed because non-validated buffs were left in broken state, causing NullReferenceExceptions in `PreviewReceivers` and other UI code that assumes all buffs are always valid.

## Solution: Validation Priority Ordering

Instead of filtering which buffs are validated, change the **order** in which they are validated. The executing group is validated first, getting first dibs on shared credit pools. Other groups are validated after, receiving whatever credits remain.

## Design

### `BufferState.Recalculate(bool updateUi, BuffGroup? priorityGroup = null)`

- When `priorityGroup` is set: sort `BuffList` before the Invalidate/Validate loops so that buffs containing `priorityGroup` in their `InGroups` come first
- `Invalidate()` and `Validate()` run over ALL buffs as before — no filtering
- All buffs remain in valid state at all times

### `BuffExecutor.Execute(BuffGroup buffGroup)`

- Line 168: `State.Recalculate(false)` → `State.Recalculate(false, buffGroup)`

### No changes to

- `Validate()` / `ValidateMass()` / `Invalidate()` methods themselves
- UI-triggered `Recalculate(true)` calls (no priority group — current behavior preserved)
- `PreviewReceivers`, `OnUpdate`, or any other UI code

## Affected Files

- `BufferState.cs` — `Recalculate()` signature + pre-validation sort
- `BuffExecutor.cs` — pass `buffGroup` to `Recalculate()`

## Out of Scope

- Refactoring the credit system (shared ReactiveProperty pools)
- Changing how the UI displays credits across groups
- Per-group credit isolation
