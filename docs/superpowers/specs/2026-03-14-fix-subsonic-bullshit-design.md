# Fix Subsonic Bullshit — Design Spec

## Problem

Carnivorous Crystals in Pathfinder: Wrath of the Righteous have a broken DC calculation for their **Subsonic Hum** ability. The tooltip shows DC 22 (correct per Tabletop PF1e), but the actual saving throw DC is massively inflated:

| Difficulty | Tooltip DC | Actual DC |
|---|---|---|
| Core (RtwP) | 22 | ~32 |
| Core (Turn-Based) | 22 | ~38-40 |
| Unfair | 22 | ~52 |

The Tabletop-correct DC is **22** (10 + 7 HD + 5 Con mod) for regular Carnivorous Crystals, **24** for mythic variants.

The root cause is unknown — suspected double-application of difficulty stat modifiers, inflated creature stats, or a TB-specific code path that adds extra modifiers.

### References

- [Steam: DC guessing game](https://steamcommunity.com/app/1184370/discussions/0/4932019356822263381/) — DC 32 RtwP, DC 40 TB confirmed
- [Steam: Immunity not working](https://steamcommunity.com/app/1184370/discussions/0/5086242673972209412/) — Per-round re-saving, DC 38-40 on Core
- Owlcat Patch 1.0.5g claimed to fix the issue but reports persist post-patch

### Non-Goals

- Fixing the Vescavor Swarm "Gibber" ability (same bug pattern, but out of scope)
- Fixing the 24h immunity mechanic (works correctly for the user)
- Adding UI, settings, or configuration

## Solution

Standalone UMM mod called **Fix Subsonic Bullshit**. Two-phase approach:

1. **Diagnose** (DEBUG build): Harmony patch on the DC calculation pipeline to log all components contributing to the Subsonic Hum DC
2. **Fix** (based on diagnosis): Correct the root cause — whether it's inflated creature stats, double-applied modifiers, or a TB-specific calculation bug

## Architecture

### Project Structure

```
~/Code/fix-subsonic-bullshit/
├── FixSubsonicBullshit/
│   ├── FixSubsonicBullshit.csproj   # .NET 4.8.1, same ref structure as BI2TL
│   ├── Info.json                     # UMM manifest
│   └── Main.cs                       # Entry point + Harmony patches
├── GamePath.props                    # WrathInstallDir → GameInstall/
├── GameInstall/                      # Symlink to game managed DLLs
├── deploy.sh                         # Build + SCP to Steam Deck
└── .gitignore
```

Minimal mod — no UI, no localization, no asset bundles, no save state.

### Mod Entry Point

```csharp
static class Main {
    static Harmony harmony;
    static UnityModManager.ModEntry.ModLogger logger;

    static bool Load(UnityModManager.ModEntry modEntry) {
        logger = modEntry.Logger;
        harmony = new Harmony(modEntry.Info.Id);
        harmony.PatchAll();
        return true;
    }

    static bool OnUnload(UnityModManager.ModEntry modEntry) {
        harmony.UnpatchAll();
        return true;
    }

    public static void Log(string msg) => logger.Log(msg);

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Verbose(string msg) => logger.Log(msg);
}
```

### Phase 1: Diagnostic Patch

Harmony Postfix on `RuleSavingThrow` and/or `RuleCalculateAbilityParams` to intercept Subsonic Hum DC calculations:

```csharp
[HarmonyPatch]
static class DiagnosticPatch {
    // Patch target TBD based on decompiled game code
    // Log: base DC, stat modifiers, difficulty bonuses, final DC
    // Filter: only log when ability is Subsonic Hum (match by BlueprintAbility GUID)
}
```

Key classes to investigate:
- `RuleSavingThrow` — saving throw resolution, contains final DC
- `RuleCalculateAbilityParams` — ability parameter calculation including DC
- `ContextRankConfig` — DC formula in blueprint components
- `BlueprintAbility` — Subsonic Hum blueprint (GUID to be determined)
- `BlueprintUnit` — Carnivorous Crystal stat block
- `AreaEffectEntityData` — aura tick logic (may explain RtwP vs TB difference)

### Phase 2: Fix Implementation

Based on diagnostic output, one of:

**A. Blueprint patch (if creature stats are inflated):**
Modify the Carnivorous Crystal `BlueprintUnit` at mod load to correct Constitution or HD values.

**B. DC override (if modifier stacking bug):**
Harmony Prefix on the DC calculation to remove double-applied modifiers for Subsonic Hum.

**C. TB aura fix (if TB-specific code path):**
Patch the turn-based aura tick logic to use the same DC calculation as RtwP.

The specific fix will be determined after Phase 1 diagnosis.

### Build System

csproj mirrors Buff It 2 The Limit:
- Target: `net481`
- `BepInEx.AssemblyPublicizer.MSBuild` for private field access
- Game DLLs via `$(WrathInstallDir)/Wrath_Data/Managed/`
- Publicized DLLs: `Assembly-CSharp.dll`, `Owlcat*.dll`, `UnityModManager.dll`
- `-p:SolutionDir=$(pwd)/` required on Linux

### Deploy

```bash
# deploy.sh
~/.dotnet/dotnet build FixSubsonicBullshit/FixSubsonicBullshit.csproj -p:SolutionDir="$(pwd)/"
scp FixSubsonicBullshit/bin/Debug/FixSubsonicBullshit.dll \
  deck-direct:"/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/FixSubsonicBullshit/"
```

### UMM Manifest (Info.json)

```json
{
  "Id": "FixSubsonicBullshit",
  "DisplayName": "Fix Subsonic Bullshit",
  "Author": "Gh05d",
  "Version": "0.1.0",
  "ManagerVersion": "0.23.0",
  "GameVersion": "1.4.0",
  "EntryMethod": "FixSubsonicBullshit.Main.Load",
  "AssemblyName": "FixSubsonicBullshit.dll",
  "Requirements": [],
  "HomePage": ""
}
```

## Testing Plan

1. Build DEBUG config, deploy to Steam Deck
2. Load save near Carnivorous Crystals (Mutasafen's Lab, Desolate Hovel, or "Where Your Soul Calls" quest area)
3. Engage crystals, check UMM log for DC component breakdown
4. Analyze log output to identify root cause
5. Implement fix, rebuild, redeploy
6. Verify: actual DC matches tooltip DC (or Tabletop-correct value)
7. Verify: no side effects on other crystal abilities (petrification touch, etc.)
8. Test in both RtwP and Turn-Based mode
9. Test on multiple difficulty levels

## Dependencies

- Unity Mod Manager (0.23.0+)
- 0Harmony.dll (bundled with UMM)
- BepInEx.AssemblyPublicizer.MSBuild (0.4.2)
- Game managed DLLs via GameInstall symlink
- No mod dependencies
