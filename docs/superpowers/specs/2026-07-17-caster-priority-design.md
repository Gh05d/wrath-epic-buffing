# Caster Priority (Manual Caster Rank) Design

## Problem

When several party members can cast the same buff, the mod picks the caster automatically: `SortProviders()` (`BubbleBuff.cs:552`) orders each buff's `CasterQueue` by active-party-before-reserve, then source type (spell/scroll/potion), then a heuristic (`BuffProvider.Priority`, `BubbleBuff.cs:679`: prepared before spontaneous, higher caster level first), then self-cast-only last. `Validate()` takes the first eligible caster per target.

Users cannot override this. A Nexus user wants Sosiel to cast Death Ward before Ember — always, or for specific buffs. The heuristic already handles the prepared-vs-spontaneous case, but there is no manual control when the heuristic ties (two prepared casters of equal CL — the unstable `List.Sort` decides arbitrarily) or when the user deliberately wants to override it. "Fiddle with party order" is not a reliable workaround because party order is only a scan-append order, not a sort key.

## Solution: Two-Layer Manual Rank

A per-character **global rank** plus a per-buff **override**, mirroring the existing `GlobalSourcePriority` + per-buff `SourcePriorityOverride` pattern. Effective rank per provider:

```
effectiveRank = PriorityOverride ?? CasterRanks[unit.UniqueId] ?? 0
```

Higher rank casts earlier. Default 0 everywhere, so behavior is unchanged until the user sets a rank (fully backward compatible, no save migration).

The rank is a new sort key in `SortProviders()`, ranked **below** active-before-reserve and source type, **above** the prepared/spontaneous heuristic (user decision: rank must not accidentally pull a reserve companion or a scroll ahead):

1. Active party before reserve (unchanged)
2. Source type order (unchanged)
3. **NEW: effective rank, higher first**
4. Heuristic: prepared before spontaneous, higher CL (becomes tiebreak)
5. Self-cast-only last (unchanged)

`Validate()`, `ValidateMass()`, and the executor need no changes — they consume the already-sorted queue.

## Design

### Data model

- `BuffProvider`: rename the existing heuristic getter `Priority` → `HeuristicPriority` (name collision), add `int? PriorityOverride` (runtime field, loaded from save).
- Global rank is keyed by **unit `UniqueId`**, not `CasterKey` — "Sosiel before Ember" is a statement about the character, not one spellbook. The per-buff override stays per `CasterKey` (consistent with `Banned`/`CustomCap`; multiclass units can override per book).
- `SortProviders()` already reaches `SavedState` for `GlobalSourcePriority` (`BubbleBuff.cs:554`) — look up `CasterRanks` the same way and compare effective ranks inside the sort lambda.

### Persistence (`SaveState.cs`)

- `SavedBufferState`: new `Dictionary<string, int> CasterRanks` (unit UniqueId → rank). Only non-zero entries are stored.
- `SavedCasterState`: new `int? PriorityOverride` (null = inherit global).
- Load in the `InitialiseFromSave` caster loop (`BubbleBuff.cs:242-251`); save in `updateSavedBuff` (`BufferState.cs:674-685`).
- **Retention:** `PriorityOverride != null` must be added to the `hasCasterConfig`/`hasSavedCasterConfig` checks (`BufferState.cs:697-707`), otherwise an override on a buff with nothing `wanted` evaporates on save.
- Cleanup: delete the dead, never-read `SavedBuffState.CasterPriority` (`List<string>`, `SaveState.cs:104`) — wrong shape for this design; removal needs no migration (unknown JSON fields are ignored on load).

### UI (caster popout, `BubbleBuffer.cs:~1620-1830`)

Two −/+ rows mirroring the existing cast-limit control, next to Ban/Limit:

- **"Rank (all buffs)"** — writes `CasterRanks[unitId]`. Set once, applies everywhere.
- **"Rank (this buff)"** — writes `PriorityOverride`. Semantics: while `PriorityOverride` is null the row displays the inherited global value greyed/de-emphasized; pressing −/+ materializes an override starting from that inherited value; setting the override back equal to the current global value clears it to null (inherit again) — same pattern as `CustomCap`, where reaching `MaxCap` resets to the −1 sentinel (`BubbleBuff.cs:603-613`). No separate reset button needed.
- **Tooltip** on the rank rows explaining the default order: *"Default: active party before reserve → source type → rank → prepared before spontaneous casters (higher caster level first) → self-cast-only last. Higher rank casts earlier."* Uses the settings-tooltip infrastructure from v1.17; read `claude-context/gotchas-ui.md` (settings-tooltip gotcha) before implementing.
- **SelectedCaster re-resolution:** adjusting a rank re-sorts `CasterQueue` on recalculate, but `SelectedCaster` is an *index* into it — re-resolve the selection by `CasterKey` identity after recalc or the open popout points at the wrong caster.
- Localization: ~4-5 new keys in all five locale files (missing en_GB key crashes the game; preserve per-file BOM state).

## Affected Files

- `BubbleBuff.cs` — `SortProviders()` sort key, `Priority` → `HeuristicPriority` rename, `PriorityOverride` field, save load/apply
- `SaveState.cs` — `CasterRanks` dict, `SavedCasterState.PriorityOverride`, delete dead `CasterPriority`
- `BufferState.cs` — save loop, retention checks
- `BubbleBuffer.cs` — popout rank rows, tooltip, SelectedCaster re-resolution
- `Config/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` — new keys

## Testing

Manual smoke test on the Steam Deck (no automated test infra):

1. Two casters share a buff → default order unchanged (prepared/higher CL first).
2. Set global rank on the lower-priority caster → order flips for all shared buffs.
3. Set a per-buff override contradicting the global rank → override wins for that buff only.
4. Save, reload → ranks persist; override on an otherwise-unconfigured buff persists.
5. Adjust rank with popout open → popout still shows the same caster.
6. Rank on a reserve companion does NOT pull it ahead of active party; rank on a scroll provider does not outrank spells.

## Out of Scope

- Rank badges on caster portraits, drag-and-drop ordering UI
- Rank overriding active/reserve or source-type ordering (user decided against)
- Cost-minimizing caster selection (e.g. "most remaining slots first")
- Reusing the dead `CasterPriority` list field
