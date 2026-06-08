# Project Structure

```text
Assets/
  Frame/          通用框架，只放可复用底座和编辑器工具
  Game/           当前项目业务代码、业务配置、业务 UI、业务 prefab
  Art/            美术资源，按 Materials/Models/Textures/Animations/VFX 拆分
  Audio/          音频资源，按 Music/SFX/Voice 拆分
  Tests/          EditMode 和 PlayMode 测试
  ThirdParty/     第三方插件或外部 SDK
  Scenes/         Unity 模板自带示例场景，后续可迁移到 Game/Scenes
  Settings/       URP 和项目模板设置
```

## Rules

- `Assets/Frame` 不写具体游戏业务逻辑。
- `Assets/Game` 可以依赖 `Frame.Runtime`。
- `Assets/Frame/Runtime` 不能依赖 `UnityEditor`。
- `Assets/Frame/Editor` 只放编辑器脚本，并通过 `Frame.Editor.asmdef` 限制到 Editor 平台。
- Resources 路径统一使用 `/`，不带扩展名。
- 大型项目后续可把 UI/Input/Audio 等适配层拆成独立 asmdef。
