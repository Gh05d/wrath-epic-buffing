using BuffIt2TheLimit.Handlers;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuffIt2TheLimit {
    public class AnimatedExecutionEngine : IBuffExecutionEngine {
        private UnitCommand Cast(CastTask task, List<EngineCastingHandler> handlers) {
            EngineCastingHandler handler = null;
            try {
                if (task.SourceType != BuffSourceType.Spell) {
                    // Equipment/scroll/potion: use instant cast.
                    // UnitUseAbility.CreateCastCommand rejects synthetic AbilityData
                    // that isn't in the caster's actual ability list.
                    handler = new EngineCastingHandler(task, true);
                    handlers.Add(handler);
                    EventBus.Subscribe(handler);
                    Rulebook.Trigger<RuleCastSpell>(new(task.SpellToCast, task.Target));
                    return null;
                }

                // Spells: use animated command (real AbilityData from spellbook).
                // A spent slot is refilled from a Pearl of Power first; without one the cast is skipped.
                if (!PearlsOfPower.EnsureSlotForCast(task)) return null;
                handler = new EngineCastingHandler(task);
                handlers.Add(handler);
                EventBus.Subscribe(handler);
                return UnitUseAbility.CreateCastCommand(task.SpellToCast, task.Target);
            }
            catch (Exception ex) {
                Main.Error(ex, "Animated Engine Casting");
                handler?.EnsureFinalized();
                return null;
            }
        }

        public IEnumerator CreateSpellCastRoutine(List<CastTask> tasks) {
            var handlers = new List<EngineCastingHandler>();
            try {
                var byCaster = tasks.GroupBy(task => task.Caster).Select(x => x.GetEnumerator()).ToList();
                UnitCommand[] running = new UnitCommand[byCaster.Count];

                int remaining = byCaster.Count;

                while (remaining > 0) {

                    for (int i = 0; i < byCaster.Count; i++) {
                        var current = running[i];
                        if (current != null) {
                            if (current.IsFinished) {
                                running[i] = null;
                            }
                            continue;
                        }


                        var queue = byCaster[i];
                        if (queue == null) {
                            continue;
                        }

                        if (!queue.MoveNext()) {
                            byCaster[i] = null;
                            remaining--;
                            continue;
                        }

                        current = Cast(queue.Current, handlers);
                        if (current != null) {
                            var caster = queue.Current.Caster;
                            caster.Commands.Run(current);
                            // Commands.Run can silently DISCARD the command without slotting
                            // it (unit not conscious, CC'd, TryMergeInto fold). A discarded
                            // command stays IsStarted=false/IsFinished=false forever — tracking
                            // it would spin this coroutine endlessly. Only track when the
                            // command actually resides in a slot; the routine-end sweep
                            // finalizes the orphaned handler.
                            if (caster.Commands.Raw.Any(c => ReferenceEquals(c, current))) {
                                running[i] = current;
                            } else {
                                Main.Log($"Cast command discarded by engine: {queue.Current.SpellToCast?.Name} ({caster.CharacterName})");
                            }
                        }
                        break;
                    }

                    yield return new WaitForFixedUpdate();

                }

                // Grace period: give in-flight AbilityExecutionProcesses a moment to end
                // and self-finalize via HandleExecutionProcessEnd before the sweep below
                // rolls back shared state.
                for (int i = 0; i < 30 && handlers.Any(h => !h.IsFinalized); i++) {
                    yield return null;
                }
            } finally {
                foreach (var handler in handlers) {
                    handler.EnsureFinalized();
                }
            }
        }
    }
}
