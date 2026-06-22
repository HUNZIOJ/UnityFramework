# Audio 模块使用示例

Audio 模块提供 BGM、SFX、UI、Ambient 等分组音量、静音、音乐淡入淡出、一次性音效和 `AudioCue` 配置播放。

## 命名空间

```csharp
using Frame.Audio;
using Frame.Core;
using UnityEngine;
```

## 获取服务

```csharp
IAudioService audio = Framework.Resolve<IAudioService>();
```

## 音频分类

```csharp
AudioCategory.Master
AudioCategory.Music
AudioCategory.Sfx
AudioCategory.UI
AudioCategory.Ambient
```

`FrameSettings` 中可以为每个分类配置 `AudioMixerGroup` 和音量参数名。

## 设置音量和静音

```csharp
audio.SetVolume(AudioCategory.Master, 1f);
audio.SetVolume(AudioCategory.Music, 0.7f);
audio.SetVolume(AudioCategory.Sfx, 0.9f);

float musicVolume = audio.GetVolume(AudioCategory.Music);

audio.SetMuted(true);
audio.SetMuted(false);
```

音量会被限制在 `0..1`。如果配置了 Mixer 参数，服务会同步写入 Mixer。

## 播放和停止音乐

```csharp
[SerializeField] private AudioClip titleMusic;

public void PlayTitleMusic()
{
    IAudioService audio = Framework.Resolve<IAudioService>();
    audio.PlayMusic(titleMusic, fadeSeconds: 0.5f, volume: 0.8f);
}

public void StopMusic()
{
    Framework.Resolve<IAudioService>().StopMusic(fadeSeconds: 0.5f);
}
```

获取当前音乐句柄：

```csharp
AudioPlaybackHandle current = audio.CurrentMusic;
if (current != null && current.IsPlaying)
{
    current.Volume = 0.5f;
}
```

## 播放一次性音效

```csharp
[SerializeField] private AudioClip clickClip;

public void PlayClick()
{
    audio.PlayOneShot(
        clip: clickClip,
        category: AudioCategory.UI,
        volume: 1f,
        pitch: 1f);
}
```

3D 位置播放：

```csharp
audio.PlayOneShot(explosionClip, AudioCategory.Sfx, volume: 1f, pitch: 1f, position: transform.position);
```

## 使用播放句柄

`PlayOneShotHandle` 和 `PlayCueHandle` 会返回 `AudioPlaybackHandle`，可调整音量、音高或停止。

```csharp
AudioPlaybackHandle handle = audio.PlayOneShotHandle(
    clip: chargeClip,
    category: AudioCategory.Sfx,
    volume: 0.8f,
    pitch: 1.2f);

if (handle.IsValid)
{
    handle.Volume = 0.4f;
    handle.Pitch = 1.0f;
}

handle.Stop();
```

可用属性：

- `Source`
- `Category`
- `Volume`
- `Pitch`
- `IsValid`
- `IsPlaying`

## AudioCue

`AudioCue` 是 ScriptableObject 配置，适合把音频剪辑、分类、音量、音高和循环参数交给策划或配置资产维护。

创建方式：Project 右键或菜单 `Create/Frame/Audio Cue`。

```csharp
[SerializeField] private AudioCue rewardCue;

public void PlayReward()
{
    IAudioService audio = Framework.Resolve<IAudioService>();
    audio.PlayCue(rewardCue);
}
```

带句柄：

```csharp
AudioPlaybackHandle cueHandle = audio.PlayCueHandle(rewardCue);
if (cueHandle.IsPlaying)
{
    cueHandle.Stop();
}
```

## 分组音量示例

```csharp
public sealed class AudioSettingsPresenter
{
    private readonly IAudioService audio = Framework.Resolve<IAudioService>();

    public void SetMusicSlider(float value)
    {
        audio.SetVolume(AudioCategory.Music, value);
    }

    public void SetSfxSlider(float value)
    {
        audio.SetVolume(AudioCategory.Sfx, value);
    }

    public void SetMuteToggle(bool muted)
    {
        audio.SetMuted(muted);
    }
}
```

## 注意事项

- `PlayMusic` 会复用音乐源，重复播放会替换当前音乐。
- `PlayOneShot` 使用内部 `AudioSource` 池，池大小由 `FrameSettings.AudioSourcePoolSize` 控制。
- `AudioCue.Loop` 为 `true` 时适合环境音或持续音效，记得保存并停止句柄。
- `AudioPlaybackHandle.Source` 可能因为播放结束或停止变为 `null`，使用前检查 `IsValid`。
