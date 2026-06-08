using System.Collections.Generic;

namespace Frame.Save
{
    public interface ISaveService
    {
        void SetSerializer(ISaveSerializer serializer);

        bool Exists(string slot);

        void Save<TData>(string slot, TData data);

        bool TryLoad<TData>(string slot, out TData data);

        TData Load<TData>(string slot, TData fallback = default(TData));

        bool Delete(string slot);

        List<SaveSlotInfo> ListSlots();

        string GetPath(string slot);
    }
}
