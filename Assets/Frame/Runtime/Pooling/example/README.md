# Pooling 模块使用示例

Pooling 模块提供两类对象池：纯 C# `ObjectPool<T>` 和面向 prefab 的 `GameObjectPool`/`IPoolService`。

## 命名空间

```csharp
using Frame.Core;
using Frame.Pooling;
using UnityEngine;
```

## 纯 C# ObjectPool

适合复用列表、命令对象、临时消息对象等非 Unity 对象。

```csharp
public sealed class DamageTextData : IResettablePoolItem
{
    public int Value;
    public Vector3 Position;

    public void ResetForPool()
    {
        Value = 0;
        Position = Vector3.zero;
    }
}

ObjectPool<DamageTextData> pool = new ObjectPool<DamageTextData>(
    factory: () => new DamageTextData(),
    onGet: item => { },
    onRelease: item => { },
    onDestroy: item => { },
    maxSize: 128);

DamageTextData data = pool.Get();
data.Value = 100;
pool.Release(data);
```

`IResettablePoolItem.ResetForPool()` 会在 `Release` 时自动调用。

## 预热和清理 ObjectPool

```csharp
pool.Prewarm(32);

PoolStats stats = pool.GetStats("damage-text");
FrameLog.Info("active=" + stats.CountActive + " inactive=" + stats.CountInactive);

pool.Clear();
```

## 获取 PoolService

```csharp
IPoolService pools = Framework.Resolve<IPoolService>();
```

## 创建 GameObject 池

```csharp
[SerializeField] private GameObject bulletPrefab;

public void Initialize()
{
    IPoolService pools = Framework.Resolve<IPoolService>();
    pools.CreateGameObjectPool(
        key: "bullet",
        prefab: bulletPrefab,
        maxSize: 128,
        prewarm: 32);
}
```

`maxSize <= 0` 时使用 `FrameSettings.DefaultGameObjectPoolMaxSize`。

## 生成和回收 GameObject

```csharp
GameObject bullet = pools.Spawn("bullet", parent: null);
bullet.transform.position = muzzle.position;

pools.Despawn("bullet", bullet);
```

如果 `Despawn` 时 key 不存在，服务会直接销毁实例。

## 使用 IPoolable 接收生命周期

```csharp
public sealed class BulletView : MonoBehaviour, IPoolable
{
    public void OnSpawned()
    {
        gameObject.SetActive(true);
    }

    public void OnDespawned()
    {
        // 停止粒子、清理 trail、重置速度等。
    }
}
```

`GameObjectPool` 会在 `Get` 时调用子节点所有 `IPoolable.OnSpawned()`，在 `Release` 时调用 `OnDespawned()`。

## 直接使用 GameObjectPool

```csharp
GameObjectPool pool = pools.CreateGameObjectPool("enemy", enemyPrefab, maxSize: 64, prewarm: 8);

GameObject enemy = pool.Get(parent);
pool.Release(enemy);
```

## 查询池和统计

```csharp
if (pools.TryGetGameObjectPool("bullet", out GameObjectPool bulletPool))
{
    PoolStats bulletStats = bulletPool.GetStats("bullet");
}

PoolStats stats = pools.GetGameObjectPoolStats("bullet");
List<PoolStats> allStats = pools.GetAllGameObjectPoolStats();
```

`PoolStats` 字段：

- `Key`
- `MaxSize`
- `CountActive`
- `CountInactive`
- `CountTotal`
- `CreatedCount`
- `DestroyedCount`
- `GetCount`
- `ReleaseCount`

## 清理池

```csharp
pools.ClearGameObjectPool("bullet");
pools.ClearAllGameObjectPools();
```

`Clear` 只销毁池内 inactive 对象，不会强制销毁已经 spawn 出去的 active 对象。

## 注意事项

- 同一个实例重复 `Release` 会被忽略，避免重复入池。
- 池满时回收对象会被销毁。
- `ObjectPool<T>` 不是线程安全容器，建议只在主线程或单线程逻辑中使用。
- `GameObjectPool` 默认把 inactive 实例挂到池根节点下，方便层级管理。
