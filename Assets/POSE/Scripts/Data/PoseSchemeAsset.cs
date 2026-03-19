using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// 推理执行模式。
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// 同步模式：画面等推理。
    /// 优点：骨骼和画面绝对对齐。
    /// 缺点：画面会卡顿。
    /// </summary>
    Synchronous,

    /// <summary>
    /// 异步模式：画面持续流畅，AI 在后台处理快照。
    /// 优点：显示流畅。
    /// 缺点：骨骼可能相对画面有轻微延迟。
    /// </summary>
    Asynchronous
}

/// <summary>
/// 模型类型。
/// </summary>
public enum InferenceType
{
    Yolo8,
    Yolo26,
}

/// <summary>
/// 姿态推理方案配置。
/// 
/// 用于集中配置：
/// - 模型资源
/// - 输入尺寸
/// - 推理后端
/// - 推理模式
/// - 置信度阈值
/// - OneEuro 平滑参数
/// </summary>
[CreateAssetMenu(fileName = "PoseScheme", menuName = "AI/PoseScheme")]
public class PoseSchemeAsset : ScriptableObject
{
    [Header("Model")]
    public InferenceType inferenceType;
    public ModelAsset modelAsset;
    public Vector2Int inputSize = new Vector2Int(640, 640);
    public BackendType backend = BackendType.GPUCompute;
    public ExecutionMode executionMode;

    [Header("Thresholds")]
    [Tooltip("人体检测框置信度阈值。越高越严格。")]
    public float confThreshold = 0.5f;

    [Tooltip("关键点置信度阈值。低于该值的点将视为无效。")]
    public float keyThreshold = 0.5f;

    [Tooltip("NMS 阈值。越低越容易压掉重叠框。")]
    public float nmsThreshold = 0.45f;

    [Header("OneEuro Filter Params")]
    [Tooltip("最小截止频率。越小越平滑，越大越跟手。")]
    public float minCutoff = 1.0f;

    [Tooltip("速度响应系数。越大表示运动快时越减少平滑。")]
    public float beta = 10.0f;

    [Tooltip("导数低通滤波截止频率。通常保持 1 左右。")]
    public float dCutoff = 1.0f;
}