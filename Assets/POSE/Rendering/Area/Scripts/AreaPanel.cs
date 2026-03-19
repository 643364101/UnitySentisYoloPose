using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 区域面板管理器。
/// 
/// 主要职责：
/// 1. 根据配置生成区域 UI
/// 2. 监听姿态更新
/// 3. 判断玩家是否进入某个区域
/// 4. 将区域内人体结果打包为 HumanPoseArea
/// 
/// 当前区域判定规则：
/// - 使用 LeftAnkle / RightAnkle 判断
/// - 任意一只脚进入区域，即视为该玩家进入区域
/// 
/// 坐标原则：
/// - 输入 poses 为 Display Space 数据
/// - Area 挂在 cameraView 下，因此可直接使用同局部空间命中检测
/// 
/// 注意：
/// - 当前 PoseManager 仍只缓存单个 HumanPoseArea
/// - 因此多个区域结果是“逐个分发”的
/// - 若未来需要同时管理全部区域结果，建议将 PoseManager 扩展为 List<HumanPoseArea>
/// </summary>
public class AreaPanel : Singleton<AreaPanel>
{
    [Header("Settings")]
    public Area areasPrefab;

    public Color normalColor = Color.black;
    public Color activeColor = Color.cyan;

    private PoseLocalConfig poseLocalConfig;

    [SerializeField]
    private List<Area> areaList = new List<Area>();

    private void OnEnable()
    {
        Refresh();

        if (PoseManager.Instance != null)
            PoseManager.Instance.OnPoseUpdated += OnPoseUpdated;
    }

    private void OnDisable()
    {
        if (PoseManager.Instance != null)
            PoseManager.Instance.OnPoseUpdated -= OnPoseUpdated;
    }

    private void OnPoseUpdated(List<HumanPose> poses)
    {
        if (poseLocalConfig == null || !poseLocalConfig.isActiveArea)
            return;

        List<HumanPoseArea> areaResults = Packet(poses);
        HandleGameLogic(areaResults);
    }

    /// <summary>
    /// 当前仍沿用“单区域逐条分发”逻辑。
    /// 若未来要一次处理全部区域，建议改成 List<HumanPoseArea> 统一分发。
    /// </summary>
    private void HandleGameLogic(List<HumanPoseArea> areaResults)
    {
        for (int i = 0; i < areaResults.Count; i++)
        {
            PoseManager.Instance.ReceiveFilteringPoseData(areaResults[i]);
        }
    }

    /// <summary>
    /// 计算每个区域内的玩家列表。
    /// </summary>
    private List<HumanPoseArea> Packet(List<HumanPose> poses)
    {
        List<HumanPoseArea> result = new List<HumanPoseArea>();

        if (PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return result;

        RectTransform cameraRect = PoseManager.Instance.cameraView.rectTransform;

        // 无人时也要刷新区域颜色并输出空结果
        if (poses == null || poses.Count == 0)
        {
            for (int j = 0; j < areaList.Count; j++)
            {
                Area currentArea = areaList[j];
                currentArea.SetColor(normalColor);

                result.Add(new HumanPoseArea
                {
                    id = currentArea.areaConfig.id,
                    humanPoses = new List<HumanPose>()
                });
            }

            return result;
        }

        for (int j = 0; j < areaList.Count; j++)
        {
            Area currentArea = areaList[j];
            RectTransform areaRect = currentArea.transform as RectTransform;
            List<HumanPose> validPoses = new List<HumanPose>();

            for (int i = 0; i < poses.Count; i++)
            {
                BodyPart leftAnkle = poses[i].GetBodyPart(BodyPartsType.LeftAnkle);
                BodyPart rightAnkle = poses[i].GetBodyPart(BodyPartsType.RightAnkle);

                // 因为 areaRect 挂在 cameraView 下，所以同局部空间判断最直接
                bool isLeftIn = PoseHitTestUtils.IsOverUILocal(leftAnkle, cameraRect, areaRect);
                bool isRightIn = PoseHitTestUtils.IsOverUILocal(rightAnkle, cameraRect, areaRect);

                if (isLeftIn || isRightIn)
                {
                    validPoses.Add(poses[i]);
                }
            }

            currentArea.SetColor(validPoses.Count > 0 ? activeColor : normalColor);

            result.Add(new HumanPoseArea
            {
                id = currentArea.areaConfig.id,
                humanPoses = validPoses
            });
        }

        return result;
    }

    /// <summary>
    /// 根据配置重建区域 UI。
    /// </summary>
    public void Refresh()
    {
        for (int i = 0; i < areaList.Count; i++)
        {
            if (areaList[i] != null)
                Destroy(areaList[i].gameObject);
        }

        areaList.Clear();

        if (PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return;

        poseLocalConfig = PoseManager.Instance.PoseLocalConfig;
        if (poseLocalConfig == null || poseLocalConfig.areaConfig == null)
            return;

        for (int i = 0; i < poseLocalConfig.areaConfig.Count; i++)
        {
            AreaConfig areaConfig = poseLocalConfig.areaConfig[i];
            Area area = Instantiate(areasPrefab, PoseManager.Instance.cameraView.transform);
            area.Init(areaConfig);
            areaList.Add(area);
        }

        SetActive(poseLocalConfig.isActiveArea);
    }

    /// <summary>
    /// 显示 / 隐藏所有区域面板。
    /// </summary>
    public void SetActive(bool active)
    {
        for (int i = 0; i < areaList.Count; i++)
        {
            Area panel = areaList[i];
            if (panel == null) continue;

            if (panel.TryGetComponent<CanvasGroup>(out CanvasGroup group))
            {
                group.alpha = active ? 1f : 0f;
                group.blocksRaycasts = active;
                group.interactable = active;
            }
            else
            {
                panel.gameObject.SetActive(active);
            }
        }
    }
}

[Serializable]
public class AreaConfig
{
    /// <summary>
    /// 区域 ID。
    /// </summary>
    public int id;

    /// <summary>
    /// 区域 anchoredPosition，长度应为 2：[x, y]
    /// </summary>
    public float[] pos;

    /// <summary>
    /// 区域 sizeDelta，长度应为 2：[width, height]
    /// </summary>
    public float[] sizeDelta;
}

/// <summary>
/// 单个区域中的人体结果。
/// </summary>
public class HumanPoseArea
{
    public int id;
    public List<HumanPose> humanPoses;
}