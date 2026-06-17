using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Pooling
{
    public sealed class GameObjectPool
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly int maxSize;
        private readonly Stack<GameObject> inactive = new Stack<GameObject>();
        private readonly HashSet<GameObject> inPool = new HashSet<GameObject>();
        private int countActive;
        private int createdCount;
        private int destroyedCount;
        private int getCount;
        private int releaseCount;

        public GameObjectPool(GameObject prefab, Transform parent, int maxSize)
        {
            this.prefab = prefab;
            this.parent = parent;
            this.maxSize = Mathf.Max(1, maxSize);
        }

        public int CountInactive
        {
            get { return inactive.Count; }
        }

        public int CountActive
        {
            get { return countActive; }
        }

        public GameObject Get(Transform newParent = null)
        {
            GameObject instance;
            if (inactive.Count > 0)
            {
                instance = inactive.Pop();
            }
            else
            {
                instance = Object.Instantiate(prefab);
                createdCount++;
            }

            inPool.Remove(instance);
            countActive++;
            getCount++;
            instance.transform.SetParent(newParent == null ? parent : newParent, false);
            instance.SetActive(true);

            IPoolable[] poolables = instance.GetComponentsInChildren<IPoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i].OnSpawned();
            }

            return instance;
        }

        public void Release(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (inPool.Contains(instance))
            {
                return;
            }

            if (countActive > 0)
            {
                countActive--;
            }

            releaseCount++;
            IPoolable[] poolables = instance.GetComponentsInChildren<IPoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i].OnDespawned();
            }

            if (inactive.Count >= maxSize)
            {
                Object.Destroy(instance);
                destroyedCount++;
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(parent, false);
            inactive.Push(instance);
            inPool.Add(instance);
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject instance = Object.Instantiate(prefab, parent, false);
                createdCount++;
                instance.SetActive(false);
                inactive.Push(instance);
                inPool.Add(instance);
            }
        }

        public void Clear()
        {
            while (inactive.Count > 0)
            {
                GameObject instance = inactive.Pop();
                if (instance != null)
                {
                    Object.Destroy(instance);
                    destroyedCount++;
                }
            }

            inPool.Clear();
        }

        public PoolStats GetStats(string key = null)
        {
            return new PoolStats
            {
                Key = key,
                MaxSize = maxSize,
                CountActive = countActive,
                CountInactive = CountInactive,
                CountTotal = countActive + CountInactive,
                CreatedCount = createdCount,
                DestroyedCount = destroyedCount,
                GetCount = getCount,
                ReleaseCount = releaseCount
            };
        }
    }
}
