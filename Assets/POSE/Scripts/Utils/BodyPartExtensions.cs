using UnityEngine;

/// <summary>
/// BodyPart 常用扩展方法。
/// 
/// 设计目标：
/// - 为业务层提供最常用的“便捷调用”
/// - 默认使用 PoseManager.Instance.cameraView.rectTransform
///   作为姿态显示参考区域
/// 
/// 适合场景：
/// - UI 按钮碰撞
/// - 区域检测
/// - 3D 交互
/// 
/// 注意：
/// - 这些扩展方法面向业务层快速开发
/// - 若你在框架层或特殊场景中需要指定 sourceRect / Camera，
///   请优先调用 PoseHitTestUtils 的完整版 API
/// </summary>
public static class BodyPartExtensions
{
    /// <summary>
    /// 判断关键点是否位于某个 UI Rect 内。
    /// 默认使用 PoseManager.cameraView 作为姿态来源区域。
    /// </summary>
    public static bool IsInsideUI(this BodyPart part, RectTransform targetRect)
    {
        return PoseHitTestUtils.IsOverUI(part, targetRect);
    }

    /// <summary>
    /// 指定 sourceRect 的 UI 命中检测。
    /// 
    /// 适用于：
    /// - 非默认 cameraView 显示区域
    /// - 特殊测试 / 工具脚本
    /// </summary>
    public static bool IsInsideUI(this BodyPart part, RectTransform sourceRect, RectTransform targetRect)
    {
        return PoseHitTestUtils.IsOverUI(part, sourceRect, targetRect, null);
    }

    /// <summary>
    /// 指定 sourceRect + targetUICamera 的 UI 命中检测。
    /// 
    /// 适用于：
    /// - 独立 Canvas
    /// - 自定义 UI Camera
    /// - 特殊 UI 结构
    /// </summary>
    public static bool IsInsideUI(this BodyPart part, RectTransform sourceRect, RectTransform targetRect, Camera targetUICamera)
    {
        return PoseHitTestUtils.IsOverUI(part, sourceRect, targetRect, targetUICamera);
    }

    /// <summary>
    /// 同局部空间下的 UI 命中检测。
    /// 
    /// 默认使用 PoseManager.cameraView 作为姿态来源区域。
    /// 
    /// 注意：
    /// - 仅推荐在 sourceRect 与 targetRect 明确属于同一 UI 变换体系时使用
    /// - 若不确定，优先使用 IsInsideUI
    /// </summary>
    public static bool IsInsideUILocal(this BodyPart part, RectTransform targetRect)
    {
        return PoseHitTestUtils.IsOverUILocal(part, targetRect);
    }

    /// <summary>
    /// 指定 sourceRect 的同局部空间 UI 命中检测。
    /// </summary>
    public static bool IsInsideUILocal(this BodyPart part, RectTransform sourceRect, RectTransform targetRect)
    {
        return PoseHitTestUtils.IsOverUILocal(part, sourceRect, targetRect);
    }

    /// <summary>
    /// 判断关键点是否命中某个 3D Collider。
    /// 默认使用 Camera.main。
    /// </summary>
    public static bool IsTouching3D(this BodyPart part, Collider targetCollider, float maxDistance = 100f)
    {
        return PoseHitTestUtils.IsTouching3D(part, targetCollider, Camera.main, maxDistance);
    }

    /// <summary>
    /// 判断关键点是否命中某个 3D Collider。
    /// 可手动指定世界相机。
    /// </summary>
    public static bool IsTouching3D(this BodyPart part, Collider targetCollider, Camera worldCamera, float maxDistance = 100f)
    {
        return PoseHitTestUtils.IsTouching3D(part, targetCollider, worldCamera, maxDistance);
    }
}