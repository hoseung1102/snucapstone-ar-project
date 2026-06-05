using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;

// YOLO11n 추론 (Unity Sentis, CPU baseline).
// Step B에서 만든 yolo11n.onnx (320×320, 80 COCO 클래스)를 그대로 사용.
//
// v0.2.1: CPU 추론, 정적/라이브 텍스처 모두 지원.
// v0.2.3에서 NPU 가속으로 교체 예정 (TFLite + NNAPI delegate 또는 QNN direct).
//
// 입력: Texture (어떤 크기든 OK, 내부에서 320×320으로 리사이즈)
// 출력: List<Detection> — confidence 필터 + NMS 통과한 박스
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
    public const int NUM_ANCHORS = 2100;  // 40×40 + 20×20 + 10×10

    [Header("추론 설정")]
    [Tooltip("Resources 폴더 내 모델 경로 (확장자 X)")]
    public string modelResourceName = "yolo11n";

    [Tooltip("최소 confidence (이하 박스는 버림)")]
    [Range(0.05f, 0.95f)]
    public float confThreshold = 0.25f;

    [Tooltip("NMS IoU 임계값 (이상 겹치는 박스 중 하나만 남김)")]
    [Range(0.1f, 0.9f)]
    public float iouThreshold = 0.45f;

    [Header("상태")]
    public bool isReady;
    public string statusMessage = "초기화 중...";
    public int lastDetectionCount;
    public long lastInferenceMs;

    Model model;
    Worker worker;

    void Start()
    {
        ModelAsset modelAsset = Resources.Load<ModelAsset>(modelResourceName);
        if (modelAsset == null)
        {
            statusMessage = $"❌ Resources/{modelResourceName}.onnx 못 찾음";
            Debug.LogError($"[YoloDetector] {statusMessage}");
            return;
        }

        model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, BackendType.CPU);

        isReady = true;
        statusMessage = $"✅ YOLO11n 로드됨 ({modelResourceName})";
        Debug.Log($"[YoloDetector] {statusMessage}");
    }

    void OnDestroy()
    {
        worker?.Dispose();
    }

    // 추론 실행. texture는 어떤 크기/포맷이든 OK (내부에서 변환).
    public List<Detection> Detect(Texture texture)
    {
        if (!isReady) return new List<Detection>();

        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        TextureTransform tx = new TextureTransform()
            .SetDimensions(width: INPUT_SIZE, height: INPUT_SIZE, channels: 3)
            .SetTensorLayout(TensorLayout.NCHW);
        Tensor<float> input = TextureConverter.ToTensor(texture, tx);

        worker.Schedule(input);

        Tensor<float> output = worker.PeekOutput() as Tensor<float>;
        Tensor<float> outputCPU = output.ReadbackAndClone() as Tensor<float>;
        float[] data = outputCPU.DownloadToArray();

        input.Dispose();
        outputCPU.Dispose();

        List<Detection> dets = Postprocess(data);

        long end = System.Diagnostics.Stopwatch.GetTimestamp();
        lastInferenceMs = (end - start) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        lastDetectionCount = dets.Count;

        return dets;
    }

    // YOLO11 output shape: (1, 84, 2100) — flattened to length 1*84*2100
    // 인덱싱: data[ch * NUM_ANCHORS + anchor]
    //   ch 0~3 = (xCenter, yCenter, width, height) in INPUT_SIZE space
    //   ch 4~83 = 80 class confidences (sigmoid 적용된 값)
    List<Detection> Postprocess(float[] data)
    {
        List<Detection> candidates = new List<Detection>();

        for (int a = 0; a < NUM_ANCHORS; a++)
        {
            // 최고 confidence 클래스 찾기
            float maxConf = 0f;
            int maxClass = -1;
            for (int c = 0; c < NUM_CLASSES; c++)
            {
                float conf = data[(4 + c) * NUM_ANCHORS + a];
                if (conf > maxConf)
                {
                    maxConf = conf;
                    maxClass = c;
                }
            }
            if (maxConf < confThreshold) continue;

            candidates.Add(new Detection
            {
                xCenter = data[0 * NUM_ANCHORS + a],
                yCenter = data[1 * NUM_ANCHORS + a],
                width   = data[2 * NUM_ANCHORS + a],
                height  = data[3 * NUM_ANCHORS + a],
                classId = maxClass,
                confidence = maxConf,
            });
        }

        // confidence 내림차순
        candidates.Sort((p, q) => q.confidence.CompareTo(p.confidence));

        // Non-Maximum Suppression
        List<Detection> kept = new List<Detection>();
        foreach (var d in candidates)
        {
            bool overlap = false;
            foreach (var k in kept)
            {
                if (IoU(d, k) > iouThreshold)
                {
                    overlap = true;
                    break;
                }
            }
            if (!overlap) kept.Add(d);
        }
        return kept;
    }

    static float IoU(Detection a, Detection b)
    {
        float ax1 = a.xCenter - a.width / 2, ay1 = a.yCenter - a.height / 2;
        float ax2 = a.xCenter + a.width / 2, ay2 = a.yCenter + a.height / 2;
        float bx1 = b.xCenter - b.width / 2, by1 = b.yCenter - b.height / 2;
        float bx2 = b.xCenter + b.width / 2, by2 = b.yCenter + b.height / 2;
        float ix1 = Mathf.Max(ax1, bx1), iy1 = Mathf.Max(ay1, by1);
        float ix2 = Mathf.Min(ax2, bx2), iy2 = Mathf.Min(ay2, by2);
        float iw = Mathf.Max(0, ix2 - ix1), ih = Mathf.Max(0, iy2 - iy1);
        float inter = iw * ih;
        float area = a.width * a.height + b.width * b.height - inter;
        return area > 0 ? inter / area : 0;
    }
}
