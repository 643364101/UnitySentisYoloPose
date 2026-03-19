using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

public class Yolo26PoseDecoder : IPoseDecoder
{
    // --- 追踪与平滑
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

    private readonly Vector2Int _modelSize;
    private readonly int _maxPeople;
    private readonly bool _enableSmoothing;
    private readonly float _minCut, _beta, _dCut;
    
    private List<TrackedPerson> _trackedPeople = new List<TrackedPerson>();
    private List<HumanPose> _humanPosesResult = new List<HumanPose>();
    private int _nextId = 0;
    private readonly float _iouMatchThreshold = 0.35f;
    private readonly float _forgetTime = 0.5f;

    public Yolo26PoseDecoder(Vector2Int modelSize, int maxPeople, bool smoothing, float minCut, float beta, float dCut)
    {
        _modelSize = modelSize;
        _maxPeople = maxPeople;
        _enableSmoothing = smoothing;
        _minCut = minCut;
        _beta = beta;
        _dCut = dCut;
    }

    public List<HumanPose> Decode(Tensor<float> t, float confThreshold, float keypointThreshold, float nmsThreshold, Vector2 webcamSize)
    {
        _humanPosesResult.Clear();
        float currentTime = Time.time;

        // 1. 获取 Tensor 维度 [1, 300, 57]
        int numPredictions = t.shape[1];
        int elementsPerPrediction = t.shape[2];

        // 2. 计算 Letterbox 逆向参数（与V8逻辑一致）
        float srcAspect = webcamSize.x / webcamSize.y;
        float modelAspect = (float)_modelSize.x / _modelSize.y;
        float scaleX = 1f, scaleY = 1f;
        float offsetX = 0f, offsetY = 0f;

        if (srcAspect > modelAspect) {
            scaleY = modelAspect / srcAspect;
            offsetY = (1f - scaleY) * 0.5f;
        } else {
            scaleX = srcAspect / modelAspect;
            offsetX = (1f - scaleX) * 0.5f;
        }

        // 3. 遍历预测结果 (YOLOv26 不需要 NMS)
        for (int i = 0; i < numPredictions; i++)
        {
            // 直接读取置信度 (Index 4)
            float score = t[0, i, 4];
            if (score < confThreshold) continue;

            // 4. 解析 Bounding Box (YOLOv26 通常输出 xmin, ymin, xmax, ymax)
            float xmin = t[0, i, 0] / _modelSize.x;
            float ymin = t[0, i, 1] / _modelSize.y;
            float xmax = t[0, i, 2] / _modelSize.x;
            float ymax = t[0, i, 3] / _modelSize.y;

            Rect currentBox = new Rect(
                (xmin - offsetX) / scaleX,
                (ymin - offsetY) / scaleY,
                (xmax - xmin) / scaleX,
                (ymax - ymin) / scaleY
            );

            // 5. 追踪匹配
            TrackedPerson targetTracker = MatchTracker(currentBox, currentTime);
            int assignedId = targetTracker?.Id ?? i;

            HumanPose pose = new HumanPose {
                index = assignedId,
                box = currentBox,
                bodyParts = new BodyPart[17]
            };

            // 6. 解析关键点 (从 Index 6 开始，每 3 个一组)
            for (int k = 0; k < 17; k++)
            {
                int baseK = 6 + (k * 3);
                float kx_norm = t[0, i, baseK] / _modelSize.x;
                float ky_norm = t[0, i, baseK + 1] / _modelSize.y;
                float ks = t[0, i, baseK + 2];

                float finalX = (kx_norm - offsetX) / scaleX;
                float finalY = (ky_norm - offsetY) / scaleY;

                BodyPart part = new BodyPart {
                    index = k,
                    score = ks,
                    hasValue = ks >= keypointThreshold
                };

                if (_enableSmoothing && targetTracker != null && part.hasValue)
                {
                    part.x = targetTracker.Filters[k, 0].Filter(finalX, currentTime);
                    part.y = targetTracker.Filters[k, 1].Filter(finalY, currentTime);
                }
                else
                {
                    part.x = finalX;
                    part.y = finalY;
                }

                part.x = Mathf.Clamp01(part.x);
                part.y = Mathf.Clamp01(part.y);
                pose.bodyParts[k] = part;
            }

            _humanPosesResult.Add(pose);
            if (_humanPosesResult.Count >= _maxPeople) break;
        }

        // 7. 清理过期追踪器
        if (_enableSmoothing) _trackedPeople.RemoveAll(p => currentTime - p.LastSeenTime > _forgetTime);
        return _humanPosesResult;
    }

    // 复用匹配逻辑
    private TrackedPerson MatchTracker(Rect box, float time)
    {
        if (!_enableSmoothing) return null;
        float bestIou = _iouMatchThreshold;
        TrackedPerson target = null;
        foreach (var p in _trackedPeople)
        {
            float iou = CalculateRectIoU(box, p.LastBox);
            if (iou > bestIou) { bestIou = iou; target = p; }
        }

        if (target == null && _trackedPeople.Count < _maxPeople)
        {
            target = new TrackedPerson(_nextId++, _minCut, _beta, _dCut);
            _trackedPeople.Add(target);
        }

        if (target != null) { target.LastSeenTime = time; target.LastBox = box; }
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
}