# Split CLAUDE.md into Routing Index + Topic-Indexed Deep Docs

**Date:** 2026-04-23
**Status:** Draft

## Problem

`wrath-epic-buffing/CLAUDE.md` has grown to 266 lines / ~40 KB and is loaded into every conversation's context. The dominant section is `## Gotchas` (~75 bullet points, ~60% of the file). Most of those gotchas are area-specific ("when you touch Unity UI, remember X"), so they're dead weight in conversations that don't touch that area.

Primary goal: **reduce always-loaded context cost.**

Non-goals:
- Rewriting or summarising gotcha content (hard-earned, preserve verbatim)
- Reorganising parent `wrath-mods/CLAUDE.md` or machine-level `~/CLAUDE.md`
- Adding process/tooling to enforce the routing rule

## Approach

Turn CLAUDE.md into a **routing index**. Move area-specific deep content into topic files under a new top-level `claude-context/` directory. CLAUDE.md's topic index tells the reader which deep doc to load before editing a given area.

## Target Structure

### CLAUDE.md (always loaded, target ~80 lines / ~12 KB)

Keeps:
- Overview
- Build
- Deploy
- Versioning
- Release
- Debug Keybinds (DEBUG builds only)
- Code Style

Adds:
- `## Topic Index` — routing table (see below)
- Rule for maintaining the routing table when new gotchas are added

Removes (migrated to topic files):
- Gotchas section
- Combat-Start Diagnostics section
- Architecture section and all subsections
- Credit System (Buff Availability) section
- Unity UI Layout Patterns section

### `claude-context/` (new top-level directory, 5 files)

| File | Contains |
|---|---|
| `architecture.md` | Architecture section (Mod Lifecycle, Core Data Flow, Key Classes, UI Structure, Enums, Localization, Asset Loading, Save System) |
| `gotchas-ui.md` | Unity UI Layout Patterns + UI-related gotchas (TMP/TextMeshProUGUI, MakeButton, buttonPrefab, ScrollRect, OwlcatButton, `sourceControlObj` visibility, Caster-Portrait-Index, "don't destroy UI during click", `GlobalBubbleBuffer.Buttons` stale refs, Debug via `GetWorldCorners`) |
| `gotchas-scanning.md` | Item Types + ActivatableAbility API + Pet/Companion API + scanning gotchas (`ConversionsProvider` patterns, Shifter's Fury dispatch, `IsRuntimeOnly`/`HiddenInUI` filter, `AddActivatable`/`AddBuff` filter concerns, `SpellsWithBeneficialBuffs` cache, `Category.Toggle`, Scan summary log, Equipment-activatable charges gate, `AddActivatable.Spell == null`, `BuffProvider.CanTarget` null crash, `BlueprintBuff.FxOnStart` fallback, archetype spellbooks override `IsArcanist`, Weapon Enchantment pattern, Magic Deceiver fused spells) |
| `gotchas-casting.md` | Credit System + Combat-Start Diagnostics + casting gotchas (`RuleCastSpell` param cloning, `MetamagicData.Clear`/`Add`, `UnitUseAbility` synthetic rejection, `Commands.Run` combat-start disruption, two dispatch paths, `ExecuteCombatStart` IsInCombat quirk, `ActivatableAbility.IsOn` vs `TryStart`, no per-round EventBus, Mass/burst self-target, `BubbleBuff.IsMass`, `ValidateMass` target selection, `AddBuff` merges providers, Phase 0 ordering, `[CSD]` log prefix, `BubbleBuff.Spell` null for songs, `BubbleBuff.SavedState` always null, `BuffProvider.SelfCastOnly` computed, `UpdateCasterDetails` filter, Share Transmutation scope, `CanTarget` `ShareTransmutation` escape) |
| `gotchas-build.md` | Build/deploy/release/Nexus/UMM gotchas (DLL timestamp verification, `Info.json` byte-count ambiguity, UMM hot-reload DEBUG-only, worktrees need `GamePath.props`, `dotnet build` skip-rebuild, Nexus BBCode/changelog/version-display, docs/ gitignored, `.NET Framework 4.8.1` missing APIs, Newtonsoft old version, `[JsonConverter]` on collections, WidgetPaths version selection, EnhancedInventory interop, Publicizer scope, `Main.Log` vs `Main.Verbose`, Harmony manual patching, `IEnumerator` needs `System.Collections`, Config JSON UTF-8 BOM, Blueprint action tree extraction, `ilspycmd` full-IL dump workaround, upstream PR rebase, diagnostic workflow without unit tests) |

### Routing Table (in CLAUDE.md)

```markdown
## Topic Index

Deep docs live in `claude-context/`. Before editing an area, read the matching file:

| Touching... | Read first |
|---|---|
| `BubbleBuffer.cs` UI code, `UIHelpers.cs`, new Unity layouts | `claude-context/gotchas-ui.md` |
| `BufferState.cs` scan/discovery, new item or activatable source | `claude-context/gotchas-scanning.md` |
| `BuffExecutor.cs`, `EngineCastingHandler.cs`, combat-start, casting coroutines | `claude-context/gotchas-casting.md` |
| Build config, release, Nexus upload, UMM, `ilspycmd` | `claude-context/gotchas-build.md` |
| First time in this codebase / broad architecture question | `claude-context/architecture.md` |

**Maintenance rule:** when a new gotcha is discovered, add it to the matching topic file. Update this table only if the routing itself changes.
```

## Migration Rules

1. **Content is verbatim.** Copy gotcha bullets and architecture prose character-for-character from the current CLAUDE.md. No rewrite-for-style, no summarising, no reformatting. These notes are hard-earned.
2. **Dedup with parent.** Entries that duplicate `~/Code/wrath-mods/CLAUDE.md` are dropped, not migrated. Dedup candidates (to verify during migration, not blindly delete):
   - `ilspycmd` stack-overflow note (parent has the authoritative version)
   - Any other entry whose text is identical or near-identical to a parent-level entry
3. **Content preservation check.** Before finalising, grep the old CLAUDE.md for every distinctive phrase (e.g., class names, method names in backticks, `[CSD]`, `ShiftersFury`, `ShareTransmutation`, `BlueprintBuff.FxOnStart`) and confirm each appears in exactly one of the new files (or is a confirmed parent-dup).
4. **Topic-file structure.** Each topic file starts with a one-line description of its scope, then uses the same heading style as the current CLAUDE.md (`##` for major, `###` for sub, bullets for gotchas).
5. **Single commit on `master`.** Suggested message: `docs: split CLAUDE.md into topic-indexed deep docs under claude-context/`.

## Directory Choice: `claude-context/`

Rejected: `docs/claude/`. `docs/` is repo-gitignored (confirmed in `.gitignore`). Committing there needs `git add -f` every time, which is friction and a footgun (easy to forget, easy to `.gitignore` the whole `docs/` tree accidentally again).

Chosen: new top-level `claude-context/`. Not gitignored, explicit purpose in the name, discoverable next to `BuffIt2TheLimit/`, `Config/`, `Assets/` at repo root.

## Success Criteria

- `wc -l CLAUDE.md` drops from 266 to ~80 lines.
- No gotcha bullet from the original CLAUDE.md is missing from the combined set of topic files + confirmed parent-duplicates.
- Routing table in CLAUDE.md covers every topic file; each topic file is reachable via at least one row in the table.
- Build and deploy instructions in CLAUDE.md still stand on their own (no forward-reference to a topic file is required for a normal build/deploy/release).

## Risks

- **Discipline risk:** topic files only help if the router (Claude / human editor) actually reads the matching file before editing. If ignored, the safety net is gone. Mitigation: the routing table is short and lives at the top of CLAUDE.md, impossible to miss.
- **Cross-topic gotchas:** some entries touch two areas (e.g., `BuffExecutor` dispatch paths relate to both casting and UI). For each, pick the dominant area and place it in one file only — do not duplicate across topic files (would reintroduce drift). If cross-linking becomes necessary, use a one-line pointer (`See also: gotchas-ui.md — ...`).
- **Stale routing:** if a file rename or architectural change invalidates the "Touching..." column, the table rots silently. No mitigation beyond "update the table when you move files" — same risk as any doc.

## Out of Scope

- Pruning genuinely stale gotchas (separate task; preserve verbatim first, prune later if desired).
- Topic files for `fix-subsonic-bullshit/` or `wrath-tactics/` siblings.
- Any tool/hook that auto-injects topic-file contents based on the file being edited.
