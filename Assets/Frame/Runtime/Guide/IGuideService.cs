using System;

namespace Frame.Runtime.Guide
{
    public interface IGuideService
    {
        /// <summary>
        /// 检查当前是否正在进行新手引导
        /// </summary>
        bool IsGuiding { get; }

        /// <summary>
        /// 开始执行一组新手引导
        /// </summary>
        /// <param name="config">引导配置</param>
        /// <param name="onComplete">全组完成时的回调</param>
        void StartGuide(GuideConfig config, Action onComplete = null);

        /// <summary>
        /// 获取指定引导组下一次应该执行的步骤索引。返回值为 0 表示从第一步开始。
        /// </summary>
        /// <param name="guideGroupId">引导组 ID</param>
        int GetGuideProgress(int guideGroupId);

        /// <summary>
        /// 判断指定引导组是否已经完整完成。
        /// </summary>
        /// <param name="guideGroupId">引导组 ID</param>
        bool IsGuideCompleted(int guideGroupId);

        /// <summary>
        /// 清除指定引导组的进度和完成状态。
        /// </summary>
        /// <param name="guideGroupId">引导组 ID</param>
        void ResetGuideProgress(int guideGroupId);

        /// <summary>
        /// 强行中断当前新手引导
        /// </summary>
        void StopGuide();

        /// <summary>
        /// 当自定义事件触发时（供业务层调用，以推进 CustomEvent 类型的引导步）
        /// </summary>
        /// <param name="eventName">事件名称</param>
        void NotifyCustomEvent(string eventName);
    }
}
