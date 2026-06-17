using System;
using Frame.Preferences;
using NUnit.Framework;
using UnityEngine;

namespace Frame.Tests.EditMode
{
    public sealed class PreferencesModuleTests
    {
        private const string Prefix = "Frame.Tests.Preferences.";

        [TearDown]
        public void Cleanup()
        {
            Delete(Prefix + "int");
            Delete(Prefix + "float");
            Delete(Prefix + "string");
            Delete(Prefix + "bool");
            Delete(Prefix + "json");
            Delete(Prefix + "bad-json");
        }

        [Test]
        public void PreferencesService_ReadsWritesDeletesAndRaisesChanged()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                PreferencesService service = fixture.Initialize(new PreferencesService());
                int changedCount = 0;
                string lastKey = null;
                service.Changed += key =>
                {
                    changedCount++;
                    lastKey = key;
                };

                service.SetInt(Prefix + "int", 12);
                Assert.AreEqual(12, service.GetInt(Prefix + "int"));

                service.SetFloat(Prefix + "float", 0.75f);
                Assert.AreEqual(0.75f, service.GetFloat(Prefix + "float"), 0.001f);

                service.SetString(Prefix + "string", "player");
                Assert.AreEqual("player", service.GetString(Prefix + "string"));

                service.SetBool(Prefix + "bool", true);
                Assert.IsTrue(service.GetBool(Prefix + "bool"));

                Assert.IsTrue(service.HasKey(Prefix + "bool"));
                Assert.IsTrue(service.DeleteKey(Prefix + "bool"));
                Assert.IsFalse(service.DeleteKey(Prefix + "bool"));
                Assert.IsFalse(service.GetBool(Prefix + "bool"));
                Assert.AreEqual(Prefix + "bool", lastKey);
                Assert.GreaterOrEqual(changedCount, 5);
            }
        }

        [Test]
        public void PreferencesService_SerializesJsonValuesAndHandlesFallbacks()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                PreferencesService service = fixture.Initialize(new PreferencesService());
                PreferenceData data = new PreferenceData { Name = "Player", Level = 5 };

                service.SetJson(Prefix + "json", data);

                Assert.IsTrue(service.TryGetJson(Prefix + "json", out PreferenceData loaded));
                Assert.AreEqual("Player", loaded.Name);
                Assert.AreEqual(5, loaded.Level);

                PreferenceData fallback = new PreferenceData { Name = "Fallback", Level = -1 };
                Assert.AreSame(fallback, service.GetJson(Prefix + "missing", fallback));

                PlayerPrefs.SetString(Prefix + "bad-json", "{ invalid json");
                AssertEx.WithFrameLogsOff(() =>
                {
                    Assert.IsFalse(service.TryGetJson(Prefix + "bad-json", out loaded));
                });
            }
        }

        [Test]
        public void PreferencesService_RejectsEmptyKeys()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                PreferencesService service = fixture.Initialize(new PreferencesService());

                Assert.Throws<ArgumentException>(() => service.SetString("", "value"));
                Assert.Throws<ArgumentException>(() => service.SetJson("", new PreferenceData()));
            }
        }

        private static void Delete(string key)
        {
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        [Serializable]
        private sealed class PreferenceData
        {
            public string Name;
            public int Level;
        }
    }
}
