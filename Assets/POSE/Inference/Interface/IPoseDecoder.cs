using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Unity.InferenceEngine;


// 定义解码器：负责把 Tensor 变成 HumanPose 列表
public interface IPoseDecoder
{
    List<HumanPose> Decode(Tensor<float> t, float confThreshold, float keypointThreshold, float nmsThreshold, Vector2 webcamSize);
}

// 定义策略：负责控制画面显示和推理节奏
public interface IInferenceStrategy : System.IDisposable // 继承接口
{
    UniTask ExecuteAsync(WebCamTexture webcam, System.Func<UniTask> performInference);
    Texture GetInferenceSource();
}