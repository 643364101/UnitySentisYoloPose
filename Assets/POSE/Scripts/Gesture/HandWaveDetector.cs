using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 手势类型。
/// 
/// 注意：
/// - Left / Right 始终表示人体自身左右
/// - 不因镜像显示而交换
/// </summary>
public enum GestureType
{
    None,
    LeftMiddle,
    RightMiddle,
    LeftUp,
    RightUp,
}

/// <summary>
/// 挥手 / 抬手 / 平举手势检测器。
/// 
/// 输入默认来自 PoseManager 分发的最终姿态数据（Display Space）。
/// 内部统一使用 ViewportPos 做几何判断，因此与横屏 / 竖屏无关。
/// 
/// 当前流程：
/// 1. 监听 OnFilteringPoseUpdated
/// 2. 从指定区域中选出主玩家
/// 3. 根据肩、肘、腕、髋、鼻子的相对关系判断手势
/// 4. 做短时防抖
/// 5. 将确认后的手势结果发送给 PoseManager
/// </summary>
public class HandWaveDetector : MonoBehaviour
{
    [Header("状态（只读）")]
    public GestureType currentType;

    /// <summary>
    /// 最近一次已经确认并广播出去的手势。
    /// </summary>
    private GestureType _lastSentType = GestureType.None;

    [Header("防抖设置")]
    /// <summary>
    /// 当前候选手势。
    /// </summary>
    private GestureType _pendingGesture = GestureType.None;

    /// <summary>
    /// 候选手势开始持续的时间戳。
    /// </summary>
    private float _pendingStartTime = -1f;

    [Tooltip("手势需要维持多久才确认切换。")]
    public float gestureHoldTime = 0.12f;

    [Header("判定阈值")]
    [Range(0f, 1f)]
    [Tooltip("参与判定的关键点最小分数。")]
    public float minScore = 0.3f;

    [Tooltip("肩-肘 与 肘-腕 的夹角阈值，越大越宽松。")]
    public float armStraightThreshold = 70f;

    [Tooltip("平举判定：手臂与躯干夹角相对 90 度的容差。")]
    public float middleAngleTolerance = 40f;

    [Tooltip("平举判定最小横向跨度，防止手缩在胸前误判。")]
    public float minArmSpan = 0.08f;

    [Tooltip("举高判定：手臂与躯干夹角大于该值视为抬高。")]
    public float upAngleThreshold = 110f;

    private void OnEnable()
    {
        if (PoseManager.Instance != null)
            PoseManager.Instance.OnFilteringPoseUpdated += OnFilteringPoseUpdated;
    }

    private void OnDisable()
    {
        if (PoseManager.Instance != null)
            PoseManager.Instance.OnFilteringPoseUpdated -= OnFilteringPoseUpdated;
    }

    /// <summary>
    /// 区域过滤结果更新回调。
    /// 当前仅处理 id == 0 的区域。
    /// </summary>
    private void OnFilteringPoseUpdated(HumanPoseArea humanPoseArea)
    {
        if (humanPoseArea == null || humanPoseArea.id != 0)
            return;

        HumanPose? targetPose = FindPrimaryUser(humanPoseArea.humanPoses);
        GestureType rawGesture = CalculateGesture(targetPose);
        UpdateGestureStability(rawGesture);
    }

    /// <summary>
    /// 计算当前姿态的原始手势结果（未经过防抖）。
    /// 判定顺序：
    /// 1. 右手
    /// 2. 左手
    /// 3. 无手势
    /// </summary>
    private GestureType CalculateGesture(HumanPose? nullablePose)
    {
        if (nullablePose == null)
            return GestureType.None;

        HumanPose pose = nullablePose.Value;

        GestureType rightResult = CheckSingleArm(pose, false);
        if (rightResult != GestureType.None)
            return rightResult;

        GestureType leftResult = CheckSingleArm(pose, true);
        if (leftResult != GestureType.None)
            return leftResult;

        return GestureType.None;
    }

    /// <summary>
    /// 检测单侧手臂是否构成目标手势。
    /// 
    /// 判定依据：
    /// - 手臂是否足够伸直
    /// - 手臂与躯干夹角
    /// - 手腕是否高于头部
    /// - 横向展开是否足够
    /// </summary>
    private GestureType CheckSingleArm(HumanPose pose, bool isLeft)
    {
        BodyPart shoulder = pose.GetBodyPart(isLeft ? BodyPartsType.LeftShoulder : BodyPartsType.RightShoulder);
        BodyPart elbow = pose.GetBodyPart(isLeft ? BodyPartsType.LeftElbow : BodyPartsType.RightElbow);
        BodyPart wrist = pose.GetBodyPart(isLeft ? BodyPartsType.LeftWrist : BodyPartsType.RightWrist);
        BodyPart hip = pose.GetBodyPart(isLeft ? BodyPartsType.LeftHip : BodyPartsType.RightHip);
        BodyPart nose = pose.GetBodyPart(BodyPartsType.Nose);

        if (!shoulder.hasValue || !elbow.hasValue || !wrist.hasValue ||
            shoulder.score < minScore || elbow.score < minScore || wrist.score < minScore)
        {
            return GestureType.None;
        }

        Vector2 shoulderPos = shoulder.ViewportPos;
        Vector2 elbowPos = elbow.ViewportPos;
        Vector2 wristPos = wrist.ViewportPos;
        Vector2 nosePos = nose.hasValue ? nose.ViewportPos : Vector2.zero;

        // 肩-肘 与 肘-腕 两段方向的夹角，越小越接近一条直线
        float armStraightAngle = GetAngle2D(shoulderPos, elbowPos, elbowPos, wristPos);

        // 用肩->髋近似表示身体向下方向
        Vector2 bodyDownVec = (hip.hasValue && hip.score > minScore)
            ? (hip.ViewportPos - shoulderPos)
            : Vector2.down;

        Vector2 armVec = wristPos - shoulderPos;
        float armBodyAngle = Vector2.Angle(bodyDownVec, armVec);

        // 横向展开跨度，防止手缩在胸前误判平举
        float xSpan = Mathf.Abs(wristPos.x - shoulderPos.x);

        if (armStraightAngle > armStraightThreshold)
            return GestureType.None;

        bool isAngleUp = armBodyAngle >= upAngleThreshold;
        bool isHeightUp = nose.hasValue && (wristPos.y > nosePos.y);

        // 防止手只是贴着头部中线附近经过
        if (isHeightUp && nose.hasValue)
        {
            if (Mathf.Abs(wristPos.x - nosePos.x) < 0.05f)
                isHeightUp = false;
        }

        if (isAngleUp || isHeightUp)
        {
            return isLeft ? GestureType.LeftUp : GestureType.RightUp;
        }

        if (Mathf.Abs(90f - armBodyAngle) <= middleAngleTolerance)
        {
            if (xSpan > minArmSpan)
            {
                return isLeft ? GestureType.LeftMiddle : GestureType.RightMiddle;
            }
        }

        return GestureType.None;
    }

    /// <summary>
    /// 计算两条 2D 线段方向的夹角。
    /// </summary>
    private float GetAngle2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        Vector2 v1 = (p2 - p1).normalized;
        Vector2 v2 = (p4 - p3).normalized;
        return Vector2.Angle(v1, v2);
    }

    /// <summary>
    /// 更新手势稳定状态（防抖）。
    /// 只有新手势持续达到一定时间后，才真正切换。
    /// </summary>
    private void UpdateGestureStability(GestureType rawGesture)
    {
        if (rawGesture == _lastSentType)
        {
            _pendingGesture = rawGesture;
            _pendingStartTime = -1f;
            return;
        }

        if (rawGesture == _pendingGesture)
        {
            if (_pendingStartTime >= 0f && Time.time - _pendingStartTime >= gestureHoldTime)
            {
                DispatchGestureEvent(rawGesture);
                _pendingStartTime = -1f;
            }

            return;
        }

        _pendingGesture = rawGesture;
        _pendingStartTime = Time.time;
    }

    /// <summary>
    /// 从候选姿态列表中选出主玩家。
    /// 当前策略：选择检测框面积最大的目标。
    /// </summary>
    private HumanPose? FindPrimaryUser(List<HumanPose> poses)
    {
        if (poses == null || poses.Count == 0)
            return null;

        HumanPose best = poses[0];
        float maxArea = -1f;

        for (int i = 0; i < poses.Count; i++)
        {
            float area = poses[i].box.width * poses[i].box.height;
            if (area > maxArea)
            {
                maxArea = area;
                best = poses[i];
            }
        }

        return best;
    }

    /// <summary>
    /// 确认并广播新手势。
    /// </summary>
    private void DispatchGestureEvent(GestureType newGesture)
    {
        currentType = newGesture;
        _lastSentType = newGesture;

        if (PoseManager.Instance != null)
            PoseManager.Instance.ReceiveLimbsData(currentType);

        if (currentType != GestureType.None)
            Debug.Log($"<color=orange>[Confirmed Gesture]</color> {currentType}");
    }
}