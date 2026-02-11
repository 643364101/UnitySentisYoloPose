using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class PoseLocalConfig
{
    public bool isActiveBone = true;
    public bool isActiveArea = true;

    public float minAreaSize = 0.04f;
    public int maxPlayerCount = 5;
    public float lostTrackingGracePeriod = 0.5f;

    public bool isMirrored = true;

    public List<AreaConfig> areaConfig = new List<AreaConfig>();
}

public class PoseManager : Singleton<PoseManager>
{
    [Header("References")] public RawImage cameraView;

    [Header("Config Data")] [SerializeField]
    private PoseLocalConfig _internalConfig; // 私有化，防止直接访问

    private bool _isConfigLoaded = false; // 标记是否已加载

    // ✅ 公开属性：访问时自动确保已加载
    public PoseLocalConfig poseLocalConfig
    {
        get
        {
            if (!_isConfigLoaded)
            {
                // 如果还没加载过，立刻加载
                // 这种情况通常发生在 PoseManager 还没 Awake，但别人已经调用 Instance.postLocalConfig 了
                EnsureConfigLoaded();
            }

            return _internalConfig;
        }
        set => _internalConfig = value;
    }

    private string ConfigPath => Path.Combine(Application.streamingAssetsPath, "PoseConfig.json");

    // --- 数据缓存 ---
    public List<HumanPose> CurrentPoses { get; private set; }
    public HumanPoseArea CurrentFilteringPoses { get; private set; }
    public HandWaveDetector.GestureType CurrentOnLimbs { get; private set; }
    public bool HasPerson => CurrentPoses != null && CurrentPoses.Count > 0;

    // --- 事件 ---
    public event Action<List<HumanPose>> OnPoseUpdated;
    public event Action<HumanPoseArea> OnFilteringPoseUpdated;
    public event Action<HandWaveDetector.GestureType> OnLimbsUpdated;

    private void Awake()
    {
        CurrentPoses = new List<HumanPose>();
        CurrentFilteringPoses = new HumanPoseArea();
        CurrentOnLimbs = HandWaveDetector.GestureType.None;

        if (cameraView == null) Debug.LogError("PoseManager: cameraView 未赋值！");

        // 主动初始化（虽然属性访问器会懒加载，但在 Awake 里主动调一次比较保险）
        EnsureConfigLoaded();
    }

    private void EnsureConfigLoaded()
    {
        if (_isConfigLoaded) return;

        // 防止 Inspector 为空
        if (_internalConfig == null) _internalConfig = new PoseLocalConfig();

        LoadConfig();
        _isConfigLoaded = true;
    }

    // ========================================================================
    // 💾 配置系统
    // ========================================================================

    [ContextMenu("Load Config")]
    public void LoadConfig()
    {
        // 确保对象存在
        if (_internalConfig == null) _internalConfig = new PoseLocalConfig();

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

    [ContextMenu("Save Config")]
    public void SaveConfig()
    {
        if (_internalConfig == null) return;

        try
        {
            string json = JsonUtility.ToJson(_internalConfig, true);
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(ConfigPath, json);
            Debug.Log($"[PoseManager] 配置已保存: {ConfigPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PoseManager] 保存失败: {e.Message}");
        }
    }

    // ========================================================================
    // 🔄 数据分发
    // ========================================================================

    public void ReceivePoseData(List<HumanPose> results)
    {
        CurrentPoses = results ?? new List<HumanPose>();
        OnPoseUpdated?.Invoke(CurrentPoses);
    }

    public void ReceiveFilteringPoseData(HumanPoseArea results)
    {
        CurrentFilteringPoses = results ?? new HumanPoseArea();
        OnFilteringPoseUpdated?.Invoke(CurrentFilteringPoses);
    }

    public void ReceiveLimbsData(HandWaveDetector.GestureType results)
    {
        CurrentOnLimbs = results;
        OnLimbsUpdated?.Invoke(results);
    }

    private void OnDisable()
    {
        SaveConfig();
    }
}