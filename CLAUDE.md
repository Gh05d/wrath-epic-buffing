# Buff It 2 The Limit

## Overview

Buff It 2 The Limit (formerly BubbleBuffs) is a Unity mod for **Pathfinder: Wrath of the Righteous** that adds automated buff casting routines to the spellbook UI. Players configure which buffs to cast on which party members, then execute them with HUD buttons. Built with C#/.NET Framework 4.81, Harmony patches, and Unity UI. Distributed via [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948).

## Build

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

**Setup:** The build requires the game's managed DLLs. The csproj references them via `$(WrathInstallDir)/Wrath_Data/Managed/`. This is resolved in order:
1. `GamePath.props` in repo root (auto-generated or manual)
2. Auto-detection from `Player.log` (Windows only, uses `findstr`)

For Linux dev, create `GamePath.props` manually or symlink game DLLs:
```xml
<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
  <PropertyGroup>
    <WrathInstallDir>/path/to/game/or/symlink</WrathInstallDir>
  </PropertyGroup>
</Project>
```

The build uses `BepInEx.AssemblyPublicizer.MSBuild` to access private/internal game fields (marked with `Publicize="true"` in csproj). Publicized DLLs go to `obj/Debug/publicized/`.

Output: `BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll` + assets copied to output dir. The build target also creates a zip for distribution.

**Release build** (for distribution — excludes debug keybinds):
```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo
```

## Deploy

```bash
./deploy.sh
```

Builds and SCPs `BuffIt2TheLimit.dll` + `Info.json` to Steam Deck mod directory. Requires `deck-direct` SSH alias. Always deploy both — UMM reads the version from `Info.json`, not the DLL.

## Versioning

Version must be updated in **three** files simultaneously:
1. `BuffIt2TheLimit/BuffIt2TheLimit.csproj` — `<Version>` (controls ZIP filename)
2. `BuffIt2TheLimit/Info.json` — `"Version"` (UMM reads this)
3. `Repository.json` — `"Version"` + `"DownloadUrl"` (UMM auto-update)

Use `/release` skill to handle this automatically.

## Localization

- UI strings: `"key".i8()` (`Config/ModSettings.cs`); keys live in `Config/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` — every new key must be added to ALL five files. A key missing from en_GB.json crashes the game (uncatchable infinite recursion in `Language.Get` — enGB is the fallback locale).
- BOM differs per file (en_GB/de_DE have UTF-8 BOM, fr/ru/zh don't) — preserve each file's state. Python: read `utf-8-sig`, write BOM back only where it was.

## Release

Use `/release minor|patch|major` — the skill handles version bump, build, tag, push, and GitHub release. Nexus Mods upload is automated via GitHub Action on release publish. See `.claude/commands/release.md`.
- **Release notes in English** — even though user communicates in German, all release notes (GitHub + Nexus) must be in English.

## Support FAQ

- **"Character can't move after entering a map, clicking the buff button fixes it"**: Game-side stuck-command bug (known after area transitions/cutscenes, esp. Chapter 5). The mod does nothing on area load — `SpellbookWatcher.OnAreaActivated` only installs UI and revalidates the ability cache. The buff button "fixes" it because any new `Commands.Run` interrupts the stuck slot occupant. Workarounds: attack something, save/load, or disable the mod to confirm (Ctrl+F10).
- **"Mount toggle in the Toggle tab only changes the cursor, the rider doesn't mount"**: Not a mod feature — Mount is a stock game `ActivatableAbilityMount` the activatable scan surfaces like any free toggle. The mod only sets `IsOn=true` (Phase 0); real mounting is a targeted command (`ContextActionMount` → `UnitPartRider.Mount(targetUnit,…)`) needing a designated mount, which an on/off toggle can't supply. Not tied to a specific mount, not mod-fixable without dedicated mount-target handling (out of scope).

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

## Debug Keybinds (DEBUG builds only)

- **Shift+I** — Reinstall UI + recalculate buffs (hot-reload during development)
- **Shift+B** — Reload the entire mod
- **Shift+R** — Debug helper (currently adds a test item)


## Code Style

- K&R brace style (opening brace on same line): `csharp_new_line_before_open_brace = none`
- 4-space indentation
- `var` when type is apparent, explicit type otherwise
- Game's private fields accessed via publicizer (e.g., `PartyView.m_Hide`, `button.m_CommonLayer`)
