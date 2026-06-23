using System;
using System.Collections.Generic;
using Frame.Assets;
using Frame.Config;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Frame.Tests.EditMode
{
    public sealed class ConfigModuleTests
    {
        [Test]
        public void AssetJsonConfigProvider_LoadsJsonThroughAssetService()
        {
            MemoryAssetService assets = new MemoryAssetService();
            assets.Register("Configs/FrameTests/item", new TextAsset("{\"Id\":\"item_001\",\"Name\":\"Iron Sword\",\"Power\":12}"));
            AssetJsonConfigProvider provider = new AssetJsonConfigProvider(assets, "Configs");

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
                MemoryConfigProvider provider = new MemoryConfigProvider(new ItemConfig
                {
                    Id = "memory",
                    Name = "Memory Item",
                    Power = 99
                });
                service.RegisterProvider(provider);

                ItemConfig config = service.Load<ItemConfig>("any");

                Assert.AreEqual("memory", config.Id);
                Assert.IsTrue(service.UnregisterProvider(provider));
                Assert.IsFalse(service.UnregisterProvider(provider));
            }
        }

        [Test]
        public void ConfigService_CachesLoadedConfigsAndCanClearCache()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                ConfigService service = fixture.Initialize(new ConfigService());
                MemoryConfigProvider provider = new MemoryConfigProvider(new ItemConfig
                {
                    Id = "cached",
                    Name = "Cached Item",
                    Power = 10
                });
                service.RegisterProvider(provider);

                Assert.IsTrue(service.TryLoad("cached", out ItemConfig first));
                Assert.IsTrue(service.TryLoad("cached", out ItemConfig second));
                Assert.AreSame(first, second);
                Assert.AreEqual(1, provider.LoadCount);

                service.ClearCache();
                Assert.IsTrue(service.TryLoad("cached", out ItemConfig third));
                Assert.AreSame(first, third);
                Assert.AreEqual(2, provider.LoadCount);

                service.CacheEnabled = false;
                ItemConfig ignored;
                Assert.IsTrue(service.TryLoad("cached", out ignored));
                Assert.IsTrue(service.TryLoad("cached", out ignored));
                Assert.AreEqual(4, provider.LoadCount);
            }
        }

        [Test]
        public void ConfigService_RejectsInvalidValidatedConfig()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                ConfigService service = fixture.Initialize(new ConfigService());
                service.RegisterProvider(new MemoryConfigProvider(new ValidatedConfig()));

                LogAssert.Expect(LogType.Warning, "[Frame] Config validation failed: invalid type=ValidatedConfig error=Id is required.");
                Assert.IsFalse(service.TryLoad("invalid", out ValidatedConfig config));
                Assert.IsNull(config);
            }
        }

        [Test]
        public void RuntimeJsonConfigProvider_OverridesAssetsAndInvalidatesCache()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                MemoryAssetService assets = new MemoryAssetService();
                assets.Register("Configs/FrameTests/item", new TextAsset("{\"Id\":\"item_001\",\"Name\":\"Iron Sword\",\"Power\":12}"));
                fixture.Services.Register<IAssetService>(assets);
                ConfigService service = fixture.Initialize(new ConfigService());
                RuntimeJsonConfigProvider provider = new RuntimeJsonConfigProvider();
                provider.SetJson("FrameTests/item.json", "{\"Id\":\"remote_001\",\"Name\":\"Remote Sword\",\"Power\":20}");
                service.RegisterProvider(provider);

                Assert.IsTrue(provider.Contains("FrameTests/item"));
                Assert.IsTrue(service.TryLoad("FrameTests/item", out ItemConfig first));
                Assert.AreEqual("remote_001", first.Id);
                Assert.AreEqual(20, first.Power);

                provider.Set("FrameTests/item", new ItemConfig
                {
                    Id = "remote_002",
                    Name = "Remote Sword V2",
                    Power = 30
                });

                Assert.IsTrue(service.TryLoad("FrameTests/item", out ItemConfig second));
                Assert.AreEqual("remote_002", second.Id);
                Assert.AreEqual(30, second.Power);

                Assert.IsTrue(provider.Remove("FrameTests/item"));
                Assert.IsTrue(service.TryLoad("FrameTests/item", out ItemConfig fallback));
                Assert.AreEqual("item_001", fallback.Id);
                Assert.AreEqual(12, fallback.Power);
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
        public void AssetScriptableConfigProvider_LoadsScriptableConfigsThroughAssetService()
        {
            TestScriptableConfig config = ScriptableObject.CreateInstance<TestScriptableConfig>();
            config.name = "resource_scriptable";
            try
            {
                MemoryAssetService assets = new MemoryAssetService();
                assets.Register("Configs/FrameTests/resource_scriptable", config);
                AssetScriptableConfigProvider provider = new AssetScriptableConfigProvider(assets, "Configs");

                Assert.IsTrue(provider.TryLoad("FrameTests/resource_scriptable", out TestScriptableConfig loaded));
                Assert.AreEqual("resource_scriptable", loaded.Id);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
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

        public sealed class ValidatedConfig : IConfigValidator
        {
            public string Id;

            public bool Validate(out string error)
            {
                if (string.IsNullOrWhiteSpace(Id))
                {
                    error = "Id is required.";
                    return false;
                }

                error = null;
                return true;
            }
        }

        private sealed class MemoryConfigProvider : IConfigProvider
        {
            private readonly object config;

            public MemoryConfigProvider(object config)
            {
                this.config = config;
            }

            public int LoadCount { get; private set; }

            public bool TryLoad<TConfig>(string key, out TConfig loaded) where TConfig : class
            {
                LoadCount++;
                loaded = config as TConfig;
                return loaded != null;
            }
        }

        private sealed class MemoryAssetService : IAssetService
        {
            private readonly Dictionary<string, Object> assets = new Dictionary<string, Object>(StringComparer.Ordinal);

            public void Register(string path, Object asset)
            {
                assets[path] = asset;
            }

            public AssetHandle<T> Load<T>(string path) where T : Object
            {
                AssetHandle<T> handle;
                TryLoad(path, out handle);
                return handle;
            }

            public bool TryLoad<T>(string path, out AssetHandle<T> handle) where T : Object
            {
                Object asset;
                if (assets.TryGetValue(path, out asset) && asset is T typed)
                {
                    handle = new AssetHandle<T>(this, path, typed);
                    return true;
                }

                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object
            {
                return null;
            }

            public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)
            {
                return null;
            }

            public bool IsLoaded(string path)
            {
                return assets.ContainsKey(path);
            }

            public int GetReferenceCount(string path)
            {
                return 0;
            }

            public List<AssetStats> GetLoadedAssetStats()
            {
                return new List<AssetStats>();
            }

            public void Release(string path)
            {
            }

            public void ReleaseAll()
            {
                assets.Clear();
            }

            public void UnloadUnusedAssets()
            {
            }
        }
    }
}
