# Extend Rod Support — Design Spec

**Date:** 2026-03-15
**Status:** Approved

## Summary

Add support for automatically applying Extend Spell metamagic rods when casting buffs. Users enable a per-buff toggle; the mod finds the weakest suitable rod in the shared party inventory, applies the Extend metamagic to the spell, and consumes a rod charge. If no rod is available, the spell is cast normally with a log message.

## Requirements

- Per-buff checkbox to toggle "Use Extend Rod"
- Automatic rod tier selection: Lesser (≤3) → Normal (≤6) → Greater (≤9), weakest first
- Rods sourced from shared party inventory (not QuickSlot-restricted)
- Graceful fallback: no rod available → cast normally + log warning
- Logging: verbose log on success, standard log on fallback, tooltip suffix `[Extend]`
- Extensible design for future metamagic rod types (Empower, Maximize, etc.)

## Design

### Data Model

**`SavedBuffState`** — new persisted field:

```csharp
[JsonProperty]
public bool UseExtendRod;
```

Per-buff, not per-caster — rods come from the shared inventory and are not bound to a specific caster.

**`MetamagicRodType` enum** — prepared for future extensibility:

```csharp
public enum MetamagicRodType {
    None = 0,
    Extend = 1,
    // Empower, Maximize, Quicken, etc. — future
}
```

Only `Extend` is implemented now. When more rod types are added, `UseExtendRod` will migrate to a `MetamagicRodType` field or list.

### Rod Discovery & Selection

New method in `BufferState` (or dedicated helper): `FindBestExtendRod(int spellLevel)`

1. Scan `Game.Instance.Player.Inventory` for items whose blueprint has a `MetamagicRodComponent` with `Metamagic.Extend`
2. Filter by `MaxSpellLevel >= spellLevel` (Lesser: ≤3, Normal: ≤6, Greater: ≤9)
3. Filter by `Charges > 0`
4. Sort ascending by `MaxSpellLevel` — pick the weakest rod that fits
5. Return the item, or `null` if none available

**Charge tracking:** Since multiple buffs in the same `Execute()` pass may consume rod charges, `BuffExecutor` maintains a local `Dictionary<ItemEntity, int> remainingRodCharges` (analogous to the existing `remainingArcanistPool`). Built and decremented per `Execute()` pass.

**Spell level:** Uses the base spell level (without metamagic cost increase), as rods in WotR check the unmodified level.

### CastTask Extension

New field on `CastTask`:

```csharp
public Kingmaker.Items.ItemEntity MetamagicRodItem; // null = no rod
```

### BuffExecutor.Execute() Changes

When creating a `CastTask`:

1. Check if `buff.SavedState.UseExtendRod == true`
2. If yes, call `FindBestExtendRod(spellLevel)` (respecting local `remainingRodCharges`)
3. If rod found: set `task.MetamagicRodItem`, decrement `remainingRodCharges`
4. If no rod: continue with normal cast, log warning

### EngineCastingHandler Changes

**Constructor** (before actual cast):
- If `_castTask.MetamagicRodItem != null`, add `Metamagic.Extend` to `_castTask.SpellToCast.MetamagicData`

**`HandleExecutionProcessEnd()`:**
- Consume rod charge via `_castTask.MetamagicRodItem.Charges--` (analogous to existing equipment charge consumption at line 126-127)

Direct MetamagicData modification is more reliable than activating the rod's ability and relying on the game's internal buff timing — especially in `InstantExecutionEngine` where no frame delay is possible.

**Open point (implementation):** Verify whether `MetamagicData` can be set directly on an `AbilityData` or whether a new `MetamagicData` object must be created. To be resolved during first build test.

### UI

Toggle placed in the **left side of the Source Controls Section** (`prioSideObj`), below the Source Priority text.

```
Source Controls Section
├── Left (55%): VLG (new — currently just a text+button)
│     Source Priority: Spells > Scrolls > Potions (clickable)
│     ☐ Use Extend Rod   ← NEW
└── Right (45%): VLG with toggles
      ☑ Use Spells
      ☑ Use Scrolls
      ☑ Use Potions
      ☑ Use Equipment
```

The left side (`prioSideObj`) is restructured from a single anchored text to a small VLG containing the priority text on top and the Extend Rod toggle below. Toggle uses the same pattern as the right-side source toggles (0.7f scale, `MakeSourceToggle` style).

**Behavior:**
- State read from `buff.SavedState.UseExtendRod`, saved on change + `state.Save()`
- Always visible when a buff is selected (like other source toggles)
- `UpdateDetailsView` sets toggle value on buff change

**Localization:** New key `"use.extendrod"` in all 5 locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`).

### Logging

| Situation | Method | Message |
|---|---|---|
| Rod assigned to cast | `Main.Verbose` | `"Extend Rod applied: {rodItem.Name} for {buff.Name}"` |
| No rod available, fallback | `Main.Log` | `"Extend Rod unavailable for {buff.Name}, casting normally"` |
| Rod charge consumption error | `Main.Error` | Exception details |
| Tooltip (CombatLog) | `BuffResult` | Suffix `[Extend]` on successful rod casts |

## Out of Scope

- Other metamagic rod types (Empower, Maximize, etc.) — enum prepared but not implemented
- Rod availability indicator in the UI — checked only at cast time
- QuickSlot restriction — rods from shared inventory, not QuickSlot-bound
