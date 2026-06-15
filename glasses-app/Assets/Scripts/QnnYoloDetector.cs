using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// QnnYoloDetector — Qualcomm QNN Runtime (HTP/NPU) 기반 YOLO 추론
// Sentis 기반 YoloDetector와 같은 public 인터페이스 (Detect, isReady, statusMessage 등)
// HelloAR.cs에서 한 줄만 바꾸면 교체 가능: AddComponent<YoloDetector>() → <QnnYoloDetector>()
//
// ⚠️ 현재 상태: SCAFFOLDING. 실제 QNN 호출 미구현 (Java native 측 필요).
//    동작시키려면 README_NPU.md 의 사용자 액션 단계 완료 필요.
//
// 호출 흐름 (production):
//   Awake → AndroidJavaObject로 QnnYoloEngine 인스턴스 생성 → initialize(.bin 경로)
//   Detect(texture) → 텍스처를 float[]로 변환 → engine.execute(float[]) → 출력 후처리
public class QnnYoloDetector : MonoBehaviour
{
    public const int INPUT_SIZE = YoloDetector.INPUT_SIZE;       // 320
    public const int NUM_CLASSES = YoloDetector.NUM_CLASSES;     // 80
    public const int NUM_ANCHORS = YoloDetector.NUM_ANCHORS;     // 2100

    [Header("v0.2.6: TFLite + QNN Delegate (w8a8 검증 path)")]
    [Tooltip("StreamingAssets 내 .tflite 파일. AI Hub 의 yolov11_det --quantize w8a8 export")]
    public string contextBinFilename = "yolo11l.tflite";

    [Tooltip("최소 confidence. w8a8 양자화로 book 같은 객체 0.16~0.47 까지 떨어짐 — 0.20 으로 살리기")]
    [Range(0.05f, 0.95f)]
    public float confThreshold = 0.20f;

    [Tooltip("NMS IoU 임계값")]
    [Range(0.1f, 0.9f)]
    public float iouThreshold = 0.45f;

    [Header("DB_obs — 광고 가치 객체 화이트리스트 (Layer 1)")]
    [Tooltip("통과시킬 COCO class IDs. 비우면 전체 통과 (legacy 동작). " +
             "db/obs_whitelist.json 가 source of truth — 변경 시 양쪽 동기화")]
    public int[] allowedClassIds = new int[] { };   // v0.3.5 PoC 디버깅: 비워 전체 통과

    [Tooltip("제외할 COCO class IDs. v0.3.8: person(0) — 사용자 손/팔 매번 잡혀 noise")]
    public int[] blockedClassIds = new int[] { 0 };
    HashSet<int> _blockedSet;

    [Header("Layer 2 — bbox 크기 필터")]
    [Tooltip("최소 bbox 면적 비율 (INPUT_SIZE² 대비). PoC 시연용 loose 5% — 작게 보여도 인정.\n" +
             "노션 4.5 Tier 3 spec(25%+)보다 완화. 작은 객체 인식률 우선.")]
    [Range(0.001f, 0.5f)]
    public float minAreaRatio = 0.05f;

    [Tooltip("최대 bbox 면적 비율. PoC 디버깅 위해 0.80 (큰 객체 통과)")]
    [Range(0.1f, 1.0f)]
    public float maxAreaRatio = 0.80f;

    HashSet<int> _allowedSet;
    public int lastFilteredOutCount;     // 디버깅 — 필터에 의해 제거된 객체 수

    [Header("상태")]
    public bool isReady;
    public string statusMessage = "초기화 중...";
    public int lastDetectionCount;
    public long lastInferenceMs;

    AndroidJavaObject qnnEngine;
    float[] inputBuffer;     // 비양자화 모델용 NCHW float32 (사용 안 함, legacy)
    byte[]  inputBytes;       // 양자화 모델용 NHWC uint8: 320*320*3 = 307,200 bytes
    System.IntPtr reusableInputPtr;  // v0.3.2: Java direct buffer 의 native address (Option C)
    bool useReusableBuffer;          // alloc 성공 시 true
    Texture2D readbackTex;

    void Start()
    {
        // DB_obs HashSet 초기화 (Layer 1 필터용). 비어있으면 전체 통과 (legacy)
        if (allowedClassIds != null && allowedClassIds.Length > 0)
        {
            _allowedSet = new HashSet<int>(allowedClassIds);
            Debug.Log($"[QnnYoloDetector] DB_obs whitelist 로드: {_allowedSet.Count} class IDs");
        }
        if (blockedClassIds != null && blockedClassIds.Length > 0)
        {
            _blockedSet = new HashSet<int>(blockedClassIds);
            Debug.Log($"[QnnYoloDetector] DB_obs blocklist 로드: {_blockedSet.Count} class IDs");
        }

        if (Application.platform != RuntimePlatform.Android)
        {
            statusMessage = "❌ Android only (QNN HTP requires Hexagon NPU)";
            Debug.LogWarning($"[QnnYoloDetector] {statusMessage}");
            return;
        }

        // .bin 파일을 StreamingAssets에서 로컬 캐시로 복사 (JNI에서 직접 path 접근 가능하도록)
        string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, contextBinFilename);
        string dstPath = System.IO.Path.Combine(Application.persistentDataPath, contextBinFilename);
        StartCoroutine(CopyAndInitialize(srcPath, dstPath));
    }

    System.Collections.IEnumerator CopyAndInitialize(string srcPath, string dstPath)
    {
        // Android에서 StreamingAssets는 APK 안에 있어서 File.Copy로 못 읽음 → UnityWebRequest 사용
        statusMessage = "context binary 복사 중...";
        var req = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
        yield return req.SendWebRequest();
        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            statusMessage = $"❌ {contextBinFilename} 로드 실패: {req.error}";
            Debug.LogError($"[QnnYoloDetector] {statusMessage}");
            yield break;
        }
        System.IO.File.WriteAllBytes(dstPath, req.downloadHandler.data);
        Debug.Log($"[QnnYoloDetector] tflite 모델 복사: {dstPath} ({req.downloadHandler.data.Length} bytes)");

        // v0.2.6: native lib path 자동 — Maven QNN Delegate가 알아서 처리
        // nativeLibraryDir = APK의 lib/arm64 폴더 (Qualcomm delegate가 skel 경로 찾는 용도)
        string nativeLibDir;
        using (var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity"))
        using (var appInfo = activity.Call<AndroidJavaObject>("getApplicationInfo"))
        {
            nativeLibDir = appInfo.Get<string>("nativeLibraryDir");
        }
        Debug.Log($"[QnnYoloDetector] nativeLibraryDir = {nativeLibDir}");

        // Java QnnYoloEngine 인스턴스 생성 + initialize
        try
        {
            qnnEngine = new AndroidJavaObject("com.eagleeye.qnn.QnnYoloEngine");
            bool ok = qnnEngine.Call<bool>("initialize", dstPath, nativeLibDir);
            if (!ok)
            {
                statusMessage = "❌ QnnYoloEngine.initialize() 실패";
                Debug.LogError($"[QnnYoloDetector] {statusMessage}");
                yield break;
            }
        }
        catch (System.Exception e)
        {
            statusMessage = $"❌ AndroidJavaObject 생성 실패: {e.Message}";
            Debug.LogError($"[QnnYoloDetector] {statusMessage} — Java/native 측 미구현일 가능성. README_NPU.md 참고");
            yield break;
        }

        inputBuffer = new float[3 * INPUT_SIZE * INPUT_SIZE];
        inputBytes  = new byte[INPUT_SIZE * INPUT_SIZE * 3];   // NHWC uint8
        readbackTex = new Texture2D(INPUT_SIZE, INPUT_SIZE, TextureFormat.RGB24, false);

        // v0.3.2 Option C: Java direct ByteBuffer 의 native address 얻기
        try
        {
            long addr = qnnEngine.Call<long>("allocateInputBuffer");
            if (addr != 0)
            {
                reusableInputPtr = new System.IntPtr(addr);
                useReusableBuffer = true;
                Debug.Log($"[QnnYoloDetector] Option C reusable buffer ready: addr=0x{addr:X}");
            }
            else
            {
                Debug.LogWarning("[QnnYoloDetector] allocateInputBuffer returned 0 — fallback to executeBytes");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[QnnYoloDetector] reusable buffer unavailable: {e.Message} — fallback");
        }

        bool isMock = false;
        try { isMock = qnnEngine.Call<bool>("isMockMode"); } catch { }

        isReady = true;
        statusMessage = isMock
            ? "⚠️ MOCK MODE (native QNN 미연결, SDK 다운로드 필요)"
            : $"✅ QNN HTP runtime ready ({contextBinFilename})";
        Debug.Log($"[QnnYoloDetector] {statusMessage}");
    }

    void OnDestroy()
    {
        if (qnnEngine != null)
        {
            try { qnnEngine.Call("release"); }
            catch { /* engine은 native 측에서 dispose */ }
            qnnEngine.Dispose();
        }
    }

    // texture (어떤 크기/포맷) → 320×320 RGB float[] (NCHW 형식, [0,1] 정규화) → JNI execute → 후처리
    public List<Detection> Detect(Texture texture)
    {
        if (!isReady) return new List<Detection>();

        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // 1. Texture → 320×320 RGB float[]
        PreprocessTexture(texture);
        long preprocTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // 2. JNI 호출
        float[] output;
        if (useReusableBuffer)
        {
            // v0.3.2 Option C: Marshal.Copy 로 Java direct buffer 에 직접 write (byte[] in marshal 제거)
            Marshal.Copy(inputBytes, 0, reusableInputPtr, inputBytes.Length);
            output = qnnEngine.Call<float[]>("executeReusable");
        }
        else
        {
            output = qnnEngine.Call<float[]>("executeBytes", inputBytes);
        }
        long jniTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // execute 실패 시 length=0 반환 → fake detection 방지
        if (output == null || output.Length == 0)
        {
            long endTicksEarly = System.Diagnostics.Stopwatch.GetTimestamp();
            lastInferenceMs = (endTicksEarly - startTicks) * 1000 / System.Diagnostics.Stopwatch.Frequency;
            lastDetectionCount = 0;
            return new List<Detection>();
        }

        // 3. NMS 후처리 (YoloDetector와 동일한 알고리즘 — 코드 공유 안 함, 별도 복사)
        List<Detection> dets = Postprocess(output);

        long endTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long freq = System.Diagnostics.Stopwatch.Frequency;
        long preprocMs = (preprocTicks - startTicks) * 1000 / freq;
        long jniMs     = (jniTicks - preprocTicks) * 1000 / freq;
        long postMs    = (endTicks - jniTicks) * 1000 / freq;
        lastInferenceMs = (endTicks - startTicks) * 1000 / freq;

        Debug.Log($"[QnnYoloDetector] TIMING ms: preproc={preprocMs} jni+npu={jniMs} post={postMs} total={lastInferenceMs} | path={(useReusableBuffer ? "Reusable" : "Bytes")} (outLen={output?.Length ?? 0})");
        lastDetectionCount = dets.Count;
        return dets;
    }

    void PreprocessTexture(Texture texture)
    {
        // GPU 텍스처를 CPU로 readback → 320×320 RGB float, NCHW
        // 효율적으로 하려면 ComputeShader 사용, 여기선 단순 구현
        RenderTexture prev = RenderTexture.active;
        RenderTexture tmp = RenderTexture.GetTemporary(INPUT_SIZE, INPUT_SIZE, 0);
        Graphics.Blit(texture, tmp);
        RenderTexture.active = tmp;
        readbackTex.ReadPixels(new Rect(0, 0, INPUT_SIZE, INPUT_SIZE), 0, 0);
        readbackTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);

        Color32[] pixels = readbackTex.GetPixels32();

        // v0.4.2: 안경 카메라 sensor frame 이 CW 90도 누워있음 (Mac A/B 실험으로 확정)
        // CCW 90도 = rotation=270 로 보정. videoRotationAngle 무시 (hardware 일관)
        int rotation = 270;

        int N = INPUT_SIZE;
        for (int srcY = 0; srcY < N; srcY++)
        {
            for (int srcX = 0; srcX < N; srcX++)
            {
                int srcIdx = srcY * N + srcX;
                int dstX, dstY;
                switch (rotation)
                {
                    case 90:  dstX = N - 1 - srcY; dstY = srcX;         break;
                    case 180: dstX = N - 1 - srcX; dstY = N - 1 - srcY; break;
                    case 270: dstX = srcY;         dstY = N - 1 - srcX; break;
                    default:  dstX = srcX;         dstY = srcY;         break;
                }
                int dstIdx = (dstY * N + dstX) * 3;
                inputBytes[dstIdx + 0] = pixels[srcIdx].r;
                inputBytes[dstIdx + 1] = pixels[srcIdx].g;
                inputBytes[dstIdx + 2] = pixels[srcIdx].b;
            }
        }
    }

    // v0.3.0: 공식 qai_hub_models yolov11_det 모델 사용
    // Java engine 에서 dequant 까지 처리, flat layout:
    //   [box0_cx, box0_cy, box0_w, box0_h, ..., box2099_cx, ..., score0, score1, ..., class0, class1, ...]
    //   총 길이 = 2100*4 + 2100 + 2100 = 14700
    const int FLAT_BOX_LEN = NUM_ANCHORS * 4;        // 8400
    const int FLAT_SCORE_OFFSET = NUM_ANCHORS * 4;    // 8400
    const int FLAT_CLASS_OFFSET = NUM_ANCHORS * 5;    // 10500

    List<Detection> Postprocess(float[] data)
    {
        List<Detection> candidates = new List<Detection>();
        if (data == null || data.Length < NUM_ANCHORS * 6) return candidates;

        // v0.3.4: DB_obs Layer 1+2 필터 추가
        const float INPUT_AREA = (float)(INPUT_SIZE * INPUT_SIZE);  // 320*320 = 102400
        int filteredByConf = 0, filteredByClass = 0, filteredByArea = 0;

        // v0.3.3 fix: ai_hub_models 의 yolov11_det 출력 = (x1, y1, x2, y2)
        // Detection 필드 (xCenter, yCenter, width, height) 로 변환
        for (int a = 0; a < NUM_ANCHORS; a++)
        {
            float conf = data[FLAT_SCORE_OFFSET + a];
            if (conf < confThreshold) { filteredByConf++; continue; }

            // Layer 1 — DB_obs class whitelist + blocklist
            int classId = (int)data[FLAT_CLASS_OFFSET + a];
            if (_allowedSet != null && !_allowedSet.Contains(classId)) { filteredByClass++; continue; }
            if (_blockedSet != null && _blockedSet.Contains(classId))  { filteredByClass++; continue; }

            float x1 = data[a * 4 + 0];
            float y1 = data[a * 4 + 1];
            float x2 = data[a * 4 + 2];
            float y2 = data[a * 4 + 3];
            float w  = x2 - x1;
            float h  = y2 - y1;

            // Layer 2 — bbox area filter
            float areaRatio = (w * h) / INPUT_AREA;
            if (areaRatio < minAreaRatio || areaRatio > maxAreaRatio) { filteredByArea++; continue; }

            candidates.Add(new Detection
            {
                xCenter = (x1 + x2) * 0.5f,
                yCenter = (y1 + y2) * 0.5f,
                width   = w,
                height  = h,
                classId = classId,
                confidence = conf,
            });
        }

        lastFilteredOutCount = filteredByConf + filteredByClass + filteredByArea;

        // 디버그: class 별 max conf — 책(73) 이 진짜 어떤 conf 로 잡히는지 확인
        // 모든 anchor 중 class 별 최고 conf 추출
        var perClassMax = new Dictionary<int, float>();
        for (int a = 0; a < NUM_ANCHORS; a++)
        {
            float c = data[FLAT_SCORE_OFFSET + a];
            int cid = (int)data[FLAT_CLASS_OFFSET + a];
            if (!perClassMax.ContainsKey(cid) || c > perClassMax[cid]) perClassMax[cid] = c;
        }
        // top-5 sorted desc
        var top5 = new List<KeyValuePair<int, float>>(perClassMax);
        top5.Sort((a, b) => b.Value.CompareTo(a.Value));
        string topStr = "";
        for (int i = 0; i < System.Math.Min(5, top5.Count); i++)
            topStr += $"cls{top5[i].Key}={top5[i].Value:F2} ";
        // book(73) 명시 표시
        float bookMax = perClassMax.ContainsKey(73) ? perClassMax[73] : -1f;
        Debug.Log($"[Postprocess] filterConf={filteredByConf} filterCls={filteredByClass} filterArea={filteredByArea} cand={candidates.Count} | top5: {topStr}| book(73)={bookMax:F2}");

        candidates.Sort((p, q) => q.confidence.CompareTo(p.confidence));
        List<Detection> kept = new List<Detection>();
        foreach (var d in candidates)
        {
            bool overlap = false;
            foreach (var k in kept) if (IoU(d, k) > iouThreshold) { overlap = true; break; }
            if (!overlap) kept.Add(d);
        }
        return kept;
    }

    static float IoU(Detection a, Detection b)
    {
        float ax1 = a.xCenter - a.width/2, ay1 = a.yCenter - a.height/2;
        float ax2 = a.xCenter + a.width/2, ay2 = a.yCenter + a.height/2;
        float bx1 = b.xCenter - b.width/2, by1 = b.yCenter - b.height/2;
        float bx2 = b.xCenter + b.width/2, by2 = b.yCenter + b.height/2;
        float ix1 = Mathf.Max(ax1, bx1), iy1 = Mathf.Max(ay1, by1);
        float ix2 = Mathf.Min(ax2, bx2), iy2 = Mathf.Min(ay2, by2);
        float iw = Mathf.Max(0, ix2 - ix1), ih = Mathf.Max(0, iy2 - iy1);
        float inter = iw * ih;
        float area = a.width * a.height + b.width * b.height - inter;
        return area > 0 ? inter / area : 0;
    }
}
