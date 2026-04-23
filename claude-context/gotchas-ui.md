# Gotchas — UI (Unity / TextMeshPro / layout)

Load when: editing `BubbleBuffer.cs` UI code, `UIHelpers.cs`, or adding/modifying any Unity layout.

## Bullet Gotchas

- **`sourceControlObj` visibility**: Controlled by `sourceCount > 1 || hasSpellProviders` in `UpdateDetailsView`. Hidden entirely for songs (sourceCount=0, no spell providers) and equipment-only buffs. Any UI control that must be visible for ALL buff types (e.g., combat start checkbox) must be parented outside `sourceControlObj` — use `spellInfoSection` with `ignoreLayout = true` instead.
- **`source-controls-section` needs minHeight ≥ 110**: The section hosts two vertical stacks (`prioSide`: prioLabel + extendRod + combatStart + roundLimit; `toggleSide`: 4 source toggles). With minHeight=30 the containers were only ~56 high, and togglePrefab's fixed sizeDelta pushed the bottom children (Use Equipment, Combat Start) outside the parent bounds — clicks silently failed. Keep `sourceControlsSection` at 110+ so all children fit vertically.
- **`TextMeshProUGUI` competes with `LayoutElement` at default priority**: TMP implements ILayoutElement (priority 1) and can make a LayoutElement's `preferredHeight` be ignored, collapsing the text to 0 height. For text with a fixed target height (e.g. the priority label), set `LayoutElement.layoutPriority = 2` AND `minHeight` to force the LayoutElement to win. Also disable `enableWordWrapping` when the text should stay on one line.
- **Caster portrait index ≠ CasterQueue index**: `BufferView.casterPortraitMap` maps portrait indices to CasterQueue indices after deduplication. Always use the map when translating portrait clicks to CasterQueue entries.
- **Debug Unity UI positions with `GetWorldCorners()`**: When UI elements appear mispositioned, add `Main.Log` calls with `RectTransform.GetWorldCorners()` to compare actual pixel positions at runtime. Anchor math from code is error-prone — verify empirically via Player.log. For "why is this child outside its parent" bugs, walk the parent chain with `transform.parent` and log each level's worldW/worldH + anchorMin/Max/sizeDelta — reveals which ancestor clamps the size.
- **Don't destroy UI elements during their click callbacks**: `GameObject.Destroy` (deferred) and `DestroyImmediate` both corrupt Unity's EventSystem mid-click. Let `ShowBuffWindow` handle rebuilds via its size-mismatch detection instead.
- **`OwlcatButton` via `AddComponent` doesn't render**: Needs internal layer structure. Use `MakeButton()` with `buttonPrefab` (static field set in `CreateWindow`) for game-styled buttons.
- **ScrollRect needs raycast target for wheel events**: Add transparent `Image` with `raycastTarget = true` to the Viewport. Without it, only drag-scroll works.
- **`GlobalBubbleBuffer.Buttons` references go stale after save/load**: The `Buttons` list contains OwlcatButton references that become destroyed Unity objects when the UI is reinstalled. Always null-guard individual buttons in `ForEach` lambdas. In EventBus handlers, separate UI operations from game logic in distinct try-catch blocks so a stale button doesn't block other functionality.

## Unity UI Layout Patterns


- **`MakeButton()` breaks layout groups**: Sets point-anchors `(0.5, 0.5)` → zero size in HLG/VLG. Always reset anchors to stretch `(0,0)→(1,1)` after calling `MakeButton()`.
- **`childControlHeight=true` + `childForceExpandHeight=false`**: Correct combo for LayoutElement-driven sizing. `childControl=false` ignores LayoutElement entirely (children collapse to RectTransform default = 0). `childForceExpand=true` stretches beyond preferred size.
- **`buttonPrefab` is designed for full-width text buttons**: Has internal Image/decoration layers that look broken at small sizes (<60px). Don't use as icon buttons.
- **`buttonPrefab` minimum height ~38px**: Below this threshold, internal decoration layers become invisible/transparent. Always set `preferredHeight >= 38` on rows containing buttonPrefab instances.
- **`layoutPriority` on LayoutElement**: Higher priority means the parent LayoutGroup uses THIS element's preferred/flexible values instead of calculating from children. Use `layoutPriority = 3` on row LayoutElements to override buttonPrefab's internal preferred sizes.
- **Anchor-based children inside VLG sections**: VLG with `childControlWidth/Height = true` controls the section's RectTransform. Anchor-based grandchildren position relative to the section rect — this works correctly. But inner LayoutGroups can fight with anchors; prefer one approach per container.
- **`UIHelpers.Create()` / `AddTo()`**: Uses `SetParent(parent)` without `false` — can cause positioning bugs. Use `SetParent(parent, false)` when positioning matters.

