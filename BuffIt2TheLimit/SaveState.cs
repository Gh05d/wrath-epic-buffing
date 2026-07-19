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
        public bool CastOnCombatStart;
        [JsonProperty]
        public int DeactivateAfterRounds;
    }


}
