using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuffIt2TheLimit {

    public class CustomDictionaryConverter<TKey, TValue> : JsonConverter {
        public override bool CanConvert(Type objectType) => objectType == typeof(Dictionary<TKey, TValue>);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            => serializer.Serialize(writer, ((Dictionary<TKey, TValue>)value).ToList());

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            // Deserialize returns null for a null/missing JSON node (old or hand-edited
            // save files) — fall back to an empty dictionary instead of crashing the load
            => serializer.Deserialize<KeyValuePair<TKey, TValue>[]>(reader)?.ToDictionary(kv => kv.Key, kv => kv.Value)
               ?? new Dictionary<TKey, TValue>();
    }

    public class SavedBufferState {

        [JsonProperty]
        [JsonConverter(typeof(CustomDictionaryConverter<BuffKey, SavedBuffState>))]
        public Dictionary<BuffKey, SavedBuffState> Buffs = new();

        [JsonProperty]
        public bool VerboseCasting;
        [JsonProperty]
        public bool SkipAnimationsOnCombatStart;
        [JsonProperty]
        public bool CastAllOnCombatStart;
        [JsonProperty]
        public bool AllowInCombat;
        [JsonProperty]
        public bool BypassArcaneSpellFailure;
        [JsonProperty]
        public bool OverwriteBuff;
        [JsonProperty]
        public SourcePriority GlobalSourcePriority = SourcePriority.SpellsScrollsPotions;
        [JsonProperty]
        // Global caster rank per unit UniqueId — higher rank casts earlier.
        // Only non-zero entries are stored; a missing unit means rank 0.
        public Dictionary<string, int> CasterRanks = new();
        [JsonProperty]
        // Preferred mount per rider UniqueId → pet UniqueId. Consulted only when
        // several pets are suitable (e.g. Azata's grown Aivu + an animal
        // companion); no entry = auto, which mounts only an unambiguous single
        // candidate.
        public Dictionary<string, string> MountPreference = new();
        [JsonProperty]
        // Preferred Shifter's Fury weapon per unit UniqueId → weapon blueprint GUID.
        // Keyed by blueprint rather than by ShiftersFuryPart.AppliedFacts index because
        // that list is destroyed and rebuilt (all entries off, selection lost) on every
        // equipment or polymorph change — an index would silently point at another limb.
        // No entry = auto, which falls back to the game's own last pick.
        public Dictionary<string, string> FuryWeaponPreference = new();
        [JsonProperty]
        public int UmdRetries = 3;
        [JsonProperty]
        public UmdMode UmdMode = UmdMode.AllowIfPossible;
        [JsonProperty]
        public bool ScrollsEnabled = true;
        [JsonProperty]
        public bool PotionsEnabled = true;
        [JsonProperty]
        public bool EquipmentEnabled = true;
        [JsonProperty]
        [System.ComponentModel.DefaultValue(true)]
        public bool SongsEnabled = true;
        [JsonProperty]
        [System.ComponentModel.DefaultValue(true)]
        public bool ActivatablesEnabled = true;
        [JsonProperty]
        [System.ComponentModel.DefaultValue(true)]
        // Refill spent spell slots with Pearls of Power from the shared inventory (out of combat only)
        public bool PearlsEnabled = true;
        [JsonProperty]
        // "Pearls' settings applied globally": on = the two pearl boxes below are one setting for every spell;
        // off = each spell keeps its own boxes (SavedBuffState.UsePearls / UseEquippedPearls, untouched meanwhile).
        public bool PearlsGlobal;
        [JsonProperty]
        public bool GlobalUsePearls;
        [JsonProperty]
        public bool GlobalUseEquippedPearls;
        [JsonProperty]
        // Pearl kinds (item blueprint ids) picked green while the global box rules: inventory / quick slot
        public HashSet<string> GlobalPearlKinds = new();
        [JsonProperty]
        public HashSet<string> GlobalEquippedPearlKinds = new();
        [JsonProperty]
        public bool SortByName;
        [JsonProperty]
        public Dictionary<BuffGroup, ShortcutBinding> ShortcutKeys = new();
        [JsonProperty]
        public ShortcutBinding OpenBuffMenuKey;
        [JsonProperty]
        public int Version;
    }

    public class SavedCasterState {
        [JsonProperty]
        public bool Banned;
        // -1 = no custom cap (sentinel must match BubbleBuff.CustomCap). A plain 0
        // default would mean "may never cast" for entries deserialized from saves
        // that predate this field.
        [JsonProperty]
        public int Cap = -1;
        // null = inherit the global CasterRanks value for this unit.
        [JsonProperty]
        public int? PriorityOverride;
        [JsonProperty]
        public bool ShareTransmutation;
        [JsonProperty]
        public bool PowerfulChange;
        [JsonProperty]
        public bool ReservoirCLBuff;
        [JsonProperty]
        public bool UseAzataZippyMagic;
    }

    public class SavedBuffState {

        [JsonProperty]
        public BuffGroup InGroup;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public HashSet<BuffGroup> InGroups;
        [JsonProperty]
        public bool Blacklisted;

        [JsonProperty]
        public string[] IgnoreForOverwriteCheck;

        [JsonProperty]
        public HashSet<string> Wanted = new();
        [JsonProperty]
        [JsonConverter(typeof(CustomDictionaryConverter<CasterKey, SavedCasterState>))]
        public Dictionary<CasterKey, SavedCasterState> Casters = new();
        [JsonProperty]
        public Guid BaseSpell;
        [JsonProperty]
        public int SourcePriorityOverride = -1; // -1 = use global default
        [JsonProperty]
        public bool UseSpells = true;
        [JsonProperty]
        public bool UseScrolls = true;
        [JsonProperty]
        public bool UsePotions = true;
        [JsonProperty]
        public bool UseEquipment = true;
        [JsonProperty]
        public bool UseExtendRod;
        [JsonProperty]
        public bool UsePearls;
        [JsonProperty]
        public bool UseEquippedPearls;
        [JsonProperty]
        // Pearl kinds (item blueprint ids) this spell may use, picked green under the caster
        public HashSet<string> PearlKinds;
        [JsonProperty]
        public HashSet<string> EquippedPearlKinds;
        [JsonProperty]
        public bool CastOnCombatStart;
        [JsonProperty]
        public int DeactivateAfterRounds;
    }


}
