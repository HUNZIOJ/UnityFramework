using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Frame.Save
{
    public interface ISaveService
    {
        void SetSerializer(ISaveSerializer serializer);

        void SetEncryptor(ISaveEncryptor encryptor);

        void RegisterMigration<TData>(SaveMigration<TData> migration);

        void ClearMigrations<TData>();

        bool Exists(string slot);

        void Save<TData>(string slot, TData data);

        void Save<TData>(string slot, TData data, int dataVersion);

        Task SaveAsync<TData>(string slot, TData data, CancellationToken cancellationToken = default(CancellationToken));

        Task SaveAsync<TData>(string slot, TData data, int dataVersion, CancellationToken cancellationToken = default(CancellationToken));

        bool TryLoad<TData>(string slot, out TData data);

        Task<SaveLoadResult<TData>> TryLoadAsync<TData>(string slot, CancellationToken cancellationToken = default(CancellationToken));

        TData Load<TData>(string slot, TData fallback = default(TData));

        Task<TData> LoadAsync<TData>(string slot, TData fallback = default(TData), CancellationToken cancellationToken = default(CancellationToken));

        bool Delete(string slot);

        List<SaveSlotInfo> ListSlots();

        bool TryGetMetadata(string slot, out SaveMetadata metadata);

        string GetPath(string slot);
    }
}
