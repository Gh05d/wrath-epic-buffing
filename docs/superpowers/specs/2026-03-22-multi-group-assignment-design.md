# Multi-Group Buff Assignment

## Problem

Buffs can currently only be assigned to one BuffGroup (Normal, Important, Quick). Some buffs need to be in multiple groups — e.g., Shield should be in both Quick and Normal because short-duration characters need it recast via Quick while long-duration characters skip it (already active).

## Behavior

- A buff can belong to zero or more groups (checkboxes, not exclusive buttons).
- All groups unchecked = buff is never cast (effectively paused).
- New buffs default to Normal only.
- When executed, a buff is cast once per trigger. If already active on a target from a previous group trigger, it is skipped.
- A buff counts in the summary label of every group it belongs to.
- HUD buttons, shortcuts, and execution flow remain per-group — no changes there.

## Data Model

### BubbleBuff (BubbleBuff.cs)

Replace `InGroup` field:

```csharp
// Old
public BuffGroup InGroup = BuffGroup.Long;

// New
public HashSet<BuffGroup> InGroups = new HashSet<BuffGroup> { BuffGroup.Long };
```

### SavedBuffState (SaveState.cs)

Add new field, keep old for backward compatibility:

```csharp
[JsonProperty]
public BuffGroup InGroup; // Legacy — read during deserialization for migration

[JsonProperty]
[JsonConverter(typeof(StringEnumConverter))] // Serialize as ["Long","Quick"] not [0,2]
public HashSet<BuffGroup> InGroups; // New — written on save
```

Note: `StringEnumConverter` ensures enum values are stored as names, not integers. This prevents save corruption if `BuffGroup` enum values are ever reordered.

### Migration (BubbleBuff.InitialiseFromSave)

```
if InGroups is null:
    InGroups = { InGroup }   // migrate from legacy single value
```

Only trigger on `null`, not empty. An empty `HashSet` is an intentional user choice ("buff paused, in no group") and must be preserved.

Old saves load correctly via the legacy `InGroup` field. After first save, `InGroups` is written. For downgrade compatibility, `InGroup` is also written (set to `InGroups.FirstOrDefault()` or `BuffGroup.Long` if empty) so older mod versions get a reasonable default.

### Save Write Path (BufferState.Save)

The `updateSavedBuff` local function (BufferState.cs:438) currently writes `save.InGroup = buff.InGroup`. Change to:

```csharp
save.InGroups = new HashSet<BuffGroup>(buff.InGroups);
save.InGroup = buff.InGroups.Count > 0 ? buff.InGroups.First() : BuffGroup.Long; // backward compat
```

### Save Cleanup Condition (BufferState.cs:481-484)

The cleanup condition currently removes saved entries when a buff has no wanted targets, no overwrite ignores, and isn't blacklisted. With multi-group, a buff with non-default group assignments must also be preserved even if it has no wanted targets yet.

```csharp
// Old cleanup (line 481)
if (save.Wanted.Empty() && save.IgnoreForOverwriteCheck.Empty() && !buff.HideBecause(HideReason.Blacklisted))

// New cleanup — also preserve non-default group assignments
static readonly HashSet<BuffGroup> DefaultGroups = new() { BuffGroup.Long };

if (save.Wanted.Empty() && save.IgnoreForOverwriteCheck.Empty()
    && !buff.HideBecause(HideReason.Blacklisted)
    && buff.InGroups.SetEquals(DefaultGroups))
```

The entry creation condition (line 484) needs the same extension:

```csharp
// Old
} else if (buff.Requested > 0 || buff.IgnoreForOverwriteCheck.Count > 0 || buff.HideBecause(HideReason.Blacklisted)) {

// New — also create entry for non-default group assignments
} else if (buff.Requested > 0 || buff.IgnoreForOverwriteCheck.Count > 0
           || buff.HideBecause(HideReason.Blacklisted)
           || !buff.InGroups.SetEquals(DefaultGroups)) {
```

## Execution Filter

### BuffExecutor.Execute (BuffExecutor.cs:187)

```csharp
// Old
.Where(b => b.InGroup == buffGroup && b.Fulfilled > 0)

// New
.Where(b => b.InGroups.Contains(buffGroup) && b.Fulfilled > 0)
```

### Summary Labels (BubbleBuffer.cs:2932)

```csharp
// Old
.Where(b => b.InGroup == group)

// New
.Where(b => b.InGroups.Contains(group))
```

A buff in multiple groups is counted in each group's summary.

## UI Changes

### Buff Detail Panel (BubbleBuffer.cs:1633-1675)

Replace `ButtonGroup<BuffGroup>` (exclusive toggle buttons) with 3 independent `ToggleWorkaround` checkboxes:

- Reuse the existing `MakeSourceToggle()` local function (defined inside `MakeDetailsView`) for consistent look with the Use Spells/Scrolls/Potions toggles below.
- Same container: HorizontalLayoutGroup in `actionBarSection`, same position and sizing.
- Each checkbox toggles its BuffGroup in `buff.InGroups` (Add/Remove) and calls `state.Save()`.
- Remove the `ButtonGroup<BuffGroup> buffGroup` variable and its `Selected.Subscribe` handler (lines 1650-1675).
- Remove anchor-fix loop (lines 1657-1668) — not needed for toggle-based checkboxes.

### UpdateDetailsView Sync (BubbleBuffer.cs:1701)

Remove the old `buffGroup.Selected.Value = buff.InGroup` line. Replace with:

```
For each group checkbox:
    toggle.isOn = buff.InGroups.Contains(group)
```

Note: Setting `toggle.isOn` programmatically fires `onValueChanged`. The existing source toggles (Use Spells, etc.) already have this pattern and it's idempotent (Add on already-present item, Remove on already-absent item both no-op). Redundant `Save()` calls are acceptable — same behavior as existing toggles.

## Validation & Credits

No changes. The credit system validates all buffs regardless of group membership. `Recalculate()` runs before each `Execute()` call, detecting which targets still need the buff. Multi-group assignment does not affect credit consumption — a buff is still one `BubbleBuff` object with one credit pool.

## Localization

Existing keys `group.normal.btn`, `group.important.btn`, `group.short.btn` work as-is for checkbox labels. No new keys needed.

## No Changes Required

- HUD buttons (3 buttons, one per group)
- Keyboard shortcuts (per-group bindings)
- BuffGroup enum values
- Buff scanning / RecalculateAvailableBuffs
- CasterQueue / BuffProvider logic
- Mass spell validation
