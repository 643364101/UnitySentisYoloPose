using UnityEngine;

public static class PoseUIUtils
{
    // ... (保留之前的 IsInsideLocal 和 IsInsideScreen) ...

    public static bool IsInsideLocal(BodyPart part, RectTransform sourceRect, RectTransform targetArea)
    {
        if (!part.hasValue || sourceRect == null || targetArea == null) return false;
        Vector2 localInSource = part.ToAnchoredPos(sourceRect);
        Vector3 worldPos = sourceRect.TransformPoint(localInSource);
        Vector3 localInTarget = targetArea.InverseTransformPoint(worldPos);
        return targetArea.rect.Contains(localInTarget);
    }

    public static bool IsInsideScreen(BodyPart part, RectTransform sourceRect, RectTransform targetArea, Camera uiCamera)
    {
        if (!part.hasValue || sourceRect == null || targetArea == null) return false;
        Vector2 localInSource = part.ToAnchoredPos(sourceRect);
        Vector3 worldPos = sourceRect.TransformPoint(localInSource);
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, worldPos);
        return RectTransformUtility.RectangleContainsScreenPoint(targetArea, screenPoint, uiCamera);
    }

    // ========================================================================
    // 方案 C：3D 物理射线检测 (新增)
    // ========================================================================
    /// <summary>
    /// [3D物体专用] 判断骨骼点是否“点击”到了 3D 物体 (Collider)。
    /// <para>原理：将骨骼点映射为屏幕坐标 -> 发射射线 -> 检测 Collider。</para>
    /// </summary>
    /// <param name="targetCollider">目标物体的碰撞体</param>
    /// <param name="mainCamera">渲染 3D 场景的相机 (通常是 Camera.main)</param>
    /// <param name="maxDistance">射线最大检测距离</param>
    /// <param name="layerMask">层级过滤</param>
    public static bool
        IsHitting3D(BodyPart part, RectTransform sourceRect, Collider targetCollider, Camera mainCamera, float maxDistance = 100f, int layerMask = -5) // -5 is Default + others
    {
        if (!part.hasValue || sourceRect == null || targetCollider == null || mainCamera == null) return false;

        // 1. 获取骨骼点在 Source UI 上的世界坐标
        Vector2 localInSource = part.ToAnchoredPos(sourceRect);
        Vector3 uiWorldPos = sourceRect.TransformPoint(localInSource);

        // 2. 将 UI 世界坐标转为屏幕坐标
        // 注意：这里假设 sourceRect 所在的 Canvas 是 Overlay 或者与 mainCamera 对齐的 Camera 模式
        // 如果 sourceRect 是 Overlay，Camera 参数传 null
        Camera uiCam = sourceRect.GetComponentInParent<Canvas>().worldCamera;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, uiWorldPos);

        // 3. 发射射线
        Ray ray = mainCamera.ScreenPointToRay(screenPos);

        // 4. 检测是否击中目标
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, layerMask))
        {
            return hit.collider == targetCollider;
        }

        return false;
    }

    /// <summary>
    /// 计算人体姿态的包围盒 (Bounding Box)
    /// </summary>
    /// <param name="humanPose">姿态数据</param>
    /// <param name="targetRect">参考 UI 容器（如 Webcam RawImage），若为 null 则返回 Viewport 坐标 (0-1)</param>
    /// <param name="filterTypes">可选：仅计算指定的关键点（如仅上半身）</param>
    /// <param name="padding">可选：边距比例 (例如 0.1 表示向四周扩张 10%)</param>
    public static Rect WholeRect(this HumanPose humanPose, RectTransform targetRect = null, BodyPartsType[] filterTypes = null, float padding = 0f)
    {
        var bodyParts = humanPose.bodyParts;
        if (bodyParts == null || bodyParts.Length == 0) return new Rect();

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool hasValidPoint = false;

        // --- 1. 遍历计算边界 (Viewport 空间 0~1) ---
        if (filterTypes != null && filterTypes.Length > 0)
        {
            foreach (var type in filterTypes)
            {
                var part = humanPose.GetBodyParts(type);
                if (part.hasValue) UpdateMinMax(part.ViewportPos, ref minX, ref maxX, ref minY, ref maxY, ref hasValidPoint);
            }
        }
        else
        {
            foreach (var part in bodyParts)
            {
                if (part.hasValue) UpdateMinMax(part.ViewportPos, ref minX, ref maxX, ref minY, ref maxY, ref hasValidPoint);
            }
        }

        if (!hasValidPoint) return new Rect();

        // --- 2. 应用 Padding (在 Viewport 空间计算) ---
        if (padding != 0)
        {
            float w = maxX - minX;
            float h = maxY - minY;
            minX -= w * padding;
            maxX += w * padding;
            minY -= h * padding;
            maxY += h * padding;
        }

        // --- 3. 结果输出 ---
        if (targetRect == null)
        {
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        // 核心映射：利用插值算法完美适配 UI
        Rect r = targetRect.rect;
        float uiMinX = Mathf.Lerp(r.xMin, r.xMax, minX);
        float uiMaxX = Mathf.Lerp(r.xMin, r.xMax, maxX);
        float uiMinY = Mathf.Lerp(r.yMin, r.yMax, minY);
        float uiMaxY = Mathf.Lerp(r.yMin, r.yMax, maxY);

        return new Rect(uiMinX, uiMinY, uiMaxX - uiMinX, uiMaxY - uiMinY);
    }

    private static void UpdateMinMax(Vector2 pos, ref float minX, ref float maxX, ref float minY, ref float maxY, ref bool hasValidPoint)
    {
        if (pos.x < minX) minX = pos.x;
        if (pos.x > maxX) maxX = pos.x;
        if (pos.y < minY) minY = pos.y;
        if (pos.y > maxY) maxY = pos.y;
        hasValidPoint = true;
    }
}

// ========================================================================
// 扩展方法
// ========================================================================
public static class BodyPartExtensions
{
    // ... (保留之前的 IsInside 和 IsInside3D) ...
    public static bool IsInside(this BodyPart part, RectTransform targetArea)
    {
        if (PoseManager.Instance.cameraView == null) return false;
        return PoseUIUtils.IsInsideLocal(part, PoseManager.Instance.cameraView.rectTransform, targetArea);
    }

    public static bool IsInside(this BodyPart part, RectTransform sourceRect, RectTransform targetArea)
    {
        return PoseUIUtils.IsInsideLocal(part, sourceRect, targetArea);
    }

    public static bool IsInside3D(this BodyPart part, RectTransform targetArea, Camera uiCamera)
    {
        if (PoseManager.Instance.cameraView == null) return false;
        return PoseUIUtils.IsInsideScreen(part, PoseManager.Instance.cameraView.rectTransform, targetArea, uiCamera);
    }

    // --- 新增：3D 物体检测 ---

    /// <summary>
    /// 判断是否触碰了 3D 物体 (需要物体有 Collider)
    /// </summary>
    public static bool IsTouching(this BodyPart part, Collider targetCollider, float maxDistance = 100f)
    {
        if (PoseManager.Instance.cameraView == null) return false;

        // 自动使用 Camera.main
        return PoseUIUtils.IsHitting3D(
            part,
            PoseManager.Instance.cameraView.rectTransform,
            targetCollider,
            Camera.main,
            maxDistance
        );
    }

    /// <summary>
    /// 判断是否触碰了 3D 物体 (指定相机)
    /// </summary>
    public static bool IsTouching(this BodyPart part, Collider targetCollider, Camera camera, float maxDistance = 100f)
    {
        if (PoseManager.Instance.cameraView == null) return false;

        return PoseUIUtils.IsHitting3D(
            part,
            PoseManager.Instance.cameraView.rectTransform,
            targetCollider,
            camera,
            maxDistance
        );
    }
}