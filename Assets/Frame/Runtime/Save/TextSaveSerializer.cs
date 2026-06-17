using System.Text;

namespace Frame.Save
{
    public abstract class TextSaveSerializer : ISaveSerializer
    {
        public abstract string FileExtension { get; }

        protected virtual Encoding TextEncoding
        {
            get { return Encoding.UTF8; }
        }

        public byte[] Serialize<TData>(TData data)
        {
            string text = SerializeToText(data);
            return TextEncoding.GetBytes(text ?? string.Empty);
        }

        public TData Deserialize<TData>(byte[] bytes)
        {
            string text = bytes == null || bytes.Length == 0 ? string.Empty : TextEncoding.GetString(bytes);
            return DeserializeFromText<TData>(text);
        }

        protected abstract string SerializeToText<TData>(TData data);

        protected abstract TData DeserializeFromText<TData>(string text);
    }
}
