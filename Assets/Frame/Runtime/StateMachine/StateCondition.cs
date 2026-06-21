using System;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态转换条件的比较模式。
    /// </summary>
    public enum StateConditionMode
    {
        /// <summary>
        /// 布尔值为 true 时满足，通常用于 Bool 或 Trigger 参数。
        /// </summary>
        If,

        /// <summary>
        /// 布尔值为 false 时满足。
        /// </summary>
        IfNot,

        /// <summary>
        /// 数值大于阈值时满足。
        /// </summary>
        Greater,

        /// <summary>
        /// 数值小于阈值时满足。
        /// </summary>
        Less,

        /// <summary>
        /// 参数值等于阈值时满足。
        /// </summary>
        Equals,

        /// <summary>
        /// 参数值不等于阈值时满足。
        /// </summary>
        NotEquals
    }

    /// <summary>
    /// 一条状态转换条件，负责检查状态机参数表中的某个参数是否满足要求。
    /// </summary>
    /// <remarks>
    /// 一个 <see cref="StateTransition"/> 可以挂多个条件，所有条件都满足时才允许转换。
    /// Float 条件比较使用 0.0001 的误差容忍。
    /// </remarks>
    public sealed class StateCondition
    {
        /// <summary>
        /// 私有构造，统一由静态工厂方法创建，避免外部传入无意义阈值组合。
        /// </summary>
        private StateCondition(string parameterName, StateConditionMode mode, float floatThreshold, int intThreshold, bool boolThreshold)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ArgumentException("Parameter name cannot be null or empty.", "parameterName");
            }

            ParameterName = parameterName;
            Mode = mode;
            FloatThreshold = floatThreshold;
            IntThreshold = intThreshold;
            BoolThreshold = boolThreshold;
        }

        /// <summary>
        /// 要检查的状态机参数名。
        /// </summary>
        public string ParameterName { get; private set; }

        /// <summary>
        /// 条件比较模式。
        /// </summary>
        public StateConditionMode Mode { get; private set; }

        /// <summary>
        /// Float 参数使用的阈值。
        /// </summary>
        public float FloatThreshold { get; private set; }

        /// <summary>
        /// Int 参数使用的阈值。
        /// </summary>
        public int IntThreshold { get; private set; }

        /// <summary>
        /// Bool 参数使用的目标值。
        /// </summary>
        public bool BoolThreshold { get; private set; }

        /// <summary>
        /// 创建布尔参数为 true 时满足的条件。
        /// </summary>
        /// <param name="parameterName">Bool 或 Trigger 参数名。</param>
        /// <returns>新条件实例。</returns>
        public static StateCondition If(string parameterName)
        {
            return new StateCondition(parameterName, StateConditionMode.If, 0f, 0, true);
        }

        /// <summary>
        /// 创建布尔参数为 false 时满足的条件。
        /// </summary>
        /// <param name="parameterName">Bool 参数名。</param>
        /// <returns>新条件实例。</returns>
        public static StateCondition IfNot(string parameterName)
        {
            return new StateCondition(parameterName, StateConditionMode.IfNot, 0f, 0, false);
        }

        /// <summary>
        /// 创建 Trigger 条件。语义等同于 <see cref="If"/>，命中转换后 Trigger 会被状态机消费。
        /// </summary>
        /// <param name="parameterName">Trigger 参数名。</param>
        /// <returns>新条件实例。</returns>
        public static StateCondition Trigger(string parameterName)
        {
            return If(parameterName);
        }

        /// <summary>
        /// 创建 Float 参数大于阈值时满足的条件。
        /// </summary>
        public static StateCondition Greater(string parameterName, float threshold)
        {
            return new StateCondition(parameterName, StateConditionMode.Greater, threshold, 0, false);
        }

        /// <summary>
        /// 创建 Int 参数大于阈值时满足的条件。
        /// </summary>
        public static StateCondition Greater(string parameterName, int threshold)
        {
            return new StateCondition(parameterName, StateConditionMode.Greater, 0f, threshold, false);
        }

        /// <summary>
        /// 创建 Float 参数小于阈值时满足的条件。
        /// </summary>
        public static StateCondition Less(string parameterName, float threshold)
        {
            return new StateCondition(parameterName, StateConditionMode.Less, threshold, 0, false);
        }

        /// <summary>
        /// 创建 Int 参数小于阈值时满足的条件。
        /// </summary>
        public static StateCondition Less(string parameterName, int threshold)
        {
            return new StateCondition(parameterName, StateConditionMode.Less, 0f, threshold, false);
        }

        /// <summary>
        /// 创建 Int 参数等于指定值时满足的条件。
        /// </summary>
        public static StateCondition Equal(string parameterName, int value)
        {
            return new StateCondition(parameterName, StateConditionMode.Equals, 0f, value, false);
        }

        /// <summary>
        /// 创建 Int 参数不等于指定值时满足的条件。
        /// </summary>
        public static StateCondition NotEqual(string parameterName, int value)
        {
            return new StateCondition(parameterName, StateConditionMode.NotEquals, 0f, value, false);
        }

        /// <summary>
        /// 创建 Bool 参数等于指定值时满足的条件。
        /// </summary>
        public static StateCondition Equal(string parameterName, bool value)
        {
            return new StateCondition(parameterName, StateConditionMode.Equals, 0f, 0, value);
        }

        /// <summary>
        /// 创建 Bool 参数不等于指定值时满足的条件。
        /// </summary>
        public static StateCondition NotEqual(string parameterName, bool value)
        {
            return new StateCondition(parameterName, StateConditionMode.NotEquals, 0f, 0, value);
        }

        /// <summary>
        /// 用给定参数表计算本条件是否满足。
        /// </summary>
        /// <param name="parameters">状态机参数表。</param>
        /// <returns>参数存在、类型匹配且比较通过时返回 true。</returns>
        public bool IsMet(StateParameterSet parameters)
        {
            if (parameters == null)
            {
                return false;
            }

            StateParameterType type;
            float floatValue;
            int intValue;
            bool boolValue;
            if (!parameters.TryGetRaw(ParameterName, out type, out floatValue, out intValue, out boolValue))
            {
                return false;
            }

            switch (type)
            {
                case StateParameterType.Float:
                    return EvaluateFloat(floatValue);
                case StateParameterType.Int:
                    return EvaluateInt(intValue);
                case StateParameterType.Bool:
                case StateParameterType.Trigger:
                    return EvaluateBool(boolValue);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 对 Float 参数执行比较。
        /// </summary>
        private bool EvaluateFloat(float value)
        {
            switch (Mode)
            {
                case StateConditionMode.Greater:
                    return value > FloatThreshold;
                case StateConditionMode.Less:
                    return value < FloatThreshold;
                case StateConditionMode.Equals:
                    return Math.Abs(value - FloatThreshold) <= 0.0001f;
                case StateConditionMode.NotEquals:
                    return Math.Abs(value - FloatThreshold) > 0.0001f;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 对 Int 参数执行比较。
        /// </summary>
        private bool EvaluateInt(int value)
        {
            switch (Mode)
            {
                case StateConditionMode.Greater:
                    return value > IntThreshold;
                case StateConditionMode.Less:
                    return value < IntThreshold;
                case StateConditionMode.Equals:
                    return value == IntThreshold;
                case StateConditionMode.NotEquals:
                    return value != IntThreshold;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 对 Bool 或 Trigger 参数执行比较。
        /// </summary>
        private bool EvaluateBool(bool value)
        {
            switch (Mode)
            {
                case StateConditionMode.If:
                    return value;
                case StateConditionMode.IfNot:
                    return !value;
                case StateConditionMode.Equals:
                    return value == BoolThreshold;
                case StateConditionMode.NotEquals:
                    return value != BoolThreshold;
                default:
                    return false;
            }
        }
    }
}
