using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

// 同步策略：画面等 AI。画面会卡顿，但骨骼与图像绝对对齐。
public class SyncStrategy : IInferenceStrategy
{
    private bool _isProcessing;
    private readonly RawImage _view;
    private readonly RenderTexture _buffer;

    public SyncStrategy(RawImage view, Vector2Int size)
    {
        _view = view;
        // 创建缓冲区，用于冻结画面
        _buffer = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
    }

    public async UniTask ExecuteAsync(WebCamTexture webcam, System.Func<UniTask> performInference)
    {
        if (_isProcessing) return;
        _isProcessing = true;

        // 1. 抓取当前帧到缓冲区
        Graphics.Blit(webcam, _buffer);
        // 2. UI 显示这个被冻结的缓冲区（而不是实时 WebCam）
        _view.texture = _buffer;

        // 3. 等待推理结束（此时 UI 画面是不动的，直到推理完成）
        if (performInference != null)
            await performInference();

        _isProcessing = false;
    }

    public Texture GetInferenceSource() => _buffer;

    // 在实现类中：
    public void Dispose()
    {
        if (_buffer != null)
        {
            _buffer.Release();
            Object.Destroy(_buffer);
        }
    }
}

// 修复后的异步策略：画面 60fps 不断，AI 在后台处理快照。
public class AsyncStrategy : IInferenceStrategy
{
    private readonly RawImage _view;
    private readonly RenderTexture _snapshotBuffer;
    private bool _isAiBusy;

    public AsyncStrategy(RawImage view, Vector2Int size)
    {
        _view = view;
        // 必须准备一个快照缓冲区！
        _snapshotBuffer = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
    }

    public async UniTask ExecuteAsync(WebCamTexture webcam, System.Func<UniTask> performInference)
    {
        // 1. 画面永远显示最流畅的实时摄像头
        if (_view.texture != webcam) _view.texture = webcam;

        // 2. 如果 AI 正在忙，我们直接跳过，不阻塞主线程，让画面继续跑
        if (_isAiBusy) return;

        // 3. AI 空闲，准备发起新一轮推理
        _isAiBusy = true;

        // --- 关键步：立即抓取当前帧快照 ---
        // 这样 AI 处理的是“发起推理那一刻”的图像，而不是推理过程中不断变化的图像
        Graphics.Blit(webcam, _snapshotBuffer);

        // 4. 异步执行推理，不 await 它（或者在 Runner 里用 Forget）
        // 注意：这里用长任务方式运行，不阻塞当前的 Update
        PerformTask(performInference).Forget();
    }

    private async UniTaskVoid PerformTask(System.Func<UniTask> action)
    {
        try
        {
            if (action != null) await action();
        }
        finally
        {
            _isAiBusy = false;
        }
    }

    // AI 推理时，应该读取快照缓冲区
    public Texture GetInferenceSource() => _snapshotBuffer;

    // 在实现类中：
    public void Dispose()
    {
        if (_snapshotBuffer != null)
        {
            _snapshotBuffer.Release();
            Object.Destroy(_snapshotBuffer);
        }
    }
}