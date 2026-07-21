using BuffIt2TheLimit.Extensions;
using JetBrains.Annotations;
using Kingmaker;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Controllers.Rest;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuffIt2TheLimit.Handlers {
    public class EngineCastingHandler : IAbilityExecutionProcessHandler, IRulebookEventAboutToTriggerHook {
        #region Fields

        private readonly CastTask _castTask;
        private readonly bool _spendSpellSlot;
        private ModifiableValue.Modifier _casterLevelModifier;
        private bool _rodMetamagicApplied;
        private bool _retentionsApplied;
        private bool _srSuppressed;
        private bool _finalized;

        // SpellResistance is mutated on the SHARED blueprint. Several handlers can be
        // alive for the same blueprint at once (two casters buffing with the same spell,
        // Azata echo). A per-handler prior-value capture lets the second handler capture
        // the already-suppressed `false` and permanently stick SpellResistance=false.
        // Refcount per blueprint instead: the first suppressor records the original,
        // the last restorer writes it back.
        private static readonly Dictionary<BlueprintAbility, int> SrSuppressCount = new();
        private static readonly Dictionary<BlueprintAbility, bool> SrOriginal = new();

        #endregion

        #region Properties

        private int ArcaneReservoirPointsAvailable {
            get {
                return _castTask.Caster?.Resources?.PersistantResources?.FirstOrDefault(x => x.Blueprint.AssetGuidThreadSafe == "cac948cbbe79b55459459dd6a8fe44ce")?.Amount ?? 0;
            }
            set {
                var arcaneReserviorResource = _castTask.Caster?.Resources?.PersistantResources?.FirstOrDefault(x => x.Blueprint.AssetGuidThreadSafe == "cac948cbbe79b55459459dd6a8fe44ce");
                if (arcaneReserviorResource != null) arcaneReserviorResource.Amount = value;
            }
        }

        private int ArcaneReservoirPointsNeeded {
            get {
                var PowerfulChangeRssLogic = AbilityCache.CasterCache[_castTask.Caster.UniqueId]?.PowerfulChange?.GetComponent<AbilityResourceLogic>();
                var ShareTransmutationCost = PowerfulChangeRssLogic ? PowerfulChangeRssLogic.CalculateCost(_castTask.SpellToCast) : 1;
                var ShareTransmutationRssLogic = AbilityCache.CasterCache[_castTask.Caster.UniqueId]?.ShareTransmutation?.GetComponent<AbilityResourceLogic>();
                var PowerfulChangeCost = ShareTransmutationRssLogic ? ShareTransmutationRssLogic.CalculateCost(_castTask.SpellToCast) : 1;
                var ReservoirCLBuffRssLogic = AbilityCache.CasterCache[_castTask.Caster.UniqueId]?.ReservoirCLBuff?.GetComponent<AbilityResourceLogic>();
                var ReservoirCLBuffCost = ReservoirCLBuffRssLogic ? ReservoirCLBuffRssLogic.CalculateCost(_castTask.SpellToCast) : 1;
                var points = 0;
                if (_castTask.ShareTransmutation && _castTask.Caster != _castTask.Target.Unit) points += Math.Max(0, ShareTransmutationCost);
                if (_castTask.PowerfulChange) points += Math.Max(0, PowerfulChangeCost);
                if (_castTask.ReservoirCLBuff) points += Math.Max(0, ReservoirCLBuffCost);
                return points;
            }
        }

        private bool IsControllingAzataZippyMagicSecondaryCast {
            get {
                // Azata Zippy Magic checks
                var hasAzataZippyMagicFact = _castTask.Caster.HasFact(Resources.GetBlueprint<BlueprintFeature>("30b4200f897ba25419ba3a292aed4053"));
                var isSpellMass = _castTask.SpellToCast.Blueprint.IsMass();
                var canCastOnOthers = _castTask.ShareTransmutation || !_castTask.SelfCastOnly;

                return _castTask.AzataZippyMagic && hasAzataZippyMagicFact && !isSpellMass && canCastOnOthers;
            }
        }

        private bool IsAzataZippyMagicSecondaryCast {
            get {
                return IsControllingAzataZippyMagicSecondaryCast && _castTask.IsDuplicateSpellApplied;
            }
        }

        private AbilityExecutionContext Context { get; set; }

        #endregion

        #region Constructors

        public EngineCastingHandler(CastTask castTask, bool spendSpellSlot = false) {
            _castTask = castTask;
            _spendSpellSlot = spendSpellSlot;

            try {
                // Only apply class-specific features for spell sources
                if (_castTask.SourceType == BuffSourceType.Spell) {
                    SetAllRetentions();
                    ModifyCasterLevel();
                }

                RemoveSpellResistance();

                // Extend Rod metamagic is applied in OnBeforeEventAboutToTrigger, not here.
                // Applying here would leak to other CastTasks sharing the same SpellToCast.

                if (_castTask.SourceType == BuffSourceType.Spell && IsAzataZippyMagicSecondaryCast) {
                    IncreaseSpellSlotsAvailable(_castTask.SpellToCast, _castTask.SpellToCast.SpellSlotCost);
                    AddMaterialComponentsForSpell(_castTask.SpellToCast, _castTask.SpellToCast.SpellSlotCost);
                }
            } catch {
                // The constructor mutates shared state (retentions, BonusCasterLevel,
                // blueprint SpellResistance). A throw mid-way must not leak any of it.
                EnsureFinalized();
                throw;
            }
        }

        #endregion

        #region IAbilityExecutionProcessHandler

        /// <summary>
        /// Needed for interface, but not used
        /// </summary>
        /// <param name="context"></param>
        public void HandleExecutionProcessStart(AbilityExecutionContext context) { }

        /// <summary>
        /// Release retentions and remove the subscription
        /// </summary>
        /// <param name="context"></param>
        public void HandleExecutionProcessEnd(AbilityExecutionContext context) {
            if (Context != null && Context == context) {
                EnsureFinalized();
            }
        }

        public bool IsFinalized => _finalized;

        /// <summary>
        /// Idempotent rollback + resource accounting + unsubscribe. Called from
        /// HandleExecutionProcessEnd on the normal path; the execution engines sweep
        /// every handler still alive at routine end. That covers the paths where
        /// HandleExecutionProcessEnd never fires: the cast command was silently
        /// discarded by Commands.Run, the cast was cancelled before the rule fired,
        /// or the game never created an AbilityExecutionProcess for an instant
        /// Rulebook.Trigger cast. Without the sweep those paths leak the handler's
        /// shared-state mutations (retentions, BonusCasterLevel, blueprint
        /// SpellResistance) permanently.
        /// </summary>
        public void EnsureFinalized() {
            if (_finalized) return;
            _finalized = true;
            try {
                if (_castTask.SourceType == BuffSourceType.Spell) {
                    ReleaseAllRetentions();
                    RestoreCasterLevel();
                }
                ResetSpellResistance();

                // Consume resources for non-spell casts — only when the rule actually
                // fired, never for discarded/cancelled casts.
                if (_castTask.ActuallyFired && _castTask.SourceItem != null) {
                    try {
                        if (_castTask.SourceType == BuffSourceType.Scroll || _castTask.SourceType == BuffSourceType.Potion) {
                            Game.Instance.Player.Inventory.Remove(_castTask.SourceItem, 1);
                        } else if (_castTask.SourceType == BuffSourceType.Equipment && _castTask.SourceItem.IsSpendCharges) {
                            _castTask.SourceItem.Charges--;
                        }
                    } catch (Exception itemEx) {
                        Main.Error(itemEx, "Consuming item after cast");
                    }
                }

                // Restore Extend Rod metamagic state (charge already consumed in OnBefore)
                if (_rodMetamagicApplied) {
                    // Best-effort restore of MetamagicData. Null-safe because the first
                    // handler to fire may already have restored it when SpellToCast is shared.
                    try {
                        if (_castTask.OriginalMetamagicWasNull) {
                            _castTask.SpellToCast.MetamagicData = null;
                        } else if (_castTask.SpellToCast.MetamagicData != null) {
                            _castTask.SpellToCast.MetamagicData.Clear();
                            _castTask.SpellToCast.MetamagicData.Add(_castTask.OriginalMetamagicMask);
                            _castTask.SpellToCast.MetamagicData.SpellLevelCost = _castTask.OriginalSpellLevelCost;
                            _castTask.SpellToCast.MetamagicData.HeightenLevel = _castTask.OriginalHeightenLevel;
                        }
                        if (Context?.m_Params != null) {
                            Context.m_Params.Metamagic = _castTask.OriginalMetamagicMask;
                        }
                    } catch (Exception restoreEx) {
                        Main.Verbose($"Metamagic restore: {restoreEx.Message}");
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "Casting: finalizing cast handler");
            } finally {
                EventBus.Unsubscribe(this);
            }
        }

        #endregion

        #region IRulebookEventAboutToTriggerHook

        /// <summary>
        /// This event handler is very handy for watching all the rule book events around casting
        /// </summary>
        /// <param name="rule"></param>
        public void OnBeforeEventAboutToTrigger([NotNull] RulebookEvent rule) {
            if (rule is RuleCastSpell evt) {
                if (_castTask.SpellToCast == evt.Spell && _castTask.Target == evt.SpellTarget) {
                    try {
                        // Set proper context so retentions may be released
                        Context = evt.Context;

                        // RuleCastSpell.OnTrigger validates the target AFTER this hook and
                        // silently aborts the cast on failure ("Invalid target" in Player.log).
                        // Bail before spending slot/rod/reservoir so an aborted cast costs
                        // nothing and reports as not applied. Cancel explicitly rather than
                        // relying on the engine's own abort.
                        if (!evt.Spell.CanTarget(evt.SpellTarget)) {
                            Main.Log($"Cast aborted (invalid target): {_castTask.SpellToCast.Name} → {evt.SpellTarget?.Unit?.CharacterName ?? "<point>"}");
                            evt.CancelAbilityExecution();
                            return;
                        }

                        // Apply or clean Extend Rod metamagic for THIS specific cast.
                        // Must happen here (not constructor) because multiple CastTasks
                        // share the same SpellToCast when one caster targets multiple units.
                        if (_castTask.MetamagicRodItem != null) {
                            try {
                                if (_castTask.SpellToCast.MetamagicData == null) {
                                    _castTask.SpellToCast.MetamagicData = new MetamagicData();
                                }
                                _castTask.SpellToCast.MetamagicData.Add(Metamagic.Extend);
                                // The RuleCastSpell constructor clones AbilityParams into
                                // Context.m_Params BEFORE this handler fires. The game reads
                                // metamagic from the cloned params for duration calculation,
                                // so we must also set the flag there.
                                if (Context?.m_Params != null) {
                                    Context.m_Params.Metamagic |= Metamagic.Extend;
                                }
                                _rodMetamagicApplied = true;
                                // Consume rod charge at cast time (not in HandleExecutionProcessEnd).
                                // ProcessEnd is unreliable for instant casts — the game may not
                                // create an AbilityExecutionProcess for every Rulebook.Trigger call.
                                if (_castTask.MetamagicRodItem.IsSpendCharges) {
                                    _castTask.MetamagicRodItem.Charges--;
                                    Main.Verbose($"Extend Rod charge consumed: {_castTask.MetamagicRodItem.Name} ({_castTask.MetamagicRodItem.Charges} remaining)");
                                }
                            } catch (Exception ex) {
                                Main.Error(ex, "Applying Extend Rod metamagic");
                            }
                        } else {
                            // No rod for this cast — clean any leaked Extend from a prior handler's
                            // rod application on the same SpellToCast. Only touch SpellToCast.MetamagicData
                            // (shared state between CastTasks), NOT Context.m_Params. Context is per-rule
                            // instance and may contain metamagic added by other mods (e.g. Dragon's
                            // auto-extend feats) — overwriting it would break cross-mod compatibility.
                            try {
                                if (_castTask.OriginalMetamagicWasNull) {
                                    _castTask.SpellToCast.MetamagicData = null;
                                } else if (_castTask.SpellToCast.MetamagicData != null) {
                                    _castTask.SpellToCast.MetamagicData.Clear();
                                    _castTask.SpellToCast.MetamagicData.Add(_castTask.OriginalMetamagicMask);
                                    _castTask.SpellToCast.MetamagicData.SpellLevelCost = _castTask.OriginalSpellLevelCost;
                                    _castTask.SpellToCast.MetamagicData.HeightenLevel = _castTask.OriginalHeightenLevel;
                                }
                            } catch (Exception ex) {
                                Main.Verbose($"Extend leak cleanup: {ex.Message}");
                            }
                        }

                        // Check for needed arcanist reservoir points
                        // Don't spend points if this is an Azata Zippy Magic secondary cast
                        if (ArcaneReservoirPointsNeeded > 0 && !IsAzataZippyMagicSecondaryCast) {
                            if (ArcaneReservoirPointsAvailable >= ArcaneReservoirPointsNeeded) {
                                DecreaseArcanePoolPoints(ArcaneReservoirPointsNeeded);
                            } 
                            else {
                                // Not enough points are available, so cancel the cast
                                Main.Error($"Unable to cast {_castTask.SpellToCast.Name} for {_castTask.Target.Unit.CharacterName} because {ArcaneReservoirPointsNeeded} arcane reservoir points are needed but only {ArcaneReservoirPointsAvailable} arcane reservoir points are available");
                                evt.CancelAbilityExecution();
                                return;
                            }
                        }

                        // Disable the logs for this cast
                        evt.Context.DisableLog = true;
                        evt.DisableBattleLogSelf = true;

                        // Always set to true if controlling Azata Zippy Magic Secondary casts
                        // This prevents the game's secondary cast from triggering, and allows us to control casting
                        if (_castTask.SourceType == BuffSourceType.Spell && IsControllingAzataZippyMagicSecondaryCast) {
                            evt.IsDuplicateSpellApplied = true;
                        }

                        // Spend spell slots if requested (e.g. cast directly from a rule trigger)
                        if (_spendSpellSlot && _castTask.SourceType == BuffSourceType.Spell) {
                            _castTask.SpellToCast.Spend();
                        }

                        // All cancellation paths above have returned. Once we reach this
                        // point the rule will execute — record the actual fire so the
                        // post-coroutine reporter can output a truthful applied count.
                        _castTask.ActuallyFired = true;
                    }
                    catch (Exception ex) {
                        Main.Error(ex, "Casting: OnBeforeRulebookEventTrigger");
                    }
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Set spell modifier retentions
        /// </summary>
        private void SetAllRetentions() {
            if (_castTask.Retentions.ShareTransmutation) _castTask.Caster.State.Features.ShareTransmutation.Retain();
            if (_castTask.Retentions.ImprovedShareTransmutation) _castTask.Caster.State.Features.ImprovedShareTransmutation.Retain();
            if (_castTask.Retentions.PowerfulChange) _castTask.Caster.State.Features.PowerfulChange.Retain();
            if (_castTask.Retentions.ImprovedPowerfulChange) _castTask.Caster.State.Features.ImprovedPowerfulChange.Retain();
            _retentionsApplied = true;
        }

        /// <summary>
        /// Release spell modifier retentions
        /// </summary>
        public void ReleaseAllRetentions() {
            if (!_retentionsApplied) return;
            _retentionsApplied = false;
            if (_castTask.Retentions.ShareTransmutation) _castTask.Caster.State.Features.ShareTransmutation.Release();
            if (_castTask.Retentions.ImprovedShareTransmutation) _castTask.Caster.State.Features.ImprovedShareTransmutation.Release();
            if (_castTask.Retentions.PowerfulChange) _castTask.Caster.State.Features.PowerfulChange.Release();
            if (_castTask.Retentions.ImprovedPowerfulChange) _castTask.Caster.State.Features.ImprovedPowerfulChange.Release();
        }

        private void RemoveSpellResistance() {
            var blueprint = _castTask.SpellToCast.Blueprint;
            if (!SrSuppressCount.TryGetValue(blueprint, out int count) || count == 0) {
                SrOriginal[blueprint] = blueprint.SpellResistance;
                count = 0;
            }
            SrSuppressCount[blueprint] = count + 1;
            blueprint.SpellResistance = false;
            _srSuppressed = true;
        }

        private void ResetSpellResistance() {
            if (!_srSuppressed) return;
            _srSuppressed = false;
            var blueprint = _castTask.SpellToCast.Blueprint;
            if (SrSuppressCount.TryGetValue(blueprint, out int count) && count > 1) {
                SrSuppressCount[blueprint] = count - 1;
            } else {
                SrSuppressCount.Remove(blueprint);
                if (SrOriginal.TryGetValue(blueprint, out bool original)) blueprint.SpellResistance = original;
                SrOriginal.Remove(blueprint);
            }
        }

        /// <summary>
        /// Decrease arcane pool points
        /// </summary>
        /// <param name="amount"></param>
        private void DecreaseArcanePoolPoints(int amount) => ArcaneReservoirPointsAvailable -= amount;

        /// <summary>
        /// Get the Spell Level (in a given spell book) of the spell specified
        /// </summary>
        /// <param name="spell"></param>
        /// <returns></returns>
        private int SpellLevel(AbilityData spell) {
            // Check if this is a converted spell.  A good test example is Magic Weapon, Primary
            if (spell.ConvertedFrom != null) {
                return SpellLevel(spell.ConvertedFrom);
            }

            // Callers only reach this for spellbook-backed casts; ConvertedFrom recursion
            // above already dereferenced `spell`, so a null check here would be dead code.
            return spell.Spellbook.GetSpellLevel(spell);
        }

        /// <summary>
        /// Increase the number of casting slots
        /// Used to add spell slots back when controlling Azata Zippy magic secondary buff
        /// </summary>
        /// <param name="spell"></param>
        /// <param name="amount"></param>
        private void IncreaseSpellSlotsAvailable(AbilityData spell, int amount) {
            if (_castTask.SourceType != BuffSourceType.Spell) return;
            // Check if this is a converted spell.  A good test example is Magic Weapon, Primary
            if (spell.ConvertedFrom != null) {
                IncreaseSpellSlotsAvailable(spell.ConvertedFrom, amount);
                return;
            }

            // Get the spell level
            var spellLevel = SpellLevel(spell);

            if (spell.Spellbook.Blueprint.Spontaneous) {
                // Increase the number of spontaneous spell slots
                spell.Spellbook.m_SpontaneousSlots[spellLevel] += amount;
            } else {
                // Find spent slots we can reactivate
                var spentSpellSlots = spell.Spellbook?.SureMemorizedSpells(spellLevel)?.Where(x => !x.Available && x.Spell == spell)?.Take(amount);

                // Make sure we found enough spell slots
                if (spentSpellSlots == null || spentSpellSlots.Count() != amount) {
                    return;
                }

                // Iterate through each spell slot reactivating each
                spentSpellSlots.ForEach(x => {
                    // Reactivate the spell slot
                    x.Available = true;

                    // Check for linked spell slots that need to also be reactivated
                    if (x.LinkedSlots != null && x.IsOpposition) {
                        x.LinkedSlots.ToList().ForEach(x => x.Available = true);
                    }
                });
            }
        }

        private void AddMaterialComponentsForSpell(AbilityData spell, int amount) {
            // Check if this is a converted spell.  A good test example is Magic Weapon, Primary
            if (spell.ConvertedFrom != null) {
                AddMaterialComponentsForSpell(spell.ConvertedFrom, amount);
                return;
            }

            // Get the material cost
            if (spell.Blueprint.MaterialComponent != null && spell.Blueprint.MaterialComponent.Item != null) {
                // Get the cost
                var item = spell.Blueprint.MaterialComponent.Item;
                var itemCost = spell.Blueprint.MaterialComponent.Count;

                // Add the cost to the inventory
                if (itemCost > 0) Game.Instance.Player.Inventory.Add(item, itemCost * amount);
            }
        }

        /// <summary>
        /// Change caster level based on modifiers
        /// </summary>
        private void ModifyCasterLevel() {
            var bonus = 0;
            // caster level from arcanist reservoir CL buff ability
            if (_castTask.ReservoirCLBuff) {
                var potent = _castTask.Caster.HasFact(Resources.GetBlueprint<BlueprintFeature>("995110cc948d5164a820403a9e903151"));
                bonus += potent ? 2 : 1;
            }
            _casterLevelModifier = new() {
                ModValue = bonus,
                ModDescriptor = Kingmaker.Enums.ModifierDescriptor.None,
                StackMode = ModifiableValue.StackMode.ForceStack
            };
            _castTask.Caster.Stats.BonusCasterLevel.AddModifier(_casterLevelModifier);
        }

        /// <summary>
        /// Restore caster level to original state
        /// </summary>
        private void RestoreCasterLevel() {
            if (_casterLevelModifier == null) return;
            _castTask.Caster.Stats.BonusCasterLevel.RemoveModifier(_casterLevelModifier);
            _casterLevelModifier = null;
        }

        #endregion
    }
}