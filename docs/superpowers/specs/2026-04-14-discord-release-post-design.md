# Discord Release Post — Design

## Problem

Release announcements need to reach Owlcat's `#mod-updates` Discord channel. The user has no webhook or admin rights there, so fully automated posting (bot/webhook) is not possible. Manually re-typing the release notes after every release is repetitive and error-prone.

## Goal

Generate a ready-to-paste Discord message at the end of every `/release` run, printed to the terminal with clear delimiters for one-shot copy-paste into Discord.

## Non-Goals

- No auto-posting (no bot, no webhook)
- No file output
- No embeds, rich formatting, or images
- No changes to the existing release flow (GitHub release, Nexus upload, tag push remain unchanged)

## Design

### Scope of change

Single-file edit: `.claude/commands/release.md`.

### Placement

New **Step 9 „Discord Post"** appended after the current Step 8 („Abschluss"). Does not alter any existing step.

### Message template

```
**Buff It 2 The Limit vX.Y.Z**

- <bullet 1 from Step 3 release notes>
- <bullet 2>
- ...

GitHub: https://github.com/Gh05d/wrath-epic-buffing/releases/tag/vX.Y.Z
Nexus: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948
```

- Bullets are reused verbatim from the "What's New" list generated in Step 3 — no recomputation, no LLM re-phrasing.
- Both links are plain URLs (Discord auto-embeds). No Markdown link syntax.
- Mod name + version in bold as the only heading-like element (Discord doesn't render headings reliably in regular messages).

### Terminal output format

```
=== Discord Post (copy everything between the delimiters) ===
<message body>
=== End Discord Post ===
```

The delimiters serve two purposes: (1) unambiguous copy range, (2) visual separator from the preceding summary.

### Incidental fix

The skill's hardcoded config currently reads `Nexus-URL: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195`. Per `Info.json` (`HomePage`) and `../CLAUDE.md` (Nexus Mod-IDs table), the correct mod ID is **948**. Corrected in the same edit so the Discord post and the existing Nexus links point to the same mod.

## Error Handling

- If Step 3's "What's New" bullets are empty (edge case: release with only `chore:` commits that got filtered), print a placeholder line `- Maintenance release` so the message is still valid.
- No retry, no fallback — if something in Step 9 fails, the release is already complete (tag pushed, GitHub release published, Nexus upload running). Step 9 is purely informational output.

## Testing

Manual verification on the next patch release: run `/release patch`, copy the printed block, paste into a Discord DM to self, confirm formatting renders correctly (bold title, bullet list, auto-embedded links).

No automated tests — this is a markdown-templated terminal print at the end of a manual workflow.
