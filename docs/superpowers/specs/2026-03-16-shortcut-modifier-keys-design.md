# Shortcut Modifier Keys & Open Buff Menu Shortcut

## Summary

Extend the keyboard shortcut system to support modifier key combinations (Ctrl, Shift, Alt) for all shortcuts, and add a new configurable shortcut to open the buff menu directly.

## Current State

- `SavedBufferState.ShortcutKeys` stores `Dictionary<BuffGroup, KeyCode>` — single keys only
- `BufferState.GetShortcut(BuffGroup)` / `SetShortcut(BuffGroup, KeyCode)` manage persistence
- `BubbleBuffGlobalController` (in `BuffExecutor.cs`) captures shortcuts via `CapturingFor` (`BuffGroup?`) and `OnShortcutCaptured` (`Action<BuffGroup, KeyCode>`)
- `KeyboardKeys` array includes modifier keys (LeftShift=304..RightAlt=309 are all < Mouse0=323) — pressing Shift alone could capture
- `MakeKeybindRow(Transform, string, BuffGroup)` in `BubbleBuffer.cs` creates UI rows tied to `BuffGroup`
- No keyboard shortcut exists for opening the buff menu — only a HUD button

## Design

### 1. ShortcutBinding Struct

New file: `BuffIt2TheLimit/ShortcutBinding.cs`. Use `readonly struct` for value-type correctness.

```csharp
[JsonConverter(typeof(ShortcutBindingConverter))]
public readonly struct ShortcutBinding {
    public readonly KeyCode Key;
    public readonly bool Ctrl;
    public readonly bool Shift;
    public readonly bool Alt;

    public ShortcutBinding(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false) {
        Key = key; Ctrl = ctrl; Shift = shift; Alt = alt;
    }

    public bool IsNone => Key == KeyCode.None;

    public bool IsPressed() {
        if (Key == KeyCode.None) return false;
        if (!Input.GetKeyDown(Key)) return false;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        return Ctrl == ctrl && Shift == shift && Alt == alt;
    }

    public static ShortcutBinding None => new(KeyCode.None);

    /// <summary>
    /// Build a ShortcutBinding from a detected keypress, snapshotting current modifier state.
    /// Called from the capture loop after Input.GetKeyDown(key) fires.
    /// </summary>
    public static ShortcutBinding Capture(KeyCode key) {
        return new ShortcutBinding(
            key,
            ctrl: Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
            shift: Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            alt: Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
        );
    }

    /// <summary>
    /// Display string like "Ctrl+Shift+B" or "F5". Modifier names stay English
    /// (gaming UI convention per CLAUDE.md).
    /// </summary>
    public string ToDisplayString() {
        if (Key == KeyCode.None) return "shortcut.none".i8();
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
```

### 2. JSON Backward Compatibility

Custom `ShortcutBindingConverter : JsonConverter<ShortcutBinding>` in the same file. Handles dictionary VALUE deserialization:

- **Old format** (string token): `"F5"` → `new ShortcutBinding(KeyCode.F5)` — no modifiers
- **New format** (object token): `{"Key":"F5","Ctrl":true,"Shift":false,"Alt":false}` → full struct

Write always uses the new object format. The `[JsonConverter]` attribute on the struct ensures Newtonsoft uses it automatically when deserializing both the `Dictionary<BuffGroup, ShortcutBinding>` values and the standalone `OpenBuffMenuKey` field.

### 3. SavedBufferState Changes

```csharp
// Changed type:
public Dictionary<BuffGroup, ShortcutBinding> ShortcutKeys = new();
// New field:
public ShortcutBinding OpenBuffMenuKey;
```

### 4. BufferState Changes

Existing methods change signature from `KeyCode` to `ShortcutBinding`:
- `GetShortcut(BuffGroup)` → returns `ShortcutBinding`
- `SetShortcut(BuffGroup, ShortcutBinding)`

New methods:
- `GetOpenBuffMenuShortcut()` → returns `ShortcutBinding`
- `SetOpenBuffMenuShortcut(ShortcutBinding)`

### 5. Capture Logic (BubbleBuffGlobalController in BuffExecutor.cs)

**Simplify capture state:** Replace `CapturingFor` (`BuffGroup?`) with a `bool CapturingActive`. The callback `OnShortcutCaptured` changes to `Action<ShortcutBinding>` — each button's click handler sets its own closure that knows what to do with the captured binding. No need for a tag/identifier since the closure captures the context.

**Filter modifier keys from KeyboardKeys:** Add `.Where()` to exclude the 6 modifier keys (`LeftShift`, `RightShift`, `LeftControl`, `RightControl`, `LeftAlt`, `RightAlt`) plus Command/Apple keys (310–313) from the `KeyboardKeys` array. This prevents capturing a modifier press alone.

**Capture flow:**
```
foreach (KeyCode kc in KeyboardKeys) {
    if (Input.GetKeyDown(kc)) {
        var binding = (kc == KeyCode.Escape)
            ? ShortcutBinding.None    // Escape ALWAYS clears, regardless of modifiers
            : ShortcutBinding.Capture(kc);
        OnShortcutCaptured?.Invoke(binding);
        CapturingActive = false;
        OnShortcutCaptured = null;
        break;
    }
}
```

**Execution loop:** Replace manual `GetShortcut` + `GetKeyDown` calls with `binding.IsPressed()` for each BuffGroup. The open-buff-menu shortcut check is **separate and outside** the `if (state != null)` block — guarded only by `GlobalBubbleBuffer.Instance != null` — because the menu shortcut must work even when the spellbook has never been opened.

### 6. Open Buff Menu Execution

When `OpenBuffMenuKey.IsPressed()` fires in the update loop:
- Guard: `GlobalBubbleBuffer.Instance != null` (not dependent on `state`)
- If already in buff mode → do nothing (open only, no toggle)
- Otherwise → same logic as HUD "Open Buffs" button (BubbleBuffer.cs lines 2420–2450): check spellbook visibility, either toggle directly or open spellbook with `PendingOpenBuffMode`

Extract the open-buff-menu logic from the HUD button lambda into a named method on `GlobalBubbleBuffer` (e.g. `OpenBuffMenu()`) to avoid duplication between the HUD button and the shortcut.

### 7. UI (Settings Panel)

`MakeKeybindRow` signature changes from `(Transform, string, BuffGroup)` to:
```csharp
(Transform parent, string labelText, Func<ShortcutBinding> getter, Action<ShortcutBinding> setter)
```

Each call site passes its own get/set lambdas. Existing BuffGroup calls become:
```csharp
MakeKeybindRow(panel.transform, key.i8(),
    () => state.GetShortcut(groupCopy),
    binding => state.SetShortcut(groupCopy, binding));
```

New open-buff-menu call:
```csharp
MakeKeybindRow(panel.transform, "shortcut.openbuffmenu".i8(),
    () => state.GetOpenBuffMenuShortcut(),
    binding => state.SetOpenBuffMenuShortcut(binding));
```

Button text uses `binding.ToDisplayString()` instead of raw `KeyCode.ToString()`.

### 8. Locale Keys

New key in all locale files:
```json
"shortcut.openbuffmenu": "Open buff menu shortcut:"
```

Update `shortcut.press` to indicate combos are accepted:
- `en_GB`: `"(press shortcut)"` (was `"(press any key)"`)
- `de_DE`: `"(Shortcut drücken)"` (was `"(Taste drücken)"`)
- Other locales: update accordingly

### 9. Edge Cases

- **Escape always clears:** Pressing Escape during capture always sets `ShortcutBinding.None`, regardless of held modifiers. Ctrl+Escape does NOT become a valid binding.
- **Duplicate bindings:** No collision detection. If a user binds the same combo to two actions, both fire. This matches the existing behavior (single keys could already collide). Collision detection is out of scope for this change.
- **Modifier display names stay English:** "Ctrl", "Shift", "Alt" are not localized — standard gaming UI convention.

## Files Changed

| File | Class | Change |
|---|---|---|
| `ShortcutBinding.cs` | `ShortcutBinding`, `ShortcutBindingConverter` | New — readonly struct, JsonConverter |
| `SaveState.cs` | `SavedBufferState` | `ShortcutKeys` type → `ShortcutBinding`, new `OpenBuffMenuKey` field |
| `BufferState.cs` | `BufferState` | Method signatures → `ShortcutBinding`, new open-menu get/set |
| `BuffExecutor.cs` | `BubbleBuffGlobalController` | `CapturingFor` → `bool CapturingActive`, modifier key filter, capture builds `ShortcutBinding`, execution uses `IsPressed()`, open-menu check outside state guard |
| `BubbleBuffer.cs` | `BubbleBuffSpellbookController`, `GlobalBubbleBuffer` | `MakeKeybindRow` generalized, new keybind row, extract `OpenBuffMenu()` method |
| `Config/en_GB.json` | — | New `shortcut.openbuffmenu`, update `shortcut.press` |
| `Config/de_DE.json` | — | New `shortcut.openbuffmenu`, update `shortcut.press` |
| `Config/fr_FR.json` | — | New `shortcut.openbuffmenu` |
| `Config/ru_RU.json` | — | New `shortcut.openbuffmenu` |
| `Config/zh_CN.json` | — | New `shortcut.openbuffmenu` |
