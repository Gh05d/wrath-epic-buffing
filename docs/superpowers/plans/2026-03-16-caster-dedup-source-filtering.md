# Caster Deduplication & Source Filtering — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show one caster portrait per character and filter out self-only source providers (potions, self-only scrolls) from caster display.

**Architecture:** Extend `SelfCastOnly` on `BuffProvider` to account for source type (potions always self-only). Build a portrait→character mapping in `UpdateCasterDetails` to deduplicate portraits. `SelectedCaster` maps to the first CasterQueue entry for the selected character.

**Tech Stack:** C#/.NET 4.8.1, Unity UI

**Spec:** `docs/superpowers/specs/2026-03-16-caster-dedup-source-filtering-design.md`

**Build command:** `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/`

**Commit strategy:** All changes are interdependent. One commit after all tasks complete.

---

## Chunk 1: SelfCastOnly for Potions

### Task 1: Extend SelfCastOnly to account for source type

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuff.cs:514` — change `SelfCastOnly` from computed property to include potion source check

- [ ] **Step 1: Change `SelfCastOnly` property**

Current (line 514):
```csharp
public bool SelfCastOnly => spell.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Owner;
```

Replace with:
```csharp
public bool SelfCastOnly =>
    SourceType == BuffSourceType.Potion ||
    spell.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Owner;
```

This makes all potion providers self-only regardless of the spell's target anchor. Scroll and spellbook providers keep the existing behavior (self-only only if the spell itself is self-target).

---

## Chunk 2: Portrait Deduplication

### Task 2: Add portrait-to-character mapping in BufferView

**Files:**
- Modify: `BuffIt2TheLimit/BubbleBuffer.cs` — `UpdateCasterDetails` method (lines 3019-3057) and portrait click handler (lines 1613-1634)

The current code maps portrait index 1:1 to CasterQueue index. We need an indirection layer: portrait index → first CasterQueue index for that distinct character.

- [ ] **Step 1: Add a mapping field to BufferView**

Near line 2790 (BufferView class fields), add:
```csharp
public int[] casterPortraitMap; // Maps portrait index → CasterQueue index
```

- [ ] **Step 2: Rewrite `UpdateCasterDetails` to deduplicate by character**

Replace the method body (lines 3019-3057). The new logic:
1. Build list of distinct (characterId, firstCasterQueueIndex) pairs from CasterQueue
2. Filter: skip characters whose ALL providers are SelfCastOnly
3. Map portrait index → first CasterQueue index for that character
4. Display portraits using the first (best) provider's data (credits, book name, source overlay)

```csharp
private void UpdateCasterDetails(BubbleBuff buff) {
    // Build distinct caster list: one entry per character, pointing to their first CasterQueue entry
    var seen = new HashSet<string>();
    var distinctCasters = new List<int>(); // CasterQueue indices of first entry per character

    for (int i = 0; i < buff.CasterQueue.Count; i++) {
        var provider = buff.CasterQueue[i];
        if (seen.Add(provider.who.UniqueId)) {
            // Check if this character has at least one non-SelfCastOnly provider
            bool hasNonSelfOnly = false;
            for (int j = i; j < buff.CasterQueue.Count; j++) {
                if (buff.CasterQueue[j].who == provider.who && !buff.CasterQueue[j].SelfCastOnly) {
                    hasNonSelfOnly = true;
                    break;
                }
            }
            if (hasNonSelfOnly)
                distinctCasters.Add(i);
        }
    }

    // Store mapping for click handlers
    casterPortraitMap = distinctCasters.ToArray();

    for (int i = 0; i < casterPortraits.Length; i++) {
        casterPortraits[i].GameObject.SetActive(i < distinctCasters.Count);
        if (i < distinctCasters.Count) {
            var who = buff.CasterQueue[distinctCasters[i]];
            if (who.CharacterIndex < targets.Length)
                casterPortraits[i].Image.sprite = targets[who.CharacterIndex].Image.sprite;
            var bookName = who.book?.Blueprint.Name ?? "";
            if (who.AvailableCredits < 100)
                casterPortraits[i].Text.text = $"{who.spent}+{who.AvailableCredits}\n<i>{bookName}</i>";
            else
                casterPortraits[i].Text.text = $"{"available.atwill".i8()}\n<i>{bookName}</i>";
            // Set source type overlay icon
            if (casterPortraits[i].SourceOverlay != null) {
                if (who.SourceType == BuffSourceType.Spell) {
                    casterPortraits[i].SourceOverlay.gameObject.SetActive(false);
                } else {
                    var overlaySprite = who.SourceType switch {
                        BuffSourceType.Scroll => GlobalBubbleBuffer.scrollOverlayIcon,
                        BuffSourceType.Potion => GlobalBubbleBuffer.potionOverlayIcon,
                        BuffSourceType.Equipment => GlobalBubbleBuffer.equipmentOverlayIcon,
                        _ => null
                    };
                    if (overlaySprite != null) {
                        casterPortraits[i].SourceOverlay.sprite = overlaySprite;
                        casterPortraits[i].SourceOverlay.gameObject.SetActive(true);
                    } else {
                        casterPortraits[i].SourceOverlay.gameObject.SetActive(false);
                    }
                }
            }
            casterPortraits[i].Text.fontSize = 12;
            casterPortraits[i].Text.outlineWidth = 0;
            casterPortraits[i].Image.color = who.Banned ? Color.red : Color.white;
        }
    }
    addToAll.GetComponentInChildren<OwlcatButton>().Interactable = buff.Requested != Bubble.Group.Count;
    removeFromAll.GetComponentInChildren<OwlcatButton>().Interactable = buff.Requested > 0;
}
```

- [ ] **Step 3: Update portrait click handler to use mapping**

In the portrait creation loop (lines 1613-1634), the click handler currently uses `casterIndex` directly:
```csharp
SelectedCaster.Value = casterIndex;
```

Change to use the mapping:
```csharp
portrait.Expand?.OnLeftClick.AddListener(() => {
    if (portrait.State) {
        SelectedCaster.Value = (view.casterPortraitMap != null && casterIndex < view.casterPortraitMap.Length)
            ? view.casterPortraitMap[casterIndex]
            : casterIndex;
        UpdateDetailsView();
    } else {
        SelectedCaster.Value = -1;
    }
});
```

This maps the portrait index through `casterPortraitMap` to get the actual CasterQueue index.

---

## Chunk 3: Build and Verify

### Task 3: Build and commit

- [ ] **Step 1: Build**

Run: `~/.dotnet/dotnet build BuffIt2TheLimit/BuffIt2TheLimit.csproj -p:SolutionDir=$(pwd)/ --nologo`
Expected: 0 errors.

- [ ] **Step 2: Commit**

```bash
git add BuffIt2TheLimit/BubbleBuff.cs BuffIt2TheLimit/BubbleBuffer.cs
git commit -m "fix: deduplicate caster portraits, make potions self-only providers"
```

- [ ] **Step 3: Deploy and test**

Run: `./deploy.sh`

Manual testing:
1. Select Aid → verify each character appears only once as caster, even with merged spellbooks
2. Have a Potion of Mage Armor in inventory → verify potion-only characters do NOT appear as casters
3. Select a self-only buff (e.g. Divine Might) → verify spellbook casters still appear
4. Click a caster portrait → verify caster details (credits, toggles) show correctly
5. Ban a caster → verify ban applies correctly
