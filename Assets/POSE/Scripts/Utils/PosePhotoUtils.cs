using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 姿态截图工具类。
/// 
/// 主要职责：
/// 1. Display Space Rect -> Source Space Rect 转换
/// 2. 生成截图裁剪框
/// 3. 从原始纹理稳定裁图
/// 4. 输出与屏幕一致或与原始纹理一致的照片
/// 
/// 【核心原则】
/// - 业务层中的 pose.box / WholeRect 默认属于 Display Space
/// - 真正裁图时，必须先将其还原为 Source Space
/// - 输出照片是否镜像，由 mirrorX 决定
/// 
/// 【默认参考区域】
/// 若需要 cameraViewRect，会优先自动使用：
/// PoseManager.Instance.cameraView.rectTransform
/// </summary>
public static class PosePhotoUtils
{
    /// <summary>
    /// 获取默认 cameraViewRect。
    /// </summary>
    private static RectTransform GetDefaultCameraViewRect()
    {
        if (PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return null;

        return PoseManager.Instance.cameraView.rectTransform;
    }

    /// <summary>
    /// 将显示空间 Rect（Display Space）还原为原始纹理空间 Rect（Source Space）。
    /// 
    /// 输入输出都使用归一化 UV 坐标（0~1）。
    /// 
    /// 使用场景：
    /// - 业务层中的 pose.box / WholeRect 默认都属于 Display Space
    /// - 但真正从 Texture 裁图时，必须转换为 Source Space
    /// 
    /// 例子：
    /// - 若当前画面是镜像显示，玩家看到人在右边
    /// - 原始纹理中他其实可能在左边
    /// - 因此裁图前必须把显示空间 rect 翻回去
    /// </summary>
    public static Rect DisplayRectToSourceRect(Rect displayRect, bool isMirrored)
    {
        Rect rect = displayRect;

        if (isMirrored)
        {
            rect.x = 1f - (rect.x + rect.width);
        }

        return Clamp01Rect(rect);
    }

    /// <summary>
    /// 将 Rect 直接裁切到 0~1 范围内。
    /// </summary>
    public static Rect Clamp01Rect(Rect rect)
    {
        float xMin = Mathf.Clamp01(rect.xMin);
        float yMin = Mathf.Clamp01(rect.yMin);
        float xMax = Mathf.Clamp01(rect.xMax);
        float yMax = Mathf.Clamp01(rect.yMax);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    /// <summary>
    /// 将 Rect 平移回 0~1 范围内，但不改变宽高。
    /// 
    /// 前提：
    /// - rect.width <= 1
    /// - rect.height <= 1
    /// 
    /// 用途：
    /// - 保持截图框大小不变
    /// - 只修正位置，使其完整落在画面内
    /// </summary>
    public static Rect Clamp01RectPushInside(Rect rect)
    {
        float x = rect.x;
        float y = rect.y;

        if (x < 0f) x = 0f;
        if (y < 0f) y = 0f;

        if (x + rect.width > 1f) x = 1f - rect.width;
        if (y + rect.height > 1f) y = 1f - rect.height;

        x = Mathf.Clamp01(x);
        y = Mathf.Clamp01(y);

        return new Rect(x, y, rect.width, rect.height);
    }

    /// <summary>
    /// 以中心为基准扩展 Rect，并适配目标宽高比。
    /// 
    /// 输入输出都使用 0~1 的归一化空间。
    /// 
    /// 参数：
    /// - sourceAspect：原始纹理宽高比
    /// - targetAspect：目标输出宽高比（宽 / 高）
    /// - scale：对原始 rect 的放大倍率
    /// 
    /// 用途：
    /// - 生成更美观的拍照取景框
    /// - 保证照片输出比例固定
    /// </summary>
    public static Rect FitRectToAspectKeepCenter(Rect rect, float sourceAspect, float targetAspect, float scale = 1f)
    {
        if (rect.width <= 0f || rect.height <= 0f || sourceAspect <= 0f || targetAspect <= 0f || scale <= 0f)
            return new Rect();

        Vector2 center = rect.center;

        float width = rect.width * scale;
        float height = rect.height * scale;

        float currentPhysicalAspect = (width * sourceAspect) / height;

        if (currentPhysicalAspect > targetAspect)
            height = width * (sourceAspect / targetAspect);
        else
            width = height * (targetAspect / sourceAspect);

        if (width > 1f || height > 1f)
        {
            float uniformScale = 1f / Mathf.Max(width, height);
            width *= uniformScale;
            height *= uniformScale;
        }

        Rect result = new Rect(
            center.x - width * 0.5f,
            center.y - height * 0.5f,
            width,
            height
        );

        return Clamp01RectPushInside(result);
    }

    /// <summary>
    /// 根据人体姿态（Display Space）生成截图所需的 Source Space UV Rect。
    /// 
    /// 流程：
    /// 1. 基于 pose 计算 Display Space 包围盒
    /// 2. 调整为目标宽高比
    /// 3. 若启用镜像，则转换回 Source Space
    /// 4. 返回最终用于裁图的 UV 区域
    /// </summary>
    public static Rect BuildPhotoSourceRectFromPose(
        HumanPose pose,
        Texture source,
        bool isMirrored,
        float targetAspect,
        float scale = 1.2f,
        float padding = 0.08f)
    {
        if (source == null) return new Rect();

        Rect displayRect = pose.WholeRect(null, null, padding);
        if (displayRect.width <= 0f || displayRect.height <= 0f) return new Rect();

        float sourceAspect = (float)source.width / source.height;
        displayRect = FitRectToAspectKeepCenter(displayRect, sourceAspect, targetAspect, scale);
        if (displayRect.width <= 0f || displayRect.height <= 0f) return new Rect();

        Rect sourceRect = DisplayRectToSourceRect(displayRect, isMirrored);
        return Clamp01RectPushInside(sourceRect);
    }

    /// <summary>
    /// 将 UI 取景框转换为原始纹理裁剪 Rect（Source Space）。
    /// 
    /// 使用场景：
    /// - 玩家拖动 UI 取景框
    /// - UI 拍照框跟随人体 / 人脸
    /// - 最终从原始纹理中按这个框裁图
    /// 
    /// 注意：
    /// - uiFrame 表示的是玩家看到的显示区域，因此属于 Display Space 语义
    /// - 若当前画面镜像，最终必须翻回 Source Space
    /// </summary>
    public static Rect BuildPhotoSourceRectFromUIFrame(
        RectTransform uiFrame,
        RectTransform cameraViewRect,
        bool isMirrored)
    {
        if (uiFrame == null || cameraViewRect == null)
            return new Rect();

        Vector3[] corners = new Vector3[4];
        uiFrame.GetWorldCorners(corners);

        Canvas canvas = uiFrame.GetComponentInParent<Canvas>();
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera
            : null;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cameraViewRect,
            RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]),
            uiCam,
            out Vector2 localBL
        );

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cameraViewRect,
            RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]),
            uiCam,
            out Vector2 localTR
        );

        Rect r = cameraViewRect.rect;

        float xMin = Mathf.InverseLerp(r.xMin, r.xMax, localBL.x);
        float yMin = Mathf.InverseLerp(r.yMin, r.yMax, localBL.y);
        float xMax = Mathf.InverseLerp(r.xMin, r.xMax, localTR.x);
        float yMax = Mathf.InverseLerp(r.yMin, r.yMax, localTR.y);

        Rect displayRect = Rect.MinMaxRect(
            Mathf.Clamp01(Mathf.Min(xMin, xMax)),
            Mathf.Clamp01(Mathf.Min(yMin, yMax)),
            Mathf.Clamp01(Mathf.Max(xMin, xMax)),
            Mathf.Clamp01(Mathf.Max(yMin, yMax))
        );

        return DisplayRectToSourceRect(displayRect, isMirrored);
    }

    /// <summary>
    /// 使用默认 cameraView 作为 cameraViewRect。
    /// </summary>
    public static Rect BuildPhotoSourceRectFromUIFrame(RectTransform uiFrame, bool isMirrored)
    {
        RectTransform cameraViewRect = GetDefaultCameraViewRect();
        if (cameraViewRect == null) return new Rect();

        return BuildPhotoSourceRectFromUIFrame(uiFrame, cameraViewRect, isMirrored);
    }

    /// <summary>
    /// 稳定 GPU 截图。
    /// 
    /// 输入要求：
    /// - sourceUvRect 必须是 Source Space 下的 UV 区域
    /// 
    /// 参数 mirrorX 的含义：
    /// - true：最终输出照片与“玩家当前看到的画面”左右方向一致
    /// - false：最终输出照片保持摄像头原始方向
    /// 
    /// 当前项目常用策略：
    /// - 先将 Display Space rect 转成 Source Space rect
    /// - 再根据 isMirrored 决定最终输出图像是否镜像
    /// 这样用户拍到的照片通常和屏幕所见一致
    /// </summary>
    public static async UniTask<Texture2D> TakeNicePhotoAsync(
        Rect sourceUvRect,
        Texture source,
        int targetHeight = 648,
        float targetAspect = 245f / 324f,
        bool mirrorX = true)
    {
        if (source == null) return null;
        if (!SystemInfo.supportsAsyncGPUReadback) return null;

        sourceUvRect = Clamp01Rect(sourceUvRect);
        if (sourceUvRect.width <= 0f || sourceUvRect.height <= 0f)
            return null;

        int targetWidth = Mathf.Max(1, Mathf.RoundToInt(targetHeight * targetAspect));
        RenderTexture tempRT = RenderTexture.GetTemporary(
            targetWidth,
            targetHeight,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        );
        tempRT.filterMode = FilterMode.Bilinear;

        try
        {
            Vector2 scale = new Vector2(sourceUvRect.width, sourceUvRect.height);
            Vector2 offset = new Vector2(sourceUvRect.x, sourceUvRect.y);

            if (mirrorX)
            {
                scale.x = -sourceUvRect.width;
                offset.x = sourceUvRect.x + sourceUvRect.width;
            }

            Graphics.Blit(source, tempRT, scale, offset);

            var request = await AsyncGPUReadback
                .Request(tempRT, 0, TextureFormat.RGBA32)
                .ToUniTask();

            if (request.hasError)
                return null;

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            var gpuData = request.GetData<byte>();
            var texData = result.GetRawTextureData<byte>();

            if (gpuData.Length == texData.Length)
            {
                texData.CopyFrom(gpuData);
            }
            else
            {
                int rowPitch = gpuData.Length / targetHeight;
                int texturePitch = targetWidth * 4;

                for (int y = 0; y < targetHeight; y++)
                {
                    Unity.Collections.NativeArray<byte>.Copy(
                        gpuData, y * rowPitch,
                        texData, y * texturePitch,
                        texturePitch
                    );
                }
            }

            result.Apply();
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"GPU Readback 截图异常: {e.Message}");
            return null;
        }
        finally
        {
            RenderTexture.ReleaseTemporary(tempRT);
        }
    }

    /// <summary>
    /// 基于人体姿态截图。
    /// 
    /// 输入 pose 默认属于 Display Space；
    /// 内部会先自动还原到 Source Space 再裁图。
    /// </summary>
    public static async UniTask<Texture2D> CapturePosePhotoAsync(
        HumanPose pose,
        Texture source,
        bool isMirrored,
        int targetHeight = 648,
        float targetAspect = 245f / 324f,
        float scale = 1.2f,
        float padding = 0.08f)
    {
        if (source == null) return null;

        Rect sourceRect = BuildPhotoSourceRectFromPose(
            pose,
            source,
            isMirrored,
            targetAspect,
            scale,
            padding
        );

        if (sourceRect.width <= 0f || sourceRect.height <= 0f)
            return null;

        return await TakeNicePhotoAsync(
            sourceRect,
            source,
            targetHeight,
            targetAspect,
            mirrorX: isMirrored
        );
    }

    /// <summary>
    /// 基于 UI 取景框截图。
    /// 
    /// 适合：
    /// - UI 拍照框
    /// - 拖拽截图区域
    /// - 所见即所得裁图
    /// </summary>
    public static async UniTask<Texture2D> CaptureFramePhotoAsync(
        RectTransform uiFrame,
        RectTransform cameraViewRect,
        Texture source,
        bool isMirrored,
        int targetHeight = 648,
        float targetAspect = 245f / 324f)
    {
        if (uiFrame == null || cameraViewRect == null || source == null)
            return null;

        Rect sourceRect = BuildPhotoSourceRectFromUIFrame(uiFrame, cameraViewRect, isMirrored);
        if (sourceRect.width <= 0f || sourceRect.height <= 0f)
            return null;

        return await TakeNicePhotoAsync(
            sourceRect,
            source,
            targetHeight,
            targetAspect,
            mirrorX: isMirrored
        );
    }

    /// <summary>
    /// 使用默认 cameraView 作为 cameraViewRect。
    /// </summary>
    public static async UniTask<Texture2D> CaptureFramePhotoAsync(
        RectTransform uiFrame,
        Texture source,
        bool isMirrored,
        int targetHeight = 648,
        float targetAspect = 245f / 324f)
    {
        RectTransform cameraViewRect = GetDefaultCameraViewRect();
        if (cameraViewRect == null || uiFrame == null || source == null)
            return null;

        return await CaptureFramePhotoAsync(
            uiFrame,
            cameraViewRect,
            source,
            isMirrored,
            targetHeight,
            targetAspect
        );
    }

    /// <summary>
    /// 截取整张原始画面。
    /// 
    /// 若 mirrorX = isMirrored，则输出图像与当前显示方向一致。
    /// </summary>
    public static async UniTask<Texture2D> CaptureFullPhotoAsync(
        Texture source,
        bool isMirrored,
        int targetHeight = 1080)
    {
        if (source == null)
            return null;

        float targetAspect = (float)source.width / source.height;

        return await TakeNicePhotoAsync(
            new Rect(0f, 0f, 1f, 1f),
            source,
            targetHeight,
            targetAspect,
            mirrorX: isMirrored
        );
    }
}