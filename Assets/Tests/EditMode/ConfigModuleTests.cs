using Frame.Config;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.EditMode
{
    public sealed class ConfigModuleTests
    {
        [Test]
        public void ResourcesJsonConfigProvider_LoadsJsonFromResources()
        {
            ResourcesJsonConfigProvider provider = new ResourcesJsonConfigProvider("Configs");

            Assert.IsTrue(provider.TryLoad("FrameTests/item", out ItemConfig config));
            Assert.AreEqual("item_001", config.Id);
            Assert.AreEqual("Iron Sword", config.Name);
            Assert.AreEqual(12, config.Power);
        }

        [Test]
        public void ConfigService_UsesRegisteredProviderBeforeDefaultProvider()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                ConfigService service = fixture.Initialize(new ConfigService());
                service.RegisterProvider(new MemoryConfigProvider(new ItemConfig
                {
                    Id = "memory",
                    Name = "Memory Item",
                    Power = 99
                }));

                ItemConfig config = service.Load<ItemConfig>("any");

                Assert.AreEqual("memory", config.Id);
            }
        }

        [Test]
        public void ScriptableConfigProvider_RegistersAndLoadsScriptableConfigs()
        {
            TestScriptableConfig config = ScriptableObject.CreateInstance<TestScriptableConfig>();
            config.name = "scriptable_id";

            try
            {
                ScriptableConfigProvider provider = new ScriptableConfigProvider();
                provider.Register(config);

                Assert.IsTrue(provider.TryLoad("scriptable_id", out TestScriptableConfig loaded));
                Assert.AreSame(config, loaded);

                provider.Clear();
                Assert.IsFalse(provider.TryLoad("scriptable_id", out loaded));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ResourcesScriptableConfigProvider_LoadsScriptableConfigsFromResources()
        {
            ResourcesScriptableConfigProvider provider = new ResourcesScriptableConfigProvider("Configs");

            Assert.IsTrue(provider.TryLoad("resource_scriptable", out TestScriptableConfig loaded));
            Assert.AreEqual("resource_scriptable", loaded.Id);
        }

        [Test]
        public void ConfigService_ReturnsNullWhenConfigIsMissing()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                ConfigService service = fixture.Initialize(new ConfigService());

                Assert.IsFalse(service.TryLoad("missing_config", out ItemConfig config));
                Assert.IsNull(config);
                LogAssert.Expect(LogType.Warning, "[Frame] Config not found: missing_config type=ItemConfig");
                Assert.IsNull(service.Load<ItemConfig>("missing_config"));
            }
        }

        public sealed class ItemConfig
        {
            public string Id;
            public string Name;
            public int Power;
        }

        private sealed class MemoryConfigProvider : IConfigProvider
        {
            private readonly ItemConfig config;

            public MemoryConfigProvider(ItemConfig config)
            {
                this.config = config;
            }

            public bool TryLoad<TConfig>(string key, out TConfig loaded) where TConfig : class
            {
                loaded = config as TConfig;
                return loaded != null;
            }
        }

    }
}
