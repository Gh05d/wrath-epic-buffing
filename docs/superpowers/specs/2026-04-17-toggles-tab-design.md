# Toggles Tab (Free-Toggle Activatable Abilities)

**Date:** 2026-04-17
**Status:** Draft

## Problem

Some activatable abilities turn off unexpectedly during normal play and the player forgets to re-enable them. Reported cases:

- **Area-transition deactivation:** Shifter Claws, Kinetist Wild Talent stat buffs
- **Action-economy deactivation:** Arcane Strike (swift action interactions), Come and Get Me (barbarian/skald), certain Monk stances

These abilities share one property: they are free-toggle activatables (no `ActivatableAbilityResourceLogic` component — they cost nothing to keep on). They are currently filtered out of the activatable scan in `BufferState.RecalculateAvailableBuffs()` (the if-chain ends at `else if (hasResourceLogic)` with no trailing else) and never appear in the buff window.

The prior design (`2026-04-09-activatable-abilities-design.md`) explicitly left non-resource-based toggles out of scope. This spec picks up that thread.

## Solution

Add a new `Toggles` tab, positioned after `Songs` in the tab bar. The tab lists every free-toggle activatable ability the party knows. The user opts in per ability (same model as the buff tab). Selected toggles fire via the existing `ExecuteCombatStart()` pipeline and can be assigned to `BuffGroup.Long/Important/Quick` like any other buff.

No new detection heuristics, no curated ability list, no mid-combat polling.

## Design

### Scope

Include every `ActivatableAbility` on every party member where:

- `srcItem == null` (class-granted, not item-backed — item-backed activatables already go to the Equipment tab)
- `!PerformanceGroups.Contains(blueprint.Group)` (not a Bard/Skald/Azata performance — those are the Songs tab)
- `blueprint.GetComponent<ActivatableAbilityResourceLogic>() == null` (no resource cost — if it has one, it already goes to the existing Ability tab)

No further filtering. Power Attack, Wings, Combat Styles, and similar always-on feats will appear in the list; the user simply leaves them unchecked.

### Data Model

**`Category` enum** (in `BubbleBuff.cs` / scanning code):
- New value `Toggle`, ordered after `Song`.

**`BuffSourceType` enum:**
- No new value needed. Reuse `BuffSourceType.Activatable` from the prior activatable-abilities design — the source is the same (an `ActivatableAbility` fact), only the category differs.

**`SavedState` (in `SaveState.cs`):**
- New `TogglesEnabled` bool, default `false`.
- Default `false` is deliberate: existing saves should not see a new populated tab the next time they load. User opts in via the settings toggle.

**`BubbleBuff`:**
- No new fields. Existing `IsActivatable`, `ActivatableSource`, `ActivatableGroup` already cover the runtime needs (populated by `AddActivatable()`).

### Scanning (BufferState.RecalculateAvailableBuffs)

Extend the activatable scan loop at `BufferState.cs:375-402`. After the existing Song and resource-Ability branches, add a final `else`:

```csharp
} else if (hasResourceLogic) {
    // existing resource-Ability branch
} else {
    if (!SavedState.TogglesEnabled) continue;
    Main.Verbose($"      Adding toggle: {blueprint.Name} (group={blueprint.Group}) for {dude.CharacterName}", "state");
    AddActivatable(dude, activatable, characterIndex, Category.Toggle);
}
```

`AddActivatable()` signature stays unchanged. It already handles `Category.Song`, `Category.Ability`, and `Category.Equipment`; adding `Category.Toggle` is a passthrough — the method constructs a `BubbleBuff` with `IsActivatable = true` and the passed `Category`.

### Execution

Reuse the existing Phase 0 activatable-activation path in `BuffExecutor.ExecuteCombatStart()`. The current loop already iterates `State.BuffList` where `b.IsActivatable`; no branching on `Category` is needed — toggles flow through the same code.

Per-toggle activation semantics:

```csharp
if (!activatable.IsOn && activatable.IsAvailable) {
    activatable.IsOn = true;
    if (!activatable.IsStarted) activatable.TryStart();
}
```

Matches the pattern documented in CLAUDE.md ("`IsOn = true` does NOT start the ability" — must also call `TryStart()`).

**HUD button firing:** Toggles assigned to `BuffGroup.Long/Important/Quick` activate when the corresponding HUD button is pressed. Same code path — no new integration.

**Mutual exclusivity:** Handled identically to the existing activatable system (tracked via `ActivatableAbilityGroup`, last selection wins per caster+group). Crane Style vs Crane Riposte, competing Monk stances, etc., fall out naturally. No new logic.

**Round-limit / deactivation:** Toggles are meant to stay on permanently. The round-limit slider exposed for other activatables is hidden for `Category.Toggle` entries.

### UI

**Tab bar:**
- Fifth tab, positioned after Songs.
- Label: `"Toggles"` (English, same string in de_DE per existing convention for technical UI terms).
- Icon: reuse an existing game sprite (gear/fist/toggle). No new asset file unless a suitable sprite cannot be found.

**Tab content rendering:**
- Identical to Songs: per-character portrait grid, checkbox per character, combat-start checkbox per entry.
- BuffGroup assignment chips (Long/Important/Quick) present and functional.
- Round-limit slider hidden (see Execution).

**Settings panel:**
- New toggle: `"Toggles"` (localization key `toggles.settings.label`, description key `toggles.settings.description`).
- Bound to `SavedState.TogglesEnabled`. Default off.
- Toggling on triggers a recalculation so the new tab populates.

### Localization

New keys in `Config/en_GB.json` and `Config/de_DE.json` only (other locales fall back to EN):

- `toggles.tab.label` — `"Toggles"`
- `toggles.settings.label` — `"Toggles"`
- `toggles.settings.description` — one-line explanation that enabling this adds free-toggle abilities (Power Attack, Shifter Claws, Monk Stances, etc.) to a new tab.

Per user preference, the German strings keep the English word `"Toggles"` for the label.

### Affected Files

| File | Changes |
|---|---|
| `BubbleBuff.cs` | Add `Category.Toggle` enum value after `Song` |
| `BufferState.cs` | New `else` branch in activatable scan loop; pass `Category.Toggle` to `AddActivatable` |
| `SaveState.cs` | Add `TogglesEnabled` bool (default false) |
| `BubbleBuffer.cs` | Fifth tab button after Songs, wire to `Category.Toggle`; settings-panel checkbox for `TogglesEnabled`; hide round-limit slider for `Category.Toggle` entries |
| `Config/en_GB.json` | Three new keys above |
| `Config/de_DE.json` | Three new keys above (English label preserved) |

No changes to `BuffExecutor.cs`, `EngineCastingHandler.cs`, `AnimatedExecutionEngine.cs`, `InstantExecutionEngine.cs` — execution reuses existing Phase 0 path.

### Out of Scope

- Mid-combat re-enable polling for abilities that deactivate during combat (Arcane Strike swift-action case). User reactivates manually via HUD button.
- Curated "known auto-off" list or heuristic detection. Opt-in model makes this unnecessary.
- Item-backed free-toggle activatables (already routed to Equipment tab; not changed).
- Separate re-enable HUD button. Existing BuffGroup HUD buttons cover this when toggles are assigned to a group.
- Per-area-transition auto-reactivation. Combat-start is the only trigger.
