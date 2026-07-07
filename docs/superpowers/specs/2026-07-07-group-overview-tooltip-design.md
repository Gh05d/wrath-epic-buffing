# Group Overview Tooltip — Design

**Date:** 2026-07-07
**Origin:** Nexus user request — "Is there a way to view what spells/skills are included in different categories of buffs? (normal/quick/important)" — currently the only way to find out is clicking through every buff's detail panel or firing the group and wasting the casts.

## Goal

Hovering a buff category's UI element shows a live list of the buffs currently assigned to that category (`BuffGroup.Long`/"Normal", `BuffGroup.Quick`/"Quick", `BuffGroup.Important`/"Important").

## Approach (chosen)

Lazy custom tooltip template. A new `TooltipTemplateGroupBuffs : TooltipBaseTemplate` builds its brick list at hover time from the live `BufferState`, so no refresh bookkeeping is needed — the list is always current after toggling checkboxes, save/load, or rescans. Precedent: `TooltipTemplateBuffer` (BubbleBuffer.cs:2598) already renders icon+name brick lists the same way.

Rejected alternatives:
- **Eager text refresh** (re-call `SetTooltip` with rebuilt `TooltipTemplateSimple` on every assignment change): refresh hooks in several places, stale-button hazard after save/load (gotchas-ui.md), no icons.
- **Dedicated overview panel/tab in the buff window**: much more UI work; hover solves the reported problem. Can still be added later if users want management actions (e.g. click-to-jump).

## Components

### 1. `TooltipTemplateGroupBuffs : TooltipBaseTemplate`

- Constructor takes only the `BuffGroup`. No UI state captured.
- `GetHeader`: existing localized header (`group.<x>.tooltip.header` — note enum→key mapping: `Long`→`normal`, `Quick`→`short`, `Important`→`important`).
- `GetBody`, built fresh per call:
  1. Existing description text (`group.<x>.tooltip.desc`) as `TooltipBrickText`.
  2. `TooltipBrickSeparator`.
  3. One `TooltipBrickIconAndName(buff.Icon, buff.NameMeta, TooltipBrickElementType.Small)` per assigned buff — `NameMeta` includes metamagic tags. Query: `state.BuffList.Where(b => b.InGroups.Contains(group))`, sorted alphabetically by `Name`.
- State is resolved at hover time via `GlobalBubbleBuffer.Instance.SpellbookController.state` (same path the HUD buttons use for `Execute(group)`).

### 2. Integration points

- **HUD buttons** (BubbleBuffer.cs:3067-3069): the three group buttons get `TooltipTemplateGroupBuffs` instead of the current static `TooltipTemplateSimple`. The map and open-buffs buttons keep their simple tooltips.
- **Buff-window summary labels** (`MakeSummary`, BubbleBuffer.cs:3825): each per-group `TextMeshProUGUI` label gets `TooltipHelper.SetTooltip(label, new TooltipTemplateGroupBuffs(group), config)`; set `raycastTarget = true` explicitly. `TooltipHelper.SetTooltip(MonoBehaviour, TooltipBaseTemplate, TooltipConfig)` is IL-verified to construct its own `TooltipHandler`, so it works on non-button elements.
- `TooltipConfig`: `InfoCallPCMethod.None`, matching the existing HUD button tooltips.

## Edge cases

- `BuffList == null` (before the first scan) or group empty → single line "No buffs assigned" (new locale key `group.overview.empty`).
- Late-game list length: cap at 25 entries, then "… and {0} more" (new locale key `group.overview.more`).
- A buff assigned to several groups appears in each group's list (intended).
- Both new keys go into **all five** locale files (en_GB, de_DE, fr_FR, ru_RU, zh_CN); preserve per-file BOM state; de_DE keeps English gaming terms.

## Known risk

Assumption: the game calls the template's `GetBody()` fresh on every tooltip show (templates are designed for lazy building). Verified first on the Deck: toggle a group checkbox, hover again without reopening the window. If the engine caches after all, fallback is re-calling `SetTooltip` on window open / after toggles — a small addition that doesn't change the design.

## Testing

Build → `./deploy.sh` → full game restart (UMM hot-reload leaves stale delegates, gotchas-ui.md) → verify:
1. Hover on all three HUD buttons shows the correct lists.
2. Hover on all three summary labels in the buff window shows the same lists.
3. Toggle a group checkbox, hover again without reopening — list reflects the change (lazy-rebuild proof).
4. Save/load, hover again — no stale-object exceptions.
5. Empty group shows the "No buffs assigned" line.
6. Group with >25 buffs shows the cap line (can be simulated by lowering the cap in a debug build).
