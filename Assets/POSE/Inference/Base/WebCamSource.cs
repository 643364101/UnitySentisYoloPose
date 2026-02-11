using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class WebCamSource : MonoBehaviour
{
    [Header("Settings")] 
    public Vector2Int resolution = new Vector2Int(1280, 720);
    public int fps = 60;
    public float reconnectInterval = 2.0f;

    [Header("UI References")] 
    public RawImage targetRawImage;
    public AspectRatioFitter aspectRatioFitter;

    public WebCamTexture Texture { get; private set; }
    public bool IsReady => Texture != null && Texture.isPlaying && Texture.width > 16;
    
    public bool IsMirrored => PoseManager.Instance.poseLocalConfig.isMirrored;

    private string _lastDeviceName;
    private bool _isReconnecting;
    private bool _isSwitching;
    private bool _lastMirrorState; // 用于监测镜像状态是否发生改变

    void Start()
    {
        // 初始记录镜像状态
        _lastMirrorState = IsMirrored;
        InitializeCamera().Forget();
        CameraHealthCheckRoutine().Forget();
    }

    void Update()
    {
        // [新增] 监测镜像状态变化，如果用户在 UI 或 JSON 里改了，画面立即翻转
        if (_lastMirrorState != IsMirrored)
        {
            _lastMirrorState = IsMirrored;
            ApplyUISettings();
        }
    }

    // ========================================================================
    // 公开 API：手动设置镜像并保存配置
    // ========================================================================
    public void SetMirror(bool mirror)
    {
        PoseManager.Instance.poseLocalConfig.isMirrored = mirror;
        // PoseManager.Instance.SaveConfig(); // 可选：立即保存到 JSON
        ApplyUISettings();
    }

    public async void SwitchCamera()
    {
        if (_isSwitching || WebCamTexture.devices.Length <= 1) return;
        _isSwitching = true;

        string nextDeviceName = GetNextDeviceName();
        if (!string.IsNullOrEmpty(nextDeviceName))
        {
            Debug.Log($"[WebCam] 正在切换到: {nextDeviceName}");
            await InitializeCamera(nextDeviceName);
        }
        
        _isSwitching = false;
    }

    private string GetNextDeviceName()
    {
        var devices = WebCamTexture.devices;
        if (devices.Length == 0) return null;
        if (string.IsNullOrEmpty(_lastDeviceName)) return devices[0].name;

        int currentIndex = -1;
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].name == _lastDeviceName) { currentIndex = i; break; }
        }

        int nextIndex = (currentIndex + 1) % devices.Length;
        return devices[nextIndex].name;
    }

    private async UniTask InitializeCamera(string deviceName = null)
    {
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            await Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }

        if (string.IsNullOrEmpty(deviceName))
        {
            if (WebCamTexture.devices.Length > 0) deviceName = WebCamTexture.devices[0].name;
            else return;
        }

        if (Texture != null) Texture.Stop();

        Texture = new WebCamTexture(deviceName, resolution.x, resolution.y, fps);
        Texture.Play();
        _lastDeviceName = deviceName;

        await UniTask.WaitUntil(() => Texture.width > 16);
        
        ApplyUISettings();
        Debug.Log($"摄像头已启动: {deviceName} ({Texture.width}x{Texture.height})");
    }

    private void ApplyUISettings()
    {
        if (targetRawImage != null)
        {
            targetRawImage.texture = Texture;
            // [关键] 根据配置中的 IsMirrored 调整 UV 矩形
            // 镜像：Rect(1, 0, -1, 1), 正常：Rect(0, 0, 1, 1)
            targetRawImage.uvRect = IsMirrored ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
        }

        if (aspectRatioFitter != null && Texture != null && Texture.height > 0)
            aspectRatioFitter.aspectRatio = (float)Texture.width / Texture.height;
    }

    private async UniTaskVoid CameraHealthCheckRoutine()
    {
        while (this != null)
        {
            await UniTask.WaitForSeconds(reconnectInterval);
            if (!_isReconnecting && !_isSwitching)
            {
                if (Texture == null || Texture.width <= 16 || !IsDeviceAvailable(_lastDeviceName))
                    await TryReconnectCamera();
            }
        }
    }

    private bool IsDeviceAvailable(string name)
    {
        foreach (var d in WebCamTexture.devices)
            if (d.name == name) return true;
        return false;
    }

    private async UniTask TryReconnectCamera()
    {
        _isReconnecting = true;
        if (Texture != null) Texture.Stop();
        while (!IsDeviceAvailable(_lastDeviceName) && WebCamTexture.devices.Length == 0)
            await UniTask.WaitForSeconds(reconnectInterval);

        string targetDevice = IsDeviceAvailable(_lastDeviceName) ? _lastDeviceName : null;
        await InitializeCamera(targetDevice);
        _isReconnecting = false;
    }

    void OnDestroy() => Texture?.Stop();
}