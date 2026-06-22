# Scenes 模块使用示例

Scenes 模块封装 Unity `SceneManager`，提供同步加载、异步加载、进度事件、手动激活、卸载、Build Settings 校验和设置 Active Scene。

## 命名空间

```csharp
using Frame.Core;
using Frame.Scenes;
using UnityEngine.SceneManagement;
```

## 获取服务

```csharp
ISceneService scenes = Framework.Resolve<ISceneService>();
```

## 同步加载

```csharp
scenes.Load("Battle", LoadSceneMode.Single);
```

跳过 Build Settings 校验：

```csharp
scenes.Load("Development/TestScene", LoadSceneMode.Single, validateInBuildSettings: false);
```

## 异步加载

```csharp
SceneLoadOperation operation = scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "Battle",
    Mode = LoadSceneMode.Single,
    ActivateOnLoad = true,
    ValidateInBuildSettings = true,
    Progress = progress => loadingBar.value = progress,
    Completed = scene => FrameLog.Info("loaded scene: " + scene.name)
});
```

协程等待：

```csharp
private IEnumerator LoadBattle()
{
    ISceneService scenes = Framework.Resolve<ISceneService>();
    SceneLoadOperation operation = scenes.LoadAsync(new SceneLoadArgs
    {
        SceneName = "Battle"
    });

    yield return operation;

    FrameLog.Info("done=" + operation.IsDone);
}
```

## 手动激活场景

```csharp
SceneLoadOperation operation = scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "Battle",
    ActivateOnLoad = false,
    Progress = progress => loadingBar.value = progress
});

// 当 NormalizedProgress 到 1 且 IsReadyToActivate 为 true 时，可以展示“点击继续”。
if (operation.IsReadyToActivate)
{
    operation.Activate();
}
```

也可以直接设置：

```csharp
operation.AllowSceneActivation = true;
```

## Additive 加载和卸载

```csharp
scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "DungeonRoom",
    Mode = LoadSceneMode.Additive,
    SetActiveOnComplete = true
});

AsyncOperation unload = scenes.UnloadAsync("DungeonRoom");
```

## 并发加载控制

默认 `AllowConcurrentLoads = false`，已有加载进行中时再次加载会抛 `FrameException`。

```csharp
scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "SceneA",
    AllowConcurrentLoads = true
});

scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "SceneB",
    AllowConcurrentLoads = true
});
```

## 监听全局加载事件

```csharp
scenes.LoadStarted += op => FrameLog.Info("start " + op.SceneName);
scenes.LoadProgress += (op, progress) => loadingBar.value = progress;
scenes.LoadCompleted += op => FrameLog.Info("complete " + op.SceneName);
```

## 查询状态

```csharp
bool isLoading = scenes.IsLoading;
Scene active = scenes.ActiveScene;
SceneLoadOperation current = scenes.CurrentOperation;

bool loaded = scenes.IsSceneLoaded("Battle");
bool inBuild = scenes.IsSceneInBuildSettings("Battle");
bool activeSet = scenes.SetActiveScene("Battle");
```

## SceneLoadOperation 属性

- `SceneName`
- `Progress`: Unity 原始进度，手动激活时通常停在 `0.9`。
- `NormalizedProgress`: 归一化进度，手动激活前可到 `1`。
- `IsDone`
- `IsReadyToActivate`
- `AllowSceneActivation`
- `LoadedScene`
- `Operation`

## 注意事项

- 默认会校验场景是否在 Build Settings 中，避免运行时加载不存在的场景。
- `LoadSceneMode.Single` 会卸载其他场景，`Additive` 需要手动卸载。
- `SetActiveOnComplete` 只在加载完成且场景有效时设置 Active Scene。
- `UnloadAsync` 对空名称或未加载场景返回 `null`。
