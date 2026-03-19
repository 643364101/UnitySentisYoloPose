using UnityEngine;
using Cysharp.Threading.Tasks;
using Unity.InferenceEngine;

/// <summary>
/// 通用姿态推理执行器。
/// 
/// 主要职责：
/// 1. 初始化模型与推理 Worker
/// 2. 从 WebCamSource 获取图像输入
/// 3. 根据执行模式选择同步 / 异步策略
/// 4. 调用解码器将模型输出还原为 HumanPose
/// 5. 调用 PosePostProcessor 做最终业务后处理
/// 6. 将最终结果分发给 PoseManager
/// 
/// 【关键数据流】
/// WebCamTexture / Snapshot
/// -> 模型推理
/// -> Decoder.Decode(...) 输出 Source Space
/// -> PosePostProcessor.Process(...) 输出 Display Space
/// -> PoseManager.ReceivePoseData(...)
/// 
/// 【Source Space / Display Space】
/// 1. Source Space：
///    - 来自模型解码器的原始坐标空间
///    - 面向原始纹理
///    - 不一定与玩家看到的左右一致
/// 
/// 2. Display Space：
///    - 经过 PosePostProcessor 后的最终业务坐标空间
///    - 与玩家看到的画面左右一致
///    - 所有 UI 显示、区域检测、手势识别、截图框计算都应使用它
/// 
/// 【横屏 / 竖屏适配原则】
/// - 推理阶段只处理原始纹理 / 归一化坐标，不依赖屏幕方向
/// - 真正的 UI 对齐由 PoseGeometryUtils / PoseHitTestUtils / BodyPart.ToAnchoredPos
///   基于 cameraView.rectTransform 处理
/// - 因此在横屏、竖屏、黑边、等比缩放情况下都更稳定
/// </summary>
public class UniversalPoseRunner : MonoBehaviour
{
    [Header("Configuration")]
    public PoseSchemeAsset schemeAsset;

    [Header("Input Source")]
    public WebCamSource webcamInput;

    [Header("Visualization")]
    public HumanPoseVisualizer visualizer;

    private Worker _worker;
    private Tensor<float> _inputTensor;
    private IInferenceStrategy _strategy;
    private IPoseDecoder _decoder;
    private TextureTransform _textureTransform;
    private RenderTexture _inferenceRT;
    private bool _isWorking;
    private PosePostProcessor _postProcessor;

    private async void Start()
    {
        if (webcamInput == null)
            webcamInput = FindObjectOfType<WebCamSource>();

        if (schemeAsset == null)
        {
            Debug.LogError("[UniversalPoseRunner] schemeAsset 未赋值！");
            return;
        }

        // --------------------------------------------------------------------
        // 1. 初始化模型与 Worker
        // --------------------------------------------------------------------
        // Worker 负责调度模型推理
        var model = ModelLoader.Load(schemeAsset.modelAsset);
        _worker = new Worker(model, schemeAsset.backend);

        // 输入 Tensor 形状：
        // [N, C, H, W] = [1, 3, inputH, inputW]
        var shape = new TensorShape(1, 3, schemeAsset.inputSize.y, schemeAsset.inputSize.x);
        _inputTensor = new Tensor<float>(shape);

        // --------------------------------------------------------------------
        // 2. 创建推理输入 RenderTexture
        // --------------------------------------------------------------------
        // 作用：
        // - 将任意宽高比的源图像等比缩放到模型输入尺寸
        // - 防止原始画面被拉伸变形
        // - 与解码器中的“逆 Letterbox”逻辑一一对应
        _inferenceRT = new RenderTexture(
            schemeAsset.inputSize.x,
            schemeAsset.inputSize.y,
            0,
            RenderTextureFormat.ARGB32
        );
        _inferenceRT.Create();

        // --------------------------------------------------------------------
        // 3. 配置 Texture -> Tensor 转换规则
        // --------------------------------------------------------------------
        // 说明：
        // - 使用 NCHW 布局
        // - 坐标原点为 TopLeft
        // - 通道顺序按 RGBA 读取
        _textureTransform = new TextureTransform()
            .SetDimensions(schemeAsset.inputSize.x, schemeAsset.inputSize.y)
            .SetTensorLayout(TensorLayout.NCHW)
            .SetChannelSwizzle(ChannelSwizzle.RGBA)
            .SetCoordOrigin(CoordOrigin.TopLeft);

        // --------------------------------------------------------------------
        // 4. 创建解码器
        // --------------------------------------------------------------------
        // Decoder 输出的仍是 Source Space。
        // 真正转换成 Display Space 的责任在 PosePostProcessor。
        if (schemeAsset.inferenceType == InferenceType.Yolo8)
        {
            _decoder = new YoloV8PoseDecoder(
                schemeAsset.inputSize,
                10,
                true,
                schemeAsset.minCutoff,
                schemeAsset.beta,
                schemeAsset.dCutoff
            );
        }
        else if (schemeAsset.inferenceType == InferenceType.Yolo26)
        {
            Debug.Log("[UniversalPoseRunner] 使用 Yolo26 解码器");
            _decoder = new Yolo26PoseDecoder(
                schemeAsset.inputSize,
                10,
                true,
                schemeAsset.minCutoff,
                schemeAsset.beta,
                schemeAsset.dCutoff
            );
        }

        // 等待摄像头就绪后再开始推理
        await UniTask.WaitUntil(() => webcamInput != null && webcamInput.IsReady);

        // --------------------------------------------------------------------
        // 5. 初始化后处理器
        // --------------------------------------------------------------------
        // 后处理器负责：
        // - 过滤
        // - 掉帧补偿
        // - 镜像转换
        // - 人数限制
        // 最终输出 Display Space
        var config = PoseManager.Instance.PoseLocalConfig;
        _postProcessor = new PosePostProcessor(
            config.lostTrackingGracePeriod,
            config.isMirrored,
            config.minAreaSize,
            config.maxPlayerCount
        );

        // --------------------------------------------------------------------
        // 6. 初始化执行策略
        // --------------------------------------------------------------------
        // Sync:
        // - UI 显示冻结帧
        // - 骨骼与画面绝对对齐
        //
        // Async:
        // - UI 持续实时显示
        // - AI 在后台处理快照
        if (schemeAsset.executionMode == ExecutionMode.Synchronous)
        {
            _strategy = new SyncStrategy(webcamInput.targetRawImage, schemeAsset.inputSize);
        }
        else
        {
            _strategy = new AsyncStrategy(webcamInput.targetRawImage, schemeAsset.inputSize);
        }
    }

    private void Update()
    {
        if (webcamInput == null || !webcamInput.IsReady || _strategy == null)
            return;

        // 每帧同步一次动态配置，保证运行时修改参数能立即生效
        if (_postProcessor != null && PoseManager.Instance != null)
        {
            var config = PoseManager.Instance.PoseLocalConfig;
            _postProcessor.UpdateSettings(
                config.lostTrackingGracePeriod,
                config.isMirrored,
                config.minAreaSize,
                config.maxPlayerCount
            );
        }

        _strategy.ExecuteAsync(webcamInput.Texture, async () =>
        {
            if (!_isWorking)
                await PerformInference();
        }).Forget();
    }

    /// <summary>
    /// 执行一次完整推理流程。
    /// 
    /// 流程：
    /// 1. 从策略获取本轮推理输入纹理
    /// 2. 等比绘制到模型输入尺寸（避免拉伸）
    /// 3. 转 Tensor
    /// 4. 模型推理
    /// 5. Decoder 解码（输出 Source Space）
    /// 6. PosePostProcessor 后处理（输出 Display Space）
    /// 7. 可视化 + 数据分发
    /// </summary>
    private async UniTask PerformInference()
    {
        _isWorking = true;

        try
        {
            Texture source = _strategy.GetInferenceSource();
            if (source == null) return;

            // 1. 将输入纹理等比绘制到模型输入 RT
            AspectFitBlit(source, _inferenceRT);

            // 2. 转 Tensor 并执行推理
            TextureConverter.ToTensor(_inferenceRT, _inputTensor, _textureTransform);
            _worker.Schedule(_inputTensor);

            if (_worker.PeekOutput() is Tensor<float> output)
            {
                using var cpuTensor = await output.ReadbackAndCloneAsync();

                // 3. 解码模型输出（Source Space）
                var rawResults = _decoder.Decode(
                    cpuTensor,
                    schemeAsset.confThreshold,
                    schemeAsset.keyThreshold,
                    schemeAsset.nmsThreshold,
                    new Vector2(source.width, source.height)
                );

                // 4. 后处理（Display Space）
                var finalResults = _postProcessor.Process(rawResults);

                // 5. 可视化
                if (visualizer != null)
                {
                    visualizer.UpdatePoseVisualizations(finalResults, schemeAsset.confThreshold);
                }

                // 6. 数据分发
                PoseManager.Instance.ReceivePoseData(finalResults);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UniversalPoseRunner] Inference Error: {e.Message}");
        }
        finally
        {
            _isWorking = false;
        }
    }

    /// <summary>
    /// 将源图像等比绘制到目标 RenderTexture。
    /// 
    /// 作用：
    /// - 保持原始宽高比
    /// - 避免图像直接拉伸到模型输入尺寸
    /// - 空余区域补黑边（Letterbox）
    /// 
    /// 这与解码器中逆向恢复坐标的过程是一一对应的：
    /// - 这里补黑边
    /// - 解码时再把黑边映射还原回原始归一化坐标
    /// </summary>
    private void AspectFitBlit(Texture source, RenderTexture dest)
    {
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = dest;
        GL.Clear(true, true, Color.black);

        float sourceAspect = (float)source.width / source.height;
        float destAspect = (float)dest.width / dest.height;

        float scaleX = 1f;
        float scaleY = 1f;

        if (sourceAspect > destAspect)
        {
            // 源图更宽：上下留黑边
            scaleY = destAspect / sourceAspect;
        }
        else
        {
            // 源图更高：左右留黑边
            scaleX = sourceAspect / destAspect;
        }

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

    private void OnDisable()
    {
        _worker?.Dispose();
        _inputTensor?.Dispose();
        _strategy?.Dispose();

        if (_inferenceRT != null)
            _inferenceRT.Release();
    }
}