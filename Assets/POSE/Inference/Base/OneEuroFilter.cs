using UnityEngine;


// --- OneEuroFilter 实现类 ---
// 一种自适应的低通滤波器，在速度快时减少平滑以降低延迟，在速度慢时增加平滑以减少抖动。
public class OneEuroFilter
{
    private float _minCutoff; // 最小截止频率
    private float _beta; // 速度系数
    private float _dCutoff; // 导数截止频率
    private float _prevValue; // 上一次的值
    private float _dx; // 导数（变化率）
    private float _lastTime; // 上一次的时间戳
    private bool _firstTime;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
        _firstTime = true;
    }

    public void UpdateParams(float minCutoff, float beta, float dCutoff)
    {
        _minCutoff = minCutoff;
        _beta = beta;
        _dCutoff = dCutoff;
    }

    public float Filter(float value, float timestamp = -1f)
    {
        // 第一次输入，直接返回
        if (_firstTime)
        {
            _firstTime = false;
            _prevValue = value;
            _lastTime = timestamp;
            return value;
        }

        // 计算时间差 dt
        float dt = (timestamp == -1f) ? (1f / 60f) : (timestamp - _lastTime);
        _lastTime = timestamp;

        // 避免除以0或负时间
        if (dt <= 0) return _prevValue;

        float frequency = 1.0f / dt;

        // 估算变化率 dx (Derivative)
        float dx = (value - _prevValue) * frequency;
        float edx = Alpha(frequency, _dCutoff) * dx + (1 - Alpha(frequency, _dCutoff)) * _dx;
        _dx = edx;

        // 动态计算截止频率 (Cutoff)
        // 速度(edx)越快，cutoff 越大，滤波越少，延迟越低
        float cutoff = _minCutoff + _beta * Mathf.Abs(edx);

        // 标准低通滤波
        float alpha = Alpha(frequency, cutoff);
        float currValue = alpha * value + (1 - alpha) * _prevValue;
        _prevValue = currValue;

        return currValue;
    }

    private float Alpha(float frequency, float cutoff)
    {
        float tau = 1.0f / (2 * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau * frequency);
    }
}