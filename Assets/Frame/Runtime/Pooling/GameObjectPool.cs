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

        public GameObject Get(Transform newParent = null)
        {
            GameObject instance = inactive.Count > 0 ? inactive.Pop() : Object.Instantiate(prefab);
            inPool.Remove(instance);
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

            IPoolable[] poolables = instance.GetComponentsInChildren<IPoolable>(true);
            for (int i = 0; i < poolables.Length; i++)
            {
                poolables[i].OnDespawned();
            }

            if (inactive.Count >= maxSize)
            {
                Object.Destroy(instance);
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
                }
            }

            inPool.Clear();
        }
    }
}
