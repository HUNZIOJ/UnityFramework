using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Frame.Core;
using Frame.Utilities;
using UnityEngine;

namespace Frame.Save
{
    public sealed class SaveService : GameModuleBase, ISaveService
    {
        private const string TempExtension = ".tmp";
        private const string BackupExtension = ".bak";
        private const string MetadataExtension = ".meta";
        private const int MetadataFormatVersion = 1;

        private readonly Dictionary<Type, List<object>> migrations = new Dictionary<Type, List<object>>();
        private ISaveSerializer serializer;
        private ISaveEncryptor encryptor;
        private string saveRoot;

        public override int Priority
        {
            get { return -100; }
        }

        protected override void OnInitialize()
        {
            serializer = new NewtonsoftSaveSerializer();
            saveRoot = Path.Combine(Application.persistentDataPath, Context.Settings.SaveFolderName);
            Directory.CreateDirectory(saveRoot);
            Context.Services.Register<ISaveService>(this);
            Context.Services.Register(this);
        }

        public void SetSerializer(ISaveSerializer serializer)
        {
            if (serializer != null)
            {
                this.serializer = serializer;
            }
        }

        public void SetEncryptor(ISaveEncryptor encryptor)
        {
            this.encryptor = encryptor;
        }

        public void RegisterMigration<TData>(SaveMigration<TData> migration)
        {
            if (migration == null)
            {
                throw new ArgumentNullException("migration");
            }

            Type dataType = typeof(TData);
            List<object> list;
            if (!migrations.TryGetValue(dataType, out list))
            {
                list = new List<object>();
                migrations[dataType] = list;
            }

            list.Add(migration);
            list.Sort((left, right) =>
            {
                SaveMigration<TData> leftMigration = (SaveMigration<TData>)left;
                SaveMigration<TData> rightMigration = (SaveMigration<TData>)right;
                return leftMigration.FromVersion.CompareTo(rightMigration.FromVersion);
            });
        }

        public void ClearMigrations<TData>()
        {
            migrations.Remove(typeof(TData));
        }

        public bool Exists(string slot)
        {
            ValidateSlot(slot);
            return File.Exists(GetPath(slot));
        }

        public void Save<TData>(string slot, TData data)
        {
            Save(slot, data, ResolveDataVersion(data));
        }

        public void Save<TData>(string slot, TData data, int dataVersion)
        {
            string path = GetPath(slot);
            string tempPath = path + TempExtension;
            string tempMetadataPath = GetMetadataPath(path) + TempExtension;
            string backupPath = path + BackupExtension;
            byte[] bytes = serializer.Serialize(data);
            if (encryptor != null)
            {
                bytes = encryptor.Encrypt(bytes);
            }

            SaveMetadata metadata = CreateMetadata(slot, bytes, dataVersion);
            File.WriteAllBytes(tempPath, bytes);
            WriteMetadata(tempMetadataPath, metadata);

            try
            {
                if (File.Exists(path))
                {
                    BackupMetadata(path, backupPath);
                    File.Replace(tempPath, path, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                ReplaceMetadata(tempMetadataPath, GetMetadataPath(path));
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                if (File.Exists(tempMetadataPath))
                {
                    File.Delete(tempMetadataPath);
                }

                throw;
            }
        }

        public Task SaveAsync<TData>(string slot, TData data, CancellationToken cancellationToken = default(CancellationToken))
        {
            return SaveAsync(slot, data, ResolveDataVersion(data), cancellationToken);
        }

        public async Task SaveAsync<TData>(string slot, TData data, int dataVersion, CancellationToken cancellationToken = default(CancellationToken))
        {
            string path = GetPath(slot);
            string tempPath = path + TempExtension;
            string tempMetadataPath = GetMetadataPath(path) + TempExtension;
            string backupPath = path + BackupExtension;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes = serializer.Serialize(data);
                if (encryptor != null)
                {
                    bytes = encryptor.Encrypt(bytes);
                }

                SaveMetadata metadata = CreateMetadata(slot, bytes, dataVersion);
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
                await WriteMetadataAsync(tempMetadataPath, metadata, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(path))
                {
                    BackupMetadata(path, backupPath);
                    File.Replace(tempPath, path, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                ReplaceMetadata(tempMetadataPath, GetMetadataPath(path));
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                if (File.Exists(tempMetadataPath))
                {
                    File.Delete(tempMetadataPath);
                }

                throw;
            }
        }

        public bool TryLoad<TData>(string slot, out TData data)
        {
            ValidateSlot(slot);
            string path = GetPath(slot);
            if (TryLoadFromPath(path, out data))
            {
                return true;
            }

            string backupPath = path + BackupExtension;
            return TryLoadFromPath(backupPath, out data);
        }

        public async Task<SaveLoadResult<TData>> TryLoadAsync<TData>(string slot, CancellationToken cancellationToken = default(CancellationToken))
        {
            ValidateSlot(slot);
            string path = GetPath(slot);
            SaveLoadResult<TData> result = await TryLoadFromPathAsync<TData>(path, cancellationToken);
            if (result.Success)
            {
                return result;
            }

            string backupPath = path + BackupExtension;
            return await TryLoadFromPathAsync<TData>(backupPath, cancellationToken);
        }

        public TData Load<TData>(string slot, TData fallback = default(TData))
        {
            TData data;
            return TryLoad(slot, out data) ? data : fallback;
        }

        public async Task<TData> LoadAsync<TData>(string slot, TData fallback = default(TData), CancellationToken cancellationToken = default(CancellationToken))
        {
            SaveLoadResult<TData> result = await TryLoadAsync<TData>(slot, cancellationToken);
            return result.Success ? result.Data : fallback;
        }

        public bool Delete(string slot)
        {
            ValidateSlot(slot);
            string path = GetPath(slot);
            string backupPath = path + BackupExtension;
            bool deleted = false;
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
                deleted = true;
            }

            string metadataPath = GetMetadataPath(path);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
                deleted = true;
            }

            string backupMetadataPath = GetMetadataPath(backupPath);
            if (File.Exists(backupMetadataPath))
            {
                File.Delete(backupMetadataPath);
                deleted = true;
            }

            return deleted;
        }

        public List<SaveSlotInfo> ListSlots()
        {
            List<SaveSlotInfo> result = new List<SaveSlotInfo>();
            if (!Directory.Exists(saveRoot))
            {
                return result;
            }

            string[] files = Directory.GetFiles(saveRoot, "*" + GetSerializerFileExtension(), SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo info = new FileInfo(files[i]);
                result.Add(new SaveSlotInfo
                {
                    Slot = Path.GetFileNameWithoutExtension(files[i]),
                    Path = files[i],
                    LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                    SizeBytes = info.Length
                });

                SaveMetadata metadata;
                if (TryReadMetadata(files[i], out metadata))
                {
                    SaveSlotInfo slotInfo = result[result.Count - 1];
                    slotInfo.HasMetadata = true;
                    slotInfo.DataVersion = metadata.DataVersion;
                    slotInfo.Encrypted = metadata.Encrypted;
                    slotInfo.SerializerExtension = metadata.SerializerExtension;
                }
            }

            return result;
        }

        public bool TryGetMetadata(string slot, out SaveMetadata metadata)
        {
            ValidateSlot(slot);
            return TryReadMetadata(GetPath(slot), out metadata);
        }

        public string GetPath(string slot)
        {
            ValidateSlot(slot);
            string fileName = FramePathUtility.SanitizeFileName(slot) + GetSerializerFileExtension();
            return Path.Combine(saveRoot, fileName);
        }

        protected override void OnShutdown()
        {
            migrations.Clear();
            serializer = null;
            encryptor = null;
            saveRoot = null;
        }

        private bool TryLoadFromPath<TData>(string path, out TData data)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                data = default(TData);
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                SaveMetadata metadata;
                bool metadataFileExists = File.Exists(GetMetadataPath(path));
                bool hasMetadata = TryReadMetadata(path, out metadata);
                if (metadataFileExists && !hasMetadata)
                {
                    data = default(TData);
                    return false;
                }

                if (hasMetadata && !ValidatePayload(path, bytes, metadata))
                {
                    data = default(TData);
                    return false;
                }

                bool shouldDecrypt = hasMetadata ? metadata.Encrypted : encryptor != null;
                if (shouldDecrypt)
                {
                    if (encryptor == null)
                    {
                        throw new InvalidOperationException("Save data is encrypted but no save encryptor is configured.");
                    }

                    bytes = encryptor.Decrypt(bytes);
                }

                data = serializer.Deserialize<TData>(bytes);
                data = ApplyMigrations(data, hasMetadata ? metadata.DataVersion : 0);

                return true;
            }
            catch (System.Exception exception)
            {
                FrameLog.Exception(exception);
                data = default(TData);
                return false;
            }
        }

        private async Task<SaveLoadResult<TData>> TryLoadFromPathAsync<TData>(string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new SaveLoadResult<TData>(false, default(TData));
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                string metadataPath = GetMetadataPath(path);
                bool metadataFileExists = File.Exists(metadataPath);
                bool hasMetadata = false;
                SaveMetadata metadata = null;
                if (metadataFileExists)
                {
                    string text = await File.ReadAllTextAsync(metadataPath, Encoding.UTF8, cancellationToken);
                    metadata = JsonUtility.FromJson<SaveMetadata>(text);
                    hasMetadata = metadata != null;
                    if (!hasMetadata)
                    {
                        return new SaveLoadResult<TData>(false, default(TData));
                    }
                }

                if (hasMetadata && !ValidatePayload(path, bytes, metadata))
                {
                    return new SaveLoadResult<TData>(false, default(TData));
                }

                bool shouldDecrypt = hasMetadata ? metadata.Encrypted : encryptor != null;
                if (shouldDecrypt)
                {
                    if (encryptor == null)
                    {
                        throw new InvalidOperationException("Save data is encrypted but no save encryptor is configured.");
                    }

                    bytes = encryptor.Decrypt(bytes);
                }

                TData data = serializer.Deserialize<TData>(bytes);
                data = ApplyMigrations(data, hasMetadata ? metadata.DataVersion : 0);

                return new SaveLoadResult<TData>(true, data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception exception)
            {
                FrameLog.Exception(exception);
                return new SaveLoadResult<TData>(false, default(TData));
            }
        }

        private SaveMetadata CreateMetadata(string slot, byte[] bytes, int dataVersion)
        {
            return new SaveMetadata
            {
                FormatVersion = MetadataFormatVersion,
                Slot = slot,
                SerializerExtension = GetSerializerFileExtension(),
                DataVersion = Math.Max(0, dataVersion),
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                PayloadSizeBytes = bytes == null ? 0 : bytes.Length,
                PayloadSha256 = ComputeSha256(bytes),
                Encrypted = encryptor != null
            };
        }

        private TData ApplyMigrations<TData>(TData data, int dataVersion)
        {
            List<object> list;
            if (!migrations.TryGetValue(typeof(TData), out list) || list.Count == 0)
            {
                return data;
            }

            int currentVersion = Math.Max(0, dataVersion);
            bool migrated;
            do
            {
                migrated = false;
                for (int i = 0; i < list.Count; i++)
                {
                    SaveMigration<TData> migration = (SaveMigration<TData>)list[i];
                    if (migration.FromVersion != currentVersion)
                    {
                        continue;
                    }

                    data = migration.Apply(data);
                    currentVersion = migration.ToVersion;
                    migrated = true;
                    break;
                }
            } while (migrated);

            return data;
        }

        private void BackupMetadata(string path, string backupPath)
        {
            string metadataPath = GetMetadataPath(path);
            string backupMetadataPath = GetMetadataPath(backupPath);
            if (File.Exists(metadataPath))
            {
                File.Copy(metadataPath, backupMetadataPath, true);
            }
            else if (File.Exists(backupMetadataPath))
            {
                File.Delete(backupMetadataPath);
            }
        }

        private static void ReplaceMetadata(string tempMetadataPath, string metadataPath)
        {
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            File.Move(tempMetadataPath, metadataPath);
        }

        private static Task WriteMetadataAsync(string metadataPath, SaveMetadata metadata, CancellationToken cancellationToken)
        {
            string text = JsonUtility.ToJson(metadata, true);
            return File.WriteAllTextAsync(metadataPath, text, Encoding.UTF8, cancellationToken);
        }

        private static void WriteMetadata(string metadataPath, SaveMetadata metadata)
        {
            string text = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(metadataPath, text, Encoding.UTF8);
        }

        private bool TryReadMetadata(string path, out SaveMetadata metadata)
        {
            string metadataPath = GetMetadataPath(path);
            if (!File.Exists(metadataPath))
            {
                metadata = null;
                return false;
            }

            try
            {
                string text = File.ReadAllText(metadataPath, Encoding.UTF8);
                metadata = JsonUtility.FromJson<SaveMetadata>(text);
                return metadata != null;
            }
            catch (System.Exception exception)
            {
                FrameLog.Exception(exception);
                metadata = null;
                return false;
            }
        }

        private static bool ValidatePayload(string path, byte[] bytes, SaveMetadata metadata)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.PayloadSha256))
            {
                return true;
            }

            long size = bytes == null ? 0 : bytes.Length;
            if (metadata.PayloadSizeBytes != size)
            {
                FrameLog.Warning("Save payload size mismatch: " + path);
                return false;
            }

            string actualSha256 = ComputeSha256(bytes);
            if (!string.Equals(metadata.PayloadSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                FrameLog.Warning("Save payload checksum mismatch: " + path);
                return false;
            }

            return true;
        }

        private static int ResolveDataVersion<TData>(TData data)
        {
            ISaveVersionedData versioned = data as ISaveVersionedData;
            return versioned == null ? 0 : Math.Max(0, versioned.SaveVersion);
        }

        private static string GetMetadataPath(string path)
        {
            return path + MetadataExtension;
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes ?? Array.Empty<byte>());
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private string GetSerializerFileExtension()
        {
            string extension = serializer == null ? null : serializer.FileExtension;
            if (string.IsNullOrWhiteSpace(extension))
            {
                return ".save";
            }

            extension = extension.Trim();
            return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        }

        private static void ValidateSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new ArgumentException("Save slot is required.", "slot");
            }
        }
    }
}
