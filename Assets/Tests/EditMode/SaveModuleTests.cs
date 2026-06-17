using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Frame.Save;
using NUnit.Framework;
using UnityEngine;

namespace Frame.Tests.EditMode
{
    public sealed class SaveModuleTests
    {
        [SetUp]
        public void CleanSaveFolder()
        {
            string path = Path.Combine(Application.persistentDataPath, "Saves");
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        [TearDown]
        public void CleanupSaveFolder()
        {
            CleanSaveFolder();
        }

        [Test]
        public void SaveService_SavesLoadsExistsListsAndDeletesSlots()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                SaveData data = new SaveData { Name = "Player", Level = 5 };

                service.Save("slot_1", data);

                Assert.IsTrue(service.GetPath("slot_1").EndsWith(".json", StringComparison.Ordinal));
                Assert.IsTrue(service.Exists("slot_1"));
                Assert.IsTrue(service.TryLoad("slot_1", out SaveData loaded));
                Assert.AreEqual("Player", loaded.Name);
                Assert.AreEqual(5, loaded.Level);
                Assert.AreEqual(5, service.Load<SaveData>("slot_1").Level);

                List<SaveSlotInfo> slots = service.ListSlots();
                Assert.AreEqual(1, slots.Count);
                Assert.AreEqual("slot_1", slots[0].Slot);
                Assert.Greater(slots[0].SizeBytes, 0);
                Assert.IsTrue(slots[0].HasMetadata);

                Assert.IsTrue(service.Delete("slot_1"));
                Assert.IsFalse(service.Exists("slot_1"));
                Assert.IsFalse(service.TryGetMetadata("slot_1", out SaveMetadata metadata));
                Assert.IsNull(metadata);
            }
        }

        [Test]
        public async Task SaveService_SaveAsyncAndLoadAsyncRoundTrip()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());

                await service.SaveAsync("slot_async", new SaveData { Name = "Async", Level = 12 }, dataVersion: 3);

                SaveLoadResult<SaveData> result = await service.TryLoadAsync<SaveData>("slot_async");
                Assert.IsTrue(result.Success);
                Assert.AreEqual("Async", result.Data.Name);
                Assert.AreEqual(12, result.Data.Level);

                Assert.IsTrue(service.TryGetMetadata("slot_async", out SaveMetadata metadata));
                Assert.AreEqual(3, metadata.DataVersion);

                SaveData fallback = await service.LoadAsync("missing_async", new SaveData { Name = "Fallback", Level = -1 });
                Assert.AreEqual("Fallback", fallback.Name);
                Assert.AreEqual(-1, fallback.Level);
            }
        }

        [Test]
        public void SaveService_FallsBackToBackupWhenPrimaryIsCorrupt()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                service.Save("slot_backup", new SaveData { Name = "Old", Level = 1 });
                service.Save("slot_backup", new SaveData { Name = "New", Level = 2 });

                File.WriteAllText(service.GetPath("slot_backup"), "{ invalid json");

                SaveData loaded = null;
                AssertEx.WithFrameLogsOff(() =>
                {
                    Assert.IsTrue(service.TryLoad("slot_backup", out loaded));
                });

                Assert.AreEqual("Old", loaded.Name);
                Assert.AreEqual(1, loaded.Level);
            }
        }

        [Test]
        public void SaveService_RejectsEmptySlotAndSupportsCustomSerializer()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                MemorySerializer serializer = new MemorySerializer();
                service.SetSerializer(serializer);

                Assert.Throws<ArgumentException>(() => service.Save("", new SaveData()));

                service.Save("custom", new SaveData { Name = "Custom", Level = 7 });
                SaveData loaded = service.Load("custom", new SaveData());

                Assert.AreEqual("Custom", loaded.Name);
                Assert.AreEqual(7, loaded.Level);
                Assert.AreEqual(1, serializer.SerializeCount);
                Assert.AreEqual(1, serializer.DeserializeCount);
            }
        }

        [Test]
        public void SaveSerializers_RoundTripSerializableData()
        {
            SaveData data = new SaveData { Name = "RoundTrip", Level = 10 };
            ComplexSaveData complex = new ComplexSaveData();
            complex.Name = "Complex";
            complex.Items.Add("Sword");
            complex.Scores["Stage1"] = 100;

            NewtonsoftSaveSerializer newtonsoft = new NewtonsoftSaveSerializer();
            SaveData newtonsoftLoaded = newtonsoft.Deserialize<SaveData>(newtonsoft.Serialize(data));
            Assert.AreEqual(data.Name, newtonsoftLoaded.Name);
            Assert.AreEqual(data.Level, newtonsoftLoaded.Level);

            ComplexSaveData newtonsoftComplex = newtonsoft.Deserialize<ComplexSaveData>(newtonsoft.Serialize(complex));
            Assert.AreEqual("Sword", newtonsoftComplex.Items[0]);
            Assert.AreEqual(100, newtonsoftComplex.Scores["Stage1"]);

            BinarySaveSerializer binary = new BinarySaveSerializer();
            SaveData binaryLoaded = binary.Deserialize<SaveData>(binary.Serialize(data));
            Assert.AreEqual(data.Name, binaryLoaded.Name);
            Assert.AreEqual(data.Level, binaryLoaded.Level);

            ComplexSaveData binaryComplex = binary.Deserialize<ComplexSaveData>(binary.Serialize(complex));
            Assert.AreEqual("Sword", binaryComplex.Items[0]);
            Assert.AreEqual(100, binaryComplex.Scores["Stage1"]);
        }

        [Test]
        public void SaveService_SupportsBinarySerializerAndEncryption()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                service.SetSerializer(new BinarySaveSerializer());
                service.SetEncryptor(new AesSaveEncryptor("test-save-key"));

                service.Save("secure", new SaveData { Name = "SecretPlayer", Level = 9 });

                string path = service.GetPath("secure");
                Assert.IsTrue(path.EndsWith(".bin", StringComparison.Ordinal));

                byte[] raw = File.ReadAllBytes(path);
                string rawText = Encoding.UTF8.GetString(raw);
                Assert.IsFalse(rawText.Contains("SecretPlayer"));

                Assert.IsTrue(service.TryLoad("secure", out SaveData loaded));
                Assert.AreEqual("SecretPlayer", loaded.Name);
                Assert.AreEqual(9, loaded.Level);
            }
        }

        [Test]
        public void SaveService_WritesMetadataAndFallsBackWhenChecksumMismatches()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                service.Save("slot_integrity", new SaveData { Name = "Old", Level = 1 }, dataVersion: 1);
                service.Save("slot_integrity", new SaveData { Name = "New", Level = 2 }, dataVersion: 2);

                Assert.IsTrue(service.TryGetMetadata("slot_integrity", out SaveMetadata metadata));
                Assert.AreEqual(2, metadata.DataVersion);
                Assert.AreEqual(".json", metadata.SerializerExtension);
                Assert.IsFalse(metadata.Encrypted);
                Assert.Greater(metadata.PayloadSizeBytes, 0);
                Assert.IsFalse(string.IsNullOrWhiteSpace(metadata.PayloadSha256));

                List<SaveSlotInfo> slots = service.ListSlots();
                Assert.AreEqual(1, slots.Count);
                Assert.IsTrue(slots[0].HasMetadata);
                Assert.AreEqual(2, slots[0].DataVersion);

                byte[] tamperedBytes = new NewtonsoftSaveSerializer().Serialize(new SaveData { Name = "Tampered", Level = 99 });
                File.WriteAllBytes(service.GetPath("slot_integrity"), tamperedBytes);

                SaveData loaded = null;
                AssertEx.WithFrameLogsOff(() =>
                {
                    Assert.IsTrue(service.TryLoad("slot_integrity", out loaded));
                });

                Assert.AreEqual("Old", loaded.Name);
                Assert.AreEqual(1, loaded.Level);
            }
        }

        [Test]
        public void SaveService_LoadsLegacyFilesWithoutMetadata()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                string path = service.GetPath("legacy");
                byte[] bytes = new NewtonsoftSaveSerializer().Serialize(new SaveData { Name = "Legacy", Level = 4 });
                File.WriteAllBytes(path, bytes);

                Assert.IsFalse(service.TryGetMetadata("legacy", out SaveMetadata metadata));
                Assert.IsNull(metadata);
                Assert.IsTrue(service.TryLoad("legacy", out SaveData loaded));
                Assert.AreEqual("Legacy", loaded.Name);
                Assert.AreEqual(4, loaded.Level);
            }
        }

        [Test]
        public void SaveService_AppliesRegisteredMigrationsAfterLoad()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                SaveService service = fixture.Initialize(new SaveService());
                service.Save("migrate", new SaveData { Name = "Player", Level = 1 }, dataVersion: 1);
                service.RegisterMigration(new SaveMigration<SaveData>(1, 2, data =>
                {
                    data.Name = data.Name + "_v2";
                    data.Level += 10;
                    return data;
                }));

                Assert.IsTrue(service.TryLoad("migrate", out SaveData loaded));
                Assert.AreEqual("Player_v2", loaded.Name);
                Assert.AreEqual(11, loaded.Level);

                service.ClearMigrations<SaveData>();
                Assert.IsTrue(service.TryLoad("migrate", out SaveData unmodified));
                Assert.AreEqual("Player", unmodified.Name);
                Assert.AreEqual(1, unmodified.Level);
            }
        }

        [Serializable]
        public sealed class SaveData
        {
            public string Name;
            public int Level;
        }

        [Serializable]
        public sealed class ComplexSaveData
        {
            public string Name;
            public List<string> Items = new List<string>();
            public Dictionary<string, int> Scores = new Dictionary<string, int>();
        }

        private sealed class MemorySerializer : ISaveSerializer
        {
            public int SerializeCount { get; private set; }
            public int DeserializeCount { get; private set; }

            public string FileExtension
            {
                get { return ".mem"; }
            }

            public byte[] Serialize<TData>(TData data)
            {
                SerializeCount++;
                SaveData saveData = data as SaveData;
                return Encoding.UTF8.GetBytes(saveData.Name + "|" + saveData.Level);
            }

            public TData Deserialize<TData>(byte[] bytes)
            {
                DeserializeCount++;
                string text = Encoding.UTF8.GetString(bytes);
                string[] parts = text.Split('|');
                object data = new SaveData { Name = parts[0], Level = int.Parse(parts[1]) };
                return (TData)data;
            }
        }
    }
}
