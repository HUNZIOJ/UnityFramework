# Config 模块使用示例

Config 模块通过 `IConfigService` 统一读取 JSON、ScriptableObject 和运行时覆盖配置。默认会尝试从 `IAssetService` 的 `Configs` 目录读取。

## 命名空间

```csharp
using Frame.Config;
using Frame.Core;
```

## 获取服务

```csharp
IConfigService configs = Framework.Resolve<IConfigService>();
```

## 从 Resources/Configs 读取 JSON

资源路径：

```text
Assets/Game/Resources/Configs/player.json
```

数据类型：

```csharp
public sealed class PlayerConfig
{
    public int MaxLevel;
    public int InitialGold;
}
```

读取：

```csharp
PlayerConfig config = configs.Load<PlayerConfig>("player");
if (config != null)
{
    FrameLog.Info("max level=" + config.MaxLevel);
}
```

不打印缺失警告：

```csharp
if (configs.TryLoad<PlayerConfig>("player", out PlayerConfig player))
{
    Use(player);
}
```

## 从 ScriptableObject 读取

定义配置类型：

```csharp
using Frame.Config;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Enemy Config")]
public sealed class EnemyConfig : ScriptableConfig
{
    public int Hp;
    public float Speed;
}
```

把资源放到：

```text
Assets/Game/Resources/Configs/enemy.asset
```

读取：

```csharp
EnemyConfig enemy = configs.Load<EnemyConfig>("enemy");
```

`ScriptableConfig.Id` 如果为空，使用资产名作为 id。

## 注册运行时 JSON 覆盖

`RegisterProvider` 会把新 Provider 插到最前面，优先级高于默认资源 Provider。

```csharp
RuntimeJsonConfigProvider runtimeProvider = new RuntimeJsonConfigProvider();
runtimeProvider.Set("player", new PlayerConfig
{
    MaxLevel = 80,
    InitialGold = 1000
});

configs.RegisterProvider(runtimeProvider);

PlayerConfig player = configs.Load<PlayerConfig>("player");
```

更新 JSON：

```csharp
runtimeProvider.SetJson("player", "{\"MaxLevel\":90,\"InitialGold\":2000}");
```

删除覆盖：

```csharp
runtimeProvider.Remove("player");
```

清空覆盖：

```csharp
runtimeProvider.Clear();
```

`RuntimeJsonConfigProvider` 实现了 `IConfigChangeNotifier`，变更时 `ConfigService` 会清空缓存。

## 手动注册 ScriptableConfigProvider

适合从 Addressables、YooAsset、编辑器工具或测试代码中拿到配置对象后手动注册。

```csharp
ScriptableConfigProvider provider = new ScriptableConfigProvider();
provider.Register(enemyConfigAsset);

configs.RegisterProvider(provider);
EnemyConfig enemy = configs.Load<EnemyConfig>(enemyConfigAsset.Id);
```

## 自定义 Provider

```csharp
public sealed class RemoteConfigProvider : IConfigProvider
{
    public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
    {
        config = null;

        string json = TryGetRemoteJson(key);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        config = Newtonsoft.Json.JsonConvert.DeserializeObject<TConfig>(json);
        return config != null;
    }
}
```

注册和卸载：

```csharp
RemoteConfigProvider provider = new RemoteConfigProvider();
configs.RegisterProvider(provider);

bool removed = configs.UnregisterProvider(provider);
```

## 配置校验

配置类型实现 `IConfigValidator` 后，加载成功后会自动校验。

```csharp
public sealed class ShopConfig : IConfigValidator
{
    public int RefreshSeconds;

    public bool Validate(out string error)
    {
        if (RefreshSeconds <= 0)
        {
            error = "RefreshSeconds must be greater than zero.";
            return false;
        }

        error = null;
        return true;
    }
}
```

校验失败时 `TryLoad` 返回 `false`，`Load` 返回 `null` 并写 warning。

## 缓存控制

```csharp
configs.CacheEnabled = true;
configs.ClearCache();

configs.CacheEnabled = false;
```

缓存 key 由配置类型和配置 key 组成。注册或移除 Provider 时会自动清缓存。

## 默认 Provider 顺序

`ConfigService` 初始化时，如果存在 `IAssetService`，会注册：

1. `AssetScriptableConfigProvider`
2. `AssetJsonConfigProvider`

之后通过 `RegisterProvider` 添加的 Provider 会插到最前面，优先级最高。

## 注意事项

- JSON 配置默认使用 Newtonsoft.Json 反序列化。
- key 会按资源路径规则标准化，不带 `.json` 或 `.asset` 扩展名。
- 配置对象建议只作为只读数据使用，运行时状态不要写回配置对象。
- 热更新配置时，推荐用 `RuntimeJsonConfigProvider` 做覆盖层，而不是替换默认资源 Provider。
