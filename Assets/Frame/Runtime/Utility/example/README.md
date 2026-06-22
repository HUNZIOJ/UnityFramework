# Utility 模块使用示例

Utility 模块目前提供 `BezierUtility`，用于二阶/三阶贝塞尔曲线位置、切线和长度估算。

## 命名空间

```csharp
using Frame.Runtime.Utility;
using UnityEngine;
```

## 二阶贝塞尔位置

三点：起点、控制点、终点。

```csharp
Vector3 p0 = new Vector3(0f, 0f, 0f);
Vector3 p1 = new Vector3(2f, 3f, 0f);
Vector3 p2 = new Vector3(4f, 0f, 0f);

Vector3 position = BezierUtility.EvaluateQuadratic(p0, p1, p2, t);
```

2D/UI：

```csharp
Vector2 position = BezierUtility.EvaluateQuadratic(p0, p1, p2, t);
```

`t` 会被限制在 `0..1`。

## 二阶贝塞尔切线

```csharp
Vector3 tangent = BezierUtility
    .EvaluateQuadraticDerivative(p0, p1, p2, t)
    .normalized;

transform.forward = tangent;
```

2D 朝向：

```csharp
Vector2 tangent = BezierUtility.EvaluateQuadraticDerivative(p0, p1, p2, t).normalized;
float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
rectTransform.rotation = Quaternion.Euler(0f, 0f, angle);
```

## 三阶贝塞尔位置

四点：起点、控制点 1、控制点 2、终点。

```csharp
Vector3 position = BezierUtility.EvaluateCubic(
    p0,
    p1,
    p2,
    p3,
    t);
```

2D/UI：

```csharp
Vector2 position = BezierUtility.EvaluateCubic(p0, p1, p2, p3, t);
```

## 三阶贝塞尔切线

```csharp
Vector3 direction = BezierUtility
    .EvaluateCubicDerivative(p0, p1, p2, p3, t)
    .normalized;
```

## 估算三阶曲线长度

```csharp
float length = BezierUtility.EstimateCubicLength(
    p0,
    p1,
    p2,
    p3,
    segments: 30);
```

`segments` 越高越精确，也越耗时。

## 匀速移动示例

```csharp
public sealed class BezierMover : MonoBehaviour
{
    public Vector3 P0;
    public Vector3 P1;
    public Vector3 P2;
    public Vector3 P3;
    public float Duration = 1f;

    private float elapsed;

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Duration);

        transform.position = BezierUtility.EvaluateCubic(P0, P1, P2, P3, t);

        Vector3 tangent = BezierUtility.EvaluateCubicDerivative(P0, P1, P2, P3, t);
        if (tangent.sqrMagnitude > 0.0001f)
        {
            transform.forward = tangent.normalized;
        }
    }
}
```

## 注意事项

- `Evaluate*` 系列只做数学计算，不分配 Unity 对象。
- 切线在控制点重合时可能接近零向量，设置朝向前先检查长度。
- `EstimateCubicLength` 是采样估算，不是精确积分。
