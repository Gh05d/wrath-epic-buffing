using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BubbleBuffs {

    public class CustomDictionaryConverter<TKey, TValue> : JsonConverter {
        public override bool CanConvert(Type objectType) => objectType == typeof(Dictionary<TKey, TValue>);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            => serializer.Serialize(writer, ((Dictionary<TKey, TValue>)value).ToList());

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            => serializer.Deserialize<KeyValuePair<TKey, TValue>[]>(reader).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public class SavedBufferState {

        [JsonProperty]
        [JsonConverter(typeof(CustomDictionaryConverter<BuffKey, SavedBuffState>))]
        public Dictionary<BuffKey, SavedBuffState> Buffs = new();

        [JsonProperty]
        public bool VerboseCasting;
        [JsonProperty]
        public bool AllowInCombat;
        [JsonProperty]
        public bool OverwriteBuff;
        [JsonProperty]
        public SourcePriority GlobalSourcePriority = SourcePriority.SpellsScrollsPotions;
        [JsonProperty]
        public int UmdRetries = 3;
        [JsonProperty]
        public UmdMode UmdMode = UmdMode.AllowIfPossible;
        [JsonProperty]
        public bool ScrollsEnabled = true;
        [JsonProperty]
        public bool PotionsEnabled = true;
        [JsonProperty]
        public int Version;
    }

    public class SavedCasterState {
        [JsonProperty]
        public bool Banned;
        [JsonProperty]
        public int Cap;
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
        [JsonProperty]
        public bool Blacklisted;

        [JsonProperty]
        public string[] IgnoreForOverwriteCheck;

        [JsonProperty]
        public HashSet<string> Wanted;
        [JsonProperty]
        public List<string> CasterPriority;
        [JsonProperty]
        [JsonConverter(typeof(CustomDictionaryConverter<CasterKey, SavedCasterState>))]
        public Dictionary<CasterKey, SavedCasterState> Casters = new();
        [JsonProperty]
        public Guid BaseSpell;
        [JsonProperty]
        public int SourcePriorityOverride = -1; // -1 = use global default
        [JsonProperty]
        public int ScrollCap = -1; // -1 = no limit
        [JsonProperty]
        public int PotionCap = -1; // -1 = no limit
        [JsonProperty]
        public bool UseSpells = true;
        [JsonProperty]
        public bool UseScrolls = true;
        [JsonProperty]
        public bool UsePotions = true;
    }


}
