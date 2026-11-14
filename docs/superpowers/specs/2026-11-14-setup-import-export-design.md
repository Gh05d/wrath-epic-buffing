# Setup Import / Export Design

## Intent

Players want to carry their buff setup to a new playthrough and share it with other players. Today the setup lives in `UserSettings/bi2tl-<GameId>.json`, one file per playthrough, and cannot move: every character reference in it is a `UnitEntityData.UniqueId`, a random GUID per unit instance. Seelah has a different `UniqueId` in every playthrough.

Export writes the setup to a file with portable character references. Import replaces the current setup with such a file and maps the references back onto the units of the current playthrough.

## Goals

- Export the whole setup (per-buff config and global settings) to a file.
- Import a file into any playthrough, own or someone else's, replacing the current setup.
- Characters that join later get their imported setup when they appear, once.

## Non-goals

- A mapping dialog for characters that don't match (for example "assign merc Bob's setup to Alice"). Unmatched references just wait.
- Merging an import into an existing setup.
- Hand-editing support or a human-friendly file schema. The file is the save format with different IDs.

## Anti-goals

- **An imported setup must never overwrite later changes.** A reference is applied once, when its character is first matched. After that only the user's own edits count. Re-applying periodically (for example on every party refresh) would be a failure even if everything else works. (User requirement.)
- Import must not lose the previous setup without a way back. Import writes a backup first.

## Approaches considered

1. **Tokens inside the existing format (chosen).** The export file holds a `SavedBufferState`, the same class as the normal save, with every `UniqueId` replaced by a portable token. Import resolves the tokens it can match and leaves the rest in place. Later party refreshes resolve those. Pro: no second schema; fields added to the save later are exported automatically; "applied once" falls out naturally, because a resolved token no longer exists. Con: unresolved tokens sit in the live save data. That is harmless because they never equal a real `UniqueId`, but the code that touches IDs has to keep them.
2. **Separate export schema** with one section per character. Rejected: every save field needs translation code in both directions, and new fields would be forgotten in the export.

## Character tokens

Every token starts with `@`. A `UniqueId` is a GUID string and never does.

| Token | Unit | Matched by |
|---|---|---|
| `@main` | `Game.Instance.Player.MainCharacter` | identity |
| `@merc:<CharacterName>` | `unit.IsCustomCompanion()` (mercenaries, which share `BlueprintRoot.Instance.CustomCompanion`, IL-verified `UnitHelper.IsCustomCompanion`) | display name |
| `@pet:<blueprint guid>\|<owner token>` | units with `UnitPartPet` | pet blueprint + owner token |
| `@comp:<blueprint guid>` | every other party unit (story companions) | blueprint GUID |

Check order: main, then pet, then merc, then companion. The main character and mercenaries may share blueprints, so `@main` and `@merc` must be decided before falling back to the blueprint.

- Use `Descriptor.OriginalBlueprint`, not `Blueprint`. Polymorph can change the current blueprint, and the engine's own `IsCustomCompanion(UnitDescriptor)` uses `OriginalBlueprint`.
- Builder: verify in IL how a pet reaches its master (`UnitPartPet` owner field). `RefreshGroup` only walks master → pets.

### Where tokens appear (all five ID sites)

- `SavedBuffState.Wanted` (target unit IDs)
- `SavedBuffState.Casters` keys: `CasterKey.Name` holds the caster's `UniqueId` (since the V1 migration)
- `SavedBufferState.CasterRanks` keys
- `SavedBufferState.MountPreference` keys **and values** (rider → pet)
- `SavedBufferState.FuryWeaponPreference` keys

One function `MapIds(SavedBufferState, Func<string, string>)` rewrites all five. Export calls it with `UniqueId → token`, resolution with `token → UniqueId`. A mapper that returns `null` drops that entry. Any field with a unit ID that gets added later must be added to `MapIds`; leave a comment on `SavedBufferState` saying so.

## Export

1. Flush the current state: `state.Save()`, non-shallow.
2. Deep-copy the in-memory `SavedBufferState` (serialize, then deserialize).
3. Build `UniqueId → token` for all party units: active group, `Player.RemoteCompanions`, and the pets of both, independent of `Bubble.ShowReserve`.
4. Run `MapIds`. Existing `@` tokens (still-waiting entries from an earlier import) pass through unchanged, so re-sharing an imported setup keeps them. IDs of units that no longer exist (dismissed or dead mercs) can't be tokenized and are dropped.
5. Write `<ModPath>/Exports/<MainCharacterName>-<yyyy-MM-dd-HHmm>.json`, with the filename sanitized, as a wrapper:
   ```json
   { "Format": "bi2tl-setup", "FormatVersion": 1, "ModVersion": "1.23.0",
     "ExportedFrom": "<main character name>", "Setup": { ...SavedBufferState... } }
   ```
6. Write a game-log message with the file name.

## Import

1. Read the file. Reject it (game-log message, nothing changed) if `Format` doesn't match or `FormatVersion` is newer than the mod supports.
2. Back up the current settings file to `UserSettings/bi2tl-<GameId>.backup-<yyyy-MM-dd-HHmmss>.json`.
3. Take `Setup` as the new `SavedBufferState` (**replace**, user decision). Run the resolution step below on it.
4. Write it to `SettingsPath` and rebuild the buff state from it, the same way `CreateBuffstate` builds it on open, so the window shows the imported setup at once.
5. Write a game-log message: how many characters were applied now, and which are waiting, by display name. Companion names come from the blueprint, merc names from the token.

Global settings (UMD retries, source priority, shortcuts, enabled sources, combat-start options) come along. Import replaces them too.

## Resolution (once per token)

- **Runs** at import and whenever the party may have changed. Builder picks the hook; `Bubble.RefreshGroup` already runs on window open and party change. It also needs to catch the save being loaded while a companion is already in the party.
- **Ordering constraint**: resolution must run before the buff list is built and before any `Save()` can write entries under a newly joined unit's real `UniqueId`. Otherwise the collision rule below could treat the imported entry as the loser against an empty or default write.
- **Considers** all party units: active, reserve (`RemoteCompanions`) and their pets, regardless of `ShowReserve`. Otherwise a benched companion would wait forever.
- **Cost**: when the save holds no tokens, which is the normal case, the check must cost nothing beyond a flag. Compute "has tokens" once on load and after each resolution, not by scanning the save on every refresh.
- **Matching**: build `token → UniqueId` from the current units. When two units produce the same token (two mercs with the same name, two pets of the same type), the first one wins. The others stay unmatched, and the token is used up once it maps.
- **Applying**: run `MapIds` with the token → `UniqueId` map; unmatched tokens map to themselves and keep waiting. Then `Save`, then rebuild the affected `BubbleBuff`s from the save. They only read their state in `InitialiseFromSave`, which runs while the buff list is built.
- **Collision**: if a key already exists under the resolved `UniqueId` (for example `CasterRanks` already holds a value the user set for that unit), the existing value wins and the token entry is dropped. This guarantees the anti-goal even in odd orderings. `Wanted` is a set union.

Tokens that never match (a companion not recruited in this playthrough) stay in the save forever. That is harmless, and the next import replaces them.

### Save() must keep tokens

The existing `BufferState.Save()` only adds and removes `Wanted` IDs of units in `ConfigGroup` and never removes unknown IDs, so tokens survive. Non-shallow `Save()` drops a `SavedBuffState` whose `Wanted` is empty and has no other config. An entry with only tokens in `Wanted` is non-empty, so it is kept. Builder: confirm that `CasterRanks`, `MountPreference` and `FuryWeaponPreference` writers never prune entries for absent units.

## Known limits (accepted)

- If the main character plays another class, the imported caster settings (ban, cap, priority override) are keyed by a spellbook GUID they don't have. Those entries stay dormant. Target choices (who gets which buff) still transfer.
- Buffs the party can't cast yet (low level, spell not learned) keep their imported config in the save and show up configured once the spell appears. The current save format already behaves this way.

## UI

- Two buttons, **Export setup** and **Import setup**, in the existing settings panel of the buff window (`MakeSettings`, `BubbleBuffer.cs:726`).
- Import opens a list of the `.json` files in `<ModPath>/Exports/`, newest first, then a confirmation: "Replaces your current setup. A backup is written to UserSettings."
- Players share setups by dropping files into that folder. The README and the Nexus description say where it is.
- Every new string key goes into all five locale files (`en_GB`, `de_DE`, `fr_FR`, `ru_RU`, `zh_CN`), keeping each file's BOM state. Key-count parity check per CLAUDE.md.
- Builder decides the list widget and the layout. Follow the existing settings-panel patterns and `claude-context/gotchas-ui.md`.

## Testing

- No unit-test project exists. Test in-game on the Deck via DevBridge (skill `testing-mods-in-game`, fixture save `BridgeTest`). Give import and export static entry points so DevBridge can call them without clicking.
- Checks:
  1. Export, then import into the same playthrough: setup identical, no tokens left.
  2. Import a file whose `@comp` token belongs to a companion not currently in the party: entry waits. A hand-edited token for a party companion resolves on the next refresh.
  3. After resolution, change that companion's setup, refresh several times, reload the save: the user's change stays (anti-goal).
  4. A broken file or wrong `Format` is rejected without touching the setup, and no backup is written.
- Log lines for export, import and each resolution (`[Import] resolved @comp:… → <name>`), so a smoke test can prove the path ran.

## Left to the builder

The resolution hook point, the pet-owner lookup, the list widget, and exact message texts.
