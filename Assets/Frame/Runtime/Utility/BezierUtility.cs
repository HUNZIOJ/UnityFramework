using UnityEngine;

namespace Frame.Runtime.Utility
{
    /// <summary>
    /// 贝塞尔曲线纯数学工具类 (拆箱即用)
    /// 提供二阶、三阶贝塞尔曲线的位置计算与切线(朝向)计算。
    /// 包含 Vector3(3D) 和 Vector2(2D/UI) 的全套重载。
    /// </summary>
    public static class BezierUtility
    {
        #region 二阶贝塞尔 (Quadratic Bezier) - 3个点 (起点, 控制点, 终点)

        /// <summary>
        /// 获取二阶贝塞尔曲线上 t 时刻的【位置】 (3D)
        /// </summary>
        /// <param name="p0">起点</param>
        /// <param name="p1">控制点</param>
        /// <param name="p2">终点</param>
        /// <param name="t">进度 (0.0 到 1.0)</param>
        public static Vector3 EvaluateQuadratic(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            
            // 公式: B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
            return (oneMinusT * oneMinusT * p0) +
                   (2f * oneMinusT * t * p1) +
                   (t * t * p2);
        }

        /// <summary>
        /// 获取二阶贝塞尔曲线上 t 时刻的【一阶导数/切线方向】 (3D)
        /// 用途: 用于让移动物体(如子弹、摄像机)始终朝向运动轨迹的前方。调用 normalized 即可获得朝向向量。
        /// </summary>
        public static Vector3 EvaluateQuadraticDerivative(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            
            // 导数公式: B'(t) = 2(1-t)(P1 - P0) + 2t(P2 - P1)
            return 2f * oneMinusT * (p1 - p0) +
                   2f * t * (p2 - p1);
        }

        /// <summary>
        /// 获取二阶贝塞尔曲线上 t 时刻的【位置】 (2D/UI)
        /// </summary>
        public static Vector2 EvaluateQuadratic(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return (oneMinusT * oneMinusT * p0) +
                   (2f * oneMinusT * t * p1) +
                   (t * t * p2);
        }

        /// <summary>
        /// 获取二阶贝塞尔曲线上 t 时刻的【切线方向】 (2D/UI)
        /// </summary>
        public static Vector2 EvaluateQuadraticDerivative(Vector2 p0, Vector2 p1, Vector2 p2, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return 2f * oneMinusT * (p1 - p0) +
                   2f * t * (p2 - p1);
        }

        #endregion

        #region 三阶贝塞尔 (Cubic Bezier) - 4个点 (起点, 控制点1, 控制点2, 终点)

        /// <summary>
        /// 获取三阶贝塞尔曲线上 t 时刻的【位置】 (3D)
        /// </summary>
        /// <param name="p0">起点</param>
        /// <param name="p1">控制点1</param>
        /// <param name="p2">控制点2</param>
        /// <param name="p3">终点</param>
        /// <param name="t">进度 (0.0 到 1.0)</param>
        public static Vector3 EvaluateCubic(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            float oneMinusTSqr = oneMinusT * oneMinusT;
            float tSqr = t * t;
            
            // 公式: B(t) = (1-t)^3 * P0 + 3(1-t)^2 * t * P1 + 3(1-t) * t^2 * P2 + t^3 * P3
            return (oneMinusTSqr * oneMinusT * p0) +
                   (3f * oneMinusTSqr * t * p1) +
                   (3f * oneMinusT * tSqr * p2) +
                   (tSqr * t * p3);
        }

        /// <summary>
        /// 获取三阶贝塞尔曲线上 t 时刻的【一阶导数/切线方向】 (3D)
        /// </summary>
        public static Vector3 EvaluateCubicDerivative(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            
            // 导数公式: B'(t) = 3(1-t)^2(P1 - P0) + 6(1-t)t(P2 - P1) + 3t^2(P3 - P2)
            return 3f * oneMinusT * oneMinusT * (p1 - p0) +
                   6f * oneMinusT * t * (p2 - p1) +
                   3f * t * t * (p3 - p2);
        }

        /// <summary>
        /// 获取三阶贝塞尔曲线上 t 时刻的【位置】 (2D/UI)
        /// </summary>
        public static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            float oneMinusTSqr = oneMinusT * oneMinusT;
            float tSqr = t * t;
            
            return (oneMinusTSqr * oneMinusT * p0) +
                   (3f * oneMinusTSqr * t * p1) +
                   (3f * oneMinusT * tSqr * p2) +
                   (tSqr * t * p3);
        }

        /// <summary>
        /// 获取三阶贝塞尔曲线上 t 时刻的【切线方向】 (2D/UI)
        /// </summary>
        public static Vector2 EvaluateCubicDerivative(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            
            return 3f * oneMinusT * oneMinusT * (p1 - p0) +
                   6f * oneMinusT * t * (p2 - p1) +
                   3f * t * t * (p3 - p2);
        }

        #endregion

        #region 高级应用：近似曲线长度估算

        /// <summary>
        /// 估算三阶贝塞尔曲线的近似物理长度 (通过线段采样累加)
        /// 用途: 用于实现真正的“匀速”贝塞尔移动。
        /// </summary>
        /// <param name="segments">采样段数，越高越精确但也越耗性能，默认 30 段</param>
        public static float EstimateCubicLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int segments = 30)
        {
            float length = 0f;
            Vector3 previousPoint = p0;
            
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 currentPoint = EvaluateCubic(p0, p1, p2, p3, t);
                length += Vector3.Distance(previousPoint, currentPoint);
                previousPoint = currentPoint;
            }
            
            return length;
        }

        #endregion
    }
}
