# Caster Source Tooltip Design

## Problem

Users see characters listed as casters for buffs they don't have prepared in their spellbook (e.g., Seelah listed for Bull's Strength). This happens because the mod correctly includes scroll/wand/potion sources, but the UI doesn't make the source type obvious enough. The existing source-overlay icon on caster portraits is too small and lacks textual explanation.

## Solution: Approach A — Larger Overlay + Rich Tooltip

Improve source-type visibility in the detail view through two changes:

1. **Enlarge the source-overlay icon** on caster portraits with a semi-transparent background
2. **Add a hover tooltip** on caster portraits showing source details

## Design

### 1. Enlarged Source Overlay

**Current:** Overlay anchors at `(0.55, 0.0)` to `(1.0, 0.35)` — small, no background, easy to miss.

**New:**
- Anchor: `(0.5, 0.0)` to `(1.0, 0.45)` — 50x45% of portrait
- Add a semi-transparent black background behind the icon (`Color(0, 0, 0, 0.6f)`) for contrast against portrait artwork
- Implementation: Insert a new `Image` sibling before the SourceOverlay with the same anchors, using a rounded/circle sprite if available, otherwise a plain filled image

**Location:** Portrait construction in `BubbleBuffer.cs` around line 387-394.

### 2. Hover Tooltip on Caster Portraits

**Current:** Caster portraits have no tooltip. Only an `OnHover` handler for border sprite swap.

**New:** Set a `TooltipTemplateSimple(title, body)` on each caster portrait's `OwlcatButton` in `UpdateCasterDetails`.

**Tooltip content by source type:**

| Source Type | Header | Body |
|---|---|---|
| Spell | Character name | "{Spellbook} — Level {N} — {X} slots remaining" |
| Scroll | Character name | "Scroll — {ItemName} — {X} remaining" |
| Potion | Character name | "Potion — {ItemName} — {X} remaining" |
| Equipment | Character name | "{ItemName} — {X} charges remaining" |
| Song | Character name | Handled by existing `TooltipTemplateActivatableAbility` |

**UMD line:** If the caster doesn't have the spell on their class list (i.e., using the item via Use Magic Device), append: "Requires Use Magic Device (DC {X})". The DC is `20 + CasterLevel`, available from the source item.

**Implementation:**
- New private method `BuildCasterTooltip(BuffProvider provider)` in `BufferView` returning a formatted body string
- Called from `UpdateCasterDetails` for each visible caster portrait
- Uses `TooltipTemplateSimple(characterName, body)` via `button.SetTooltip()`
- Song caster portraits use `TooltipTemplateActivatableAbility` instead

### 3. Data Sources

All required data is already on `BuffProvider`:
- `who` — character (name via `who.CharacterName`)
- `SourceType` — Spell/Scroll/Potion/Equipment/Song
- `SourceItem` — concrete item (name, charges) for non-spell sources
- `book` — spellbook reference (name)
- `spell` — AbilityData (spell level)
- `AvailableCredits` / `spent` — remaining and assigned credits

UMD detection: `BufferState.CanUseItemWithUmd()` already checks class spell list vs. UMD. The tooltip needs to replicate just the class-list check to decide whether to show the UMD line.

### 4. Localization

New keys in locale files:

| Key | en_GB | de_DE |
|---|---|---|
| `tooltip.source.spell` | `"Spell — {0} (Level {1})"` | `"Spell — {0} (Level {1})"` |
| `tooltip.source.scroll` | `"Scroll — {0}"` | `"Scroll — {0}"` |
| `tooltip.source.potion` | `"Potion — {0}"` | `"Potion — {0}"` |
| `tooltip.source.equipment` | `"Equipment — {0}"` | `"Equipment — {0}"` |
| `tooltip.source.charges` | `"{0} charges remaining"` | `"{0} Ladungen verbleibend"` |
| `tooltip.source.stacks` | `"{0} remaining"` | `"{0} verbleibend"` |
| `tooltip.source.umd` | `"Requires Use Magic Device (DC {0})"` | `"Benötigt Use Magic Device (DC {0})"` |

Only `en_GB` and `de_DE` get full translations. Other locales fall back to `en_GB` (existing pattern).

## Scope

**In scope:**
- Source overlay enlargement + background
- Hover tooltip on caster portraits in detail view
- New locale keys

**Out of scope:**
- Buff list (before selecting a buff) — no source indicators there
- Bless "Mass" tag confusion — separate issue
- Color coding or colored borders by source type
- Steam Deck touch tooltip support

## Affected Files

- `BubbleBuffer.cs` — Portrait construction (overlay resize + background), `UpdateCasterDetails` (tooltip binding), new `BuildCasterTooltip` method
- `Config/en_GB.json` — new tooltip keys
- `Config/de_DE.json` — new tooltip keys (technical terms stay English)
