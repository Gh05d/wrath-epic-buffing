using BuffIt2TheLimit.Config;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Controllers;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Common;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.TargetCheckers;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BuffIt2TheLimit {

    public interface IBuffExecutionEngine {
        public IEnumerator CreateSpellCastRoutine(List<CastTask> tasks);
    }
    public class BubbleBuffGlobalController : MonoBehaviour {

        public static BubbleBuffGlobalController Instance { get; private set; }

        public const int BATCH_SIZE = 8;
        public const float DELAY = 0.05f;

        // Shortcut capture state
        public static bool CapturingActive = false;
        public static Action<ShortcutBinding> OnShortcutCaptured = null;

        // Cached enum arrays to avoid per-frame allocation in Update()
        private static readonly HashSet<KeyCode> ModifierKeys = new() {
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt,
            KeyCode.LeftCommand, KeyCode.RightCommand,
            KeyCode.LeftApple, KeyCode.RightApple,
        };
        // Keyboard keys plus the extra mouse buttons (thumb + side: Mouse3..Mouse6).
        // LMB/RMB/MMB (Mouse0..Mouse2) and joystick buttons (>= Mouse6's successor) are
        // excluded — LMB arms the rebind row and would self-capture; RMB/MMB are game controls.
        private static readonly KeyCode[] BindableKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(kc => kc < KeyCode.Mouse0 || (kc >= KeyCode.Mouse3 && kc <= KeyCode.Mouse6))
            .Where(kc => !ModifierKeys.Contains(kc))
            .ToArray();
        private static readonly BuffGroup[] BuffGroups = (BuffGroup[])Enum.GetValues(typeof(BuffGroup));

        private void Awake() {
            Instance = this;
        }

        private void Update() {
            // Round limit deactivation check
            GlobalBubbleBuffer.RoundLimitWatcher?.Tick();

            // Handle pending open-buff-mode from the quick open button.
            // Two-phase approach: Phase 0 waits for spellbook ready, Phase 1 monitors PartyView.
            // The game's spellbook animation can re-show PartyView after our HideAnimation call,
            // so we keep checking and re-hiding for a few frames after ToggleBuffMode.
            var instance = GlobalBubbleBuffer.Instance;
            if (instance != null && instance.PendingOpenBuffMode) {
                instance.pendingFrameCount++;
                if (instance.pendingFrameCount > 120) {
                    instance.ResetPendingState();
                    Main.Log("BuffIt2TheLimit: Pending open buff mode timed out");
                } else if (instance.pendingPhase == 0) {
                    // Phase 0: Wait for spellbook controller to be ready
                    try {
                        if (instance.SpellbookController != null && instance.SpellbookController.IsReady
                            && !instance.SpellbookController.Buffing) {
                            instance.SpellbookController.ToggleBuffMode();
                            instance.pendingPhase = 1;
                            instance.pendingHideFrames = 0;
                        }
                    } catch (Exception ex) {
                        instance.ResetPendingState();
                        Main.Error(ex, "Pending open buff mode");
                    }
                } else if (instance.pendingPhase == 1) {
                    // Phase 1: Monitor PartyView — game animation may un-hide it after our call
                    instance.pendingHideFrames++;
                    try {
                        if (instance.SpellbookController != null) {
                            instance.SpellbookController.EnsurePartyViewHidden();
                        }
                        if (instance.pendingHideFrames >= 30) {
                            instance.ResetPendingState();
                        }
                    } catch (Exception ex) {
                        instance.ResetPendingState();
                        Main.Error(ex, "Pending hide party view");
                    }
                }
            }

            // Handle keyboard shortcut capture
            if (CapturingActive) {
                foreach (KeyCode kc in BindableKeys) {
                    if (Input.GetKeyDown(kc)) {
                        var binding = kc == KeyCode.Escape ? ShortcutBinding.None : ShortcutBinding.Capture(kc);
                        OnShortcutCaptured?.Invoke(binding);
                        CapturingActive = false;
                        OnShortcutCaptured = null;
                        break;
                    }
                }
            } else {
                // Handle buff group shortcut execution
                var state = GlobalBubbleBuffer.Instance?.SpellbookController?.state;
                if (state != null) {
                    foreach (BuffGroup group in BuffGroups) {
                        var binding = state.GetShortcut(group);
                        if (binding.IsPressed()) {
                            GlobalBubbleBuffer.Execute(group);
                        }
                    }

                    // Handle open-buff-menu shortcut
                    if (state.GetOpenBuffMenuShortcut().IsPressed()) {
                        instance?.OpenBuffMenu();
                    }
                }
            }
        }

        public void Destroy() {
        }

        public void EndSuppression() { UnitBuffPartView.EndSuppresion(); }

        public void CastSpells(List<CastTask> tasks, bool armorBypass = false) {
            IEnumerator castingCoroutine = Engine.CreateSpellCastRoutine(tasks);
            if (armorBypass)
                castingCoroutine = BuffExecutor.WithArmorBypass(castingCoroutine);
            StartCoroutine(castingCoroutine);
        }

        // Same as CastSpells but emits the user-facing combat-log message AFTER the
        // casting coroutine completes, using the actual fired-cast count rather than
        // the queue size. Avoids overstating success when commands get dropped (e.g.
        // animation-speed mods truncate UnitCommands before RuleCastSpell triggers).
        internal void CastSpellsAndLog(List<CastTask> tasks, bool armorBypass, string title, int attempted, int skipped, TooltipTemplateBuffer tooltip) {
            IEnumerator castingCoroutine = Engine.CreateSpellCastRoutine(tasks);
            if (armorBypass)
                castingCoroutine = BuffExecutor.WithArmorBypass(castingCoroutine);
            StartCoroutine(BuffExecutor.WithDeferredLog(castingCoroutine, tasks, title, attempted, skipped, tooltip));
        }

        public static IBuffExecutionEngine Engine =>
            GlobalBubbleBuffer.Instance.SpellbookController.state.VerboseCasting 
                ? new AnimatedExecutionEngine() 
                : new InstantExecutionEngine();
    }
    public class BuffExecutor {
        public BufferState State;

        // Active while the mod's own cast coroutine is running.
        // Read by ArcaneSpellFailurePatch to gate the ASF bypass.
        public static int ArmorBypassActive;

        public static IEnumerator WithArmorBypass(IEnumerator inner) {
            ArmorBypassActive++;
            try {
                while (inner.MoveNext()) {
                    yield return inner.Current;
                }
            } finally {
                ArmorBypassActive--;
                // Propagate disposal so the inner routine's finally (handler sweep)
                // runs even when this wrapper is stopped externally.
                (inner as IDisposable)?.Dispose();
            }
        }

        // Walks the casting coroutine to completion, then emits the combat-log
        // message with `applied = tasks where ActuallyFired` instead of the queue
        // size. The flag is set in EngineCastingHandler when RuleCastSpell actually
        // fires, so dropped commands no longer inflate the reported count.
        internal static IEnumerator WithDeferredLog(IEnumerator inner, List<CastTask> tasks, string title, int attempted, int skipped, TooltipTemplateBuffer tooltip) {
            try {
                while (inner.MoveNext()) {
                    yield return inner.Current;
                }
            } finally {
                // Propagate disposal so the inner routine's finally (handler sweep)
                // runs even when this wrapper is stopped externally.
                (inner as IDisposable)?.Dispose();
            }

            int applied = tasks.Count(t => t.ActuallyFired);
            var messageString = $"{title} {"log.applied".i8()} {applied}/{attempted} ({"log.skipped".i8()} {skipped})";
            Main.Verbose(messageString);

            try {
                var message = new CombatLogMessage(messageString, Color.blue, PrefixIcon.RightArrow, tooltip, true);
                var messageLog = LogThreadService.Instance.m_Logs[LogChannelType.Common].First(x => x is MessageLogThread);
                messageLog.AddMessage(message);
            } catch (Exception ex) {
                Main.Error(ex, "Emitting combat log message");
            }
        }

        public BuffExecutor(BufferState state) {
            State = state;
        }

        // Resolves the actual ActivatableAbility to flip IsOn on.
        // For ShiftersFury the parent is ActivationDisable-locked; the real activators live on
        // ShiftersFuryPart.AppliedFacts (one per wielded natural weapon). We honour the player's
        // last-picked variant via ShiftersFuryPart.State.SelectedWeaponIndex and fall back to the
        // first applied fact if no pick has happened yet.
        internal static ActivatableAbility ResolveActivationTarget(UnitEntityData caster, ActivatableAbility candidate) {
            if (candidate?.ConversionsProvider is ShiftersFury) {
                var part = caster.Get<ShiftersFuryPart>();
                if (part?.AppliedFacts == null || part.AppliedFacts.Count == 0) return null;
                int idx = part.State?.SelectedWeaponIndex ?? -1;
                if (idx >= 0 && idx < part.AppliedFacts.Count) return part.AppliedFacts[idx];
                return part.AppliedFacts[0];
            }
            return candidate;
        }

        // ShiftersFury parent's IsOn is always false (ActivationDisable blocks SetIsOn).
        // The real aggregate "is on" state lives on ShiftersFuryPart.m_IsOn.
        internal static bool IsEffectivelyOn(UnitEntityData caster, ActivatableAbility candidate) {
            if (candidate?.ConversionsProvider is ShiftersFury) {
                var part = caster.Get<ShiftersFuryPart>();
                return part != null && part.m_IsOn;
            }
            if (candidate == null) return false;
            // Targeted toggles (the stock Mount toggle): bare IsOn can be a wedged
            // "armed, waiting for target" state — SetIsOn(true) without a target only arms
            // the global ClickWithSelectedAbilityHandler, and a second arming in the same
            // pass drops the first while leaving its IsOn flag set. Only on-with-target
            // counts as active; Stop/OnDidTurnOff clear m_Target, so a dismounted rider
            // reads as off again.
            if (candidate.Blueprint.IsTargeted)
                return candidate.IsOn && candidate.m_Target != null;
            return candidate.IsOn;
        }

        internal enum MountResult { NotTargeted, Mounted, NoCandidate, Ambiguous }

        // Targeted activatables (the stock Mount toggle is the only vanilla case) cannot be
        // activated via IsOn/TryStart: SetIsOn(true) without a target only arms the single
        // GLOBAL ClickWithSelectedAbilityHandler with the rider's SelectTargetAbility (the
        // cursor change), and TryStart returns while IsWaitingForTarget. Arming a second
        // rider's toggle in the same pass calls DropAbility() on the first — at most one
        // rider per run could ever mount, and the dropped toggle wedges at IsOn=true with
        // no target. Instead resolve the rider's suitable pet with the game's own target
        // checkers (CanMount enforces pet.Master == rider, conscious, no polymorph; the
        // size checker wants a bigger pet) and mount directly — the exact call the click
        // path's ContextActionMount makes. UnitPartRider.Mount flips the toggle
        // on-with-target itself. Several suitable pets on one rider (Azata's grown Aivu +
        // an animal companion) resolve through the rider's saved mount preference
        // (caster-popout picker); without one the pass skips and the player saddles
        // manually.
        internal static List<UnitEntityData> GetMountCandidates(UnitEntityData rider) {
            return Bubble.Group
                .Where(u => AbilityTargetIsSuitableMount.CanMount(rider, u)
                         && AbilityTargetIsSuitableMountSize.CanMount(rider, u))
                .ToList();
        }

        internal static MountResult TryMountTargeted(UnitEntityData rider, ActivatableAbility activatable, out UnitEntityData mount) {
            mount = null;
            if (!activatable.Blueprint.IsTargeted) return MountResult.NotTargeted;

            var candidates = GetMountCandidates(rider);
            if (candidates.Count == 0) return MountResult.NoCandidate;

            UnitEntityData chosen;
            if (candidates.Count == 1) {
                chosen = candidates[0];
            } else {
                // A preference pointing at a pet that is currently unsuitable (dead,
                // polymorphed, left the party) counts as unset — fall back to the skip.
                var prefs = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState?.MountPreference;
                chosen = prefs != null && prefs.TryGetValue(rider.UniqueId, out var petId)
                    ? candidates.FirstOrDefault(c => c.UniqueId == petId)
                    : null;
                if (chosen == null) return MountResult.Ambiguous;
            }

            // A wedged armed toggle (on, no target — left by an overwritten arming or a
            // pre-1.16 run) would make Mount's SetIsOnWithTarget early-return without
            // recording the target; reset it so the toggle ends up cleanly on-with-target.
            if (activatable.IsOn)
                activatable.IsOn = false;

            mount = chosen;
            rider.Ensure<UnitPartRider>().Mount(mount);
            return MountResult.Mounted;
        }

        private Dictionary<BuffGroup, float> lastExecutedForGroup = new() {
            { BuffGroup.Long, -1 },
            { BuffGroup.Important, -1 },
            { BuffGroup.Quick, -1 },
        };

        // Mass/burst spells center on the caster — avoids movement-to-target interrupts
        // at combat start. But AoE-ally spells with CanTargetSelf=false (e.g. Expanded
        // Content's Zephyr's Fleetness) make RuleCastSpell.OnTrigger abort on the caster
        // as target — for those, center on the ValidateMass-chosen wanted target. That
        // choice used BuffProvider.CanTarget at validate time and can be stale by cast
        // time, so re-check it and fall back to any currently targetable party member.
        private static TargetWrapper ResolveMassTarget(BubbleBuff buff, AbilityData spellToCast, UnitEntityData caster, UnitEntityData fallbackTarget) {
            var self = new TargetWrapper(caster);
            if (spellToCast == null || spellToCast.CanTarget(self)) return self;
            var center = fallbackTarget;
            if (!spellToCast.CanTarget(new TargetWrapper(center))) {
                center = Bubble.Group.FirstOrDefault(u => spellToCast.CanTarget(new TargetWrapper(u))) ?? fallbackTarget;
            }
            Main.Log($"Mass buff '{buff.Name}' cannot target its caster, centering on {center.CharacterName}");
            return new TargetWrapper(center);
        }

        public void Execute(BuffGroup buffGroup) {
            if (Game.Instance.Player.IsInCombat && !State.AllowInCombat)
                return;

            var lastExecuted = lastExecutedForGroup[buffGroup];
            if (lastExecuted > 0 && (Time.realtimeSinceStartup - lastExecuted) < .5f) {
                return;
            }
            lastExecutedForGroup[buffGroup] = Time.realtimeSinceStartup;

            Main.Verbose($"Begin buff: {buffGroup}");

            State.Recalculate(false, buffGroup);

            // Phase 0: Activate activatable abilities before casting buffs.
            // Order matters — ShiftersFury's real activation target (AppliedFacts) only materialises
            // after a form-change activatable (Chimeric Aspect, etc.) has populated natural weapons.
            // Three priority buckets (OrderBy is stable, so within a bucket .Reverse() order holds):
            //   0: normal activatables (Rage, Bardic Performance, …)
            //   1: Pattern-B parents with sub-selections (Chimeric Aspect — activates the form)
            //   2: Shifter's Fury (needs AppliedFacts from the form to exist first)
            foreach (var actBuff in State.BuffList
                .Where(b => b.IsActivatable && b.InGroups.Contains(buffGroup) && b.Fulfilled > 0)
                .Reverse()
                .OrderBy(b => b.ActivatableSource?.ConversionsProvider is ShiftersFury ? 2
                           : b.ActivatableSource?.ConversionsProvider != null ? 1 : 0)) {
                if (actBuff.ActualCastQueue == null) continue;
                foreach (var (_, provider) in actBuff.ActualCastQueue) {
                    try {
                        var activatable = provider.ActivatableSource ?? actBuff.ActivatableSource;
                        if (activatable == null) {
                            Main.Verbose($"Activatable {actBuff.Name}: null source, skipping");
                            continue;
                        }

                        var caster = provider.who;
                        if (caster == null) continue;

                        if (IsEffectivelyOn(caster, activatable)) {
                            Main.Verbose($"Activatable {actBuff.Name}: already active on {caster.CharacterName}, skipping");
                            continue;
                        }

                        var group = activatable.Blueprint.Group;
                        // Per-group cap is dynamic: features like Aeon's mythic gaze (Lv6/Lv10) or
                        // other "IncreaseActivatableAbilityGroupSize" components raise it above the
                        // default of 1. Count live on-state activatables in the group and compare
                        // against UnitPartActivatableAbility.GetGroupSize. Group.None has no cap.
                        if (group != ActivatableAbilityGroup.None) {
                            int cap = caster.Get<UnitPartActivatableAbility>()?.GetGroupSize(group) ?? 1;
                            int activeInGroup = caster.ActivatableAbilities.RawFacts
                                .Count(a => a.Blueprint.Group == group && IsEffectivelyOn(caster, a));
                            if (activeInGroup >= cap) {
                                Main.Log($"Activatable {actBuff.Name}: skipped — {group} cap reached for {caster.CharacterName} ({activeInGroup}/{cap})");
                                continue;
                            }
                        }

                        var target = ResolveActivationTarget(caster, activatable);
                        if (target == null) {
                            Main.Log($"Activatable {actBuff.Name}: no activation target (no conversions available for {caster.CharacterName})");
                            continue;
                        }

                        if (!target.IsAvailable) {
                            Main.Verbose($"Activatable {actBuff.Name}: not available for {caster.CharacterName} (resources or restrictions)");
                            continue;
                        }

                        var mountResult = TryMountTargeted(caster, target, out var mount);
                        if (mountResult != MountResult.NotTargeted) {
                            switch (mountResult) {
                                case MountResult.Mounted:
                                    Main.Verbose($"Activatable {actBuff.Name}: mounted {caster.CharacterName} on {mount.CharacterName}");
                                    if (actBuff.DeactivateAfterRounds > 0)
                                        GlobalBubbleBuffer.RoundLimitWatcher?.TrackActivation(activatable.Blueprint.AssetGuid);
                                    break;
                                case MountResult.NoCandidate:
                                    Main.Log($"Activatable {actBuff.Name}: no suitable mount for {caster.CharacterName} (needs their own conscious pet of larger size in the party)");
                                    break;
                                case MountResult.Ambiguous:
                                    Main.Log($"Activatable {actBuff.Name}: several suitable mounts for {caster.CharacterName} — pick a preferred mount in the caster popout or saddle up manually");
                                    break;
                            }
                            continue;
                        }

                        Main.Verbose($"Activating: {actBuff.Name} on {caster.CharacterName}");
                        target.IsOn = true;
                        if (!target.IsStarted)
                            target.TryStart();
                        if (actBuff.DeactivateAfterRounds > 0)
                            GlobalBubbleBuffer.RoundLimitWatcher?.TrackActivation(activatable.Blueprint.AssetGuid);
                    } catch (Exception ex) {
                        Main.Error(ex, $"activating {actBuff.Name}");
                    }
                }
            }

            TargetWrapper[] targets = Bubble.Group.Select(u => new TargetWrapper(u)).ToArray();
            int attemptedCasts = 0;
            int skippedCasts = 0;


            var tooltip = new TooltipTemplateBuffer();


            var unitBuffs = Bubble.Group.Select(u => new UnitBuffData(u)).ToDictionary(bd => bd.Unit.UniqueId);

            List<CastTask> tasks = new();

            Dictionary<UnitEntityData, int> remainingArcanistPool = new Dictionary<UnitEntityData, int>();
            Dictionary<Kingmaker.Items.ItemEntity, int> remainingRodCharges = new();
            BlueprintScriptableObject arcanistPoolBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>("cac948cbbe79b55459459dd6a8fe44ce");

            // Requested (not Fulfilled) — buffs whose wanted targets all lost their caster
            // (spell slots spent, no scroll/potion fallback) still need a log entry below.
            foreach (var buff in State.BuffList.Where(b => b.InGroups.Contains(buffGroup) && b.Requested > 0)) {

                try {
                    if (buff.IsActivatable) continue; // Activatables handled in Phase 0

                    int thisBuffGood = 0;
                    int thisBuffSkip = 0;
                    var thisBuffSourceCounts = new Dictionary<BuffSourceType, int>();
                    bool anyExtendRod = false;
                    TooltipTemplateBuffer.BuffResult badResult = null;

                    // Null when Validate() found no castable provider for any wanted target
                    foreach (var (target, caster) in buff.ActualCastQueue ?? Enumerable.Empty<(string, BuffProvider)>()) {
                        var forTarget = unitBuffs[target];

                        // For mass spells, check if ANY wanted target is missing the buff.
                        // The single CastTask target may already have it, but others might not.
                        if (buff.IsMass) {
                            bool anyTargetMissingBuff = Bubble.Group.Any(u =>
                                buff.UnitWants(u) && !buff.BuffsApplied.IsPresent(unitBuffs[u.UniqueId], buff.IgnoreForOverwriteCheck));
                            if (!anyTargetMissingBuff && !State.OverwriteBuff) {
                                thisBuffSkip++;
                                skippedCasts++;
                                continue;
                            }
                        } else if (buff.BuffsApplied.IsPresent(forTarget, buff.IgnoreForOverwriteCheck) && !State.OverwriteBuff) {
                            thisBuffSkip++;
                            skippedCasts++;
                            continue;
                        }

                        // Note: credit availability was already validated in BubbleBuff.Validate()
                        // which built the ActualCastQueue. Do NOT re-check credits here —
                        // Validate() already consumed them via ChargeCredits(), so the value
                        // is 0 even though the cast was legitimately planned.

                        attemptedCasts++;

                        AbilityData spellToCast;
                        if (!caster.SlottedSpell.IsAvailable) {
                            if (badResult == null)
                                badResult = tooltip.AddBad(buff);
                            badResult.messages.Add($"  [{caster.who.CharacterName}] => [{Bubble.GroupById[target].CharacterName}], {"noslot".i8()}");
                            continue;
                        }

                        // UMD check for scroll usage
                        if (caster.SourceType == BuffSourceType.Scroll && caster.RequiresUmdCheck) {
                            int maxRetries = State.SavedState.UmdRetries;
                            bool passed = false;
                            for (int retry = 0; retry < maxRetries; retry++) {
                                if (caster.TryUmdCheck()) {
                                    passed = true;
                                    break;
                                }
                            }
                            if (!passed) {
                                Main.Verbose($"UMD retries exhausted for {caster.who.CharacterName} using {buff.Name}");
                                if (badResult == null)
                                    badResult = tooltip.AddBad(buff);
                                badResult.messages.Add($"  [{caster.who.CharacterName}] => [{Bubble.GroupById[target].CharacterName}], {"log.umd-retries-exhausted".i8()}");
                                continue;
                            }
                        }

                        // Azata Zippy Magic
                        var priorSpellTasks = tasks.Where(x => x.Caster == caster.who && x.SlottedSpell.UniqueId == caster.SlottedSpell.UniqueId).ToList();
                        
                        // Check to see if this spell does count for casting
                        if (!caster.AzataZippyMagic || (caster.AzataZippyMagic && priorSpellTasks.Count() % 2 == 0)) {
                            int neededArcanistPool = 0;
                            if (caster.PowerfulChange) {
                                var PowerfulChangeRssLogic = AbilityCache.CasterCache[caster.who.UniqueId]?.PowerfulChange?.GetComponent<AbilityResourceLogic>();
                                var PowerfulChangeCost = PowerfulChangeRssLogic ? PowerfulChangeRssLogic.CalculateCost(caster.spell) : 1;
                                neededArcanistPool += Math.Max(0, PowerfulChangeCost);
                            }
                            if (caster.ShareTransmutation && caster.who != forTarget.Unit) {
                                var ShareTransmutationRssLogic = AbilityCache.CasterCache[caster.who.UniqueId]?.ShareTransmutation?.GetComponent<AbilityResourceLogic>();
                                var ShareTransmutationCost = ShareTransmutationRssLogic ? ShareTransmutationRssLogic.CalculateCost(caster.spell) : 1;
                                neededArcanistPool += Math.Max(0, ShareTransmutationCost);
                            }
                            if (caster.ReservoirCLBuff) {
                                var ReservoirCLBuffRssLogic = AbilityCache.CasterCache[caster.who.UniqueId]?.ReservoirCLBuff?.GetComponent<AbilityResourceLogic>();
                                var ReservoirCLBuffCost = ReservoirCLBuffRssLogic ? ReservoirCLBuffRssLogic.CalculateCost(caster.spell) : 1;
                                neededArcanistPool += Math.Max(0, ReservoirCLBuffCost);
                            }

                            if (neededArcanistPool != 0) {
                                int availableArcanistPool;
                                if (remainingArcanistPool.ContainsKey(caster.who)) {
                                    availableArcanistPool = remainingArcanistPool[caster.who];
                                } else {
                                    availableArcanistPool = caster.who.Resources.GetResourceAmount(arcanistPoolBlueprint);
                                }
                                if (availableArcanistPool < neededArcanistPool) {
                                    if (badResult == null)
                                        badResult = tooltip.AddBad(buff);
                                    badResult.messages.Add($"  [{caster.who.CharacterName}] => [{Bubble.GroupById[target].CharacterName}], {"noarcanist".i8()}");
                                    continue;
                                } else {
                                    remainingArcanistPool[caster.who] = availableArcanistPool - neededArcanistPool;
                                }
                            }
                        }

                        // This is a free cast
                        var IsDuplicateSpellApplied = false;
                        if (caster.AzataZippyMagic && priorSpellTasks.Count() % 2 == 1) {
                            IsDuplicateSpellApplied = true;
                        }


                        var touching = caster.spell.Blueprint.GetComponent<AbilityEffectStickyTouch>();
                        Main.Verbose("Adding cast task for: " + caster.spell.Name, "apply");
                        if (touching) {
                            Main.Verbose("   Switching spell to touch => " + touching.TouchDeliveryAbility.Name, "apply");
                            spellToCast = new AbilityData(caster.spell, touching.TouchDeliveryAbility);
                        } else {
                            spellToCast = caster.spell;
                        }
                        var spellParams = spellToCast.CalculateParams();

                        var task = new CastTask {
                            SlottedSpell = caster.SlottedSpell,
                            Target = buff.IsMass ? ResolveMassTarget(buff, spellToCast, caster.who, forTarget.Unit) : new TargetWrapper(forTarget.Unit),
                            Caster = caster.who,
                            SpellToCast = spellToCast,
                            PowerfulChange = caster.SourceType == BuffSourceType.Spell && caster.PowerfulChange,
                            ShareTransmutation = caster.SourceType == BuffSourceType.Spell && caster.ShareTransmutation,
                            ReservoirCLBuff = caster.SourceType == BuffSourceType.Spell && caster.ReservoirCLBuff,
                            AzataZippyMagic = caster.SourceType == BuffSourceType.Spell && caster.AzataZippyMagic,
                            IsDuplicateSpellApplied = IsDuplicateSpellApplied,
                            SelfCastOnly = caster.SelfCastOnly,
                            SourceType = caster.SourceType,
                            SourceItem = caster.SourceItem,
                            OriginalMetamagicWasNull = spellToCast.MetamagicData == null,
                            OriginalMetamagicMask = spellToCast.MetamagicData?.MetamagicMask ?? (Metamagic)0,
                            OriginalSpellLevelCost = spellToCast.MetamagicData?.SpellLevelCost ?? 0,
                            OriginalHeightenLevel = spellToCast.MetamagicData?.HeightenLevel ?? 0,
                        };

                        // Extend Rod lookup
                        // Only for spell-source casts — scroll/wand/equipment casts don't have a
                        // spellbook to determine spell level from. Future extension possible.
                        if (buff.UseExtendRod && caster.SourceType == BuffSourceType.Spell) {
                            int spellLevel = caster.spell.Spellbook.GetSpellLevel(caster.spell);
                            var rod = BufferState.FindBestExtendRod(spellLevel, remainingRodCharges);
                            if (rod != null) {
                                task.MetamagicRodItem = rod;
                                remainingRodCharges[rod] = remainingRodCharges[rod] - 1;
                                anyExtendRod = true;
                                Main.Verbose($"Extend Rod applied: {rod.Name} for {buff.Name}");
                            } else {
                                Main.Log($"Extend Rod unavailable for {buff.Name}, casting normally");
                            }
                        }

                        tasks.Add(task);

                        // Warn if last item of this type
                        if (caster.SourceType != BuffSourceType.Spell && caster.SourceItem != null) {
                            if (caster.AvailableCredits <= 1) {
                                Main.Log($"{"log.last-item-consumed".i8()}: {buff.Name} ({caster.SourceType})");
                            }
                        }

                        thisBuffGood++;
                        thisBuffSourceCounts.TryGetValue(caster.SourceType, out var sc);
                        thisBuffSourceCounts[caster.SourceType] = sc + 1;
                    }

                    // Wanted targets Validate() couldn't assign any caster for (slots spent,
                    // no scroll/potion fallback): Validate skips unavailable providers, so
                    // these never reach ActualCastQueue and previously vanished from the
                    // report entirely ("applied 0/0 (skipped 0)"). Already-buffed targets
                    // count as skipped; the rest are failures the user should see.
                    bool anyNoCaster = false;
                    if (buff.IsMass) {
                        // A mass buff is one cast — one report entry, not one per member
                        if (buff.Fulfilled == 0 && Bubble.Group.Any(buff.UnitWants)) {
                            bool anyTargetMissingBuff = Bubble.Group.Any(u =>
                                buff.UnitWants(u) && !buff.BuffsApplied.IsPresent(unitBuffs[u.UniqueId], buff.IgnoreForOverwriteCheck));
                            if (!anyTargetMissingBuff && !State.OverwriteBuff) {
                                thisBuffSkip++;
                                skippedCasts++;
                            } else {
                                if (badResult == null)
                                    badResult = tooltip.AddBad(buff);
                                badResult.messages.Add($"  {"log.no-caster".i8()}");
                                attemptedCasts++;
                                anyNoCaster = true;
                            }
                        }
                    } else {
                        foreach (var unit in Bubble.Group) {
                            if (!buff.UnitWants(unit) || buff.UnitGiven(unit))
                                continue;
                            if (buff.BuffsApplied.IsPresent(unitBuffs[unit.UniqueId], buff.IgnoreForOverwriteCheck) && !State.OverwriteBuff) {
                                thisBuffSkip++;
                                skippedCasts++;
                            } else {
                                if (badResult == null)
                                    badResult = tooltip.AddBad(buff);
                                badResult.messages.Add($"  [{unit.CharacterName}], {"log.no-caster".i8()}");
                                attemptedCasts++;
                                anyNoCaster = true;
                            }
                        }
                    }
                    // Player.log detail for support — the tooltip line is generic, the
                    // real rejection reason (slots spent / source disabled / can't target)
                    // is only known per provider. Mirrors the [CSD] diagnostics pattern.
                    if (anyNoCaster) {
                        if (buff.CasterQueue.Count == 0) {
                            Main.Log($"no caster for '{buff.Name}': no providers found by scan");
                        } else {
                            foreach (var c in buff.CasterQueue) {
                                string reason;
                                try { reason = buff.DiagnoseCaster(c); }
                                catch (Exception ex) { reason = $"<diagnose threw: {ex.Message}>"; }
                                Main.Log($"no caster for '{buff.Name}': {buff.FormatCaster(c)} → {reason}");
                            }
                        }
                    }

                    if (thisBuffGood > 0) {
                        var goodResult = tooltip.AddGood(buff);
                        goodResult.count = thisBuffGood;
                        goodResult.sourceCounts = thisBuffSourceCounts;
                        goodResult.ExtendRodUsed = anyExtendRod;
                    }
                    if (thisBuffSkip > 0)
                        tooltip.AddSkip(buff).count = thisBuffSkip;

                } catch (Exception ex) {
                    // buff.Spell is null for songs — Name handles all source types
                    Main.Error(ex, $"casting buff: {buff.Name}");
                }
            }

            bool armorBypass = State.BypassArcaneSpellFailure && !Game.Instance.Player.IsInCombat;
            string title = buffGroup.i8();
            BubbleBuffGlobalController.Instance.CastSpellsAndLog(tasks, armorBypass, title, attemptedCasts, skippedCasts, tooltip);
        }

        public void ExecuteCombatStart() {
            Main.Log("[CSD] === Combat Start ===");

            // Recalculate for all groups
            State.Recalculate(false);

            // Header: mod version, engine choice, party composition
            try {
                string version = ModSettings.ModEntry?.Info?.Version ?? "?";
                int activeCount = Bubble.Group?.Count ?? -1;
                int reserveCount = Game.Instance?.Player?.RemoteCompanions != null ? Game.Instance.Player.RemoteCompanions.Count() : -1;
                Main.Log($"[CSD] Mod={version} SkipAnim={State.SkipAnimationsOnCombatStart} CastAll={State.CastAllOnCombatStart} Active={activeCount} Reserve={reserveCount}");
                if (Bubble.Group != null) {
                    Main.Log($"[CSD] ActiveParty: {string.Join(", ", Bubble.Group.Select(u => u.CharacterName))}");
                }
            } catch (Exception ex) {
                Main.Error(ex, "[CSD] header");
            }

            var allBuffs = State.BuffList.ToList();
            // Global toggle only covers configured buffs (Requested > 0) — BuffList holds
            // every scanned spellbook entry, and unconfigured ones would spam the [CSD]
            // rejection diagnostics on every combat start.
            var combatStartBuffs = allBuffs.Where(b => b.CastOnCombatStart || (State.CastAllOnCombatStart && b.Requested > 0)).ToList();
            Main.Log($"[CSD] Marked={combatStartBuffs.Count} (of {allBuffs.Count} total)");
            foreach (var b in combatStartBuffs) {
                int queue = b.ActualCastQueue?.Count ?? -1;
                Main.Log($"[CSD] Buff '{b.Name}' IsActivatable={b.IsActivatable} Fulfilled={b.Fulfilled}/{b.Requested} Queue={queue} CasterQueue={b.CasterQueue.Count}");

                // When something failed to be queued, dump per-caster rejection reasons.
                bool noProgress = queue <= 0 || (b.IsActivatable && b.Fulfilled == 0);
                if (noProgress) {
                    if (b.CasterQueue.Count == 0) {
                        Main.Log("[CSD]   (no casters in queue — scan didn't find any provider)");
                    } else {
                        foreach (var c in b.CasterQueue) {
                            string reason;
                            try { reason = b.DiagnoseCaster(c); }
                            catch (Exception ex) { reason = $"<diagnose threw: {ex.Message}>"; }
                            Main.Log($"[CSD]   reject {b.FormatCaster(c)} → {reason}");
                        }
                    }
                } else if (b.ActualCastQueue != null) {
                    foreach (var (targetId, caster) in b.ActualCastQueue) {
                        string targetName = Bubble.GroupById.TryGetValue(targetId, out var t) ? t.CharacterName : targetId;
                        Main.Log($"[CSD]   queued: {b.FormatCaster(caster)} → {targetName}{(b.IsMass ? " (mass)" : "")}");
                    }
                }
            }

            // Phase 0: Activate activatable abilities marked for combat start
            int activatablesActivated = 0;
            foreach (var actBuff in combatStartBuffs
                .Where(b => b.IsActivatable && b.Fulfilled > 0)
                .Reverse()
                .OrderBy(b => b.ActivatableSource?.ConversionsProvider is ShiftersFury ? 2
                           : b.ActivatableSource?.ConversionsProvider != null ? 1 : 0)) {
                if (actBuff.ActualCastQueue == null || actBuff.ActualCastQueue.Count == 0) {
                    Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' (no eligible caster)");
                    continue;
                }
                foreach (var (_, provider) in actBuff.ActualCastQueue) {
                    try {
                        var activatable = provider.ActivatableSource ?? actBuff.ActivatableSource;
                        if (activatable == null) {
                            Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' (null source)");
                            continue;
                        }

                        var caster = provider.who;
                        if (caster == null) {
                            Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' (provider has no unit)");
                            continue;
                        }

                        if (IsEffectivelyOn(caster, activatable)) {
                            Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' on {caster.CharacterName} (already on)");
                            continue;
                        }

                        var group = activatable.Blueprint.Group;
                        // Per-group cap is dynamic: Aeon's mythic gaze (Lv6/Lv10), Master of Many
                        // Styles, and other IncreaseActivatableAbilityGroupSize content raise it above
                        // the default of 1. Count live on-state activatables in the group against
                        // UnitPartActivatableAbility.GetGroupSize instead of capping at one-per-group,
                        // so a caster who may hold several performances gets all of them on. The count
                        // is live (IsEffectivelyOn reads IsOn, which we set synchronously below), so it
                        // already reflects abilities activated earlier in this pass. Group.None = no cap.
                        if (group != ActivatableAbilityGroup.None) {
                            int cap = caster.Get<UnitPartActivatableAbility>()?.GetGroupSize(group) ?? 1;
                            int activeInGroup = caster.ActivatableAbilities.RawFacts
                                .Count(a => a.Blueprint.Group == group && IsEffectivelyOn(caster, a));
                            if (activeInGroup >= cap) {
                                Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' on {caster.CharacterName} (group {group} cap reached {activeInGroup}/{cap})");
                                continue;
                            }
                        }

                        var target = ResolveActivationTarget(caster, activatable);
                        if (target == null) {
                            Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' on {caster.CharacterName} (no activation target: no conversions available)");
                            continue;
                        }

                        if (!target.IsAvailable) {
                            Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' on {caster.CharacterName} (not available: resources/restrictions)");
                            continue;
                        }

                        var mountResult = TryMountTargeted(caster, target, out var mount);
                        if (mountResult != MountResult.NotTargeted) {
                            switch (mountResult) {
                                case MountResult.Mounted:
                                    Main.Log($"[CSD] Phase0 mounted {caster.CharacterName} on {mount.CharacterName} ('{actBuff.Name}')");
                                    activatablesActivated++;
                                    if (actBuff.DeactivateAfterRounds > 0)
                                        GlobalBubbleBuffer.RoundLimitWatcher?.TrackActivation(activatable.Blueprint.AssetGuid);
                                    break;
                                case MountResult.NoCandidate:
                                    Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' on {caster.CharacterName} (no suitable mount: needs their own conscious pet of larger size in the party)");
                                    break;
                                case MountResult.Ambiguous:
                                    Main.Log($"[CSD] Phase0 skip '{actBuff.Name}' on {caster.CharacterName} (several suitable mounts — pick a preferred mount in the caster popout or saddle up manually)");
                                    break;
                            }
                            continue;
                        }

                        string targetLabel = ReferenceEquals(target, activatable) ? actBuff.Name : $"{actBuff.Name} → {target.Blueprint.Name}";
                        Main.Log($"[CSD] Phase0 activate '{targetLabel}' on {caster.CharacterName}");
                        target.IsOn = true;
                        if (!target.IsStarted)
                            target.TryStart();
                        activatablesActivated++;
                        if (actBuff.DeactivateAfterRounds > 0)
                            GlobalBubbleBuffer.RoundLimitWatcher?.TrackActivation(activatable.Blueprint.AssetGuid);
                    } catch (Exception ex) {
                        Main.Error(ex, $"combat start: activating {actBuff.Name}");
                    }
                }
            }

            // Phase 1: Cast spells/abilities marked for combat start
            TargetWrapper[] targets = Bubble.Group.Select(u => new TargetWrapper(u)).ToArray();
            var unitBuffs = Bubble.Group.Select(u => new UnitBuffData(u)).ToDictionary(bd => bd.Unit.UniqueId);
            List<CastTask> tasks = new();
            Dictionary<Kingmaker.Items.ItemEntity, int> remainingRodCharges = new();
            int actuallyCast = 0;
            int skippedAlreadyActive = 0;

            foreach (var buff in combatStartBuffs.Where(b => !b.IsActivatable && b.Fulfilled > 0)) {
                try {
                    foreach (var (target, caster) in buff.ActualCastQueue) {
                        var forTarget = unitBuffs[target];

                        if (buff.IsMass) {
                            bool anyTargetMissingBuff = Bubble.Group.Any(u =>
                                buff.UnitWants(u) && !buff.BuffsApplied.IsPresent(unitBuffs[u.UniqueId], buff.IgnoreForOverwriteCheck));
                            if (!anyTargetMissingBuff && !State.OverwriteBuff) {
                                Main.Log($"[CSD] Phase1 skip '{buff.Name}' (mass — all wanted targets already buffed)");
                                skippedAlreadyActive++;
                                continue;
                            }
                        } else if (buff.BuffsApplied.IsPresent(forTarget, buff.IgnoreForOverwriteCheck) && !State.OverwriteBuff) {
                            string tName = Bubble.GroupById.TryGetValue(target, out var tu) ? tu.CharacterName : target;
                            Main.Log($"[CSD] Phase1 skip '{buff.Name}' on {tName} (already buffed)");
                            skippedAlreadyActive++;
                            continue;
                        }

                        var spellToCast = caster.spell;
                        if (spellToCast == null && caster.SourceType != BuffSourceType.Song) {
                            Main.Log($"[CSD] Phase1 skip '{buff.Name}' (spellToCast=null, sourceType={caster.SourceType})");
                            continue;
                        }

                        var task = new CastTask {
                            SpellToCast = spellToCast,
                            PowerfulChange = caster.SourceType == BuffSourceType.Spell && caster.PowerfulChange,
                            ShareTransmutation = caster.SourceType == BuffSourceType.Spell && caster.ShareTransmutation,
                            ReservoirCLBuff = caster.SourceType == BuffSourceType.Spell && caster.ReservoirCLBuff,
                            AzataZippyMagic = caster.SourceType == BuffSourceType.Spell && caster.AzataZippyMagic,
                            IsDuplicateSpellApplied = false,
                            SelfCastOnly = caster.SelfCastOnly,
                            SourceType = caster.SourceType,
                            SourceItem = caster.SourceItem,
                            OriginalMetamagicWasNull = spellToCast?.MetamagicData == null,
                            OriginalMetamagicMask = spellToCast?.MetamagicData?.MetamagicMask ?? (Metamagic)0,
                            OriginalSpellLevelCost = spellToCast?.MetamagicData?.SpellLevelCost ?? 0,
                            OriginalHeightenLevel = spellToCast?.MetamagicData?.HeightenLevel ?? 0,
                        };

                        if (buff.UseExtendRod && caster.SourceType == BuffSourceType.Spell) {
                            int spellLevel = caster.spell.Spellbook.GetSpellLevel(caster.spell);
                            var rod = BufferState.FindBestExtendRod(spellLevel, remainingRodCharges);
                            if (rod != null) {
                                task.MetamagicRodItem = rod;
                                remainingRodCharges[rod] = remainingRodCharges[rod] - 1;
                            }
                        }

                        task.Target = buff.IsMass
                            ? ResolveMassTarget(buff, spellToCast, caster.who, Bubble.GroupById[target])
                            : new TargetWrapper(Bubble.GroupById[target]);
                        task.Caster = caster.who;

                        tasks.Add(task);
                        actuallyCast++;
                        string targetName = task.Target?.Unit?.CharacterName ?? "<point>";
                        string rodTag = task.MetamagicRodItem != null ? " +ExtendRod" : "";
                        Main.Log($"[CSD] Phase1 queue '{buff.Name}' {caster.who.CharacterName} → {targetName} ({caster.SourceType}{rodTag})");
                    }
                } catch (Exception ex) {
                    Main.Error(ex, $"[CSD] Phase1 exception for '{buff.Name}'");
                }
            }

            // Combat log message (same pattern as Execute)
            var messageString = $"Combat Start: {"log.applied".i8()} {actuallyCast + activatablesActivated} ({"log.skipped".i8()} {skippedAlreadyActive})";
            Main.Log($"[CSD] Summary activatables={activatablesActivated} spell-tasks={tasks.Count} skipped-already-active={skippedAlreadyActive}");

            if (tasks.Count > 0) {
                IBuffExecutionEngine engine = State.SkipAnimationsOnCombatStart
                    ? (IBuffExecutionEngine)new InstantExecutionEngine()
                    : new AnimatedExecutionEngine();
                Main.Log($"[CSD] Engine={engine.GetType().Name} dispatching {tasks.Count} tasks");
                var castingCoroutine = engine.CreateSpellCastRoutine(tasks);
                BubbleBuffGlobalController.Instance.StartCoroutine(castingCoroutine);
            }

            if (actuallyCast + activatablesActivated > 0) {
                var message = new CombatLogMessage(messageString, Color.blue, PrefixIcon.RightArrow);
                var messageLog = LogThreadService.Instance.m_Logs[LogChannelType.Common].First(x => x is MessageLogThread);
                messageLog.AddMessage(message);
            }
        }
    }
    //castTask.Retentions.Any
    public class CastTask {
        public AbilityData SpellToCast;
        public AbilityData SlottedSpell;
        public bool PowerfulChange;
        public bool ShareTransmutation;
        public bool ReservoirCLBuff;
        public bool AzataZippyMagic;
        public bool IsDuplicateSpellApplied;
        public TargetWrapper Target;
        public UnitEntityData Caster;
        public bool SelfCastOnly;
        public BuffSourceType SourceType;
        public Kingmaker.Items.ItemEntity SourceItem;
        public Kingmaker.Items.ItemEntity MetamagicRodItem;
        // Original MetamagicData state, captured before any EngineCastingHandler
        // modifies the shared SpellToCast. Used by handlers to restore/clean state.
        public bool OriginalMetamagicWasNull;
        public Metamagic OriginalMetamagicMask;
        public int OriginalSpellLevelCost;
        public int OriginalHeightenLevel;

        // Set by EngineCastingHandler.OnBeforeEventAboutToTrigger when the matching
        // RuleCastSpell actually fires. Used to report a truthful applied/attempted
        // count after the casting coroutine completes — `actuallyCast` at queue time
        // is just an attempt counter and overstates results when commands get dropped
        // (e.g. animation-speed mods truncate UnitCommands before the rule triggers).
        public bool ActuallyFired;

        public Retentions Retentions {
            get {
                return new Retentions(this);
            }
        }
    }

    public class Retentions {
        private CastTask _castTask;

        public Retentions(CastTask castTask) {
            _castTask = castTask;
        }

        public bool ShareTransmutation {
            get {
                var casterHasAvailable = _castTask.Caster.HasFact(Resources.GetBlueprint<BlueprintFeature>("c4ed8d1a90c93754eacea361653a7d56"));
                var userSelectedForSpell = _castTask.ShareTransmutation;

                return casterHasAvailable && userSelectedForSpell;
            }
        }

        public bool ImprovedShareTransmutation {
            get {
                var casterHasAvailable = _castTask.Caster.HasFact(Resources.GetBlueprint<BlueprintFeature>("c94d764d2ce3cd14f892f7c00d9f3a70"));
                var userSelectedForSpell = _castTask.ShareTransmutation;

                return casterHasAvailable && userSelectedForSpell;
            }
        }

        public bool PowerfulChange {
            get {
                var casterHasAvailable = _castTask.Caster.HasFact(Resources.GetBlueprint<BlueprintFeature>("5e01e267021bffe4e99ebee3fdc872d1"));
                var userSelectedForSpell = _castTask.PowerfulChange;

                return casterHasAvailable && userSelectedForSpell;
            }
        }

        public bool ImprovedPowerfulChange {
            get {
                var casterHasAvailable = _castTask.Caster.HasFact(Resources.GetBlueprint<BlueprintFeature>("c94d764d2ce3cd14f892f7c00d9f3a70"));
                var userSelectedForSpell = _castTask.PowerfulChange;

                return casterHasAvailable && userSelectedForSpell;
            }
        }

        public bool Any {
            get {
                return ShareTransmutation || ImprovedShareTransmutation || PowerfulChange || ImprovedPowerfulChange;
            }
        }
    }
}
