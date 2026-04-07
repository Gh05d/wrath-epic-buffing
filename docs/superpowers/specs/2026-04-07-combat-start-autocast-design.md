# Combat Start Auto-Cast

## Goal

Automatically cast configured buffs and activate abilities (songs, enchantments) when combat begins, using full animation for balance.

## Data Model

- New `bool CastOnCombatStart` field on `BubbleBuff` (runtime) and `SavedBuffState` (persistence)
- Independent of BuffGroup — a buff can be in "Normal" AND marked for combat-start
- Available for all categories: Spells, Songs, Abilities, Equipment

## UI

- New checkbox in the buff detail panel (right side), placed alongside existing toggles (Use Extend Rod, Use Spells, etc.)
- Label: "Cast on combat start" / localized equivalent
- New localization keys in all locale files (en_GB, de_DE, fr_FR, ru_RU, zh_CN)

## Execution

### New Method: `BuffExecutor.ExecuteCombatStart()`

Separate from the existing `Execute(BuffGroup)` path:

1. Calls `State.Recalculate()` to refresh available buffs
2. Iterates all buffs across all groups (not filtered by BuffGroup)
3. Filters for `CastOnCombatStart == true`
4. Phase 0: Activates songs via `ActivatableAbility.IsOn = true` (existing pattern)
5. Phase 1: Builds CastTask list from remaining spells/abilities
6. Forces `AnimatedExecutionEngine` regardless of VerboseCasting setting
7. Bypasses `AllowInCombat` check (animation ensures balance)
8. Logs results to combat log (existing pattern)

### Buff Validation

No new validation needed. The existing pipeline handles everything:

- `AbilityCombinedEffects.IsPresent()` skips buffs already active on targets
- `BubbleBuff.Validate()` checks spell slots, charges, arcanist pool
- Mass/communal spells use `ValidateMass()` which checks per-target presence
- `OverwriteExistingBuffs` setting (default: false) is respected

### Combat Start Hook

Modify `HideBubbleButtonsWatcher.HandlePartyCombatStateChanged(bool inCombat)` in `BubbleBuffer.cs`:

- When `inCombat == true`: start a coroutine that yields one frame, then calls `ExecuteCombatStart()`
- The one-frame delay ensures the game has fully initialized combat state
- `HideBubbleButtonsWatcher` already implements `IPartyCombatHandler` and is subscribed via EventBus

## Save/Load

Follows existing pattern:
- `SavedBuffState.CastOnCombatStart` serialized via Newtonsoft.Json
- `BubbleBuff.InitialiseFromSave()` reads from saved state
- `BufferState.Save()` writes to saved state

## Balance

- Always uses `AnimatedExecutionEngine` — characters spend their action casting
- Normal spell slot / charge consumption applies
- Already-active buffs are skipped (no wasted resources)
- No bypass of any game mechanic except the mod's own `AllowInCombat` gate

## Files to Modify

| File | Change |
|------|--------|
| `BubbleBuff.cs` | Add `CastOnCombatStart` field, wire in `InitialiseFromSave()` |
| `SaveState.cs` | Add `CastOnCombatStart` to `SavedBuffState` |
| `BufferState.cs` | Save the new field in `Save()` |
| `BuffExecutor.cs` | Add `ExecuteCombatStart()` method |
| `BubbleBuffer.cs` | Add checkbox in detail panel, modify `HandlePartyCombatStateChanged` |
| `Config/en_GB.json` | Add localization key |
| `Config/de_DE.json` | Add localization key |
| `Config/fr_FR.json` | Add localization key |
| `Config/ru_RU.json` | Add localization key |
| `Config/zh_CN.json` | Add localization key |
