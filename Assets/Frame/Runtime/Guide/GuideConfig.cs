using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frame.Runtime.Guide
{
    /// <summary>
    /// 遮罩的开孔形状
    /// </summary>
    public enum GuideMaskShape
    {
        Rectangle,
        RoundedRectangle,
        Circle,
        Ellipse
    }

    /// <summary>
    /// 引导的触发/完成条件类型
    /// </summary>
    public enum GuideTriggerType
    {
        ClickTarget, // 点击了高亮目标
        AutoNext,    // 仅展示文本，点击屏幕任意区域继续
        CustomEvent  // 抛出自定义事件后继续（例如点击了某个非UI物体）
    }

    /// <summary>
    /// 引导单步配置数据
    /// </summary>
    [Serializable]
    public class GuideStep
    {
        [Header("目标寻址")]
        [Tooltip("对应 GuideTarget 上填写的字符串 ID。如果为空，表示此步骤无需挖孔遮罩（比如纯剧情展示）")]
        public string TargetId;

        [Header("遮罩表现")]
        public GuideMaskShape MaskShape = GuideMaskShape.Rectangle;
        
        [Tooltip("挖孔区域的扩大范围（像素），让孔比实际按钮大一点更美观")]
        public Vector2 Padding = new Vector2(10f, 10f);

        [Tooltip("当 MaskShape 为 RoundedRectangle 时使用的圆角半径（像素）")]
        public float CornerRadius = 18f;

        [Header("控制与触发")]
        public GuideTriggerType TriggerType = GuideTriggerType.ClickTarget;
        
        [Tooltip("当 TriggerType 为 CustomEvent 时，监听此事件名才推进")]
        public string CustomEventName;

        [Header("提示文本")]
        [TextArea(2, 5)]
        [Tooltip("如果配置了，UI上会显示这行引导文字")]
        public string DialogueText;
        
        [Tooltip("可选：提示框相对于挖孔中心的本地坐标偏移")]
        public Vector2 DialogueOffset = new Vector2(0, -100f);
    }

    /// <summary>
    /// 一组引导的配置表 (ScriptableObject)
    /// </summary>
    [CreateAssetMenu(fileName = "NewGuideConfig", menuName = "Framework/Guide/Guide Config", order = 0)]
    public class GuideConfig : ScriptableObject
    {
        [Tooltip("这一组引导的全局唯一标识，用于记录玩家是否完成过")]
        public int GuideGroupId;

        [Tooltip("是否持久化这一组引导的当前步骤和完成状态。GuideGroupId 必须大于 0 才会生效")]
        public bool PersistProgress = true;

        [Tooltip("可选：引导文本框预制体。预制体内可以包含 UnityEngine.UI.Text；为空时使用框架默认文本框")]
        public GameObject DialoguePrefab;

        [Tooltip("该引导组的所有步骤")]
        public List<GuideStep> Steps = new List<GuideStep>();
    }
}
