using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using System;
using System.Collections.Generic;
using System.Linq;
using UniRx;
using Kingmaker.UnitLogic.Mechanics.Actions;
using BuffIt2TheLimit.Extensions;
using Newtonsoft.Json;
using Kingmaker.Blueprints;
using Kingmaker.Utility;
using static Kingmaker.Blueprints.BlueprintAbilityResource;
using UnityEngine;

namespace BuffIt2TheLimit {

    public struct BuffKey {
        [JsonProperty]
        public readonly Guid Guid;
        [JsonProperty]
        public readonly Metamagic MetamagicMask;
        [JsonProperty]
        public readonly bool Archmage;

        public BuffKey(AbilityData ability, bool archmage) {
            Guid = ability.Blueprint.AssetGuid.m_Guid;
            if (ability.IsMetamagicked())
                MetamagicMask = ability.MetamagicData.MetamagicMask;
            else
                MetamagicMask = 0;
            Archmage = archmage;
        }

        public BuffKey(BlueprintGuid blueprintGuid) {
            Guid = blueprintGuid.m_Guid;
            MetamagicMask = 0;
            Archmage = false;
        }

        public override bool Equals(object obj) {
            return obj is BuffKey key &&
                   Guid.Equals(key.Guid) &&
                   MetamagicMask == key.MetamagicMask &&
                   Archmage == key.Archmage;
        }

        public override int GetHashCode() {
            int hashCode = 1282151259;
            hashCode = hashCode * -1521134295 + Guid.GetHashCode();
            hashCode = hashCode * -1521134295 + MetamagicMask.GetHashCode();
            hashCode = hashCode * -1521134295 + Archmage.GetHashCode();
            return hashCode;
        }
    }
    public class BubbleBuff {
        public HashSet<BuffGroup> InGroups = new HashSet<BuffGroup> { BuffGroup.Long };
        public AbilityData Spell;
        HashSet<string> wanted = new();
        HashSet<string> notWanted = new();
        HashSet<string> given = new();
        public HashSet<Guid> IgnoreForOverwriteCheck = new();

        public bool IsMass;

        public readonly BuffKey Key;

        public HideReason HiddenBecause;

        public bool Hidden { get { return HiddenBecause != 0; } }

        public AbilityCombinedEffects BuffsApplied;

        public int Requested {
            get => wanted.Count;
        }
        public int Fulfilled {
            get => given.Count;
        }

        public int Available {
            get => CasterQueue.Sum(caster => caster.AvailableCredits);
        }
        public (int, int) AvailableAndSelfOnly {
            get {
                var normal = 0;
                var self = 0;
                foreach (var c in CasterQueue) {
                    if (c.clamp == 1)
                        self += c.AvailableCredits;
                    else
                        normal += c.AvailableCredits;
                }

                return (normal, self);
            }
        }

        private string metaMagicRendered = null;
        private string MetaMagicFlags {
            get {
                if (IsSong || Metamagics == null)
                    return "";
                if (metaMagicRendered == null) {
                    metaMagicRendered = "[";
                    foreach (Metamagic flag in Enum.GetValues(typeof(Metamagic))) {
                        if (Spell.MetamagicData.Has(flag))
                            metaMagicRendered += flag.Initial();
                    }
                    metaMagicRendered += "]";
                }
                return metaMagicRendered;

            }

        }


        public string Name => IsSong ? ActivatableSource.Blueprint.Name
            : Key.Archmage ? "Archmage Armor"
            : Spell.Name;
        public string NameMeta => IsSong ? Name : $"{Spell.Name} {MetaMagicFlags}";
        public Sprite Icon => IsSong ? ActivatableSource.Blueprint.Icon : Spell?.Blueprint?.Icon;


        public bool UnitWants(UnitEntityData unit) => wanted.Contains(unit.UniqueId);
        public bool UnitWantsRemoved(UnitEntityData unit) => notWanted.Contains(unit.UniqueId);
        public bool UnitGiven(UnitEntityData unit) => given.Contains(unit.UniqueId);

        public List<BuffProvider> CasterQueue = new();
        public List<(string, BuffProvider)> ActualCastQueue;

        public Metamagic[] Metamagics;

        public BubbleBuff(AbilityData spell, bool archmageArmor) {
            this.Spell = spell;
            this.NameLower = spell.Name.ToLower();
            this.Key = new BuffKey(spell, archmageArmor);

            if (Spell.IsMetamagicked()) {
                Metamagics = spell.GetMetamagicks().ToArray();
            }
        }

        public BubbleBuff(Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility activatable) {
            this.ActivatableSource = activatable;
            this.Spell = null;
            this.IsSong = true;
            var blueprint = activatable.Blueprint;
            this.NameLower = blueprint.Name.ToLower();
            this.Key = new BuffKey(blueprint.AssetGuid);
            this.Category = Category.Song;
            this.BuffsApplied = new AbilityCombinedEffects(Enumerable.Empty<IBeneficialEffect>());
        }

        public Action OnUpdate = null;
        internal String NameLower;
        internal Spellbook book;
        internal Category Category = Category.Buff;
        internal SavedBuffState SavedState;
        public int SourcePriorityOverride = -1; // -1 = use global
        public bool UseSpells = true;
        public bool UseScrolls = true;
        public bool UsePotions = true;
        public bool UseEquipment = true;
        public bool UseExtendRod;
        public bool IsSong;
        public Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility ActivatableSource;

        public void AddProvider(UnitEntityData provider, Spellbook book, AbilityData spell, AbilityData baseSpell, IReactiveProperty<int> credits, bool newCredit, int creditClamp, int u, BuffSourceType sourceType = BuffSourceType.Spell, Kingmaker.Items.ItemEntity sourceItem = null) {
            if (this.book == null) {
                this.book = book;
            }

            foreach (var buffer in CasterQueue) {
                if (buffer.who == provider && buffer.book?.Blueprint.AssetGuid == book?.Blueprint.AssetGuid
                    && buffer.SourceType == sourceType) {
                    if (!Key.Archmage && newCredit)
                        buffer.AddCredits(1);
                    return;
                }
            }

            var providerHandle = new BuffProvider(credits) {
                who = provider,
                spent = 0,
                clamp = creditClamp,
                book = book,
                spell = spell,
                baseSpell = baseSpell,
                CharacterIndex = u,
                ArchmageArmor = this.Key.Archmage,
                SourceType = sourceType,
                SourceItem = sourceItem
            };

            //providerHandle.InstallDebugListeners();
            CasterQueue.Add(providerHandle);
        }

        internal void SetUnitWants(UnitEntityData unit, bool v) {
            if (v) {
                wanted.Add(unit.UniqueId);
                notWanted.Remove(unit.UniqueId);
            } else {
                wanted.Remove(unit.UniqueId);
                notWanted.Add(unit.UniqueId);
            }
        }

        public void InitialiseFromSave(SavedBuffState state) {
            if (state.InGroups != null) {
                InGroups = new HashSet<BuffGroup>(state.InGroups);
            } else {
                InGroups = new HashSet<BuffGroup> { state.InGroup };
            }
            SourcePriorityOverride = state.SourcePriorityOverride;
            for (int i = 0; i < Bubble.Group.Count; i++) {
                UnitEntityData u = Bubble.Group[i];
                if (state.Wanted.Contains(u.UniqueId))
                    SetUnitWants(u, true);
            }
            if (state.IgnoreForOverwriteCheck != null) {
                IgnoreForOverwriteCheck = state.IgnoreForOverwriteCheck.Select(gstr => Guid.Parse(gstr)).ToHashSet();
            }
            UseSpells = state.UseSpells;
            UseScrolls = state.UseScrolls;
            UsePotions = state.UsePotions;
            UseEquipment = state.UseEquipment;
            UseExtendRod = state.UseExtendRod;
            SetHidden(HideReason.Blacklisted, state.Blacklisted);
            foreach (var caster in CasterQueue) {
                if (state.Casters.TryGetValue(caster.Key, out var casterState)) {
                    caster.Banned = casterState.Banned;
                    caster.CustomCap = casterState.Cap;
                    caster.ShareTransmutation = casterState.ShareTransmutation;
                    caster.PowerfulChange = casterState.PowerfulChange;
                    caster.ReservoirCLBuff = casterState.ReservoirCLBuff;
                    caster.AzataZippyMagic = casterState.UseAzataZippyMagic;
                }
            }
        }

        public void Invalidate() {
            foreach (var caster in CasterQueue) {
                if (caster == null) continue;

                caster.AddCredits(caster.spent);
                caster.spent = 0;
                caster.MaxCap = caster.AvailableCreditsNoCap;
            }
            given.Clear();

            if (ActualCastQueue != null)
                ActualCastQueue.Clear();

        }

        public bool CanTarget(UnitEntityData who) {
            foreach (var caster in CasterQueue) {
                if (caster.CanTarget(who.UniqueId))
                    return true;
            }
            return false;
        }

        private int CreditsNeeded(AbilityData spell) {
            if (spell.ConvertedFrom != null) {
                return CreditsNeeded(spell.ConvertedFrom);
            }

            if (spell.Spellbook == null)
                return 1;

            if (spell.Spellbook.Blueprint.Spontaneous) {
                return 1;
            }
            else {
                if (spell.SpellSlot?.LinkedSlots != null && (spell.SpellSlot?.IsOpposition ?? false)) {
                    return spell.SpellSlot.LinkedSlots.Count();
                }
                else {
                    return 1;
                }
            }
        }

        public void Validate() {
            if (IsSong) {
                ValidateSong();
                return;
            }
            if (IsMass) {
                ValidateMass();
                return;
            }
            foreach (var target in wanted) {

                for (int n = 0; n < CasterQueue.Count; n++) {
                    var caster = CasterQueue[n];

                    // Skip disabled source types
                    if (caster.SourceType == BuffSourceType.Spell && !UseSpells) continue;
                    if (caster.SourceType == BuffSourceType.Scroll && !UseScrolls) continue;
                    if (caster.SourceType == BuffSourceType.Potion && !UsePotions) continue;
                    if (caster.SourceType == BuffSourceType.Equipment && !UseEquipment) continue;

                    // Available Credit check incorporating Azata Zippy Magic
                    var numberOfSpellCastsByCaster = ActualCastQueue?.Where(x => x.Item2 == caster).Count() ?? 0;
                    var creditsNeeded = CreditsNeeded(caster.spell);
                    var hasAvailableCredits = caster.AvailableCredits >= creditsNeeded || (caster.AvailableCredits < creditsNeeded && caster.AvailableCredits >= 0 && caster.AzataZippyMagic && numberOfSpellCastsByCaster % 2 == 1);

                    if (hasAvailableCredits) {
                        // Skip providers whose underlying spell/ability is no longer available
                        // (e.g., prepared spell slot already cast). This allows fallback to
                        // alternative sources like potions or scrolls.
                        if (!caster.SlottedSpell.IsAvailable) continue;

                        //Main.Verbose($"checking if: {caster.who.CharacterName} => {Name} => {Bubble.Group[i].CharacterName}");
                        if (!caster.CanTarget(target)) continue;

                        //Main.Verbose($"casting: {caster.who.CharacterName} => {Name} => {Bubble.Group[i].CharacterName}");

                        // Azata Zippy Magic - only charge credits if not prime cast
                        if (!caster.AzataZippyMagic || (caster.AzataZippyMagic && numberOfSpellCastsByCaster % 2 == 0)) {
                            // Check for opposition school
                            caster.ChargeCredits(creditsNeeded);
                            caster.spent += creditsNeeded;
                        }
                        given.Add(target);

                        if (ActualCastQueue == null)
                            ActualCastQueue = new();
                        ActualCastQueue.Add((target, caster));
                        break;
                    }
                }
            }
        }

        private void ValidateMass() {
            if (wanted.Count == 0) return;

            // Azata Zippy Magic is disabled for IsMass spells in EngineCastingHandler,
            // so no Zippy credit adjustment needed here.

            // For mass/communal spells: find one caster, consume one credit, cast once.
            // All wanted targets are marked as given since the spell affects everyone.
            for (int n = 0; n < CasterQueue.Count; n++) {
                var caster = CasterQueue[n];

                // Skip disabled source types
                if (caster.SourceType == BuffSourceType.Spell && !UseSpells) continue;
                if (caster.SourceType == BuffSourceType.Scroll && !UseScrolls) continue;
                if (caster.SourceType == BuffSourceType.Potion && !UsePotions) continue;
                if (caster.SourceType == BuffSourceType.Equipment && !UseEquipment) continue;

                var creditsNeeded = CreditsNeeded(caster.spell);
                if (caster.AvailableCredits < creditsNeeded) continue;
                if (!caster.SlottedSpell.IsAvailable) continue;

                // Find a wanted target this caster can reach (game distributes to all allies)
                string validTarget = null;
                foreach (var t in wanted) {
                    if (caster.CanTarget(t)) {
                        validTarget = t;
                        break;
                    }
                }
                if (validTarget == null) continue;

                caster.ChargeCredits(creditsNeeded);
                caster.spent += creditsNeeded;

                if (ActualCastQueue == null)
                    ActualCastQueue = new();
                ActualCastQueue.Add((validTarget, caster));

                // Mark all wanted targets as given — the spell affects everyone
                foreach (var target in wanted)
                    given.Add(target);

                return;
            }
        }

        public void ValidateSong() {
            if (ActivatableSource == null) return;
            ActualCastQueue = new List<(string, BuffProvider)>();

            if (ActivatableSource.IsOn) {
                // Already active — mark all wanted as given
                foreach (var target in wanted) {
                    given.Add(target);
                }
                return;
            }

            if (!ActivatableSource.IsAvailable) {
                Main.Verbose($"Song {Name}: not available (resources or restrictions)");
                return;
            }

            if (CasterQueue.Count == 0) return;

            var caster = CasterQueue[0];
            // Mark all wanted targets as given (songs are party-wide)
            foreach (var target in wanted) {
                given.Add(target);
            }
            ActualCastQueue.Add((caster.who.UniqueId, caster));
        }

        internal void SetHidden(HideReason reason, bool set) {
            if (set)
                HiddenBecause |= reason;
            else
                HiddenBecause &= ~reason;
        }

        internal bool HideBecause(HideReason reason) {
            return (HiddenBecause & reason) != 0;
        }

        public static int[] GetSourceOrder(SourcePriority priority) {
            return priority switch {
                SourcePriority.SpellsScrollsPotions => new[] { 0, 1, 2, 3 },
                SourcePriority.SpellsPotionsScrolls => new[] { 0, 2, 1, 3 },
                SourcePriority.ScrollsSpellsPotions => new[] { 1, 0, 2, 3 },
                SourcePriority.ScrollsPotionsSpells => new[] { 2, 0, 1, 3 },
                SourcePriority.PotionsSpellsScrolls => new[] { 1, 2, 0, 3 },
                SourcePriority.PotionsScrollsSpells => new[] { 2, 1, 0, 3 },
                _ => new[] { 0, 1, 2, 3 }
            };
        }

        internal void SortProviders() {
            if (IsSong) return;
            var globalPriority = GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState?.GlobalSourcePriority
                ?? SourcePriority.SpellsScrollsPotions;
            var effectivePriority = SourcePriorityOverride >= 0
                ? (SourcePriority)SourcePriorityOverride
                : globalPriority;
            var sourceOrder = GetSourceOrder(effectivePriority);

            CasterQueue.Sort((a, b) => {
                int aSourceWeight = sourceOrder[(int)a.SourceType];
                int bSourceWeight = sourceOrder[(int)b.SourceType];
                if (aSourceWeight != bSourceWeight)
                    return aSourceWeight - bSourceWeight;

                if (a.Priority == b.Priority) {
                    int aScore = 0;
                    int bScore = 0;

                    if (!a.SelfCastOnly)
                        aScore += 10_000;
                    if (!b.SelfCastOnly)
                        bScore += 10_000;

                    return aScore - bScore;
                } else {
                    return a.Priority - b.Priority;
                }
            });
        }

        internal void ClearRemovals() {
            notWanted.Clear();
        }

        internal void AdjustCap(int casterIndex, int v) {
            var caster = CasterQueue[casterIndex];
            if (caster.CustomCap == -1) {
                if (v > 0)
                    Main.Error("Error: can't increase cap above max");
                caster.CustomCap = caster.MaxCap - 1;
            } else {
                caster.CustomCap += v;
                if (caster.CustomCap == caster.MaxCap)
                    caster.CustomCap = -1;
            }
        }
    }
    public class BuffProvider {
        public CasterKey Key => new() {
            Name = who.UniqueId,
            Spellbook = book?.Blueprint.AssetGuid.m_Guid ?? Guid.Empty,
            SourceType = SourceType
        };

        public bool ArchmageArmor = false;
        public bool ShareTransmutation;
        public bool PowerfulChange;
        public bool ReservoirCLBuff;
        public bool AzataZippyMagic;
        public UnitEntityData who;
        public AbilityData baseSpell;
        public Spellbook book;
        private IReactiveProperty<int> credits;
        public int spent;
        public int clamp;
        public AbilityData spell;
        public int CharacterIndex;
        public BuffSourceType SourceType = BuffSourceType.Spell;
        public Kingmaker.Items.ItemEntity SourceItem;

        public void InstallDebugListeners() {
            credits.Subscribe<int>(c => {
                Main.Verbose($"{spell.Name}/{who.CharacterName} => credits changed to: {c}");
            });
        }

        private int ClampValue => ShareTransmutation ? int.MaxValue : clamp;

        public int ClampCredits(int clamp, int value, int spent) {
            if (clamp < int.MaxValue)
                return clamp - spent;
            else
                return value;
        }

        public bool Banned = false;
        public int CustomCap = -1;
        private int ClampForCap => CustomCap == -1 ? int.MaxValue : CustomCap;
        internal int MaxCap;

        public BuffProvider(IReactiveProperty<int> credits) {
            this.credits = credits;
        }

        public int AvailableCredits {
            get {
                if (Banned)
                    return 0;
                return ClampCredits(Math.Min(ClampValue, ClampForCap), credits.Value, spent);
            }
        }
        public int AvailableCreditsNoCap => Banned ? 0 : ClampCredits(ClampValue, credits.Value, spent);


        public AbilityData SlottedSpell => baseSpell ?? spell;

        public int Priority {
            get {
                if (book == null)
                    return 0;

                if (book.Blueprint.Spontaneous) {
                    return 100 - book.CasterLevel;
                } else {
                    return 0 - book.CasterLevel;
                }
            }
        }

        public class ForceShareTransmutation : IDisposable {
            private BuffProvider unit;


            public ForceShareTransmutation(BuffProvider unit) {
                this.unit = unit;
                if (unit.ShareTransmutation)
                    unit.who.State.Features.ShareTransmutation.Retain();
            }

            public void Dispose() {
                if (unit.ShareTransmutation)
                    unit.who.State.Features.ShareTransmutation.Release();
            }
        }

        public bool SelfCastOnly =>
            SourceType == BuffSourceType.Song ||
            SourceType == BuffSourceType.Potion ||
            spell.TargetAnchor == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityTargetAnchor.Owner;

        public bool CanTarget(string targetId) {
            if (ArchmageArmor)
                return targetId == who.UniqueId;

            using (new ForceShareTransmutation(this)) {
                if (!spell.CanTarget(new TargetWrapper(Bubble.GroupById[targetId])))
                    return false;

                if (SelfCastOnly)
                    return targetId == who.UniqueId;
                return true;
            }
        }

        internal void AddCredits(int v) {
            credits.Value += v;
        }

        internal void ChargeCredits(int v) {
            credits.Value -= v;
        }

        public bool RequiresUmdCheck {
            get {
                if (SourceType != BuffSourceType.Scroll) return false;
                return !who.Spellbooks.Any(b =>
                    b.Blueprint.SpellList?.SpellsByLevel?.Any(level =>
                        level.Spells.Any(s => s == spell.Blueprint)) == true);
            }
        }

        public int ScrollDC {
            get {
                if (SourceItem?.Blueprint is Kingmaker.Blueprints.Items.Equipment.BlueprintItemEquipmentUsable usable)
                    return 20 + usable.CasterLevel;
                return 25;
            }
        }

        public bool TryUmdCheck() {
            if (!RequiresUmdCheck) return true;
            var umdBonus = who.Stats.SkillUseMagicDevice.ModifiedValue;
            var roll = UnityEngine.Random.Range(1, 21);
            var total = roll + umdBonus;
            Main.Verbose($"UMD Check: {who.CharacterName} rolled {roll} + {umdBonus} = {total} vs DC {ScrollDC}");
            return total >= ScrollDC;
        }
    }
}
