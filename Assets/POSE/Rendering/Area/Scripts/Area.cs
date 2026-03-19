using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 单个区域 UI。
/// 
/// 作用：
/// - 根据 AreaConfig 初始化位置 / 尺寸 / 文本
/// - 提供颜色切换
/// - 可在编辑模式下将运行时拖动结果回写到配置对象
/// </summary>
public class Area : MonoBehaviour
{
    [Header("References")]
    public Image imgArea;
    public TextMeshProUGUI txtId;

    [Header("Data")]
    public AreaConfig areaConfig;

    [Header("Debug")]
    [Tooltip("勾选后，运行时移动 UI 会实时回写到 areaConfig。仅用于调试编辑。")]
    public bool isEditable = false;

    private RectTransform _rectTransform;

    public void Init(AreaConfig config)
    {
        areaConfig = config;

        if (txtId != null)
            txtId.text = (config.id + 1).ToString();

        _rectTransform = GetComponent<RectTransform>();
        if (_rectTransform == null)
        {
            Debug.LogError("[Area] 缺少 RectTransform。");
            return;
        }

        // 容错：防止 JSON 数据不完整
        if (areaConfig.pos == null || areaConfig.pos.Length < 2)
            areaConfig.pos = new float[2];

        if (areaConfig.sizeDelta == null || areaConfig.sizeDelta.Length < 2)
            areaConfig.sizeDelta = new float[2] { 100f, 100f };

        _rectTransform.anchoredPosition = new Vector2(areaConfig.pos[0], areaConfig.pos[1]);
        _rectTransform.sizeDelta = new Vector2(areaConfig.sizeDelta[0], areaConfig.sizeDelta[1]);
    }

    public void SetColor(Color color)
    {
        if (imgArea != null)
            imgArea.color = color;
    }

    private void Update()
    {
        if (!isEditable || areaConfig == null || _rectTransform == null)
            return;

        // 将运行时 UI 位置回写到配置对象
        Vector2 pos = _rectTransform.anchoredPosition;
        Vector2 size = _rectTransform.sizeDelta;

        areaConfig.pos[0] = pos.x;
        areaConfig.pos[1] = pos.y;

        areaConfig.sizeDelta[0] = size.x;
        areaConfig.sizeDelta[1] = size.y;
    }
}