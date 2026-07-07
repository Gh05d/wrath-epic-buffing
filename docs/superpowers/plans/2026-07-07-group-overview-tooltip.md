# Group Overview Hover Tooltip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hovering a buff category's HUD button or its summary label in the buff window shows a live list of the buffs assigned to that category.

**Architecture:** One new lazy tooltip template class (`TooltipTemplateGroupBuffs : TooltipBaseTemplate`) that queries the live `BufferState` in `GetBody()` at hover time — no refresh bookkeeping. It is attached at two existing integration points: the three HUD group buttons (replacing their static `TooltipTemplateSimple`) and the three per-group summary labels in the buff window (via `TooltipHelper.SetTooltip`, which works on any `MonoBehaviour` — IL-verified).

**Tech Stack:** C#/.NET Framework 4.8.1 Unity mod for Pathfinder: WotR. Game-native Owlcat tooltip API (`TooltipBaseTemplate`, tooltip bricks). No test project exists — the test cycle per task is a successful build; behavior verification happens on the Steam Deck in Task 4.

**Spec:** `docs/superpowers/specs/2026-07-07-group-overview-tooltip-design.md`

## Global Constraints

- Build command (from repo root): `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/` — expected output contains `Build succeeded`. The `-p:SolutionDir=$(pwd)/` part is REQUIRED on Linux; without it all game-DLL references silently break.
- Every new localization key MUST be added to ALL FIVE files: `BuffIt2TheLimit/Config/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json`. A key missing from en_GB.json crashes the game with uncatchable infinite recursion.
- Preserve each locale file's BOM state: en_GB and de_DE start with a UTF-8 BOM (`ef bb bf`), fr_FR/ru_RU/zh_CN do not. Edits below line 1 (via the Edit tool) leave the BOM untouched — do not rewrite whole files.
- Code style: K&R braces (opening brace on same line), 4-space indentation, `var` when the type is apparent.
- Do NOT bump the version in csproj/Info.json/Repository.json — the `/release` skill handles that later and requires the pre-bump version.
- Commit directly on `master` (user preference for this repo).
- The `BuffGroup` enum→locale-key mapping is NOT 1:1: `Long`→`normal`, `Quick`→`short`, `Important`→`important`.

---

### Task 1: `TooltipTemplateGroupBuffs` template class + localization keys

**Files:**
- Create: `BuffIt2TheLimit/TooltipTemplateGroupBuffs.cs`
- Modify: `BuffIt2TheLimit/Config/en_GB.json:29` (insert after)
- Modify: `BuffIt2TheLimit/Config/de_DE.json:29` (insert after)
- Modify: `BuffIt2TheLimit/Config/fr_FR.json:29` (insert after)
- Modify: `BuffIt2TheLimit/Config/ru_RU.json:29` (insert after)
- Modify: `BuffIt2TheLimit/Config/zh_CN.json:29` (insert after)

**Interfaces:**
- Consumes: `BuffGroup` enum (`BubbleBuffer.cs:3211` — values `Long`, `Quick`, `Important`); `GlobalBubbleBuffer.Instance.SpellbookController.state` (`BubbleBuffer.cs:95`, type `BufferState`, field `public BufferState state`); `BufferState.BuffList` (`BufferState.cs:36`, `IEnumerable<BubbleBuff>`, **null before the first scan**); `BubbleBuff.InGroups` (`HashSet<BuffGroup>`), `BubbleBuff.Name`, `BubbleBuff.NameMeta`, `BubbleBuff.Icon` (`BubbleBuff.cs:57,119-124`); `"…".i8()` string extension (class `Language`, `Config/ModSettings.cs:61`).
- Produces: `class TooltipTemplateGroupBuffs : TooltipBaseTemplate` in namespace `BuffIt2TheLimit`, constructor `TooltipTemplateGroupBuffs(BuffGroup group)`. Tasks 2 and 3 construct it and pass it to `SetTooltip`.

- [ ] **Step 1: Create the template class**

Create `BuffIt2TheLimit/TooltipTemplateGroupBuffs.cs` with exactly this content (SDK-style csproj auto-globs new `.cs` files — no csproj edit needed):

```csharp
using BuffIt2TheLimit.Config;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;
using System.Collections.Generic;
using System.Linq;

namespace BuffIt2TheLimit {
    class TooltipTemplateGroupBuffs : TooltipBaseTemplate {
        private const int MaxEntries = 25;
        private readonly BuffGroup group;

        public TooltipTemplateGroupBuffs(BuffGroup group) {
            this.group = group;
        }

        private string KeyPrefix => group switch {
            BuffGroup.Long => "group.normal",
            BuffGroup.Quick => "group.short",
            BuffGroup.Important => "group.important",
            _ => "group.normal"
        };

        public override IEnumerable<ITooltipBrick> GetHeader(TooltipTemplateType type) {
            yield return new TooltipBrickEntityHeader($"{KeyPrefix}.tooltip.header".i8(), null);
        }

        public override IEnumerable<ITooltipBrick> GetBody(TooltipTemplateType type) {
            List<ITooltipBrick> elements = new();
            elements.Add(new TooltipBrickText($"{KeyPrefix}.tooltip.desc".i8()));
            elements.Add(new TooltipBrickSeparator());

            var buffList = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.BuffList;
            var assigned = buffList?.Where(b => b.InGroups.Contains(group))
                                    .OrderBy(b => b.Name)
                                    .ToList() ?? new List<BubbleBuff>();

            if (assigned.Count == 0) {
                elements.Add(new TooltipBrickText("group.overview.empty".i8()));
                return elements;
            }

            foreach (var buff in assigned.Take(MaxEntries)) {
                // buff.Icon, not buff.Spell.Icon — null-safe for fused/MagicHack spells
                elements.Add(new TooltipBrickIconAndName(buff.Icon, $"<b>{buff.NameMeta}</b>", TooltipBrickElementType.Small));
            }

            if (assigned.Count > MaxEntries) {
                elements.Add(new TooltipBrickText(string.Format("group.overview.more".i8(), assigned.Count - MaxEntries)));
            }

            return elements;
        }
    }
}
```

Namespace facts (IL-verified, don't second-guess): `ITooltipBrick`, `TooltipBaseTemplate`, `TooltipTemplateType` live in `Owlcat.Runtime.UI.Tooltips`; all `TooltipBrick*` classes live in `Kingmaker.UI.MVVM._VM.Tooltip.Bricks`.

- [ ] **Step 2: Add the two locale keys to all five locale files**

Each file has `"group.short.tooltip.desc"` on line 29. Using the Edit tool, insert the two new lines directly AFTER that line in each file (this preserves line-1 BOM state). Values per file:

`en_GB.json` — anchor line: `  "group.short.tooltip.desc": "Try to cast buffs set in the buff window (Quick Buffs)",`
```json
  "group.overview.empty": "No buffs assigned",
  "group.overview.more": "… and {0} more",
```

`de_DE.json` — anchor line: `  "group.short.tooltip.desc": "Als <b>Schnell<b> konfigurierte Buffs einsetzen.",`
```json
  "group.overview.empty": "Keine Buffs zugewiesen",
  "group.overview.more": "… und {0} weitere",
```

`fr_FR.json` — anchor line: `  "group.short.tooltip.desc": "Tente de lancer les sorts configurés dans la fenêtre de buff (Buffs Rapides)",`
```json
  "group.overview.empty": "Aucun buff assigné",
  "group.overview.more": "… et {0} de plus",
```

`ru_RU.json` — anchor line: `  "group.short.tooltip.desc": "Попробовать бафнуть группой (Быстрые)",`
```json
  "group.overview.empty": "Баффы не назначены",
  "group.overview.more": "… и ещё {0}",
```

`zh_CN.json` — anchor line: `  "group.short.tooltip.desc": "尝试施放在Buff窗口中设置的Buff (快速施法)",`
```json
  "group.overview.empty": "未分配Buff",
  "group.overview.more": "……还有 {0} 个",
```

(de_DE deliberately keeps the English gaming term "Buffs"; zh_CN keeps "Buff" — both match each file's existing style.)

- [ ] **Step 3: Validate JSON + BOM state of all five files**

Run from repo root:
```bash
for f in BuffIt2TheLimit/Config/{en_GB,de_DE,fr_FR,ru_RU,zh_CN}.json; do
  python3 -c "import json; json.load(open('$f', encoding='utf-8-sig'))" && echo "$f JSON ok"
  head -c3 "$f" | od -An -tx1
done
```
Expected: `JSON ok` for all five; first-bytes line shows `ef bb bf` for en_GB and de_DE only (fr_FR/ru_RU/zh_CN show their first content bytes instead, e.g. `7b` for `{`).

- [ ] **Step 4: Build**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded`. (Harmless `findstr` warnings on Linux are expected.)

- [ ] **Step 5: Commit**

```bash
git add BuffIt2TheLimit/TooltipTemplateGroupBuffs.cs BuffIt2TheLimit/Config/en_GB.json BuffIt2TheLimit/Config/de_DE.json BuffIt2TheLimit/Config/fr_FR.json BuffIt2TheLimit/Config/ru_RU.json BuffIt2TheLimit/Config/zh_CN.json
git commit -m "feat(tooltip): add lazy group-overview tooltip template + locale keys"
```

---

### Task 2: Wire the template onto the three HUD group buttons

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:3045-3069` (local function `AddButton` inside `GlobalBubbleBuffer.TryInstallUI` and its call sites)

**Interfaces:**
- Consumes: `TooltipTemplateGroupBuffs(BuffGroup)` from Task 1; existing local `AddButton(string, string, ButtonSprites, Action)`.
- Produces: new local function `AddButtonWithTemplate(TooltipBaseTemplate, ButtonSprites, Action)`; the three group-button call sites use it. (C# local functions CANNOT be overloaded — same name twice is compile error CS0128, hence the distinct name.)

- [ ] **Step 1: Split `AddButton` into a template-taking core + string-based wrapper**

In `BubbleBuffer.cs`, replace the current `AddButton` local function (lines 3045-3064):

```csharp
                void AddButton(string text, string tooltip, ButtonSprites sprites, Action act) {
                    var applyBuffsButton = GameObject.Instantiate(prefab, buttonsContainer.transform);
                    applyBuffsButton.SetActive(true);
                    OwlcatButton button = applyBuffsButton.GetComponentInChildren<OwlcatButton>();
                    button.m_CommonLayer[0].SpriteState = new SpriteState {
                        pressedSprite = sprites.down,
                        highlightedSprite = sprites.hover,
                    };
                    button.OnLeftClick.AddListener(() => {
                        act();
                    });
                    button.SetTooltip(new TooltipTemplateSimple(text, tooltip), new TooltipConfig {
                        InfoCallPCMethod = InfoCallPCMethod.None
                    });

                    Buttons.Add(button);

                    applyBuffsButton.GetComponentInChildren<Image>().sprite = sprites.normal;

                }
```

with:

```csharp
                void AddButtonWithTemplate(TooltipBaseTemplate template, ButtonSprites sprites, Action act) {
                    var applyBuffsButton = GameObject.Instantiate(prefab, buttonsContainer.transform);
                    applyBuffsButton.SetActive(true);
                    OwlcatButton button = applyBuffsButton.GetComponentInChildren<OwlcatButton>();
                    button.m_CommonLayer[0].SpriteState = new SpriteState {
                        pressedSprite = sprites.down,
                        highlightedSprite = sprites.hover,
                    };
                    button.OnLeftClick.AddListener(() => {
                        act();
                    });
                    button.SetTooltip(template, new TooltipConfig {
                        InfoCallPCMethod = InfoCallPCMethod.None
                    });

                    Buttons.Add(button);

                    applyBuffsButton.GetComponentInChildren<Image>().sprite = sprites.normal;

                }

                void AddButton(string text, string tooltip, ButtonSprites sprites, Action act) {
                    AddButtonWithTemplate(new TooltipTemplateSimple(text, tooltip), sprites, act);
                }
```

- [ ] **Step 2: Switch the three group-button call sites to the live template**

Replace lines 3067-3069:

```csharp
                AddButton("group.normal.tooltip.header".i8(), "group.normal.tooltip.desc".i8(), applyBuffsSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Long));
                AddButton("group.important.tooltip.header".i8(), "group.important.tooltip.desc".i8(), applyBuffsImportantSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Important));
                AddButton("group.short.tooltip.header".i8(), "group.short.tooltip.desc".i8(), applyBuffsShortSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Quick));
```

with:

```csharp
                AddButtonWithTemplate(new TooltipTemplateGroupBuffs(BuffGroup.Long), applyBuffsSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Long));
                AddButtonWithTemplate(new TooltipTemplateGroupBuffs(BuffGroup.Important), applyBuffsImportantSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Important));
                AddButtonWithTemplate(new TooltipTemplateGroupBuffs(BuffGroup.Quick), applyBuffsShortSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Quick));
```

The show-map button (line ~3070) and open-buffs button (line ~3082) keep calling `AddButton(...)` unchanged.

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat(tooltip): HUD group buttons show live buff list on hover"
```

---

### Task 3: Wire the template onto the buff-window summary labels

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:3838-3844` (`BufferView.MakeSummary`)

**Interfaces:**
- Consumes: `TooltipTemplateGroupBuffs(BuffGroup)` from Task 1; `TooltipHelper.SetTooltip(MonoBehaviour, TooltipBaseTemplate, TooltipConfig)` (namespace `Kingmaker.UI.MVVM._VM.Tooltip.Utils`, already imported in BubbleBuffer.cs line 19; IL-verified it constructs its own `TooltipHandler`, so it works on a plain `TextMeshProUGUI`).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Attach tooltip to each summary label**

In `MakeSummary` (BubbleBuffer.cs:3838-3844), replace the loop body:

```csharp
            foreach (BuffGroup group in Enum.GetValues(typeof(BuffGroup))) {
                var l = GameObject.Instantiate(BigLabelPrefab, rect.transform);
                var label = l.GetComponent<TextMeshProUGUI>();
                label.text = MakeSummaryLabel(group, 0, 0);
                l.SetActive(true);
                groupSummaryLabels[group] = label;
            }
```

with:

```csharp
            foreach (BuffGroup group in Enum.GetValues(typeof(BuffGroup))) {
                var l = GameObject.Instantiate(BigLabelPrefab, rect.transform);
                var label = l.GetComponent<TextMeshProUGUI>();
                label.text = MakeSummaryLabel(group, 0, 0);
                label.raycastTarget = true;
                TooltipHelper.SetTooltip(label, new TooltipTemplateGroupBuffs(group), new TooltipConfig {
                    InfoCallPCMethod = InfoCallPCMethod.None
                });
                l.SetActive(true);
                groupSummaryLabels[group] = label;
            }
```

No stale-handler bookkeeping is needed: `MakeSummary` clears and re-creates the label GameObjects on every window build, so each fresh label gets a fresh handler.

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat(tooltip): buff-window summary labels show live buff list on hover"
```

---

### Task 4: Deploy to Steam Deck + manual verification

**Files:**
- None modified (deploy + verify only).

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: verified feature; go/no-go on the lazy-rebuild assumption.

- [ ] **Step 1: Guard against the stale-build gotcha, then deploy**

`dotnet build` has been observed skipping recompilation despite edited sources. Check DLL mtime is NEWER than the newest source edit:
```bash
ls -l BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll BuffIt2TheLimit/TooltipTemplateGroupBuffs.cs BuffIt2TheLimit/BubbleBuffer.cs
```
If the DLL is older: `touch BuffIt2TheLimit/BubbleBuffer.cs` and rebuild. Then:
```bash
./deploy.sh
```
Expected: DLL + Info.json SCP'd to the Deck mod directory without error.

- [ ] **Step 2: Manual verification on the Deck (user plays; full game restart, NOT UMM hot-reload — hot-reload leaves stale delegates)**

Checklist to hand to the user (all six from the spec):
1. Hover each of the three HUD buttons → tooltip shows header, description, then the icon+name list of assigned buffs.
2. Open the buff window, hover each of the three summary labels ("Normal Buffs 3/5" etc.) → same lists.
3. **Lazy-rebuild proof (the spec's known risk):** toggle a buff's group checkbox, then WITHOUT reopening anything hover the HUD button again → list reflects the change. If it does NOT, the engine caches templates — implement the spec's fallback (re-call `SetTooltip` on window open / after toggles) as a follow-up task.
4. Save, load, hover again → no exceptions, lists correct.
5. A group with zero buffs shows "No buffs assigned".
6. `>25` buffs in one group shows "… and N more" (optional — only if a late-game save is handy; otherwise trust the `Take(MaxEntries)` code path).

- [ ] **Step 3: Check Player.log for mod exceptions**

Run the `/check-logs` skill (greps the Deck's `Player.log` for `BuffIt2TheLimit|Exception|Error` over SSH).
Expected: no new exceptions referencing `TooltipTemplateGroupBuffs` or `MakeSummary`.

- [ ] **Step 4: Done — hand off to release flow**

No commit in this task. Release (version bump, tag, Nexus upload) happens later via `/release` per the standard process; csproj must still hold the pre-bump version.
