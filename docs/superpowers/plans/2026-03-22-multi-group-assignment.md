# Multi-Group Buff Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow buffs to belong to multiple BuffGroups simultaneously via checkboxes instead of exclusive buttons.

**Architecture:** Replace the single `BuffGroup InGroup` field with `HashSet<BuffGroup> InGroups` on both the runtime (`BubbleBuff`) and persistence (`SavedBuffState`) classes. Replace the exclusive `ButtonGroup<BuffGroup>` UI component with 3 independent `ToggleWorkaround` checkboxes. Update all filter sites (`BuffExecutor`, summary labels) to use `.Contains()`.

**Tech Stack:** C#/.NET Framework 4.81, Unity UI, Newtonsoft.Json (game-bundled, old version — has `StringEnumConverter` but no generic `JsonConverter<T>`)

**Spec:** `docs/superpowers/specs/2026-03-22-multi-group-assignment-design.md`

**Build command:** `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`

---

### Task 1: Data Model — SavedBuffState

Add `InGroups` field to the persistence class while keeping backward compatibility.

**Files:**
- Modify: `BuffIt2TheLimit/SaveState.cs:1-102`

- [ ] **Step 1: Add `InGroups` field to `SavedBuffState`**

Add `using Newtonsoft.Json.Converters;` to the top of the file. Then add the new field after the existing `InGroup` field (line 71):

```csharp
[JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
public HashSet<BuffGroup> InGroups;
```

The existing `InGroup` field stays as-is for backward-compatible deserialization of old saves.

- [ ] **Step 2: Build and verify**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add BuffIt2TheLimit/SaveState.cs
git commit -m "feat: add InGroups field to SavedBuffState for multi-group persistence"
```

---

### Task 2: Data Model — BubbleBuff

Replace `InGroup` with `InGroups` on the runtime class and update migration from save.

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:49-50` (field declaration)
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:188-189` (InitialiseFromSave)

- [ ] **Step 1: Replace field declaration**

At line 50, change:

```csharp
// Old
public BuffGroup InGroup = BuffGroup.Long;

// New
public HashSet<BuffGroup> InGroups = new HashSet<BuffGroup> { BuffGroup.Long };
```

- [ ] **Step 2: Update `InitialiseFromSave` migration**

At line 188-189, change:

```csharp
// Old
InGroup = state.InGroup;

// New — migrate from legacy single value if InGroups not present
if (state.InGroups != null) {
    InGroups = new HashSet<BuffGroup>(state.InGroups);
} else {
    InGroups = new HashSet<BuffGroup> { state.InGroup };
}
```

Only trigger migration on `null`, not empty. An empty `HashSet` means the user intentionally paused the buff (no groups).

- [ ] **Step 3: Build and verify**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`
Expected: Build FAILS — there are 4 remaining references to `buff.InGroup` that need updating (Tasks 3-5). This confirms the field rename is working.

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuff.cs
git commit -m "feat: replace InGroup with InGroups HashSet on BubbleBuff"
```

---

### Task 3: Save Write Path & Cleanup Conditions

Update `BufferState.Save()` to write `InGroups` and adjust cleanup/creation conditions.

**Files:**
- Modify: `BuffIt2TheLimit/BufferState.cs:437-490`

- [ ] **Step 1: Add DefaultGroups constant**

Add a static field to `BufferState` class (before the `Save` method, around line 437):

```csharp
private static readonly HashSet<BuffGroup> DefaultGroups = new HashSet<BuffGroup> { BuffGroup.Long };
```

- [ ] **Step 2: Update `updateSavedBuff` in `Save()`**

At line 440, replace:

```csharp
// Old
save.InGroup = buff.InGroup;

// New
save.InGroups = new HashSet<BuffGroup>(buff.InGroups);
save.InGroup = buff.InGroups.Count > 0 ? buff.InGroups.First() : BuffGroup.Long;
```

The `save.InGroup` line provides backward compatibility for mod downgrades.

- [ ] **Step 3: Update cleanup condition**

At line 481, replace:

```csharp
// Old
if (save.Wanted.Empty() && save.IgnoreForOverwriteCheck.Empty() && !buff.HideBecause(HideReason.Blacklisted)) {

// New
if (save.Wanted.Empty() && save.IgnoreForOverwriteCheck.Empty()
    && !buff.HideBecause(HideReason.Blacklisted)
    && buff.InGroups.SetEquals(DefaultGroups)) {
```

This preserves saved entries for buffs with non-default group assignments.

- [ ] **Step 4: Update entry creation condition**

At line 484, replace:

```csharp
// Old
} else if (buff.Requested > 0 || buff.IgnoreForOverwriteCheck.Count > 0 || buff.HideBecause(HideReason.Blacklisted)) {

// New
} else if (buff.Requested > 0 || buff.IgnoreForOverwriteCheck.Count > 0
           || buff.HideBecause(HideReason.Blacklisted)
           || !buff.InGroups.SetEquals(DefaultGroups)) {
```

- [ ] **Step 5: Build and verify**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`
Expected: Build FAILS — 3 remaining references to `buff.InGroup` in BubbleBuffer.cs and BuffExecutor.cs.

- [ ] **Step 6: Commit**

```bash
git add BuffIt2TheLimit/BufferState.cs
git commit -m "feat: update save write path and cleanup for multi-group"
```

---

### Task 4: Execution Filter & Summary Labels

Update the two filter sites that use `InGroup` for group matching.

**Files:**
- Modify: `BuffIt2TheLimit/BuffExecutor.cs:187`
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:2932`

- [ ] **Step 1: Update execution filter**

At `BuffExecutor.cs:187`, change:

```csharp
// Old
foreach (var buff in State.BuffList.Where(b => b.InGroup == buffGroup && b.Fulfilled > 0)) {

// New
foreach (var buff in State.BuffList.Where(b => b.InGroups.Contains(buffGroup) && b.Fulfilled > 0)) {
```

- [ ] **Step 2: Update summary labels**

At `BubbleBuffer.cs:2932`, change:

```csharp
// Old
var list = state.BuffList.Where(b => b.InGroup == group)

// New
var list = state.BuffList.Where(b => b.InGroups.Contains(group))
```

- [ ] **Step 3: Build and verify**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`
Expected: Build FAILS — 2 remaining references in BubbleBuffer.cs (the UI code, lines 1672 and 1701). These are addressed in Task 5.

- [ ] **Step 4: Commit**

```bash
git add BuffIt2TheLimit/BuffExecutor.cs BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: update execution filter and summaries for multi-group"
```

---

### Task 5: UI — Replace ButtonGroup with Checkboxes

Replace the exclusive `ButtonGroup<BuffGroup>` with 3 independent toggle checkboxes.

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1633-1675` (ButtonGroup creation → checkboxes)
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs:1701` (UpdateDetailsView sync)

**Context:** The `MakeSourceToggle()` local function (line 1510-1516) creates toggle objects from `togglePrefab` and parents them to `toggleSideObj`. For the group checkboxes, we need the same pattern but parented to the existing `groupRect` container (the HorizontalLayoutGroup in `actionBarSection`). Create a similar local helper or instantiate directly.

- [ ] **Step 1: Replace ButtonGroup creation with 3 checkboxes**

Replace lines 1633-1675 (from `var groupObj = new GameObject("buff-group"` through the `buffGroup.Selected.Subscribe` handler closing brace). The new code:

```csharp
var groupObj = new GameObject("buff-group", typeof(RectTransform));
var groupRect = groupObj.GetComponent<RectTransform>();
groupRect.SetParent(actionBarSection.transform, false);
var buffGroupHLG = groupObj.AddComponent<HorizontalLayoutGroup>();
buffGroupHLG.childForceExpandWidth = true;
buffGroupHLG.childForceExpandHeight = true;
buffGroupHLG.childControlWidth = true;
buffGroupHLG.childControlHeight = true;
buffGroupHLG.spacing = 8;
buffGroupHLG.padding = new RectOffset(8, 8, 0, 0);
var groupLE = groupObj.AddComponent<LayoutElement>();
groupLE.flexibleWidth = 1;
groupLE.preferredHeight = 38;
groupLE.flexibleHeight = 0;
groupLE.layoutPriority = 3;
groupObj.SetActive(false);

float groupToggleScale = 0.7f;
GameObject MakeGroupToggle(string label) {
    var toggleObj = GameObject.Instantiate(togglePrefab, groupRect);
    toggleObj.SetActive(true);
    toggleObj.transform.localScale = new Vector3(groupToggleScale, groupToggleScale, groupToggleScale);
    toggleObj.GetComponentInChildren<TextMeshProUGUI>().text = label;
    return toggleObj;
}

var groupNormalObj = MakeGroupToggle("group.normal.btn".i8());
var groupImportantObj = MakeGroupToggle("group.important.btn".i8());
var groupQuickObj = MakeGroupToggle("group.short.btn".i8());

var groupNormalToggle = groupNormalObj.GetComponentInChildren<ToggleWorkaround>();
var groupImportantToggle = groupImportantObj.GetComponentInChildren<ToggleWorkaround>();
var groupQuickToggle = groupQuickObj.GetComponentInChildren<ToggleWorkaround>();

groupNormalToggle.onValueChanged.AddListener(val => {
    if (view.Get(out var buff)) {
        if (val) buff.InGroups.Add(BuffGroup.Long);
        else buff.InGroups.Remove(BuffGroup.Long);
        state.Save();
    }
});

groupImportantToggle.onValueChanged.AddListener(val => {
    if (view.Get(out var buff)) {
        if (val) buff.InGroups.Add(BuffGroup.Important);
        else buff.InGroups.Remove(BuffGroup.Important);
        state.Save();
    }
});

groupQuickToggle.onValueChanged.AddListener(val => {
    if (view.Get(out var buff)) {
        if (val) buff.InGroups.Add(BuffGroup.Quick);
        else buff.InGroups.Remove(BuffGroup.Quick);
        state.Save();
    }
});
```

This keeps the same `groupObj`/`groupRect`/HLG container setup but replaces the `ButtonGroup` + anchor-fix loop + subscribe handler with toggle instantiation and individual listeners.

- [ ] **Step 2: Update `UpdateDetailsView` sync**

At line 1701, replace:

```csharp
// Old
buffGroup.Selected.Value = buff.InGroup;

// New
groupNormalToggle.isOn = buff.InGroups.Contains(BuffGroup.Long);
groupImportantToggle.isOn = buff.InGroups.Contains(BuffGroup.Important);
groupQuickToggle.isOn = buff.InGroups.Contains(BuffGroup.Quick);
```

Note: The toggle variables (`groupNormalToggle`, etc.) are captured in the `UpdateDetailsView` closure just like the old `buffGroup` variable was. Setting `isOn` fires `onValueChanged`, but the Add/Remove on HashSet is idempotent — same pattern as existing source toggles.

- [ ] **Step 3: Build and verify**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeds with no errors. All `InGroup` references are now replaced.

- [ ] **Step 4: Verify no remaining `InGroup` references on `BubbleBuff`**

Run: `grep -n '\.InGroup\b' BuffIt2TheLimit/*.cs`
Expected: Only `SaveState.cs` (legacy field) and `BufferState.cs` (backward-compat write) should reference `InGroup`. No references to `buff.InGroup` or `b.InGroup` should remain.

- [ ] **Step 5: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "feat: replace BuffGroup buttons with multi-select checkboxes"
```

---

### Task 6: Build, Deploy & Manual Test

**Files:** None (testing only)

- [ ] **Step 1: Clean build**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`
Expected: Build succeeds.

- [ ] **Step 2: Deploy to Steam Deck**

Run: `./deploy.sh`
Expected: Build + SCP succeeds.

- [ ] **Step 3: Manual test checklist**

Test in-game on Steam Deck:
1. Open buff menu — group checkboxes appear where buttons used to be
2. Select a buff — checkboxes reflect its current group (Normal by default)
3. Check Quick additionally — buff is now in Normal + Quick
4. Uncheck all — buff is in no group (paused)
5. Click Normal HUD button — only Normal-group buffs cast
6. Click Quick HUD button — only Quick-group buffs cast (including shared ones)
7. Buff in both groups: cast via Quick, then cast via Normal — already-active buff is skipped
8. Save/load game — group assignments persist
9. Summary labels show correct counts (buff in 2 groups counted in both)

- [ ] **Step 4: Test backward compatibility**

Load an existing save file from before this change. Buffs should retain their original single-group assignment (migrated to `InGroups` set with one element).
