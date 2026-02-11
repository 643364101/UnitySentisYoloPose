using UnityEngine;
using System.Collections.Generic;
using Unity.InferenceEngine;

// 内部使用的中间结构体
struct DetectionCandidate
{
    public int index;
    public float confidence;
    public float x, y, width, height;
}

public class YoloV8PoseDecoder : IPoseDecoder
{
    // --- 追踪相关类 ---
    private class TrackedPerson
    {
        public int Id;
        public OneEuroFilter[,] Filters;
        public float LastSeenTime;
        public Rect LastBox;

        public TrackedPerson(int id, float minCut, float beta, float dCut)
        {
            Id = id;
            Filters = new OneEuroFilter[17, 2];
            for (int i = 0; i < 17; i++)
            {
                Filters[i, 0] = new OneEuroFilter(minCut, beta, dCut);
                Filters[i, 1] = new OneEuroFilter(minCut, beta, dCut);
            }
        }
    }

    // --- 成员变量 ---
    private readonly Vector2Int _modelSize;
    private readonly List<DetectionCandidate> _candidates = new List<DetectionCandidate>(8400);
    private readonly List<int> _keptIndices = new List<int>(100);
    private bool[] _suppressed = new bool[8400];
    private List<TrackedPerson> _trackedPeople = new List<TrackedPerson>();
    private List<HumanPose> _humanPosesResult = new List<HumanPose>();
    private int _nextId = 0;

    // --- 参数 ---
    private readonly float _iouMatchThreshold = 0.35f;
    private readonly float _forgetTime = 0.5f; // 缩短忘记时间，反应更灵敏
    private bool _enableSmoothing;
    private int _maxPeople;
    private float _minCut, _beta, _dCut;

    public YoloV8PoseDecoder(Vector2Int modelSize, int maxPeople, bool smoothing, float minCut, float beta, float dCut)
    {
        _modelSize = modelSize;
        _maxPeople = maxPeople;
        _enableSmoothing = smoothing;
        _minCut = minCut;
        _beta = beta;
        _dCut = dCut;
    }

    public List<HumanPose> Decode(Tensor<float> t, float confThreshold, float keyThreshold, float nmsThreshold, Vector2 webcamSize)
    {
        _humanPosesResult.Clear();
        
        // 1. 准备 Aspect Ratio 修正参数 (逆向 Letterbox)
        // Sentis/TextureConverter 默认使用 AspectFit (保持比例缩放，不足处补黑边)
        float srcAspect = webcamSize.x / webcamSize.y;
        float modelAspect = (float)_modelSize.x / _modelSize.y;

        float scaleX = 1f, scaleY = 1f;
        float offsetX = 0f, offsetY = 0f;

        if (srcAspect > modelAspect)
        {
            // 图片更宽 (如 16:9 进 1:1) -> 上下有黑边
            // 宽度占满，高度被缩放
            scaleY = modelAspect / srcAspect; 
            offsetY = (1f - scaleY) * 0.5f;
        }
        else
        {
            // 图片更高 (如 9:16 进 1:1) -> 左右有黑边
            scaleX = srcAspect / modelAspect;
            offsetX = (1f - scaleX) * 0.5f;
        }

        // 2. 解析 Tensor
        // YOLOv8 output: [1, 56, 8400] -> [Batch, Channels, Anchors]
        // 0-3: Box (cx, cy, w, h)
        // 4: Confidence
        // 5-55: Keypoints (x, y, conf) * 17
        int numAnchors = t.shape[2]; 

        _candidates.Clear();

        for (int i = 0; i < numAnchors; i++)
        {
            float conf = t[0, 4, i];
            if (conf < confThreshold) continue;

            float cx = t[0, 0, i];
            float cy = t[0, 1, i];
            float w = t[0, 2, i];
            float h = t[0, 3, i];

            _candidates.Add(new DetectionCandidate
            {
                index = i,
                confidence = conf,
                x = cx - 0.5f * w,
                y = cy - 0.5f * h,
                width = w,
                height = h
            });
        }

        // 按置信度排序 (用于 NMS)
        _candidates.Sort((a, b) => b.confidence.CompareTo(a.confidence));

        // 3. NMS (非极大值抑制)
        _keptIndices.Clear();
        if (_suppressed.Length < _candidates.Count) _suppressed = new bool[_candidates.Count + 128];
        System.Array.Clear(_suppressed, 0, _candidates.Count);

        for (int i = 0; i < _candidates.Count && _keptIndices.Count < _maxPeople; i++)
        {
            if (_suppressed[i]) continue;
            _keptIndices.Add(i);

            for (int j = i + 1; j < _candidates.Count; j++)
            {
                if (!_suppressed[j] && CalculateIoU(_candidates[i], _candidates[j]) > nmsThreshold)
                    _suppressed[j] = true;
            }
        }

        // 4. 生成结果 & 追踪平滑
        float currentTime = Time.time;

        for (int r = 0; r < _keptIndices.Count; r++)
        {
            var cand = _candidates[_keptIndices[r]];

            // --- 坐标逆向映射 (Box) ---
            // 1. 归一化 (除以模型尺寸)
            // 2. 移除黑边 (减去 Offset)
            // 3. 放大回有效区域 (除以 Scale)
            
            float normX = cand.x / _modelSize.x;
            float normY = cand.y / _modelSize.y;
            float normW = cand.width / _modelSize.x;
            float normH = cand.height / _modelSize.y;

            Rect currentBox = new Rect(
                (normX - offsetX) / scaleX,
                (normY - offsetY) / scaleY,
                normW / scaleX,
                normH / scaleY
            );

            // 追踪匹配
            int assignedId = r;
            TrackedPerson targetTracker = MatchTracker(currentBox, currentTime);
            if (targetTracker != null) assignedId = targetTracker.Id;

            HumanPose pose = new HumanPose { index = assignedId, box = currentBox, bodyParts = new BodyPart[17] };

            // --- 坐标逆向映射 (Keypoints) ---
            for (int k = 0; k < 17; k++)
            {
                int baseCh = 5 + k * 3;
                float rawX = t[0, baseCh, cand.index];
                float rawY = t[0, baseCh + 1, cand.index];
                float ks = t[0, baseCh + 2, cand.index];

                // 同样进行逆向 Letterbox 计算
                float kx_norm = rawX / _modelSize.x;
                float ky_norm = rawY / _modelSize.y;

                float finalX = (kx_norm - offsetX) / scaleX;
                float finalY = (ky_norm - offsetY) / scaleY;

                BodyPart part = new BodyPart { index = k, score = ks, hasValue = ks >= keyThreshold };

                if (_enableSmoothing && targetTracker != null)
                {
                    // 仅对有效点进行平滑
                    if (part.hasValue)
                    {
                        part.x = targetTracker.Filters[k, 0].Filter(finalX, currentTime);
                        part.y = targetTracker.Filters[k, 1].Filter(finalY, currentTime);
                    }
                    else
                    {
                        // 如果点无效，重置滤波器状态或直接赋值，防止飞尸
                        part.x = finalX; 
                        part.y = finalY;
                    }
                }
                else
                {
                    part.x = finalX;
                    part.y = finalY;
                }

                // 钳制在 0-1 之间，防止轻微溢出
                part.x = Mathf.Clamp01(part.x);
                part.y = Mathf.Clamp01(part.y);

                pose.bodyParts[k] = part;
            }

            _humanPosesResult.Add(pose);
        }

        // 清理过期追踪器
        if (_enableSmoothing) _trackedPeople.RemoveAll(p => currentTime - p.LastSeenTime > _forgetTime);
        else _trackedPeople.Clear();

        return _humanPosesResult;
    }

    // --- 辅助方法 ---
    private TrackedPerson MatchTracker(Rect box, float time)
    {
        if (!_enableSmoothing) return null;
        float bestIou = _iouMatchThreshold;
        TrackedPerson target = null;
        foreach (var p in _trackedPeople)
        {
            float iou = CalculateRectIoU(box, p.LastBox);
            if (iou > bestIou)
            {
                bestIou = iou;
                target = p;
            }
        }

        if (target == null && _trackedPeople.Count < _maxPeople)
        {
            target = new TrackedPerson(_nextId++, _minCut, _beta, _dCut);
            _trackedPeople.Add(target);
        }

        if (target != null)
        {
            target.LastSeenTime = time;
            target.LastBox = box;
        }

        return target;
    }

    private float CalculateRectIoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);
        float intersection = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float union = (a.width * a.height) + (b.width * b.height) - intersection;
        return intersection / (union + 1e-6f);
    }

    private float CalculateIoU(DetectionCandidate a, DetectionCandidate b)
    {
        float x1 = Mathf.Max(a.x, b.x), y1 = Mathf.Max(a.y, b.y);
        float x2 = Mathf.Min(a.x + a.width, b.x + b.width), y2 = Mathf.Min(a.y + a.height, b.y + b.height);
        float intArea = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        return intArea / (a.width * a.height + b.width * b.height - intArea + 1e-6f);
    }
}