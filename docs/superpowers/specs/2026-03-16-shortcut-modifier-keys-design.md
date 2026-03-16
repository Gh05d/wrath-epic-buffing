# Shortcut Modifier Keys & Open Buff Menu Shortcut

## Summary

Extend the keyboard shortcut system to support modifier key combinations (Ctrl, Shift, Alt) for all shortcuts, and add a new configurable shortcut to open the buff menu directly.

## Current State

- `SavedBufferState.ShortcutKeys` stores `Dictionary<BuffGroup, KeyCode>` — single keys only
- `BufferState.GetShortcut(BuffGroup)` / `SetShortcut(BuffGroup, KeyCode)` manage persistence
- `BubbleBuffGlobalController` captures shortcuts via `CapturingFor` (`BuffGroup?`) and `OnShortcutCaptured` (`Action<BuffGroup, KeyCode>`)
- `MakeKeybindRow(Transform, string, BuffGroup)` creates UI rows tied to `BuffGroup`
- No keyboard shortcut exists for opening the buff menu — only a HUD button

## Design

### 1. ShortcutBinding Struct

New file: `BuffIt2TheLimit/ShortcutBinding.cs`

```csharp
public struct ShortcutBinding {
    public KeyCode Key;
    public bool Ctrl;
    public bool Shift;
    public bool Alt;

    public bool IsNone => Key == KeyCode.None;

    public bool IsPressed() {
        if (Key == KeyCode.None) return false;
        if (!Input.GetKeyDown(Key)) return false;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        return Ctrl == ctrl && Shift == shift && Alt == alt;
    }

    public static ShortcutBinding None => new() { Key = KeyCode.None };

    public static ShortcutBinding Capture() {
        // Called when a key is detected during capture mode
        // Reads current modifier state
    }

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

Custom `JsonConverter<ShortcutBinding>` that reads both formats:
- **Old format** (string): `"F5"` → `ShortcutBinding { Key = F5, Ctrl/Shift/Alt = false }`
- **New format** (object): `{"Key":"F5","Ctrl":true,"Shift":false,"Alt":false}` → full struct

Write always uses the new object format.

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

### 5. Capture Logic (BubbleBuffGlobalController)

Generalize capture from `BuffGroup?` to `string?`:
- `CapturingFor` → `string? CapturingTag` (values: `"long"`, `"quick"`, `"important"`, `"openbuffmenu"`)
- `OnShortcutCaptured` → `Action<string, KeyCode>` stays as `Action<string, ShortcutBinding>`

On capture: when `Input.GetKeyDown(kc)` fires, also read modifier state to build a full `ShortcutBinding`. Modifier keys alone (LeftControl, LeftShift, LeftAlt, and Right variants) are excluded from the `KeyboardKeys` array so pressing Ctrl alone doesn't capture.

Execution loop checks all bindings via `binding.IsPressed()`.

### 6. Open Buff Menu Execution

When `OpenBuffMenuKey.IsPressed()` fires in the update loop:
- If already in buff mode → do nothing (open only, no toggle)
- Otherwise → run the same logic as the HUD "Open Buffs" button (lines 2420–2450 in BubbleBuffer.cs): check spellbook visibility, either toggle directly or open spellbook with `PendingOpenBuffMode`

### 7. UI (Settings Panel)

`MakeKeybindRow` signature changes from `(Transform, string, BuffGroup)` to `(Transform, string, string tag, Func<ShortcutBinding> getter, Action<ShortcutBinding> setter)`.

Existing calls updated to pass lambdas for BuffGroup get/set. New call added for "openbuffmenu" after the group shortcuts.

### 8. Locale Keys

New key in all locale files:
```json
"shortcut.openbuffmenu": "Open buff menu shortcut:"
```

## Files Changed

| File | Change |
|---|---|
| `ShortcutBinding.cs` | New — struct + JsonConverter |
| `SaveState.cs` | `ShortcutKeys` type change, new `OpenBuffMenuKey` field |
| `BufferState.cs` | Method signatures → `ShortcutBinding`, new open-menu get/set |
| `BuffExecutor.cs` | `CapturingFor` → `string?`, capture reads modifiers, execution uses `IsPressed()` |
| `BubbleBuffer.cs` | `MakeKeybindRow` generalized, new keybind row, open-menu execution logic extracted |
| `Config/en_GB.json` | New `shortcut.openbuffmenu` key |
| `Config/de_DE.json` | New `shortcut.openbuffmenu` key |
| `Config/fr_FR.json` | New `shortcut.openbuffmenu` key |
| `Config/ru_RU.json` | New `shortcut.openbuffmenu` key |
| `Config/zh_CN.json` | New `shortcut.openbuffmenu` key |
