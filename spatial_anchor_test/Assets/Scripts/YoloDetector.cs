using UnityEngine;

// YoloDetector — Detection class + const 만 보존하는 stub.
// 실제 추론은 QnnYoloDetector (Java native QNN engine) 가 담당.
//
// 원본 YoloDetector.cs 는 Unity Sentis 의존이라 우리 Unity 2022.3 + Sentis 없는 환경에서
// 컴파일 fail. Detection class 와 const (QnnYoloDetector 가 reference) 만 살림.

public class Detection
{
    public float xCenter;   // 0~320 (input space)
    public float yCenter;
    public float width;
    public float height;
    public int classId;     // 0~79 (COCO)
    public float confidence;

    public string Label => COCO_CLASS_NAMES[classId];

    public static readonly string[] COCO_CLASS_NAMES = new[] {
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
        "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
        "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket", "bottle",
        "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
        "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch", "potted plant", "bed",
        "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone", "microwave", "oven",
        "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
    };
}

public class YoloDetector : MonoBehaviour
{
    public const int INPUT_SIZE = 320;
    public const int NUM_CLASSES = 80;
    public const int NUM_ANCHORS = 2100;
}
