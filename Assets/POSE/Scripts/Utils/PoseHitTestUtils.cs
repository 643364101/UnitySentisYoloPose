using UnityEngine;

/// <summary>
/// 姿态命中检测工具类。
/// 
/// 主要职责：
/// 1. 关键点 -> UI 世界坐标 / 屏幕坐标映射
/// 2. UI 命中检测
/// 3. 3D Collider 命中检测
/// 
/// 【设计原则】
/// - 输入的 BodyPart 默认属于 Display Space
/// - sourceRect 应传“实际显示姿态的区域”，通常是 cameraView.rectTransform
/// - 对于跨 Canvas / 横屏 / 竖屏场景，优先使用屏幕点方案
/// 
/// 【便捷原则】
/// 默认便捷版本会自动使用：
/// PoseManager.Instance.cameraView.rectTransform
/// 作为 sourceRect。
/// </summary>
public static class PoseHitTestUtils
{
    /// <summary>
    /// 获取默认 sourceRect：PoseManager.cameraView.rectTransform
    /// </summary>
    private static RectTransform GetDefaultSourceRect()
    {
        if (PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return null;

        return PoseManager.Instance.cameraView.rectTransform;
    }

    /// <summary>
    /// 获取某个 RectTransform 所在 Canvas 对应的 UI Camera。
    /// 
    /// 返回规则：
    /// - Overlay 模式返回 null
    /// - 其他模式返回 canvas.worldCamera
    /// </summary>
    public static Camera GetUICamera(RectTransform rect)
    {
        if (rect == null) return null;

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return null;

        return canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
    }

    /// <summary>
    /// 将关键点映射到 sourceRect 所在 UI 上的世界坐标。
    /// 
    /// 前提：
    /// - part 为 Display Space 数据
    /// - sourceRect 为实际显示摄像头画面的区域
    /// </summary>
    public static Vector3 GetUIWorldPoint(BodyPart part, RectTransform sourceRect)
    {
        if (!part.hasValue || sourceRect == null)
            return Vector3.zero;

        return sourceRect.TransformPoint(part.ToAnchoredPos(sourceRect));
    }

    /// <summary>
    /// 使用默认 cameraView 作为 sourceRect。
    /// </summary>
    public static Vector3 GetUIWorldPoint(BodyPart part)
    {
        RectTransform sourceRect = GetDefaultSourceRect();
        if (sourceRect == null) return Vector3.zero;

        return GetUIWorldPoint(part, sourceRect);
    }

    /// <summary>
    /// 将关键点映射为屏幕坐标。
    /// 
    /// 推荐 sourceRect 传入：
    /// - PoseManager.Instance.cameraView.rectTransform
    /// </summary>
    public static Vector2 GetScreenPoint(BodyPart part, RectTransform sourceRect)
    {
        if (!part.hasValue || sourceRect == null)
            return Vector2.zero;

        return RectTransformUtility.WorldToScreenPoint(
            GetUICamera(sourceRect),
            GetUIWorldPoint(part, sourceRect)
        );
    }

    /// <summary>
    /// 使用默认 cameraView 作为 sourceRect。
    /// </summary>
    public static Vector2 GetScreenPoint(BodyPart part)
    {
        RectTransform sourceRect = GetDefaultSourceRect();
        if (sourceRect == null) return Vector2.zero;

        return GetScreenPoint(part, sourceRect);
    }

    /// <summary>
    /// 判断关键点是否落在某个 UI Rect 内。
    /// 
    /// 推荐作为通用 UI 命中检测方案：
    /// - 先将姿态点映射为屏幕点
    /// - 再交给 Unity 原生 UI 命中判断
    /// 
    /// 优点：
    /// - 更适合不同 Canvas、不同层级、不同缩放体系
    /// - 横屏 / 竖屏更稳定
    /// </summary>
    public static bool IsOverUI(
        BodyPart part,
        RectTransform sourceRect,
        RectTransform targetRect,
        Camera targetUICamera = null)
    {
        if (!part.hasValue || sourceRect == null || targetRect == null)
            return false;

        Vector2 screenPoint = GetScreenPoint(part, sourceRect);

        if (targetUICamera == null)
            targetUICamera = GetUICamera(targetRect);

        return RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPoint, targetUICamera);
    }

    /// <summary>
    /// 使用默认 cameraView 作为 sourceRect。
    /// </summary>
    public static bool IsOverUI(BodyPart part, RectTransform targetRect, Camera targetUICamera = null)
    {
        RectTransform sourceRect = GetDefaultSourceRect();
        if (sourceRect == null) return false;

        return IsOverUI(part, sourceRect, targetRect, targetUICamera);
    }

    /// <summary>
    /// 同局部空间方案下的 UI 命中检测。
    /// 
    /// 只推荐在以下情况下使用：
    /// - sourceRect 和 targetRect 明确位于同一 UI 变换体系
    /// - 例如 targetRect 是 cameraView 的子节点
    /// 
    /// 若不确定，优先使用 IsOverUI。
    /// </summary>
    public static bool IsOverUILocal(BodyPart part, RectTransform sourceRect, RectTransform targetRect)
    {
        if (!part.hasValue || sourceRect == null || targetRect == null)
            return false;

        Vector3 worldPos = sourceRect.TransformPoint(part.ToAnchoredPos(sourceRect));
        return targetRect.rect.Contains(targetRect.InverseTransformPoint(worldPos));
    }

    /// <summary>
    /// 使用默认 cameraView 作为 sourceRect。
    /// </summary>
    public static bool IsOverUILocal(BodyPart part, RectTransform targetRect)
    {
        RectTransform sourceRect = GetDefaultSourceRect();
        if (sourceRect == null) return false;

        return IsOverUILocal(part, sourceRect, targetRect);
    }

    /// <summary>
    /// 判断关键点是否命中某个 3D Collider。
    /// 
    /// 原理：
    /// - 关键点 -> 屏幕坐标
    /// - 屏幕坐标 -> 世界射线
    /// - 射线命中目标 Collider
    /// </summary>
    public static bool IsTouching3D(
        BodyPart part,
        RectTransform sourceRect,
        Collider targetCollider,
        Camera worldCamera,
        float maxDistance = 100f,
        int layerMask = Physics.DefaultRaycastLayers)
    {
        if (!part.hasValue || sourceRect == null || targetCollider == null || worldCamera == null)
            return false;

        Ray ray = worldCamera.ScreenPointToRay(GetScreenPoint(part, sourceRect));
        return Physics.Raycast(ray, out RaycastHit hit, maxDistance, layerMask) && hit.collider == targetCollider;
    }

    /// <summary>
    /// 使用默认 cameraView 作为 sourceRect。
    /// </summary>
    public static bool IsTouching3D(
        BodyPart part,
        Collider targetCollider,
        Camera worldCamera,
        float maxDistance = 100f,
        int layerMask = Physics.DefaultRaycastLayers)
    {
        RectTransform sourceRect = GetDefaultSourceRect();
        if (sourceRect == null) return false;

        return IsTouching3D(part, sourceRect, targetCollider, worldCamera, maxDistance, layerMask);
    }

    /// <summary>
    /// 使用默认 cameraView + Camera.main。
    /// </summary>
    public static bool IsTouching3D(
        BodyPart part,
        Collider targetCollider,
        float maxDistance = 100f,
        int layerMask = Physics.DefaultRaycastLayers)
    {
        return IsTouching3D(part, targetCollider, Camera.main, maxDistance, layerMask);
    }
}