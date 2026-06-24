# Bindable Mouse Buttons for Buff Shortcuts

**Date:** 2026-06-24
**Status:** Approved

## Goal

Let players assign the extra mouse buttons (thumb buttons "Mouse3"/"Mouse4" and any
additional side buttons "Mouse5"/"Mouse6") to the Normal / Quick / Important buff
shortcuts and the Open-Buff-Menu shortcut — not just keyboard keys. Requested as a
quality-of-life addition for mice with side buttons.

## Scope

- **In scope:** `KeyCode.Mouse3`–`KeyCode.Mouse6` become assignable via the existing
  rebind UI, alongside keyboard keys.
- **Out of scope:** Left/right/middle click (`Mouse0`/`Mouse1`/`Mouse2`) — see Decisions.
  Joystick/controller buttons. New display formatting. New localization strings.

## Background — current behavior

Shortcut binding is already a clean, `KeyCode`-based system:

- `ShortcutBinding` (struct) holds a `KeyCode` + Ctrl/Shift/Alt flags.
- `ShortcutBinding.IsPressed()` → `Input.GetKeyDown(Key)` with an exact modifier match.
- `ShortcutBindingConverter` round-trips the binding to/from JSON by the `KeyCode` name.
- `ShortcutBinding.ToDisplayString()` → `Key.ToString()` for the rebind UI label.
- Capture flow (`BuffExecutor.cs`): when a rebind row is armed, `Update()` scans a
  precomputed `KeyboardKeys` array and captures the first `Input.GetKeyDown(kc)`.

Unity's `KeyCode` enum **already includes** mouse buttons (`Mouse0`=323 … `Mouse6`=329,
then `JoystickButton0`=330+), and `Input.GetKeyDown` already fires for them. So the
execution path, save/load, and display are all already mouse-capable.

**The only blocker** is the capture scan, which is built with
`.TakeWhile(kc => kc < KeyCode.Mouse0)` — deliberately stopping before every mouse
button, so a mouse button can never be *assigned*.

## Change

Single edit in `BuffIt2TheLimit/BuffExecutor.cs` — broaden the capture array (and rename
it to reflect that it is no longer keyboard-only):

```csharp
// before
private static readonly KeyCode[] KeyboardKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
    .TakeWhile(kc => kc < KeyCode.Mouse0)
    .Where(kc => !ModifierKeys.Contains(kc))
    .ToArray();

// after — keyboard keys PLUS Mouse3..Mouse6 (thumb + side buttons)
private static readonly KeyCode[] BindableKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
    .Where(kc => kc < KeyCode.Mouse0 || (kc >= KeyCode.Mouse3 && kc <= KeyCode.Mouse6))
    .Where(kc => !ModifierKeys.Contains(kc))
    .ToArray();
```

The `foreach (KeyCode kc in KeyboardKeys)` loop in `Update()` is updated to reference
`BindableKeys`. `TakeWhile` becomes `Where` because the array no longer needs the values
to be contiguous from the start: the filter keeps all keyboard keys (`< 323`) plus the
`326–329` mouse range, and excludes `Mouse0`/`Mouse1`/`Mouse2` and every joystick button
(`≥ 330`).

No other files change.

## Decisions

- **Allowed buttons = Mouse3–Mouse6.** Covers the requested thumb buttons plus extra
  side buttons on gaming mice. Middle-click (`Mouse2`) is excluded because it is the
  game's default camera-rotate.
- **LMB/RMB (`Mouse0`/`Mouse1`) stay excluded — and not just for safety.** The rebind row
  is armed by a left-click; if `Mouse0` were in the scan it would self-capture on the same
  frame. RMB is reserved for game controls.
- **No friendlier display name.** `Key.ToString()` renders `"Mouse3"`/`"Mouse4"`, which
  matches the user's own naming and stays consistent with how keyboard keys display. Avoids
  touching all five locale files for zero functional gain.
- **Modifier combos come for free.** `ShortcutBinding.Capture(kc)` already records held
  Ctrl/Shift/Alt, so e.g. `Ctrl+Mouse4` works with no extra code.

## Verification

No unit-test harness exists (Unity mod; verification is a manual smoke test per the
release process). Smoke test on the Steam Deck:

1. Build + `./deploy.sh` to the deck.
2. Open the buff menu → rebind a group's shortcut → press a thumb button.
   - Expect: capture closes and the row label reads `"Mouse4"` (or whichever).
3. In normal gameplay, press that thumb button.
   - Expect: the corresponding buff routine fires.
4. Confirm the binding survives a save/reload (JSON round-trip).

**Residual risk to confirm in step 3:** that the game's input layer lets Unity's legacy
`Input.GetKeyDown` observe the thumb buttons. Legacy `Input` reads hardware state and
keyboard binds already prove the path, so this is expected to work — but it is the one
thing only an on-device test can confirm.

## Release

Standard patch release once smoke-tested: bump version in the three files
(`BuffIt2TheLimit.csproj`, `Info.json`, `Repository.json`), then `/release patch` →
GitHub release → automated Nexus upload. Release notes in English.
