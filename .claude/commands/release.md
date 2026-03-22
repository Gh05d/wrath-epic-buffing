# /release — Buff It 2 The Limit Release Orchestrator

## Konfiguration (hardcoded)

- Remote: `fork`
- Repo: `Gh05d/wrath-epic-buffing`
- Mod-Name: `Buff It 2 The Limit`
- Nexus-URL: `https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195`
- csproj: `BuffIt2TheLimit/BuffIt2TheLimit.csproj`
- Info.json: `BuffIt2TheLimit/Info.json`
- Repository.json: `Repository.json`
- Build: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo`

---

## Schritt 1: Release-Typ bestimmen

Lies `$ARGUMENTS`.

- Wenn `$ARGUMENTS` eines von `patch`, `minor`, `major` ist — verwende es direkt.
- Sonst: Frage den User: „Welcher Release-Typ? (patch / minor / major)"

---

## Schritt 2: Pre-flight-Checks

Führe alle Checks aus, bevor du irgendetwas änderst.

1. **Working Tree sauber?**
   ```
   git diff --quiet && git diff --cached --quiet
   ```
   Schlägt fehl: Abbruch mit „Fehler: Working Tree ist dirty. Bitte erst committen oder stashen."

2. **Auf master?**
   ```
   git rev-parse --abbrev-ref HEAD
   ```
   Nicht `master`: Abbruch mit „Fehler: Nicht auf master-Branch."

3. **Aktuelle Version lesen** aus `BuffIt2TheLimit/BuffIt2TheLimit.csproj`:
   ```
   grep -oP '<Version>\K[^<]+' BuffIt2TheLimit/BuffIt2TheLimit.csproj
   ```
   Gültige Form: `X.Y.Z` (drei Zahlen). Ungültig: Abbruch mit „Fehler: Keine gültige Semver-Version in csproj gefunden."

4. **Neue Version berechnen** anhand des Release-Typs:
   - `patch`: Z+1
   - `minor`: Y+1, Z=0
   - `major`: X+1, Y=0, Z=0

5. **Tag noch nicht vorhanden?**
   ```
   git rev-parse "vX.Y.Z" 2>/dev/null
   ```
   Existiert bereits: Abbruch mit „Fehler: Tag vX.Y.Z existiert bereits. Version prüfen."

---

## Schritt 3: Release Notes generieren

1. **Letzten Tag finden:**
   ```
   git describe --tags --abbrev=0 --match 'v*'
   ```

2. **Commits seit letztem Tag lesen:**
   ```
   git log <letzter-tag>..HEAD --oneline
   ```

3. **`chore:`-Commits herausfiltern** (Versions-Bumps, Repository.json-Updates — kein Mehrwert für User).

4. **Release Notes erstellen (GitHub Markdown):**

   ```markdown
   ## What's New

   - <Bullet-Liste aus gefilterten Commits, als lesbarer Satz formuliert>

   ## Installation

   1. Download `BuffIt2TheLimit-X.Y.Z.zip`
   2. Extract into `{GameDir}/Mods/BuffIt2TheLimit/`
   3. Enable in Unity Mod Manager

   ## Requirements

   - [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) 0.23.0+
   - Pathfinder: Wrath of the Righteous 1.4+
   ```

   Nexus-Upload wird automatisch von der GitHub Action übernommen (`.github/workflows/nexus-upload.yml`).

5. **Dem User die Release Notes zeigen** und fragen: „Release Notes so in Ordnung, oder soll ich etwas anpassen?"

   Warte auf Freigabe oder Änderungswünsche. Überarbeite bis der User zustimmt.

---

## Schritt 4: Versions-Bump

Aktualisiere die Version in exakt diesen drei Dateien:

1. **`BuffIt2TheLimit/BuffIt2TheLimit.csproj`** — `<Version>X.Y.Z</Version>` ersetzen
2. **`BuffIt2TheLimit/Info.json`** — `"Version": "X.Y.Z"` ersetzen
3. **`Repository.json`** — `"Version": "X.Y.Z"` und `"DownloadUrl"` auf:
   ```
   https://github.com/Gh05d/wrath-epic-buffing/releases/download/vX.Y.Z/BuffIt2TheLimit-X.Y.Z.zip
   ```

Commit erstellen:
```
git add BuffIt2TheLimit/BuffIt2TheLimit.csproj BuffIt2TheLimit/Info.json Repository.json
git commit -m "chore: bump version to X.Y.Z"
```

---

## Schritt 5: Build

```
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo
```

Danach prüfen ob das ZIP existiert:
```
ls BuffIt2TheLimit/bin/BuffIt2TheLimit-X.Y.Z.zip
```

**Build schlägt fehl oder ZIP nicht vorhanden:**
```
git reset --soft HEAD~1
git restore --staged BuffIt2TheLimit/BuffIt2TheLimit.csproj BuffIt2TheLimit/Info.json Repository.json
```
Abbruch mit „Fehler: Build fehlgeschlagen. Versions-Bump wurde rückgängig gemacht."

---

## Schritt 6: Bestätigungs-Gate (Point of No Return)

Zeige dem User eine Zusammenfassung:

```
=== Release bereit ===
Version:  vX.Y.Z
ZIP:      BuffIt2TheLimit/bin/BuffIt2TheLimit-X.Y.Z.zip

Was jetzt passiert:
  1. git push fork master
  2. git tag -a vX.Y.Z
  3. git push fork vX.Y.Z
  4. GitHub Release erstellen mit ZIP-Upload
  5. GitHub Action lädt automatisch zu Nexus Mods hoch

GitHub Release Notes (Vorschau):
<GitHub Markdown Notes>

Fortfahren? (ja/nein)
```

**User sagt nein oder bricht ab:**
```
git reset --soft HEAD~1
git restore --staged BuffIt2TheLimit/BuffIt2TheLimit.csproj BuffIt2TheLimit/Info.json Repository.json
```
Meldung: „Release abgebrochen. Versions-Bump rückgängig gemacht."

---

## Schritt 7: Push, Tag, GitHub Release

Reihenfolge ist wichtig — Code erst pushen, dann taggen:

1. **Push master:**
   ```
   git push fork master
   ```
   Schlägt fehl:
   ```
   git reset --soft HEAD~1
   git restore --staged BuffIt2TheLimit/BuffIt2TheLimit.csproj BuffIt2TheLimit/Info.json Repository.json
   ```
   Abbruch mit „Fehler: Push fehlgeschlagen. Versions-Bump rückgängig gemacht."

2. **Tag erstellen:**
   ```
   git tag -a vX.Y.Z -m "Release vX.Y.Z"
   ```

3. **Tag pushen:**
   ```
   git push fork vX.Y.Z
   ```
   Schlägt fehl: Lokalen Tag löschen:
   ```
   git tag -d vX.Y.Z
   ```
   Meldung: „Tag-Push fehlgeschlagen. Manuell ausführen: `git push fork vX.Y.Z`"

4. **GitHub Release erstellen:**
   ```
   gh release create vX.Y.Z "BuffIt2TheLimit/bin/BuffIt2TheLimit-X.Y.Z.zip" \
     --repo Gh05d/wrath-epic-buffing \
     --title "Buff It 2 The Limit vX.Y.Z" \
     --notes "<GitHub Markdown Notes>"
   ```
   Schlägt fehl: Manuellen Befehl anzeigen und weitermachen.

---

## Schritt 8: Abschluss

Prüfe ob die GitHub Action für den Nexus-Upload erfolgreich war:
```
gh run list --repo Gh05d/wrath-epic-buffing --limit 1
```

Zeige dem User die Zusammenfassung:

```
=== Release vX.Y.Z abgeschlossen! ===

GitHub: https://github.com/Gh05d/wrath-epic-buffing/releases/tag/vX.Y.Z
Nexus:  Automatisch hochgeladen via GitHub Action (Status: <success/failure>)
```

Falls die GitHub Action fehlgeschlagen ist, zeige den manuellen Nexus-Upload-Link:
```
Nexus Upload (manuell): https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/195?tab=files
ZIP: BuffIt2TheLimit/bin/BuffIt2TheLimit-X.Y.Z.zip
```

---

## Fehlerbehandlung — Übersicht

| Fehler | Verhalten |
|--------|-----------|
| Dirty working tree | Abbruch vor jeder Änderung |
| Nicht auf master | Abbruch vor jeder Änderung |
| Keine gültige Semver | Abbruch vor jeder Änderung |
| Tag existiert bereits | Abbruch vor jeder Änderung |
| Build schlägt fehl | Bump-Commit rückgängig machen, Abbruch |
| User bricht am Gate ab | Bump-Commit rückgängig machen |
| Push master schlägt fehl | Bump-Commit rückgängig machen, Abbruch |
| Push tag schlägt fehl | Lokalen Tag löschen, manuellen Befehl zeigen |
| GitHub Release schlägt fehl | Manuellen `gh release create`-Befehl zeigen |
