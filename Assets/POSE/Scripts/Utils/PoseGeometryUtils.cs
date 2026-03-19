using UnityEngine;

/// <summary>
/// 姿态几何工具类。
/// 
/// 主要职责：
/// 1. 姿态相似度比较
/// 2. HumanPose 包围盒计算
/// 3. Display Space -> UI Rect 映射
/// 4. 检测框 / 取景框几何计算
/// 
/// 【坐标约定】
/// - 输入的人体姿态默认应为 Display Space
/// - BodyPart.ViewportPos 统一为左下原点的 0~1 Viewport 语义
/// - 若映射到 UI，则统一基于目标 RectTransform 的 rect 内插
/// 
/// 【横屏 / 竖屏稳定性】
/// - 这里所有 UI 映射都不依赖屏幕像素尺寸
/// - 只依赖 targetRect 的实际大小
/// - 因此在横屏、竖屏、黑边、Canvas 缩放场景下更稳定
/// 
/// 【便捷原则】
/// - 保留显式传入 referenceRect 的完整版
/// - 同时提供默认走 PoseManager.Instance.cameraView.rectTransform 的便捷重载
/// </summary>
public static class PoseGeometryUtils
{
    /// <summary>
    /// 骨骼连接定义。
    /// 每个元组表示一条骨骼：parent -> child
    /// </summary>
    private static readonly (int, int)[] parentChildrenTuples =
    {
        (0, 1), (1, 3), (0, 2), (2, 4),
        (5, 7), (7, 9),
        (5, 11), (11, 13), (13, 15),
        (6, 8), (8, 10),
        (6, 12), (12, 14), (14, 16)
    };

    /// <summary>
    /// 忽略头部时，从该索引开始比较骨骼。
    /// </summary>
    private const int BodyOnlyStartIndex = 4;

    /// <summary>
    /// 获取默认参考区域：PoseManager.cameraView.rectTransform
    /// </summary>
    private static RectTransform GetDefaultReferenceRect()
    {
        if (PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return null;

        return PoseManager.Instance.cameraView.rectTransform;
    }

    // ========================================================================
    // 动作比较
    // ========================================================================

    /// <summary>
    /// 比较两个人体姿态相似度，返回 0~100，越高越接近。
    /// 
    /// 原理：
    /// - 对固定骨骼连线的方向向量做夹角比较
    /// - 将误差累积并归一化为相似度分数
    /// 
    /// 特点：
    /// - 不依赖人物在画面中的绝对位置
    /// - 更关注姿态骨架形态本身
    /// - 适用于动作模仿、模板匹配、姿势评分
    /// 
    /// 参数说明：
    /// - diff：单条骨骼允许的标准误差角度，越大越宽松
    /// - includeHead：是否把头部纳入比较
    /// - toleranceRatio：免费容错比例，越大越宽松
    /// </summary>
    public static float CompareHumans(
        BodyPart[] standard,
        BodyPart[] player,
        float diff = 45f,
        bool includeHead = true,
        float toleranceRatio = 0.4f)
    {
        if (standard == null || player == null) return 0f;
        if (standard.Length == 0 || player.Length == 0) return 0f;

        float totalAngleDifference = 0f;
        float maxAllowedDifference = 0f;

        int startIdx = includeHead ? 0 : BodyOnlyStartIndex;
        int validBoneCount = 0;

        for (int i = startIdx; i < parentChildrenTuples.Length; i++)
        {
            Vector2 standardDir = GetDirection(standard, parentChildrenTuples[i]);
            Vector2 playerDir = GetDirection(player, parentChildrenTuples[i]);

            if (standardDir == Vector2.zero)
                continue;

            validBoneCount++;
            maxAllowedDifference += diff;

            if (playerDir != Vector2.zero)
            {
                float angleDifference = Vector2.Angle(standardDir, playerDir);
                totalAngleDifference += Mathf.Min(angleDifference, diff * 1.5f);
            }
            else
            {
                totalAngleDifference += diff;
            }
        }

        if (validBoneCount == 0 || maxAllowedDifference <= 0f)
            return 0f;

        toleranceRatio = Mathf.Clamp01(toleranceRatio);

        float freeTolerance = maxAllowedDifference * toleranceRatio;
        float effectiveDifference = Mathf.Max(0f, totalAngleDifference - freeTolerance);
        float effectiveMax = maxAllowedDifference - freeTolerance;

        if (effectiveMax <= 0f)
            return 100f;

        float errorRatio = Mathf.Clamp01(effectiveDifference / effectiveMax);
        float smoothError = errorRatio * errorRatio * (3f - 2f * errorRatio);

        return (1f - smoothError) * 100f;
    }

    /// <summary>
    /// 获取某条骨骼的方向向量。
    /// 若任一关键点无效，则返回 Vector2.zero。
    /// </summary>
    private static Vector2 GetDirection(BodyPart[] bodyParts, (int, int) parentChildTuple)
    {
        if (bodyParts == null) return Vector2.zero;
        if (parentChildTuple.Item1 >= bodyParts.Length || parentChildTuple.Item2 >= bodyParts.Length)
            return Vector2.zero;

        BodyPart part1 = bodyParts[parentChildTuple.Item1];
        BodyPart part2 = bodyParts[parentChildTuple.Item2];

        if (part1.hasValue && part2.hasValue)
            return (part2.ViewportPos - part1.ViewportPos).normalized;

        return Vector2.zero;
    }

    // ========================================================================
    // HumanPose 包围盒 / Rect 映射
    // ========================================================================

    /// <summary>
    /// 计算人体关键点整体包围盒。
    /// 
    /// 若 targetRect == null：
    /// - 返回 Display Space 下的 Viewport Rect（左下原点，0~1）
    /// 
    /// 若 targetRect != null：
    /// - 返回映射到指定 UI RectTransform 内部的本地 UI Rect
    /// 
    /// 说明：
    /// - 该包围盒基于 bodyParts 计算
    /// - 更适合做“真实关键点范围”的人物框、拍照框、判定框
    /// - 与 pose.box（检测框）不同
    /// </summary>
    public static Rect WholeRect(
        this HumanPose humanPose,
        RectTransform targetRect = null,
        BodyPartsType[] filterTypes = null,
        float padding = 0f)
    {
        BodyPart[] bodyParts = humanPose.bodyParts;
        if (bodyParts == null || bodyParts.Length == 0)
            return new Rect();

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        bool hasValidPoint = false;

        void UpdateMinMax(BodyPart part)
        {
            if (!part.hasValue) return;

            Vector2 pos = part.ViewportPos;
            if (pos.x < minX) minX = pos.x;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.y < minY) minY = pos.y;
            if (pos.y > maxY) maxY = pos.y;
            hasValidPoint = true;
        }

        if (filterTypes != null && filterTypes.Length > 0)
        {
            for (int i = 0; i < filterTypes.Length; i++)
                UpdateMinMax(humanPose.GetBodyPart(filterTypes[i]));
        }
        else
        {
            for (int i = 0; i < bodyParts.Length; i++)
                UpdateMinMax(bodyParts[i]);
        }

        if (!hasValidPoint)
            return new Rect();

        if (padding != 0f)
        {
            float w = maxX - minX;
            float h = maxY - minY;
            minX -= w * padding;
            maxX += w * padding;
            minY -= h * padding;
            maxY += h * padding;
        }

        minX = Mathf.Clamp01(minX);
        maxX = Mathf.Clamp01(maxX);
        minY = Mathf.Clamp01(minY);
        maxY = Mathf.Clamp01(maxY);

        Rect viewportRect = new Rect(minX, minY, maxX - minX, maxY - minY);

        if (targetRect == null)
            return viewportRect;

        return viewportRect.ToUIRect(targetRect);
    }

    /// <summary>
    /// 使用默认 cameraView 作为 referenceRect 的便捷版本。
    /// 
    /// 返回：
    /// - 映射到 cameraView.rectTransform 内的本地 UI Rect
    /// </summary>
    public static Rect WholeUIRect(this HumanPose humanPose, BodyPartsType[] filterTypes = null, float padding = 0f)
    {
        RectTransform referenceRect = GetDefaultReferenceRect();
        if (referenceRect == null) return new Rect();

        return humanPose.WholeRect(referenceRect, filterTypes, padding);
    }

    /// <summary>
    /// 将 Display Space 下的 Viewport Rect 映射到指定 UI RectTransform 内。
    /// </summary>
    public static Rect ToUIRect(this Rect viewportRect, RectTransform targetRect)
    {
        if (targetRect == null || viewportRect.width <= 0f || viewportRect.height <= 0f)
            return new Rect();

        Rect r = targetRect.rect;

        float xMin = Mathf.Lerp(r.xMin, r.xMax, viewportRect.xMin);
        float xMax = Mathf.Lerp(r.xMin, r.xMax, viewportRect.xMax);
        float yMin = Mathf.Lerp(r.yMin, r.yMax, viewportRect.yMin);
        float yMax = Mathf.Lerp(r.yMin, r.yMax, viewportRect.yMax);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    /// <summary>
    /// 将 pose.box（Display Space 检测框）映射到指定 UI 区域。
    /// 
    /// 适合：
    /// - 检测框显示
    /// - UI 跟随框
    /// - 人物编号框
    /// </summary>
    public static Rect BoxToUIRect(this HumanPose pose, RectTransform referenceRect)
    {
        if (referenceRect == null)
            return new Rect();

        Rect box = pose.box;
        if (box.width <= 0f || box.height <= 0f)
            return new Rect();

        float cx = box.x + box.width * 0.5f;
        float cy = box.y + box.height * 0.5f;

        BodyPart centerPart = new BodyPart { x = cx, y = cy, hasValue = true };
        Vector2 centerPos = centerPart.ToAnchoredPos(referenceRect);

        BodyPart topLeft = new BodyPart { x = box.x, y = box.y, hasValue = true };
        BodyPart bottomRight = new BodyPart { x = box.x + box.width, y = box.y + box.height, hasValue = true };

        Vector2 p1 = topLeft.ToAnchoredPos(referenceRect);
        Vector2 p2 = bottomRight.ToAnchoredPos(referenceRect);

        float uiWidth = Mathf.Abs(p2.x - p1.x);
        float uiHeight = Mathf.Abs(p2.y - p1.y);

        return new Rect(
            centerPos.x - uiWidth * 0.5f,
            centerPos.y - uiHeight * 0.5f,
            uiWidth,
            uiHeight
        );
    }

    /// <summary>
    /// 使用默认 cameraView 作为 referenceRect 的便捷版本。
    /// </summary>
    public static Rect BoxToUIRect(this HumanPose pose)
    {
        RectTransform referenceRect = GetDefaultReferenceRect();
        if (referenceRect == null) return new Rect();

        return pose.BoxToUIRect(referenceRect);
    }

    /// <summary>
    /// 基于 pose.box 构建固定宽高比的 UI 取景框。
    /// 
    /// 参数：
    /// - aspect：宽高比（宽 / 高）
    /// - scale：对原始 box 的整体放大倍率
    /// </summary>
    public static Rect BuildFrameUIRectFromBox(this HumanPose pose, RectTransform referenceRect, float aspect, float scale = 1.2f)
    {
        if (referenceRect == null || aspect <= 0f)
            return new Rect();

        Rect baseRect = pose.BoxToUIRect(referenceRect);
        if (baseRect.width <= 0f || baseRect.height <= 0f)
            return new Rect();

        Vector2 center = baseRect.center;
        float width = baseRect.width * scale;
        float height = baseRect.height * scale;

        float currentAspect = width / height;

        if (currentAspect > aspect)
            height = width / aspect;
        else
            width = height * aspect;

        return new Rect(
            center.x - width * 0.5f,
            center.y - height * 0.5f,
            width,
            height
        );
    }

    /// <summary>
    /// 使用默认 cameraView 作为 referenceRect 的便捷版本。
    /// </summary>
    public static Rect BuildFrameUIRectFromBox(this HumanPose pose, float aspect, float scale = 1.2f)
    {
        RectTransform referenceRect = GetDefaultReferenceRect();
        if (referenceRect == null) return new Rect();

        return pose.BuildFrameUIRectFromBox(referenceRect, aspect, scale);
    }
}