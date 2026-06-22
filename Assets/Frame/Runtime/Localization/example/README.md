# Localization 模块使用示例

Localization 模块提供轻量多语言文本表、当前语言切换、fallback locale、格式化文本、缺失 key 记录和 UGUI `LocalizedText` 自动刷新。

## 命名空间

```csharp
using Frame.Core;
using Frame.Localization;
using UnityEngine;
```

## 获取服务

```csharp
ILocalizationService localization = Framework.Resolve<ILocalizationService>();
```

## 创建文本表

`LocalizedTextTable` 支持 CSV/TSV。第一列是 key，后续列是语言。

CSV 示例：

```csv
key,en,zh-CN
menu.start,Start,开始
menu.quit,Quit,退出
player.level,Level {0},等级 {0}
```

运行时导入：

```csharp
LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
table.ImportCsv(csvText);

localization.AddTable(table);
```

使用 TextAsset：

```csharp
[SerializeField] private TextAsset localizationCsv;

public void LoadTable()
{
    LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
    table.SetSource(localizationCsv, ",");
    Framework.Resolve<ILocalizationService>().AddTable(table);
}
```

TSV：

```csharp
table.ImportTsv(tsvText);
table.SetSource(tsvTextAsset, "\\t");
```

## 切换语言

```csharp
localization.FallbackLocale = "en";
localization.SetLocale("zh-CN");

string current = localization.CurrentLocale;
```

监听变化：

```csharp
localization.LocaleChanged += locale =>
{
    FrameLog.Info("locale changed: " + locale);
};
```

## 翻译文本

```csharp
string start = localization.Translate("menu.start", fallback: "Start");
```

格式化：

```csharp
string level = localization.Translate("player.level", fallback: "Level {0}", 12);
```

Try 版本：

```csharp
if (localization.TryTranslate("menu.quit", out string value))
{
    quitText.text = value;
}
```

当当前语言找不到 key 时，会尝试 `FallbackLocale`。仍找不到时，`Translate` 返回 fallback；fallback 为空时返回 key，并把 key 加入 `MissingKeys`。

## 缺失 key 统计

```csharp
foreach (string key in localization.MissingKeys)
{
    FrameLog.Warning("missing localization key: " + key);
}

localization.ClearMissingKeys();
```

## 管理文本表

```csharp
localization.AddTable(baseTable);
localization.AddTable(eventOverrideTable);

bool removed = localization.RemoveTable(eventOverrideTable);
localization.ClearTables();
```

后添加的表优先级更高，适合活动文案覆盖基础文案。

## LocalizedText 组件

给 UGUI `Text` 对象挂 `LocalizedText`，设置 key 和 fallback。语言切换时组件会自动刷新。

运行时设置：

```csharp
LocalizedText localizedText = textGameObject.GetComponent<LocalizedText>();
localizedText.SetKey("menu.start");
localizedText.SetFallback("Start");
localizedText.Refresh();
```

手动绑定服务：

```csharp
localizedText.Bind(localization);
localizedText.Unbind();
```

## 直接查询文本表

```csharp
if (table.TryGet("zh-CN", "menu.start", out string value))
{
    Debug.Log(value);
}

bool hasLocale = table.ContainsLocale("en");
IReadOnlyList<string> locales = table.Locales;
```

## 注意事项

- CSV 解析支持双引号和 `""` 转义。
- key 和 locale 会 trim，并移除 BOM。
- `Translate` 的格式化使用 `string.Format`，占位符数量不匹配会记录异常并返回原模板。
- `LocalizedText` 依赖 `UnityEngine.UI.Text`，TextMeshPro 需要单独适配组件。
