# Project Structure

本项目把通用框架、业务代码、美术资源、第三方插件和项目设置分开管理。核心原则是：`Assets/Frame` 只放可复用框架代码，具体游戏业务放到独立目录。

```text
Assets/
  Frame/          通用框架，只放可复用底座、适配层、示例和编辑器工具
  Game/           当前项目业务代码、业务配置、业务 UI、业务 prefab
  Art/            美术资源，按 Materials/Models/Textures/Animations/VFX 拆分
  Audio/          音频资源，按 Music/SFX/Voice 拆分
  Tests/          EditMode 和 PlayMode 测试
  ThirdParty/     第三方插件或外部 SDK
  Scenes/         场景资源，后续可按业务迁移到 Game/Scenes
  Settings/       URP 和项目模板设置
```

## Rules

- `Assets/Frame` 不写具体游戏业务逻辑。
- `Assets/Game` 可以依赖 `Frame.Runtime`。
- `Assets/Frame/Runtime` 不能依赖 `UnityEditor`。
- `Assets/Frame/Editor` 只放编辑器脚本，并通过 `Frame.Editor.asmdef` 限制到 Editor 平台。
- Resources 路径统一使用 `/`，不带扩展名。
- UI prefab 建议放在 `Resources/UI` 或业务约定目录下。
- JSON 配置建议放在 `Resources/Configs` 下，由 `ConfigService` 读取。
- 大型项目后续可把 UI/Input/Audio/Networking 等适配层拆成独立 asmdef。

## Recommended Business Layout

```text
Assets/Game/
  Scripts/
    Runtime/
    Editor/
  Resources/
    UI/
    Configs/
  Scenes/
  Prefabs/
  ScriptableObjects/
```

框架代码通过接口向业务层提供服务，业务层不要反向修改框架内部实现。确实需要扩展框架时，优先新增接口实现或 `IFrameModuleInstaller`，再决定是否修改 `Assets/Frame`。
