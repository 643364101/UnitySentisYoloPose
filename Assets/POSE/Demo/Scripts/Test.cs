using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 姿态系统综合测试脚本。
/// 
/// 主要演示：
/// 1. 手势驱动按钮
/// 2. 空中触碰普通 UI
/// 3. 空中触碰独立 Canvas / 独立 UI Camera 的 UI
/// 4. 手部跟随 2D UI
/// 5. 手部跟随 3D 物体
/// 6. 手部碰撞 3D Collider
/// 
/// 这是一个“业务示例脚本”，用于验证：
/// - BodyPartExtensions
/// - PoseHitTestUtils
/// - ToAnchoredPos / ToWorldPos
/// - OnPoseUpdated / OnLimbsUpdated
/// 等功能是否协同正常。
/// </summary>
public class Test : MonoBehaviour
{
    [Header("Gesture Buttons (手势触发)")]
    public ARButton left;
    public ARButton leftUp;
    public ARButton right;
    public ARButton rightUp;

    [Header("Touch Buttons (触碰触发)")]
    public ARButton touchBtn_Default;     // 普通 UI
    public ARButton touchBtn_SeparateUI;  // 独立 Canvas / UI Camera 的 UI
    public Collider touchCollider_Cube;   // 3D 物体
    public Camera uiCamera;               // 独立 UI Canvas 使用的相机

    [Header("2D UI Tracking")]
    public RectTransform leftHandUI;
    public RectTransform rightHandUI;

    [Header("3D World Tracking")]
    public Transform leftHand3D;
    public Transform rightHand3D;

    [Header("Debug")]
    public TextMeshProUGUI txtHint;

    /// <summary>
    /// 缓存默认姿态来源区域，通常是 cameraView.rectTransform。
    /// </summary>
    private RectTransform _sourceRect;

    private void Start()
    {
        if (uiCamera == null)
            uiCamera = Camera.main;

        if (PoseManager.Instance != null && PoseManager.Instance.cameraView != null)
            _sourceRect = PoseManager.Instance.cameraView.rectTransform;

        if (left) left.onClick.AddListener(() => ShowHint("Gesture: Left Middle"));
        if (leftUp) leftUp.onClick.AddListener(() => ShowHint("Gesture: Left Up"));
        if (right) right.onClick.AddListener(() => ShowHint("Gesture: Right Middle"));
        if (rightUp) rightUp.onClick.AddListener(() => ShowHint("Gesture: Right Up"));

        if (touchBtn_Default) touchBtn_Default.onClick.AddListener(() => ShowHint("Touched: Default UI"));
        if (touchBtn_SeparateUI) touchBtn_SeparateUI.onClick.AddListener(() => ShowHint("Touched: Separate UI"));
    }

    private void OnEnable()
    {
        if (PoseManager.Instance != null)
        {
            PoseManager.Instance.OnPoseUpdated += OnPoseUpdated;
            PoseManager.Instance.OnLimbsUpdated += OnLimbsUpdated;
        }
    }

    private void OnDisable()
    {
        if (PoseManager.Instance != null)
        {
            PoseManager.Instance.OnPoseUpdated -= OnPoseUpdated;
            PoseManager.Instance.OnLimbsUpdated -= OnLimbsUpdated;
        }
    }

    private void ShowHint(string msg)
    {
        if (txtHint) txtHint.text = msg;
        Debug.Log(msg);
    }

    // ========================================================================
    // 1. 手势逻辑
    // ========================================================================

    /// <summary>
    /// 收到手势更新后，刷新对应 ARButton 的触发状态。
    /// </summary>
    private void OnLimbsUpdated(GestureType type)
    {
        UpdateBtnState(left, type == GestureType.LeftMiddle);
        UpdateBtnState(leftUp, type == GestureType.LeftUp);
        UpdateBtnState(right, type == GestureType.RightMiddle);
        UpdateBtnState(rightUp, type == GestureType.RightUp);
    }

    // ========================================================================
    // 2. 姿态更新
    // ========================================================================

    /// <summary>
    /// 姿态更新回调：
    /// - UI 跟随
    /// - 3D 跟随
    /// - UI 触碰
    /// - 3D 触碰
    /// </summary>
    private void OnPoseUpdated(List<HumanPose> poses)
    {
        if (_sourceRect == null && PoseManager.Instance != null && PoseManager.Instance.cameraView != null)
            _sourceRect = PoseManager.Instance.cameraView.rectTransform;

        if (poses == null || poses.Count == 0 || _sourceRect == null)
        {
            HideAllTrackers();
            ResetTouchTargets();
            return;
        }

        HumanPose pose = poses[0];

        BodyPart leftWrist = pose.GetBodyPart(BodyPartsType.LeftWrist);
        BodyPart rightWrist = pose.GetBodyPart(BodyPartsType.RightWrist);

        // 2D UI 跟随
        UpdateHandUI(leftHandUI, leftWrist, _sourceRect);
        UpdateHandUI(rightHandUI, rightWrist, _sourceRect);

        // 3D 跟随
        UpdateHand3D(leftHand3D, leftWrist);
        UpdateHand3D(rightHand3D, rightWrist);

        // 触碰检测
        CheckAirTouch(leftWrist, rightWrist);
    }

    // ========================================================================
    // 3. 触碰检测
    // ========================================================================

    /// <summary>
    /// 检测左右手是否碰到：
    /// - 普通 UI
    /// - 独立 Canvas / 独立 Camera 的 UI
    /// - 3D Collider
    /// </summary>
    private void CheckAirTouch(BodyPart leftHand, BodyPart rightHand)
    {
        // A. 普通 UI
        if (touchBtn_Default != null)
        {
            RectTransform target = touchBtn_Default.GetComponent<RectTransform>();

            bool isTouched =
                leftHand.IsInsideUI(_sourceRect, target) ||
                rightHand.IsInsideUI(_sourceRect, target);

            UpdateBtnState(touchBtn_Default, isTouched);
        }

        // B. 独立 Canvas / 独立 UI Camera 的 UI
        if (touchBtn_SeparateUI != null)
        {
            RectTransform target = touchBtn_SeparateUI.GetComponent<RectTransform>();

            bool isTouched =
                leftHand.IsInsideUI(_sourceRect, target, uiCamera) ||
                rightHand.IsInsideUI(_sourceRect, target, uiCamera);

            UpdateBtnState(touchBtn_SeparateUI, isTouched);
        }

        // C. 3D Collider
        if (touchCollider_Cube != null)
        {
            bool isHit =
                leftHand.IsTouching3D(touchCollider_Cube) ||
                rightHand.IsTouching3D(touchCollider_Cube);

            Renderer renderer = touchCollider_Cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = isHit ? Color.green : Color.white;
            }

            if (isHit)
            {
                ShowHint("Touched: 3D Cube");
            }
        }
    }

    // ========================================================================
    // 4. 辅助
    // ========================================================================

    /// <summary>
    /// 更新按钮触发状态。
    /// </summary>
    private void UpdateBtnState(ARButton btn, bool isActive)
    {
        if (btn == null) return;

        if (isActive) btn.SetProgress();
        else btn.BreakOffProgress();
    }

    /// <summary>
    /// 更新 2D UI 跟随位置。
    /// </summary>
    private void UpdateHandUI(RectTransform ui, BodyPart part, RectTransform sourceRect)
    {
        if (ui == null) return;

        if (part.hasValue && part.score > 0.3f)
        {
            ui.gameObject.SetActive(true);
            ui.anchoredPosition = part.ToAnchoredPos(sourceRect);
        }
        else
        {
            ui.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 更新 3D 物体跟随位置。
    /// </summary>
    private void UpdateHand3D(Transform hand, BodyPart part)
    {
        if (hand == null) return;

        if (part.hasValue && part.score > 0.3f)
        {
            hand.gameObject.SetActive(true);
            hand.position = part.ToWorldPos();
        }
        else
        {
            hand.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 隐藏所有跟随物体。
    /// </summary>
    private void HideAllTrackers()
    {
        if (leftHandUI) leftHandUI.gameObject.SetActive(false);
        if (rightHandUI) rightHandUI.gameObject.SetActive(false);

        if (leftHand3D) leftHand3D.gameObject.SetActive(false);
        if (rightHand3D) rightHand3D.gameObject.SetActive(false);
    }

    /// <summary>
    /// 重置触碰目标状态。
    /// </summary>
    private void ResetTouchTargets()
    {
        UpdateBtnState(touchBtn_Default, false);
        UpdateBtnState(touchBtn_SeparateUI, false);

        if (touchCollider_Cube != null)
        {
            Renderer renderer = touchCollider_Cube.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = Color.white;
        }
    }
}