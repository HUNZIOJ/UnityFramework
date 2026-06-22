# Guide 模块使用示例

Guide 模块用于新手引导，支持目标高亮、镂空遮罩、文本提示、点击目标、点击任意处、等待自定义事件和进度持久化。

## 命名空间

```csharp
using Frame.Core;
using Frame.Runtime.Guide;
```

## 获取服务

```csharp
IGuideService guides = Framework.Resolve<IGuideService>();
```

## 给 UI 元素挂 GuideTarget

在需要被引导高亮的 UI 节点上挂 `GuideTarget`，设置唯一 `TargetId`。

```csharp
GuideTarget target = buttonGameObject.AddComponent<GuideTarget>();
target.TargetId = "main.start.button";
```

运行时也可以查找当前活跃目标：

```csharp
GuideTarget target = GuideTarget.GetTarget("main.start.button");
```

`GuideTarget` 启用时注册，禁用时注销。

## 创建 GuideConfig

创建方式：Project 右键或菜单 `Create/Framework/Guide/Guide Config`。

关键字段：

- `GuideGroupId`: 引导组 id，大于 0 时可用于持久化进度。
- `PersistProgress`: 是否保存进度和完成状态。
- `DialoguePrefab`: 可选的提示框 prefab，内部可包含 `UnityEngine.UI.Text`。
- `Steps`: 引导步骤列表。

## GuideStep 字段

- `TargetId`: 对应 `GuideTarget.TargetId`，为空表示不高亮具体目标。
- `MaskShape`: `Rectangle`、`RoundedRectangle`、`Circle`、`Ellipse`。
- `Padding`: 镂空区域额外扩展。
- `CornerRadius`: 圆角矩形半径。
- `TriggerType`: 完成条件。
- `CustomEventName`: 自定义事件名。
- `DialogueText`: 提示文本。
- `DialogueOffset`: 提示框相对目标中心偏移。

## 开始引导

```csharp
[SerializeField] private GuideConfig tutorial;

public void StartTutorial()
{
    IGuideService guides = Framework.Resolve<IGuideService>();
    guides.StartGuide(tutorial, onComplete: () =>
    {
        FrameLog.Info("tutorial completed");
    });
}
```

如果该组已完成且 `PersistProgress = true`，`StartGuide` 会直接调用完成回调。

## 点击目标推进

配置步骤：

```csharp
new GuideStep
{
    TargetId = "main.start.button",
    MaskShape = GuideMaskShape.RoundedRectangle,
    TriggerType = GuideTriggerType.ClickTarget,
    DialogueText = "点击开始按钮",
    Padding = new Vector2(20f, 20f)
};
```

目标对象需要有 `Button` 组件。遮罩镂空区域会允许点击穿透到底层按钮。

## 点击任意处推进

```csharp
new GuideStep
{
    TargetId = "",
    TriggerType = GuideTriggerType.AutoNext,
    DialogueText = "欢迎来到游戏"
};
```

框架会创建透明点击层，点击任意位置进入下一步。

## 自定义事件推进

配置：

```csharp
new GuideStep
{
    TargetId = "bag.equip.slot",
    TriggerType = GuideTriggerType.CustomEvent,
    CustomEventName = "equip_finished",
    DialogueText = "装备第一件武器"
};
```

业务完成动作后通知：

```csharp
guides.NotifyCustomEvent("equip_finished");
```

事件名不匹配时不会推进。

## 查询和重置进度

```csharp
int nextStepIndex = guides.GetGuideProgress(1001);
bool completed = guides.IsGuideCompleted(1001);

guides.ResetGuideProgress(1001);
```

持久化优先使用 `IPreferencesService`，如果没有该服务则回退到 `PlayerPrefs`。

## 停止引导

```csharp
if (guides.IsGuiding)
{
    guides.StopGuide();
}
```

停止会取消当前异步流程、销毁遮罩和提示框，不会调用完成回调。

## 自定义遮罩组件

`UIGuideMask` 可单独使用：

```csharp
UIGuideMask mask = maskGameObject.AddComponent<UIGuideMask>();
mask.color = new Color(0f, 0f, 0f, 0.7f);
mask.SetTarget(center, size, GuideMaskShape.Circle);
mask.ClearTarget();
```

它实现了 `ICanvasRaycastFilter`，在镂空区域返回不拦截，区域外拦截点击。

## 注意事项

- 同一时刻只能运行一个引导，重复 `StartGuide` 会被忽略。
- `ClickTarget` 目标必须能在当前 UI 中找到，服务会等待目标出现。
- 重复 `TargetId` 会保留最新启用的目标。
- 引导遮罩使用独立 Screen Space Overlay Canvas，sortingOrder 为高层级。
- 默认提示框使用内置字体；正式项目建议配置自己的 `DialoguePrefab`。
