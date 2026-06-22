# Preferences 模块使用示例

Preferences 模块封装 Unity `PlayerPrefs`，适合保存音量、语言、画质、按键绑定等轻量用户偏好。

## 命名空间

```csharp
using Frame.Core;
using Frame.Preferences;
```

## 获取服务

```csharp
IPreferencesService preferences = Framework.Resolve<IPreferencesService>();
```

## 基础类型

```csharp
preferences.SetInt("graphics.quality", 2);
preferences.SetFloat("audio.music", 0.8f);
preferences.SetString("player.name", "Player");
preferences.SetBool("tutorial.finished", true);

int quality = preferences.GetInt("graphics.quality", fallback: 1);
float music = preferences.GetFloat("audio.music", fallback: 1f);
string playerName = preferences.GetString("player.name", fallback: "Guest");
bool tutorialFinished = preferences.GetBool("tutorial.finished", fallback: false);
```

## JSON 对象

```csharp
public sealed class PreferenceProfile
{
    public string Locale;
    public bool Vibration;
    public float UiScale;
}

preferences.SetJson("profile", new PreferenceProfile
{
    Locale = "zh-CN",
    Vibration = true,
    UiScale = 1f
});

PreferenceProfile profile = preferences.GetJson(
    "profile",
    new PreferenceProfile { Locale = "en", Vibration = true, UiScale = 1f });
```

使用 Try 版本：

```csharp
if (preferences.TryGetJson<PreferenceProfile>("profile", out PreferenceProfile loaded))
{
    Apply(loaded);
}
```

## 判断和删除

```csharp
if (preferences.HasKey("profile"))
{
    preferences.DeleteKey("profile");
}
```

`DeleteKey` 返回是否真的删除了已有 key。

## 保存到磁盘

```csharp
preferences.SetFloat("audio.sfx", 0.6f);
preferences.Save();
```

`PreferencesService` 在模块关闭时也会调用 `Save()`，但设置页面点击确定或应用切后台时建议显式保存。

## 监听变化

```csharp
private IPreferencesService preferences;

public void Open()
{
    preferences = Framework.Resolve<IPreferencesService>();
    preferences.Changed += OnPreferenceChanged;
}

public void Close()
{
    if (preferences != null)
    {
        preferences.Changed -= OnPreferenceChanged;
    }
}

private void OnPreferenceChanged(string key)
{
    FrameLog.Info("preference changed: " + key);
}
```

## 设置界面示例

```csharp
public sealed class SettingsPresenter
{
    private readonly IPreferencesService preferences = Framework.Resolve<IPreferencesService>();

    public void LoadToView()
    {
        float music = preferences.GetFloat("audio.music", 1f);
        bool muted = preferences.GetBool("audio.muted", false);
        string locale = preferences.GetString("locale", "en");
    }

    public void Apply(float music, bool muted, string locale)
    {
        preferences.SetFloat("audio.music", music);
        preferences.SetBool("audio.muted", muted);
        preferences.SetString("locale", locale);
        preferences.Save();
    }
}
```

## 注意事项

- key 不能为空，否则设置类方法会抛 `ArgumentException`。
- `SetString` 写入 `null` 时会保存为空字符串。
- JSON 解析失败会返回 `false` 或 fallback，并记录异常。
- `PlayerPrefs` 不适合保存大体积数据或敏感数据，游戏存档请使用 Save 模块。
