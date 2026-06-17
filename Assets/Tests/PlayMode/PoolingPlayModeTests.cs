using System.Collections;
using Frame.Pooling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class PoolingPlayModeTests
    {
        [UnityTest]
        public IEnumerator PoolService_GameObjectPoolsWorkInPlayMode()
        {
            GameObject prefab = new GameObject("PlayModePoolPrefab");
            prefab.AddComponent<TestPoolable>();

            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                PoolService service = fixture.Initialize(new PoolService());

                GameObjectPool pool = service.CreateGameObjectPool("playmode", prefab, maxSize: 2, prewarm: 1);
                Assert.AreEqual(1, pool.CountInactive);
                Assert.AreEqual(1, pool.GetStats("playmode").CreatedCount);
                Assert.IsTrue(service.TryGetGameObjectPool("playmode", out GameObjectPool resolved));
                Assert.AreSame(pool, resolved);
                Assert.AreSame(pool, service.CreateGameObjectPool("playmode", prefab));

                GameObject instance = service.Spawn("playmode");
                Assert.AreEqual(1, pool.CountActive);
                Assert.AreEqual(0, pool.CountInactive);
                TestPoolable poolable = instance.GetComponent<TestPoolable>();
                Assert.AreEqual(1, poolable.Spawned);

                service.Despawn("playmode", instance);
                Assert.AreEqual(0, pool.CountActive);
                Assert.AreEqual(1, pool.CountInactive);
                Assert.AreEqual(1, poolable.Despawned);
                PoolStats stats = service.GetGameObjectPoolStats("playmode");
                Assert.AreEqual("playmode", stats.Key);
                Assert.AreEqual(1, stats.GetCount);
                Assert.AreEqual(1, stats.ReleaseCount);
                Assert.AreEqual(1, service.GetAllGameObjectPoolStats().Count);

                service.ClearGameObjectPool("playmode");
                Assert.AreEqual(0, pool.CountInactive);
                Assert.AreEqual(1, pool.GetStats().DestroyedCount);

                GameObject unknown = new GameObject("UnknownPoolInstance");
                service.Despawn("missing", unknown);
                yield return null;
                Assert.IsTrue(unknown == null);

                Assert.Throws<Frame.Core.FrameException>(() => service.Spawn("missing"));
                service.Shutdown();
                Object.Destroy(prefab);
            }
        }

        private sealed class TestPoolable : MonoBehaviour, IPoolable
        {
            public int Spawned;
            public int Despawned;

            public void OnSpawned() { Spawned++; }

            public void OnDespawned() { Despawned++; }
        }
    }
}
