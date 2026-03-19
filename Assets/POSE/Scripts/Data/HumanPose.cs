using UnityEngine;

/// <summary>
/// 单个人体姿态数据。
/// 
/// 【全局坐标约定】
/// 本项目对外分发给业务层的 HumanPose，默认统一使用 Display Space（显示空间）：
/// 
/// - x：0~1，表示玩家看到画面中的 左 -> 右
/// - y：内部仍保留 0~1，且 0 在顶部（兼容常见检测模型输出）
/// - 当需要映射到 Unity Viewport / UI / Screen 时，统一通过
///   BodyPart.ViewportPos / ToAnchoredPos / ScreenPos 等方法转换
/// 
/// 【重要说明】
/// 1. HumanPose 在 Decoder 刚输出时，可能仍属于 Source Space（原始纹理空间）
/// 2. 经过 PosePostProcessor 后，才统一转换为 Display Space
/// 3. PoseManager 对外缓存和分发的 HumanPose，一律应视为 Display Space
/// 
/// 【Left / Right 语义】
/// - bodyParts 中的 Left / Right 始终表示“人体自身左右”
/// - 即使开启镜像显示，也不会交换关键点语义索引
/// - 例如 LeftWrist 永远表示人体自身左手，不表示“屏幕左边那只手”
/// </summary>
public struct HumanPose
{
    /// <summary>
    /// 跨帧跟踪 ID / 检测 ID。
    /// 
    /// 用于：
    /// - 多人稳定排序
    /// - 掉帧补偿
    /// - UI 编号显示
    /// - 玩家识别
    /// </summary>
    public int index;

    /// <summary>
    /// 身体关键点数组。
    /// 
    /// 数组索引与 BodyPartsType 一一对应。
    /// 约定：
    /// - 索引语义固定，不因镜像显示而改变
    /// - 坐标在最终业务层默认应为 Display Space
    /// </summary>
    public BodyPart[] bodyParts;

    /// <summary>
    /// 人体检测框，归一化 0~1。
    /// 
    /// 对外业务层默认约定为 Display Space：
    /// - x 表示玩家看到画面中的左右位置
    /// - width / height 为相对显示区域大小
    /// 
    /// 注意：
    /// - 若当前画面开启镜像，则此框已在 PosePostProcessor 中完成水平翻转
    /// - 截图时若要从原始纹理裁图，需要先通过
    ///   PosePhotoUtils.DisplayRectToSourceRect(...) 转回 Source Space
    /// </summary>
    public Rect box;

    /// <summary>
    /// 获取指定类型的关键点。
    /// 
    /// 若：
    /// - bodyParts 为空
    /// - 索引越界
    /// 则返回 default。
    /// </summary>
    public BodyPart GetBodyPart(BodyPartsType type)
    {
        if (bodyParts == null)
            return default;

        int idx = (int)type;
        if (idx < 0 || idx >= bodyParts.Length)
            return default;

        return bodyParts[idx];
    }

    /// <summary>
    /// 统计所有关键点置信度之和。
    /// 
    /// 可用于粗略评估：
    /// - 当前姿态整体可信度
    /// - 用于多目标排序的辅助指标
    /// </summary>
    public float TotalScore()
    {
        float score = 0f;

        if (bodyParts == null)
            return score;

        for (int i = 0; i < bodyParts.Length; i++)
        {
            score += bodyParts[i].score;
        }

        return score;
    }
}

/// <summary>
/// 单个身体关键点数据。
/// 
/// 【存储约定】
/// - x：最终业务使用的显示空间 X（Display Space X），0~1，左 -> 右
/// - y：内部仍保留“上 -> 下”的存储方式，0~1，0 在顶部
/// 
/// 为什么保留 y 为“上 -> 下”：
/// - 兼容大多数检测模型 / YOLO 输出
/// - 避免多个阶段反复翻转 y 造成语义混乱
/// 
/// 因此：
/// - 若你要做 Unity Viewport / UI / Screen 映射，请不要直接使用 x / y
/// - 应优先使用 ViewportPos / ScreenPos / ToAnchoredPos(...)
/// </summary>
public struct BodyPart
{
    /// <summary>
    /// 当前关键点在数组中的索引。
    /// 通常与 BodyPartsType 对应。
    /// </summary>
    public int index;

    /// <summary>
    /// 归一化 X 坐标，范围 0~1。
    /// 
    /// 语义：
    /// - 对外业务层默认视为 Display Space
    /// - x=0 表示玩家看到画面的最左侧
    /// - x=1 表示玩家看到画面的最右侧
    /// - 若开启镜像，该值已在 PosePostProcessor 中做过水平翻转
    /// </summary>
    public float x;

    /// <summary>
    /// 归一化 Y 坐标，范围 0~1。
    /// 
    /// 内部存储语义：
    /// - y=0 表示顶部
    /// - y=1 表示底部
    /// 
    /// 注意：
    /// - 这不是 Unity Viewport 的下 -> 上语义
    /// - 若要映射到 Unity UI / Viewport，请使用 ViewportPos / ToAnchoredPos
    /// </summary>
    public float y;

    /// <summary>
    /// 关键点置信度。
    /// </summary>
    public float score;

    /// <summary>
    /// 当前关键点是否有效。
    /// </summary>
    public bool hasValue;

    /// <summary>
    /// 转换为 Unity Viewport 坐标（左下为 0,0，右上为 1,1）。
    /// 
    /// 转换逻辑：
    /// - x 保持不变，因为 x 已经是 Display Space 左 -> 右
    /// - y 从“上 -> 下”翻成 Unity 常用的“下 -> 上”
    /// 
    /// 因此：
    /// - 适合做方向计算、骨骼角度比较、包围盒计算
    /// - 也适合做 UI 映射前的中间坐标
    /// </summary>
    public Vector2 ViewportPos => new Vector2(x, 1f - y);

    /// <summary>
    /// 转换为全屏像素坐标（Screen Space）。
    /// 
    /// 注意：
    /// - 基于整个屏幕尺寸计算
    /// - 若摄像头画面没有铺满全屏，而是显示在某个 RawImage 中，
    ///   则这个结果不适合做高精度 UI 对齐
    /// 
    /// 推荐：
    /// - UI 对齐优先使用 ToAnchoredPos(cameraView.rectTransform)
    /// </summary>
    public Vector2 ScreenPos => new Vector2(x * Screen.width, (1f - y) * Screen.height);

    /// <summary>
    /// 将关键点映射为世界坐标。
    /// 
    /// 原理：
    /// 1. 先将关键点映射到 cameraView 对应的 UI 本地坐标
    /// 2. 再转换为屏幕坐标
    /// 3. 最后由世界相机投射到指定深度
    /// 
    /// 适合：
    /// - 3D 特效跟随人体关键点
    /// - 世界空间物体跟随头、手、脚
    /// 
    /// 参数：
    /// - depth：相对于 worldCam 的深度
    /// - worldCam：世界相机，若为空则使用 Camera.main
    /// 
    /// 返回：
    /// - 成功时返回世界坐标
    /// - 若缺少 cameraView 或 worldCam，返回 Vector3.zero
    /// </summary>
    public Vector3 ToWorldPos(float depth = 10f, Camera worldCam = null)
    {
        if (worldCam == null)
            worldCam = Camera.main;

        if (worldCam == null || PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return Vector3.zero;

        RectTransform targetRect = PoseManager.Instance.cameraView.rectTransform;

        // 1. 映射到摄像头显示区域的本地 UI 坐标
        Vector2 anchoredPos = ToAnchoredPos(targetRect);

        // 2. 本地 UI 坐标 -> UI 世界坐标
        Vector3 uiWorldPos = targetRect.TransformPoint(new Vector3(anchoredPos.x, anchoredPos.y, 0f));

        // 3. UI 世界坐标 -> 屏幕像素坐标
        Canvas canvas = targetRect.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera
            : null;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCam, uiWorldPos);

        // 4. 屏幕像素坐标 -> 世界坐标
        return worldCam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
    }

    /// <summary>
    /// 【传统方案】通过 ScreenPos 映射到指定 RectTransform 的局部坐标。
    /// 
    /// 缺点：
    /// - 依赖全屏 Screen 坐标
    /// - 如果摄像头画面不是铺满屏幕，而是显示在某个 UI 区域中，
    ///   可能在横屏 / 竖屏 / 黑边 / 安全区情况下出现偏移
    /// 
    /// 一般不推荐作为主方案。
    /// </summary>
    public Vector2 ToLocalUIPos(RectTransform rectTransform, Camera uiCamera = null)
    {
        if (rectTransform == null)
            return Vector2.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            ScreenPos,
            uiCamera,
            out Vector2 localPoint
        );

        return localPoint;
    }

    /// <summary>
    /// 【推荐】将关键点直接映射到指定 RectTransform 的 anchoredPosition 坐标系。
    /// 
    /// 优点：
    /// - 不依赖屏幕分辨率
    /// - 不依赖当前横屏 / 竖屏
    /// - 不怕黑边、等比缩放、Canvas 缩放
    /// - 只要 targetRect 就是“真实显示摄像头画面的 UI 区域”，映射就稳定
    /// 
    /// 前提：
    /// - x 已是 Display Space 左 -> 右
    /// - y 内部仍是上 -> 下，因此这里会自动做 1f-y 转换
    /// 
    /// 推荐传入：
    /// - PoseManager.Instance.cameraView.rectTransform
    /// </summary>
    public Vector2 ToAnchoredPos(RectTransform targetRect)
    {
        if (targetRect == null)
            return Vector2.zero;

        Rect r = targetRect.rect;

        float targetX = Mathf.Lerp(r.xMin, r.xMax, x);
        float targetY = Mathf.Lerp(r.yMin, r.yMax, 1f - y);

        return new Vector2(targetX, targetY);
    }
}

/// <summary>
/// 身体关键点索引定义。
/// 
/// 注意：
/// - Left / Right 始终表示人体自身左右
/// - 不因镜像显示而交换
/// </summary>
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