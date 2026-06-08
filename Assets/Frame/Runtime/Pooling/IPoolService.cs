using UnityEngine;

namespace Frame.Pooling
{
    public interface IPoolService
    {
        GameObjectPool CreateGameObjectPool(string key, GameObject prefab, int maxSize = -1, int prewarm = 0);

        bool TryGetGameObjectPool(string key, out GameObjectPool pool);

        GameObject Spawn(string key, Transform parent = null);

        void Despawn(string key, GameObject instance);
    }
}
