using System;
using System.Collections.Generic;

namespace Frame.Pooling
{
    public sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> factory;
        private readonly Action<T> onGet;
        private readonly Action<T> onRelease;
        private readonly Action<T> onDestroy;
        private readonly Stack<T> inactive = new Stack<T>();
        private readonly HashSet<T> inPool = new HashSet<T>();
        private int countActive;
        private int createdCount;
        private int destroyedCount;
        private int getCount;
        private int releaseCount;

        public ObjectPool(Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null, int maxSize = 128)
        {
            if (factory == null)
            {
                throw new ArgumentNullException("factory");
            }

            this.factory = factory;
            this.onGet = onGet;
            this.onRelease = onRelease;
            this.onDestroy = onDestroy;
            MaxSize = Math.Max(1, maxSize);
        }

        public int MaxSize
        {
            get;
            private set;
        }

        public int CountInactive
        {
            get { return inactive.Count; }
        }

        public int CountActive
        {
            get { return countActive; }
        }

        public T Get()
        {
            T item;
            if (inactive.Count > 0)
            {
                item = inactive.Pop();
                inPool.Remove(item);
            }
            else
            {
                item = factory();
                createdCount++;
            }

            countActive++;
            getCount++;
            if (onGet != null)
            {
                onGet(item);
            }

            return item;
        }

        public void Release(T item)
        {
            if (item == null || inPool.Contains(item))
            {
                return;
            }

            IResettablePoolItem resettable = item as IResettablePoolItem;
            if (resettable != null)
            {
                resettable.ResetForPool();
            }

            if (countActive > 0)
            {
                countActive--;
            }

            releaseCount++;
            if (onRelease != null)
            {
                onRelease(item);
            }

            if (inactive.Count >= MaxSize)
            {
                if (onDestroy != null)
                {
                    onDestroy(item);
                }

                destroyedCount++;
                return;
            }

            inactive.Push(item);
            inPool.Add(item);
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                T item = factory();
                createdCount++;
                Release(item);
            }
        }

        public PoolStats GetStats(string key = null)
        {
            return new PoolStats
            {
                Key = key,
                MaxSize = MaxSize,
                CountActive = countActive,
                CountInactive = CountInactive,
                CountTotal = countActive + CountInactive,
                CreatedCount = createdCount,
                DestroyedCount = destroyedCount,
                GetCount = getCount,
                ReleaseCount = releaseCount
            };
        }

        public void Clear()
        {
            if (onDestroy != null)
            {
                while (inactive.Count > 0)
                {
                    onDestroy(inactive.Pop());
                    destroyedCount++;
                }
            }
            else
            {
                destroyedCount += inactive.Count;
            }

            inactive.Clear();
            inPool.Clear();
        }
    }
}
