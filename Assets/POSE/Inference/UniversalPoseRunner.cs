using UnityEngine;
using Cysharp.Threading.Tasks;
using Unity.InferenceEngine;

public class UniversalPoseRunner : MonoBehaviour
{
    [Header("Configuration")] public PoseSchemeAsset schemeAsset;
    [Header("Input Source")] public WebCamSource webcamInput;
    [Header("Visualization")] public HumanPoseVisualizer visualizer;

    private Worker _worker;
    private Tensor<float> _inputTensor;
    private IInferenceStrategy _strategy;
    private IPoseDecoder _decoder;
    private TextureTransform _textureTransform;
    private RenderTexture _inferenceRT;
    private bool _isWorking;
    private PosePostProcessor _postProcessor;

    async void Start()
    {
        if (webcamInput == null) webcamInput = FindObjectOfType<WebCamSource>();

        // 1. 初始化 AI
        var model = ModelLoader.Load(schemeAsset.modelAsset);
        _worker = new Worker(model, schemeAsset.backend);

        var shape = new TensorShape(1, 3, schemeAsset.inputSize.y, schemeAsset.inputSize.x);
        _inputTensor = new Tensor<float>(shape);

        // 2. 防拉伸 RT
        _inferenceRT = new RenderTexture(
            schemeAsset.inputSize.x,
            schemeAsset.inputSize.y,
            0, RenderTextureFormat.ARGB32
        );
        _inferenceRT.Create();

        // 3. 纹理转换器
        _textureTransform = new TextureTransform()
            .SetDimensions(schemeAsset.inputSize.x, schemeAsset.inputSize.y)
            .SetTensorLayout(TensorLayout.NCHW)
            .SetChannelSwizzle(ChannelSwizzle.RGBA)
            .SetCoordOrigin(CoordOrigin.TopLeft);

        // 4. 解码器
        if (schemeAsset.inferenceType == InferenceType.Yolo8)
        {
            _decoder = new YoloV8PoseDecoder(
                schemeAsset.inputSize, 10, true,
                schemeAsset.minCutoff, schemeAsset.beta, schemeAsset.dCutoff
            );
        }
        else if (schemeAsset.inferenceType == InferenceType.Yolo26)
        {
            Debug.Log("yolo26");
            _decoder = new Yolo26PoseDecoder(
                schemeAsset.inputSize, 10, true,
                schemeAsset.minCutoff, schemeAsset.beta, schemeAsset.dCutoff
            );
        }


        await UniTask.WaitUntil(() => webcamInput != null && webcamInput.IsReady);

        // 5. 从 PoseManager 读取配置，初始化后处理器
        var config = PoseManager.Instance.poseLocalConfig;
        _postProcessor = new PosePostProcessor(
            config.lostTrackingGracePeriod,
            config.isMirrored,
            config.minAreaSize,
            config.maxPlayerCount
        );

        // 6. 初始化策略
        if (schemeAsset.executionMode == ExecutionMode.Synchronous)
            _strategy = new SyncStrategy(webcamInput.targetRawImage, schemeAsset.inputSize);
        else
            _strategy = new AsyncStrategy(webcamInput.targetRawImage, schemeAsset.inputSize);
    }

    void Update()
    {
        if (webcamInput == null || !webcamInput.IsReady || _strategy == null) return;

        // [核心] 每帧从 PoseManager 同步最新配置
        // 这样用户在设置界面修改参数后，下一帧立即生效
        if (_postProcessor != null && PoseManager.Instance != null)
        {
            var config = PoseManager.Instance.poseLocalConfig;
            _postProcessor.UpdateSettings(
                config.lostTrackingGracePeriod,
                config.isMirrored,
                config.minAreaSize,
                config.maxPlayerCount
            );
        }

        _strategy.ExecuteAsync(webcamInput.Texture, async () =>
        {
            if (!_isWorking) await PerformInference();
        }).Forget();
    }

    async UniTask PerformInference()
    {
        _isWorking = true;
        try
        {
            Texture source = _strategy.GetInferenceSource();

            // 1. 防拉伸
            AspectFitBlit(source, _inferenceRT);

            // 2. 推理
            TextureConverter.ToTensor(_inferenceRT, _inputTensor, _textureTransform);
            _worker.Schedule(_inputTensor);

            if (_worker.PeekOutput() is Tensor<float> output)
            {
                using var cpuTensor = await output.ReadbackAndCloneAsync();

                // 3. 解码
                var rawResults = _decoder.Decode(
                    cpuTensor,
                    schemeAsset.confThreshold,
                    schemeAsset.keyThreshold,
                    schemeAsset.nmsThreshold,
                    new Vector2(source.width, source.height)
                );

                // 4. 后处理（过滤、补偿、镜像、排序 全自动）
                var finalResults = _postProcessor.Process(rawResults);

                // 5. 分发
                PoseManager.Instance.ReceivePoseData(finalResults);

                if (visualizer != null)
                    visualizer.UpdatePoseVisualizations(finalResults, schemeAsset.confThreshold);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Inference Error: {e.Message}");
        }
        finally
        {
            _isWorking = false;
        }
    }

    private void AspectFitBlit(Texture source, RenderTexture dest)
    {
        var prevRT = RenderTexture.active;
        RenderTexture.active = dest;
        GL.Clear(true, true, Color.black);

        float sourceAspect = (float)source.width / source.height;
        float destAspect = (float)dest.width / dest.height;

        float scaleX = 1, scaleY = 1;
        if (sourceAspect > destAspect) scaleY = destAspect / sourceAspect;
        else scaleX = sourceAspect / destAspect;

        GL.PushMatrix();
        GL.LoadPixelMatrix(0, dest.width, dest.height, 0);

        float drawWidth = dest.width * scaleX;
        float drawHeight = dest.height * scaleY;
        float x = (dest.width - drawWidth) * 0.5f;
        float y = (dest.height - drawHeight) * 0.5f;

        Graphics.DrawTexture(new Rect(x, y, drawWidth, drawHeight), source);
        GL.PopMatrix();
        RenderTexture.active = prevRT;
    }

    void OnDisable()
    {
        _worker?.Dispose();
        _inputTensor?.Dispose();
        _strategy?.Dispose();
        if (_inferenceRT != null) _inferenceRT.Release();
    }
}