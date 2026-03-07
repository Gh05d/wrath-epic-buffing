# Scroll & Potion Support for BubbleBuffs

## Summary

Add automatic detection and usage of scrolls and potions as buff sources, alongside existing spell slots. Users can configure priority order (spells/scrolls/potions) globally and per-buff, with checkboxes to enable/disable each source type.

## Data Structures

### New Enum: `BuffSourceType`

```csharp
enum BuffSourceType { Spell, Scroll, Potion }
```

### BuffProvider Extensions

- `SourceType` — `BuffSourceType` (default: `Spell`)
- `SourceItem` — reference to the inventory item
- For potions: `clamp = 1` (self-cast only)
- For scrolls: UMD DC calculation + pre-filter

### SavedBufferState Extensions (Global Settings)

- `GlobalSourcePriority` — index into 6 predefined orderings
- `UmdRetries` — int 1-20 (slider)
- `UmdMode` — enum: `SafeOnly`, `AllowIfPossible`, `AlwaysTry`
- `ScrollsEnabled`, `PotionsEnabled` — global toggles

### SavedBuffState Extensions (Per-Buff)

- `SourcePriorityOverride` — nullable, overrides global default
- `ScrollCap`, `PotionCap` — nullable int, item consumption limit

## Priority System

Global default as dropdown with 6 options:

1. Spells > Scrolls > Potions
2. Spells > Potions > Scrolls
3. Scrolls > Spells > Potions
4. Scrolls > Potions > Spells
5. Potions > Spells > Scrolls
6. Potions > Scrolls > Spells

Per-buff override: same dropdown + "Use Global Default" option.

Implementation: `SortProviders()` sorts BuffProviders by `SourceType` weighted by priority order. Within same SourceType, existing sorting applies (CasterLevel etc.).

## Inventory Scanning

New block in `RecalculateAvailableBuffs` after the abilities block:

- Iterate `Game.Instance.Player.Inventory`
- Filter for `BlueprintItemEquipmentUsable` (scrolls and potions)
- Check if spell has known buffs via `GetBeneficialBuffs()`
- **Potions:** Create one `BuffProvider` per character that wants the buff, `SourceType = Potion`, `clamp = 1`
- **Scrolls:** Per character check:
  - Spell on class list: safe caster
  - UMD check possible (`UMD bonus + 20 >= DC`): UMD caster
  - Neither: skip
- Credits = item count in inventory (shared `ReactiveProperty<int>`)

## UMD Handling

### Pre-filter

Characters where `UMD bonus + 20 < DC` are excluded from the queue entirely. Natural 20 is NOT an automatic success on skill checks in Pathfinder 1e, so if the total can never reach the DC, the character cannot use the scroll.

### UMD Mode (Global Setting)

- **Safe Only:** Only characters with the spell on their class list can use scrolls
- **Allow if Possible:** UMD characters are included if they can theoretically pass the DC
- **Always Try:** All characters with UMD skill are included regardless

### Retry Handling

- Configurable retries: 1-20 (slider in settings)
- On UMD failure: scroll is NOT consumed, retry counter increments
- After max retries reached: skip this buff, log entry
- Counter resets per buff cycle

## Casting Logic

### BuffExecutor Changes

- Scroll cast: simulate UMD check, on failure retry up to limit
- Potion cast: target character drinks (self-cast)
- After cast: remove item from inventory
- Log warning when last item of a type is consumed
- Azata Zippy Magic, Share Transmutation, Powerful Change are disabled for item providers

## UI Changes

### Per-Buff Controls

- Checkboxes: [x] Spells  [x] Scrolls  [x] Potions
- Dropdown for priority override (or "Global Default")
- Optional cap input for scroll/potion consumption limit

### Caster List (BubbleSpellView)

- Small icon next to character name indicating source type:
  - Scroll icon for scroll providers
  - Potion icon for potion providers
  - No icon for regular spells
- Available count displayed (e.g. "Seelah [scroll] x3")

### Global Settings Panel

- Dropdown: default source priority
- Slider: UMD retries (1-20)
- Dropdown: UMD mode
- Checkboxes: enable scrolls / enable potions

## Edge Cases

- **Same buff from spell + scroll:** Separate providers in the same `BubbleBuff`, priority decides order
- **Items exhausted during buff cycle:** Next provider in queue takes over
- **Potion buff is self-only:** Correct — potions always have `clamp = 1`
- **Scrolls with variants:** Expanded like normal spells
- **Feature interactions:** Azata/ShareTransmutation/PowerfulChange flags forced to false for item providers
- **Shared inventory:** Scrolls share a single credit pool across all characters using them
