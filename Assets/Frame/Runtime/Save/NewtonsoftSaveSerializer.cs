using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Frame.Save
{
    public sealed class NewtonsoftSaveSerializer : ISaveSerializer
    {
        private readonly JsonSerializerSettings settings;

        public NewtonsoftSaveSerializer()
            : this(CreateDefaultSettings())
        {
        }

        public NewtonsoftSaveSerializer(JsonSerializerSettings settings)
        {
            this.settings = settings == null ? CreateDefaultSettings() : settings;
        }

        public string Serialize<TData>(TData data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented, settings);
        }

        public TData Deserialize<TData>(string text)
        {
            return JsonConvert.DeserializeObject<TData>(text, settings);
        }

        private static JsonSerializerSettings CreateDefaultSettings()
        {
            JsonSerializerSettings serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver(),
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                DefaultValueHandling = DefaultValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None
            };

            serializerSettings.Converters.Add(new StringEnumConverter());
            return serializerSettings;
        }
    }
}
