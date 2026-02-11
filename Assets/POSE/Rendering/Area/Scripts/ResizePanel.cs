/*
 *FileName:      ResizePanel.cs
 *Description:   通用的UI拖拽与边缘缩放脚本，支持任意Pivot和Canvas模式。
 */

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public enum UI_Edge
{
    None,
    Top,
    Down,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public class ResizePanel : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler, IPointerUpHandler
{
    [Header("设置")] [Tooltip("渲染UI的相机，ScreenSpace-Overlay模式可不填")]
    public Camera arCamera;

    [Tooltip("检测边距百分比 (0-1)")] [Range(0f, 1f)]
    public float MonitorPercent = 0.8f;

    [Header("限制")] public Vector2 minSize = new Vector2(100, 100);
    public Vector2 maxSize = new Vector2(1920, 1080);

    [Header("边缘高亮图片")] public Transform Right_Image;
    public Transform Down_Image;
    public Transform Top_Image;
    public Transform Left_Image;

    // 内部变量
    private RectTransform panelRectTransform;
    private Canvas _parentCanvas; // 缓存Canvas以获取缩放比
    private bool isPointerDown = false;
    private UI_Edge currentEdge = UI_Edge.None;

    private void Start()
    {
        panelRectTransform = transform.GetComponent<RectTransform>();
        _parentCanvas = GetComponentInParent<Canvas>();

        // 自动获取相机 (如果是Overlay模式，arCamera保持null即可)
        if (arCamera == null && _parentCanvas != null && _parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            arCamera = _parentCanvas.worldCamera;
        }

        SetActiveEdgeImage(false);
    }

    public void OnPointerDown(PointerEventData data)
    {
        // 按下时，无论是否选中边缘，都标记为按下状态
        isPointerDown = true;

        // 如果选中了边缘，停止检测协程，锁定当前的 Edge 状态
        if (currentEdge != UI_Edge.None)
        {
            StopCoroutine("EdgeJudgeCoroutine");
        }
    }

    public void OnDrag(PointerEventData data)
    {
        if (panelRectTransform == null || !isPointerDown) return;

        // 获取 Canvas 缩放系数
        float scale = _parentCanvas != null ? _parentCanvas.scaleFactor : 1.0f;
        if (scale == 0) scale = 1.0f;

        // 将屏幕像素 delta 转换为 UI 逻辑坐标 delta
        Vector2 localDelta = data.delta / scale;

        // 1. 普通拖拽移动
        if (currentEdge == UI_Edge.None)
        {
            // 直接使用 localDelta 累加，这样无论 Pivot 在哪，移动都是线性的
            panelRectTransform.anchoredPosition += localDelta;
            return;
        }

        // 2. 边缘缩放逻辑
        Vector2 oldSize = panelRectTransform.sizeDelta;
        Vector2 newSize = oldSize;
        Vector2 pivot = panelRectTransform.pivot;
        Vector2 posDelta = Vector2.zero;

        switch (currentEdge)
        {
            case UI_Edge.Right:
                newSize.x = Mathf.Clamp(oldSize.x + localDelta.x, minSize.x, maxSize.x);
                // 补偿公式：(新宽 - 旧宽) * pivot.x
                posDelta.x = (newSize.x - oldSize.x) * pivot.x;
                break;

            case UI_Edge.Left:
                newSize.x = Mathf.Clamp(oldSize.x - localDelta.x, minSize.x, maxSize.x);
                // 补偿公式：-(新宽 - 旧宽) * (1 - pivot.x)
                posDelta.x = -(newSize.x - oldSize.x) * (1f - pivot.x);
                break;

            case UI_Edge.Top:
                newSize.y = Mathf.Clamp(oldSize.y + localDelta.y, minSize.y, maxSize.y);
                // 补偿公式：(新高 - 旧高) * pivot.y
                posDelta.y = (newSize.y - oldSize.y) * pivot.y;
                break;

            case UI_Edge.Down:
                newSize.y = Mathf.Clamp(oldSize.y - localDelta.y, minSize.y, maxSize.y);
                // 补偿公式：-(新高 - 旧高) * (1 - pivot.y)
                posDelta.y = -(newSize.y - oldSize.y) * (1f - pivot.y);
                break;
        }

        // 应用更改
        panelRectTransform.sizeDelta = newSize;
        panelRectTransform.anchoredPosition += posDelta;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;
        // 抬起后重启边缘检测
        StartCoroutine("EdgeJudgeCoroutine");
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isPointerDown) return;
        StartCoroutine("EdgeJudgeCoroutine");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isPointerDown) return;

        StopCoroutine("EdgeJudgeCoroutine");
        currentEdge = UI_Edge.None;
        SetActiveEdgeImage(false);
    }

    private void SetActiveEdgeImage(bool isActive)
    {
        if (Right_Image) Right_Image.gameObject.SetActive(isActive);
        if (Down_Image) Down_Image.gameObject.SetActive(isActive);
        if (Left_Image) Left_Image.gameObject.SetActive(isActive);
        if (Top_Image) Top_Image.gameObject.SetActive(isActive);
    }

    // 边缘检测协程
    IEnumerator EdgeJudgeCoroutine()
    {
        while (true)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRectTransform, Input.mousePosition, arCamera, out Vector2 localMousePos))
            {
                yield return null;
                continue;
            }

            currentEdge = GetCurrentEdge(localMousePos);
            UpdateEdgeImages(currentEdge, localMousePos);

            yield return null;
        }
    }

    private UI_Edge GetCurrentEdge(Vector2 pos)
    {
        float width = panelRectTransform.rect.width;
        float height = panelRectTransform.rect.height;

        float xThreshold = width * 0.5f * MonitorPercent;
        float yThreshold = height * 0.5f * MonitorPercent;

        if (pos.x < -xThreshold) return UI_Edge.Left;
        if (pos.x > xThreshold) return UI_Edge.Right;
        if (pos.y < -yThreshold) return UI_Edge.Down;
        if (pos.y > yThreshold) return UI_Edge.Top;

        return UI_Edge.None;
    }

    private void UpdateEdgeImages(UI_Edge edge, Vector2 localPos)
    {
        SetActiveEdgeImage(false);
        float w = panelRectTransform.rect.width;
        float h = panelRectTransform.rect.height;

        // 计算高亮条的位置限制 (防止跑出UI)
        float halfW = w / 2;
        float halfH = h / 2;

        switch (edge)
        {
            case UI_Edge.Left:
                if (Left_Image)
                {
                    Left_Image.gameObject.SetActive(true);
                    Left_Image.localPosition = new Vector3(Left_Image.localPosition.x, Mathf.Clamp(localPos.y, -halfH, halfH), 0);
                }

                break;
            case UI_Edge.Right:
                if (Right_Image)
                {
                    Right_Image.gameObject.SetActive(true);
                    Right_Image.localPosition = new Vector3(Right_Image.localPosition.x, Mathf.Clamp(localPos.y, -halfH, halfH), 0);
                }

                break;
            case UI_Edge.Top:
                if (Top_Image)
                {
                    Top_Image.gameObject.SetActive(true);
                    Top_Image.localPosition = new Vector3(Mathf.Clamp(localPos.x, -halfW, halfW), Top_Image.localPosition.y, 0);
                }

                break;
            case UI_Edge.Down:
                if (Down_Image)
                {
                    Down_Image.gameObject.SetActive(true);
                    Down_Image.localPosition = new Vector3(Mathf.Clamp(localPos.x, -halfW, halfW), Down_Image.localPosition.y, 0);
                }

                break;
        }
    }
}