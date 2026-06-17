using Frame.Pooling;
using NUnit.Framework;

namespace Frame.Tests.EditMode
{
    public sealed class PoolingModuleTests
    {
        [Test]
        public void ObjectPool_GetReleasePrewarmAndClearManageInactiveItems()
        {
            int created = 0;
            int got = 0;
            int released = 0;
            int destroyed = 0;

            ObjectPool<PooledItem> pool = new ObjectPool<PooledItem>(
                () => new PooledItem(++created),
                _ => got++,
                _ => released++,
                _ => destroyed++,
                maxSize: 2);

            pool.Prewarm(2);
            Assert.AreEqual(2, pool.CountInactive);
            Assert.AreEqual(2, released);
            PoolStats stats = pool.GetStats("items");
            Assert.AreEqual("items", stats.Key);
            Assert.AreEqual(0, stats.CountActive);
            Assert.AreEqual(2, stats.CountInactive);
            Assert.AreEqual(2, stats.CreatedCount);
            Assert.AreEqual(2, stats.ReleaseCount);

            PooledItem itemA = pool.Get();
            PooledItem itemB = pool.Get();
            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(2, pool.CountActive);
            Assert.AreEqual(2, got);

            pool.Release(itemA);
            pool.Release(itemA);
            pool.Release(itemB);
            pool.Release(new PooledItem(100));

            Assert.AreEqual(2, pool.CountInactive);
            Assert.AreEqual(5, released);
            Assert.AreEqual(2, itemA.ResetCount);
            Assert.AreEqual(1, destroyed);
            stats = pool.GetStats("items");
            Assert.AreEqual(0, stats.CountActive);
            Assert.AreEqual(2, stats.CountInactive);
            Assert.AreEqual(2, stats.GetCount);
            Assert.AreEqual(5, stats.ReleaseCount);
            Assert.AreEqual(1, stats.DestroyedCount);

            pool.Clear();
            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(3, destroyed);
            Assert.AreEqual(3, pool.GetStats().DestroyedCount);
        }

        private sealed class PooledItem : IResettablePoolItem
        {
            public PooledItem(int id)
            {
                Id = id;
            }

            public int Id { get; private set; }

            public int ResetCount { get; private set; }

            public void ResetForPool()
            {
                ResetCount++;
            }
        }

    }
}
