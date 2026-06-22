using System;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Runtime.Guide
{
    /// <summary>
    /// 镂空遮罩组件。
    /// 通过重写 OnPopulateMesh 用顶点构建带有孔洞的面片（纯C#实现，无需特殊Shader）。
    /// 同时实现 ICanvasRaycastFilter 进行精确的点击穿透拦截。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIGuideMask : MaskableGraphic, ICanvasRaycastFilter
    {
        private Vector2 targetCenter;
        private Vector2 targetSize;
        private GuideMaskShape maskShape = GuideMaskShape.Rectangle;
        private float cornerRadius = 18f;
        private const int EllipseSegmentCount = 48;
        private const int RoundedRectangleSegmentCount = 24;

        /// <summary>
        /// 屏幕中心的默认位置（避免一开始没有目标时挖出奇怪的孔）
        /// </summary>
        private bool hasTarget = false;

        /// <summary>
        /// 设置要挖孔的目标区域（屏幕/本地坐标系下的中心点和大小）
        /// </summary>
        public void SetTarget(Vector2 center, Vector2 size, GuideMaskShape shape)
        {
            SetTarget(center, size, shape, cornerRadius);
        }

        /// <summary>
        /// 设置要挖孔的目标区域（屏幕/本地坐标系下的中心点和大小）
        /// </summary>
        public void SetTarget(Vector2 center, Vector2 size, GuideMaskShape shape, float radius)
        {
            targetCenter = center;
            targetSize = new Vector2(Mathf.Max(0f, size.x), Mathf.Max(0f, size.y));
            maskShape = shape;
            cornerRadius = Mathf.Max(0f, radius);
            hasTarget = true;
            
            // 标记顶点需要重新生成
            SetVerticesDirty();
        }

        public void ClearTarget()
        {
            hasTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect outerRect = rectTransform.rect;
            Color32 color32 = color;

            if (!hasTarget)
            {
                // 没有目标时，整个屏幕涂黑（如果需要的话，或者完全透明，根据具体需求）
                // 这里我们画一个覆盖全屏的单张Quad
                AddQuad(vh, 
                    new Vector2(outerRect.xMin, outerRect.yMax),
                    new Vector2(outerRect.xMax, outerRect.yMax),
                    new Vector2(outerRect.xMax, outerRect.yMin),
                    new Vector2(outerRect.xMin, outerRect.yMin), 
                    color32);
                return;
            }

            // 计算孔洞的四个边界
            float holeLeft = targetCenter.x - targetSize.x * 0.5f;
            float holeRight = targetCenter.x + targetSize.x * 0.5f;
            float holeTop = targetCenter.y + targetSize.y * 0.5f;
            float holeBottom = targetCenter.y - targetSize.y * 0.5f;

            switch (maskShape)
            {
                case GuideMaskShape.RoundedRectangle:
                    AddRoundedRectangleHole(vh, outerRect, holeLeft, holeRight, holeBottom, holeTop, color32);
                    break;
                case GuideMaskShape.Circle:
                    float radius = Mathf.Max(targetSize.x, targetSize.y) * 0.5f;
                    AddEllipseHole(vh, outerRect, radius, radius, color32);
                    break;
                case GuideMaskShape.Ellipse:
                    AddEllipseHole(vh, outerRect, targetSize.x * 0.5f, targetSize.y * 0.5f, color32);
                    break;
                default:
                    AddRectangleHole(vh, outerRect, holeLeft, holeRight, holeBottom, holeTop, color32);
                    break;
            }
        }

        private void AddRectangleHole(VertexHelper vh, Rect outerRect, float holeLeft, float holeRight, float holeBottom, float holeTop, Color32 color)
        {
            // 矩形镂空：在孔洞周围画 4 个矩形 (左、上、右、下)
            AddQuad(vh,
                new Vector2(outerRect.xMin, outerRect.yMax),
                new Vector2(holeLeft, outerRect.yMax),
                new Vector2(holeLeft, outerRect.yMin),
                new Vector2(outerRect.xMin, outerRect.yMin), color);

            AddQuad(vh,
                new Vector2(holeRight, outerRect.yMax),
                new Vector2(outerRect.xMax, outerRect.yMax),
                new Vector2(outerRect.xMax, outerRect.yMin),
                new Vector2(holeRight, outerRect.yMin), color);

            AddQuad(vh,
                new Vector2(holeLeft, outerRect.yMax),
                new Vector2(holeRight, outerRect.yMax),
                new Vector2(holeRight, holeTop),
                new Vector2(holeLeft, holeTop), color);

            AddQuad(vh,
                new Vector2(holeLeft, holeBottom),
                new Vector2(holeRight, holeBottom),
                new Vector2(holeRight, outerRect.yMin),
                new Vector2(holeLeft, outerRect.yMin), color);
        }

        private void AddEllipseHole(VertexHelper vh, Rect outerRect, float radiusX, float radiusY, Color32 color)
        {
            radiusX = Mathf.Max(0.01f, radiusX);
            radiusY = Mathf.Max(0.01f, radiusY);
            float holeBottom = targetCenter.y - radiusY;
            float holeTop = targetCenter.y + radiusY;

            AddHorizontalHole(vh, outerRect, holeBottom, holeTop, EllipseSegmentCount, y =>
            {
                float normalizedY = (y - targetCenter.y) / radiusY;
                float normalizedX = Mathf.Sqrt(Mathf.Max(0f, 1f - normalizedY * normalizedY));
                float halfWidth = radiusX * normalizedX;
                return new Vector2(targetCenter.x - halfWidth, targetCenter.x + halfWidth);
            }, color);
        }

        private void AddRoundedRectangleHole(VertexHelper vh, Rect outerRect, float holeLeft, float holeRight, float holeBottom, float holeTop, Color32 color)
        {
            float width = Mathf.Max(0f, holeRight - holeLeft);
            float height = Mathf.Max(0f, holeTop - holeBottom);
            float radius = Mathf.Clamp(cornerRadius, 0f, Mathf.Min(width, height) * 0.5f);

            if (radius <= 0.01f)
            {
                AddRectangleHole(vh, outerRect, holeLeft, holeRight, holeBottom, holeTop, color);
                return;
            }

            AddHorizontalHole(vh, outerRect, holeBottom, holeTop, RoundedRectangleSegmentCount, y =>
            {
                if (y >= holeBottom + radius && y <= holeTop - radius)
                {
                    return new Vector2(holeLeft, holeRight);
                }

                float circleCenterY = y > targetCenter.y ? holeTop - radius : holeBottom + radius;
                float dy = y - circleCenterY;
                float dx = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
                return new Vector2(holeLeft + radius - dx, holeRight - radius + dx);
            }, color);
        }

        private void AddHorizontalHole(VertexHelper vh, Rect outerRect, float holeBottom, float holeTop, int segments, Func<float, Vector2> getRangeAtY, Color32 color)
        {
            if (holeTop <= holeBottom)
            {
                return;
            }

            AddQuad(vh,
                new Vector2(outerRect.xMin, outerRect.yMax),
                new Vector2(outerRect.xMax, outerRect.yMax),
                new Vector2(outerRect.xMax, holeTop),
                new Vector2(outerRect.xMin, holeTop), color);

            AddQuad(vh,
                new Vector2(outerRect.xMin, holeBottom),
                new Vector2(outerRect.xMax, holeBottom),
                new Vector2(outerRect.xMax, outerRect.yMin),
                new Vector2(outerRect.xMin, outerRect.yMin), color);

            int safeSegments = Mathf.Max(1, segments);
            float step = (holeTop - holeBottom) / safeSegments;
            for (int i = 0; i < safeSegments; i++)
            {
                float y0 = holeBottom + step * i;
                float y1 = i == safeSegments - 1 ? holeTop : y0 + step;
                Vector2 range0 = getRangeAtY(y0);
                Vector2 range1 = getRangeAtY(y1);

                AddQuad(vh,
                    new Vector2(outerRect.xMin, y1),
                    new Vector2(range1.x, y1),
                    new Vector2(range0.x, y0),
                    new Vector2(outerRect.xMin, y0), color);

                AddQuad(vh,
                    new Vector2(range1.y, y1),
                    new Vector2(outerRect.xMax, y1),
                    new Vector2(outerRect.xMax, y0),
                    new Vector2(range0.y, y0), color);
            }
        }

        private void AddQuad(VertexHelper vh, Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, Color32 color)
        {
            int startIndex = vh.currentVertCount;

            vh.AddVert(new Vector3(tl.x, tl.y, 0), color, new Vector2(0, 1));
            vh.AddVert(new Vector3(tr.x, tr.y, 0), color, new Vector2(1, 1));
            vh.AddVert(new Vector3(br.x, br.y, 0), color, new Vector2(1, 0));
            vh.AddVert(new Vector3(bl.x, bl.y, 0), color, new Vector2(0, 0));

            vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }

        /// <summary>
        /// 事件拦截：当点击位置发生时调用。
        /// </summary>
        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
        {
            if (!hasTarget)
            {
                // 没有目标时，拦截一切点击
                return true;
            }

            // 将屏幕坐标系转换到当前遮罩的本地坐标系
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out Vector2 localPoint))
            {
                // 判断点击点是否在“孔洞”范围内
                float holeLeft = targetCenter.x - targetSize.x * 0.5f;
                float holeRight = targetCenter.x + targetSize.x * 0.5f;
                float holeTop = targetCenter.y + targetSize.y * 0.5f;
                float holeBottom = targetCenter.y - targetSize.y * 0.5f;

                bool inHole = false;

                if (maskShape == GuideMaskShape.Rectangle)
                {
                    inHole = localPoint.x >= holeLeft && localPoint.x <= holeRight &&
                             localPoint.y >= holeBottom && localPoint.y <= holeTop;
                }
                else if (maskShape == GuideMaskShape.RoundedRectangle)
                {
                    inHole = IsInsideRoundedRectangle(localPoint, holeLeft, holeRight, holeBottom, holeTop);
                }
                else if (maskShape == GuideMaskShape.Circle)
                {
                    // 圆形判定：距离圆心距离小于半径
                    float radius = Mathf.Max(targetSize.x, targetSize.y) * 0.5f;
                    float dist = Vector2.Distance(localPoint, targetCenter);
                    inHole = dist <= radius;
                }
                else if (maskShape == GuideMaskShape.Ellipse)
                {
                    float radiusX = Mathf.Max(0.01f, targetSize.x * 0.5f);
                    float radiusY = Mathf.Max(0.01f, targetSize.y * 0.5f);
                    float dx = (localPoint.x - targetCenter.x) / radiusX;
                    float dy = (localPoint.y - targetCenter.y) / radiusY;
                    inHole = dx * dx + dy * dy <= 1f;
                }

                // 如果在孔洞内，返回 false（表示不在图片上，不阻挡射线，射线会穿透到下层UI）
                // 如果在孔洞外，返回 true（表示点在黑幕上，阻挡射线，拦截点击）
                return !inHole;
            }

            return true;
        }

        private bool IsInsideRoundedRectangle(Vector2 point, float left, float right, float bottom, float top)
        {
            if (point.x < left || point.x > right || point.y < bottom || point.y > top)
            {
                return false;
            }

            float width = Mathf.Max(0f, right - left);
            float height = Mathf.Max(0f, top - bottom);
            float radius = Mathf.Clamp(cornerRadius, 0f, Mathf.Min(width, height) * 0.5f);
            if (radius <= 0.01f)
            {
                return true;
            }

            float innerLeft = left + radius;
            float innerRight = right - radius;
            float innerBottom = bottom + radius;
            float innerTop = top - radius;

            if ((point.x >= innerLeft && point.x <= innerRight) ||
                (point.y >= innerBottom && point.y <= innerTop))
            {
                return true;
            }

            float cornerX = point.x < innerLeft ? innerLeft : innerRight;
            float cornerY = point.y < innerBottom ? innerBottom : innerTop;
            return Vector2.SqrMagnitude(point - new Vector2(cornerX, cornerY)) <= radius * radius;
        }
    }
}
