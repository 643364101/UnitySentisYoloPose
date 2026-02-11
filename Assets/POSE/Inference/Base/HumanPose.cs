using UnityEngine;

public struct HumanPose
{
    public int index;
    public BodyPart[] bodyParts;
    public Rect box;

    public BodyPart GetBodyParts(BodyPartsType type)
    {
        if (bodyParts == null || (int)type >= bodyParts.Length) return default;
        return bodyParts[(int)type];
    }

    public float TotalScore()
    {
        float score = 0;
        if (bodyParts != null)
        {
            for (int i = 0; i < bodyParts.Length; i++)
                score += bodyParts[i].score;
        }

        return score;
    }
}

public struct BodyPart
{
    public int index;
    public float x; // 0-1 (YOLO原始 X)
    public float y; // 0-1 (YOLO原始 Y, 0在顶部)
    public float score;
    public bool hasValue;

    /// <summary>
    /// 获取修正后的 Viewport 坐标 (0-1)。
    /// <para>Unity Viewport: 左下(0,0)，右上(1,1)</para>
    /// </summary>
    public Vector2 ViewportPos => new Vector2(x, 1f - y);

    /// <summary>
    /// 获取屏幕像素坐标 (Screen Space)
    /// <para>注意：这基于全屏分辨率。如果画面有黑边，此坐标可能不准。</para>
    /// </summary>
    public Vector2 ScreenPos => new Vector2(x * Screen.width, (1f - y) * Screen.height);

    /// <summary>
    /// 获取 3D 世界坐标
    /// </summary>
    public Vector3 ToWorldPos(float depth = 10f, Camera cam = null)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return Vector3.zero;

        Vector3 viewportPoint = new Vector3(x, 1f - y, depth);
        return cam.ViewportToWorldPoint(viewportPoint);
    }

    /// <summary>
    /// 【传统方法】获取相对于指定 RectTransform 的本地 UI 坐标
    /// 依赖屏幕像素转换，适合全屏无黑边的情况。
    /// </summary>
    public Vector2 ToLocalUIPos(RectTransform rectTransform, Camera uiCamera = null)
    {
        if (rectTransform == null) return Vector2.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            ScreenPos,
            uiCamera,
            out Vector2 localPoint
        );

        return localPoint;
    }

    /// <summary>
    /// 【推荐：万能映射】直接获取相对于指定 RectTransform 的 anchoredPosition
    /// <para>完美解决竖屏、横屏、缩放、黑边等所有适配问题。</para>
    /// <para>原理：忽略屏幕分辨率，直接在目标 Rect 的尺寸内进行插值。</para>
    /// </summary>
    /// <param name="targetRect">显示摄像头的 RawImage</param>
    public Vector2 ToAnchoredPos(RectTransform targetRect)
    {
        if (targetRect == null) return Vector2.zero;

        // 获取目标矩形的内部尺寸范围 (相对于 Pivot)
        Rect r = targetRect.rect;

        // x (0~1) 映射到 (Left ~ Right)
        float targetX = Mathf.Lerp(r.xMin, r.xMax, x);

        // y (0~1) 映射到 (Bottom ~ Top) 注意 Y 轴反转
        float targetY = Mathf.Lerp(r.yMin, r.yMax, 1f - y);

        return new Vector2(targetX, targetY);
    }
}

public enum BodyPartsType
{
    Nose = 0,
    LeftEye,
    RightEye,
    LeftEar,
    RightEar,
    LeftShoulder,
    RightShoulder,
    LeftElbow,
    RightElbow,
    LeftWrist,
    RightWrist,
    LeftHip,
    RightHip,
    LeftKnee,
    RightKnee,
    LeftAnkle,
    RightAnkle,
}