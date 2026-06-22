# Diagnostics 模块使用示例

Diagnostics 模块收集框架日志、运行时快照、FPS/内存计数，并可以把日志写入文件。它也支持运行时 IMGUI 诊断面板。

## 命名空间

```csharp
using Frame.Core;
using Frame.Diagnostics;
```

## 获取服务

```csharp
IDiagnosticsService diagnostics = Framework.Resolve<IDiagnosticsService>();
```

## 写入框架日志

日志入口在 Core 模块的 `FrameLog`：

```csharp
FrameLog.Trace("trace message");
FrameLog.Debug("debug message");
FrameLog.Info("info message");
FrameLog.Warning("warning message");
FrameLog.Error("error message");
```

异常日志：

```csharp
try
{
    RiskyCall();
}
catch (Exception exception)
{
    FrameLog.Exception(exception);
}
```

## 监听日志

```csharp
private IDiagnosticsService diagnostics;

public void Open()
{
    diagnostics = Framework.Resolve<IDiagnosticsService>();
    diagnostics.LogReceived += OnLogReceived;
}

public void Close()
{
    if (diagnostics != null)
    {
        diagnostics.LogReceived -= OnLogReceived;
    }
}

private void OnLogReceived(FrameLogEntry entry)
{
    UnityEngine.Debug.Log(entry.UtcTime + " [" + entry.Level + "] " + entry.Message);
}
```

## 读取日志缓冲

```csharp
IReadOnlyList<FrameLogEntry> logs = diagnostics.Logs;
for (int i = 0; i < logs.Count; i++)
{
    FrameLogEntry entry = logs[i];
    UnityEngine.Debug.Log(entry.FormattedMessage);
}
```

清空缓冲：

```csharp
diagnostics.ClearLogs();
```

## 捕获运行时快照

```csharp
DiagnosticsSnapshot snapshot = diagnostics.CaptureSnapshot();

FrameLog.Info("fps=" + snapshot.AverageFps);
FrameLog.Info("managed memory=" + snapshot.ManagedMemoryBytes);
FrameLog.Info("allocated memory=" + snapshot.TotalAllocatedMemoryBytes);
FrameLog.Info("warnings=" + snapshot.WarningCount);
FrameLog.Info("errors=" + snapshot.ErrorCount);
```

可用字段：

- `FrameCount`
- `RealtimeSinceStartup`
- `UnscaledTime`
- `DeltaTime`
- `AverageFps`
- `ManagedMemoryBytes`
- `TotalAllocatedMemoryBytes`
- `BufferedLogCount`
- `WarningCount`
- `ErrorCount`
- `ExceptionCount`

## 写日志到文件

```csharp
IDisposable fileSink = diagnostics.WriteLogsToFile(
    filePath: System.IO.Path.Combine(Application.persistentDataPath, "frame.log"),
    maxBytes: 1024 * 1024);

FrameLog.Info("this line is written to file");

fileSink.Dispose();
```

建议在调试页面打开文件写入，在页面关闭或登出时释放返回的 `IDisposable`。

## 启用运行时诊断面板

方式一，在 `FrameSettings` 中启用：

- `EnableRuntimeDiagnosticsOverlay`
- `RuntimeDiagnosticsOverlayVisibleOnStart`
- `RuntimeDiagnosticsOverlayToggleKey`

方式二，手动创建：

```csharp
RuntimeDiagnosticsOverlay overlay = RuntimeDiagnosticsOverlay.Ensure(
    parent: Framework.Context.Root,
    visibleAtStart: true,
    toggleKey: KeyCode.BackQuote);

overlay.Visible = true;
overlay.ToggleKey = KeyCode.F12;
```

面板会展示 FPS、内存、生命周期、HTTP、Socket、Timer、Scene、Asset、Pool 和最近日志。

## 注意事项

- `DiagnosticsService` 的优先级很早，适合其他模块初始化期间记录日志。
- 日志缓冲长度由 `FrameLog.MaxBufferedEntries` 控制。
- 文件日志需要写权限，建议写到 `Application.persistentDataPath`。
- 运行时面板使用 IMGUI，适合调试，不建议作为正式 UI。
