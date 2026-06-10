using UnityEngine;

// Eagle Eye v0.9.0 — OCR via EasyOCR (Qualcomm AI Hub w8a8 TFLite + QNN Delegate, Hexagon v73).
// MLKitOCR(CPU, ~10s latency) 대체. ClipExtractor 와 같은 init 패턴:
//   StreamingAssets → persistentDataPath 복사 → AndroidJavaObject EasyOCREngine.initialize.
public class OCRExtractor : MonoBehaviour
{
    [Header("상태")]
    public bool isReady;
    public string statusMessage = "초기화 중...";
    public string lastExtractedText = "";
    public long lastInferenceMs;

    [Header("EasyOCR 모델 (StreamingAssets)")]
    public string detectorFilename = "easyocr_detector.tflite";
    public string recognizerFilename = "easyocr_recognizer.tflite";
    public string charsetFilename = "easyocr_charset.txt";

    [Header("v0.7.3 회전 보정")]
    [Tooltip("-1: 자동 (v0.9.0 이전: WebCamTexture.videoRotationAngle. v0.9.0+: XRCameraHelper.getOrientation).\n" +
             "0/90/180/270: 강제 override (자동이 0 으로 잘못 보고할 때).")]
    public int rotationOverride = -1;

    [Header("v0.7.3 중앙 crop + 확대 (OCR 전처리)")]
    [Tooltip("프레임 중앙을 잘라 OCR. 1.0=전체, 0.9=거의 전체.\n" +
             "v0.7.5: 조준이 어려워 라벨이 가장자리에 자주 걸림 → 0.55→0.9 로 완화.\n" +
             "brand 는 키워드 매칭(\"pepsi\"/\"coca-cola\")이라 배경 글자 끼어도 안전.")]
    [Range(0.2f, 1.0f)] public float cropFraction = 0.9f;
    [Tooltip("crop 영역 업스케일 배수. 멀리 있어 작은 라벨의 인식률 ↑.")]
    [Range(1.0f, 4.0f)] public float upscaleFactor = 2.0f;

    [Tooltip("true: OCR 에 들어간 확대 이미지를 files/ocr_crops/ 에 저장 (디버그용 adb pull).")]
    public bool saveOcrInput = true;
    int _ocrSaveIdx;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject _ocr;
#endif

    void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(InitializeEasyOcr());
#else
        isReady = false;
        statusMessage = "OCR Android only";
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    System.Collections.IEnumerator InitializeEasyOcr()
    {
        string detSrc = System.IO.Path.Combine(Application.streamingAssetsPath, detectorFilename);
        string recSrc = System.IO.Path.Combine(Application.streamingAssetsPath, recognizerFilename);
        string charSrc = System.IO.Path.Combine(Application.streamingAssetsPath, charsetFilename);
        string detDst = System.IO.Path.Combine(Application.persistentDataPath, detectorFilename);
        string recDst = System.IO.Path.Combine(Application.persistentDataPath, recognizerFilename);
        string charDst = System.IO.Path.Combine(Application.persistentDataPath, charsetFilename);

        yield return CopyAsset(detSrc, detDst, "detector");
        yield return CopyAsset(recSrc, recDst, "recognizer");
        yield return CopyAsset(charSrc, charDst, "charset");

        string nativeLibDir;
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var appInfo = activity.Call<AndroidJavaObject>("getApplicationInfo"))
        {
            nativeLibDir = appInfo.Get<string>("nativeLibraryDir");
        }

        try
        {
            _ocr = new AndroidJavaObject("com.eagleeye.ocr.EasyOCREngine");
            bool ok = _ocr.Call<bool>("initialize", detDst, recDst, charDst, nativeLibDir);
            if (!ok)
            {
                statusMessage = "❌ EasyOCR init 실패";
                Debug.LogError("[OCRExtractor] " + statusMessage);
                yield break;
            }
            isReady = true;
            statusMessage = "✅ EasyOCR ready (NPU)";
            Debug.Log("[OCRExtractor] " + statusMessage);
        }
        catch (System.Exception e)
        {
            statusMessage = "❌ EasyOCR init 예외: " + e.Message;
            Debug.LogError("[OCRExtractor] " + statusMessage);
        }
    }

    System.Collections.IEnumerator CopyAsset(string srcPath, string dstPath, string label)
    {
        if (System.IO.File.Exists(dstPath) && new System.IO.FileInfo(dstPath).Length > 0)
        {
            Debug.Log($"[OCRExtractor] {label} 이미 복사됨: {dstPath}");
            yield break;
        }
        statusMessage = $"{label} 복사 중...";
        var req = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
        yield return req.SendWebRequest();
        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            statusMessage = $"❌ {label} 복사 실패: {req.error}";
            Debug.LogError("[OCRExtractor] " + statusMessage);
            yield break;
        }
        System.IO.File.WriteAllBytes(dstPath, req.downloadHandler.data);
        Debug.Log($"[OCRExtractor] {label} 복사: {dstPath} ({req.downloadHandler.data.Length} bytes)");
    }
#endif

    /// <summary>
    /// Camera frame → text. 동기 호출, EasyOCR NPU 기준 ~200-400ms 목표.
    /// 실패 시 빈 문자열.
    /// v0.9.0: WebCamTexture → ShareCamera Texture2D. 인자 type 을 base Texture 로 일반화.
    /// </summary>
    public string ExtractText(Texture tex)
    {
        if (!isReady || tex == null || tex.width < 16) return "";

#if UNITY_ANDROID && !UNITY_EDITOR
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            byte[] jpg = BuildOcrJpg(tex, out int outW, out int outH);
            if (jpg == null) return "";

            if (saveOcrInput)
            {
                try
                {
                    string dir = System.IO.Path.Combine(Application.persistentDataPath, "ocr_crops");
                    System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir, $"ocr_{(++_ocrSaveIdx):D4}.jpg");
                    System.IO.File.WriteAllBytes(path, jpg);
                    Debug.Log($"[OCRExtractor] OCR 입력 이미지 저장: {path}");
                }
                catch (System.Exception e) { Debug.LogWarning("[OCRExtractor] OCR 이미지 저장 실패: " + e.Message); }
            }

            // v0.9.0: ShareCamera 는 videoRotationAngle 가 없음 → XRCameraHelper.getOrientation("0").
            int autoRot = 0;
            try { autoRot = com.rayneo.xr.extensions.XRCameraHelper.getOrientation("0"); } catch { }
            int rot = rotationOverride >= 0 ? rotationOverride : autoRot;

            string text = _ocr.Call<string>("recognize", jpg, rot);
            lastExtractedText = text ?? "";
            Debug.Log($"[OCRExtractor] rot={rot} crop={cropFraction:F2} up={upscaleFactor:F1} → {outW}x{outH} " +
                      $"(autoRot={autoRot}, override={rotationOverride}) text='{(text ?? "").Replace('\n', ' ')}'");
            lastInferenceMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 /
                              System.Diagnostics.Stopwatch.Frequency;
            return lastExtractedText;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[OCRExtractor] ExtractText fail: " + e.Message);
            return "";
        }
#else
        return "";
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// 프레임 중앙을 cropFraction 만큼 잘라 upscaleFactor 배 확대 → JPG.
    /// 배경 글자 제거 + 작은 라벨 인식률 보강. crop 은 회전과 무관 (중심 기준).
    /// v0.9.0: 임의 Texture 입력 → GPU readback. WebCamTexture.GetPixels32 의존 제거.
    /// </summary>
    byte[] BuildOcrJpg(Texture tex, out int outW, out int outH)
    {
        outW = outH = 0;
        int w = tex.width, h = tex.height;

        // 1) full → RGBA32 readable Texture2D via Graphics.Blit
        var fullRT = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(tex, fullRT);

        float frac = Mathf.Clamp(cropFraction, 0.2f, 1.0f);
        int cw = Mathf.Max(16, Mathf.RoundToInt(w * frac));
        int ch = Mathf.Max(16, Mathf.RoundToInt(h * frac));
        int x0 = (w - cw) / 2, y0 = (h - ch) / 2;

        // 2) crop: ReadPixels 로 fullRT 의 중앙 영역만 cropTex 로 복사
        var prevActive = RenderTexture.active;
        RenderTexture.active = fullRT;
        var cropTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
        cropTex.ReadPixels(new Rect(x0, y0, cw, ch), 0, 0);
        cropTex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(fullRT);

        // 3) upscale via Graphics.Blit (bilinear)
        float up = Mathf.Clamp(upscaleFactor, 1.0f, 4.0f);
        outW = Mathf.RoundToInt(cw * up);
        outH = Mathf.RoundToInt(ch * up);

        var rt = RenderTexture.GetTemporary(outW, outH, 0, RenderTextureFormat.ARGB32);
        var prevFilter = cropTex.filterMode;
        cropTex.filterMode = FilterMode.Bilinear;
        Graphics.Blit(cropTex, rt);

        RenderTexture.active = rt;
        var outTex = new Texture2D(outW, outH, TextureFormat.RGB24, false);
        outTex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0);
        outTex.Apply();
        RenderTexture.active = prevActive;

        cropTex.filterMode = prevFilter;
        RenderTexture.ReleaseTemporary(rt);
        Destroy(cropTex);

        byte[] jpg = outTex.EncodeToJPG(85);
        Destroy(outTex);
        return jpg;
    }
#endif

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try { _ocr?.Call("release"); } catch { }
#endif
    }
}
