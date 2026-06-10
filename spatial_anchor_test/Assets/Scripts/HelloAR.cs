using System.Collections.Generic;
using UnityEngine;

// Eagle Eye PoC v0.2.3
// - Stereo SBS 렌더링
// - GyroTrigger (one-shot)
// - CameraPreview (라이브)
// - YoloDetector (라이브 프레임에 주기적 추론, CPU)
//
// 동작:
//   카메라 프레임을 매 inferenceIntervalSec마다 YOLO에 투입
//   결과 박스를 카메라 배경 위에 라이브 갱신
//   회전은 카메라 텍스처와 박스에 동시 적용 (같은 좌표계 유지)
//
// 알려진 제약:
//   - YOLO 입력 텐서는 320×320, 카메라 1280×720 → 비율 무시하고 stretch (v0.2.3 단순화)
//   - 카메라 sensor orientation으로 추론 → 회전된 객체 인식률 ↓ (v0.2.3에서 RenderTexture로 사전 회전)
//   - CPU 추론 → 300~800ms 예상 (NPU 가속은 v0.2.3)
public class HelloAR : MonoBehaviour
{
    [Header("v0.2.3 추론 빈도")]
    [Tooltip("YOLO 호출 간격 (초). 너무 짧으면 CPU 부담")]
    public float inferenceIntervalSec = 1.0f;

    [Header("v0.3.7 진짜 AR 모드")]
    [Tooltip("false = 카메라 영상 안 띄움, 검은 배경 + bbox만 (웨이브가이드 투명 → 현실 직접 보임)\n" +
             "true = 기존 pass-through (카메라 영상 + bbox 합성, VR 같은 화면)")]
    public bool showCameraPreview = false;

    GyroTrigger gyro;
    CameraPreview cam;
    QnnYoloDetector yolo;
    ClipExtractor clip;
    ProductMatcher matcher;
    AdRenderer ad;
    SpatialAnchorTest spatial;   // v0.8: 객체 옆 3D world-anchored 영상 광고
    AmbientInterestProfile aip;
    OCRExtractor ocr;

    List<Detection> detections = new List<Detection>();
    float lastInferenceTime = -10f;
    bool inferenceInFlight;

    float triggerFlashUntil = -1f;

    [Header("v0.5.0 Detection pipeline mode")]
    [Tooltip("true = CLIP only (YOLO skip, 매 트리거마다 CLIP. 비교용)\n" +
             "false = YOLO 1차 필터 + det > 0 시 CLIP (default, 발열 절약)")]
    public bool clipOnlyMode = false;

    [Header("v0.9.1 광고 표시 후 ShareCamera reopen 지연")]
    [Tooltip("매칭 후 N초 동안 ShareCamera off (SLAM 의 RGB pipeline 자유 → quad world-anchored). \n"+
             "이 시간 후 자동 reopen → DETECTING state 복귀.")]
    public float adShowSeconds = 10f;

    [Header("v0.5.0 CLIP 매칭 임계값")]
    [Tooltip("v0.7.3: 0.45. 온디바이스 CLIP sim 이 Mac 대비 ~0.3 낮게 나오고(전처리/양자화 차이),\n" +
             "중앙 crop 적용 후 환경 무관 매칭이라 0.45 로 완화 (coke 온디바이스 0.53~0.55 통과).")]
    [Range(0f, 1f)] public float clipMatchThreshold = 0.45f;

    GUIStyle big, small, flash, statusStyle, boxLabelStyle;
    Texture2D boxTex;

    void Awake()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            mainCam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = Color.black;

        gyro = gameObject.AddComponent<GyroTrigger>();
        gyro.OnTrigger += HandleTrigger;
        // v0.5.12: 앱 startup 시 sensor calibration 노이즈로 첫 trigger 까지 2분+ 지연 문제 fix.
        // threshold 완화 (0.3 → 0.5 rad/s) + duration 단축 (2.0 → 1.0초).
        // 안경 정지 상태 sensor noise 흡수, 첫 trigger ~1초 후 발화 기대.
        gyro.stableThreshold = 0.5f;
        gyro.stableDuration = 1.0f;
        // v0.8.2: RayNeo 정지 시 gyro 가 정확히 (0,0,0) stuck → oneShot 모드에서
        // firedInThisStableWindow 영구 true → 첫 trigger 후 재발화 안 됨. cooldown 모드로 전환.
        gyro.oneShotPerStableWindow = false;
        gyro.triggerCooldown = 5.0f;

        // v0.5.10: Inspector 직렬화 값이 코드 default 를 override 하는 Unity 동작 차단.
        // 매 Awake 에서 강제 적용. Inspector 로 변경하려면 이 줄을 주석 처리.
        // v0.7.5 fix: 0.55 → 0.45. 온디바이스 CLIP sim 이 Mac 대비 ~0.3 낮아 0.55 면
        //   중앙 crop 후에도 cola 가 0.53~0.58 로 경계에 걸림. (v0.7.4 는 이 줄이 0.55 라
        //   field default 0.45 가 무시되던 버그 — 실제론 0.55 로 돌고 있었음.)
        clipMatchThreshold = 0.45f;

        // v0.5.11: CLIP-only 모드 강제. YOLO 우회 (안경 시점 class 분류 신뢰성 낮은 문제).
        // 매 trigger 마다 frame 통째 → CLIP → best match. 배터리 ↑ but 매칭률 ↑ 기대.
        clipOnlyMode = true;

        cam = gameObject.AddComponent<CameraPreview>();
        // v0.7.3: CLIP-only 모드면 YOLO 컴포넌트 자체를 안 붙임 → 앱 시작 시 QNN 이 yolo11l
        //   그래프(10000+ 노드, w8a8 640²)를 Hexagon 용으로 컴파일하던 수십 초 지연 제거.
        //   런타임에 YOLO 추론을 안 하므로(clipOnlyMode) 동작에 영향 없음.
        //   YOLO+CLIP 모드로 되돌리면 (clipOnlyMode=false) 자동으로 다시 init.
        if (!clipOnlyMode)
            yolo = gameObject.AddComponent<QnnYoloDetector>();
        clip = gameObject.AddComponent<ClipExtractor>();
        matcher = gameObject.AddComponent<ProductMatcher>();
        matcher.minSimilarity = clipMatchThreshold;
        // v0.8.5: RayNeo 에서 MLKit OCR 5초씩 걸리며 text='' 일관 → STAGE2 영구 FAIL.
        // CLIP brand-specific fallback ON 으로 demo escape. OCR fix 후에도 안전망으로 유지.
        matcher.enableClipBrandFallback = true;
        // v0.5.15: topK=1 (가장 가까운 ref 만 사용). environment-aligned + laptop fairness.
        // top-3 평균이 broad-coverage refs 에 유리 → coke 시연 시 pepsi 잡는 문제 해결 시도.
        matcher.topK = 1;
        ad = gameObject.AddComponent<AdRenderer>();
        // v1.0: B8 에선 scene 의 SpatialAnchorHost 가 이미 SpatialAnchorTest 를 갖고 HelloAR 를 AddComponent 함.
        //   기존 인스턴스 재사용 — 두 번째 AddComponent 면 SLAM 구독·anchor·HUD 가 이중 spawn 됨.
        spatial = GetComponent<SpatialAnchorTest>();
        if (spatial == null) spatial = gameObject.AddComponent<SpatialAnchorTest>();
        aip = gameObject.AddComponent<AmbientInterestProfile>();
        ocr = gameObject.AddComponent<OCRExtractor>();

        Debug.Log($"[HelloAR] Init complete (v0.5.1). pipeline mode={(clipOnlyMode ? "CLIP-only" : "YOLO+CLIP")}");
    }

    // v1.0 conquest 매핑: 인식 brand → 경쟁사 광고 mp4 (StreamingAssets/db/ads_video/ 상대경로).
    // brand.name 은 metadata.json 의 정확한 문자열. 매핑 없으면 호출부가 brand 자체 ad_image 로 fallback.
    static readonly System.Collections.Generic.Dictionary<string, string> CompetitorAdVideo =
        new System.Collections.Generic.Dictionary<string, string>
    {
        { "coca-cola", "db/ads_video/pepsi_bottle_ad.mp4" },   // 코크 인식 → 펩시 광고
        { "pepsi",     "db/ads_video/coke_bottle_ad.mp4"  },   // 펩시 인식 → 코크 광고
    };

    bool pendingInference;
    float lastTriggerTime;   // v0.3.6 진단: 트리거 발화 ~ 추론 사이 lag 측정

    void Update()
    {
        if (!pendingInference) return;
        if (cam == null || !cam.isReady || cam.webCamTex == null) return;
        if (cam.webCamTex.width < 16) return;

        // YOLO 필요 (clipOnlyMode 아닐 때만)
        if (!clipOnlyMode && (yolo == null || !yolo.isReady)) return;
        if (clip == null || !clip.isReady) return;
        if (matcher == null || !matcher.isReady) return;

        pendingInference = false;
        lastInferenceTime = Time.time;
        float triggerToInferLag = Time.time - lastTriggerTime;

        // v0.5.16: 매 trigger 시 webCamTex frame 저장 (ref 만들기용)
        SaveCurrentFrame();

        // ───── Stage 1: YOLO (1차 필터, 발열 절약) ─────
        int yoloDetCount = 0;
        long yoloMs = 0;
        if (!clipOnlyMode)
        {
            detections = yolo.Detect(cam.webCamTex);
            yoloDetCount = detections.Count;
            yoloMs = yolo.lastInferenceMs;
            if (yoloDetCount == 0)
            {
                Debug.Log($"[HelloAR] Trigger: YOLO 0 det in {yoloMs}ms → CLIP skip (배터리 절약)");
                return;
            }
        }
        else
        {
            detections = new List<Detection>();  // CLIP only 면 박스 없음
        }

        // ───── Stage 2: CLIP embedding ─────
        long clipT0 = System.Diagnostics.Stopwatch.GetTimestamp();
        float[] embedding = clip.Embed(cam.webCamTex);
        long clipMs = (System.Diagnostics.Stopwatch.GetTimestamp() - clipT0) * 1000 /
                      System.Diagnostics.Stopwatch.Frequency;
        if (embedding == null || embedding.Length == 0)
        {
            Debug.LogWarning("[HelloAR] CLIP embedding 실패");
            return;
        }

        // ───── Stage 3a: CLIP category 분류 (먼저 "무슨 물체") ─────
        var category = matcher.MatchCategory(embedding, out float catScore);

        // category 미매칭이면 OCR 도 skip (낭비 방지) — v0.7.3 구조.
        ProductMatcher.MatchResult result = null;
        string ocrText = "";
        long ocrMs = 0;
        if (category != null)
        {
            // ───── Stage 3b: OCR — category 매칭된 것만 라벨 글자 추출 ─────
            if (ocr != null && ocr.isReady)
            {
                long ocrT0 = System.Diagnostics.Stopwatch.GetTimestamp();
                ocrText = ocr.ExtractText(cam.webCamTex);
                ocrMs = (System.Diagnostics.Stopwatch.GetTimestamp() - ocrT0) * 1000 /
                        System.Diagnostics.Stopwatch.Frequency;
            }

            // ───── Stage 3c: category 안에서 OCR brand 확정 ─────
            var brand = matcher.ResolveBrand(category, embedding, ocrText,
                                             out string brandSrc, out float brandScore);
            if (brand != null)
                result = new ProductMatcher.MatchResult {
                    category = category, brand = brand, categoryScore = catScore,
                    brandSource = brandSrc, brandScore = brandScore,
                };
        }

        // v0.5.9: YOLO detection 의 class+conf 로그 (false positive 진단)
        string yoloInfo = "(clip-only)";
        if (detections != null && detections.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < detections.Count; k++)
            {
                var d = detections[k];
                if (k > 0) sb.Append(',');
                sb.Append($"{d.Label}({d.confidence:F2})");
            }
            yoloInfo = sb.ToString();
        }

        // ───── Stage 4: Ad rendering — brand 의 ad ─────
        if (result != null)
        {
            // v1.0 conquest: 인식 brand → 경쟁사 광고 mp4. 매핑 없으면 brand 자체 ad_image 에서 파생.
            string vidPath;
            string bname = result.brand.name != null ? result.brand.name.ToLowerInvariant() : "";
            if (!CompetitorAdVideo.TryGetValue(bname, out vidPath))
            {
                vidPath = (result.brand.ad_image ?? "")
                    .Replace("db/ads/", "db/ads_video/")
                    .Replace("_ad.png", "_ad.mp4");
            }
            // 2D HUD 대체 → 3D world-anchored 영상 spawn (가장 큰 bbox 옆에 객체와 같은 depth)
            int W = (cam != null && cam.webCamTex != null) ? cam.webCamTex.width  : QnnYoloDetector.INPUT_SIZE;
            int H = (cam != null && cam.webCamTex != null) ? cam.webCamTex.height : QnnYoloDetector.INPUT_SIZE;
            spatial.ShowAdBesideMatch(vidPath, result, detections, W, H);
            Debug.Log($"[HelloAR] ✅ MATCH yolo={yoloDetCount}/{yoloMs}ms [{yoloInfo}] clip={clipMs}ms ocr={ocrMs}ms → " +
                      $"category={result.category.name}({result.categoryScore:F2}) brand={result.brand.name} → spatial '{vidPath}'");
            // v0.9.1: 광고 spawn 즉시 ShareCamera close → SLAM RGB pipeline 회복 → quad world-anchored.
            // adShowSeconds 후 자동 reopen (DETECTING state 복귀).
            if (cam != null) cam.CloseCamera();
            StartCoroutine(ReopenCameraAfter(adShowSeconds));
        }
        else
        {
            Debug.Log($"[HelloAR] no match yolo={yoloDetCount}/{yoloMs}ms [{yoloInfo}] clip={clipMs}ms ocr={ocrMs}ms");
        }

        // ───── Stage 5: AIP 로깅 (v1 spec — schema 정의만, v2+ 에서 활용) ─────
        if (aip != null)
        {
            // v0.7.0: hierarchical 매칭 결과 누적. category + brand.
            string pname = result?.brand?.name;
            float psim = result?.categoryScore ?? -1f;
            if (detections != null && detections.Count > 0)
            {
                var top = detections[0];
                aip.Log(top.classId, top.Label, top.confidence, pname, psim);
            }
            else if (result != null)
            {
                aip.Log(-1, "(clip-only)", 0f, pname, psim);
            }
        }
    }

    System.Collections.IEnumerator ReopenCameraAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (cam != null && !cam.isReady && !cam.isOpening)
        {
            Debug.Log($"[HelloAR] ad timer expired → cam.OpenCamera (DETECTING state)");
            cam.OpenCamera();
        }
    }

    void HandleTrigger()
    {
        triggerFlashUntil = Time.time + 1.0f;
        pendingInference = true;
        lastTriggerTime = Time.time;
        Debug.Log("[HelloAR] Trigger received → NPU 추론 schedule");
    }

    // v0.5.16: trigger 마다 webCamTex 의 raw frame 을 persistentDataPath/captures/ 에 jpg 저장.
    // 시연 후 adb pull 로 추출 → pepsi 등 ref 환경-aligned 생성.
    // v0.9.0: webCamTex 가 WebCamTexture → ShareCamera Texture2D 로 바뀌어
    //   GetPixels32() 직접 호출 불가 (WebCamTexture 전용). RenderTexture readback 으로 통일.
    void SaveCurrentFrame()
    {
        if (cam == null || !cam.isReady || cam.webCamTex == null) return;
        if (cam.webCamTex.width < 16) return;

        try
        {
            int w = cam.webCamTex.width;
            int h = cam.webCamTex.height;

            // GPU readback: any Texture → RGB24 Texture2D via Graphics.Blit + ReadPixels.
            // ShareCamera 의 Texture2D 는 BGRA32 isReadable 일 수 있지만, 일관성 위해 RT 경유.
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(cam.webCamTex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = tex.EncodeToJPG(90);
            Destroy(tex);

            string dir = System.IO.Path.Combine(Application.persistentDataPath, "captures");
            System.IO.Directory.CreateDirectory(dir);
            int n = gyro != null ? gyro.totalTriggers : 0;
            string path = System.IO.Path.Combine(dir, $"frame_{n:D4}.jpg");
            System.IO.File.WriteAllBytes(path, jpg);
            Debug.Log($"[HelloAR] frame saved: {path} ({jpg.Length / 1024}KB)");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HelloAR] frame save 실패: {e.Message}");
        }
    }

    void EnsureStyles()
    {
        if (big != null) return;

        big = new GUIStyle();
        big.fontSize = 28;
        big.fontStyle = FontStyle.Bold;
        big.alignment = TextAnchor.MiddleCenter;
        big.normal.textColor = Color.white;

        small = new GUIStyle();
        small.fontSize = 14;
        small.alignment = TextAnchor.MiddleCenter;
        small.normal.textColor = Color.white;

        flash = new GUIStyle();
        flash.fontSize = 36;
        flash.fontStyle = FontStyle.Bold;
        flash.alignment = TextAnchor.MiddleCenter;
        flash.normal.textColor = new Color(0.2f, 1f, 0.3f);

        statusStyle = new GUIStyle();
        statusStyle.fontSize = 12;
        statusStyle.alignment = TextAnchor.MiddleCenter;
        statusStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);

        boxLabelStyle = new GUIStyle();
        boxLabelStyle.fontSize = 14;
        boxLabelStyle.fontStyle = FontStyle.Bold;
        boxLabelStyle.normal.textColor = new Color(0.2f, 1f, 0.3f);
        boxLabelStyle.alignment = TextAnchor.UpperLeft;

        boxTex = new Texture2D(1, 1);
        boxTex.SetPixel(0, 0, new Color(0.2f, 1f, 0.3f));
        boxTex.Apply();
    }

    void OnGUI()
    {
        EnsureStyles();
        if (gyro == null) return;

        int halfW = Screen.width / 2;
        int h = Screen.height;

        DrawEye(0, 0, halfW, h);
        DrawEye(halfW, 0, halfW, h);
    }

    void DrawEye(int x0, int y0, int w, int h)
    {
        Rect eyeRect = new Rect(x0, y0, w, h);

        // v0.4.2: 회전 적용 안 함
        // 모델 input 이 이미 정상 orientation 으로 회전 보정됨 (PreprocessTexture)
        // → 박스 좌표도 정상 orientation 기준
        // → 화면도 정상 orientation (안경 디스플레이 landscape)
        // → GUI 회전 X
        if (cam != null && cam.isReady && cam.webCamTex != null)
        {
            if (showCameraPreview)
                GUI.DrawTexture(eyeRect, cam.webCamTex, ScaleMode.StretchToFill);
            DrawDetections(x0, y0, w, h);
        }
        else if (showCameraPreview)
        {
            GUI.DrawTexture(eyeRect, Texture2D.blackTexture);
        }

        // === 회전 안 된 좌표계 (UI 텍스트) ===
        int cx = x0 + w / 2;
        int cy = y0 + h / 2;
        int labelW = Mathf.Min(w - 40, 600);
        int xOff = cx - labelW / 2;

        GUI.Label(new Rect(xOff, y0 + 16, labelW, 32), "Eagle Eye v0.2.3", big);

        string yoloStatus = yolo != null ? yolo.statusMessage : "(yolo none)";
        string camStatus = cam != null ? cam.statusMessage : "(cam none)";
        GUI.Label(new Rect(xOff, y0 + 50, labelW, 18),
            $"yolo: {yoloStatus}", statusStyle);
        GUI.Label(new Rect(xOff, y0 + 68, labelW, 18),
            $"  → {detections.Count} det / {yolo?.lastInferenceMs}ms / interval {inferenceIntervalSec:F1}s", statusStyle);
        GUI.Label(new Rect(xOff, y0 + 86, labelW, 18),
            $"cam: {camStatus}", statusStyle);

        Vector3 g = gyro.currentGyro;
        int yBottom = y0 + h - 76;
        GUI.Label(new Rect(xOff, yBottom + 0,  labelW, 18),
            $"gyro: ({g.x:F2}, {g.y:F2}, {g.z:F2})", small);
        GUI.Label(new Rect(xOff, yBottom + 18, labelW, 18),
            $"stable: {gyro.isStable}  elapsed: {gyro.stableElapsed:F1}s", small);
        GUI.Label(new Rect(xOff, yBottom + 36, labelW, 18),
            $"triggers: {gyro.totalTriggers}", small);

        if (Time.time < triggerFlashUntil)
        {
            GUI.Label(new Rect(xOff, cy + 80, labelW, 50), "▶ TRIGGER ◀", flash);
        }
    }

    // YOLO 박스를 화면 좌표로 변환.
    // 박스는 YOLO INPUT_SIZE(320×320) 기준 → DrawEye의 (x0, y0, w, h) 영역으로 stretch 매핑.
    // 호출 시점은 카메라 텍스처와 동일한 GUI.matrix 상태(회전 적용)에서.
    // v0.3.7 진단: 박스 표시 max 2초 (stale 박스 인지 위해)
    const float BOX_DISPLAY_DURATION = 2.0f;

    void DrawDetections(int x0, int y0, int w, int h)
    {
        if (detections == null || detections.Count == 0) return;
        float age = Time.time - lastInferenceTime;
        if (age > BOX_DISPLAY_DURATION) return;   // 2초 지나면 박스 사라짐

        float scaleX = (float)w / QnnYoloDetector.INPUT_SIZE;
        float scaleY = (float)h / QnnYoloDetector.INPUT_SIZE;

        foreach (var d in detections)
        {
            float bx = x0 + (d.xCenter - d.width / 2) * scaleX;
            float by = y0 + (d.yCenter - d.height / 2) * scaleY;
            float bw = d.width * scaleX;
            float bh = d.height * scaleY;

            DrawBoxOutline(bx, by, bw, bh, 2);
            GUI.Label(new Rect(bx + 2, by - 18, 200, 18),
                $"{d.Label} {d.confidence:F2} [{age:F1}s]", boxLabelStyle);
        }
    }

    void DrawBoxOutline(float x, float y, float w, float h, float thickness)
    {
        GUI.DrawTexture(new Rect(x, y, w, thickness), boxTex);
        GUI.DrawTexture(new Rect(x, y + h - thickness, w, thickness), boxTex);
        GUI.DrawTexture(new Rect(x, y, thickness, h), boxTex);
        GUI.DrawTexture(new Rect(x + w - thickness, y, thickness, h), boxTex);
    }
}
