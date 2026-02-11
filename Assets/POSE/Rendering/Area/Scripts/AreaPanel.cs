using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AreaPanel : Singleton<AreaPanel>
{
    [Header("Settings")] public Area areasPrefab;

    public Color normalColor = Color.black;
    public Color activeColor = Color.cyan;

    private PoseLocalConfig poseLocalConfig;
    [SerializeField] private List<Area> areaList;

    // ========================================================================
    // 1. 生命周期与事件监听
    // ========================================================================

    private void OnEnable()
    {
        // 初始化时加载配置并生成区域
        Refresh();


        PoseManager.Instance.OnPoseUpdated += OnPoseUpdated;
    }

    private void OnDisable()
    {
        PoseManager.Instance.OnPoseUpdated -= OnPoseUpdated;
    }

    // ========================================================================
    // 2. 核心回调逻辑
    // ========================================================================

    /// <summary>
    /// 当 AI 识别到新数据时自动调用此方法
    /// </summary>
    private void OnPoseUpdated(List<HumanPose> poses)
    {
        // 如果配置未加载或区域检测功能未开启，直接跳过
        if (poseLocalConfig == null) return;

        // 1. 计算每个区域里有谁
        var areaResults = Packet(poses);

        // 2. 处理具体的游戏业务逻辑 (原 Runner 里的逻辑移到这里)
        HandleGameLogic(areaResults);
    }

    /// <summary>
    /// 在此处理具体的触发逻辑
    /// </summary>
    private void HandleGameLogic(List<HumanPoseArea> areaResults)
    {
        foreach (var humanPoseArea in areaResults)
        {
            PoseManager.Instance.ReceiveFilteringPoseData(humanPoseArea);
        }
    }
    // ========================================================================
    // 3. 区域检测算法
    // ========================================================================

    private List<HumanPoseArea> Packet(List<HumanPose> poses)
    {
        List<HumanPoseArea> listHumanPoseArea = new List<HumanPoseArea>();

        // 获取参考系 (Webcam RawImage)
        if (PoseManager.Instance.cameraView == null)
            return listHumanPoseArea;

        RectTransform cameraRect = PoseManager.Instance.cameraView.rectTransform;

        // 遍历所有生成的 UI 区域
        for (int j = 0; j < areaList.Count; j++)
        {
            Area currentArea = areaList[j];
            RectTransform areaRect = (RectTransform)currentArea.transform;
            List<HumanPose> validPoses = new List<HumanPose>();

            // 遍历所有检测到的人
            for (int i = 0; i < poses.Count; i++)
            {
                var leftAnkle = poses[i].GetBodyParts(BodyPartsType.LeftAnkle);
                var rightAnkle = poses[i].GetBodyParts(BodyPartsType.RightAnkle);

                // ✅ 使用 PoseUIUtils 进行高性能、自动适配的判断
                // 只要有一只脚在区域内就算进入
                bool isLeftIn = PoseUIUtils.IsInsideLocal(leftAnkle, cameraRect, areaRect);
                bool isRightIn = PoseUIUtils.IsInsideLocal(rightAnkle, cameraRect, areaRect);

                if (isLeftIn || isRightIn)
                {
                    validPoses.Add(poses[i]);
                }
            }

            // 更新 UI 颜色状态 (有人变亮，没人变暗)
            currentArea.SetColor(validPoses.Count > 0 ? activeColor : normalColor);


            // 打包结果
            HumanPoseArea humanPoseArea = new HumanPoseArea
            {
                id = currentArea.areaConfig.id,
                humanPoses = validPoses
            };
            listHumanPoseArea.Add(humanPoseArea);
        }

        return listHumanPoseArea;
    }

    // ========================================================================
    // 4. UI 生成与刷新
    // ========================================================================

    public void Refresh()
    {
        // 1. 清理旧物体
        foreach (var panel in areaList)
        {
            if (panel != null) Destroy(panel.gameObject);
        }

        areaList.Clear();

        // 2. 获取配置
        poseLocalConfig = PoseManager.Instance.poseLocalConfig;


        if (poseLocalConfig == null || poseLocalConfig.areaConfig == null) return;

        // 3. 生成新区域
        foreach (AreaConfig areaConfig in poseLocalConfig.areaConfig)
        {
            var area = Instantiate(areasPrefab, PoseManager.Instance.cameraView.transform);
            area.Init(areaConfig); // 确保 Area 脚本有 Init 方法设置位置和大小
            areaList.Add(area);
        }

        // 4. 设置显示状态
        SetActive(poseLocalConfig.isActiveArea);
    }

    public void SetActive(bool active)
    {
        foreach (var panel in areaList)
        {
            if (panel.TryGetComponent<CanvasGroup>(out var group))
            {
                group.alpha = active ? 1 : 0;
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
    public int id;
    public float[] pos; // [x, y]
    public float[] sizeDelta; // [w, h]
}

public class HumanPoseArea
{
    public int id;
    public List<HumanPose> humanPoses;
}