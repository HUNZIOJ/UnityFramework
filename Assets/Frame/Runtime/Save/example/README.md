# Save 模块使用示例

Save 模块把存档写入 `Application.persistentDataPath/<FrameSettings.SaveFolderName>`，支持 JSON/二进制序列化、AES 加密、元数据、备份恢复、异步读写和版本迁移。

## 命名空间

```csharp
using Frame.Core;
using Frame.Save;
```

## 获取服务

```csharp
ISaveService saves = Framework.Resolve<ISaveService>();
```

## 定义存档数据

```csharp
[System.Serializable]
public sealed class PlayerSaveData : ISaveVersionedData
{
    public int SaveVersion
    {
        get { return 2; }
    }

    public string PlayerName;
    public int Level;
    public int Gold;
}
```

实现 `ISaveVersionedData` 后，调用不带版本参数的 `Save` 时会自动写入 `SaveVersion`。

## 保存和读取

```csharp
PlayerSaveData data = new PlayerSaveData
{
    PlayerName = "Player",
    Level = 5,
    Gold = 100
};

saves.Save("slot_1", data);

PlayerSaveData loaded = saves.Load("slot_1", fallback: new PlayerSaveData());
```

显式指定数据版本：

```csharp
saves.Save("slot_1", data, dataVersion: 2);
```

## TryLoad

```csharp
if (saves.TryLoad<PlayerSaveData>("slot_1", out PlayerSaveData loaded))
{
    Apply(loaded);
}
else
{
    CreateNewGame();
}
```

`TryLoad` 会先读取主文件，失败时尝试读取 `.bak` 备份。

## 异步保存和读取

```csharp
await saves.SaveAsync("slot_1", data, dataVersion: 2);

SaveLoadResult<PlayerSaveData> result = await saves.TryLoadAsync<PlayerSaveData>("slot_1");
if (result.Success)
{
    Apply(result.Data);
}

PlayerSaveData loaded = await saves.LoadAsync("slot_1", fallback: new PlayerSaveData());
```

带取消：

```csharp
using CancellationTokenSource cts = new CancellationTokenSource();
await saves.SaveAsync("slot_1", data, cts.Token);
```

## 判断、删除和路径

```csharp
bool exists = saves.Exists("slot_1");
string path = saves.GetPath("slot_1");
bool deleted = saves.Delete("slot_1");
```

slot 名会经过 `FramePathUtility.SanitizeFileName` 处理，避免非法文件名字符。

## 列出所有存档

```csharp
List<SaveSlotInfo> slots = saves.ListSlots();
foreach (SaveSlotInfo slot in slots)
{
    FrameLog.Info(slot.Slot + " size=" + slot.SizeBytes + " version=" + slot.DataVersion);
}
```

`SaveSlotInfo` 常用字段：

- `Slot`
- `Path`
- `LastWriteUtc`
- `SizeBytes`
- `HasMetadata`
- `DataVersion`
- `Encrypted`
- `SerializerExtension`

## 读取元数据

```csharp
if (saves.TryGetMetadata("slot_1", out SaveMetadata metadata))
{
    FrameLog.Info("saved at=" + metadata.SavedAtUtc);
    FrameLog.Info("sha256=" + metadata.PayloadSha256);
}
```

元数据包含数据版本、保存时间、payload 大小、SHA256、是否加密等信息。读取时会校验大小和 SHA256，校验失败会返回读取失败。

## 切换序列化器

默认序列化器是 `NewtonsoftSaveSerializer`，扩展名为 `.json`。

```csharp
saves.SetSerializer(new NewtonsoftSaveSerializer());
```

使用二进制序列化器：

```csharp
saves.SetSerializer(new BinarySaveSerializer());
```

自定义序列化器：

```csharp
public sealed class CustomSaveSerializer : ISaveSerializer
{
    public string FileExtension
    {
        get { return ".custom"; }
    }

    public byte[] Serialize<TData>(TData data)
    {
        return System.Text.Encoding.UTF8.GetBytes(
            Newtonsoft.Json.JsonConvert.SerializeObject(data));
    }

    public TData Deserialize<TData>(byte[] bytes)
    {
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        return Newtonsoft.Json.JsonConvert.DeserializeObject<TData>(text);
    }
}
```

## AES 加密

```csharp
saves.SetEncryptor(new AesSaveEncryptor("project-specific-passphrase"));
saves.Save("secure_slot", data);
```

读取加密存档时也必须配置同一个 Encryptor：

```csharp
saves.SetEncryptor(new AesSaveEncryptor("project-specific-passphrase"));
PlayerSaveData secure = saves.Load<PlayerSaveData>("secure_slot");
```

关闭加密：

```csharp
saves.SetEncryptor(null);
```

## 版本迁移

假设旧版本 `PlayerSaveData` 的 `PlayerName` 需要补后缀：

```csharp
saves.RegisterMigration(new SaveMigration<PlayerSaveData>(
    fromVersion: 1,
    toVersion: 2,
    migrate: oldData =>
    {
        oldData.PlayerName = oldData.PlayerName + "_v2";
        return oldData;
    }));
```

读取时会按版本连续应用迁移：

```csharp
PlayerSaveData loaded = saves.Load<PlayerSaveData>("slot_1");
```

清空某个类型的迁移：

```csharp
saves.ClearMigrations<PlayerSaveData>();
```

## 完整存档管理示例

```csharp
public sealed class SavePresenter
{
    private readonly ISaveService saves = Framework.Resolve<ISaveService>();

    public void SaveGame(PlayerSaveData data)
    {
        saves.Save("slot_1", data);
    }

    public PlayerSaveData LoadGame()
    {
        return saves.Load("slot_1", new PlayerSaveData
        {
            PlayerName = "New Player",
            Level = 1,
            Gold = 0
        });
    }

    public void DeleteGame()
    {
        saves.Delete("slot_1");
    }
}
```

## 注意事项

- slot 不能为空，否则会抛 `ArgumentException`。
- 不同序列化器使用不同扩展名，切换后 `ListSlots()` 只列出当前扩展名的存档。
- 加密存档没有配置 Encryptor 时无法读取。
- 异步读写使用 .NET 文件 API，仍要避免在极高频率下反复写入。
- 敏感项目不要把加密 passphrase 明文硬编码在客户端代码里。
