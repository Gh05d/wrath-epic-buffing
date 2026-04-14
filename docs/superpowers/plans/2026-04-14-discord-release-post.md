# Discord Release Post Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a final step to `/release` that prints a Discord-formatted announcement (bullets + GitHub link + Nexus link) to the terminal for manual copy-paste into Owlcat's `#mod-updates` channel.

**Architecture:** Single-file edit to `.claude/commands/release.md`. Appends a new "Schritt 9: Discord Post" after the existing "Schritt 8: Abschluss". Reuses the "What's New" bullets already generated in Schritt 3. No new dependencies, no code — pure prompt/template change. Also corrects the hardcoded Nexus mod ID (`195` → `948`) in two places.

**Tech Stack:** Markdown (slash-command prompt file).

---

## File Structure

**Modify:** `.claude/commands/release.md`
- Line 8 — config: fix `Nexus-URL` from `.../mods/195` to `.../mods/948`
- Line 230 — Step 8 fallback link: fix `.../mods/195?tab=files` to `.../mods/948?tab=files`
- After line 232 (after Step 8, before the `---` separator at 234) — insert new `## Schritt 9: Discord Post` section

Spec reference: `docs/superpowers/specs/2026-04-14-discord-release-post-design.md`

---

## Task 1: Fix Nexus mod ID and add Step 9

**Files:**
- Modify: `.claude/commands/release.md:8` (config Nexus-URL)
- Modify: `.claude/commands/release.md:230` (Step 8 fallback)
- Modify: `.claude/commands/release.md` insert new Step 9 between line 232 and the `---` at line 234

- [ ] **Step 1: Fix the Nexus mod ID in the config header**

Find:
```
- Nexus-URL: `https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195`
```
Replace with:
```
- Nexus-URL: `https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948`
```

- [ ] **Step 2: Fix the Nexus mod ID in the Step 8 fallback block**

Find:
```
Nexus Upload (manuell): https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195?tab=files
```
Replace with:
```
Nexus Upload (manuell): https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948?tab=files
```

- [ ] **Step 3: Insert the new Schritt 9 between Schritt 8 and "Fehlerbehandlung"**

Locate the line `---` immediately after the Step 8 block (currently at line 234, right before `## Fehlerbehandlung — Übersicht`). Insert the following block **above** that `---` line, leaving the existing `---` and everything after it untouched:

````markdown

---

## Schritt 9: Discord Post

Generiere die fertige Discord-Nachricht zum Copy-Pasten in Owlcat's `#mod-updates`-Kanal. Nutze die **identischen** „What's New"-Bullets, die in Schritt 3 erzeugt wurden — nicht neu formulieren.

Falls die Bullet-Liste leer wäre (z.B. nur `chore:`-Commits seit letztem Tag), verwende stattdessen den Platzhalter-Bullet `- Maintenance release`.

Ausgabeformat im Terminal (exakt so, inkl. Delimiter-Zeilen):

```
=== Discord Post (alles zwischen den Zeilen kopieren) ===
**Buff It 2 The Limit vX.Y.Z**

- <bullet 1>
- <bullet 2>
- ...

GitHub: https://github.com/Gh05d/wrath-epic-buffing/releases/tag/vX.Y.Z
Nexus: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/948
=== End Discord Post ===
```

Regeln:

- Mod-Name + Version in einer Zeile fett (`**...**`), danach eine Leerzeile.
- Bullets mit `- ` prefix, genau wie in den GitHub-Release-Notes aus Schritt 3.
- Beide Links als reine URLs (kein Markdown-Link-Syntax) — Discord erzeugt automatisch Previews.
- Keine „Installation" oder „Requirements" Sektionen — Discord-Post bleibt kurz, Details sind auf Nexus.
- Schritt 9 ist rein informativ: schlägt er fehl, ist das Release bereits durch (Tag gepushed, GitHub Release live, Nexus Action läuft). Nicht abbrechen, nicht rückgängig machen.
````

- [ ] **Step 4: Verify the edit compiles as a valid markdown file**

Run:
```bash
grep -c "^## Schritt" .claude/commands/release.md
```
Expected: `9` (eight existing steps + the new Step 9).

Run:
```bash
grep -n "mods/195" .claude/commands/release.md
```
Expected: no output (all occurrences replaced).

Run:
```bash
grep -c "mods/948" .claude/commands/release.md
```
Expected: `2` (config + fallback).

- [ ] **Step 5: Commit**

```bash
git add .claude/commands/release.md
git commit -m "feat(release): add Discord post step and fix Nexus mod ID

Adds Schritt 9 that prints a copy-paste-ready Discord announcement
(What's New bullets + GitHub + Nexus links, delimited block) at the
end of /release.

Also fixes the hardcoded Nexus mod ID (195 -> 948) in the config
header and the Step 8 manual-upload fallback — the correct ID per
Info.json and CLAUDE.md."
```

---

## Manual Verification

Per spec: manual test on the next patch release. Run `/release patch`, copy the block printed between the `=== Discord Post ... ===` delimiters, paste into a Discord DM to self, confirm:
- Title renders bold.
- Bullets render as a list.
- Both links auto-embed (unless suppressed).
