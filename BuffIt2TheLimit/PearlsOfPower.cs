using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Controllers.Rest;
using Kingmaker.PubSubSystem;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BuffIt2TheLimit {

    /// <summary>
    /// Pearls of Power (DLC5, levels I-IX, "double" pearls with 2 uses for I-VI) carry an ability with
    /// AbilityRestoreSpellSlot: it refills ONE spent slot of the pearl's level or lower (spontaneous books:
    /// one slot of that level while below spells-per-day; prepared books: one spent slot holding the chosen
    /// spell). The game offers it through the spellbook conversions of a pearl sitting in a quick slot
    /// (AbilityData.GetConversions). The routine uses two kinds of pearls:
    ///   - pearls in the shared inventory, the way ItemEntity.TryUseFromInventory uses any other item —
    ///     which the game allows out of combat only;
    ///   - with the buff's "Allow equipped pearls' usage" box, also pearls in the CASTER'S OWN quick slots,
    ///     through the Ability fact the game gives the wielder for an equipped item (ItemEntity.Ability) —
    ///     the game's quick-slot use, so in combat too. Inventory pearls go first, quick-slot pearls are the
    ///     fallback. Nobody uses a pearl from another character's quick slot.
    /// Both get the spellbook parameters the way GetConversions sets them.
    ///
    /// Order of use ("cast, refill, cast"): a pearl is only ever used for a cast that finds its slot spent,
    /// right before that cast (EnsureSlotForCast). A pearl never tops up a slot that is still free.
    /// </summary>
    internal static class PearlsOfPower {

        internal static SavedBufferState Settings => GlobalBubbleBuffer.Instance?.SpellbookController?.state?.SavedState;

        internal static bool TryGetRestore(ItemEntity item, out AbilityRestoreSpellSlot restore) {
            restore = (item?.Blueprint as BlueprintItemEquipmentUsable)?.Ability?.GetComponent<AbilityRestoreSpellSlot>();
            return restore != null;
        }

        // Sort key: an any-level pearl (none in the base game) fits every slot, so it goes last.
        internal static int PearlLevel(AbilityRestoreSpellSlot restore) => restore.AnySpellLevel ? 10 : restore.SpellLevel;

        // The game's own level rule (IsAbilityRestrictionPassed): a pearl refills a slot of its level or LOWER.
        // Which of the compatible pearls a spell may actually use is the player's pick (PearlPermit).
        internal static bool Compatible(AbilityRestoreSpellSlot restore, int slotLevel) =>
            restore.AnySpellLevel || restore.SpellLevel >= slotLevel;

        // A pearl kind = its item blueprint; the per-spell picks are stored by this id.
        internal static string KindId(ItemEntity item) => item.Blueprint.AssetGuidThreadSafe;

        // Mirrors AbilityData.GetConversions, which offers a restore only for non-alchemist books, levels above 0,
        // and books the pearl accepts.
        internal static bool BookAccepts(AbilityRestoreSpellSlot restore, Spellbook book, int slotLevel) =>
            book != null && !book.Blueprint.IsAlchemist && slotLevel > 0 && restore.CanCreateRestoreSpell(book);

        private static List<(ItemEntity item, AbilityRestoreSpellSlot restore)> Sorted(List<(ItemEntity, AbilityRestoreSpellSlot)> list) =>
            list.OrderBy(p => PearlLevel(p.Item2))
                .ThenBy(p => p.Item1.Charges)
                .ToList();

        /// <summary>
        /// Unequipped pearls with a charge left that the game would let a character use from the inventory
        /// right now (IsUsableFromInventory: false in combat). Lowest level first, part-used stacks first.
        /// </summary>
        internal static List<(ItemEntity item, AbilityRestoreSpellSlot restore)> InventoryPearls() {
            var list = new List<(ItemEntity, AbilityRestoreSpellSlot)>();
            foreach (var item in Game.Instance.Player.Inventory) {
                if (item == null || item.HoldingSlot != null) continue;   // equipped: see EquippedPearls
                if (!item.IsIdentified || item.Charges <= 0) continue;
                if (!TryGetRestore(item, out var restore)) continue;
                if (!item.IsUsableFromInventory) continue;
                list.Add((item, restore));
            }
            return Sorted(list);
        }

        /// <summary>
        /// Pearls with a charge left in this character's own quick slots, each with the Ability fact the game
        /// granted the wielder on equipping it. Lowest level first, part-used stacks first.
        /// </summary>
        internal static List<(ItemEntity item, AbilityRestoreSpellSlot restore)> EquippedPearls(UnitEntityData unit) {
            var list = new List<(ItemEntity, AbilityRestoreSpellSlot)>();
            var slots = unit?.Body?.QuickSlots;
            if (slots == null) return list;
            foreach (var slot in slots) {
                if (slot == null || !slot.HasItem) continue;
                var item = slot.Item;
                if (!item.IsIdentified || item.Charges <= 0 || item.Ability == null) continue;
                if (!TryGetRestore(item, out var restore)) continue;
                list.Add((item, restore));
            }
            return Sorted(list);
        }

        internal static int SlotLevel(AbilityData slotted) => slotted.Spellbook.GetSpellLevel(slotted);

        /// <summary>
        /// Slots one cast consumes: SpendFromSpellbook spends SpellSlotCost charges; on a prepared book one
        /// charge spends the slot and every slot linked to it (opposition school = 2 linked slots).
        /// </summary>
        internal static int SlotsPerCast(AbilityData slotted) {
            int cost = Math.Max(1, slotted.SpellSlotCost);
            if (slotted.Spellbook.Blueprint.Spontaneous) return cost;
            return cost * Math.Max(1, BubbleBuff.CreditsNeeded(slotted));
        }

        // Prepared books: the memorized slots holding this spell — the same AbilityData equality Spellbook.Spend
        // and AbilityRestoreSpellSlot use. Spontaneous books (arcanist too) cast from a shared per-level pool.
        internal static IEnumerable<SpellSlot> MatchingSlots(AbilityData slotted, int level) =>
            slotted.Spellbook.GetMemorizedSpellSlots(level).Where(s => s != null && s.SpellShell == slotted);

        internal static int FreeSlots(AbilityData slotted, int level) {
            var book = slotted.Spellbook;
            if (book.Blueprint.Spontaneous) return book.GetSpontaneousSlots(level);
            return MatchingSlots(slotted, level).Count(s => s.Available);
        }

        // How many slots a pearl could ever give back for this spell: spontaneous restores stop at spells per
        // day; prepared restores need a spent slot that holds the spell.
        internal static int RestorableCapacity(AbilityData slotted, int level) {
            var book = slotted.Spellbook;
            if (book.Blueprint.Spontaneous) return book.GetSpellsPerDay(level);
            return MatchingSlots(slotted, level).Count();
        }

        /// <summary>
        /// Would the spell be castable once its missing slots were refilled? Asks the game's own
        /// AbilityData.IsAvailable (caster conscious, not silenced or polymorphed, material components ...)
        /// on the real spellbook with the missing slots briefly marked free, then puts them back. Nothing else
        /// runs in between, so no pearl is ever spent on a spell that would still fail for another reason.
        /// </summary>
        internal static bool CastableWithRefill(AbilityData slotted, int level, int perCast) {
            var book = slotted.Spellbook;
            int missing = perCast - FreeSlots(slotted, level);
            if (missing <= 0) return slotted.IsAvailable;

            if (book.Blueprint.Spontaneous) {
                book.m_SpontaneousSlots[level] += missing;
                try {
                    return slotted.IsAvailable;
                } finally {
                    book.m_SpontaneousSlots[level] -= missing;
                }
            }

            var flipped = MatchingSlots(slotted, level).Where(s => !s.Available).Take(missing).ToList();
            if (flipped.Count < missing) return false;
            try {
                foreach (var s in flipped) s.Available = true;
                return slotted.IsAvailable;
            } finally {
                foreach (var s in flipped) s.Available = false;
            }
        }

        /// <summary>
        /// Called by both execution engines right before a spell cast. Returns false when the cast must not
        /// happen: its slot is gone and no pearl could refill it. Without this check the instant engine fired
        /// RuleCastSpell anyway — Spellbook.Spend just returned false — so a planned cast beyond the remaining
        /// slots was free.
        /// </summary>
        internal static bool EnsureSlotForCast(CastTask task) {
            try {
                // Buffs not in pearl mode cast exactly as the original mod did.
                if (!task.UsePearls) return true;
                if (task.SourceType != BuffSourceType.Spell) return true;
                // Azata Zippy Magic echo: the cast handler hands its own slot back before spending it —
                // but an echo of a cast that was dropped here would be a free cast of its own.
                if (task.IsDuplicateSpellApplied) {
                    if (task.EchoOf?.SkippedNoSlot != true) return true;
                    task.SkippedNoSlot = true;
                    return false;
                }
                var slotted = task.SlottedSpell;
                var book = slotted?.Spellbook;
                if (book == null) return true;               // class ability, not a spellbook spell
                if (slotted.IsAvailable) return true;

                int level = SlotLevel(slotted);
                if (level >= 1 && task.Pearls != null && TryRefill(task.Caster, slotted, level, task.Pearls, out int used)) {
                    task.PearlsUsed += used;
                    if (slotted.IsAvailable) return true;
                }
                Main.Log($"[Pearls] cast skipped, no spell slot left: {slotted.Name} ({task.Caster?.CharacterName})");
                task.SkippedNoSlot = true;
                return false;
            } catch (Exception ex) {
                Main.Error(ex, $"[Pearls] preparing slot for {task.SpellToCast?.Name}");
                return true;   // fall back to the old behaviour rather than drop the cast
            }
        }

        private static bool TryRefill(UnitEntityData caster, AbilityData slotted, int level, PearlPermit permit, out int used) {
            used = 0;
            var settings = Settings;
            if (settings == null || !settings.PearlsEnabled) return false;

            var book = slotted.Spellbook;
            int perCast = SlotsPerCast(slotted);
            int missing = perCast - FreeSlots(slotted, level);
            if (missing <= 0) return false;
            if (RestorableCapacity(slotted, level) < perCast) return false;
            if (!CastableWithRefill(slotted, level, perCast)) return false;

            // Take the pearls first and use none unless all of them are there: half a refill (one slot of an
            // opposition pair) would burn a pearl and still not allow the cast. Only the kinds the player picked
            // for this spell (green panes); inventory first, the caster's own quick slots only when allowed.
            var inventoryPearls = ItemEntity.IsSuitableUnitForUseAbility(caster)
                ? InventoryPearls()
                : new List<(ItemEntity item, AbilityRestoreSpellSlot restore)>();
            var quickSlotPearls = permit.AllowEquipped
                ? EquippedPearls(caster)
                : new List<(ItemEntity item, AbilityRestoreSpellSlot restore)>();
            var picks = new List<(ItemEntity item, bool equipped)>();
            var usesLeft = new Dictionary<ItemEntity, int>();
            bool Usable((ItemEntity item, AbilityRestoreSpellSlot restore) p, bool equipped) =>
                BookAccepts(p.restore, book, level) && Compatible(p.restore, level) && permit.Allows(KindId(p.item), equipped);
            bool Take(ItemEntity item, bool equipped) {
                int left = usesLeft.TryGetValue(item, out var n) ? n : item.Count * item.Charges;
                if (left <= 0) return false;
                usesLeft[item] = left - 1;
                picks.Add((item, equipped));
                return true;
            }
            // 1. The pearls the plan set aside for this very cast (SpellSlotLedger gives every pearl to a spell of
            //    its own level first), so casting in buff order cannot hand them to an earlier, lower spell.
            foreach (var (kind, equipped) in permit.Planned) {
                if (picks.Count == missing) break;
                var source = equipped ? quickSlotPearls : inventoryPearls;
                foreach (var p in source) {
                    if (KindId(p.item) == kind && Usable(p, equipped) && Take(p.item, equipped)) break;
                }
            }
            // 2. Anything else picked and fitting, lowest level first
            void Pick(List<(ItemEntity item, AbilityRestoreSpellSlot restore)> source, bool equipped) {
                foreach (var p in source) {
                    if (!Usable(p, equipped)) continue;
                    while (picks.Count < missing) {
                        if (!Take(p.item, equipped)) break;
                    }
                    if (picks.Count == missing) return;
                }
            }
            Pick(inventoryPearls, equipped: false);
            Pick(quickSlotPearls, equipped: true);
            if (picks.Count < missing) return false;

            foreach (var (item, equipped) in picks) {
                SpellSlot spent = null;
                if (!book.Blueprint.Spontaneous) {
                    spent = MatchingSlots(slotted, level).FirstOrDefault(s => !s.Available);
                    if (spent == null) break;
                }
                string name = item.Name;
                bool ok = equipped
                    ? UseEquippedPearl(item, caster, book, level, spent)
                    : UsePearl(item, caster, book, level, spent);
                if (!ok) break;
                used++;
                Main.Log($"[Pearls] {caster.CharacterName} used {name}{(equipped ? " (quick slot)" : "")}: level {level} slot back for {slotted.Name}");
            }
            return used > 0;
        }

        /// <summary>
        /// ItemEntity.TryUseFromInventory with the restore parameters filled in: a temporary Ability fact with
        /// the pearl as its source item, the restore cast delivered and ticked to the end on the spot, the
        /// pearl's charge spent through AbilityData.Spend, the fact removed again.
        /// </summary>
        private static bool UsePearl(ItemEntity pearl, UnitEntityData user, Spellbook book, int level, SpellSlot slot) {
            if (!pearl.IsUsableFromInventory || !ItemEntity.IsSuitableUnitForUseAbility(user)) return false;
            if (!pearl.TryGetUsableBlueprintAbility(out var blueprint)) return false;

            // ItemEntity.SpendCharges decrements the Charges of the WHOLE stack when a stack of single-charge
            // items is used (it only splits stacks with Charges > 1), so take one pearl off the stack first.
            var item = pearl.Count > 1 && pearl.Charges < 2 ? pearl.Split(1) : pearl;

            var fact = user.Descriptor.AddFact<Ability>(blueprint);
            if (fact == null) {
                Remerge(item, pearl);
                return false;
            }
            fact.SetSourceItem(item);
            try {
                var data = WithParams(new AbilityData(fact), book, level, slot);
                var target = new TargetWrapper(user);
                if (!data.IsAvailable || !data.CanTarget(target)) return false;

                var rule = Deliver(data, target);
                if (!rule.IsUMDFailed)
                    data.Spend();
                return rule.Success;
            } catch (Exception ex) {
                Main.Error(ex, $"[Pearls] using {pearl.Name} for {user.CharacterName}");
                return false;
            } finally {
                user.Descriptor.RemoveFact(fact);
                Remerge(item, pearl);
            }
        }

        /// <summary>
        /// A pearl in the user's own quick slot, used through the Ability fact the game gave the wielder when it
        /// was equipped — the in-combat quick-slot use, with the restore parameters filled in.
        /// </summary>
        private static bool UseEquippedPearl(ItemEntity pearl, UnitEntityData user, Spellbook book, int level, SpellSlot slot) {
            var fact = pearl.Ability;
            if (fact == null || pearl.Wielder?.Unit != user) return false;   // only the wielder, for their own book
            try {
                var data = WithParams(new AbilityData(fact), book, level, slot);
                var target = new TargetWrapper(user);
                if (!data.IsAvailable || !data.CanTarget(target)) return false;

                // Several identical single-charge pearls in one quick slot share ONE Charges value, and the
                // game's own spend would empty all of them. Split the used pearl off (it goes to the shared
                // inventory, spent until the next rest) so the others stay ready in the slot.
                var spentPearl = pearl.Count > 1 && pearl.Charges < 2 ? pearl.Split(1) : pearl;

                var rule = Deliver(data, target);
                if (!rule.IsUMDFailed) {
                    if (spentPearl == pearl)
                        data.Spend();
                    else
                        spentPearl.SpendCharges(user.Descriptor);
                }
                Remerge(spentPearl, pearl);
                return rule.Success;
            } catch (Exception ex) {
                Main.Error(ex, $"[Pearls] using {pearl.Name} from {user.CharacterName}'s quick slot");
                return false;
            }
        }

        private static AbilityData WithParams(AbilityData data, Spellbook book, int level, SpellSlot slot) {
            data.ParamSpellbook = book;
            data.ParamSpellLevel = level;
            data.ParamSpellSlot = slot;
            return data;
        }

        // The restore cast, delivered and ticked to its end on the spot, as TryUseFromInventory does.
        private static RuleCastSpell Deliver(AbilityData data, TargetWrapper target) {
            var rule = Rulebook.Trigger(new RuleCastSpell(data, target));
            if (rule.Success && rule.Result != null) {
                rule.Result.InstantDeliver();
                rule.Result.Detach();
                int ticks = 0;
                while (!rule.Result.IsEnded && ticks++ < 1000)
                    rule.Result.Tick();
            }
            return rule;
        }

        // A split-off pearl goes back to its stack when unused; a spent one joins other spent pearls of its kind.
        private static void Remerge(ItemEntity item, ItemEntity original) {
            if (item == original) return;
            try {
                item.Collection?.TryMergeContainedItem(item);
            } catch (Exception ex) {
                Main.Error(ex, "[Pearls] merging a pearl back into its stack");
            }
        }
    }

    /// <summary>
    /// Real spell-slot bookkeeping for one Recalculate pass. The scan's spell credits stand for the daily
    /// maximum (spontaneous) or every memorized copy, spent or not (prepared), so on a partly spent book
    /// Validate planned casts that had no slot behind them. The ledger starts from the slots actually free
    /// now, takes them as casts are planned, and lends Pearls of Power when a provider runs dry.
    /// </summary>
    /// <summary>
    /// After a rest the game restores every item's charges (RestController.ApplyRestInterval) but leaves stacks
    /// that were split apart — by a pearl use (ours or the game's own split of multi-charge stacks) — as
    /// separate stacks of identical, fully charged pearls. Merge the unequipped pearls of each kind back together
    /// with the inventory's own merge (TryMergeContainedItem: same kind, same charges, same craft data).
    /// </summary>
    internal class PearlStackMerger : IRestFinishedHandler {
        public void HandleRestFinished(RestStatus status) {
            try {
                var inventory = Game.Instance.Player.Inventory;
                bool IsLoosePearl(ItemEntity i) => i != null && i.HoldingSlot == null && PearlsOfPower.TryGetRestore(i, out _);
                var pearls = inventory.Where(IsLoosePearl).ToList();
                foreach (var pearl in pearls) {
                    if (pearl.Collection != inventory) continue;   // already merged into another stack
                    inventory.TryMergeContainedItem(pearl);
                }
                int after = inventory.Count(IsLoosePearl);
                if (after < pearls.Count)
                    Main.Log($"[Pearls] after the rest: {pearls.Count} pearl stacks merged into {after}");
            } catch (Exception ex) {
                Main.Error(ex, "[Pearls] merging pearl stacks after a rest");
            }
        }
    }

    /// <summary>
    /// Which pearls a spell may use: the kinds the player clicked green under the caster (inventory and quick
    /// slot picked separately), and whether quick-slot pearls are allowed at all ("Allow equipped pearls'
    /// usage"). A snapshot — taken when a routine is planned, carried by its CastTasks.
    /// </summary>
    internal class PearlPermit {
        private readonly HashSet<string> inventory;
        private readonly HashSet<string> equipped;
        internal readonly bool AllowEquipped;
        // For a cast task: the pearls its plan set aside (kind, from a quick slot), used before any other
        internal readonly List<(string Kind, bool FromQuickSlot)> Planned;

        internal PearlPermit(IEnumerable<string> inventory, IEnumerable<string> equipped, bool allowEquipped,
                             List<(string Kind, bool FromQuickSlot)> planned = null) {
            this.inventory = new HashSet<string>(inventory ?? Enumerable.Empty<string>());
            this.equipped = new HashSet<string>(equipped ?? Enumerable.Empty<string>());
            AllowEquipped = allowEquipped;
            Planned = planned ?? new List<(string Kind, bool FromQuickSlot)>();
        }

        internal bool Allows(string kind, bool fromQuickSlot) =>
            fromQuickSlot ? AllowEquipped && equipped.Contains(kind) : inventory.Contains(kind);
    }

    internal class SpellSlotLedger {
        internal static SpellSlotLedger Current;

        private readonly bool inventoryUsable;
        private readonly Dictionary<object, int> free = new();
        private readonly Dictionary<object, bool> castableWithRefill = new();
        private readonly List<PearlStock> inventory = new();
        private readonly Dictionary<UnitEntityData, List<PearlStock>> equipped = new();

        private class PearlStock {
            public AbilityRestoreSpellSlot Restore;
            public string Kind;
            public bool FromQuickSlot;
            public int Uses;
        }

        // The master switch in the settings. A buff is in pearl mode only with it on AND its own box ticked
        // (or the global box, see BubbleBuff.UsesPearls).
        internal readonly bool Enabled;
        internal readonly SavedBufferState Settings;

        internal SpellSlotLedger(SavedBufferState settings) {
            Settings = settings;
            Enabled = settings?.PearlsEnabled ?? false;
            inventoryUsable = Enabled && !Game.Instance.Player.IsInCombat;
            if (!inventoryUsable) return;
            try {
                foreach (var (item, restore) in PearlsOfPower.InventoryPearls())
                    inventory.Add(new PearlStock { Restore = restore, Kind = PearlsOfPower.KindId(item), Uses = item.Count * item.Charges });
            } catch (Exception ex) {
                Main.Error(ex, "[Pearls] reading the inventory");
            }
        }

        // The caster's own quick-slot pearls, read once per pass and then drawn down as casts are planned.
        private List<PearlStock> Equipped(UnitEntityData unit) {
            if (!equipped.TryGetValue(unit, out var list)) {
                list = new List<PearlStock>();
                try {
                    foreach (var (item, restore) in PearlsOfPower.EquippedPearls(unit))
                        list.Add(new PearlStock { Restore = restore, Kind = PearlsOfPower.KindId(item), FromQuickSlot = true, Uses = item.Count * item.Charges });
                } catch (Exception ex) {
                    Main.Error(ex, "[Pearls] reading quick slots");
                }
                equipped[unit] = list;
            }
            return list;
        }

        internal static bool Tracks(BuffProvider provider) =>
            provider.SourceType == BuffSourceType.Spell && provider.SlottedSpell?.Spellbook != null;

        // Spontaneous books share one pool per level; prepared slots are keyed by the first memorized slot that
        // holds the spell, so the variants of one prepared spell share it too.
        private static object PoolKey(AbilityData slotted, int level) {
            var book = slotted.Spellbook;
            if (book.Blueprint.Spontaneous) return (book, level);
            return PearlsOfPower.MatchingSlots(slotted, level).FirstOrDefault();
        }

        private int Free(object key, AbilityData slotted, int level) {
            if (!free.TryGetValue(key, out int n)) {
                n = PearlsOfPower.FreeSlots(slotted, level);
                free[key] = n;
            }
            return n;
        }

        /// <summary>
        /// Two passes per Recalculate (BufferState): first every spell may only take pearls of its OWN level,
        /// then whatever is left — higher pearls on lower spells. Without it the first spells in the list
        /// (often low-level ones like Barkskin) took the picked Pearl IIIs before a level-3 spell further down
        /// got its turn, because the cheapest fitting pearl went first (seen in game: Life Bubble planned 1 of 8).
        /// </summary>
        internal bool ExactLevelOnly;

        /// <summary>
        /// Plans one cast for this provider: from free slots if enough are left, else from free slots plus
        /// Pearls of Power. pearlSlots = the slots that pearls will have to refill; taken = which pearls (kind,
        /// quick slot) the plan set aside for this cast, so casting can use exactly those.
        /// </summary>
        internal bool TryReserve(BubbleBuff buff, BuffProvider provider, PearlPermit permit, out int pearlSlots, out List<(string Kind, bool FromQuickSlot)> taken) {
            pearlSlots = 0;
            taken = null;
            var slotted = provider.SlottedSpell;
            int level = PearlsOfPower.SlotLevel(slotted);
            if (level < 1) return slotted.IsAvailable;   // cantrips cost no slot

            var key = PoolKey(slotted, level);
            if (key == null) return false;               // prepared spell no longer memorized
            int perCast = PearlsOfPower.SlotsPerCast(slotted);
            int have = Free(key, slotted, level);

            if (have >= perCast) {
                if (!slotted.IsAvailable) return false;   // free slots, but the caster cannot cast right now
                free[key] = have - perCast;
                return true;
            }

            int missing = perCast - have;
            if (permit == null || !CanRefill(provider, slotted, level, perCast, key, permit, ExactLevelOnly)) return false;
            taken = TakePearls(provider, slotted.Spellbook, level, missing, permit, ExactLevelOnly);
            if (taken == null) return false;
            free[key] = 0;
            pearlSlots = missing;
            foreach (var (kind, fromQuickSlot) in taken)
                Allocate(kind, fromQuickSlot ? provider.who : null, buff);
            return true;
        }

        // Who the planned pearls are set aside for, per pearl kind and stock (the shared inventory, or one
        // character's quick slots): spell → number of pearls. Read by the pearl strip's hover panel.
        private readonly Dictionary<(string Kind, UnitEntityData QuickSlotOwner), Dictionary<BubbleBuff, int>> allocations = new();

        private void Allocate(string kind, UnitEntityData quickSlotOwner, BubbleBuff buff) {
            if (buff == null) return;
            var key = (kind, quickSlotOwner);
            if (!allocations.TryGetValue(key, out var bySpell)) {
                bySpell = new Dictionary<BubbleBuff, int>();
                allocations[key] = bySpell;
            }
            bySpell.TryGetValue(buff, out int n);
            bySpell[buff] = n + 1;
        }

        /// <summary>The spells this pearl kind is planned for (most pearls first); quickSlotOwner null = inventory.</summary>
        internal List<(BubbleBuff Buff, int Pearls)> AllocationsFor(string kind, UnitEntityData quickSlotOwner) {
            if (!allocations.TryGetValue((kind, quickSlotOwner), out var bySpell))
                return new List<(BubbleBuff Buff, int Pearls)>();
            return bySpell
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key.Name)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        /// <summary>
        /// First pass only: TryReserve just failed for want of a same-level pearl — would a higher picked pearl
        /// do it in the second pass? Then the cast is left for that pass instead of falling through to a scroll
        /// or potion (a pearl comes back after a rest, a scroll does not). No side effects.
        /// </summary>
        internal bool HigherPearlCouldServe(BuffProvider provider, PearlPermit permit) {
            if (!ExactLevelOnly || permit == null || !Tracks(provider)) return false;
            var slotted = provider.SlottedSpell;
            int level = PearlsOfPower.SlotLevel(slotted);
            if (level < 1) return false;
            var key = PoolKey(slotted, level);
            if (key == null) return false;
            int perCast = PearlsOfPower.SlotsPerCast(slotted);
            int missing = perCast - Free(key, slotted, level);
            if (missing <= 0) return false;              // not short of a slot: refused for another reason
            return Sources(provider, slotted.Spellbook, level, permit, exactOnly: false).Sum(p => p.Uses) >= missing
                && CanRefill(provider, slotted, level, perCast, key, permit, exactOnly: false);
        }

        /// <summary>
        /// A cast planned the original way (a buff not in pearl mode) still takes its slots: book them here so
        /// the buffs in pearl mode plan with what is left. Bookkeeping only — it never refuses anything, and a
        /// failure here must not touch the original planning.
        /// </summary>
        internal void Consume(BuffProvider provider) {
            if (!Enabled || !Tracks(provider)) return;
            try {
                var slotted = provider.SlottedSpell;
                int level = PearlsOfPower.SlotLevel(slotted);
                if (level < 1) return;
                var key = PoolKey(slotted, level);
                if (key == null) return;
                free[key] = Math.Max(0, Free(key, slotted, level) - PearlsOfPower.SlotsPerCast(slotted));
            } catch (Exception ex) {
                Main.Error(ex, $"[Pearls] slot bookkeeping for {provider.spell?.Name}");
            }
        }

        // Inventory pearls: out of combat, and only for a character the game would let use an inventory item.
        private bool InventoryOpenTo(UnitEntityData unit) =>
            inventoryUsable && inventory.Count > 0 && ItemEntity.IsSuitableUnitForUseAbility(unit);

        private bool CanRefill(BuffProvider provider, AbilityData slotted, int level, int perCast, object key, PearlPermit permit, bool exactOnly) {
            if (Sources(provider, slotted.Spellbook, level, permit, exactOnly).Count == 0) return false;
            if (PearlsOfPower.RestorableCapacity(slotted, level) < perCast) return false;
            if (!castableWithRefill.TryGetValue(key, out bool ok)) {
                ok = PearlsOfPower.CastableWithRefill(slotted, level, perCast);
                castableWithRefill[key] = ok;
            }
            return ok;
        }

        private static bool LevelOk(AbilityRestoreSpellSlot restore, int level, bool exactOnly) =>
            exactOnly ? !restore.AnySpellLevel && restore.SpellLevel == level : PearlsOfPower.Compatible(restore, level);

        // Pearls with uses left that fit this slot by the game's rule (exactly its level in the first pass) AND
        // were picked (green) for this spell
        private IEnumerable<PearlStock> Fitting(IEnumerable<PearlStock> stock, Spellbook book, int level, PearlPermit permit, bool exactOnly) =>
            stock.Where(p => p.Uses > 0
                && PearlsOfPower.BookAccepts(p.Restore, book, level)
                && LevelOk(p.Restore, level, exactOnly)
                && permit.Allows(p.Kind, p.FromQuickSlot));

        // The pearls this provider may draw on, in the order the cast will use them: inventory, then own quick slots.
        private List<PearlStock> Sources(BuffProvider provider, Spellbook book, int level, PearlPermit permit, bool exactOnly) {
            var sources = new List<PearlStock>();
            if (permit == null) return sources;
            if (InventoryOpenTo(provider.who)) sources.AddRange(Fitting(inventory, book, level, permit, exactOnly));
            if (permit.AllowEquipped) sources.AddRange(Fitting(Equipped(provider.who), book, level, permit, exactOnly));
            return sources;
        }

        // Takes `count` pearl uses (lowest level first) and returns which ones; null when there are not enough.
        private List<(string Kind, bool FromQuickSlot)> TakePearls(BuffProvider provider, Spellbook book, int level, int count, PearlPermit permit, bool exactOnly) {
            var sources = Sources(provider, book, level, permit, exactOnly);
            if (sources.Sum(p => p.Uses) < count) return null;
            var taken = new List<(string Kind, bool FromQuickSlot)>();
            foreach (var p in sources) {
                int take = Math.Min(p.Uses, count);
                p.Uses -= take;
                count -= take;
                for (int i = 0; i < take; i++) taken.Add((p.Kind, p.FromQuickSlot));
                if (count == 0) break;
            }
            return taken;
        }

        /// <summary>Why TryReserve would refuse this provider now, for the no-caster diagnostics; null = it would not.</summary>
        internal string Explain(BuffProvider provider, PearlPermit permit) {
            var slotted = provider.SlottedSpell;
            int level = PearlsOfPower.SlotLevel(slotted);
            if (level < 1) return slotted.IsAvailable ? null : "SlottedSpell not available";
            var key = PoolKey(slotted, level);
            if (key == null) return "spell no longer memorized";
            int perCast = PearlsOfPower.SlotsPerCast(slotted);
            int have = Free(key, slotted, level);
            if (have >= perCast)
                return slotted.IsAvailable ? null : "SlottedSpell not available";

            string slots = $"no level {level} slot left ({have} of {perCast} needed)";
            if (!Enabled) return $"{slots}; Pearls of Power off in settings";
            if (permit == null) return $"{slots}; Pearls of Power not used for this spell";
            var sources = Sources(provider, slotted.Spellbook, level, permit, exactOnly: false);
            if (sources.Count == 0) {
                return Game.Instance.Player.IsInCombat && !permit.AllowEquipped
                    ? $"{slots}; inventory pearls are used out of combat only"
                    : $"{slots}; no picked (green) Pearl of Power with a use left";
            }
            if (!CanRefill(provider, slotted, level, perCast, key, permit, exactOnly: false))
                return $"{slots}; a refilled slot still could not be cast";
            int uses = sources.Sum(p => p.Uses);
            return uses < perCast - have
                ? $"{slots}; {uses} picked Pearl of Power use(s) left"
                : null;
        }
    }
}
