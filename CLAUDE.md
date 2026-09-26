# Buff It 2 The Limit

## Overview

Buff It 2 The Limit (formerly BubbleBuffs) is a Unity mod for **Pathfinder: Wrath of the Righteous** that adds automated buff casting routines to the spellbook UI. Players configure which buffs to cast on which party members, then execute them with HUD buttons. Built with C#/.NET Framework 4.8.1, Harmony patches, and Unity UI. Distributed via [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948).

Shared build/deploy/Nexus/release rules: → parent `wrath-mods/CLAUDE.md` (§Common Build Setup, §Steam Deck Deployment, §Nexus Mods, §Release Process). Git remotes: `fork` = Gh05d (push here), `origin` = upstream (→ parent §Nexus Mods).

## Build

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

Setup (`GamePath.props`, `GameInstall/`, publicizer): → parent §Common Build Setup. Output: `BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll` + assets copied to output dir; the build target also creates a zip for distribution.

**Release build** (for distribution — excludes debug keybinds):
```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo
```

## Deploy

```bash
./deploy.sh
```

Rules (timeout, no `&&` chain before commit, SSH probe, verification): → parent §Steam Deck Deployment. Mod-specific: `deploy.sh` shippt nur `BuffIt2TheLimit.dll` + `Info.json`; `AssetBundles`/`tutorialcanvas` kommen nur per Release-Zip aufs Deck (Erstinstallation aus dem Zip). Locale-JSONs sind EmbeddedResource → DLL-Deploy reicht.

## Versioning

Version files (generic rule → parent §Release Process): `BuffIt2TheLimit/BuffIt2TheLimit.csproj` `<Version>` (controls ZIP filename), `BuffIt2TheLimit/Info.json` `"Version"` (UMM reads this), `Repository.json` `"Version"` + `"DownloadUrl"` (UMM auto-update). `/release` handles all three.

## Repo Quirks

- `docs/` ist gitignored, aber Specs/Pläne/Nexus-Assets darunter sind getrackt (historisch force-added) — **jeder** `git add` unter `docs/` braucht `-f`, auch für bereits getrackte Dateien (git verweigert sonst mit „paths are ignored").
- `.superpowers/sdd/progress.md` (SDD-Ledger) überlebt Feature-Runs: vor Vertrauen die Commit-SHAs gegen `git log` prüfen — ein stale Ledger des Vorgänger-Features behauptet sonst „alle Tasks complete". Bei neuem Plan: Header neu schreiben.
- Nexus-Antwort-Entwürfe nach `docs/nexus-reply-<topic>.txt` schreiben (`git add -f`), nicht nur ins Scratchpad — das überlebt Compaction nicht zuverlässig, und wiederkehrende Reports brauchen die Vorlage erneut.
- `CHANGELOG.md` ist Upstream-Relikt (BubbleBuffs 5.1.x), wird nicht gepflegt; Release Notes nur in GitHub Releases.

## Localization

- UI strings: `"key".i8()` (`BuffIt2TheLimit/Config/ModSettings.cs`); keys live in `BuffIt2TheLimit/Config/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` — every new key must be added to ALL five files. A key missing from en_GB.json crashes the game (uncatchable infinite recursion in `Language.Get` — enGB is the fallback locale).
- BOM differs per file (en_GB/de_DE have UTF-8 BOM, fr/ru/zh don't) — preserve each file's state. Python: read `utf-8-sig`, write BOM back only where it was.
- Für einzeilige Key-Inserts reicht `sed -i '/anchor/a\  "key": "value",'` — BOM hängt an Zeile 1, sed-Edits darunter lassen es intakt. Danach: `head -c3 | od -An -tx1` (efbbbf = BOM) + JSON-Validierung.
- Nach Key-Inserts Parität belegen: alle fünf Dateien müssen dieselbe Key-Zahl haben (`json.load(open(f, encoding='utf-8-sig'))` → `len`). Das ist der einzige echte Nachweis, dass kein File vergessen wurde.

## Release

Use `/release minor|patch|major` (`.claude/commands/release.md`) — bump, build, tag, push, GitHub release; Nexus upload is automatic on release publish. Cycle, pre-conditions and English-only release notes: → parent §Release Process.

## Support FAQ

Known reports, fix versions and reply patterns: `claude-context/support-faq.md` — read it BEFORE answering a Nexus/Discord report or feature request.

- **Fetching in-game logs:** run `/check-logs` (user-invoked skill, `.claude/skills/check-logs/`) to tail + filter the Steam Deck `Player.log` for mod-related exceptions after a deploy/repro. It greps `Player.log` for `BuffIt2TheLimit|Exception|Error|…` over SSH (`deck-direct`).
- **Still open (no report yet):** Item-Metamagic + ExtendPotion for synthetic item casts (context: v1.20.1 Infuse-Magic-Device fix).
- **Still open (by design):** Combat-Start-Pfad + Activatables haben den Silent-Drop übersprungener Buffs noch (Player.log-only; the routine path shows skips since v1.14.9).

## Topic Index

Deep docs live in `claude-context/`. Before editing an area, read the matching file:

| Touching... | Read first |
|---|---|
| `BubbleBuffer.cs` UI code, `UIHelpers.cs`, new Unity layouts | `claude-context/gotchas-ui.md` |
| `BufferState.cs` scan/discovery, new item or activatable source | `claude-context/gotchas-scanning.md` |
| `BuffExecutor.cs`, `EngineCastingHandler.cs`, combat-start, casting coroutines | `claude-context/gotchas-casting.md` |
| Build config, release, Nexus upload, UMM, `ilspycmd` | `claude-context/gotchas-build.md` |
| First time in this codebase / broad architecture question | `claude-context/architecture.md` |
| Nexus/Discord reports, feature requests, "is this fixed?" | `claude-context/support-faq.md` |

**Maintenance rule:** when a new gotcha is discovered, add it to the matching topic file. Update this table only if the routing itself changes.

## Debug Keybinds (DEBUG builds only)

- **Shift+I** — Reinstall UI + recalculate buffs (hot-reload during development)
- **Shift+B** — Reload the entire mod
- **Shift+R** — Debug helper (currently adds a test item)

## Code Style

- Shared style (K&R, 4-space, `var`): → parent §Code Style; editorconfig enforces `csharp_new_line_before_open_brace = none`
- Game's private fields accessed via publicizer (e.g., `PartyView.m_Hide`, `button.m_CommonLayer`)
