using UnityEngine;
using Unity.InferenceEngine;

public enum ExecutionMode
{
    Synchronous,
    Asynchronous
}

public enum InferenceType
{
    Yolo8,
    Yolo26,
}

[CreateAssetMenu(fileName = "PoseScheme", menuName = "AI/PoseScheme")]
public class PoseSchemeAsset : ScriptableObject
{
    public InferenceType inferenceType;
    public ModelAsset modelAsset;
    public Vector2Int inputSize = new Vector2Int(640, 640);
    public BackendType backend = BackendType.GPUCompute;
    public ExecutionMode executionMode;

    [Header("Thresholds")] public float confThreshold = 0.5f;
    public float keyThreshold = 0.5f;
    public float nmsThreshold = 0.45f;

    [Header("OneEuro Filter Params")] public float minCutoff = 1.0f;
    public float beta = 10.0f;
    public float dCutoff = 1.0f;
}