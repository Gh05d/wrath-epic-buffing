# Buff It 2 The Limit

## Overview

Buff It 2 The Limit (formerly BubbleBuffs) is a Unity mod for **Pathfinder: Wrath of the Righteous** that adds automated buff casting routines to the spellbook UI. Players configure which buffs to cast on which party members, then execute them with HUD buttons. Built with C#/.NET Framework 4.81, Harmony patches, and Unity UI. Distributed via [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948).

## Build

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

**Setup:** needs the game's managed DLLs via `GamePath.props` (`<WrathInstallDir>` → `Wrath_Data/Managed/`) and the publicizer — see parent `wrath-mods/CLAUDE.md` §Common Build Setup.

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

## Repo Quirks

- `docs/` ist gitignored, aber Specs/Pläne/Nexus-Assets darunter sind getrackt (historisch force-added) — neue Dateien unter `docs/` brauchen `git add -f`.
- `.superpowers/sdd/progress.md` (SDD-Ledger) überlebt Feature-Runs: vor Vertrauen die Commit-SHAs gegen `git log` prüfen — ein stale Ledger des Vorgänger-Features behauptet sonst „alle Tasks complete". Bei neuem Plan: Header neu schreiben.

## Localization

- UI strings: `"key".i8()` (`Config/ModSettings.cs`); keys live in `Config/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json` — every new key must be added to ALL five files. A key missing from en_GB.json crashes the game (uncatchable infinite recursion in `Language.Get` — enGB is the fallback locale).
- BOM differs per file (en_GB/de_DE have UTF-8 BOM, fr/ru/zh don't) — preserve each file's state. Python: read `utf-8-sig`, write BOM back only where it was.
- Für einzeilige Key-Inserts reicht `sed -i '/anchor/a\  "key": "value",'` — BOM hängt an Zeile 1, sed-Edits darunter lassen es intakt. Danach: `head -c3 | od -An -tx1` (efbbbf = BOM) + JSON-Validierung.

## Release

Use `/release minor|patch|major` — the skill handles version bump, build, tag, push, and GitHub release. Nexus Mods upload is automated via GitHub Action on release publish. See `.claude/commands/release.md`.
- **Release notes in English** — even though user communicates in German, all release notes (GitHub + Nexus) must be in English.

## Support FAQ

- **Fetching in-game logs:** run `/check-logs` (user-invoked skill) to tail + filter the Steam Deck `Player.log` for mod-related exceptions after a deploy/repro. It greps `Player.log` for `BuffIt2TheLimit|Exception|Error|…` over SSH (`deck-direct`).
- **"Character can't move after entering a map, clicking the buff button fixes it"**: Game-side stuck-command bug (known after area transitions/cutscenes, esp. Chapter 5). The mod does nothing on area load — `SpellbookWatcher.OnAreaActivated` only installs UI and revalidates the ability cache. The buff button "fixes" it because any new `Commands.Run` interrupts the stuck slot occupant. Workarounds: attack something, save/load, or disable the mod to confirm (Ctrl+F10).
- **"Saddle up / Mount toggle: cursor only changes, or with two rideable pets only one character mounts"**: Fixed in v1.16.0 — Phase 0 now mounts directly. Mount is a stock `IsTargeted` activatable: `IsOn=true` never mounted, it only armed the ONE global `ClickWithSelectedAbilityHandler` (cursor change); arming a second rider in the same pass dropped the first and wedged its toggle at `IsOn=true`/no target, so later runs skipped it as "already on". The executor's mount branch (`BuffExecutor.TryMountTargeted`) resolves the rider's pet via the game's own checkers and calls `UnitPartRider.Mount(pet)` — details in `claude-context/gotchas-casting.md`. **Deliberately single-candidate**: a rider with SEVERAL suitable mounts (e.g. animal companion + statue-summoned triceratops) is skipped with a Player.log line — saddle up manually; absolute edge case, won't-support. Wedged toggles from older versions self-heal on the next routine run.
- **"Arcane Weapon / Sacred Weapon / Weapon Bond wird bei jedem Routine-Lauf neu gecastet (obwohl aktiv)"**: Fixed in v1.16.1. `IsPresent` matchte nur die DefaultEnchantments (+1..+5) — bei +5-Waffe oder Pool komplett in Properties legt das Spiel keins davon an. Jetzt via `UnitPartEnchantPoolData` erkannt (Details: `claude-context/gotchas-scanning.md`). Bei Reports mit älterer Version: Update empfehlen.
- **"Armored Mask (Arcanist Exploit) wird übersprungen, wenn Mage Armor bereits aktiv ist"**: Fixed in v1.18.0. Armored Mask re-appliziert bei fehlender Rüstung den vanilla MageArmorBuff (sonst seinen Bonus-Buff), sein flaches `AppliedBuffs` enthielt also MageArmorBuff → `IsPresent` matchte auf den geteilten Buff und skippte. Jetzt via self-gated-buff-Exclusion erkannt (Details: `claude-context/gotchas-scanning.md`). Bei Reports mit älterer Version: Update empfehlen.
- **"Skipped buffs are never shown / log says applied 0/0"**: Fixed in v1.14.9. Ursache war der `Fulfilled > 0`-Filter im Executor + `IsAvailable`-Skip in `Validate()` (Details: `claude-context/gotchas-casting.md`). Bei Reports mit älterer Version: Update empfehlen. Combat-Start-Pfad + Activatables haben den Silent-Drop noch (bewusst, Player.log-only).
- **"Kann ich steuern, welcher Caster einen geteilten Buff zuerst castet?"**: Ja, seit v1.19.0 (Caster Priority): zwei −/+ Zeilen im Caster-Popout — „Priority (all buffs)" (global, `SavedBufferState.CasterRanks[unitId]`) + „Priority (this buff)" (Override, `BuffProvider.PriorityOverride`; grau = erbt). Sortierung in `SortProviders()`: aktiv vor Reserve → Quelltyp → Rang (höher = früher) → prepared vor spontan (höheres CL zuerst) → self-cast-only zuletzt. Prepared-vor-spontan war schon IMMER Default — viele Anfragen dazu sind ohne Rang bereits erfüllt. Bei älteren Versionen: Update empfehlen.
- **"AoE-Party-Buff (z.B. Zephyr's Fleetness aus Expanded Content) meldet Erfolg, Slot weg, aber niemand bekommt den Buff"**: Fixed in v1.19.1. Mass-Buffs targeten den Caster; bei Spells mit `CanTargetSelf=false` brach `RuleCastSpell.OnTrigger` den Cast ab, NACHDEM der Mod den Slot gespent hatte. Jetzt wählt `ResolveMassTarget` ein gültiges Zentrum und der Handler bricht vor jedem Spend ab (Details: `claude-context/gotchas-casting.md`). Bei Reports mit älterer Version: Update empfehlen. Diagnose-Signatur im Player.log der alten Version: `Invalid target ... for spell '...'`.
- **"Goggles of Pure Sight: Charge wird vom Mod nicht verbraucht, manuell neu castbar"**: Kein Bug (analysiert 2026-07-21). Die Brillen-Ability applied einen PERMANENTEN Buff („take 20 on next dispel magic attempt", ModifyD20), der erst beim nächsten Dispel-Wurf verbraucht wird. Solange er aktiv ist, skippt der Mod den Cast korrekt (Present-Check) → Charge bleibt. Manuelles Casten umgeht den Present-Check. Der Equipment-Charge-Verbrauchspfad selbst ist Ende-zu-Ende verifiziert (`EnsureFinalized` → `Charges--`, gated auf `ActuallyFired`; `IsSpendCharges` = `GainAbility && SpendCharges`, IL-geprüft). Nur falls ein Report ein echtes „applied" mit frischem Buff UND unveränderter Charge belegt: Player.log anfordern (`Consuming item after cast`-Errors).
- **"Can you add summon/conjure spells (e.g. Summon Spirit Paladin)?"**: No — won't-do (evaluated 2026-06-28). Summons are point-targeted ("summoned monsters appear where you designate"), so there's no party member to assign them to. The scan rejects them (summon actions, not `ContextActionApplyBuff`; `TargetAnchor != Owner` so the self-target fallback misses) and the cast pipeline assumes a unit target. Even if built, ~1-min duration makes them pointless in a pre-combat buff routine. Not worth a point-target cast-path rewrite.

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

- Shared style (K&R, 4-space, `var`): parent `wrath-mods/CLAUDE.md` §Code Style; editorconfig enforces `csharp_new_line_before_open_brace = none`
- Game's private fields accessed via publicizer (e.g., `PartyView.m_Hide`, `button.m_CommonLayer`)
