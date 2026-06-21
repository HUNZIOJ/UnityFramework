using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态机参数类型，参考 Animator Controller 的参数模型。
    /// </summary>
    public enum StateParameterType
    {
        /// <summary>
        /// 浮点参数，通常用于速度、距离、血量比例等连续值条件。
        /// </summary>
        Float,

        /// <summary>
        /// 整型参数，通常用于阶段、索引、枚举值等离散条件。
        /// </summary>
        Int,

        /// <summary>
        /// 布尔参数，通常用于是否在地面、是否锁定目标等开关条件。
        /// </summary>
        Bool,

        /// <summary>
        /// 触发器参数，满足转换后会被消费并自动重置为 false。
        /// </summary>
        Trigger
    }

    /// <summary>
    /// 参数发生变化时派发的事件数据。
    /// </summary>
    public struct StateParameterChanged
    {
        /// <summary>
        /// 创建参数变化事件。
        /// </summary>
        /// <param name="name">发生变化的参数名。</param>
        /// <param name="type">发生变化的参数类型。</param>
        internal StateParameterChanged(string name, StateParameterType type)
        {
            Name = name;
            Type = type;
        }

        /// <summary>
        /// 发生变化的参数名。
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// 发生变化的参数类型。
        /// </summary>
        public StateParameterType Type { get; private set; }
    }

    /// <summary>
    /// 状态机参数表，负责保存 Float、Int、Bool 和 Trigger 参数。
    /// </summary>
    /// <remarks>
    /// 转换条件会从这里读取参数。Trigger 与 Bool 都用布尔值保存，但 Trigger 在触发转换后会被
    /// <see cref="ConsumeTriggers"/> 自动重置，适合表达“一次性事件”。
    /// </remarks>
    public sealed class StateParameterSet
    {
        /// <summary>
        /// 参数名到参数值的映射。具体数值保存在内部 <see cref="Parameter"/> 容器里。
        /// </summary>
        private readonly Dictionary<string, Parameter> parameters = new Dictionary<string, Parameter>();

        /// <summary>
        /// 参数新增、赋值或 Trigger 被消费时触发。
        /// </summary>
        public event Action<StateParameterChanged> Changed;

        /// <summary>
        /// 当前参数总数。
        /// </summary>
        public int Count
        {
            get { return parameters.Count; }
        }

        /// <summary>
        /// 添加 Float 参数；如果同名同类型参数已存在则返回已有参数并更新默认值。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="defaultValue">默认浮点值。</param>
        public void AddFloat(string name, float defaultValue = 0f)
        {
            Add(name, StateParameterType.Float).FloatValue = defaultValue;
        }

        /// <summary>
        /// 添加 Int 参数；如果同名同类型参数已存在则返回已有参数并更新默认值。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="defaultValue">默认整型值。</param>
        public void AddInt(string name, int defaultValue = 0)
        {
            Add(name, StateParameterType.Int).IntValue = defaultValue;
        }

        /// <summary>
        /// 添加 Bool 参数；如果同名同类型参数已存在则返回已有参数并更新默认值。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="defaultValue">默认布尔值。</param>
        public void AddBool(string name, bool defaultValue = false)
        {
            Add(name, StateParameterType.Bool).BoolValue = defaultValue;
        }

        /// <summary>
        /// 添加 Trigger 参数，初始值为 false。
        /// </summary>
        /// <param name="name">参数名。</param>
        public void AddTrigger(string name)
        {
            Add(name, StateParameterType.Trigger);
        }

        /// <summary>
        /// 检查是否存在指定名称的参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns>存在时返回 true。</returns>
        public bool Has(string name)
        {
            return parameters.ContainsKey(name);
        }

        /// <summary>
        /// 尝试读取参数类型。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="type">成功时返回参数类型。</param>
        /// <returns>参数存在时返回 true。</returns>
        public bool TryGetType(string name, out StateParameterType type)
        {
            Parameter parameter;
            if (!parameters.TryGetValue(name, out parameter))
            {
                type = default(StateParameterType);
                return false;
            }

            type = parameter.Type;
            return true;
        }

        /// <summary>
        /// 设置 Float 参数；不存在时会自动创建。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">新浮点值。</param>
        public void SetFloat(string name, float value)
        {
            Parameter parameter = GetOrCreate(name, StateParameterType.Float);
            parameter.FloatValue = value;
            RaiseChanged(name, parameter.Type);
        }

        /// <summary>
        /// 读取 Float 参数；不存在或类型不匹配时抛异常。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns>参数当前浮点值。</returns>
        public float GetFloat(string name)
        {
            return Get(name, StateParameterType.Float).FloatValue;
        }

        /// <summary>
        /// 尝试读取 Float 参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">成功时返回参数当前浮点值。</param>
        /// <returns>参数存在且类型为 Float 时返回 true。</returns>
        public bool TryGetFloat(string name, out float value)
        {
            Parameter parameter;
            if (!TryGet(name, StateParameterType.Float, out parameter))
            {
                value = 0f;
                return false;
            }

            value = parameter.FloatValue;
            return true;
        }

        /// <summary>
        /// 设置 Int 参数；不存在时会自动创建。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">新整型值。</param>
        public void SetInt(string name, int value)
        {
            Parameter parameter = GetOrCreate(name, StateParameterType.Int);
            parameter.IntValue = value;
            RaiseChanged(name, parameter.Type);
        }

        /// <summary>
        /// 读取 Int 参数；不存在或类型不匹配时抛异常。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns>参数当前整型值。</returns>
        public int GetInt(string name)
        {
            return Get(name, StateParameterType.Int).IntValue;
        }

        /// <summary>
        /// 尝试读取 Int 参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">成功时返回参数当前整型值。</param>
        /// <returns>参数存在且类型为 Int 时返回 true。</returns>
        public bool TryGetInt(string name, out int value)
        {
            Parameter parameter;
            if (!TryGet(name, StateParameterType.Int, out parameter))
            {
                value = 0;
                return false;
            }

            value = parameter.IntValue;
            return true;
        }

        /// <summary>
        /// 设置 Bool 参数；不存在时会自动创建。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">新布尔值。</param>
        public void SetBool(string name, bool value)
        {
            Parameter parameter = GetOrCreate(name, StateParameterType.Bool);
            parameter.BoolValue = value;
            RaiseChanged(name, parameter.Type);
        }

        /// <summary>
        /// 读取 Bool 参数；不存在或类型不匹配时抛异常。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <returns>参数当前布尔值。</returns>
        public bool GetBool(string name)
        {
            return Get(name, StateParameterType.Bool).BoolValue;
        }

        /// <summary>
        /// 尝试读取 Bool 参数。
        /// </summary>
        /// <param name="name">参数名。</param>
        /// <param name="value">成功时返回参数当前布尔值。</param>
        /// <returns>参数存在且类型为 Bool 时返回 true。</returns>
        public bool TryGetBool(string name, out bool value)
        {
            Parameter parameter;
            if (!TryGet(name, StateParameterType.Bool, out parameter))
            {
                value = false;
                return false;
            }

            value = parameter.BoolValue;
            return true;
        }

        /// <summary>
        /// 设置 Trigger 为 true。触发相关转换后会被状态机自动消费并重置。
        /// </summary>
        /// <param name="name">Trigger 参数名。</param>
        public void SetTrigger(string name)
        {
            Parameter parameter = GetOrCreate(name, StateParameterType.Trigger);
            parameter.BoolValue = true;
            RaiseChanged(name, parameter.Type);
        }

        /// <summary>
        /// 手动将 Trigger 重置为 false。
        /// </summary>
        /// <param name="name">Trigger 参数名。</param>
        public void ResetTrigger(string name)
        {
            Parameter parameter = GetOrCreate(name, StateParameterType.Trigger);
            parameter.BoolValue = false;
            RaiseChanged(name, parameter.Type);
        }

        /// <summary>
        /// 判断 Trigger 当前是否处于已触发状态。
        /// </summary>
        /// <param name="name">Trigger 参数名。</param>
        /// <returns>Trigger 存在且值为 true 时返回 true。</returns>
        public bool IsTriggerSet(string name)
        {
            Parameter parameter;
            return TryGet(name, StateParameterType.Trigger, out parameter) && parameter.BoolValue;
        }

        /// <summary>
        /// 清空所有参数。
        /// </summary>
        public void Clear()
        {
            parameters.Clear();
        }

        /// <summary>
        /// 消费指定条件中引用到的 Trigger 参数，把它们重置为 false。
        /// </summary>
        /// <param name="conditions">刚刚命中的转换条件集合。</param>
        internal void ConsumeTriggers(IList<StateCondition> conditions)
        {
            if (conditions == null)
            {
                return;
            }

            for (int i = 0; i < conditions.Count; i++)
            {
                StateCondition condition = conditions[i];
                Parameter parameter;
                if (parameters.TryGetValue(condition.ParameterName, out parameter) &&
                    parameter.Type == StateParameterType.Trigger)
                {
                    parameter.BoolValue = false;
                    RaiseChanged(condition.ParameterName, parameter.Type);
                }
            }
        }

        /// <summary>
        /// 供条件系统读取参数原始值，避免按类型分别查询多次。
        /// </summary>
        internal bool TryGetRaw(string name, out StateParameterType type, out float floatValue, out int intValue, out bool boolValue)
        {
            Parameter parameter;
            if (!parameters.TryGetValue(name, out parameter))
            {
                type = default(StateParameterType);
                floatValue = 0f;
                intValue = 0;
                boolValue = false;
                return false;
            }

            type = parameter.Type;
            floatValue = parameter.FloatValue;
            intValue = parameter.IntValue;
            boolValue = parameter.BoolValue;
            return true;
        }

        /// <summary>
        /// 显式添加参数；如果同名参数已存在且类型相同则复用，类型不同则报错。
        /// </summary>
        private Parameter Add(string name, StateParameterType type)
        {
            ValidateName(name);

            Parameter existing;
            if (parameters.TryGetValue(name, out existing))
            {
                if (existing.Type != type)
                {
                    throw new InvalidOperationException("State parameter '" + name + "' already exists as " + existing.Type + ".");
                }

                return existing;
            }

            Parameter parameter = new Parameter(type);
            parameters.Add(name, parameter);
            RaiseChanged(name, type);
            return parameter;
        }

        /// <summary>
        /// 按名称和类型获取参数；不存在时创建，存在但类型不同时报错。
        /// </summary>
        private Parameter GetOrCreate(string name, StateParameterType type)
        {
            ValidateName(name);

            Parameter parameter;
            if (!parameters.TryGetValue(name, out parameter))
            {
                parameter = new Parameter(type);
                parameters.Add(name, parameter);
                return parameter;
            }

            if (parameter.Type != type)
            {
                throw new InvalidOperationException("State parameter '" + name + "' is " + parameter.Type + ", not " + type + ".");
            }

            return parameter;
        }

        /// <summary>
        /// 获取指定类型参数；不存在或类型不匹配时抛异常。
        /// </summary>
        private Parameter Get(string name, StateParameterType type)
        {
            Parameter parameter;
            if (!TryGet(name, type, out parameter))
            {
                throw new InvalidOperationException("State parameter '" + name + "' does not exist as " + type + ".");
            }

            return parameter;
        }

        /// <summary>
        /// 尝试获取指定类型参数。
        /// </summary>
        private bool TryGet(string name, StateParameterType type, out Parameter parameter)
        {
            if (!parameters.TryGetValue(name, out parameter))
            {
                return false;
            }

            return parameter.Type == type;
        }

        /// <summary>
        /// 派发参数变化事件。
        /// </summary>
        private void RaiseChanged(string name, StateParameterType type)
        {
            Action<StateParameterChanged> handler = Changed;
            if (handler != null)
            {
                handler(new StateParameterChanged(name, type));
            }
        }

        /// <summary>
        /// 校验参数名不能为空。
        /// </summary>
        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Parameter name cannot be null or empty.", "name");
            }
        }

        /// <summary>
        /// 单个参数的内部存储。为了减少装箱，三种值字段同时存在，实际使用哪一个由 <see cref="Type"/> 决定。
        /// </summary>
        private sealed class Parameter
        {
            /// <summary>
            /// 创建指定类型的参数容器。
            /// </summary>
            public Parameter(StateParameterType type)
            {
                Type = type;
            }

            /// <summary>
            /// 参数类型，创建后不可改变。
            /// </summary>
            public readonly StateParameterType Type;

            /// <summary>
            /// Float 参数的实际值。
            /// </summary>
            public float FloatValue;

            /// <summary>
            /// Int 参数的实际值。
            /// </summary>
            public int IntValue;

            /// <summary>
            /// Bool 或 Trigger 参数的实际值。
            /// </summary>
            public bool BoolValue;
        }
    }
}
