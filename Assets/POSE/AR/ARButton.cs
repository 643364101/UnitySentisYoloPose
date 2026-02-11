using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ARButton : Button
{
    public enum AniType
    {
        None,
        Breathing, // 呼吸动画
    }

    [Header("Animation Settings")]
    public AniType aniType;
    public float aniDuration = 2.0f; // 呼吸一个完整周期所需时间
    public Vector3 targetScale = new Vector3(1.1f, 1.1f, 1.1f);
    
    [Header("Progress Settings")]
    public Transform btnParent; // 存放进度条Image的父物体
    public float btnDuration = 1.5f; // 触发所需时间
    [Range(0, 1)] public float thresholdValue = 0.2f; // 初始延迟/防抖阈值

    // 内部状态
    private Vector3 startScale;
    private bool isHovering; 
    private bool isTriggered; 

    private float currentTimer; 
    private float TotalDuration => thresholdValue + btnDuration;

    private Action<float> onUpdateCallback;

    // --- 初始化 ---
    protected override void Awake()
    {
        base.Awake();
        startScale = transform.localScale;
        if (btnParent == null) btnParent = transform;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ResetState();
        UpdateProgressUI(0);
    }

    // --- 主循环 ---
    private void Update()
    {
        HandleScaleAnimation();
        HandleProgressLogic();
    }

    // --- 动画逻辑 (原生实现) ---
    private void HandleScaleAnimation()
    {
        if (aniType == AniType.Breathing && !isHovering)
        {
            // 使用正弦波实现平滑呼吸
            // 让 Sin 的值在 0 到 1 之间循环
            float lerpVal = (Mathf.Sin(Time.time * (Mathf.PI * 2 / aniDuration)) + 1f) * 0.5f;
            transform.localScale = Vector3.Lerp(startScale, targetScale, lerpVal);
        }
        else
        {
            // 处理交互缩放过渡 (Lerp)
            Vector3 target = isHovering ? targetScale : startScale;
            if (Vector3.Distance(transform.localScale, target) > 0.001f)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, target, Time.deltaTime * 10f);
            }
        }
    }

    // --- 进度逻辑 ---
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
            else
            {
                if (!isTriggered)
                {
                    isTriggered = true;
                    UpdateProgressUI(1.0f);
                    onClick?.Invoke();
                }
            }
        }
        else
        {
            // 回退逻辑
            if (currentTimer > 0)
            {
                currentTimer -= Time.deltaTime * 2f; 
                float validProgress = Mathf.Clamp01((currentTimer - thresholdValue) / btnDuration);
                UpdateProgressUI(validProgress);
                onUpdateCallback?.Invoke(validProgress);
            }
            else if (isTriggered)
            {
                isTriggered = false; 
                UpdateProgressUI(0);
            }
        }
    }

    // --- 公开 API ---

    public void SetProgress(Action onStart = null, Action<float> onUpdate = null)
    {
        if (!isHovering)
        {
            isHovering = true;
            isTriggered = false;
            this.onUpdateCallback = onUpdate;
            onStart?.Invoke();
        }
    }

    public void BreakOffProgress(Action onStart = null, Action<float> onUpdate = null)
    {
        if (isHovering)
        {
            isHovering = false;
            this.onUpdateCallback = onUpdate;
            onStart?.Invoke();
        }
    }

    // --- 内部辅助 ---

    private void ResetState()
    {
        isHovering = false;
        isTriggered = false;
        currentTimer = 0;
        transform.localScale = startScale;
    }

    private void UpdateProgressUI(float progress)
    {
        if (btnParent == null) return;
        
        // 优化：避免每帧 GetComponent，如果性能要求高，可以在 Start 里先缓存 Image 数组
        for (int i = 0; i < btnParent.childCount; i++)
        {
            var img = btnParent.GetChild(i).GetComponent<Image>();
            if (img != null && img.type == Image.Type.Filled)
            {
                img.fillAmount = progress;
            }
        }
    }
}