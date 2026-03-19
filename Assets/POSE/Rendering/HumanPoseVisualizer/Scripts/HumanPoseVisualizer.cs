using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 人体姿态可视化组件。
/// 
/// 输入姿态默认应为 Display Space。
/// 所有 UI 映射统一基于 PoseManager.cameraView.rectTransform，
/// 因此在横屏 / 竖屏 / 黑边 / 缩放适配下更稳定。
/// </summary>
public class HumanPoseVisualizer : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("姿态容器预制体。建议包含名为 JointContainer 和 BoneContainer 的子物体；若没有，则直接使用容器本身作为挂载节点。")]
    [SerializeField] private RectTransform poseContainerPrefab;
    [SerializeField] private Image jointPrefab;
    [SerializeField] private RectTransform bonePrefab;
    [SerializeField] private Image boxPrefab;
    [SerializeField] private Image numberPrefab;

    [Header("Configuration")]
    [SerializeField] private TextAsset bodyPartConnectionsFile;

    [Range(0f, 1f)]
    public float alpha = 1.0f;

    [Header("Toggles")]
    public bool isHead = true;
    public bool isBox = true;
    public bool isBone = true;
    public bool isNumber = true;

    [System.Serializable]
    private class BodyPartConnection
    {
        public int from;
        public int to;
        public int r, g, b;
    }

    [System.Serializable]
    private class BodyPartConnectionList
    {
        public List<BodyPartConnection> bodyPartConnections;
    }

    private List<BodyPartConnection> bodyPartConnections;

    private readonly List<RectTransform> poseContainers = new List<RectTransform>();
    private readonly List<Image> boxs = new List<Image>();
    private readonly List<Image> numbers = new List<Image>();
    private readonly List<List<Image>> joints = new List<List<Image>>();
    private readonly List<List<RectTransform>> bones = new List<List<RectTransform>>();

    private float confidenceThreshold;
    private readonly Dictionary<RectTransform, Coroutine> coroutines = new Dictionary<RectTransform, Coroutine>();

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
                var list = JsonUtility.FromJson<BodyPartConnectionList>(bodyPartConnectionsFile.text);
                bodyPartConnections = list != null ? list.bodyPartConnections : new List<BodyPartConnection>();
            }
            catch
            {
                Debug.LogError("[HumanPoseVisualizer] BodyPartConnection json 解析失败，请检查文件格式。");
                bodyPartConnections = new List<BodyPartConnection>();
            }
        }
        else
        {
            Debug.LogWarning("[HumanPoseVisualizer] 未分配 BodyPartConnectionsFile，将不显示骨骼连线。");
            bodyPartConnections = new List<BodyPartConnection>();
        }
    }

    public void UpdatePoseVisualizations(List<HumanPose> humanPoses, float confidenceThreshold = 0.2f)
    {
        if (PoseManager.Instance == null || PoseManager.Instance.cameraView == null)
            return;

        if (humanPoses == null)
            humanPoses = new List<HumanPose>();

        bool showBone = PoseManager.Instance.PoseLocalConfig.isActiveBone;
        if (!showBone)
        {
            HideAllContainers();
            return;
        }

        if (poseContainerPrefab == null || jointPrefab == null || bonePrefab == null || boxPrefab == null || numberPrefab == null)
        {
            Debug.LogError("[HumanPoseVisualizer] 缺少必要预制体引用。");
            return;
        }

        this.confidenceThreshold = confidenceThreshold;
        RectTransform referenceRect = PoseManager.Instance.cameraView.rectTransform;

        while (poseContainers.Count < humanPoses.Count)
        {
            CreateNewPoseContainer(referenceRect);
        }

        for (int i = 0; i < poseContainers.Count; i++)
        {
            if (i < humanPoses.Count)
            {
                HumanPose pose = humanPoses[i];
                RectTransform container = poseContainers[i];
                container.gameObject.SetActive(true);

                if (coroutines.TryGetValue(container, out Coroutine co))
                {
                    StopCoroutine(co);
                    coroutines.Remove(container);
                }

                if (isBox)
                    UpdateBox(pose, boxs[i], referenceRect);
                boxs[i].gameObject.SetActive(isBox);

                if (isNumber)
                    UpdateNumber(pose, numbers[i], referenceRect);
                numbers[i].gameObject.SetActive(isNumber);

                if (isBone)
                {
                    Transform jointContainerTrans = container.Find("JointContainer");
                    Transform boneContainerTrans = container.Find("BoneContainer");

                    RectTransform jointContainer = jointContainerTrans ? (RectTransform)jointContainerTrans : container;
                    RectTransform boneContainer = boneContainerTrans ? (RectTransform)boneContainerTrans : container;

                    UpdateJoints(pose.bodyParts, jointContainer, joints[i], referenceRect);
                    UpdateBones(pose.bodyParts, boneContainer, bones[i], referenceRect);
                }
                else
                {
                    HideList(joints[i]);
                    HideList(bones[i]);
                }
            }
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
        RectTransform newPoseContainer = Instantiate(poseContainerPrefab, parent);

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
        for (int i = 0; i < poseContainers.Count; i++)
        {
            if (poseContainers[i] != null)
                poseContainers[i].gameObject.SetActive(false);
        }
    }

    private void HideList<T>(List<T> list) where T : Component
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
                list[i].gameObject.SetActive(false);
        }
    }

    private void UpdateBox(HumanPose pose, Image box, RectTransform referenceRect)
    {
        Rect uiRect = pose.BoxToUIRect(referenceRect);
        box.rectTransform.anchoredPosition = uiRect.center;
        box.rectTransform.sizeDelta = uiRect.size;
    }

    private void UpdateNumber(HumanPose pose, Image number, RectTransform referenceRect)
    {
        Rect uiRect = pose.BoxToUIRect(referenceRect);
        Vector2 numberPos = new Vector2(uiRect.center.x, uiRect.yMax);
        number.rectTransform.anchoredPosition = numberPos;

        TextMeshProUGUI textComp = number.transform.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null)
            textComp.text = (pose.index + 1).ToString();
    }

    private void UpdateJoints(BodyPart[] bodyParts, RectTransform container, List<Image> jointsList, RectTransform referenceRect)
    {
        if (bodyParts == null || bodyParts.Length == 0)
        {
            HideList(jointsList);
            return;
        }

        while (jointsList.Count < bodyParts.Length)
        {
            jointsList.Add(Instantiate(jointPrefab, container));
        }

        for (int i = 0; i < jointsList.Count; i++)
        {
            if (!isHead && i <= 4)
            {
                jointsList[i].gameObject.SetActive(false);
                continue;
            }

            if (i < bodyParts.Length && bodyParts[i].hasValue && bodyParts[i].score >= confidenceThreshold)
            {
                Image joint = jointsList[i];
                joint.rectTransform.anchoredPosition = bodyParts[i].ToAnchoredPos(referenceRect);

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

    private void UpdateBones(BodyPart[] bodyParts, RectTransform container, List<RectTransform> bonesList, RectTransform referenceRect)
    {
        if (bodyParts == null || bodyParts.Length == 0 || bodyPartConnections == null)
        {
            HideList(bonesList);
            return;
        }

        while (bonesList.Count < bodyPartConnections.Count)
        {
            bonesList.Add(Instantiate(bonePrefab, container));
        }

        for (int i = 0; i < bonesList.Count; i++)
        {
            BodyPartConnection conn = bodyPartConnections[i];

            if (!isHead && (conn.from <= 4 || conn.to <= 4))
            {
                bonesList[i].gameObject.SetActive(false);
                continue;
            }

            bool valid =
                conn.from < bodyParts.Length &&
                conn.to < bodyParts.Length &&
                bodyParts[conn.from].hasValue &&
                bodyParts[conn.to].hasValue &&
                bodyParts[conn.from].score >= confidenceThreshold &&
                bodyParts[conn.to].score >= confidenceThreshold;

            if (valid)
            {
                RectTransform bone = bonesList[i];

                Vector2 fromPos = bodyParts[conn.from].ToAnchoredPos(referenceRect);
                Vector2 toPos = bodyParts[conn.to].ToAnchoredPos(referenceRect);

                Vector2 direction = toPos - fromPos;
                float distance = direction.magnitude;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

                bone.sizeDelta = new Vector2(distance, bone.sizeDelta.y);
                bone.anchoredPosition = (fromPos + toPos) * 0.5f;
                bone.localEulerAngles = new Vector3(0f, 0f, angle);

                Image boneImg = bone.GetComponent<Image>();
                if (boneImg != null)
                {
                    boneImg.color = new Color(
                        conn.r / 255f,
                        conn.g / 255f,
                        conn.b / 255f,
                        alpha
                    );
                }

                bone.gameObject.SetActive(true);
            }
            else
            {
                bonesList[i].gameObject.SetActive(false);
            }
        }
    }
}