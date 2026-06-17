using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace Frame.Save
{
    public sealed class BinarySaveSerializer : ISaveSerializer
    {
        private readonly DataContractSerializerSettings settings;

        public BinarySaveSerializer()
            : this(CreateDefaultSettings())
        {
        }

        public BinarySaveSerializer(DataContractSerializerSettings settings)
        {
            this.settings = settings == null ? CreateDefaultSettings() : settings;
        }

        public string FileExtension
        {
            get { return ".bin"; }
        }

        public byte[] Serialize<TData>(TData data)
        {
            DataContractSerializer serializer = CreateSerializer(typeof(TData));
            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlDictionaryWriter writer = XmlDictionaryWriter.CreateBinaryWriter(stream))
                {
                    serializer.WriteObject(writer, data);
                }

                return stream.ToArray();
            }
        }

        public TData Deserialize<TData>(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException("Binary save data is empty.", "bytes");
            }

            DataContractSerializer serializer = CreateSerializer(typeof(TData));
            using (MemoryStream stream = new MemoryStream(bytes))
            using (XmlDictionaryReader reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                return (TData)serializer.ReadObject(reader);
            }
        }

        private DataContractSerializer CreateSerializer(Type type)
        {
            return new DataContractSerializer(type, settings);
        }

        private static DataContractSerializerSettings CreateDefaultSettings()
        {
            return new DataContractSerializerSettings
            {
                PreserveObjectReferences = true
            };
        }
    }
}
