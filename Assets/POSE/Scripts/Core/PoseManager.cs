using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 本地姿态系统配置。
/// 
/// 该配置会被保存到：
/// StreamingAssets/PoseConfig.json
/// 
/// 主要用于控制：
/// - 骨骼显示
/// - 区域检测
/// - 镜像
/// - 人数过滤
/// - 掉帧补偿
/// </summary>
[Serializable]
public class PoseLocalConfig
{
    /// <summary>
    /// 是否启用骨骼可视化。
    /// </summary>
    public bool isActiveBone = true;

    /// <summary>
    /// 是否启用区域检测。
    /// </summary>
    public bool isActiveArea = true;

    /// <summary>
    /// 最小人体面积阈值（归一化面积）。
    /// 小于该值的人体会被过滤掉。
    /// </summary>
    public float minAreaSize = 0.04f;

    /// <summary>
    /// 允许保留的最大玩家数量。
    /// </summary>
    public int maxPlayerCount = 5;

    /// <summary>
    /// 姿态丢失后的宽限时间。
    /// 在该时间内，历史姿态可被短暂补偿回来，减少闪烁。
    /// </summary>
    public float lostTrackingGracePeriod = 0.5f;

    /// <summary>
    /// 是否镜像显示。
    /// 
    /// 注意：
    /// - 该配置不仅影响 RawImage 的显示镜像
    /// - 还会影响 PosePostProcessor 是否将姿态结果转换为镜像后的 Display Space
    /// </summary>
    public bool isMirrored = true;

    /// <summary>
    /// 区域配置列表。
    /// </summary>
    public List<AreaConfig> areaConfig = new List<AreaConfig>();
}

/// <summary>
/// 姿态系统总管理器。
/// 
/// 主要职责：
/// 1. 管理本地配置加载 / 保存
/// 2. 缓存当前姿态结果
/// 3. 对外分发姿态更新事件
/// 4. 提供 cameraView 作为 UI / 命中检测 / 截图映射基准
/// 
/// 【关键坐标约定】
/// - 进入 PoseManager 的最终姿态数据，统一应为 Display Space
/// - 即：已经过 PosePostProcessor 处理，坐标方向与玩家看到的画面一致
/// 
/// 【数据流】
/// Decoder（Source Space）
/// -> PosePostProcessor（转换为 Display Space）
/// -> PoseManager（统一对外提供）
/// </summary>
public class PoseManager : Singleton<PoseManager>
{
    [Header("References")]
    [Tooltip("显示摄像头画面的 RawImage。也是姿态 UI / 命中检测 / 截图映射的基准区域。")]
    public RawImage cameraView;

    [Header("Config Data")]
    [SerializeField]
    [Tooltip("内部配置缓存。请优先通过 PoseLocalConfig 属性访问。")]
    private PoseLocalConfig _internalConfig;

    /// <summary>
    /// 是否已完成配置加载。
    /// 用于支持懒加载访问。
    /// </summary>
    private bool _isConfigLoaded = false;

    /// <summary>
    /// 对外公开的姿态本地配置。
    /// 
    /// 特点：
    /// - 首次访问时自动确保已加载
    /// - 建议业务层始终通过该属性访问，而不是直接访问 _internalConfig
    /// </summary>
    public PoseLocalConfig PoseLocalConfig
    {
        get
        {
            if (!_isConfigLoaded)
                EnsureConfigLoaded();

            return _internalConfig;
        }
        set => _internalConfig = value;
    }

    /// <summary>
    /// 本地配置文件路径。
    /// </summary>
    private string ConfigPath => Path.Combine(Application.streamingAssetsPath, "PoseConfig.json");

    // ------------------------------------------------------------------------
    // 当前运行时数据缓存
    // ------------------------------------------------------------------------

    /// <summary>
    /// 当前帧最终姿态结果（Display Space）。
    /// </summary>
    public List<HumanPose> CurrentPoses { get; private set; }

    /// <summary>
    /// 当前区域过滤结果。
    /// 
    /// 注意：
    /// 当前仍是单个 HumanPoseArea 缓存。
    /// 若未来要同时管理多个区域，建议扩展为 List<HumanPoseArea>。
    /// </summary>
    public HumanPoseArea CurrentFilteringPoses { get; private set; }

    /// <summary>
    /// 当前肢体手势状态。
    /// </summary>
    public GestureType CurrentOnLimbs { get; private set; }

    /// <summary>
    /// 当前是否检测到至少一个人。
    /// </summary>
    public bool HasPerson => CurrentPoses != null && CurrentPoses.Count > 0;

    // ------------------------------------------------------------------------
    // 事件
    // ------------------------------------------------------------------------

    /// <summary>
    /// 姿态更新事件。
    /// 输出数据为最终业务姿态（Display Space）。
    /// </summary>
    public event Action<List<HumanPose>> OnPoseUpdated;

    /// <summary>
    /// 区域过滤结果更新事件。
    /// </summary>
    public event Action<HumanPoseArea> OnFilteringPoseUpdated;

    /// <summary>
    /// 肢体手势更新事件。
    /// </summary>
    public event Action<GestureType> OnLimbsUpdated;

    private void Awake()
    {
        CurrentPoses = new List<HumanPose>();
        CurrentFilteringPoses = new HumanPoseArea();
        CurrentOnLimbs = GestureType.None;

        if (cameraView == null)
            Debug.LogError("PoseManager: cameraView 未赋值！");

        // 主动初始化配置，避免外部首次访问时机不明确
        EnsureConfigLoaded();
    }

    /// <summary>
    /// 确保配置已加载。
    /// </summary>
    private void EnsureConfigLoaded()
    {
        if (_isConfigLoaded) return;

        if (_internalConfig == null)
            _internalConfig = new PoseLocalConfig();

        LoadConfig();
        _isConfigLoaded = true;
    }

    // ========================================================================
    // 配置系统
    // ========================================================================

    /// <summary>
    /// 从本地 JSON 加载配置。
    /// 若文件不存在，则自动生成默认配置文件。
    /// </summary>
    [ContextMenu("Load Config")]
    public void LoadConfig()
    {
        if (_internalConfig == null)
            _internalConfig = new PoseLocalConfig();

        if (File.Exists(ConfigPath))
        {
            try
            {
                string json = File.ReadAllText(ConfigPath);
                JsonUtility.FromJsonOverwrite(json, _internalConfig);
                Debug.Log($"[PoseManager] 已加载配置: {ConfigPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[PoseManager] 加载失败: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("[PoseManager] 配置不存在，生成默认文件...");
            SaveConfig();
        }
    }

    /// <summary>
    /// 将当前配置保存到本地 JSON。
    /// </summary>
    [ContextMenu("Save Config")]
    public void SaveConfig()
    {
        if (_internalConfig == null)
            return;

        try
        {
            string json = JsonUtility.ToJson(_internalConfig, true);
            string dir = Path.GetDirectoryName(ConfigPath);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ConfigPath, json);
            Debug.Log($"[PoseManager] 配置已保存: {ConfigPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PoseManager] 保存失败: {e.Message}");
        }
    }

    // ========================================================================
    // 数据分发
    // ========================================================================

    /// <summary>
    /// 接收最终姿态结果并对外分发。
    /// 
    /// 输入要求：
    /// - results 应为最终业务姿态（Display Space）
    /// - 一般来自 PosePostProcessor.Process(...)
    /// </summary>
    public void ReceivePoseData(List<HumanPose> results)
    {
        CurrentPoses = results ?? new List<HumanPose>();
        OnPoseUpdated?.Invoke(CurrentPoses);
    }

    /// <summary>
    /// 接收区域过滤结果并对外分发。
    /// </summary>
    public void ReceiveFilteringPoseData(HumanPoseArea results)
    {
        CurrentFilteringPoses = results ?? new HumanPoseArea();
        OnFilteringPoseUpdated?.Invoke(CurrentFilteringPoses);
    }

    /// <summary>
    /// 接收肢体手势结果并对外分发。
    /// </summary>
    public void ReceiveLimbsData(GestureType results)
    {
        CurrentOnLimbs = results;
        OnLimbsUpdated?.Invoke(results);
    }

    private void OnDisable()
    {
        SaveConfig();
    }

    private void OnApplicationQuit()
    {
        SaveConfig();
    }
}