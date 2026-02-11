using System.Collections.Generic;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using UnityEngine;

public class HumanPoseVisualizer : MonoBehaviour
{
    // --- 预制体 ---
    [Header("Prefabs")] [Tooltip("必须包含名为 JointContainer 和 BoneContainer 的子物体，或者代码里会自动创建")] [SerializeField]
    private RectTransform poseContainerPrefab;

    [SerializeField] private Image jointPrefab;
    [SerializeField] private RectTransform bonePrefab;
    [SerializeField] private Image boxPrefab;
    [SerializeField] private Image numberPrefab;

    // --- 配置 ---
    [Header("Configuration")] [SerializeField]
    private TextAsset bodyPartConnectionsFile;

    [Range(0, 1)] public float alpha = 1.0f;

    [Header("Toggles")] public bool isHead = true;
    public bool isBox = true;
    public bool isBone = true;
    public bool isNumber = true;

    // --- 数据结构 ---
    [System.Serializable]
    class BodyPartConnection
    {
        public int from;
        public int to;
        public int r, g, b;
    }

    [System.Serializable]
    class BodyPartConnectionList
    {
        public List<BodyPartConnection> bodyPartConnections;
    }

    // --- 运行时状态 ---
    private List<BodyPartConnection> bodyPartConnections;

    // 对象池列表
    private List<RectTransform> poseContainers = new List<RectTransform>();
    private List<Image> boxs = new List<Image>();
    private List<Image> numbers = new List<Image>();
    private List<List<Image>> joints = new List<List<Image>>();
    private List<List<RectTransform>> bones = new List<List<RectTransform>>();

    private float confidenceThreshold;
    private Dictionary<RectTransform, Coroutine> coroutines = new Dictionary<RectTransform, Coroutine>();

    private void Start()
    {
        LoadBodyPartConnectionList();
    }

    private void LoadBodyPartConnectionList()
    {
        if (bodyPartConnectionsFile != null)
        {
            try
            {
                bodyPartConnections = JsonUtility.FromJson<BodyPartConnectionList>(bodyPartConnectionsFile.text).bodyPartConnections;
            }
            catch
            {
                Debug.LogError("BodyPartConnection json 解析失败，请检查文件格式。");
                bodyPartConnections = new List<BodyPartConnection>();
            }
        }
        else
        {
            Debug.LogWarning("未分配 BodyPartConnectionsFile (Json)，将不显示连线。");
            bodyPartConnections = new List<BodyPartConnection>();
        }
    }

    // ==============================================================================================
    // 更新主逻辑
    // ==============================================================================================

    public void UpdatePoseVisualizations(List<HumanPose> humanPoses, float confidenceThreshold = 0.2f)
    {
        // 1. 安全检查
        if (PoseManager.Instance.cameraView == null) return;

        // 2. 检查全局开关 (来自 Config)
        bool showBone = PoseManager.Instance.poseLocalConfig.isActiveBone;
        if (!showBone)
        {
            HideAllContainers();
            return;
        }

        this.confidenceThreshold = confidenceThreshold;

        // 【关键】获取 Webcam 显示区域作为参考矩形
        // 所有的骨骼点都将映射到这个矩形内部，从而自动适配黑边和缩放
        RectTransform referenceRect = PoseManager.Instance.cameraView.rectTransform;

        // 3. 对象池扩容 (不够就生成)
        while (poseContainers.Count < humanPoses.Count)
        {
            CreateNewPoseContainer(referenceRect);
        }

        // 4. 更新每个人的可视化
        for (int i = 0; i < poseContainers.Count; i++)
        {
            if (i < humanPoses.Count)
            {
                var pose = humanPoses[i];
                var container = poseContainers[i];

                // 确保容器激活
                container.gameObject.SetActive(true);

                // 如果之前有延迟隐藏的协程，取消它
                if (coroutines.TryGetValue(container, out var value))
                {
                    StopCoroutine(value);
                    coroutines.Remove(container);
                }

                // --- 绘制包围盒 ---
                if (isBox) UpdateBox(pose, boxs[i], referenceRect);
                boxs[i].gameObject.SetActive(isBox);

                // --- 绘制 ID 编号 ---
                if (isNumber) UpdateNumber(pose, numbers[i], referenceRect);
                numbers[i].gameObject.SetActive(isNumber);

                // --- 绘制骨骼 ---
                if (isBone)
                {
                    // 查找或使用默认容器
                    Transform jointContainerTrans = container.Find("JointContainer");
                    Transform boneContainerTrans = container.Find("BoneContainer");

                    RectTransform jointContainer = jointContainerTrans ? (RectTransform)jointContainerTrans : container;
                    RectTransform boneContainer = boneContainerTrans ? (RectTransform)boneContainerTrans : container;

                    UpdateJoints(pose.bodyParts, jointContainer, joints[i], referenceRect);
                    UpdateBones(pose.bodyParts, boneContainer, joints[i], bones[i], referenceRect);
                }
                else
                {
                    HideList(joints[i]);
                    HideList(bones[i]);
                }
            }
            // 5. 隐藏多余的容器 (没人了)
            else
            {
                if (poseContainers[i].gameObject.activeSelf && !coroutines.ContainsKey(poseContainers[i]))
                {
                    coroutines.Add(poseContainers[i], StartCoroutine(DelayHide(poseContainers[i])));
                }
            }
        }
    }

    private void CreateNewPoseContainer(RectTransform parent)
    {
        // 将容器挂在 referenceRect (Webcam RawImage) 下面，确保坐标系一致
        RectTransform newPoseContainer = Instantiate(poseContainerPrefab, parent);

        // 填满父物体
        newPoseContainer.anchorMin = Vector2.zero;
        newPoseContainer.anchorMax = Vector2.one;
        newPoseContainer.offsetMin = Vector2.zero;
        newPoseContainer.offsetMax = Vector2.zero;
        newPoseContainer.localScale = Vector3.one;

        poseContainers.Add(newPoseContainer);
        joints.Add(new List<Image>());
        bones.Add(new List<RectTransform>());
        boxs.Add(Instantiate(boxPrefab, newPoseContainer));
        numbers.Add(Instantiate(numberPrefab, newPoseContainer));
    }

    private IEnumerator DelayHide(RectTransform rectTransform)
    {
        yield return new WaitForSeconds(0.2f);
        if (rectTransform != null)
        {
            rectTransform.gameObject.SetActive(false);
            coroutines.Remove(rectTransform);
        }
    }

    private void HideAllContainers()
    {
        foreach (var container in poseContainers)
        {
            if (container != null) container.gameObject.SetActive(false);
        }
    }

    private void HideList<T>(List<T> list) where T : Component
    {
        foreach (var item in list) item.gameObject.SetActive(false);
    }

    // ==============================================================================================
    // 子组件更新方法
    // ==============================================================================================

    private void UpdateBox(HumanPose pose, Image box, RectTransform referenceRect)
    {
        // 1. 计算中心位置
        // 使用临时 BodyPart 利用 ToAnchoredPos 的插值逻辑
        float cx = pose.box.x + pose.box.width * 0.5f;
        float cy = pose.box.y + pose.box.height * 0.5f;
        BodyPart centerPart = new BodyPart { x = cx, y = cy };

        box.rectTransform.anchoredPosition = centerPart.ToAnchoredPos(referenceRect);

        // 2. 计算大小 (SizeDelta)
        // 技巧：计算左上角和右下角在 UI 上的真实 anchoredPosition
        // 这样计算出的宽高是绝对准确的，不受分辨率、AspectFitter 或黑边的影响
        BodyPart topLeft = new BodyPart { x = pose.box.x, y = pose.box.y };
        BodyPart bottomRight = new BodyPart { x = pose.box.x + pose.box.width, y = pose.box.y + pose.box.height };

        Vector2 p1 = topLeft.ToAnchoredPos(referenceRect);
        Vector2 p2 = bottomRight.ToAnchoredPos(referenceRect);

        // p1 和 p2 的差值就是真实的 UI 像素宽高
        box.rectTransform.sizeDelta = new Vector2(Mathf.Abs(p2.x - p1.x), Mathf.Abs(p2.y - p1.y));
    }

    private void UpdateNumber(HumanPose pose, Image number, RectTransform referenceRect)
    {
        float tx, ty;
        // 降级方案：放在 Box 顶部中间
        tx = pose.box.x + pose.box.width * 0.5f;
        ty = pose.box.y + pose.box.height * 0.5f;;


        BodyPart temp = new BodyPart { x = tx, y = ty };
        number.rectTransform.anchoredPosition = temp.ToAnchoredPos(referenceRect);

        var textComp = number.transform.GetComponentInChildren<TextMeshProUGUI>();
        // ID + 1 显得更人性化
        if (textComp) textComp.text = (pose.index + 1).ToString();
    }

    private void UpdateJoints(BodyPart[] bodyParts, RectTransform container, List<Image> jointsList, RectTransform referenceRect)
    {
        while (jointsList.Count < bodyParts.Length)
            jointsList.Add(Instantiate(jointPrefab, container));

        for (int i = 0; i < jointsList.Count; i++)
        {
            // 如果不显示头部 (0-4 是鼻子眼睛耳朵)
            if (!isHead && i <= 4)
            {
                jointsList[i].gameObject.SetActive(false);
                continue;
            }

            if (bodyParts[i].hasValue && bodyParts[i].score >= confidenceThreshold)
            {
                Image joint = jointsList[i];

                // ✅ 核心调用：直接映射到 UI 坐标
                joint.rectTransform.anchoredPosition = bodyParts[i].ToAnchoredPos(referenceRect);

                // 根据是否有连线配置改变颜色 (如果有配置表则红色，没有则绿色)
                if (bodyPartConnections != null && bodyPartConnections.Count > 0)
                    joint.color = new Color(1f, 0f, 0f, alpha);
                else
                    joint.color = new Color(0f, 1f, 0f, alpha);

                joint.gameObject.SetActive(true);
            }
            else
            {
                jointsList[i].gameObject.SetActive(false);
            }
        }
    }

    private void UpdateBones(BodyPart[] bodyParts, RectTransform container, List<Image> jointsList, List<RectTransform> bonesList, RectTransform referenceRect)
    {
        if (bodyPartConnections == null) return;

        while (bonesList.Count < bodyPartConnections.Count)
            bonesList.Add(Instantiate(bonePrefab, container));

        for (int i = 0; i < bonesList.Count; i++)
        {
            var conn = bodyPartConnections[i];

            // 过滤头部连线
            if (!isHead && (conn.from <= 4 || conn.to <= 4))
            {
                bonesList[i].gameObject.SetActive(false);
                continue;
            }

            // 只有当连线的两个端点都有效时才绘制线段
            if (bodyParts[conn.from].hasValue && bodyParts[conn.to].hasValue &&
                bodyParts[conn.from].score >= confidenceThreshold && bodyParts[conn.to].score >= confidenceThreshold)
            {
                RectTransform bone = bonesList[i];

                // ✅ 核心调用：计算两端 UI 坐标
                Vector2 fromPos = bodyParts[conn.from].ToAnchoredPos(referenceRect);
                Vector2 toPos = bodyParts[conn.to].ToAnchoredPos(referenceRect);

                Vector2 direction = toPos - fromPos;
                float distance = direction.magnitude;

                // 计算旋转角度 (Atan2 返回弧度)
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

                // 设置线段
                bone.sizeDelta = new Vector2(distance, bone.sizeDelta.y); // 长度设为距离，宽度保持不变
                bone.anchoredPosition = (fromPos + toPos) * 0.5f; // 位置设为中点
                bone.localEulerAngles = new Vector3(0, 0, angle);

                // 颜色与透明度
                Image boneImg = bone.GetComponent<Image>();
                if (boneImg != null)
                    boneImg.color = new Color(conn.r / 255.0f, conn.g / 255.0f, conn.b / 255.0f, alpha);

                bone.gameObject.SetActive(true);
            }
            else
            {
                bonesList[i].gameObject.SetActive(false);
            }
        }
    }
}