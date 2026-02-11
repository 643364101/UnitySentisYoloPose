using System;
using System.Collections.Generic;
using UnityEngine;

public class HandWaveDetector : MonoBehaviour
{
    public enum GestureType
    {
        None,
        LeftMiddle,
        RightMiddle,
        LeftUp,
        RightUp,
    }

    [Header("状态 (只读)")] public GestureType currentType;
    private GestureType _lastSentType = GestureType.None;

    [Header("防抖设置")] private GestureType _pendingGesture = GestureType.None;
    private float _holdTimer = 0f;
    [Tooltip("手势需维持的时间(秒)才确认切换")] public float gestureHoldTime = 0.12f;

    [Header("判定阈值 (根据Log调优后)")] [Range(0, 1)]
    public float minScore = 0.3f;

    [Tooltip("手臂直线度：放宽到70以适应2D透视误差")] public float armStraightThreshold = 70f;

    [Tooltip("平举判定：与90度的容差 (建议40)")] public float middleAngleTolerance = 40f;

    [Tooltip("平举判定：最小X轴跨度 (防止手缩在胸前，建议0.08)")]
    public float minArmSpan = 0.08f;

    [Tooltip("举高判定：角度大于此值算举高 (建议110)")] public float upAngleThreshold = 110f;

    private void Start()
    {
        PoseManager.Instance.OnFilteringPoseUpdated += OnFilteringPoseUpdated;
    }

    private void OnDisable()
    {
        PoseManager.Instance.OnFilteringPoseUpdated -= OnFilteringPoseUpdated;
    }

    private void OnFilteringPoseUpdated(HumanPoseArea humanPoseArea)
    {
        if (humanPoseArea.id == 0)
        {
            HumanPose? targetPose = FindPrimaryUser(humanPoseArea.humanPoses);
            GestureType raw = CalculateGesture(targetPose);
            ProcessStateStability(raw);
        }
    }

    private GestureType CalculateGesture(HumanPose? nullablePose)
    {
        if (nullablePose == null) return GestureType.None;
        HumanPose pose = nullablePose.Value;

        var nose = pose.GetBodyParts(BodyPartsType.Nose);
        Vector2 headPos = nose.hasValue ? nose.ViewportPos : Vector2.zero;

        // 判定顺序：右手 -> 左手
        GestureType rightResult = CheckSingleArm(pose, false, headPos);
        if (rightResult != GestureType.None) return rightResult;

        GestureType leftResult = CheckSingleArm(pose, true, headPos);
        if (leftResult != GestureType.None) return leftResult;

        return GestureType.None;
    }

    private GestureType CheckSingleArm(HumanPose pose, bool isLeft, Vector2 headPos)
    {
        var s = pose.GetBodyParts(isLeft ? BodyPartsType.LeftShoulder : BodyPartsType.RightShoulder);
        var e = pose.GetBodyParts(isLeft ? BodyPartsType.LeftElbow : BodyPartsType.RightElbow);
        var w = pose.GetBodyParts(isLeft ? BodyPartsType.LeftWrist : BodyPartsType.RightWrist);
        var h = pose.GetBodyParts(isLeft ? BodyPartsType.LeftHip : BodyPartsType.RightHip);
        var n = pose.GetBodyParts(BodyPartsType.Nose);

        if (!s.hasValue || !e.hasValue || !w.hasValue ||
            s.score < minScore || e.score < minScore || w.score < minScore)
            return GestureType.None;

        // 1. 计算直线度
        float armStraightAngle = GetAngle_2D(s.ViewportPos, e.ViewportPos, e.ViewportPos, w.ViewportPos);

        // 2. 计算躯干角
        Vector2 bodyDownVec = (h.hasValue && h.score > minScore) ? (h.ViewportPos - s.ViewportPos) : Vector2.down;
        Vector2 armVec = w.ViewportPos - s.ViewportPos;
        float armBodyAngle = Vector2.Angle(bodyDownVec, armVec);

        // 3. 计算X轴跨度
        float xSpan = Mathf.Abs(w.ViewportPos.x - s.ViewportPos.x);

        // --- 判定开始 ---

        // 直度检查：根据你的Log，66都算直，所以阈值设为70是安全的
        if (armStraightAngle <= armStraightThreshold)
        {
            // A. Up
            bool isAngleUp = armBodyAngle >= upAngleThreshold;
            bool isHeightUp = n.hasValue && (w.ViewportPos.y > n.ViewportPos.y);

            // 防挠头
            if (isHeightUp && n.hasValue)
            {
                if (Mathf.Abs(w.ViewportPos.x - n.ViewportPos.x) < 0.05f)
                    isHeightUp = false;
            }

            if (isAngleUp || isHeightUp)
            {
                return isLeft ? GestureType.LeftUp : GestureType.RightUp;
            }

            // B. Middle
            if (Mathf.Abs(90 - armBodyAngle) <= middleAngleTolerance)
            {
                // 跨度检查：根据你的Log，0.12有点悬，改为变量控制，默认0.08
                if (xSpan > minArmSpan)
                {
                    return isLeft ? GestureType.LeftMiddle : GestureType.RightMiddle;
                }
            }
        }

        return GestureType.None;
    }

    private float GetAngle_2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        Vector2 v1 = (p2 - p1).normalized;
        Vector2 v2 = (p4 - p3).normalized;
        return Vector2.Angle(v1, v2);
    }

    private void ProcessStateStability(GestureType raw)
    {
        if (raw != _lastSentType)
        {
            if (raw == _pendingGesture)
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= gestureHoldTime)
                {
                    DispatchGestureEvent(raw);
                    _holdTimer = 0f;
                }
            }
            else
            {
                _pendingGesture = raw;
                _holdTimer = 0f;
            }
        }
        else
        {
            _pendingGesture = raw;
            _holdTimer = 0f;
        }
    }

    private HumanPose? FindPrimaryUser(List<HumanPose> poses)
    {
        if (poses == null || poses.Count == 0) return null;
        HumanPose best = poses[0];
        float maxArea = -1;
        foreach (var p in poses)
        {
            float area = p.box.width * p.box.height;
            if (area > maxArea)
            {
                maxArea = area;
                best = p;
            }
        }

        return best;
    }

    private void DispatchGestureEvent(GestureType newGesture)
    {
        currentType = newGesture;
        _lastSentType = newGesture;
        PoseManager.Instance.ReceiveLimbsData(currentType);

        if (currentType != GestureType.None)
            Debug.Log($"<color=orange>[Confirmed]</color> {currentType}");
    }
}