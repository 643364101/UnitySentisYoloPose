using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class Test : MonoBehaviour
{
    [Header("Gesture Buttons (手势触发)")] public ARButton left;
    public ARButton leftUp;
    public ARButton right;
    public ARButton rightUp;


    [Header("Touch Buttons (触碰触发 - 新增)")] public ARButton touchBtn_Default; // 普通UI触碰
    public ARButton touchBtn_3D; // 3D UI触碰
    public Collider touchCollider_Cube; // 3D 物体触碰
    public Camera uiCamera; // 用于 WorldSpace UI 射线检测

    [Header("2D UI Tracking")] public RectTransform leftHandUI;
    public RectTransform rightHandUI;

    [Header("3D World Tracking")] public Transform leftHand3D;
    public Transform rightHand3D;

    [Header("Debug")] public TextMeshProUGUI txtHint;

    private void Start()
    {
        // 自动获取相机 (防空)
        if (uiCamera == null) uiCamera = Camera.main;

        if (left) left.onClick.AddListener(() => ShowHint("Gesture: Left Middle"));
        if (leftUp) leftUp.onClick.AddListener(() => ShowHint("Gesture: Left Up"));
        if (right) right.onClick.AddListener(() => ShowHint("Gesture: Right Middle"));
        if (rightUp) rightUp.onClick.AddListener(() => ShowHint("Gesture: Right Up"));

        if (touchBtn_Default) touchBtn_Default.onClick.AddListener(() => ShowHint("Touched: Default UI"));
        if (touchBtn_3D) touchBtn_3D.onClick.AddListener(() => ShowHint("Touched: 3D UI"));
    }

    private void ShowHint(string msg)
    {
        if (txtHint) txtHint.text = msg;
        Debug.Log(msg);
    }

    void OnEnable()
    {
        if (PoseManager.Instance == null) return;
        PoseManager.Instance.OnPoseUpdated += OnPoseUpdated;
        PoseManager.Instance.OnFilteringPoseUpdated += OnFilteringPoseUpdated;
        PoseManager.Instance.OnLimbsUpdated += OnLimbsUpdated;
    }

    void OnDisable()
    {
        if (PoseManager.Instance == null) return;
        PoseManager.Instance.OnPoseUpdated -= OnPoseUpdated;
        PoseManager.Instance.OnFilteringPoseUpdated -= OnFilteringPoseUpdated;
        PoseManager.Instance.OnLimbsUpdated -= OnLimbsUpdated;
    }

    // --- 1. 手势逻辑 (完全保留) ---
    private void OnLimbsUpdated(HandWaveDetector.GestureType type)
    {
        UpdateBtnState(left, type == HandWaveDetector.GestureType.LeftMiddle);
        UpdateBtnState(leftUp, type == HandWaveDetector.GestureType.LeftUp);
        UpdateBtnState(right, type == HandWaveDetector.GestureType.RightMiddle);
        UpdateBtnState(rightUp, type == HandWaveDetector.GestureType.RightUp);
    }

    // --- 2. 2D UI 跟随 & 触碰检测 (修改处) ---
    private void OnFilteringPoseUpdated(HumanPoseArea humanPoseArea)
    {
        // 安全检查
        if (humanPoseArea.humanPoses.Count == 0 || humanPoseArea.id != 0 || PoseManager.Instance.cameraView == null)
        {
            if (leftHandUI) leftHandUI.gameObject.SetActive(false);
            if (rightHandUI) rightHandUI.gameObject.SetActive(false);

            // 没人时重置触碰按钮
            ResetTouchButtons();
            return;
        }

        // 获取参考系
        RectTransform referenceRect = PoseManager.Instance.cameraView.rectTransform;
        HumanPose pose = humanPoseArea.humanPoses[0];

        var leftWrist = pose.GetBodyParts(BodyPartsType.LeftWrist);
        var rightWrist = pose.GetBodyParts(BodyPartsType.RightWrist);

        UpdateHandUI(leftHandUI, leftWrist, referenceRect);
        UpdateHandUI(rightHandUI, rightWrist, referenceRect);
    }

    // --- 新增方法：检测所有触碰目标 ---
    private void CheckAirTouch(BodyPart leftHand, BodyPart rightHand)
    {
        // 1. 普通 2D UI (IsInside)
        if (touchBtn_Default != null)
        {
            RectTransform target = touchBtn_Default.GetComponent<RectTransform>();
            bool isTouched = leftHand.IsInside(target) || rightHand.IsInside(target);
            UpdateBtnState(touchBtn_Default, isTouched);
        }

        // 2. 3D UI / World Space UI (IsInside3D)
        if (touchBtn_3D != null)
        {
            RectTransform target = touchBtn_3D.GetComponent<RectTransform>();
            // 需要指定渲染 UI 的相机
            bool isTouched = leftHand.IsInside3D(target, uiCamera) || rightHand.IsInside3D(target, uiCamera);
            UpdateBtnState(touchBtn_3D, isTouched);
        }

        // 3. 3D 物体 / Collider (IsTouching)
        if (touchCollider_Cube != null)
        {
            // 使用主相机射线检测
            bool isHit = leftHand.IsTouching(touchCollider_Cube) || rightHand.IsTouching(touchCollider_Cube);

            // 简单的变色反馈
            var renderer = touchCollider_Cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = isHit ? Color.green : Color.white;
            }

            if (isHit) ShowHint("Touched: 3D Cube");
        }
    }

    // --- 3. 3D 物体跟随 (完全保留) ---
    private void OnPoseUpdated(List<HumanPose> poses)
    {
        if (poses == null || poses.Count == 0)
        {
            if (leftHand3D) leftHand3D.gameObject.SetActive(false);
            if (rightHand3D) rightHand3D.gameObject.SetActive(false);
            return;
        }

        HumanPose pose = poses[0];
        var leftWrist = pose.GetBodyParts(BodyPartsType.LeftWrist);
        var rightWrist = pose.GetBodyParts(BodyPartsType.RightWrist);

        if (leftHand3D != null)
        {
            leftHand3D.gameObject.SetActive(leftWrist.hasValue && leftWrist.score > 0.3f);
            if (leftWrist.hasValue) leftHand3D.position = leftWrist.ToWorldPos();
        }

        if (rightHand3D != null)
        {
            rightHand3D.gameObject.SetActive(rightWrist.hasValue && rightWrist.score > 0.3f);
            if (rightWrist.hasValue) rightHand3D.position = rightWrist.ToWorldPos();
        }

        // --- 执行触碰检测 (新增逻辑) ---
        CheckAirTouch(leftWrist, rightWrist);
    }

    // --- 辅助方法 ---
    private void UpdateBtnState(ARButton btn, bool isActive)
    {
        if (btn == null) return;
        if (isActive) btn.SetProgress();
        else btn.BreakOffProgress();
    }

    private void UpdateHandUI(RectTransform ui, BodyPart part, RectTransform refer)
    {
        if (ui == null) return;
        if (part.hasValue && part.score > 0.3f)
        {
            ui.gameObject.SetActive(true);
            ui.anchoredPosition = part.ToAnchoredPos(refer);
        }
        else
        {
            ui.gameObject.SetActive(false);
        }
    }

    private void ResetTouchButtons()
    {
        UpdateBtnState(touchBtn_Default, false);
        UpdateBtnState(touchBtn_3D, false);
        if (touchCollider_Cube != null)
            touchCollider_Cube.GetComponent<Renderer>().material.color = Color.white;
    }
}