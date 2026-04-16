# Magic Deceiver Fused Spell Support

## Problem

The Magic Deceiver (Arcanist archetype) has a "Magic Fusion" feature that combines two spells into one fused spell. These fused spells don't appear in the mod's buff list — a long-standing issue since the original BubbleBuffs.

## Root Cause

Fused spells are stored via `Spellbook.AddCustomSpell()` and live in `GetCustomSpells()`. The MagicDeceiver spellbook is `Spontaneous: true, IsArcanist: false`, so the Spontaneous scanning branch finds them correctly.

The rejection happens in `AddBuff()` at the `GetBeneficialBuffs()` filter. Fused spells use template blueprints (`MagicHackDefaultSlot1-10`, `MagicHackTouchSlot1-10`) that are empty shells:

- `AbilityEffectRunAction.Actions` is empty — no beneficial effects detected
- `m_DisplayName` is empty, `m_Icon` is null
- `EffectOnEnemy: Harmful`, all `CanTarget*: false`

The actual spell data lives in `AbilityData.MagicHackData` at runtime:

- `MagicHackData.Spell1` / `Spell2` — the two component spell blueprints
- `MagicHackData.Name` — user-given fusion name
- `MagicHackData.DeliverBlueprint` — the component spell that determines targeting
- `MagicHackData.SpellLevel` — max(level1, level2)

The game engine routes `AbilityData.Name`, `CanTargetAlly`, `CanTargetEnemies`, `TargetAnchor`, and `GetDeliverBlueprint()` through `MagicHackData` when present. The mod's static blueprint analysis bypasses this.

## Design

### Change 1: MagicHackData check in AddBuff() (BufferState.cs)

Before the `GetBeneficialBuffs()` call, check if the spell is a fused spell:

```
if spell.MagicHackData != null:
    check Spell1 and Spell2 blueprints via GetBeneficialBuffs()
    if at least one component has beneficial effects: accept the spell
    else: reject as usual
else:
    existing logic unchanged
```

This only fires when `MagicHackData != null` — zero impact on non-fused spells.

### Change 2: Icon fix in BubbleBuff (BubbleBuff.cs)

Current code: `Spell?.Blueprint?.Icon` — accesses template blueprint icon (null for fused spells).

Fix: for fused spells, use `Spell.Icon` which goes through `AbilityData.get_Icon()` → `GetDeliverBlueprint().Icon`, returning the component spell's icon.

```csharp
public Sprite Icon => IsActivatable ? ActivatableSource.Blueprint.Icon
    : (Spell?.MagicHackData != null ? Spell.Icon : Spell?.Blueprint?.Icon);
```

### What doesn't need changes

- **Display name**: `AbilityData.Name` checks `MagicHackData` first, returns user-given name or auto-generated default. `BubbleBuff.Name` uses `Spell.Name` which already works.
- **Targeting**: `CanTargetAlly`, `CanTargetEnemies`, `TargetAnchor` all route through `GetDeliverBlueprint()` which returns `MagicHackData.DeliverBlueprint`. `SelfCastOnly` and `BuffProvider.CanTarget()` work correctly.
- **Credits**: Fused spells share the Spontaneous per-level credit pool, already handled by the existing Spontaneous scanning branch.
- **Casting**: Fused spells are valid `AbilityData` instances. The game engine handles `MagicHackData` transparently during cast execution. Both `AnimatedExecutionEngine` and `InstantExecutionEngine` should work.
- **Save/Load**: `BuffKey` uses template blueprint GUID. If the player reconfigures a fusion slot, saved target assignments become stale but don't crash — same behavior as when any spell is swapped out.

### Behavior for mixed fusions

When one component is a buff (e.g., Haste) and the other is damage (e.g., Fireball): the fused spell appears in the buff list if at least one component has beneficial effects. The damage part happens as a bonus at cast time.

## Files Modified

| File | Change |
|---|---|
| `BuffIt2TheLimit/BufferState.cs` | MagicHackData check in `AddBuff()` before `GetBeneficialBuffs()` |
| `BuffIt2TheLimit/BubbleBuff.cs` | Icon property: use `Spell.Icon` for fused spells |

## Testing

Requires a Magic Deceiver character with configured fused buff spells (e.g., Haste + Shield). Verify:

1. Fused buff spells appear in the mod's buff list
2. Correct name and icon displayed
3. Targeting works (can assign party members)
4. Casting executes correctly (buff applied, game doesn't crash)
5. Non-fused spells unaffected
