using System;
using System.Collections.Generic;
using System.IO;
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

                Assert.IsTrue(service.Exists("slot_1"));
                Assert.IsTrue(service.TryLoad("slot_1", out SaveData loaded));
                Assert.AreEqual("Player", loaded.Name);
                Assert.AreEqual(5, loaded.Level);
                Assert.AreEqual(5, service.Load<SaveData>("slot_1").Level);

                List<SaveSlotInfo> slots = service.ListSlots();
                Assert.AreEqual(1, slots.Count);
                Assert.AreEqual("slot_1", slots[0].Slot);
                Assert.Greater(slots[0].SizeBytes, 0);

                Assert.IsTrue(service.Delete("slot_1"));
                Assert.IsFalse(service.Exists("slot_1"));
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

            NewtonsoftSaveSerializer newtonsoft = new NewtonsoftSaveSerializer();
            SaveData newtonsoftLoaded = newtonsoft.Deserialize<SaveData>(newtonsoft.Serialize(data));
            Assert.AreEqual(data.Name, newtonsoftLoaded.Name);
            Assert.AreEqual(data.Level, newtonsoftLoaded.Level);

            JsonUtilitySaveSerializer unity = new JsonUtilitySaveSerializer();
            SaveData unityLoaded = unity.Deserialize<SaveData>(unity.Serialize(data));
            Assert.AreEqual(data.Name, unityLoaded.Name);
            Assert.AreEqual(data.Level, unityLoaded.Level);
        }

        [Serializable]
        public sealed class SaveData
        {
            public string Name;
            public int Level;
        }

        private sealed class MemorySerializer : ISaveSerializer
        {
            public int SerializeCount { get; private set; }
            public int DeserializeCount { get; private set; }

            public string Serialize<TData>(TData data)
            {
                SerializeCount++;
                SaveData saveData = data as SaveData;
                return saveData.Name + "|" + saveData.Level;
            }

            public TData Deserialize<TData>(string text)
            {
                DeserializeCount++;
                string[] parts = text.Split('|');
                object data = new SaveData { Name = parts[0], Level = int.Parse(parts[1]) };
                return (TData)data;
            }
        }
    }
}
