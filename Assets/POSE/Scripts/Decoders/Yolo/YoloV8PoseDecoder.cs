using UnityEngine;
using System.Collections.Generic;
using Unity.InferenceEngine;

/// <summary>
/// 解码阶段内部使用的候选框结构。
/// 
/// 注意：
/// 这里的 x / y / width / height 仍处于模型输入尺度下，
/// 还未完成逆 Letterbox 到原始归一化空间。
/// </summary>
struct DetectionCandidate
{
    public int index;
    public float confidence;
    public float x, y, width, height;
}

/// <summary>
/// YOLOv8 Pose 解码器。
/// 
/// 主要职责：
/// 1. 解析模型输出 Tensor
/// 2. 按置信度筛选人体候选
/// 3. 做 NMS 去重
/// 4. 将模型输入空间坐标逆映射回原始归一化空间（逆 Letterbox）
/// 5. 为每个人分配追踪 ID
/// 6. 对关键点做 OneEuro 平滑
/// 
/// 【重要坐标约定】
/// 本解码器输出的是 Source Space（原始纹理空间）：
/// - x：左 -> 右
/// - y：上 -> 下
/// - 原点在左上
/// 
/// 也就是说：
/// - 这里输出的 HumanPose 还不是最终业务层使用的 Display Space
/// - 若项目开启镜像显示，镜像转换应在 PosePostProcessor 中统一完成
/// </summary>
public class YoloV8PoseDecoder : IPoseDecoder
{
    /// <summary>
    /// 单个追踪目标。
    /// 为每个关键点的 x / y 维护一个滤波器。
    /// </summary>
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
                // [i,0] -> x 滤波器
                // [i,1] -> y 滤波器
                Filters[i, 0] = new OneEuroFilter(minCut, beta, dCut);
                Filters[i, 1] = new OneEuroFilter(minCut, beta, dCut);
            }
        }
    }

    // --- 基础配置 ---
    private readonly Vector2Int _modelSize;
    private readonly float _iouMatchThreshold = 0.35f;
    private readonly float _forgetTime = 0.5f;

    // --- 解码工作缓存 ---
    private readonly List<DetectionCandidate> _candidates = new List<DetectionCandidate>(8400);
    private readonly List<int> _keptIndices = new List<int>(100);
    private bool[] _suppressed = new bool[8400];
    private readonly List<HumanPose> _humanPosesResult = new List<HumanPose>();

    // --- 追踪缓存 ---
    private readonly List<TrackedPerson> _trackedPeople = new List<TrackedPerson>();
    private int _nextId = 0;

    // --- 平滑参数 ---
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

    /// <summary>
    /// 解码模型输出。
    /// 
    /// 输入：
    /// - t：模型输出 Tensor
    /// - confThreshold：人体检测阈值
    /// - keyThreshold：关键点有效阈值
    /// - nmsThreshold：NMS 阈值
    /// - webcamSize：原始输入图像尺寸（用于逆向 Letterbox）
    /// 
    /// 输出：
    /// - HumanPose 列表（Source Space）
    /// </summary>
    public List<HumanPose> Decode(Tensor<float> t, float confThreshold, float keyThreshold, float nmsThreshold, Vector2 webcamSize)
    {
        _humanPosesResult.Clear();

        // --------------------------------------------------------------------
        // 1. 计算逆 Letterbox 参数
        // --------------------------------------------------------------------
        // 模型输入通常是固定尺寸（例如 640x640），但摄像头画面可能是 16:9 / 9:16。
        // 当原图按 AspectFit 缩放到模型输入时，四周会产生黑边。
        //
        // 为了把模型输出点还原回“原始图像归一化坐标”，需要知道：
        // - 有效画面在模型输入中的缩放比例 scaleX / scaleY
        // - 有效画面在模型输入中的偏移 offsetX / offsetY
        float srcAspect = webcamSize.x / webcamSize.y;
        float modelAspect = (float)_modelSize.x / _modelSize.y;

        float scaleX = 1f, scaleY = 1f;
        float offsetX = 0f, offsetY = 0f;

        if (srcAspect > modelAspect)
        {
            // 原图更宽：上下补黑边
            scaleY = modelAspect / srcAspect;
            offsetY = (1f - scaleY) * 0.5f;
        }
        else
        {
            // 原图更高：左右补黑边
            scaleX = srcAspect / modelAspect;
            offsetX = (1f - scaleX) * 0.5f;
        }

        // --------------------------------------------------------------------
        // 2. 解析候选框
        // --------------------------------------------------------------------
        // YOLOv8 Pose 输出格式：
        // [1, 56, 8400]
        // 0~3   : box(cx, cy, w, h)
        // 4     : conf
        // 5~55  : keypoints (x, y, conf) * 17
        int numAnchors = t.shape[2];
        _candidates.Clear();

        for (int i = 0; i < numAnchors; i++)
        {
            float conf = t[0, 4, i];
            if (conf < confThreshold)
                continue;

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

        // 按置信度从高到低排序，便于后续 NMS
        _candidates.Sort((a, b) => b.confidence.CompareTo(a.confidence));

        // --------------------------------------------------------------------
        // 3. NMS 去重
        // --------------------------------------------------------------------
        _keptIndices.Clear();

        if (_suppressed.Length < _candidates.Count)
            _suppressed = new bool[_candidates.Count + 128];

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

        // --------------------------------------------------------------------
        // 4. 生成最终姿态 + 追踪 + 平滑
        // --------------------------------------------------------------------
        float currentTime = Time.time;

        for (int r = 0; r < _keptIndices.Count; r++)
        {
            DetectionCandidate cand = _candidates[_keptIndices[r]];

            // 4.1 先将 box 从模型输入空间还原到原始归一化空间（Source Space）
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

            // 4.2 追踪匹配
            int assignedId = r;
            TrackedPerson targetTracker = MatchTracker(currentBox, currentTime);

            if (targetTracker != null)
                assignedId = targetTracker.Id;

            HumanPose pose = new HumanPose
            {
                index = assignedId,
                box = currentBox,
                bodyParts = new BodyPart[17]
            };

            // 4.3 解码关键点
            for (int k = 0; k < 17; k++)
            {
                int baseCh = 5 + k * 3;

                float rawX = t[0, baseCh, cand.index];
                float rawY = t[0, baseCh + 1, cand.index];
                float ks = t[0, baseCh + 2, cand.index];

                // 先归一化到模型输入 0~1
                float kxNorm = rawX / _modelSize.x;
                float kyNorm = rawY / _modelSize.y;

                // 再逆 Letterbox 回原始归一化空间（Source Space）
                float finalX = (kxNorm - offsetX) / scaleX;
                float finalY = (kyNorm - offsetY) / scaleY;

                BodyPart part = new BodyPart
                {
                    index = k,
                    score = ks,
                    hasValue = ks >= keyThreshold
                };

                if (_enableSmoothing && targetTracker != null)
                {
                    // 仅对有效点做平滑，避免无效点污染滤波器状态
                    if (part.hasValue)
                    {
                        part.x = targetTracker.Filters[k, 0].Filter(finalX, currentTime);
                        part.y = targetTracker.Filters[k, 1].Filter(finalY, currentTime);
                    }
                    else
                    {
                        part.x = finalX;
                        part.y = finalY;
                    }
                }
                else
                {
                    part.x = finalX;
                    part.y = finalY;
                }

                // 防止轻微溢出
                part.x = Mathf.Clamp01(part.x);
                part.y = Mathf.Clamp01(part.y);

                pose.bodyParts[k] = part;
            }

            _humanPosesResult.Add(pose);
        }

        // --------------------------------------------------------------------
        // 5. 清理长时间未出现的追踪目标
        // --------------------------------------------------------------------
        if (_enableSmoothing)
            _trackedPeople.RemoveAll(p => currentTime - p.LastSeenTime > _forgetTime);
        else
            _trackedPeople.Clear();

        return _humanPosesResult;
    }

    /// <summary>
    /// 根据 IoU 在历史追踪目标中寻找最匹配的人。
    /// 若找不到且名额允许，则创建新的追踪目标。
    /// </summary>
    private TrackedPerson MatchTracker(Rect box, float time)
    {
        if (!_enableSmoothing)
            return null;

        float bestIou = _iouMatchThreshold;
        TrackedPerson target = null;

        for (int i = 0; i < _trackedPeople.Count; i++)
        {
            TrackedPerson p = _trackedPeople[i];
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

    /// <summary>
    /// 计算两个 Rect 的 IoU。
    /// </summary>
    private float CalculateRectIoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);

        float intersection = Mathf.Max(0f, x2 - x1) * Mathf.Max(0f, y2 - y1);
        float union = (a.width * a.height) + (b.width * b.height) - intersection;

        return intersection / (union + 1e-6f);
    }

    /// <summary>
    /// 计算两个候选框的 IoU。
    /// </summary>
    private float CalculateIoU(DetectionCandidate a, DetectionCandidate b)
    {
        float x1 = Mathf.Max(a.x, b.x);
        float y1 = Mathf.Max(a.y, b.y);
        float x2 = Mathf.Min(a.x + a.width, b.x + b.width);
        float y2 = Mathf.Min(a.y + a.height, b.y + b.height);

        float intArea = Mathf.Max(0f, x2 - x1) * Mathf.Max(0f, y2 - y1);
        return intArea / (a.width * a.height + b.width * b.height - intArea + 1e-6f);
    }
}