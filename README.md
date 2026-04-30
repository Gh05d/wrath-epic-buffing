# Buff It 2 The Limit

A fork of [factubsio's BubbleBuffs](https://github.com/factubsio/BubbleBuffs) — the buff automation mod for **Pathfinder: Wrath of the Righteous**, now continued as **Buff It 2 The Limit**.

## Highlights

- **All buff sources in one place** — Spells, scrolls, potions, wands, and activatable equipment (staves, rods) live in a unified Buffs tab with one entry per buff regardless of source. Inline source-type controls + priority ordering. Wands use UMD logic like scrolls.
- **Toggles, songs and activatables** — Bardic Performance, Inspire Courage, Hunter's Bond and other free-toggle / song / activatable abilities are first-class buff sources. Round-limit auto-deactivation, mutual exclusivity within the same activation group, and proper handling of form-change abilities (Shifter's Fury, Chimeric Aspect).
- **Cast on Combat Start** — Mark buffs to fire automatically when combat begins. Optional Skip-Animations mode casts instantly so you don't lose the surprise round to animation time.
- **Reserve companion support** — Configure buffs for characters who aren't in your active party. When they swap in, their buff configuration travels with them.
- **Smart casting** — Extend Rod metamagic auto-pick, Bypass Arcane Spell Failure when prebuffing out of combat, Brownfur Transmuter (Share Transmutation), Azata Zippy Magic, Powerful Change, Magic Deceiver fused spells, Magic Weapon (EnhanceWeapon), and crafted-item caster levels all handled correctly.
- **Multi-group assignment** — One buff can belong to Normal, Quick, and Important groups simultaneously.
- **Customizable keyboard shortcuts** — Bind each buff group and the buff menu to a keyboard shortcut with modifier-key support.
- **Quick open button** — HUD button jumps straight into buff configuration without navigating the spellbook.
- **Redesigned details panel** — Vertical split layout with spell info, source controls, scrollable caster/target portraits with reserve separator, and an action bar.
- **Source-type overlays** — Game-native icons on caster portraits showing spell, scroll, potion or equipment source.
- **Sort-by-name toggle** — Optional alphabetical buff ordering, persisted across save reloads and area transitions.
- **Improved buff filtering** — Better detection of beneficial vs harmful spells via descriptor checks, nested action analysis, and equipment-item recognition.

## About

Buff It 2 The Limit adds an in-game option to spellbooks to create buff routines. Configure which buffs to cast on which party members, then execute entire buff sequences with a single click.

Original mod by [factubsio](https://github.com/factubsio/BubbleBuffs) — download the original from [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195).

## Known mod interactions

- **BubbleTweak animation speedup + animated casting**: When BubbleTweak's "Increase Animation Speed" is active and Buff It 2 The Limit is set to use full cast animations, some casts can be dropped silently. The sped-up animation can finish the underlying `UnitCommand` before the spell rule actually fires, so the buff never lands even though the cast was queued. Workaround: enable **Skip animations** in this mod, or disable BubbleTweak's animation-speed multiplier for casting. The combat-log "applied" count reports actually-fired casts so you can spot the mismatch.

## Development Setup

1. Clone this repository
2. Provide game DLLs — either:
   - Set `WrathInstallDir` environment variable to your game install path, or
   - Create `GamePath.props` in the repo root (see [CLAUDE.md](CLAUDE.md) for format)
3. Build:
   ```bash
   dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj
   ```
4. Output goes to `BuffIt2TheLimit/bin/Debug/` — copy contents to `{GameDir}/Mods/BuffIt2TheLimit/`

### Debugging

See [Owlcat's modding wiki](https://github.com/spacehamster/OwlcatModdingWiki/wiki/Debugging#debugging-with-visual-studio) for Visual Studio debugging setup with Unity.

## License

[MIT](LICENSE) — originally by Sean Petrie (Vek17) and factubsio (Bubbles).

## Acknowledgments

- [@factubsio](https://github.com/factubsio) for the original BubbleBuffs mod
- [@Balkoth](https://github.com/Balkoth) for Buffbot, the direct inspiration
- [@Vek17](https://github.com/Vek17) for the codebase foundation
- The Pathfinder WotR Discord community
