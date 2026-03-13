# Buff It 2 The Limit

A fork of [factubsio's BubbleBuffs](https://github.com/factubsio/BubbleBuffs) — the buff automation mod for **Pathfinder: Wrath of the Righteous**, now continued as **Buff It 2 The Limit**.

## What's Different

This fork continues development with new features:

- **Unified buff sources** — Spells, scrolls, and potions merged into a single "Buffs" tab. One entry per buff regardless of source, with inline source-type controls (checkboxes + priority).
- **Equipment support** — Activatable quickslot items (staves, rods) and wands from inventory as buff sources in a dedicated "Equipment" tab. Wands use the same UMD logic as scrolls.
- **Redesigned details panel** — Vertical split layout with flex-weighted sections: spell info, source controls, caster/target portraits, and action bar. Add/Remove buttons positioned next to target portraits. Portraits auto-shrink to fit available space.
- **Source-type overlays** — Game-native icons on caster portraits showing whether they're casting from spell, scroll, potion, or equipment.
- **Quick open button** — HUD button to directly open the buff configuration menu without navigating through the spellbook screen first.
- **Renamed buff groups** — "Normal Buffs", "Quick Buffs", "Important Buffs" (clearer labels).
- **Equipment charge handling** — Proper charge consumption for equipment items and fix for false "out of charges" errors on single-charge items.

## About

Buff It 2 The Limit adds an in-game option to spellbooks to create buff routines. Configure which buffs to cast on which party members, then execute entire buff sequences with a single click.

Original mod by [factubsio](https://github.com/factubsio/BubbleBuffs) — download the original from [Nexus Mods](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195).

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
