using UnityEngine;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniRx;
using BuffIt2TheLimit.Extensions;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic;
using Kingmaker.Blueprints.Classes;

namespace BuffIt2TheLimit {

    public class BufferState {
        private readonly Dictionary<BuffKey, BubbleBuff> BuffsByKey = new();
        private readonly HashSet<BlueprintFeature> Feats = new();
        //public List<Buff> buffList = new();
        public IEnumerable<BubbleBuff> BuffList;

        public bool Dirty = false;

        public Action OnRecalculated;

        public void RecalculateAvailableBuffs(List<UnitEntityData> Group) {
            Dirty = true;
            BuffsByKey.Clear();

            Main.Verbose("Recalculating full state");

            try {
                for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                    UnitEntityData dude = Group[characterIndex];
                    Main.Verbose($"Looking at dude: ${dude.CharacterName}", "state");
                    foreach (var book in dude.Spellbooks) {
                        Main.Verbose($"  Looking at spellbook: {book.Blueprint.DisplayName}", "state");
                        foreach (var spell in book.GetCustomSpells(0)) {
                            ReactiveProperty<int> credits = new ReactiveProperty<int>(500);
                            Main.Verbose($"      Adding cantrip (completely normal): {spell.Name}", "state");
                            AddBuff(dude: dude,
                                    book: book,
                                    spell: spell,
                                    baseSpell: null,
                                    credits: credits,
                                    newCredit: false,
                                    creditClamp: int.MaxValue,
                                    charIndex: characterIndex);
                        }

                        foreach (var spell in book.GetKnownSpells(0)) {
                            ReactiveProperty<int> credits = new ReactiveProperty<int>(500);
                            Main.Verbose($"      Adding cantrip: {spell.Name}", "state");
                            AddBuff(dude: dude,
                                    book: book,
                                    spell: spell,
                                    baseSpell: null,
                                    credits: credits,
                                    newCredit: false,
                                    creditClamp: int.MaxValue,
                                    charIndex: characterIndex);
                        }
                        
                        if (book.Blueprint.IsArcanist) {
                            for (int level = 1; level <= book.LastSpellbookLevel; level++) {
                                Main.Verbose($"    Looking at arcanist level {level}", "state");
                                ReactiveProperty<int> credits = new ReactiveProperty<int>(book.GetSpellsPerDay(level));
                                foreach (var slot in book.GetMemorizedSpells(level)) {
                                    Main.Verbose($"      Adding arcanist buff: {slot.Spell.Name}", "state");
                                    AddBuff(dude: dude,
                                            book: book,
                                            spell: slot.Spell,
                                            baseSpell: null,
                                            credits: credits,
                                            newCredit: false,
                                            creditClamp: int.MaxValue,
                                            charIndex: characterIndex);
                                }
                            }
                        } else if (book.Blueprint.Spontaneous) {
                            for (int level = 1; level <= book.LastSpellbookLevel; level++) {
                                Main.Verbose($"    Looking at spont level {level}", "state");
                                ReactiveProperty<int> credits = new ReactiveProperty<int>(book.GetSpellsPerDay(level));
                                foreach (var spell in book.GetKnownSpells(level)) {
                                    Main.Verbose($"      Adding spontaneous buff: {spell.Name}", "state");
                                    AddBuff(dude: dude,
                                            book: book,
                                            spell: spell,
                                            baseSpell: null,
                                            credits: credits,
                                            newCredit: false,
                                            creditClamp: int.MaxValue,
                                            charIndex: characterIndex);
                                }
                                foreach (var spell in book.GetCustomSpells(level)) {
                                    Main.Verbose($"      Adding spontaneous (customised) buff: {spell.Name}/{dude.CharacterName}", "state");
                                    AddBuff(dude: dude,
                                            book: book,
                                            spell: spell,
                                            baseSpell: null,
                                            credits: credits,
                                            newCredit: false,
                                            creditClamp: int.MaxValue,
                                            charIndex: characterIndex);
                                }
                            }
                        } else {
                            foreach (var slot in book.GetAllMemorizedSpells()) {
                                Main.Verbose($"      Adding prepared buff: {slot.Spell.Name}", "state");
                                AddBuff(dude: dude,
                                        book: book,
                                        spell: slot.Spell,
                                        baseSpell: null,
                                        credits: new ReactiveProperty<int>(1),
                                        newCredit: true,
                                        creditClamp: int.MaxValue,
                                        charIndex: characterIndex);
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "finding spells");
            }

            try {
                for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                    UnitEntityData dude = Group[characterIndex];
                    foreach (Ability ability in dude.Abilities.RawFacts) {
                        ItemEntity sourceItem = ability.SourceItem;
                        if (sourceItem != null) {
                        }
                        if (sourceItem == null || !sourceItem.IsSpendCharges) {
                            var credits = new ReactiveProperty<int>(500);
                            if (ability.Data.Resource != null) {
                                credits.Value = ability.Data.Resource.GetMaxAmount(dude);
                            }
                            AddBuff(dude: dude,
                                    book: null,
                                    spell: ability.Data,
                                    baseSpell: null,
                                    credits: credits,
                                    newCredit: true,
                                    creditClamp: int.MaxValue,
                                    charIndex: characterIndex,
                                    archmageArmor: false,
                                    category: Category.Ability);
                        } else if (SavedState.EquipmentEnabled
                                   && !(sourceItem.Blueprint is BlueprintItemEquipmentUsable)) {
                            // Equipped item abilities (staves, etc.) — not quickslot items
                            // which are handled by the equipment scan below
                            int charges = sourceItem.Charges;
                            if (charges <= 0) continue;
                            var credits = new ReactiveProperty<int>(charges);
                            Main.Verbose($"      Adding equipped item buff: {ability.Name} from {sourceItem.Name} for {dude.CharacterName}", "state");
                            AddBuff(dude: dude,
                                    book: null,
                                    spell: ability.Data,
                                    baseSpell: null,
                                    credits: credits,
                                    newCredit: true,
                                    creditClamp: int.MaxValue,
                                    charIndex: characterIndex,
                                    archmageArmor: false,
                                    category: Category.Equipment,
                                    sourceType: BuffSourceType.Equipment,
                                    sourceItem: sourceItem);
                        }
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "finding abilities:");
            }

            try {
                if (SavedState.ScrollsEnabled || SavedState.PotionsEnabled || SavedState.EquipmentEnabled) {
                    // Group usable items by blueprint to share credits across stacks
                    var usableItems = Game.Instance.Player.Inventory
                        .Where(item => item.Blueprint is BlueprintItemEquipmentUsable usable
                            && (usable.Type == UsableItemType.Scroll
                                || usable.Type == UsableItemType.Potion
                                || usable.Type == UsableItemType.Wand))
                        .GroupBy(item => item.Blueprint)
                        .ToList();

                    foreach (var itemGroup in usableItems) {
                        var blueprint = (BlueprintItemEquipmentUsable)itemGroup.Key;
                        var spellBlueprint = blueprint.Ability;
                        if (spellBlueprint == null) continue;

                        var isScroll = blueprint.Type == UsableItemType.Scroll;
                        var isPotion = blueprint.Type == UsableItemType.Potion;
                        var isWand = blueprint.Type == UsableItemType.Wand;

                        if (isScroll && !SavedState.ScrollsEnabled) continue;
                        if (isPotion && !SavedState.PotionsEnabled) continue;
                        if (isWand && !SavedState.EquipmentEnabled) continue;

                        var firstItem = itemGroup.First();

                        if (isPotion) {
                            int totalCount = itemGroup.Sum(item => item.Count);
                            var sharedCredits = new ReactiveProperty<int>(totalCount);
                            for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                                UnitEntityData dude = Group[characterIndex];
                                var abilityData = new AbilityData(spellBlueprint, dude);
                                Main.Verbose($"      Adding potion buff: {spellBlueprint.Name} for {dude.CharacterName}", "state");

                                AddBuff(dude: dude,
                                        book: null,
                                        spell: abilityData,
                                        baseSpell: null,
                                        credits: sharedCredits,
                                        newCredit: false,
                                        creditClamp: 1,
                                        charIndex: characterIndex,
                                        archmageArmor: false,
                                        category: Category.Buff,
                                        sourceType: BuffSourceType.Potion,
                                        sourceItem: firstItem);
                            }
                        } else if (isScroll) {
                            int totalCount = itemGroup.Sum(item => item.Count);
                            var sharedCredits = new ReactiveProperty<int>(totalCount);
                            int scrollDC = 20 + blueprint.CasterLevel;

                            for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                                UnitEntityData dude = Group[characterIndex];
                                if (!CanUseItemWithUmd(dude, spellBlueprint, scrollDC)) continue;

                                var abilityData = new AbilityData(spellBlueprint, dude);
                                Main.Verbose($"      Adding scroll buff: {spellBlueprint.Name} for {dude.CharacterName}", "state");

                                AddBuff(dude: dude,
                                        book: null,
                                        spell: abilityData,
                                        baseSpell: null,
                                        credits: sharedCredits,
                                        newCredit: false,
                                        creditClamp: int.MaxValue,
                                        charIndex: characterIndex,
                                        archmageArmor: false,
                                        category: Category.Buff,
                                        sourceType: BuffSourceType.Scroll,
                                        sourceItem: firstItem);
                            }
                        } else if (isWand) {
                            int totalCharges = itemGroup.Sum(item => item.Charges);
                            if (totalCharges <= 0) continue;
                            var wandCredits = new ReactiveProperty<int>(totalCharges);
                            int wandDC = 20 + blueprint.CasterLevel;

                            for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                                UnitEntityData dude = Group[characterIndex];
                                if (!CanUseItemWithUmd(dude, spellBlueprint, wandDC)) continue;

                                var abilityData = new AbilityData(spellBlueprint, dude);
                                Main.Verbose($"      Adding wand buff: {spellBlueprint.Name} from {blueprint.Name} for {dude.CharacterName}", "state");

                                AddBuff(dude: dude,
                                        book: null,
                                        spell: abilityData,
                                        baseSpell: null,
                                        credits: wandCredits,
                                        newCredit: false,
                                        creditClamp: int.MaxValue,
                                        charIndex: characterIndex,
                                        archmageArmor: false,
                                        category: Category.Equipment,
                                        sourceType: BuffSourceType.Equipment,
                                        sourceItem: firstItem);
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "finding scrolls/potions");
            }

            try {
                if (SavedState.EquipmentEnabled) {
                    // Scan quickslot items for activatable equipment buffs (wands, rods, etc.)
                    for (int characterIndex = 0; characterIndex < Group.Count; characterIndex++) {
                        UnitEntityData dude = Group[characterIndex];
                        foreach (var slot in dude.Body.QuickSlots) {
                            if (!slot.HasItem) continue;
                            if (!(slot.Item.Blueprint is BlueprintItemEquipmentUsable usableBp)) {
                                continue;
                            }

                            // Skip scrolls and potions - they're handled above
                            if (usableBp.Type == UsableItemType.Scroll || usableBp.Type == UsableItemType.Potion) continue;
                            if (usableBp.Type == UsableItemType.Wand) continue;

                            var spellBlueprint = usableBp.Ability;
                            if (spellBlueprint == null) continue;

                            var itemEntity = slot.Item;
                            int charges = itemEntity.Charges;
                            if (charges <= 0) continue;

                            var credits = new ReactiveProperty<int>(charges);
                            var abilityData = new AbilityData(spellBlueprint, dude);

                            Main.Verbose($"      Adding equipment buff: {spellBlueprint.Name} from {usableBp.Name} for {dude.CharacterName}", "state");

                            AddBuff(dude: dude,
                                    book: null,
                                    spell: abilityData,
                                    baseSpell: null,
                                    credits: credits,
                                    newCredit: true,
                                    creditClamp: int.MaxValue,
                                    charIndex: characterIndex,
                                    archmageArmor: false,
                                    category: Category.Equipment,
                                    sourceType: BuffSourceType.Equipment,
                                    sourceItem: itemEntity);
                        }
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "finding equipment buffs");
            }

            //foreach (var rejectKey in SpellsWithBeneficialBuffs.Where(kv => kv.Value.EmptyIfNull().Empty()).Select(kv => kv.Key)) {
            //    var name = SpellNames[rejectKey];
            //    Main.Verbose($"Rejected spell: {name}", "spell-rejection");
            //}

            var list = new List<BubbleBuff>(BuffsByKey.Values);
            //list.Sort((a, b) => {
            //    return a.Name.CompareTo(b.Name);
            //});
            //Main.Log("Sorting buffs");
            BuffList = list;

            foreach (var buff in BuffList) {
                if (SavedState.Buffs.TryGetValue(buff.Key, out var fromSave)) {
                    buff.InitialiseFromSave(fromSave);
                }
                buff.SortProviders();
            }



            lastGroup.Clear();
            lastGroup.AddRange(Group.Select(x => x.UniqueId));
            foreach (var u in Group)
                Bubble.GroupById[u.UniqueId] = u;
            InputDirty = false;


        }


        public SavedBufferState SavedState;

        public BufferState(SavedBufferState save) {
            this.SavedState = save;
        }

        internal void Recalculate(bool updateUi) {
            var group = Bubble.Group;
            if (InputDirty || GroupIsDirty(group)) {
                AbilityCache.Revalidate();
                RecalculateAvailableBuffs(group);
            }

            foreach (var gbuff in BuffList)
                gbuff.Invalidate();
            foreach (var gbuff in BuffList)
                gbuff.Validate();
            foreach (var gbuff in BuffList)
                gbuff.OnUpdate?.Invoke();

            if (updateUi) {
                OnRecalculated?.Invoke();
            }

            Save();
        }

        public bool GroupIsDirty(List<UnitEntityData> group) {
            if (lastGroup.Count != group.Count)
                return true;

            for (int i = 0; i < lastGroup.Count; i++) {
                if (lastGroup[i] != group[i].UniqueId)
                    return true;
            }

            return false;
        }

        public KeyCode GetShortcut(BuffGroup group) =>
            SavedState.ShortcutKeys.TryGetValue(group, out var kc) ? kc : KeyCode.None;

        public void SetShortcut(BuffGroup group, KeyCode key) {
            if (key == KeyCode.None)
                SavedState.ShortcutKeys.Remove(group);
            else
                SavedState.ShortcutKeys[group] = key;
            Save(true);
        }

        public void Save(bool shallow = false) {
            static void updateSavedBuff(BubbleBuff buff, SavedBuffState save) {
                save.Blacklisted = buff.HideBecause(HideReason.Blacklisted);
                save.InGroup = buff.InGroup;

                if (buff.IgnoreForOverwriteCheck.Count > 0) {
                    save.IgnoreForOverwriteCheck = buff.IgnoreForOverwriteCheck.Select(g => g.ToString()).ToArray();
                } else {
                    save.IgnoreForOverwriteCheck = null;
                }

                foreach (var u in Bubble.Group) {
                    if (buff.UnitWants(u)) {
                        save.Wanted.Add(u.UniqueId);
                    } else if (buff.UnitWantsRemoved(u)) {
                        save.Wanted.Remove(u.UniqueId);
                    }
                }
                foreach (var caster in buff.CasterQueue) {
                    if (!save.Casters.TryGetValue(caster.Key, out var state)) {
                        state = new SavedCasterState();
                        save.Casters[caster.Key] = state;
                    }
                    state.Banned = caster.Banned;
                    state.Cap = caster.CustomCap;
                    state.ShareTransmutation = caster.ShareTransmutation;
                    state.PowerfulChange = caster.PowerfulChange;
                    state.ReservoirCLBuff = caster.ReservoirCLBuff;
                    state.UseAzataZippyMagic = caster.AzataZippyMagic;
                }
                save.SourcePriorityOverride = buff.SourcePriorityOverride;
                save.UseSpells = buff.UseSpells;
                save.UseScrolls = buff.UseScrolls;
                save.UsePotions = buff.UsePotions;
                save.UseEquipment = buff.UseEquipment;
                save.UseExtendRod = buff.UseExtendRod;
            }


            if (!shallow) {
                foreach (var buff in BuffList) {
                    var key = buff.Key;
                    if (SavedState.Buffs.TryGetValue(key, out var save)) {
                        updateSavedBuff(buff, save);
                        if (save.Wanted.Empty() && save.IgnoreForOverwriteCheck.Empty() && !buff.HideBecause(HideReason.Blacklisted)) {
                            SavedState.Buffs.Remove(key);
                        }
                    } else if (buff.Requested > 0 || buff.IgnoreForOverwriteCheck.Count > 0 || buff.HideBecause(HideReason.Blacklisted)) {
                        save = new();
                        save.Wanted = new HashSet<string>();
                        updateSavedBuff(buff, save);
                        SavedState.Buffs[key] = save;
                    }
                }
            }

            SavedState.Version = 1;
            using var settingsWriter = File.CreateText(BubbleBuffSpellbookController.SettingsPath);
            JsonSerializer.CreateDefault().Serialize(settingsWriter, SavedState);
        }

        // Extend Rod blueprint GUIDs (Lesser → Normal → Greater, sorted by MaxSpellLevel)
        private static readonly (string guid, int maxSpellLevel)[] ExtendRodBlueprints = {
            ("1cf04842d5dbd0f49946b1af1022cd1a", 3),  // Lesser Extend Rod
            ("1b2a09528da9e9948aa9026037bada90", 6),  // Normal Extend Rod
            ("9bab0e37c72be78418516e57a5e78a99", 9),   // Greater Extend Rod
        };

        /// <summary>
        /// Find the weakest Extend Rod in party inventory that can affect a spell of the given level.
        /// Uses remainingCharges to track charges within a single Execute() pass.
        /// Returns the item, or null if no suitable rod is available.
        /// </summary>
        public static Kingmaker.Items.ItemEntity FindBestExtendRod(int spellLevel, Dictionary<Kingmaker.Items.ItemEntity, int> remainingCharges) {
            foreach (var (guid, maxSpellLevel) in ExtendRodBlueprints) {
                if (spellLevel > maxSpellLevel) continue;

                foreach (var item in Game.Instance.Player.Inventory) {
                    if (item.Blueprint.AssetGuidThreadSafe != guid) continue;

                    int charges;
                    if (remainingCharges.TryGetValue(item, out charges)) {
                        if (charges <= 0) continue;
                    } else {
                        charges = item.Charges;
                        if (charges <= 0) continue;
                        remainingCharges[item] = charges;
                    }

                    return item;
                }
            }
            return null;
        }

        private static Dictionary<Guid, AbilityCombinedEffects> SpellsWithBeneficialBuffs = new();
        private static Dictionary<Guid, string> SpellNames = new();
        private static Guid MageArmorGuid = Guid.Parse("9e1ad5d6f87d19e4d8883d63a6e35568");
        private static BlueprintFeature ArchmageArmorFeature => Resources.GetBlueprint<BlueprintFeature>("c3ef5076c0feb3c4f90c229714e62cd0");

        public bool AllowInCombat {
            get => SavedState.AllowInCombat;
            set {
                SavedState.AllowInCombat = value;
                Save(true);
            }
        }

        public bool OverwriteBuff {
            get => SavedState.OverwriteBuff;
            set {
                SavedState.OverwriteBuff = value;
                Save(true);
            }
        }
        public bool VerboseCasting {
            get => SavedState.VerboseCasting;
            set {
                SavedState.VerboseCasting = value;
                Save(true);
            }
        }

        //private static Dictionary<Guid, List<ContextActionApplyBuff>> CachedBuffEffects;

        public void AddBuff(UnitEntityData dude, Kingmaker.UnitLogic.Spellbook book, AbilityData spell, AbilityData baseSpell, IReactiveProperty<int> credits, bool newCredit, int creditClamp, int charIndex, bool archmageArmor = false, Category category = Category.Buff, BuffSourceType sourceType = BuffSourceType.Spell, ItemEntity sourceItem = null) {
            //if (spell.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Point)
            //    Main.Log($"Rejecting {spell.Name} due to being cast-at-point");

            //bool anyTargets = Bubble.Group.Any(t => spell.CanTarget(new TargetWrapper(t)));
            //if (!anyTargets) {
            //    return;
            //} 

            if (spell.Blueprint.AssetGuid.m_Guid == MageArmorGuid && !archmageArmor && dude.HasFact(ArchmageArmorFeature)) {
                Main.Verbose($"        Adding archmage armor", "state");
                AddBuff(dude, book, spell, null, credits, false, creditClamp, charIndex, true, category, BuffSourceType.Spell, null);
            }


            if (spell.Blueprint.HasVariants) {
                var variantsComponent = spell.Blueprint.AbilityVariants;
                Main.Verbose($"        Adding variants...", "state");

                //Only credit the first variant each time (they act like spontaneous and should share the same credit)
                bool addCredit = true;

                foreach (var variant in variantsComponent.Variants) {
                    AbilityData data= new AbilityData(spell, variant);
                    Main.Verbose($"          Variant: {variant.Name}", "state");

                    AddBuff(dude: dude,
                            book: book,
                            spell: data,
                            baseSpell: spell,
                            credits: credits,
                            newCredit: addCredit,
                            creditClamp: creditClamp,
                            charIndex: charIndex,
                            archmageArmor: archmageArmor,
                            category: category,
                            sourceType: sourceType,
                            sourceItem: sourceItem);

                    addCredit = false;
                }
                return;
            }

            int clamp = int.MaxValue;
            if (archmageArmor || spell.TargetAnchor == AbilityTargetAnchor.Owner) {
                clamp = 1;
            }

            var key = new BuffKey(spell, archmageArmor);
            if (BuffsByKey.TryGetValue(key, out var buff)) {
                buff.AddProvider(dude, book, spell, baseSpell, credits, newCredit, clamp, charIndex, sourceType, sourceItem);
            } else {
                if (!SpellsWithBeneficialBuffs.TryGetValue(spell.Blueprint.AssetGuid.m_Guid, out var abilityEffect)) {
                    var beneficial = spell.Blueprint.GetBeneficialBuffs();
                    abilityEffect = new AbilityCombinedEffects(beneficial);
                    SpellsWithBeneficialBuffs[spell.Blueprint.AssetGuid.m_Guid] = abilityEffect;
                    SpellNames[spell.Blueprint.AssetGuid.m_Guid] = spell.Name;
                }

                if (abilityEffect.Empty) {
                    Main.Verbose($"Rejecting {spell.Name} because it has no applied effects", "rejection");
                    return;
                }


                buff = new BubbleBuff(spell, archmageArmor) {
                    BuffsApplied = abilityEffect
                };

                buff.IsMass = spell.Blueprint.IsMass();

                buff.Category = category;

                buff.SetHidden(HideReason.Short, !abilityEffect.IsLong);

                if (dude != null) {
                    buff.AddProvider(dude, book, spell, baseSpell, credits, newCredit, clamp, charIndex, sourceType, sourceItem);
                }

                BuffsByKey[key] = buff;
            }
        }


        private bool CanUseItemWithUmd(UnitEntityData dude, BlueprintAbility spell, int dc) {
            bool onClassList = dude.Spellbooks.Any(book =>
                book.Blueprint.SpellList?.SpellsByLevel?.Any(level =>
                    level.Spells.Any(s => s == spell)) == true);
            if (onClassList) return true;

            if (SavedState.UmdMode == UmdMode.SafeOnly) return false;

            var umdBonus = dude.Stats.SkillUseMagicDevice.ModifiedValue;
            if (SavedState.UmdMode == UmdMode.AlwaysTry) return umdBonus > 0;
            return (umdBonus + 20) >= dc;
        }

        private List<string> lastGroup = new();
        internal bool InputDirty = true;

    }

}
