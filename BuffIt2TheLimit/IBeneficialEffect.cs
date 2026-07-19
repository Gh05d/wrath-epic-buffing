using BuffIt2TheLimit.Extensions;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities.Components.AreaEffects;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using System;
using System.Linq;
using System.Collections.Generic;
using Kingmaker.Enums;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;

namespace BuffIt2TheLimit {

    public class UnitBuffData {
        public readonly UnitEntityData Unit;
        public readonly HashSet<Guid> Buffs;

        public UnitBuffData(UnitEntityData u) {
            Unit = u;
            Buffs = new(u.Buffs.RawFacts.Select(b => b.BGuid()));
        }
    }

    public class AbilityCombinedEffects {

        private HashSet<Guid> AppliedBuffs;
        private HashSet<Guid> AppliedPetBuffs;
        private HashSet<Guid> PrimaryWeaponEnchants;
        private HashSet<Guid> SecondaryWeaponEnchants;
        private HashSet<EnchantPoolType> EnchantPools;
        // Applied buffs the ability re-grants only when absent (also checked via HasFact Not:true).
        // Unreliable presence markers — see AbilityCombinedEffects ctor and IsPresent.
        private HashSet<Guid> SelfGatedBuffs;
        public PetType? PetType = null;

        public IEnumerable<(string name, Guid guid)> All {
            get {
                IEnumerable<Guid> some = Enumerable.Empty<Guid>();
                if (AppliedBuffs != null)
                     some = some.Concat(AppliedBuffs);
                if (AppliedPetBuffs != null)
                     some = some.Concat(AppliedPetBuffs);
                if (PrimaryWeaponEnchants != null)
                     some = some.Concat(PrimaryWeaponEnchants);
                if (SecondaryWeaponEnchants != null)
                     some = some.Concat(SecondaryWeaponEnchants);

                return some.Select(x => (Resources.GetBlueprint<SimpleBlueprint>(new BlueprintGuid(x)).name, x));
            }
        }

        private void Add(ref HashSet<Guid> set, Guid fact) {
            if (set == null)
                set = new();
            set.Add(fact);
        }

        public void AddPetBuff(Guid buff, PetType type, bool isLong) {
            if (PetType != null && type != PetType) {
                Main.Error("Could not add pet buff with different pet type");
                return;
            }

            Add(ref AppliedPetBuffs, buff);
            PetType = type;
            IsLong |= isLong;
        }

        public void AddBuff(Guid buff, bool isLong) {
            Add(ref AppliedBuffs, buff);
            IsLong |= isLong;
        }
        public void AddPrimaryWeaponEnchant(Guid buff, bool isLong) {
            Add(ref PrimaryWeaponEnchants, buff);
            IsLong |= isLong;
        }
        public void AddEnchantPool(EnchantPoolType pool, bool isLong) {
            if (EnchantPools == null)
                EnchantPools = new();
            EnchantPools.Add(pool);
            IsLong |= isLong;
        }
        public void AddSecondaryWeaponEnchnant(Guid buff, bool isLong) {
            Add(ref SecondaryWeaponEnchants, buff);
            IsLong |= isLong;
        }

        public AbilityCombinedEffects(IEnumerable<IBeneficialEffect> beneficialEffects, HashSet<Guid> absenceCheckedFacts = null) {
            foreach (var effect in beneficialEffects.EmptyIfNull())
                effect.AppendTo(this);
            Empty = AppliedBuffs == null && AppliedPetBuffs == null && PrimaryWeaponEnchants == null && SecondaryWeaponEnchants == null && EnchantPools == null;

            // A buff the ability both APPLIES and guards on !HasFact(itself) is re-granted only
            // when absent (Armored Mask ↔ MageArmorBuff): having it doesn't prove this ability
            // ran, so it's not a reliable presence marker. IsPresent drops these (empty-fallback).
            if (absenceCheckedFacts != null && absenceCheckedFacts.Count > 0 && AppliedBuffs != null) {
                SelfGatedBuffs = AppliedBuffs.Where(absenceCheckedFacts.Contains).ToHashSet();
                if (SelfGatedBuffs.Count == 0)
                    SelfGatedBuffs = null;
            }
        }


        internal bool IsPresent(UnitBuffData unitBuffData, HashSet<Guid> ignoreForOverwriteCheck) {
            if (AppliedPetBuffs != null) {
                // GetPet returns null when the pet is dead, dismissed, or not summoned
                var pet = unitBuffData.Unit.GetPet(PetType.Value);
                if (pet != null) {
                    var existingBuffs = new HashSet<Guid>(pet.Buffs.RawFacts.Select(b => b.BGuid()));
                    if (existingBuffs.Overlaps(AppliedPetBuffs.Except(ignoreForOverwriteCheck)))
                        return true;
                }
            }

            if (AppliedBuffs != null) {
                IEnumerable<Guid> markers = AppliedBuffs.Except(ignoreForOverwriteCheck);
                if (SelfGatedBuffs != null) {
                    var narrowed = markers.Except(SelfGatedBuffs).ToHashSet();
                    // Empty-fallback: a pure "apply X if absent" buff (X is the only marker) keeps
                    // X so it isn't re-cast every run; multi-payload abilities (Armored Mask) narrow
                    // to their unique buff (the bonus applied when the shared buff is already present).
                    if (narrowed.Count > 0)
                        markers = narrowed;
                }
                if (unitBuffData.Buffs.Overlaps(markers))
                    return true;
            }

            // Enchant-pool abilities (Arcane Weapon, Sacred Weapon, Weapon Bond …) may apply
            // ONLY property enchants chosen via toggles (Flaming, Keen, …): the generic
            // DefaultEnchantments (+1..+5) slot is skipped entirely when the weapon's own
            // enhancement is already +5 or the whole pool is spent on properties — so the
            // weapon-enchant overlap below can never match. The game books every pool-applied
            // enchant fact in UnitPartEnchantPoolData; a booked fact still live on the item
            // means the ability's effect is active (unequip/expiry remove the fact).
            if (EnchantPools != null) {
                var poolData = unitBuffData.Unit.Get<UnitPartEnchantPoolData>();
                if (poolData != null) {
                    foreach (var desc in poolData.m_EnchantPoolDataDescriptions) {
                        if (!EnchantPools.Contains(desc.Pool))
                            continue;
                        var item = desc.ItemRef.Entity;
                        if (item == null)
                            continue;
                        foreach (var factId in desc.Enchantments) {
                            var live = item.Facts.FindById(factId);
                            if (live != null && !ignoreForOverwriteCheck.Contains(live.BGuid()))
                                return true;
                        }
                    }
                }
            }

            // Magic Weapon / Greater Magic Weapon's m_Enchantment list is the standard
            // Enhancement1..5 blueprints — those same GUIDs are permanently baked into
            // any magic weapon item. So an enchant overlap is unreliable when the spell
            // also applies a unit-side buff (which it always does for these spells).
            // Use the buff as the canonical marker; only fall back to weapon-enchant
            // checks when the spell tracks no buffs at all.
            bool hasBuffMarker = AppliedBuffs != null || AppliedPetBuffs != null;
            if (!hasBuffMarker) {
                if (PrimaryWeaponEnchants != null) {
                    var important = PrimaryWeaponEnchants.Except(ignoreForOverwriteCheck).ToHashSet();
                    foreach (var enchant in unitBuffData.Unit.Body.PrimaryHand.MaybeWeapon.Enchantments) {
                        if (important.Contains(enchant.BGuid()))
                            return true;
                    }
                }
                if (SecondaryWeaponEnchants != null) {
                    var important = SecondaryWeaponEnchants.Except(ignoreForOverwriteCheck).ToHashSet();
                    foreach (var enchant in unitBuffData.Unit.Body.SecondaryHand.MaybeWeapon.Enchantments) {
                        if (important.Contains(enchant.BGuid()))
                            return true;
                    }
                }
            }

            return false;
        }

        public readonly bool Empty = true;
        public bool IsLong { get; private set; }
    }

    public interface IBeneficialEffect {
        public void AppendTo(AbilityCombinedEffects effect);
        public PetType? PetType { get; set; }
    }
    public class AreaBuffEffect : IBeneficialEffect {

        public readonly Guid Applied;
        public readonly bool IsLong;
        public AreaBuffEffect(AbilityAreaEffectBuff action, bool isLong) {
            Applied = action.Buff.AssetGuid.m_Guid;
            IsLong = isLong;
        }

        public PetType? PetType { get; set; }

        public void AppendTo(AbilityCombinedEffects effect) {
            if (PetType != null)
                effect.AddPetBuff(Applied, PetType.Value, IsLong);
            else
                effect.AddBuff(Applied, IsLong);
        }
    }

    public class BuffEffect : IBeneficialEffect {

        public readonly Guid Applied = Guid.Empty;
        public readonly bool IsLong;
        public  BuffEffect(ContextActionApplyBuff action) {
            if (action.Buff == null) return;
            Applied = action.Buff.AssetGuid.m_Guid;
            IsLong = action.IsLong();
        }

        public BuffEffect(Guid applied) {
            Applied = applied;
            IsLong = true;
        }

        public PetType? PetType { get; set; }

        public void AppendTo(AbilityCombinedEffects effect) {
            if (Applied != Guid.Empty) {
            if (PetType != null)
                effect.AddPetBuff(Applied, PetType.Value, IsLong);
                else
                    effect.AddBuff(Applied, IsLong);
            }
        }
    }

    public class WornItemEnchantmentEffect : IBeneficialEffect {
        public readonly Guid Applied;
        public readonly bool PrimaryWeapon;
        public readonly bool SecondaryWeapon;
        public readonly bool IsLong;

        public PetType? PetType { get; set; }

        public WornItemEnchantmentEffect(ContextActionEnchantWornItem action) {
            Applied = action.Enchantment.AssetGuid.m_Guid;
            if (action.Slot == Kingmaker.UI.GenericSlot.EquipSlotBase.SlotType.PrimaryHand)
                PrimaryWeapon = true;
            if (action.Slot == Kingmaker.UI.GenericSlot.EquipSlotBase.SlotType.SecondaryHand)
                SecondaryWeapon = true;

            IsLong = action.IsLong();
        }
        public void AppendTo(AbilityCombinedEffects effect) {
            if (PrimaryWeapon)
                effect.AddPrimaryWeaponEnchant(Applied, IsLong);
            else if (SecondaryWeapon)
                effect.AddSecondaryWeaponEnchnant(Applied, IsLong);
        }
    }

    public class EnhanceWeaponEffect : IBeneficialEffect {
        public readonly HashSet<Guid> Enchantments;
        public readonly bool SecondaryHand;
        public readonly bool IsLong;

        public PetType? PetType { get; set; }

        public EnhanceWeaponEffect(EnhanceWeapon action) {
            Enchantments = new HashSet<Guid>(
                action.m_Enchantment
                    .Where(e => e?.Get() != null)
                    .Select(e => e.Get().AssetGuid.m_Guid)
            );
            SecondaryHand = action.UseSecondaryHand;
            IsLong = action.IsLong();
        }

        public void AppendTo(AbilityCombinedEffects effect) {
            foreach (var enchant in Enchantments) {
                if (SecondaryHand)
                    effect.AddSecondaryWeaponEnchnant(enchant, IsLong);
                else
                    effect.AddPrimaryWeaponEnchant(enchant, IsLong);
            }
        }
    }

    public class WeaponEnchantPoolEffect : IBeneficialEffect {
        public readonly HashSet<Guid> DefaultEnchantments;
        public readonly EnchantPoolType Pool;
        public readonly bool SecondaryHand;
        public readonly bool IsLong;

        public PetType? PetType { get; set; }

        public WeaponEnchantPoolEffect(ContextActionWeaponEnchantPool action) {
            DefaultEnchantments = new HashSet<Guid>(
                action.DefaultEnchantments
                    .Where(e => e != null)
                    .Select(e => e.AssetGuid.m_Guid)
            );
            Pool = action.EnchantPool;
            SecondaryHand = action.EnchantSecondaryHandInstead;
            IsLong = action.IsLong();
        }

        public void AppendTo(AbilityCombinedEffects effect) {
            effect.AddEnchantPool(Pool, IsLong);
            foreach (var enchant in DefaultEnchantments) {
                if (SecondaryHand)
                    effect.AddSecondaryWeaponEnchnant(enchant, IsLong);
                else
                    effect.AddPrimaryWeaponEnchant(enchant, IsLong);
            }
        }
    }

}
