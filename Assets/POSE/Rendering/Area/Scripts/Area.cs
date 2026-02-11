using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Area : MonoBehaviour
{
    [Header("References")] public Image imgArea;
    public TextMeshProUGUI txtId;

    [Header("Data")] public AreaConfig areaConfig;

    [Header("Debug")] [Tooltip("勾选后，移动 UI 会实时回写数据到 areaConfig")]
    public bool isEditable = false;

    // 缓存 RectTransform 以提升性能
    private RectTransform _rectTransform;

    public void Init(AreaConfig config)
    {
        areaConfig = config;

        // 设置显示 ID
        if (txtId != null)
        {
            txtId.text = (config.id + 1).ToString();
        }

        _rectTransform = GetComponent<RectTransform>();
        _rectTransform.localPosition = new Vector2(config.pos[0], config.pos[1]);
        _rectTransform.sizeDelta = new Vector2(config.sizeDelta[0], config.sizeDelta[1]);
    }

    public void SetColor(Color color)
    {
        imgArea.color = color;
    }

    void Update()
    {
        // 性能优化：只有在编辑模式下才执行回写逻辑
        // 在正式发布时，建议把 isEditable 设为 false
        if (!isEditable || areaConfig == null) return;

        // 实时将 UI 的变动同步回 Config 对象
        // 这样如果你在运行时拖动了 UI，AreaPanel 里的 Config 数据也会更新
        // (注意：这不会自动保存到磁盘，需要你另外写保存逻辑)
        areaConfig.pos[0] = _rectTransform.localPosition.x;
        areaConfig.pos[1] = _rectTransform.localPosition.y;

        areaConfig.sizeDelta[0] = _rectTransform.sizeDelta.x;
        areaConfig.sizeDelta[1] = _rectTransform.sizeDelta.y;
    }
}