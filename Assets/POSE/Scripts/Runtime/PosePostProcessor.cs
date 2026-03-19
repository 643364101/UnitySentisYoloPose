using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 姿态后处理器。
/// 
/// 主要职责：
/// 1. 过滤过小目标
/// 2. 限制最大玩家数量
/// 3. 对短时掉帧目标做补偿
/// 4. 将模型原始结果（Source Space）统一转换为业务层使用的 Display Space
/// 
/// 【坐标空间约定】
/// 输入 rawResults：
/// - 来自模型解码器
/// - 属于 Source Space（原始纹理空间）
/// - x：左 -> 右
/// - y：上 -> 下
/// - 原点：左上
/// 
/// 输出结果：
/// - 统一为 Display Space（显示空间）
/// - x：玩家看到画面的左 -> 右
/// - y：仍保留上 -> 下的内部存储方式
/// - box：与玩家看到的画面一致
/// 
/// 【镜像原则】
/// - 只翻转坐标，不交换 Left / Right 关键点索引语义
/// - LeftWrist 永远表示人体自身左手，不表示“屏幕左边那只手”
/// 
/// 【设计原则】
/// - 不直接修改输入 rawResults，避免副作用
/// - 所有对外输出结果都应视为最终业务姿态（Display Space）
/// </summary>
public class PosePostProcessor
{
    private float _gracePeriod;
    private bool _isMirrored;
    private float _minAreaSize;
    private int _maxPlayerCount;

    /// <summary>
    /// 历史追踪缓存。
    /// key = pose.index
    /// value = 最近一次有效姿态及其时间
    /// </summary>
    private readonly Dictionary<int, TrackedPose> _trackedPoses = new Dictionary<int, TrackedPose>();

    private class TrackedPose
    {
        public HumanPose Pose;
        public float LastValidTime;
    }

    public PosePostProcessor(float gracePeriod, bool isMirrored, float minAreaSize = 0.05f, int maxPlayerCount = 2)
    {
        _isMirrored = isMirrored;
        UpdateSettings(gracePeriod, isMirrored, minAreaSize, maxPlayerCount);
    }

    /// <summary>
    /// 更新后处理配置。
    /// 
    /// 注意：
    /// - 若镜像状态发生变化，会清空历史追踪缓存
    /// - 这样可以避免“旧缓存是镜像坐标，新帧是非镜像坐标”导致补偿结果错乱
    /// </summary>
    public void UpdateSettings(float gracePeriod, bool isMirrored, float minAreaSize, int maxPlayerCount)
    {
        bool mirrorChanged = _isMirrored != isMirrored;

        _gracePeriod = Mathf.Max(0f, gracePeriod);
        _isMirrored = isMirrored;
        _minAreaSize = Mathf.Max(0f, minAreaSize);
        _maxPlayerCount = Mathf.Max(1, maxPlayerCount);

        if (mirrorChanged)
        {
            _trackedPoses.Clear();
        }
    }

    /// <summary>
    /// 手动清空追踪缓存。
    /// 建议在以下场景调用：
    /// - 切换摄像头
    /// - 切换模型
    /// - 重新初始化姿态系统
    /// </summary>
    public void ClearTracking()
    {
        _trackedPoses.Clear();
    }

    /// <summary>
    /// 主处理流程：
    /// 1. 过滤过小目标
    /// 2. 必要时镜像转换
    /// 3. 限制最大玩家数
    /// 4. 更新追踪缓存
    /// 5. 对短时丢失目标做补偿
    /// 6. 输出稳定排序结果
    /// 
    /// 输入：
    /// - rawResults：Decoder 输出的原始姿态（Source Space）
    /// 
    /// 输出：
    /// - 最终业务姿态（Display Space）
    /// </summary>
    public List<HumanPose> Process(List<HumanPose> rawResults)
    {
        List<HumanPose> currentFrameValidPoses = new List<HumanPose>();
        HashSet<int> currentIds = new HashSet<int>();

        if (rawResults != null && rawResults.Count > 0)
        {
            // A. 过滤 + 坐标转换，不直接修改输入
            List<HumanPose> filtered = new List<HumanPose>(rawResults.Count);

            for (int i = 0; i < rawResults.Count; i++)
            {
                HumanPose rawPose = rawResults[i];

                float area = rawPose.box.width * rawPose.box.height;
                if (area < _minAreaSize)
                    continue;

                HumanPose finalPose = _isMirrored ? MirrorPose(rawPose) : rawPose;
                filtered.Add(finalPose);
            }

            // B. 按面积从大到小排序，优先保留主体玩家
            if (filtered.Count > 1)
            {
                filtered.Sort((a, b) =>
                    (b.box.width * b.box.height).CompareTo(a.box.width * a.box.height));
            }

            // C. 限制最大玩家数
            int count = Mathf.Min(filtered.Count, _maxPlayerCount);

            for (int i = 0; i < count; i++)
            {
                HumanPose pose = filtered[i];
                currentFrameValidPoses.Add(pose);
                currentIds.Add(pose.index);

                _trackedPoses[pose.index] = new TrackedPose
                {
                    Pose = pose,
                    LastValidTime = Time.time
                };
            }
        }

        // D. 对短时掉帧目标做补偿
        List<int> lostKeys = new List<int>();

        foreach (var kvp in _trackedPoses)
        {
            int id = kvp.Key;
            TrackedPose tracked = kvp.Value;

            if (!currentIds.Contains(id))
            {
                if (Time.time - tracked.LastValidTime < _gracePeriod)
                {
                    currentFrameValidPoses.Add(tracked.Pose);
                }
                else
                {
                    lostKeys.Add(id);
                }
            }
        }

        // E. 清理彻底丢失的人
        for (int i = 0; i < lostKeys.Count; i++)
        {
            _trackedPoses.Remove(lostKeys[i]);
        }

        // F. 稳定排序
        if (currentFrameValidPoses.Count > 1)
        {
            currentFrameValidPoses.Sort((a, b) => a.index.CompareTo(b.index));
        }

        return currentFrameValidPoses;
    }

    /// <summary>
    /// 将单个人体姿态从 Source Space 左右方向转换为 Display Space 左右方向。
    /// 
    /// 说明：
    /// - 这里只翻转坐标，不交换关键点语义
    /// - 例如 LeftWrist 仍然表示人体自身左手，只是其屏幕位置变成玩家看到的那一侧
    /// </summary>
    private HumanPose MirrorPose(HumanPose pose)
    {
        // 1. 翻转检测框
        Rect r = pose.box;
        pose.box = new Rect(1f - (r.x + r.width), r.y, r.width, r.height);

        // 2. 翻转关键点 X 坐标
        if (pose.bodyParts != null)
        {
            for (int i = 0; i < pose.bodyParts.Length; i++)
            {
                if (!pose.bodyParts[i].hasValue)
                    continue;

                BodyPart part = pose.bodyParts[i];
                part.x = 1f - part.x;
                pose.bodyParts[i] = part;
            }
        }

        return pose;
    }
}