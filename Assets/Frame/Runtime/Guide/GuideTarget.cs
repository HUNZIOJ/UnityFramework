using System.Collections.Generic;
using UnityEngine;

namespace Frame.Runtime.Guide
{
    /// <summary>
    /// 挂载在需要被引导高亮的 UI 元素上。
    /// 内部维护一个静态字典，做到真正的 UI 与引导系统解耦。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class GuideTarget : MonoBehaviour
    {
        [Tooltip("该目标的唯一标识 ID，配置表里寻址就填这个名字")]
        public string TargetId;

        private RectTransform rectTransform;
        public RectTransform RectTransform
        {
            get
            {
                if (rectTransform == null)
                    rectTransform = GetComponent<RectTransform>();
                return rectTransform;
            }
        }

        // --- 全局注册表 ---
        private static readonly Dictionary<string, GuideTarget> activeTargets = new Dictionary<string, GuideTarget>();

        /// <summary>
        /// 全局获取一个当前存活的引导目标
        /// </summary>
        public static GuideTarget GetTarget(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            activeTargets.TryGetValue(id, out var target);
            return target;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(TargetId))
            {
                Debug.LogWarning($"[Guide] GuideTarget 组件的 TargetId 为空！(挂载在 {gameObject.name})");
                return;
            }

            if (activeTargets.ContainsKey(TargetId))
            {
                Debug.LogWarning($"[Guide] 存在重复的 GuideTarget ID: {TargetId}！只保留最新的一个。");
            }
            activeTargets[TargetId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(TargetId) && activeTargets.ContainsKey(TargetId))
            {
                // 防止同名组件禁用时误删了别人
                if (activeTargets[TargetId] == this)
                {
                    activeTargets.Remove(TargetId);
                }
            }
        }
    }
}
