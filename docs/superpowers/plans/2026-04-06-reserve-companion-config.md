# Reserve Companion Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow players to configure buff assignments for all recruited companions (including those in reserve), not just the active party.

**Architecture:** New `Bubble.ConfigGroup` provides the extended character list for UI and scanning, while `Bubble.Group` remains the execution scope. Portrait row becomes scrollable with a separator + overlay distinguishing reserve characters. Toggle button in the buff window controls visibility.

**Tech Stack:** C#/.NET 4.8.1, Unity UI (ScrollRect, HorizontalLayoutGroup), Harmony, Newtonsoft.Json

---

### Task 1: Add `Bubble.ShowReserve` and `Bubble.ConfigGroup`

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:3272-3301` (Bubble class)

- [ ] **Step 1: Add ShowReserve field and ConfigGroup property**

In the `Bubble` static class (line 3272), add the toggle state and the new property after the existing `GroupById` field:

```csharp
static class Bubble {
    public static List<UnitEntityData> Group = new();
    public static List<UnitEntityData> ConfigGroup = new();
    public static Dictionary<string, UnitEntityData> GroupById = new();
    public static bool ShowReserve = false;
```

- [ ] **Step 2: Build ConfigGroup in RefreshGroup()**

Replace the `RefreshGroup()` method (lines 3276-3301) to also build `ConfigGroup`:

```csharp
public static void RefreshGroup() {
    var baseGroup = Game.Instance.SelectionCharacter.ActualGroup;
    var result = new List<UnitEntityData>(baseGroup);

    foreach (var unit in baseGroup) {
        var petMaster = unit.Get<UnitPartPetMaster>();
        if (petMaster == null) continue;

        var pets = new List<UnitEntityData>();
        foreach (var petRef in petMaster.Pets) {
            var pet = petRef.Entity;
            if (pet != null && pet.IsInGame && !result.Contains(pet)) {
                pets.Add(pet);
            }
        }
        pets.Sort((a, b) => string.Compare(a.UniqueId, b.UniqueId, StringComparison.Ordinal));
        result.AddRange(pets);
    }

    Group = result;

    if (ShowReserve) {
        var config = new List<UnitEntityData>(result);
        var activeIds = new HashSet<string>(result.Select(u => u.UniqueId));

        foreach (var unit in Game.Instance.Player.AllCharacters) {
            if (activeIds.Contains(unit.UniqueId)) continue;

            config.Add(unit);

            var petMaster = unit.Get<UnitPartPetMaster>();
            if (petMaster == null) continue;

            var pets = new List<UnitEntityData>();
            foreach (var petRef in petMaster.Pets) {
                var pet = petRef.Entity;
                if (pet != null && !activeIds.Contains(pet.UniqueId) && !config.Contains(pet)) {
                    pets.Add(pet);
                }
            }
            pets.Sort((a, b) => string.Compare(a.UniqueId, b.UniqueId, StringComparison.Ordinal));
            config.AddRange(pets);
        }
        ConfigGroup = config;
    } else {
        ConfigGroup = result;
    }

    GroupById.Clear();
    foreach (var u in ConfigGroup) {
        GroupById[u.UniqueId] = u;
    }
}
```

Key changes:
- `GroupById` is built from `ConfigGroup` (not `Group`) so `CanTarget` lookups work for reserve chars
- Reserve chars + their pets are appended after the active party block
- Pet `IsInGame` filter is removed for reserve chars (their pets may not be in-scene but are still configurable)

- [ ] **Step 3: Build and verify compilation**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

Expected: Build succeeds. No runtime change yet (ShowReserve defaults to false, ConfigGroup == Group).

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add Bubble.ShowReserve and ConfigGroup for reserve companion support"
```

---

### Task 2: Route scanning and save/load through ConfigGroup

**Files:**
- Modify: `BuffIt2TheLimit/BufferState.cs:403-408` (Recalculate method)
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1952` (RevalidateSpells)
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1975` (AbilityCache.Revalidate)
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:218-221` (InitialiseFromSave)

- [ ] **Step 1: Change Recalculate to pass ConfigGroup**

In `BufferState.Recalculate()` (line 403), change the group variable:

```csharp
internal void Recalculate(bool updateUi, BuffGroup? priorityGroup = null) {
    Bubble.RefreshGroup();
    var group = Bubble.ConfigGroup;
    if (InputDirty || GroupIsDirty(group)) {
        AbilityCache.Revalidate();
        RecalculateAvailableBuffs(group);
    }
```

- [ ] **Step 2: Update AbilityCache.Revalidate to use ConfigGroup**

In `AbilityCache.Revalidate()` (BubbleBuffer.cs line 1975), change:

```csharp
public static void Revalidate() {
    Main.Verbose("Revalidating Caster Cache");
    CasterCache.Clear();
    foreach (var u in Bubble.ConfigGroup) {
```

- [ ] **Step 3: Update RevalidateSpells to use ConfigGroup**

In `BubbleBuffSpellbookController.RevalidateSpells()` (BubbleBuffer.cs line 1952), change:

```csharp
internal void RevalidateSpells() {
    if (state.GroupIsDirty(Bubble.ConfigGroup)) {
        AbilityCache.Revalidate();
    }

    state.InputDirty = true;
}
```

- [ ] **Step 4: Update InitialiseFromSave to use ConfigGroup**

In `BubbleBuff.InitialiseFromSave()` (BubbleBuff.cs line 218), change:

```csharp
for (int i = 0; i < Bubble.ConfigGroup.Count; i++) {
    UnitEntityData u = Bubble.ConfigGroup[i];
    if (state.Wanted.Contains(u.UniqueId))
        SetUnitWants(u, true);
}
```

- [ ] **Step 5: Build and verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 6: Commit**

```bash
git add BuffIt2TheLimit/BufferState.cs BuffIt2TheLimit/BubbleBuffer.cs BuffIt2TheLimit/BubbleBuff.cs
git commit -m "feat: route scanning and save/load through ConfigGroup"
```

---

### Task 3: Filter Validate to active party only

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:288-385` (Validate, ValidateMass)

During execution, only active party members can cast or receive buffs. Reserve chars may be in `wanted` and `CasterQueue` but must be skipped.

- [ ] **Step 1: Add active-party guard to Validate()**

In `BubbleBuff.Validate()` (line 288), add filtering for both targets and casters:

```csharp
public void Validate() {
    if (IsSong) {
        ValidateSong();
        return;
    }
    if (IsMass) {
        ValidateMass();
        return;
    }
    foreach (var target in wanted) {
        // Skip reserve characters — can't buff someone not in the active party
        if (!Bubble.Group.Any(u => u.UniqueId == target)) continue;

        for (int n = 0; n < CasterQueue.Count; n++) {
            var caster = CasterQueue[n];

            // Skip reserve casters — can't cast if not in the active party
            if (!Bubble.Group.Any(u => u.UniqueId == caster.who.UniqueId)) continue;

            // Skip disabled source types
            if (caster.SourceType == BuffSourceType.Spell && !UseSpells) continue;
            if (caster.SourceType == BuffSourceType.Scroll && !UseScrolls) continue;
            if (caster.SourceType == BuffSourceType.Potion && !UsePotions) continue;
            if (caster.SourceType == BuffSourceType.Equipment && !UseEquipment) continue;

            // ... rest unchanged from line 308 onward
```

- [ ] **Step 2: Add active-party guard to ValidateMass()**

In `BubbleBuff.ValidateMass()` (line 341), add caster filtering and adjust target selection:

```csharp
private void ValidateMass() {
    if (wanted.Count == 0) return;

    // Azata Zippy Magic is disabled for IsMass spells in EngineCastingHandler,
    // so no Zippy credit adjustment needed here.

    // For mass/communal spells: find one caster, consume one credit, cast once.
    // All wanted targets are marked as given since the spell affects everyone.
    for (int n = 0; n < CasterQueue.Count; n++) {
        var caster = CasterQueue[n];

        // Skip reserve casters
        if (!Bubble.Group.Any(u => u.UniqueId == caster.who.UniqueId)) continue;

        // Skip disabled source types
        if (caster.SourceType == BuffSourceType.Spell && !UseSpells) continue;
        if (caster.SourceType == BuffSourceType.Scroll && !UseScrolls) continue;
        if (caster.SourceType == BuffSourceType.Potion && !UsePotions) continue;
        if (caster.SourceType == BuffSourceType.Equipment && !UseEquipment) continue;

        var creditsNeeded = CreditsNeeded(caster.spell);
        if (caster.AvailableCredits < creditsNeeded) continue;
        if (!caster.SlottedSpell.IsAvailable) continue;

        // Find a wanted target IN THE ACTIVE PARTY this caster can reach
        string validTarget = null;
        foreach (var t in wanted) {
            if (!Bubble.Group.Any(u => u.UniqueId == t)) continue; // Skip reserve targets
            if (caster.CanTarget(t)) {
                validTarget = t;
                break;
            }
        }
        if (validTarget == null) continue;

        caster.ChargeCredits(creditsNeeded);
        caster.spent += creditsNeeded;

        if (ActualCastQueue == null)
            ActualCastQueue = new();
        ActualCastQueue.Add((validTarget, caster));

        // Mark all wanted targets IN THE ACTIVE PARTY as given
        foreach (var target in wanted) {
            if (Bubble.Group.Any(u => u.UniqueId == target))
                given.Add(target);
        }

        return;
    }
}
```

- [ ] **Step 3: ValidateSong needs no changes**

`ValidateSong()` (line 387) uses `CasterQueue[0]` which is always the song owner. Songs are self-cast only, so reserve chars would only be in the queue for their own songs — which can't execute anyway since they're not in the scene. The `ActivatableSource.IsAvailable` check will naturally fail for absent characters. No code change needed.

- [ ] **Step 4: Build and verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 5: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuff.cs
git commit -m "feat: filter Validate/ValidateMass to active party during execution"
```

---

### Task 4: Add localization strings

**Files:**
- Modify: `BuffIt2TheLimit/Config/en_GB.json`
- Modify: `BuffIt2TheLimit/Config/de_DE.json`
- Modify: `BuffIt2TheLimit/Config/fr_FR.json`
- Modify: `BuffIt2TheLimit/Config/ru_RU.json`
- Modify: `BuffIt2TheLimit/Config/zh_CN.json`

- [ ] **Step 1: Add keys to en_GB.json**

Add before the closing `}`:

```json
  "reserve.toggle": "Show Reserve",
  "reserve.toggle.tooltip": "Show all recruited companions, including those not in the active party",
  "reserve.portrait.tooltip": "Not in active party"
```

- [ ] **Step 2: Add keys to de_DE.json**

```json
  "reserve.toggle": "Reserve anzeigen",
  "reserve.toggle.tooltip": "Alle rekrutierten Begleiter anzeigen, auch wenn sie nicht in der aktiven Gruppe sind",
  "reserve.portrait.tooltip": "Nicht in aktiver Gruppe"
```

- [ ] **Step 3: Add English fallback to fr_FR.json, ru_RU.json, zh_CN.json**

Add the same English keys to all three files:

```json
  "reserve.toggle": "Show Reserve",
  "reserve.toggle.tooltip": "Show all recruited companions, including those not in the active party",
  "reserve.portrait.tooltip": "Not in active party"
```

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/Config/
git commit -m "feat: add localization strings for reserve companion toggle"
```

---

### Task 5: Make portrait row scrollable

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1846-1905` (MakeGroupHolder method)

This makes the portrait row scrollable regardless of reserve mode — benefits any party with many pets.

- [ ] **Step 1: Wrap portrait HorizontalLayoutGroup in a ScrollRect**

Replace the `MakeGroupHolder` method (line 1846) with a scrollable version. The existing `groupHolder` with its `HorizontalLayoutGroup` becomes the content inside a ScrollRect:

```csharp
private void MakeGroupHolder(GameObject portraitPrefab, GameObject expandButtonPrefab, GameObject buttonPrefab, Transform content) {
    // ScrollRect viewport
    var scrollObj = new GameObject("PortraitScroll", typeof(RectTransform));
    var scrollRect = scrollObj.GetComponent<RectTransform>();
    scrollRect.AddTo(content);

    scrollRect.anchorMin = new Vector2(0.25f, 0f);
    scrollRect.anchorMax = new Vector2(1f, 1f);
    scrollRect.pivot = new Vector2(0.5f, 0.5f);
    scrollRect.offsetMin = new Vector2(2, 4);
    scrollRect.offsetMax = new Vector2(-4, -4);

    var scroll = scrollObj.AddComponent<ScrollRect>();
    scroll.horizontal = true;
    scroll.vertical = false;
    scroll.movementType = ScrollRect.MovementType.Clamped;
    scroll.scrollSensitivity = 30f;

    // Viewport with mask
    var viewportObj = new GameObject("Viewport", typeof(RectTransform));
    var viewportRect = viewportObj.GetComponent<RectTransform>();
    viewportRect.SetParent(scrollRect, false);
    viewportRect.anchorMin = Vector2.zero;
    viewportRect.anchorMax = Vector2.one;
    viewportRect.offsetMin = Vector2.zero;
    viewportRect.offsetMax = Vector2.zero;
    viewportObj.AddComponent<RectMask2D>();

    scroll.viewport = viewportRect;

    // Content container (HorizontalLayoutGroup)
    var groupHolder = new GameObject("GroupHolder", typeof(RectTransform));
    var groupRect = groupHolder.GetComponent<RectTransform>();
    groupRect.SetParent(viewportRect, false);
    groupRect.anchorMin = new Vector2(0, 0);
    groupRect.anchorMax = new Vector2(0, 1);
    groupRect.pivot = new Vector2(0, 0.5f);
    groupRect.offsetMin = Vector2.zero;
    groupRect.offsetMax = Vector2.zero;

    const float groupHeight = 100f;

    var horizontalGroup = groupHolder.AddComponent<HorizontalLayoutGroup>();
    horizontalGroup.spacing = 6;
    horizontalGroup.childControlHeight = true;
    horizontalGroup.childForceExpandHeight = false;
    horizontalGroup.childControlWidth = false;
    horizontalGroup.childForceExpandWidth = false;
    horizontalGroup.childAlignment = TextAnchor.MiddleLeft;

    var contentFitter = groupHolder.AddComponent<ContentSizeFitter>();
    contentFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    contentFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

    scroll.content = groupRect;

    view.targets = new Portrait[Bubble.ConfigGroup.Count];

    for (int i = 0; i < Bubble.ConfigGroup.Count; i++) {
        bool isReserve = i >= Bubble.Group.Count;

        // Add separator before first reserve character
        if (isReserve && i == Bubble.Group.Count) {
            var separator = new GameObject("ReserveSeparator", typeof(RectTransform));
            var sepRect = separator.GetComponent<RectTransform>();
            sepRect.SetParent(groupRect, false);
            var sepImage = separator.AddComponent<Image>();
            sepImage.color = new Color(1f, 1f, 1f, 0.3f);
            var sepLayout = separator.AddComponent<LayoutElement>();
            sepLayout.preferredWidth = 2;
            sepLayout.flexibleWidth = 0;
        }

        Portrait portrait = CreatePortrait(groupHeight, groupRect, false, false);

        portrait.GameObject.SetActive(true);
        var aspect = portrait.GameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        aspect.aspectRatio = 0.75f;

        portrait.Image.sprite = Bubble.ConfigGroup[i].Portrait.SmallPortrait;

        // Dim reserve portraits
        if (isReserve) {
            portrait.Image.color = new Color(1f, 1f, 1f, 0.5f);
            portrait.Button.SetTooltip(
                new TooltipTemplateSimple(Bubble.ConfigGroup[i].CharacterName, "reserve.portrait.tooltip".i8()),
                new TooltipConfig { InfoCallPCMethod = InfoCallPCMethod.None });
        }

        int personIndex = i;

        portrait.Button.OnLeftClick.AddListener(() => {
            UnitEntityData me = Bubble.ConfigGroup[personIndex];
            var buff = view.Selected;
            if (buff == null)
                return;

            if (!buff.CanTarget(me))
                return;

            if (buff.UnitWants(me)) {
                buff.SetUnitWants(me, false);
            } else {
                buff.SetUnitWants(me, true);
            }

            try {
                state.Recalculate(true);
            } catch (Exception ex) {
                Main.Error(ex, "Recalculating spell list?");
            }

        });
        view.targets[i] = portrait;
    }
}
```

Key changes from the original:
- ScrollRect + Viewport + RectMask2D wrapping the HorizontalLayoutGroup
- `childControlWidth = false`, `childForceExpandWidth = false` — portraits use AspectRatioFitter width
- `ContentSizeFitter` on the content so it grows beyond viewport
- Content anchored left (`anchorMin/Max = (0,0)-(0,1)`) so it scrolls right
- `Bubble.Group` → `Bubble.ConfigGroup` for portrait count and indexing
- Separator + dimmed overlay for reserve chars
- Portrait click handler uses `Bubble.ConfigGroup[personIndex]`

- [ ] **Step 2: Add required using directive if missing**

Check if `UnityEngine.UI` is already imported (for `RectMask2D`, `ScrollRect`, `ContentSizeFitter`). These are in the Unity UI namespace which should already be imported since `Image`, `LayoutGroup` etc. are used extensively. Verify at the top of the file.

- [ ] **Step 3: Build and verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: make portrait row scrollable with reserve separator and overlay"
```

---

### Task 6: Update all Bubble.Group UI references to ConfigGroup

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs` (multiple locations)

Several UI methods reference `Bubble.Group` for portrait iteration. These must use `Bubble.ConfigGroup` since the portrait array now maps to ConfigGroup.

- [ ] **Step 1: Update ShowBuffWindow rebuild check (line 1910)**

```csharp
if (WindowCreated && view.targets.Length != Bubble.ConfigGroup.Count) {
```

- [ ] **Step 2: Update totalCasters calculation (lines 537-540)**

```csharp
totalCasters = 0;
for (int i = 0; i < Bubble.ConfigGroup.Count; i++) {
    totalCasters += Bubble.ConfigGroup[i].Spellbooks?.Count() ?? 0;
}
```

- [ ] **Step 3: Update addToAll/removeFromAll handlers (lines 1372-1390)**

```csharp
view.addToAll.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
    var buff = view.Selected;
    if (buff == null) return;

    for (int i = 0; i < Bubble.ConfigGroup.Count && i < view.targets.Length; i++) {
        if (view.targets[i].Button.Interactable && !buff.UnitWants(Bubble.ConfigGroup[i])) {
            buff.SetUnitWants(Bubble.ConfigGroup[i], true);
        }
    }
    state.Recalculate(true);

});
view.removeFromAll.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
    var buff = view.Selected;
    if (buff == null) return;

    for (int i = 0; i < Bubble.ConfigGroup.Count && i < view.targets.Length; i++) {
        if (buff.UnitWants(Bubble.ConfigGroup[i])) {
            buff.SetUnitWants(Bubble.ConfigGroup[i], false);
        }
    }
    state.Recalculate(true);
});
```

- [ ] **Step 4: Update addToAll interactable check (line 3224)**

```csharp
addToAll.GetComponentInChildren<OwlcatButton>().Interactable = buff.Requested != Bubble.ConfigGroup.Count;
```

- [ ] **Step 5: Update PreviewReceivers and UpdateTargetBuffColor (lines 3041, 3061)**

```csharp
public void PreviewReceivers(BubbleBuff buff) {
    if (buff == null && currentSelectedSpell.Value != null)
        buff = Selected;

    for (int p = 0; p < Bubble.ConfigGroup.Count && p < targets.Length; p++)
        UpdateTargetBuffColor(buff, p);
}
```

And in `UpdateTargetBuffColor` (line 3061):

```csharp
var me = Bubble.ConfigGroup[i];
```

- [ ] **Step 6: Update ReorderTargetPortraits (line 2883-2888)**

```csharp
public void ReorderTargetPortraits() {
    var group = Bubble.ConfigGroup;
    for (int i = 0; i < group.Count && i < targets.Length; i++) {
        targets[i].Image.sprite = group[i].Portrait.SmallPortrait;
    }
}
```

- [ ] **Step 7: Build and verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 8: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: update UI portrait references from Group to ConfigGroup"
```

---

### Task 7: Add reserve toggle button

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs` (CreateWindow area + MakeGroupHolder area)

- [ ] **Step 1: Add toggle button in MakeGroupHolder, before the portrait loop**

In the `MakeGroupHolder` method (from Task 5), add the toggle button before the ScrollRect. Place it at the left side of the portrait area. Insert after the anchoring of `scrollRect` and before `scroll.viewport = viewportRect;`:

Actually, the toggle should be placed outside the scroll area. Add it to `content` (the `_targetsSection` parent) before the scroll rect, using anchors to position it at the left edge where the 0-0.25 horizontal space is available (the scroll uses 0.25-1.0):

Add this block at the very beginning of `MakeGroupHolder`, before the ScrollRect creation:

```csharp
// Reserve toggle button
var toggleObj = new GameObject("ReserveToggle", typeof(RectTransform));
var toggleRect = toggleObj.GetComponent<RectTransform>();
toggleRect.SetParent(content, false);
toggleRect.anchorMin = new Vector2(0f, 0f);
toggleRect.anchorMax = new Vector2(0.24f, 0.5f);
toggleRect.offsetMin = new Vector2(4, 4);
toggleRect.offsetMax = new Vector2(-2, -2);

var toggleImage = toggleObj.AddComponent<Image>();
toggleImage.color = Bubble.ShowReserve ? new Color(0.4f, 0.8f, 0.4f, 0.8f) : new Color(0.3f, 0.3f, 0.3f, 0.8f);

var toggleButton = toggleObj.AddComponent<OwlcatButton>();
toggleButton.SetTooltip(
    new TooltipTemplateSimple("reserve.toggle".i8(), "reserve.toggle.tooltip".i8()),
    new TooltipConfig { InfoCallPCMethod = InfoCallPCMethod.None });

var toggleLabel = new GameObject("Label", typeof(RectTransform));
var toggleLabelRect = toggleLabel.GetComponent<RectTransform>();
toggleLabelRect.SetParent(toggleRect, false);
toggleLabelRect.anchorMin = Vector2.zero;
toggleLabelRect.anchorMax = Vector2.one;
toggleLabelRect.offsetMin = Vector2.zero;
toggleLabelRect.offsetMax = Vector2.zero;
var toggleText = toggleLabel.AddComponent<TextMeshProUGUI>();
toggleText.text = "reserve.toggle".i8();
toggleText.fontSize = 14;
toggleText.alignment = TextAlignmentOptions.Center;
toggleText.color = Color.white;

toggleButton.OnLeftClick.AddListener(() => {
    Bubble.ShowReserve = !Bubble.ShowReserve;
    // Force full window rebuild
    foreach (Transform child in Root.transform) {
        GameObject.Destroy(child.gameObject);
    }
    WindowCreated = false;
    ShowBuffWindow();
});
```

Note: `Root` and `WindowCreated` are instance fields on `BubbleBuffSpellbookController`. The `MakeGroupHolder` method is an instance method, so these are accessible.

- [ ] **Step 2: Build and verify**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/
```

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: add reserve toggle button in buff window"
```

---

### Task 8: Deploy and verify on Steam Deck

**Files:**
- Run: `./deploy.sh`

- [ ] **Step 1: Build release**

```bash
~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo
```

- [ ] **Step 2: Deploy**

```bash
./deploy.sh
```

- [ ] **Step 3: Verify DLL timestamp**

```bash
ls -la BuffIt2TheLimit/bin/Debug/BuffIt2TheLimit.dll
ssh deck-direct "ls -la '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/BuffIt2TheLimit/BuffIt2TheLimit.dll'"
```

File size and date must match.

- [ ] **Step 4: Manual testing checklist**

1. Open spellbook, open buff window — portrait row should work as before
2. Click reserve toggle — reserve companions should appear after a separator
3. Reserve portraits should be dimmed (50% opacity)
4. Scrolling works when portraits exceed window width
5. Select a buff — reserve chars show correct can-target colors
6. Assign buff to reserve char — toggle preserved after window close/reopen
7. Toggle off reserve — window rebuilds without reserve portraits
8. Press cast button — only active party members receive buffs
9. Swap a configured reserve char into the party — their buff config should apply on next cast
10. Save/load — configured reserve char buffs persist

- [ ] **Step 5: Commit any fixes from testing**

```bash
git add -A
git commit -m "fix: adjustments from manual testing"
```
