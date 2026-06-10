using UnityEngine;

// ClipExtractor — MobileCLIP-S2 INT8 TFLite + QNN Delegate (NPU)
// QnnYoloDetector 와 같은 패턴. AndroidJavaObject 로 com.eagleeye.qnn.QnnClipEngine 호출.
//
// 사용:
//   var clip = gameObject.AddComponent<ClipExtractor>();
//   // ... clip.isReady == true 대기
//   float[] embedding = clip.Embed(croppedTexture);  // (512,) L2-normalized
//
// 입출력:
//   입력: Texture (어떤 크기/포맷) → 내부에서 256×256 RGB로 resize + OpenAI CLIP normalize
//   출력: float[512]  L2-normalized 임베딩 (그래프에 포함됨)
public class ClipExtractor : MonoBehaviour
{
    public const int INPUT_SIZE = 256;
    public const int EMBED_DIM = 512;

    [Header("CLIP 설정 (TFLite + QNN Delegate, HTP cache 활성)")]
    [Tooltip("StreamingAssets 내 .tflite 파일")]
    public string contextBinFilename = "mobileclip_s2.tflite";

    [Tooltip("(선택) StreamingAssets 내 사전 빌드된 HTP context cache. " +
             "존재하면 첫 실행에도 4분 컴파일 없이 즉시 로드. " +
             "없어도 동작 — 첫 실행 1회만 컴파일 후 자동 캐싱돼서 이후 launch 는 즉시 ready.")]
    public string prebuiltContextBinFilename = "mobileclip_s2.qnn_context.bin";

    [Header("v0.7.3 중앙 crop (환경 무관 category 매칭)")]
    [Tooltip("프레임 중앙만 잘라 embedding → 배경(침대/주방/책상) 제거, 가운데 든 물체에 집중.\n" +
             "⚠️ build_adversarial_db.py 의 CLIP_CROP 과 반드시 같은 값이어야 query↔ref 비교 성립.")]
    [Range(0.2f, 1.0f)] public float cropFraction = 0.5f;

    [Header("v1.2 색 기반 brand 판별 (코크 빨강 / 펩시 파랑)")]
    [Tooltip("PreprocessTexture readback 의 중앙 박스 비율. 256² 평면에서 중앙만 색 카운트 (배경 색 제거).")]
    [Range(0.4f, 1.0f)] public float colorSampleFraction = 0.8f;
    [Tooltip("채널 우세 판정 마진. r>g+m && r>b+m → red. b>r+m && b>g+m → blue.")]
    public int colorMargin = 20;

    [Header("상태")]
    public bool isReady;
    public string statusMessage = "초기화 중...";
    public long lastEmbedMs;
    // v1.2: 마지막 PreprocessTexture 의 중앙 박스 색 통계 (HelloAR ResolveBrandByColor 가 읽음).
    // v1.3: dominant 픽셀 카운트 → 영역 평균색(mean RGB) 으로 brand 판별 전환. 아래 ratio 들은
    //   strict count 방식이라 펩시 파란 몸통이 임계 미달로 blue=0.00 → 빨간 뚜껑 때문에 coke 오판됨.
    //   ratio 3종은 로그 비교용으로만 유지 (실측 보정 시 mean 과 대조).
    public float lastRedRatio { get; private set; }
    public float lastBlueRatio { get; private set; }
    public bool lastColorValid { get; private set; }
    // v1.3: 중앙 박스 평균색 (0-255 스케일 float). 면적 큰 색이 평균을 끌어 brand 판별.
    public float lastMeanR { get; private set; }
    public float lastMeanG { get; private set; }
    public float lastMeanB { get; private set; }

    AndroidJavaObject qnnEngine;
    float[] inputBuffer;     // NCHW (3*256*256) float32 normalized
    Texture2D readbackTex;

    // OpenAI CLIP 정규화 상수 (MobileCLIP 동일)
    static readonly float[] CLIP_MEAN = { 0.48145466f, 0.4578275f, 0.40821073f };
    static readonly float[] CLIP_STD  = { 0.26862954f, 0.26130258f, 0.27577711f };

    void Start()
    {
        if (Application.platform != RuntimePlatform.Android)
        {
            statusMessage = "❌ Android only";
            Debug.LogWarning($"[ClipExtractor] {statusMessage}");
            return;
        }

        string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, contextBinFilename);
        string dstPath = System.IO.Path.Combine(Application.persistentDataPath, contextBinFilename);
        string prebuiltSrcPath = System.IO.Path.Combine(Application.streamingAssetsPath, prebuiltContextBinFilename);
        string prebuiltDstPath = System.IO.Path.Combine(Application.persistentDataPath, prebuiltContextBinFilename);
        StartCoroutine(CopyAndInitialize(srcPath, dstPath, prebuiltSrcPath, prebuiltDstPath));
    }

    System.Collections.IEnumerator CopyAndInitialize(string srcPath, string dstPath,
                                                     string prebuiltSrcPath, string prebuiltDstPath)
    {
        // 1) tflite 모델 복사 (필수)
        statusMessage = $"{contextBinFilename} 복사 중...";
        var req = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
        yield return req.SendWebRequest();
        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            statusMessage = $"❌ {contextBinFilename} 로드 실패: {req.error}";
            Debug.LogError($"[ClipExtractor] {statusMessage}");
            yield break;
        }
        System.IO.File.WriteAllBytes(dstPath, req.downloadHandler.data);
        Debug.Log($"[ClipExtractor] tflite 모델 복사: {dstPath} ({req.downloadHandler.data.Length} bytes)");

        // 2) (선택) 사전 빌드 HTP context cache 복사. StreamingAssets 에 없으면 skip.
        //    UnityWebRequest 로 시도 — 실패 시 단순히 prebuilt 경로 미사용으로 폴백.
        bool prebuiltAvailable = false;
        var preReq = UnityEngine.Networking.UnityWebRequest.Get(prebuiltSrcPath);
        yield return preReq.SendWebRequest();
        if (preReq.result == UnityEngine.Networking.UnityWebRequest.Result.Success
            && preReq.downloadHandler.data != null
            && preReq.downloadHandler.data.Length > 0)
        {
            System.IO.File.WriteAllBytes(prebuiltDstPath, preReq.downloadHandler.data);
            prebuiltAvailable = true;
            Debug.Log($"[ClipExtractor] 사전 빌드 context bin 발견: {prebuiltDstPath} ({preReq.downloadHandler.data.Length} bytes)");
        }
        else
        {
            Debug.Log($"[ClipExtractor] 사전 빌드 context bin 없음 (정상 — 첫 launch 컴파일 후 자동 캐싱). " +
                      $"src={prebuiltSrcPath} reason={preReq.error}");
        }

        // 3) native lib dir 확인
        string nativeLibDir;
        using (var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity"))
        using (var appInfo = activity.Call<AndroidJavaObject>("getApplicationInfo"))
        {
            nativeLibDir = appInfo.Get<string>("nativeLibraryDir");
        }

        // 4) QnnClipEngine init — prebuilt 있으면 prebuilt 경로, 없으면 일반 경로.
        //    두 경로 모두 HTP cache 활성. 차이는 "첫 launch 가 즉시 ready 인가 vs 4분 컴파일 후 ready 인가" 뿐.
        try
        {
            qnnEngine = new AndroidJavaObject("com.eagleeye.qnn.QnnClipEngine");
            bool ok;
            if (prebuiltAvailable)
            {
                Debug.Log("[ClipExtractor] init path = initializeFromContextBin (사전 빌드 cache 사용)");
                ok = qnnEngine.Call<bool>("initializeFromContextBin", prebuiltDstPath, dstPath, nativeLibDir);
            }
            else
            {
                Debug.Log("[ClipExtractor] init path = initialize (tflite 만, HTP cache 자동 생성)");
                ok = qnnEngine.Call<bool>("initialize", dstPath, nativeLibDir);
            }
            if (!ok)
            {
                statusMessage = "❌ QnnClipEngine init 실패";
                Debug.LogError($"[ClipExtractor] {statusMessage}");
                yield break;
            }
        }
        catch (System.Exception e)
        {
            statusMessage = $"❌ Java 측 미구현/로드 실패: {e.Message}";
            Debug.LogError($"[ClipExtractor] {statusMessage}");
            yield break;
        }

        inputBuffer = new float[3 * INPUT_SIZE * INPUT_SIZE];
        readbackTex = new Texture2D(INPUT_SIZE, INPUT_SIZE, TextureFormat.RGB24, false);

        // v1.2: initialize/initializeFromContextBin 은 worker 스레드로 submit 만 하고 즉시 true 반환.
        //   실제 HTP 컴파일(첫 실행 ~125초)이 끝나야 isReady=true → 그때까지 폴링.
        //   컴파일은 worker 스레드라 메인스레드(렌더)는 안 멈춤.
        //   컴파일 영구실패 시 무한 폴링 방지 — 180초 타임아웃.
        const float COMPILE_TIMEOUT_SEC = 180f;
        float compileElapsed = 0f;
        while (!qnnEngine.Call<bool>("isReady"))
        {
            if (compileElapsed >= COMPILE_TIMEOUT_SEC)
            {
                bool timeoutMock = false;
                try { timeoutMock = qnnEngine.Call<bool>("isMockMode"); } catch { }
                if (!timeoutMock)
                {
                    statusMessage = $"❌ CLIP 컴파일 timeout ({COMPILE_TIMEOUT_SEC:F0}초 초과)";
                    Debug.LogError($"[ClipExtractor] {statusMessage}");
                    yield break;
                }
                break;   // mock 모드면 mock 진입 (아래 isReady=true)
            }
            statusMessage = "CLIP 컴파일 중... (최초 1회 ~125초, 화면 정상)";
            yield return new WaitForSeconds(0.5f);
            compileElapsed += 0.5f;
        }

        bool isMock = false;
        bool prewarmed = false;
        try { isMock = qnnEngine.Call<bool>("isMockMode"); } catch { }
        try { prewarmed = qnnEngine.Call<bool>("isCacheBinPrewarmed"); } catch { }

        isReady = true;
        statusMessage = isMock
            ? "⚠️ MOCK MODE (CLIP)"
            : (prewarmed
                ? $"✅ CLIP NPU ready ({contextBinFilename}, prebuilt cache)"
                : $"✅ CLIP NPU ready ({contextBinFilename})");
        Debug.Log($"[ClipExtractor] {statusMessage}");
    }

    void OnDestroy()
    {
        if (qnnEngine != null)
        {
            try { qnnEngine.Call("release"); } catch { }
            qnnEngine.Dispose();
        }
    }

    // texture (어떤 크기) → 256×256 RGB → NCHW float32 normalized → JNI → (512,) L2-norm 임베딩
    public float[] Embed(Texture texture)
    {
        if (!isReady) return null;

        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // 1. Texture → 256×256 RGB → NCHW float32 normalized
        PreprocessTexture(texture);

        // 2. JNI 호출 → float[512]
        float[] embedding;
        try
        {
            embedding = qnnEngine.Call<float[]>("embed", inputBuffer);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ClipExtractor] embed JNI 실패: {e.Message}");
            return null;
        }

        long endTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        lastEmbedMs = (endTicks - startTicks) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        return embedding;
    }

    void PreprocessTexture(Texture texture)
    {
        // GPU 텍스처 → 256×256 RGB → NCHW [R[256*256], G[256*256], B[256*256]] OpenAI CLIP normalized
        RenderTexture prev = RenderTexture.active;
        RenderTexture tmp = RenderTexture.GetTemporary(INPUT_SIZE, INPUT_SIZE, 0);
        // v0.7.3: 중앙 cropFraction 영역만 256² 로 샘플 (배경 제거, 가운데 물체 집중).
        // 중앙 crop 이라 Y flip 방향과 무관. ref 도 동일 crop 으로 인코딩되어야 함.
        float f = Mathf.Clamp(cropFraction, 0.2f, 1.0f);
        Graphics.Blit(texture, tmp, new Vector2(f, f), new Vector2((1f - f) * 0.5f, (1f - f) * 0.5f));
        RenderTexture.active = tmp;
        readbackTex.ReadPixels(new Rect(0, 0, INPUT_SIZE, INPUT_SIZE), 0, 0);
        readbackTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(tmp);

        Color32[] pixels = readbackTex.GetPixels32();
        int plane = INPUT_SIZE * INPUT_SIZE;
        // v1.2: NCHW 정규화와 같은 루프에서 중앙 박스 색 카운트 (별도 readback 금지).
        float cs = Mathf.Clamp(colorSampleFraction, 0.4f, 1.0f);
        int colMargin = (int)(INPUT_SIZE * (1f - cs) * 0.5f);
        int lo = colMargin, hi = INPUT_SIZE - colMargin;
        int m = colorMargin;
        int red = 0, blue = 0, sampled = 0;
        // v1.3: 중앙 박스 평균색 누적 (Color32 r/g/b 는 0-255 byte). long 으로 overflow 방지.
        long sumR = 0, sumG = 0, sumB = 0;
        for (int i = 0; i < plane; i++)
        {
            // (pixel - mean) / std for each channel, NCHW layout
            inputBuffer[0 * plane + i] = (pixels[i].r / 255f - CLIP_MEAN[0]) / CLIP_STD[0];
            inputBuffer[1 * plane + i] = (pixels[i].g / 255f - CLIP_MEAN[1]) / CLIP_STD[1];
            inputBuffer[2 * plane + i] = (pixels[i].b / 255f - CLIP_MEAN[2]) / CLIP_STD[2];

            // 중앙 colorSampleFraction 박스 안의 픽셀만 색 우세 카운트
            int x = i % INPUT_SIZE, y = i / INPUT_SIZE;
            if (x < lo || x >= hi || y < lo || y >= hi) continue;
            int r = pixels[i].r, g = pixels[i].g, b = pixels[i].b;
            if (r > g + m && r > b + m) red++;
            else if (b > r + m && b > g + m) blue++;
            // v1.3: 같은 박스 픽셀의 채널 합 누적 → 루프 후 평균색 산출.
            sumR += r; sumG += g; sumB += b;
            sampled++;
        }
        lastColorValid = sampled > 0;
        lastRedRatio = lastColorValid ? (float)red / sampled : 0f;
        lastBlueRatio = lastColorValid ? (float)blue / sampled : 0f;
        // v1.3: 영역 평균색 (0-255 스케일). 면적 큰 색이 평균을 지배 → 펩시 파란 몸통이 평균을 파랑으로.
        lastMeanR = lastColorValid ? (float)sumR / sampled : 0f;
        lastMeanG = lastColorValid ? (float)sumG / sampled : 0f;
        lastMeanB = lastColorValid ? (float)sumB / sampled : 0f;
    }
}
