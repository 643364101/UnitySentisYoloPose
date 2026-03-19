using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 基于“停留时长”触发的 AR / Pose 交互按钮。
/// 
/// 使用方式：
/// - 外部检测到“手 / 关键点进入按钮区域”时调用 SetProgress()
/// - 外部检测到“手 / 关键点离开按钮区域”时调用 BreakOffProgress()
/// 
/// 按钮内部会：
/// - 播放缩放动画
/// - 累积停留时间
/// - 到达阈值后自动触发 onClick
/// - 控制若干 Filled Image 作为进度条显示
/// 
/// 适用场景：
/// - 空中触摸按钮
/// - 姿态停留触发
/// - AR / 体感交互 UI
/// </summary>
public class ARButton : Button
{
    public enum AniType
    {
        None,
        Breathing,
    }

    [Header("Animation Settings")]
    public AniType aniType;
    public float aniDuration = 2.0f;
    public Vector3 targetScale = new Vector3(1.1f, 1.1f, 1.1f);

    [Header("Progress Settings")]
    [Tooltip("进度条 Image 的父节点。其子节点中所有 Filled Image 都会被同步刷新。")]
    public Transform btnParent;

    [Tooltip("扣除 thresholdValue 后，真正完成触发所需的停留时长。")]
    public float btnDuration = 1.5f;

    [Range(0f, 1f)]
    [Tooltip("初始防抖时长。手刚进入区域不会立刻开始增长可见进度。")]
    public float thresholdValue = 0.2f;

    private Vector3 startScale;
    private bool isHovering;
    private bool isTriggered;
    private float currentTimer;

    /// <summary>
    /// 总触发时长 = 初始防抖时间 + 真正有效进度时间。
    /// </summary>
    private float TotalDuration => thresholdValue + btnDuration;

    /// <summary>
    /// 外部可选的进度更新回调。
    /// </summary>
    private Action<float> onUpdateCallback;

    /// <summary>
    /// 缓存所有进度条 Image，避免每帧 GetComponent。
    /// </summary>
    private readonly List<Image> progressImages = new List<Image>();

    protected override void Awake()
    {
        base.Awake();
        startScale = transform.localScale;

        if (btnParent == null)
            btnParent = transform;

        CacheProgressImages();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResetState();
        UpdateProgressUI(0f);
    }

    /// <summary>
    /// 当子节点结构变化时，重新缓存 Filled Image。
    /// 适合运行时动态替换进度条 UI 的情况。
    /// </summary>
    private void OnTransformChildrenChanged()
    {
        CacheProgressImages();
    }

    private void Update()
    {
        HandleScaleAnimation();
        HandleProgressLogic();
    }

    /// <summary>
    /// 缓存进度条图片，只收集 Filled 类型的 Image。
    /// </summary>
    private void CacheProgressImages()
    {
        progressImages.Clear();
        if (btnParent == null) return;

        for (int i = 0; i < btnParent.childCount; i++)
        {
            Image img = btnParent.GetChild(i).GetComponent<Image>();
            if (img != null && img.type == Image.Type.Filled)
            {
                progressImages.Add(img);
            }
        }
    }

    /// <summary>
    /// 缩放动画逻辑：
    /// - 非 Hover 状态可播放呼吸动画
    /// - Hover / 非 Hover 切换时做平滑过渡
    /// </summary>
    private void HandleScaleAnimation()
    {
        if (aniType == AniType.Breathing && !isHovering)
        {
            float lerpVal = (Mathf.Sin(Time.time * (Mathf.PI * 2f / aniDuration)) + 1f) * 0.5f;
            transform.localScale = Vector3.Lerp(startScale, targetScale, lerpVal);
        }
        else
        {
            Vector3 target = isHovering ? targetScale : startScale;
            if (Vector3.Distance(transform.localScale, target) > 0.001f)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, target, Time.deltaTime * 10f);
            }
        }
    }

    /// <summary>
    /// 进度累计 / 回退逻辑。
    /// </summary>
    private void HandleProgressLogic()
    {
        if (isHovering)
        {
            if (currentTimer < TotalDuration)
            {
                currentTimer += Time.deltaTime;
                float validProgress = Mathf.Clamp01((currentTimer - thresholdValue) / btnDuration);

                UpdateProgressUI(validProgress);
                onUpdateCallback?.Invoke(validProgress);
            }
            else if (!isTriggered)
            {
                isTriggered = true;
                UpdateProgressUI(1f);
                onClick?.Invoke();
            }
        }
        else
        {
            if (currentTimer > 0f)
            {
                // 回退速度比增长快一些，减少“拖泥带水”感
                currentTimer -= Time.deltaTime * 2f;
                currentTimer = Mathf.Max(0f, currentTimer);

                float validProgress = Mathf.Clamp01((currentTimer - thresholdValue) / btnDuration);
                UpdateProgressUI(validProgress);
                onUpdateCallback?.Invoke(validProgress);
            }
            else
            {
                if (isTriggered)
                    isTriggered = false;

                UpdateProgressUI(0f);
            }
        }
    }

    /// <summary>
    /// 告知按钮：当前有交互目标停留在按钮上。
    /// 
    /// 一般在“骨骼点进入按钮区域”时调用。
    /// </summary>
    public void SetProgress(Action onStart = null, Action<float> onUpdate = null)
    {
        if (!isHovering)
        {
            isHovering = true;
            isTriggered = false;
            onUpdateCallback = onUpdate;
            onStart?.Invoke();
        }
    }

    /// <summary>
    /// 告知按钮：当前交互目标已离开按钮。
    /// 
    /// 一般在“骨骼点离开按钮区域”时调用。
    /// </summary>
    public void BreakOffProgress(Action onStart = null, Action<float> onUpdate = null)
    {
        if (isHovering)
        {
            isHovering = false;
            onUpdateCallback = onUpdate;
            onStart?.Invoke();
        }
    }

    /// <summary>
    /// 重置内部状态。
    /// </summary>
    private void ResetState()
    {
        isHovering = false;
        isTriggered = false;
        currentTimer = 0f;
        transform.localScale = startScale;
    }

    /// <summary>
    /// 更新所有进度条 Image 的 fillAmount。
    /// </summary>
    private void UpdateProgressUI(float progress)
    {
        for (int i = 0; i < progressImages.Count; i++)
        {
            if (progressImages[i] != null)
                progressImages[i].fillAmount = progress;
        }
    }
}