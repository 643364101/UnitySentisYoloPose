using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 姿态数据后处理器 (最终修复版)
/// 职责：面积过滤(去杂讯)、智能筛选(限制人数)、掉帧补偿(防闪烁)、镜像翻转、多人排序
/// </summary>
public class PosePostProcessor
{
    private float _gracePeriod;
    private bool _isMirrored;
    
    // 最小有效面积 (0.0~1.0)，小于此比例的人被忽略
    private float _minAreaSize;

    // 最大玩家数量，超过则保留面积最大的前N个
    private int _maxPlayerCount;

    // 缓存“处理后的最终结果”
    private List<HumanPose> _lastFinalResults = new List<HumanPose>();
    private float _lastValidTime = -1f;

    public PosePostProcessor(float gracePeriod, bool isMirrored, float minAreaSize = 0.05f, int maxPlayerCount = 2)
    {
        _gracePeriod = gracePeriod;
        _isMirrored = isMirrored;
        _minAreaSize = minAreaSize;
        _maxPlayerCount = maxPlayerCount;
    }

    public void UpdateSettings(float gracePeriod, bool isMirrored, float minAreaSize, int maxPlayerCount)
    {
        _gracePeriod = gracePeriod;
        _isMirrored = isMirrored;
        _minAreaSize = minAreaSize;
        _maxPlayerCount = maxPlayerCount;
    }

    public List<HumanPose> Process(List<HumanPose> rawResults)
    {
        // 1. 如果有新数据输入
        if (rawResults != null && rawResults.Count > 0)
        {
            // --- A. 距离/面积过滤 ---
            // 移除太小的人 (通常意味着离得很远)
            rawResults.RemoveAll(pose => (pose.box.width * pose.box.height) < _minAreaSize);

            // [关键判断] 过滤后还有人吗？
            if (rawResults.Count > 0)
            {
                // --- B. 智能筛选 ---
                // 如果人太多，保留面积最大的前 N 个
                if (rawResults.Count > _maxPlayerCount)
                {
                    // 按面积从大到小排序 (大面积=离得近)
                    rawResults.Sort((a, b) => 
                    {
                        float areaA = a.box.width * a.box.height;
                        float areaB = b.box.width * b.box.height;
                        return areaB.CompareTo(areaA);
                    });

                    // 截取前 N 个
                    // 注意：GetRange 返回的是新 List，必须赋值回 rawResults
                    rawResults = rawResults.GetRange(0, _maxPlayerCount);
                }

                // --- C. 镜像处理 ---
                // 仅对有效的新数据进行镜像
                if (_isMirrored)
                {
                    ApplyMirroring(rawResults);
                }

                // --- D. 多人 ID 排序 ---
                // 筛选完之后，再按 ID 排序，保证控制权稳定
                if (rawResults.Count > 1)
                {
                    rawResults.Sort((a, b) => a.index.CompareTo(b.index));
                }

                // --- E. 更新缓存 ---
                // 创建副本存入缓存，作为新的“上一帧有效数据”
                _lastFinalResults = new List<HumanPose>(rawResults);
                _lastValidTime = Time.time;

                // 返回处理好的新数据
                return rawResults;
            }
        }
        
        // 2. 如果没有新数据（或者都被过滤掉了），尝试掉帧补偿
        // 检查缓存是否还在宽限期内
        if (_lastFinalResults.Count > 0 && (Time.time - _lastValidTime) < _gracePeriod)
        {
            // 返回缓存 (缓存里的数据已经是镜像和排序过的，直接用)
            return _lastFinalResults;
        }

        // 3. 彻底没数据，清除缓存
        if (_lastFinalResults.Count > 0) 
        {
            _lastFinalResults.Clear();
        }
        
        return new List<HumanPose>();
    }

    private void ApplyMirroring(List<HumanPose> results)
    {
        for (int i = 0; i < results.Count; i++)
        {
            HumanPose pose = results[i];

            // 翻转 Box (屏幕空间镜像)
            Rect r = pose.box;
            float newXMin = 1f - (r.x + r.width);
            pose.box = new Rect(newXMin, r.y, r.width, r.height);

            // 翻转 Keypoints
            for (int k = 0; k < pose.bodyParts.Length; k++)
            {
                if (pose.bodyParts[k].hasValue)
                {
                    pose.bodyParts[k].x = 1f - pose.bodyParts[k].x;
                }
            }

            results[i] = pose;
        }
    }
}