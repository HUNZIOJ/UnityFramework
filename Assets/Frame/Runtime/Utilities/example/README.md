# Utilities 模块使用示例

Utilities 模块提供通用小工具，目前包含 `DisposableAction` 和 `FramePathUtility`。

## 命名空间

```csharp
using Frame.Utilities;
```

## DisposableAction

`DisposableAction` 把一个清理动作包装成 `IDisposable`，适合实现作用域恢复、事件解绑和临时状态撤销。

```csharp
IDisposable scope = new DisposableAction(() =>
{
    FrameLog.Info("disposed once");
});

scope.Dispose();
scope.Dispose(); // 第二次不会重复执行
```

典型用法：打开 UI 时切换输入上下文，关闭时恢复。

```csharp
public IDisposable EnterUiInputScope()
{
    IInputService input = Framework.Resolve<IInputService>();
    InputContext previous = input.CurrentContext;
    input.SetContext(InputContext.UI);

    return new DisposableAction(() => input.SetContext(previous));
}
```

## NormalizeResourcesPath

`FramePathUtility.NormalizeResourcesPath` 用于把 Unity 资源路径标准化成 Resources key。

```csharp
string a = FramePathUtility.NormalizeResourcesPath("Assets/Game/Resources/UI/MainMenu.prefab");
string b = FramePathUtility.NormalizeResourcesPath("UI\\MainMenu.prefab");
string c = FramePathUtility.NormalizeResourcesPath("UI/MainMenu");

// a == b == c == "UI/MainMenu"
```

规则：

- 空字符串返回 `string.Empty`。
- 反斜杠转成 `/`。
- 去掉扩展名。
- 如果包含 `/Resources/`，截取它后面的部分。
- trim 前后空白。

## SanitizeFileName

`FramePathUtility.SanitizeFileName` 会把非法文件名字符替换为 `_`。

```csharp
string safe = FramePathUtility.SanitizeFileName("slot:1/player");
string fallback = FramePathUtility.SanitizeFileName("");
```

空文件名返回 `"default"`。

## 注意事项

- `NormalizeResourcesPath` 只适合 Resources 风格路径，不要用它处理 URL。
- Addressables 和 YooAsset 地址可能有自己的命名规则，是否标准化取决于对应集成服务。
- `DisposableAction` 不捕获异常，清理动作抛异常会传给调用方。
