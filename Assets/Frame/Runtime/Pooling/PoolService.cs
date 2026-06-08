using System.Collections.Generic;
using Frame.Core;
using UnityEngine;

namespace Frame.Pooling
{
    public sealed class PoolService : GameModuleBase, IPoolService
    {
        private readonly Dictionary<string, GameObjectPool> gameObjectPools = new Dictionary<string, GameObjectPool>();
        private Transform poolRoot;

        public override int Priority
        {
            get { return -700; }
        }

        protected override void OnInitialize()
        {
            GameObject root = new GameObject("Pools");
            root.transform.SetParent(Context.Root, false);
            poolRoot = root.transform;
            Context.Services.Register<IPoolService>(this);
            Context.Services.Register(this);
        }

        public GameObjectPool CreateGameObjectPool(string key, GameObject prefab, int maxSize = -1, int prewarm = 0)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                key = prefab == null ? "Pool" : prefab.name;
            }

            if (prefab == null)
            {
                throw new FrameException("Prefab is required to create a GameObjectPool.");
            }

            GameObjectPool existing;
            if (gameObjectPools.TryGetValue(key, out existing))
            {
                return existing;
            }

            Transform parent = new GameObject(key).transform;
            parent.SetParent(poolRoot, false);
            int resolvedMaxSize = maxSize > 0 ? maxSize : Context.Settings.DefaultGameObjectPoolMaxSize;
            GameObjectPool pool = new GameObjectPool(prefab, parent, resolvedMaxSize);
            if (prewarm > 0)
            {
                pool.Prewarm(prewarm);
            }

            gameObjectPools.Add(key, pool);
            return pool;
        }

        public bool TryGetGameObjectPool(string key, out GameObjectPool pool)
        {
            return gameObjectPools.TryGetValue(key, out pool);
        }

        public GameObject Spawn(string key, Transform parent = null)
        {
            GameObjectPool pool;
            if (!gameObjectPools.TryGetValue(key, out pool))
            {
                throw new FrameException("GameObject pool is not registered: " + key);
            }

            return pool.Get(parent);
        }

        public void Despawn(string key, GameObject instance)
        {
            GameObjectPool pool;
            if (gameObjectPools.TryGetValue(key, out pool))
            {
                pool.Release(instance);
            }
            else if (instance != null)
            {
                Object.Destroy(instance);
            }
        }

        protected override void OnShutdown()
        {
            foreach (GameObjectPool pool in gameObjectPools.Values)
            {
                pool.Clear();
            }

            gameObjectPools.Clear();
            if (poolRoot != null)
            {
                Object.Destroy(poolRoot.gameObject);
            }

            poolRoot = null;
        }
    }
}
