using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 摄像头输入源。
/// 
/// 主要职责：
/// 1. 初始化并管理 WebCamTexture
/// 2. 将摄像头画面显示到指定 RawImage
/// 3. 根据配置控制 RawImage 的显示镜像
/// 4. 提供摄像头切换与断线重连能力
/// 
/// 【重要说明】
/// 这里的镜像只影响 UI 显示（RawImage.uvRect），不会修改 WebCamTexture 原始像素数据。
/// 
/// 也就是说：
/// - 玩家看到的画面是否镜像，由这里控制
/// - AI 推理若直接读取 WebCamTexture，拿到的仍是原始未镜像纹理
/// - 因此姿态结果若要与玩家看到的左右一致，必须在 PosePostProcessor 中做坐标转换
/// </summary>
public class WebCamSource : MonoBehaviour
{
    [Header("Settings")]
    public Vector2Int resolution = new Vector2Int(1280, 720);
    public int fps = 60;
    public float reconnectInterval = 2.0f;

    [Header("UI References")]
    [Tooltip("用于显示摄像头画面的 RawImage。")]
    public RawImage targetRawImage;

    [Tooltip("用于自动保持画面宽高比。")]
    public AspectRatioFitter aspectRatioFitter;

    /// <summary>
    /// 当前摄像头纹理。
    /// </summary>
    public WebCamTexture Texture { get; private set; }

    /// <summary>
    /// 当前摄像头是否已可用。
    /// 条件：
    /// - Texture 不为空
    /// - 正在播放
    /// - 宽度已正确初始化（大于 16）
    /// </summary>
    public bool IsReady => Texture != null && Texture.isPlaying && Texture.width > 16;

    /// <summary>
    /// 当前是否镜像显示。
    /// 数据来自 PoseManager.PoseLocalConfig。
    /// </summary>
    public bool IsMirrored
    {
        get
        {
            var mgr = PoseManager.Instance;
            return mgr != null && mgr.PoseLocalConfig != null && mgr.PoseLocalConfig.isMirrored;
        }
    }

    private string _lastDeviceName;
    private bool _isReconnecting;
    private bool _isSwitching;
    private bool _lastMirrorState;

    private void Start()
    {
        _lastMirrorState = IsMirrored;
        InitializeCamera().Forget();
        CameraHealthCheckRoutine().Forget();
    }

    private void Update()
    {
        // 若镜像配置变化，立即刷新 RawImage 显示方式
        if (_lastMirrorState != IsMirrored)
        {
            _lastMirrorState = IsMirrored;
            ApplyUISettings();
        }
    }

    /// <summary>
    /// 手动设置镜像显示。
    /// 
    /// 说明：
    /// - 这里只更新配置并刷新 UI
    /// - 同时保存到本地配置文件
    /// </summary>
    public void SetMirror(bool mirror)
    {
        var mgr = PoseManager.Instance;
        if (mgr == null) return;

        mgr.PoseLocalConfig.isMirrored = mirror;
        mgr.SaveConfig();
        ApplyUISettings();
    }

    /// <summary>
    /// 切换到下一个可用摄像头。
    /// </summary>
    public async void SwitchCamera()
    {
        if (_isSwitching || WebCamTexture.devices.Length <= 1)
            return;

        _isSwitching = true;

        string nextDeviceName = GetNextDeviceName();
        if (!string.IsNullOrEmpty(nextDeviceName))
        {
            Debug.Log($"[WebCam] 正在切换到: {nextDeviceName}");
            await InitializeCamera(nextDeviceName);
        }

        _isSwitching = false;
    }

    /// <summary>
    /// 获取下一个摄像头设备名。
    /// </summary>
    private string GetNextDeviceName()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0) return null;

        if (string.IsNullOrEmpty(_lastDeviceName))
            return devices[0].name;

        int currentIndex = -1;
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].name == _lastDeviceName)
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + 1) % devices.Length;
        return devices[nextIndex].name;
    }

    /// <summary>
    /// 初始化摄像头。
    /// 
    /// 若未传 deviceName，则使用第一个可用设备。
    /// </summary>
    private async UniTask InitializeCamera(string deviceName = null)
    {
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            await Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }

        if (string.IsNullOrEmpty(deviceName))
        {
            if (WebCamTexture.devices.Length > 0)
                deviceName = WebCamTexture.devices[0].name;
            else
                return;
        }

        if (Texture != null)
            Texture.Stop();

        Texture = new WebCamTexture(deviceName, resolution.x, resolution.y, fps);
        Texture.Play();
        _lastDeviceName = deviceName;

        // 等待摄像头真正初始化成功
        await UniTask.WaitUntil(() => this == null || (Texture != null && Texture.width > 16));
        if (this == null || Texture == null) return;

        ApplyUISettings();
        Debug.Log($"[WebCam] 摄像头已启动: {deviceName} ({Texture.width}x{Texture.height})");
    }

    /// <summary>
    /// 将摄像头纹理应用到 UI，并根据镜像配置更新显示方式。
    /// 
    /// 注意：
    /// - 这里只修改显示层，不修改原始摄像头像素数据
    /// - 推理若直接读取 WebCamTexture，仍然拿到未镜像原图
    /// </summary>
    private void ApplyUISettings()
    {
        if (targetRawImage != null)
        {
            targetRawImage.texture = Texture;

            // 镜像显示：u 从 1 -> 0
            // 正常显示：u 从 0 -> 1
            targetRawImage.uvRect = IsMirrored
                ? new Rect(1f, 0f, -1f, 1f)
                : new Rect(0f, 0f, 1f, 1f);
        }

        if (aspectRatioFitter != null && Texture != null && Texture.height > 0)
        {
            aspectRatioFitter.aspectRatio = (float)Texture.width / Texture.height;
        }
    }

    /// <summary>
    /// 周期性检测摄像头健康状态。
    /// 若设备断开或纹理异常，则尝试重连。
    /// </summary>
    private async UniTaskVoid CameraHealthCheckRoutine()
    {
        while (this != null)
        {
            await UniTask.WaitForSeconds(reconnectInterval);

            if (!_isReconnecting && !_isSwitching)
            {
                if (Texture == null || Texture.width <= 16 || !IsDeviceAvailable(_lastDeviceName))
                {
                    await TryReconnectCamera();
                }
            }
        }
    }

    /// <summary>
    /// 判断指定设备名是否仍存在。
    /// </summary>
    private bool IsDeviceAvailable(string name)
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].name == name)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试重连摄像头。
    /// - 若原设备还在，优先重连原设备
    /// - 否则退回到默认可用设备
    /// </summary>
    private async UniTask TryReconnectCamera()
    {
        _isReconnecting = true;

        if (Texture != null)
            Texture.Stop();

        // 等待系统中至少存在一个设备
        while (this != null && WebCamTexture.devices.Length == 0)
        {
            await UniTask.WaitForSeconds(reconnectInterval);
        }

        if (this == null)
        {
            _isReconnecting = false;
            return;
        }

        string targetDevice = IsDeviceAvailable(_lastDeviceName) ? _lastDeviceName : null;
        await InitializeCamera(targetDevice);

        _isReconnecting = false;
    }

    private void OnDestroy()
    {
        if (Texture != null)
            Texture.Stop();
    }
}