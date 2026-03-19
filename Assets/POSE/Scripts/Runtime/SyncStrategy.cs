using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>
/// 同步推理策略：
/// 画面冻结，等待 AI 处理完成后再进入下一帧。
/// 
/// 特点：
/// - UI 显示的是“本次推理使用的同一帧图像”
/// - 骨骼与图像绝对对齐
/// - 但画面会卡顿，不适合追求流畅度的场景
/// </summary>
public class SyncStrategy : IInferenceStrategy
{
    private bool _isProcessing;
    private readonly RawImage _view;
    private readonly RenderTexture _buffer;

    public SyncStrategy(RawImage view, Vector2Int size)
    {
        _view = view;
        _buffer = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
        _buffer.Create();
    }

    public async UniTask ExecuteAsync(WebCamTexture webcam, System.Func<UniTask> performInference)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        // 1. 将当前摄像头帧抓到缓冲区
        Graphics.Blit(webcam, _buffer);

        // 2. UI 显示缓冲区，而不是实时 webcam
        _view.texture = _buffer;

        // 3. 推理直接处理这张冻结帧
        if (performInference != null)
            await performInference();

        _isProcessing = false;
    }

    /// <summary>
    /// 推理输入源：冻结帧缓冲区。
    /// </summary>
    public Texture GetInferenceSource() => _buffer;

    public void Dispose()
    {
        if (_buffer != null)
        {
            _buffer.Release();
            Object.Destroy(_buffer);
        }
    }
}

/// <summary>
/// 异步推理策略：
/// UI 一直显示实时摄像头，AI 在后台处理快照。
/// 
/// 特点：
/// - 画面流畅
/// - AI 不阻塞显示
/// - 骨骼相对于实时画面可能有轻微延迟
/// 
/// 适合：
/// - 更关注视觉流畅度
/// - 玩家不需要逐帧绝对对齐的交互场景
/// </summary>
public class AsyncStrategy : IInferenceStrategy
{
    private readonly RawImage _view;
    private readonly RenderTexture _snapshotBuffer;
    private bool _isAiBusy;

    public AsyncStrategy(RawImage view, Vector2Int size)
    {
        _view = view;
        _snapshotBuffer = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
        _snapshotBuffer.Create();
    }

    public async UniTask ExecuteAsync(WebCamTexture webcam, System.Func<UniTask> performInference)
    {
        // 1. UI 始终显示实时摄像头
        if (_view.texture != webcam)
            _view.texture = webcam;

        // 2. 若 AI 忙，则本帧跳过，保证显示不卡
        if (_isAiBusy)
            return;

        _isAiBusy = true;

        // 3. 抓取当前帧快照，作为本轮 AI 推理输入
        Graphics.Blit(webcam, _snapshotBuffer);

        // 4. 在后台执行推理
        PerformTask(performInference).Forget();
        await UniTask.CompletedTask;
    }

    private async UniTaskVoid PerformTask(System.Func<UniTask> action)
    {
        try
        {
            if (action != null)
                await action();
        }
        finally
        {
            _isAiBusy = false;
        }
    }

    /// <summary>
    /// 推理输入源：快照缓冲区，而不是实时 webcam。
    /// </summary>
    public Texture GetInferenceSource() => _snapshotBuffer;

    public void Dispose()
    {
        if (_snapshotBuffer != null)
        {
            _snapshotBuffer.Release();
            Object.Destroy(_snapshotBuffer);
        }
    }
}