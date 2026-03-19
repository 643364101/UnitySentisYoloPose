using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 面板当前命中的边缘类型。
/// 
/// 当前版本仅支持：
/// - 上 / 下 / 左 / 右
/// - 中间区域拖拽移动
/// 
/// 若未来需要支持四角同时缩放，可再扩展：
/// - TopLeft
/// - TopRight
/// - BottomLeft
/// - BottomRight
/// </summary>
public enum UIEdge
{
    None,
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>
/// 通用 UI 面板拖拽与边缘缩放组件。
/// 
/// 主要功能：
/// 1. 点击中间区域：拖拽移动
/// 2. 点击靠近边缘区域：拖拽缩放
/// 3. 支持不同 Pivot
/// 4. 支持不同 Canvas 模式
/// 
/// 设计说明：
/// - 在 ScreenSpace-Overlay 模式下，arCamera 可为空
/// - 在 ScreenSpace-Camera / WorldSpace 模式下，建议传入 UI 所用相机
/// - 内部会根据 Canvas.scaleFactor 将屏幕像素 delta 转换为 UI 逻辑坐标 delta
/// 
/// 适用场景：
/// - 编辑区域框
/// - 拍照取景框拖拽缩放
/// - 调试面板 / 可编辑 UI 框
/// </summary>
public class ResizePanel : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerUpHandler
{
    [Header("Settings")]
    [Tooltip("渲染 UI 的相机。若 Canvas 为 ScreenSpace-Overlay，可不填。")]
    public Camera arCamera;

    [Tooltip("边缘检测百分比（0~1）。值越大，越容易被识别为边缘区域。")]
    [Range(0f, 1f)]
    public float monitorPercent = 0.8f;

    [Header("Limits")]
    public Vector2 minSize = new Vector2(100f, 100f);
    public Vector2 maxSize = new Vector2(1920f, 1080f);

    [Header("Edge Highlight")]
    public Transform rightImage;
    public Transform bottomImage;
    public Transform topImage;
    public Transform leftImage;

    private RectTransform panelRectTransform;
    private Canvas parentCanvas;

    private bool isPointerDown = false;
    private UIEdge currentEdge = UIEdge.None;
    private Coroutine edgeJudgeCoroutine;

    private void Start()
    {
        panelRectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (arCamera == null && parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            arCamera = parentCanvas.worldCamera;
        }

        SetActiveEdgeImage(false);
    }

    public void OnPointerDown(PointerEventData data)
    {
        isPointerDown = true;

        // 若已经命中边缘，则停止边缘检测协程，锁定当前 edge
        if (currentEdge != UIEdge.None)
        {
            StopEdgeJudgeCoroutine();
        }
    }

    public void OnDrag(PointerEventData data)
    {
        if (panelRectTransform == null || !isPointerDown)
            return;

        // 将屏幕像素 delta 转换成 UI 逻辑坐标 delta
        float scale = parentCanvas != null ? parentCanvas.scaleFactor : 1f;
        if (Mathf.Approximately(scale, 0f))
            scale = 1f;

        Vector2 localDelta = data.delta / scale;

        // 1. 中间区域拖拽移动
        if (currentEdge == UIEdge.None)
        {
            panelRectTransform.anchoredPosition += localDelta;
            return;
        }

        // 2. 边缘缩放
        Vector2 oldSize = panelRectTransform.sizeDelta;
        Vector2 newSize = oldSize;
        Vector2 pivot = panelRectTransform.pivot;
        Vector2 posDelta = Vector2.zero;

        switch (currentEdge)
        {
            case UIEdge.Right:
                newSize.x = Mathf.Clamp(oldSize.x + localDelta.x, minSize.x, maxSize.x);
                posDelta.x = (newSize.x - oldSize.x) * pivot.x;
                break;

            case UIEdge.Left:
                newSize.x = Mathf.Clamp(oldSize.x - localDelta.x, minSize.x, maxSize.x);
                posDelta.x = -(newSize.x - oldSize.x) * (1f - pivot.x);
                break;

            case UIEdge.Top:
                newSize.y = Mathf.Clamp(oldSize.y + localDelta.y, minSize.y, maxSize.y);
                posDelta.y = (newSize.y - oldSize.y) * pivot.y;
                break;

            case UIEdge.Bottom:
                newSize.y = Mathf.Clamp(oldSize.y - localDelta.y, minSize.y, maxSize.y);
                posDelta.y = -(newSize.y - oldSize.y) * (1f - pivot.y);
                break;
        }

        panelRectTransform.sizeDelta = newSize;
        panelRectTransform.anchoredPosition += posDelta;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;
        StartEdgeJudgeCoroutine();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isPointerDown) return;
        StartEdgeJudgeCoroutine();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isPointerDown) return;

        StopEdgeJudgeCoroutine();
        currentEdge = UIEdge.None;
        SetActiveEdgeImage(false);
    }

    /// <summary>
    /// 统一控制边缘高亮显示开关。
    /// </summary>
    private void SetActiveEdgeImage(bool isActive)
    {
        if (rightImage) rightImage.gameObject.SetActive(isActive);
        if (bottomImage) bottomImage.gameObject.SetActive(isActive);
        if (leftImage) leftImage.gameObject.SetActive(isActive);
        if (topImage) topImage.gameObject.SetActive(isActive);
    }

    private void StartEdgeJudgeCoroutine()
    {
        StopEdgeJudgeCoroutine();
        edgeJudgeCoroutine = StartCoroutine(EdgeJudgeCoroutine());
    }

    private void StopEdgeJudgeCoroutine()
    {
        if (edgeJudgeCoroutine != null)
        {
            StopCoroutine(edgeJudgeCoroutine);
            edgeJudgeCoroutine = null;
        }
    }

    /// <summary>
    /// 鼠标悬停时持续检测当前命中的边缘类型。
    /// </summary>
    private IEnumerator EdgeJudgeCoroutine()
    {
        while (true)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    panelRectTransform,
                    Input.mousePosition,
                    arCamera,
                    out Vector2 localMousePos))
            {
                yield return null;
                continue;
            }

            currentEdge = GetCurrentEdge(localMousePos);
            UpdateEdgeImages(currentEdge, localMousePos);

            yield return null;
        }
    }

    /// <summary>
    /// 根据鼠标在本地坐标中的位置，判断当前命中的边缘。
    /// </summary>
    private UIEdge GetCurrentEdge(Vector2 pos)
    {
        float width = panelRectTransform.rect.width;
        float height = panelRectTransform.rect.height;

        float xThreshold = width * 0.5f * monitorPercent;
        float yThreshold = height * 0.5f * monitorPercent;

        if (pos.x < -xThreshold) return UIEdge.Left;
        if (pos.x > xThreshold) return UIEdge.Right;
        if (pos.y < -yThreshold) return UIEdge.Bottom;
        if (pos.y > yThreshold) return UIEdge.Top;

        return UIEdge.None;
    }

    /// <summary>
    /// 根据当前命中的边缘，刷新边缘高亮条位置。
    /// </summary>
    private void UpdateEdgeImages(UIEdge edge, Vector2 localPos)
    {
        SetActiveEdgeImage(false);

        float w = panelRectTransform.rect.width;
        float h = panelRectTransform.rect.height;
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;

        switch (edge)
        {
            case UIEdge.Left:
                if (leftImage)
                {
                    leftImage.gameObject.SetActive(true);
                    leftImage.localPosition = new Vector3(
                        leftImage.localPosition.x,
                        Mathf.Clamp(localPos.y, -halfH, halfH),
                        0f
                    );
                }
                break;

            case UIEdge.Right:
                if (rightImage)
                {
                    rightImage.gameObject.SetActive(true);
                    rightImage.localPosition = new Vector3(
                        rightImage.localPosition.x,
                        Mathf.Clamp(localPos.y, -halfH, halfH),
                        0f
                    );
                }
                break;

            case UIEdge.Top:
                if (topImage)
                {
                    topImage.gameObject.SetActive(true);
                    topImage.localPosition = new Vector3(
                        Mathf.Clamp(localPos.x, -halfW, halfW),
                        topImage.localPosition.y,
                        0f
                    );
                }
                break;

            case UIEdge.Bottom:
                if (bottomImage)
                {
                    bottomImage.gameObject.SetActive(true);
                    bottomImage.localPosition = new Vector3(
                        Mathf.Clamp(localPos.x, -halfW, halfW),
                        bottomImage.localPosition.y,
                        0f
                    );
                }
                break;
        }
    }
}