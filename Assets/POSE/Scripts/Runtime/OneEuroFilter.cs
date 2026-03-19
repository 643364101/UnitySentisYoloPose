using UnityEngine;

/// <summary>
/// One Euro Filter（一欧元滤波器）
///
/// 一种自适应低通滤波器：
/// - 目标移动慢时：增加平滑，减少抖动
/// - 目标移动快时：减少平滑，降低延迟
///
/// 非常适合用于：
/// - 人体关键点坐标平滑
/// - 手势追踪
/// - 头部、手腕、脚踝等抖动较明显的实时数据
///
/// 参考思想：
/// cutoff = minCutoff + beta * |dx|
/// 速度越大，截止频率越高，滤波越弱。
/// </summary>
public class OneEuroFilter
{
    /// <summary>最小截止频率。越大越跟手，越小越平滑。</summary>
    private float _minCutoff;

    /// <summary>速度系数。越大表示“运动快时更少滤波”。</summary>
    private float _beta;

    /// <summary>导数低通滤波截止频率。</summary>
    private float _dCutoff;

    /// <summary>上一次滤波后的值。</summary>
    private float _prevValue;

    /// <summary>平滑后的导数（变化率）。</summary>
    private float _dx;

    /// <summary>上一次时间戳。</summary>
    private float _lastTime;

    /// <summary>是否为第一次输入。</summary>
    private bool _firstTime;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        _minCutoff = Mathf.Max(0.0001f, minCutoff);
        _beta = Mathf.Max(0f, beta);
        _dCutoff = Mathf.Max(0.0001f, dCutoff);
        _firstTime = true;
    }

    /// <summary>
    /// 更新滤波参数。
    /// 可在运行时动态调整平滑程度。
    /// </summary>
    public void UpdateParams(float minCutoff, float beta, float dCutoff)
    {
        _minCutoff = Mathf.Max(0.0001f, minCutoff);
        _beta = Mathf.Max(0f, beta);
        _dCutoff = Mathf.Max(0.0001f, dCutoff);
    }

    /// <summary>
    /// 输入一个值并返回滤波结果。
    ///
    /// 参数 timestamp：
    /// - 若传入真实时间（如 Time.time），滤波会按真实帧间隔计算
    /// - 若不传，则默认按 60 FPS 估算 dt
    /// </summary>
    public float Filter(float value, float timestamp = -1f)
    {
        // 第一次输入直接返回，避免初始跳变
        if (_firstTime)
        {
            _firstTime = false;
            _prevValue = value;
            _lastTime = timestamp;
            return value;
        }

        // 若未提供时间戳，则假定 60fps
        float dt = (timestamp < 0f) ? (1f / 60f) : (timestamp - _lastTime);
        _lastTime = timestamp;

        // 避免 dt 非法
        if (dt <= 0f)
            return _prevValue;

        float frequency = 1f / dt;

        // 1. 先估算当前输入变化率
        float dx = (value - _prevValue) * frequency;

        // 2. 对变化率本身做一次低通滤波，得到更稳定的速度估计
        float dAlpha = Alpha(frequency, _dCutoff);
        float edx = dAlpha * dx + (1f - dAlpha) * _dx;
        _dx = edx;

        // 3. 根据速度动态调整 cutoff
        float cutoff = _minCutoff + _beta * Mathf.Abs(edx);

        // 4. 用动态 alpha 做最终值滤波
        float alpha = Alpha(frequency, cutoff);
        float currValue = alpha * value + (1f - alpha) * _prevValue;

        _prevValue = currValue;
        return currValue;
    }

    /// <summary>
    /// 根据频率与截止频率计算低通滤波系数 alpha。
    /// </summary>
    private float Alpha(float frequency, float cutoff)
    {
        cutoff = Mathf.Max(0.0001f, cutoff);
        frequency = Mathf.Max(0.0001f, frequency);

        float tau = 1f / (2f * Mathf.PI * cutoff);
        return 1f / (1f + tau * frequency);
    }
}