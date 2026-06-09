using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using Frame.Core;
using Frame.Utilities;
using UnityEngine;

namespace Frame.Save
{
    public sealed class SaveService : GameModuleBase, ISaveService
    {
        private const string TempExtension = ".tmp";
        private const string BackupExtension = ".bak";

        private ISaveSerializer serializer;
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

        public bool Exists(string slot)
        {
            ValidateSlot(slot);
            return File.Exists(GetPath(slot));
        }

        public void Save<TData>(string slot, TData data)
        {
            string path = GetPath(slot);
            string tempPath = path + TempExtension;
            string backupPath = path + BackupExtension;
            string text = serializer.Serialize(data);
            File.WriteAllText(tempPath, text, Encoding.UTF8);

            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
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

        public TData Load<TData>(string slot, TData fallback = default(TData))
        {
            TData data;
            return TryLoad(slot, out data) ? data : fallback;
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

            return deleted;
        }

        public List<SaveSlotInfo> ListSlots()
        {
            List<SaveSlotInfo> result = new List<SaveSlotInfo>();
            if (!Directory.Exists(saveRoot))
            {
                return result;
            }

            string[] files = Directory.GetFiles(saveRoot, "*.json", SearchOption.TopDirectoryOnly);
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
            }

            return result;
        }

        public string GetPath(string slot)
        {
            ValidateSlot(slot);
            string fileName = FramePathUtility.SanitizeFileName(slot) + ".json";
            return Path.Combine(saveRoot, fileName);
        }

        protected override void OnShutdown()
        {
            serializer = null;
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
                string text = File.ReadAllText(path, Encoding.UTF8);
                data = serializer.Deserialize<TData>(text);
                return true;
            }
            catch (System.Exception exception)
            {
                FrameLog.Exception(exception);
                data = default(TData);
                return false;
            }
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
