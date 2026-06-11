using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

// SpatialAnchorTest v2 — RayNeo OpenXR ARDK 6DoF SLAM, 즉시 spawn + 점진 refine 패턴.
//
// v1 (이전): SLAM converge wait → tracked 후 spawn. OpenXR stereo 에서 OnGUI invisible
//            이라 진단 불가 → 8분 logcat 만 보고 답답.
// v2 (현재): 즉시 spawn (provisional anchor). SLAM converge 도달 시 1회 reposition.
//            World-space TextMesh HUD — quad 옆 띄움. stereo display 자동 호환.
public class SpatialAnchorTest : MonoBehaviour
{
    [Header("Anchor")]
    public float anchorDistanceM = 0.5f;
    public float quadWidthM = 0.30f;

    [Header("v1.5 광고 배치")]
    public float adDistanceM = 1.2f;     // 광고 거리 — 물체 0.5m 보다 멀게
    public float adQuadWidthM = 0.22f;   // 광고 quad 폭 — 0.30 보다 작게
    // v1.6: 광고 텍스처 좌우 미러. 정면배치(LookRotation -camFwd)에선 거울상 아님 → 기본 false(미러 없음).
    //   (이전 옆배치 땐 true 가 맞았으나 정면 전환으로 facing 손방향이 뒤집혀 과교정됐었음.)
    public bool adMirrorX = false;

    [Header("HUD (head-locked 2D overlay)")]
    // v1.5: HUD 를 월드 고정 → 카메라 parent head-lock 으로 전환.
    //   SLAM converge 후 월드 고정되면 시선 돌릴 때 드리프트해 시야 밖으로 나가던 문제 해결.
    //   카운터는 상단 좌측, SLAM 진단은 상단 우측. parent 이후 head pose 따라 자동으로 따라옴.
    public float hudCharSize = 0.0015f;
    public int hudFontSize = 80;
    // 상단 좌(카운터)/우(SLAM) localPosition. x 음수=좌, 양수=우 / y 양수=상단 / z=전방거리(1.0~1.5m).
    public Vector3 hudCountersLocalPos = new Vector3(-0.35f, 0.28f, 1.2f);   // 상단 좌측
    public Vector3 hudDiagLocalPos     = new Vector3( 0.35f, 0.28f, 1.2f);   // 상단 우측
    // 거울상 교정: 카메라 parent 후 텍스트가 정상으로 읽히게 하는 localRotation.
    //   기본 (0,180,0) — RayNeo stereo 에서 거울상이면 on-device 에서 조정.
    //   hudMirror=false 면 회전 없이(identity) — 토글로 어느 쪽이 정상인지 확인.
    public bool hudMirror = true;
    public Vector3 hudLocalEuler = new Vector3(0f, 180f, 0f);

    [Header("Resources")]
    public string textureResourceName = "cola_anchor";
    [Tooltip("v1.2: conquest 데모는 provisional 마커(cola_anchor) 숨김 — 경쟁사 광고 1개만 표시. true 면 SLAM 디버그 마커 표시.")]
    public bool showAnchorMarker = false;

    // bisection: helloar component 추가 case. B0=baseline only, B1=+Gyro, B2=+Camera (ShareCamera),
    // B3=+Clip, B4=+OCR, B5=+Gyro+Camera, B6=+Gyro+Clip, B7=+Camera+Clip, B8=full helloar.
    public string bisectionCase = "B8";

    // ----- state -----
    Camera xrCam;
    GameObject anchorQuad;
    // v1.4: 경쟁사 광고 quad 를 단일 교체에서 최대 maxAds 개 누적(FIFO)으로 변경.
    //   초과 시 가장 오래된 것부터 제거 → 동시에 여러 경쟁사 비교 표시 가능.
    List<GameObject> adQuads = new List<GameObject>();
    public int maxAds = 2;
    // b25 color-video: 각 광고 quad 가 자기 RenderTexture 를 소유 (per-quad). max-2 FIFO 에서
    //   동시 2개 영상이 가능하도록 adQuads 와 1:1 정렬된 RenderTexture 리스트로 관리.
    //   VideoPlayer 는 해당 quad GameObject 에 직접 AddComponent → quad Destroy 시 함께 사라짐.
    //   RenderTexture 는 GC 안 되므로 FIFO 제거/OnDestroy 에서 명시적으로 Release().
    List<RenderTexture> adRTs = new List<RenderTexture>();
    // 광고 영상 mp4 의 StreamingAssets→파일경로 복사 캐시 (한 번 복사하면 재사용).
    Dictionary<string, string> _videoLocalPaths = new Dictionary<string, string>();
    // v1.5: HUD 를 카운터(상단 좌) + SLAM 진단(상단 우) 2개 TextMesh 로 분리, 둘 다 camera 에 parent (head-lock).
    TextMesh hudCounters;
    GameObject hudCountersObj;
    TextMesh hudDiag;
    GameObject hudDiagObj;
    ClipExtractor clipExt;                       // v1.6: CLIP 컴파일 완료 플래그용 (같은 GameObject, lazily)
    public float monitorLogInterval = 0.5f;      // v1.6 MONITOR: [MONITOR] 로그 주기(초) — eagle-monitor 스킬이 파싱
    float lastMonitorEmit = -1f;

    // v1.4: HUD 카운터 표시용 HelloAR 참조 (같은 GameObject). lazily 획득 + null-safe.
    HelloAR helloAr;

    float spawnTime;
    Vector3 anchorWorldPos;
    Vector3 anchorAtPlacementCamPos;
    Vector3 anchorAtPlacementCamFwd;

    int lastSlamStatus = -1;
    // v1.1: SLAM 발산 감지 + 콘텐츠 재앵커. 근거: ShareCamera preview 열린 채 pause 시
    //   camera provider SIGPIPE 사망 → SLAM 발산 (camPos 595m 관측, 06-11 00:10:44).
    //   복구 로직 없으면 콘텐츠가 시야에서 영구 소실.
    float divergedSince = -1f;
    int divergenceRecoveries;
    float lastReanchorTime = -999f;
    bool slamEnableCalled;
    float slamSeekStartTime = -1f;
    float slamConvergeSeconds = -1f;
    bool repositionedOnConverge = false;

    int frameCount;
    float fpsSampleStart;
    float fpsLast;
    Vector3 prevCamPos;
    Vector3 prevCamEuler;
    float prevSampleTime;
    Vector3 linearVel;
    Vector3 angularVelDps;

    // DIAG: HeadTrackedPoseDriver.OnPostUpdate 로 전달되는 ground-truth pose 추적
    Pose lastHeadPose;
    int headPoseCallCount;

    // DIAG2: 네이티브 head tracker pose 직접 폴링 (centerEye 라우팅 우회).
    // nonzero 면 SLAM 은 pose 생산 중인데 centerEye 라우팅이 깨진 것 → 직접 구동으로 우회 가능.
    float[] htPos = new float[3];
    float[] htRot = new float[4];
    int htRet = -999;

    void Awake()
    {
        xrCam = Camera.main;
        if (xrCam == null)
            Debug.LogError("[SpatialAnchorTest] Camera.main 없음");

        // legacy StandaloneInputModule 비활성화 (new InputSystem only 환경에서 매 frame 예외 방지)
        foreach (var m in FindObjectsOfType<UnityEngine.EventSystems.StandaloneInputModule>())
        {
            m.enabled = false;
            Debug.Log($"[SpatialAnchorTest] disabled StandaloneInputModule on {m.gameObject.name}");
        }

        fpsSampleStart = Time.unscaledTime;
        prevSampleTime = Time.unscaledTime;
    }

    void Start()
    {
#if !UNITY_EDITOR
        // vendor SlamDemoCtrl.Start() 와 동일한 최소 시퀀스: EnableSlamHeadTracker + OnPostUpdate 구독만.
        // 이전의 InitializeLoaderSync/StartSubsystems(런타임 XR 재초기화) 와 EnablePlaneDetection 은
        // vendor 가 하지 않는 divergence 라 제거 — XR 은 auto-init(automaticLoading=1) 로 이미 떠 있음.
        try
        {
            com.rayneo.xr.extensions.XRInterfaces.EnableSlamHeadTracker();
            slamEnableCalled = true;
            Debug.Log("[SpatialAnchorTest] EnableSlamHeadTracker() called.");
        }
        catch (System.Exception e) { Debug.LogError($"[SpatialAnchorTest] EnableSlam: {e.Message}"); }

        // vendor 패턴 — HeadTrackedPoseDriver.OnPostUpdate 구독 (pose ground-truth).
        try
        {
            RayNeo.HeadTrackedPoseDriver.OnPostUpdate += OnHeadPose;
            var cams = FindObjectsOfType<Camera>();
            int mainTagged = 0;
            foreach (var c in cams) if (c.CompareTag("MainCamera")) mainTagged++;
            var drivers = FindObjectsOfType<RayNeo.HeadTrackedPoseDriver>();
            bool mainHasDriver = xrCam != null && xrCam.GetComponent<RayNeo.HeadTrackedPoseDriver>() != null;
            Debug.Log($"[SpatialAnchorTest][DIAG] Camera.main={(xrCam != null ? xrCam.gameObject.name : "NULL")} totalCams={cams.Length} mainTagged={mainTagged} HeadTrackedPoseDrivers={drivers.Length} mainHasDriver={mainHasDriver}");
        }
        catch (System.Exception e) { Debug.LogError($"[SpatialAnchorTest][DIAG] OnPostUpdate subscribe: {e.Message}"); }
#endif

        slamSeekStartTime = Time.time;

        // 즉시 provisional anchor + HUD spawn. SLAM 미수렴 상태에서도 visible.
        SpawnProvisional();

        // bisection: case 별 helloar component conditional AddComponent.
#if !UNITY_EDITOR
        Debug.Log($"[Bisection] case={bisectionCase}");
        try
        {
            switch (bisectionCase)
            {
                case "B0": break;
                case "B1": gameObject.AddComponent<GyroTrigger>(); break;
                case "B2": gameObject.AddComponent<CameraPreview>(); break;
                case "B3": gameObject.AddComponent<ClipExtractor>(); break;
                case "B4": gameObject.AddComponent<OCRExtractor>(); break;
                case "B5":
                    gameObject.AddComponent<GyroTrigger>();
                    gameObject.AddComponent<CameraPreview>();
                    break;
                case "B6":
                    gameObject.AddComponent<GyroTrigger>();
                    gameObject.AddComponent<ClipExtractor>();
                    break;
                case "B7":
                    gameObject.AddComponent<CameraPreview>();
                    gameObject.AddComponent<ClipExtractor>();
                    break;
                case "B8":
                    gameObject.AddComponent<HelloAR>();
                    break;
            }
        }
        catch (System.Exception e) { Debug.LogError($"[Bisection] AddComponent: {e.Message}"); }
#endif
    }

    void OnHeadPose(Pose p)
    {
        lastHeadPose = p;
        headPoseCallCount++;
    }

    void OnDestroy()
    {
#if !UNITY_EDITOR
        try { RayNeo.HeadTrackedPoseDriver.OnPostUpdate -= OnHeadPose; } catch { }
#endif
        // b25: per-quad VideoPlayer + RenderTexture 전부 정지/해제 (leak 방지).
        foreach (var q in adQuads)
        {
            if (q == null) continue;
            var vp = q.GetComponent<VideoPlayer>();
            if (vp != null) { try { vp.Stop(); } catch { } }
            Destroy(q);
        }
        adQuads.Clear();
        foreach (var rt in adRTs) if (rt != null) rt.Release();
        adRTs.Clear();
    }

    void SpawnProvisional()
    {
        if (xrCam == null) return;

        Vector3 camPos = xrCam.transform.position;
        Vector3 camFwd = xrCam.transform.forward;
        Vector3 camUp  = xrCam.transform.up;
        Vector3 anchorPos = camPos + camFwd * anchorDistanceM;
        Quaternion anchorRot = Quaternion.LookRotation(-camFwd, camUp);

        anchorQuad = BuildImageQuad(anchorPos, anchorRot);
        // v1.2: conquest 데모에선 provisional 마커(cola_anchor) 숨김 — 경쟁사 광고(adQuad) 1개만.
        //   앵커 위치 추적/HUD drift 계산은 유지(quad 는 살아있고 invisible).
        if (!showAnchorMarker) { var amr = anchorQuad.GetComponent<MeshRenderer>(); if (amr != null) amr.enabled = false; }
        anchorWorldPos = anchorPos;
        anchorAtPlacementCamPos = camPos;
        anchorAtPlacementCamFwd = camFwd;
        spawnTime = Time.time;

        // v1.5: HUD — 카메라에 parent 한 head-locked 2D 오버레이 2개.
        //   카운터=상단 좌측, SLAM 진단=상단 우측. parent 가 head-lock 담당 → UpdateHud 는 텍스트만 갱신.
        hudCountersObj = BuildHudOverlay("HudCounters", hudCountersLocalPos, TextAnchor.UpperLeft, out hudCounters);
        hudDiagObj     = BuildHudOverlay("HudDiag",     hudDiagLocalPos,     TextAnchor.UpperLeft, out hudDiag);

        Debug.Log($"[SpatialAnchorTest] Provisional anchor spawned + head-locked HUD parented to {xrCam.gameObject.name}");
    }

    // v1.5: head-locked HUD 오버레이 1개 생성 — 카메라에 parent (head pose 따라 자동 이동).
    //   고정 localPosition(상단 좌/우) + localRotation(거울상 교정). 이후 텍스트만 갱신.
    GameObject BuildHudOverlay(string name, Vector3 localPos, TextAnchor anchor, out TextMesh tm)
    {
        GameObject obj = new GameObject(name);
        // SetParent(.., false) — worldPositionStays=false 로 local transform 그대로 적용.
        obj.transform.SetParent(xrCam.transform, false);
        obj.transform.localPosition = localPos;
        // 거울상 교정: parent(카메라) 정면을 향하되 텍스트가 정상으로 읽히게.
        //   hudMirror=true 면 hudLocalEuler(기본 0,180,0), false 면 회전 없음 (on-device 토글).
        obj.transform.localRotation = hudMirror ? Quaternion.Euler(hudLocalEuler) : Quaternion.identity;
        obj.transform.localScale = Vector3.one;

        tm = obj.AddComponent<TextMesh>();
        tm.text = "INIT";
        tm.fontSize = hudFontSize;
        tm.characterSize = hudCharSize;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Left;
        tm.color = Color.white;
        tm.richText = false;
        // TextMesh 의 default material 그대로 사용 — GUI/Text Shader 가 build 에서
        // stripped 된 경우 Shader.Find 가 null 이라 override 시도하면 throw.
        return obj;
    }

    GameObject BuildImageQuad(Vector3 pos, Quaternion rot)
    {
        Texture2D tex = Resources.Load<Texture2D>(textureResourceName);
        if (tex == null)
            Debug.LogError($"[SpatialAnchorTest] Resources/{textureResourceName} 못 찾음");

        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "TigerAnchorQuad";
        q.transform.position = pos;
        q.transform.rotation = rot;

        float aspect = tex != null ? (float)tex.height / Mathf.Max(1, tex.width) : 0.75f;
        q.transform.localScale = new Vector3(quadWidthM, quadWidthM * aspect, 1f);

        var mr = q.GetComponent<MeshRenderer>();
        var unlit = Shader.Find("Unlit/Texture");
        if (unlit == null) unlit = Shader.Find("Sprites/Default");
        if (unlit == null) unlit = Shader.Find("Standard");
        if (unlit != null)
        {
            var mat = new Material(unlit);
            if (tex != null) mat.mainTexture = tex;
            mr.material = mat;
        }
        else if (tex != null) mr.material.mainTexture = tex;

        var col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);
        return q;
    }

    // v1.0: 트리거 시점 응시 물체 옆에 world-anchored 경쟁사 광고 영상 spawn (conquest 데모 핵심).
    // HelloAR 전체 pipeline (B8) 에서만 호출됨. 6DoF 가 카메라를 구동하므로 quad 는 spawn 순간
    // world 좌표에 한 번만 고정 (head re-follow 안 함 → 진짜 world-anchored).
    public void ShowAdBesideMatch(string vidPath, ProductMatcher.MatchResult result,
                                  List<Detection> detections, int W, int H)
    {
        if (xrCam == null) return;

        // ── 트리거 시점 응시 지점 ≈ 인식된 물체 위치 (clipOnlyMode: bbox 없어 gaze-ray 근사) ──
        Vector3 camPos   = xrCam.transform.position;
        Vector3 camFwd   = xrCam.transform.forward;
        Vector3 camUp    = xrCam.transform.up;
        Vector3 camRight = xrCam.transform.right;
        Vector3 objectPos = camPos + camFwd * anchorDistanceM;             // 응시 물체 (앵커 마커용, 0.5m)
        // v1.3: 옆칸 배치가 헷갈려서 정면(응시 지점)에 띄움 — 경쟁사 광고를 시선 정중앙에 world-anchored.
        // v1.5: 광고를 정면 방향 유지하되 물체(0.5m)보다 멀리(adDistanceM) — 더 작고 멀게.
        Vector3 adPos     = camPos + camFwd * adDistanceM;                 // 정면, 물체보다 멀게
        Quaternion faceUser = Quaternion.LookRotation(-camFwd, camUp);     // spawn 시점 1회 (billboard X)

        // 앵커 마커(quad)를 응시 물체로 이동 (world 고정).
        if (anchorQuad != null)
        {
            anchorQuad.transform.position = objectPos;
            anchorQuad.transform.rotation = faceUser;
            anchorWorldPos = objectPos;
        }

        // 광고 quad spawn (world 고정).
        // v1.4: 단일 교체 대신 누적 — adQuads 에 추가 후 maxAds 초과분(FIFO, 앞=오래된 것) 제거.
        GameObject newQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        newQuad.name = "AdQuad";
        newQuad.transform.position = adPos;
        newQuad.transform.rotation = faceUser;
        newQuad.transform.localScale = new Vector3(adQuadWidthM, adQuadWidthM * 0.75f, 1f);
        var col = newQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var mr = newQuad.GetComponent<MeshRenderer>();

        // material 기본 셋업 (이미지 로드 완료 시 mainTexture 교체).
        var sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
        if (sh != null) mr.material = new Material(sh);

        adQuads.Add(newQuad);
        adRTs.Add(null);   // b25: adQuads 와 1:1 정렬 유지. 영상 prepare 완료 시 이 슬롯에 RT 할당.
        while (adQuads.Count > maxAds)
        {
            EvictAdQuad(0);
        }

        // b25 color-video: 정지 PNG 대신 영상(VideoPlayer→RenderTexture→quad) 경로.
        //   Android 에선 StreamingAssets mp4 가 APK 내부(jar:file://...!/assets/...)라 VideoPlayer 가
        //   재생 불가(크래시/무재생) → persistentDataPath 로 실제 파일 복사 후 file:// 로 재생.
        //   복사/재생 실패 시 정지 PNG 로 폴백 (크래시 금지).
        string pngPath = (vidPath ?? "").Replace("ads_video", "ads").Replace(".mp4", ".png");
        StartCoroutine(SetupAdVideo(vidPath, pngPath, newQuad, mr));

        string brandName = result != null && result.brand != null ? result.brand.name : "AD";
        Debug.Log($"[SpatialAnchorTest] ShowAdBesideMatch brand={brandName} vid='{vidPath}' png='{pngPath}' obj={objectPos} ad={adPos} (dets={(detections != null ? detections.Count : 0)})");
    }

    // b25: FIFO 제거 helper — quad 의 VideoPlayer 정지 + 정렬된 RenderTexture 해제 후 둘 다 리스트에서 제거.
    void EvictAdQuad(int idx)
    {
        if (idx < 0 || idx >= adQuads.Count) return;
        var q = adQuads[idx];
        if (q != null)
        {
            var vp = q.GetComponent<VideoPlayer>();
            if (vp != null) { try { vp.Stop(); } catch { } }
            Destroy(q);
        }
        if (idx < adRTs.Count)
        {
            if (adRTs[idx] != null) adRTs[idx].Release();
            adRTs.RemoveAt(idx);
        }
        adQuads.RemoveAt(idx);
    }

    // b25: StreamingAssets 의 mp4 를 file:// 로 재생 가능한 실제 경로로 복사한 뒤 VideoPlayer 셋업.
    //   - Android: UnityWebRequest.Get(jar URL) 로 바이트 읽어 persistentDataPath 로 write (ClipExtractor 패턴 미러).
    //   - Editor: File.Copy.
    //   복사 결과를 캐시 → 같은 mp4 두 번째부터는 즉시 재생.
    //   실패 시 정지 PNG 로 폴백.
    IEnumerator SetupAdVideo(string vidRelPath, string pngRelPath, GameObject quad, MeshRenderer mr)
    {
        if (string.IsNullOrEmpty(vidRelPath))
        {
            StartCoroutine(LoadCompetitorImage(pngRelPath, mr));
            yield break;
        }

        string localPath;
        if (!_videoLocalPaths.TryGetValue(vidRelPath, out localPath) || !System.IO.File.Exists(localPath))
        {
            string srcUrl = System.IO.Path.Combine(Application.streamingAssetsPath, vidRelPath);
            string fileName = System.IO.Path.GetFileName(vidRelPath);
            string dstPath = System.IO.Path.Combine(Application.persistentDataPath, fileName);
            bool copyOk = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            // StreamingAssets 는 APK 내부 jar → 직접 File API 불가. UnityWebRequest 로 바이트 추출.
            var req = UnityEngine.Networking.UnityWebRequest.Get(srcUrl);
            yield return req.SendWebRequest();
            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success
                && req.downloadHandler.data != null && req.downloadHandler.data.Length > 0)
            {
                try
                {
                    System.IO.File.WriteAllBytes(dstPath, req.downloadHandler.data);
                    copyOk = true;
                    Debug.Log($"[SpatialAnchorTest] ad video 복사: {dstPath} ({req.downloadHandler.data.Length} bytes)");
                }
                catch (System.Exception e) { Debug.LogError($"[SpatialAnchorTest] ad video write 실패: {e.Message}"); }
            }
            else Debug.LogError($"[SpatialAnchorTest] ad video 로드 실패: {vidRelPath} — {req.error}");
#else
            try
            {
                if (System.IO.File.Exists(srcUrl))
                {
                    System.IO.File.Copy(srcUrl, dstPath, true);
                    copyOk = true;
                }
                else Debug.LogError($"[SpatialAnchorTest] ad video 없음 (Editor): {srcUrl}");
            }
            catch (System.Exception e) { Debug.LogError($"[SpatialAnchorTest] ad video copy 실패: {e.Message}"); }
            yield return null;
#endif
            if (!copyOk)
            {
                // 복사 실패 → 정지 PNG 폴백 (크래시 금지).
                StartCoroutine(LoadCompetitorImage(pngRelPath, mr));
                yield break;
            }
            localPath = dstPath;
            _videoLocalPaths[vidRelPath] = localPath;
        }

        // quad 가 그새 FIFO 제거됐으면 중단.
        if (quad == null || mr == null) yield break;

        // VideoPlayer 셋업 + Prepare 를 try 로 감싸 native 호출 예외를 흡수.
        //   주의: C# 에선 catch 있는 try 블록 안에서 yield 불가 → yield 없이 bool 플래그만 설정.
        VideoPlayer vp = null;
        bool ok = false;
        try
        {
            // b25: per-quad VideoPlayer — 이 quad GameObject 에 직접 부착 (quad Destroy 시 함께 정리).
            vp = quad.AddComponent<VideoPlayer>();
            vp.source = VideoSource.Url;
            vp.url = "file://" + localPath;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.isLooping = true;
            vp.playOnAwake = false;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.skipOnDrop = true;
            vp.aspectRatio = VideoAspectRatio.FitInside;

            // 영상 에러 시 정지 PNG 로 폴백 (재생 중 디코더 오류 포함).
            vp.errorReceived += (v, msg) =>
            {
                Debug.LogError($"[SpatialAnchorTest] ad video error: {msg} → PNG 폴백");
                try { v.Stop(); } catch { }
                if (mr != null) StartCoroutine(LoadCompetitorImage(pngRelPath, mr));
            };
            vp.prepareCompleted += OnAdVideoPrepared;
            vp.Prepare();
            ok = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SpatialAnchorTest] VideoPlayer 셋업/Prepare 실패: {e.Message} → PNG 폴백");
        }

        if (!ok)
            StartCoroutine(LoadCompetitorImage(pngRelPath, mr));
    }

    // Candidate A: 경쟁사 광고 정지 이미지를 StreamingAssets 에서 로드해 quad 에 적용 (Android jar → UnityWebRequest).
    IEnumerator LoadCompetitorImage(string imgRelPath, MeshRenderer mr)
    {
        if (mr == null) yield break;
        string url = System.IO.Path.Combine(Application.streamingAssetsPath, imgRelPath);
        byte[] bytes = null;
#if UNITY_ANDROID && !UNITY_EDITOR
        var req = UnityEngine.Networking.UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success) bytes = req.downloadHandler.data;
        else Debug.LogError($"[SpatialAnchorTest] 광고 이미지 로드 실패: {imgRelPath} — {req.error}");
#else
        if (System.IO.File.Exists(url)) bytes = System.IO.File.ReadAllBytes(url);
        yield return null;
#endif
        if (bytes != null && mr != null && mr.material != null)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            mr.material.mainTexture = tex;
            // v1.6: 정면배치에선 거울상 아님 → adMirrorX=false 기본(미러 없음). true 면 U축 미러.
            mr.material.mainTextureScale  = adMirrorX ? new Vector2(-1f, 1f) : new Vector2(1f, 1f);
            mr.material.mainTextureOffset = adMirrorX ? new Vector2( 1f, 0f) : new Vector2(0f, 0f);
            // 광고 가로세로비에 맞춰 quad 스케일 보정 (찌그러짐 방지).
            // v1.4: 전역 adQuad 대신 이 텍스처를 받은 quad(mr.transform) 기준으로 보정.
            if (tex.height > 0)
            {
                float a = (float)tex.height / tex.width;
                mr.transform.localScale = new Vector3(adQuadWidthM, adQuadWidthM * a, 1f);
            }
            Debug.Log($"[SpatialAnchorTest] 광고 이미지 적용 {tex.width}x{tex.height}: {imgRelPath}");
        }
    }

    // VideoPlayer prepare 완료 → 영상 해상도 RenderTexture 할당 후 quad material 에 바인딩.
    // b25 color-video: per-quad — VideoPlayer 가 부착된 quad(=vp.gameObject)를 adQuads 에서 찾아
    //   해당 슬롯(adRTs)에 RenderTexture 를 만들어 바인딩. 영상 좌우미러(adMirrorX)도 image 경로와 동일 처리.
    void OnAdVideoPrepared(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnAdVideoPrepared;
        int idx = adQuads.IndexOf(vp.gameObject);
        if (idx < 0) { try { vp.Stop(); } catch { } return; }   // 이미 FIFO 제거됨
        var target = adQuads[idx];
        int w = (int)vp.width, h = (int)vp.height;
        if (w <= 0 || h <= 0) { Debug.LogWarning("[SpatialAnchorTest] ad video 0 dim"); return; }

        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        rt.Create();
        if (idx < adRTs.Count)
        {
            if (adRTs[idx] != null) adRTs[idx].Release();
            adRTs[idx] = rt;
        }
        vp.targetTexture = rt;

        var mr = target.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null)
        {
            mr.material.mainTexture = rt;
            // 정면배치에선 거울상 아님 → adMirrorX=false 기본(미러 없음). image 경로와 동일.
            mr.material.mainTextureScale  = adMirrorX ? new Vector2(-1f, 1f) : new Vector2(1f, 1f);
            mr.material.mainTextureOffset = adMirrorX ? new Vector2( 1f, 0f) : new Vector2(0f, 0f);
            // 영상 가로세로비에 맞춰 quad 스케일 보정 (찌그러짐 방지).
            float a = (float)h / w;
            target.transform.localScale = new Vector3(adQuadWidthM, adQuadWidthM * a, 1f);
        }
        vp.Play();
        Debug.Log($"[SpatialAnchorTest] ad video playing {w}x{h} (quad idx={idx})");
    }

    float _logNext = 0f;

    void Update()
    {
        frameCount++;

        // SLAM status polling (매 frame)
#if !UNITY_EDITOR
        try { lastSlamStatus = com.rayneo.xr.extensions.XRInterfaces.GetHeadTrackerStatus(); }
        catch { lastSlamStatus = -2; }
#else
        lastSlamStatus = 1;
#endif

        // bisection diagnostic: 1Hz log of SLAM status + camPos + camRot. measurable fact 위해.
        if (xrCam != null && Time.time > _logNext)
        {
            Vector3 p = xrCam.transform.position;
            Vector3 e = xrCam.transform.eulerAngles;
            Vector3 hp = lastHeadPose.position;
            Debug.Log($"[SpatialAnchorTest] SLAM status={lastSlamStatus} camPos=({p.x:F3},{p.y:F3},{p.z:F3}) camRot=({e.x:F1},{e.y:F1},{e.z:F1}) | headDrv calls={headPoseCallCount} pos=({hp.x:F3},{hp.y:F3},{hp.z:F3}) uptime={Time.time:F1}s");
            _logNext = Time.time + 1f;
        }

        // 수렴 도달 시 한 번만: convergence time 기록 + anchor 위치 refine
        if (lastSlamStatus == 1 && slamConvergeSeconds < 0 && slamSeekStartTime > 0)
        {
            slamConvergeSeconds = Time.time - slamSeekStartTime;
            Debug.Log($"[SpatialAnchorTest] SLAM CONVERGED after {slamConvergeSeconds:F2}s");
        }

        // SLAM 수렴 후 첫 번째 frame 에 anchor reposition (camera pose 가 진짜 6DoF 로 jump 했으므로)
        if (lastSlamStatus == 1 && !repositionedOnConverge && xrCam != null && anchorQuad != null)
        {
            Vector3 camPos = xrCam.transform.position;
            Vector3 camFwd = xrCam.transform.forward;
            Vector3 camUp  = xrCam.transform.up;
            Vector3 anchorPos = camPos + camFwd * anchorDistanceM;
            anchorQuad.transform.position = anchorPos;
            anchorQuad.transform.rotation = Quaternion.LookRotation(-camFwd, camUp);
            anchorWorldPos = anchorPos;
            anchorAtPlacementCamPos = camPos;
            anchorAtPlacementCamFwd = camFwd;
            // v1.5: HUD 는 카메라 parent head-lock 이라 월드 reposition 안 함 (자동으로 따라옴).
            repositionedOnConverge = true;
            Debug.Log($"[SpatialAnchorTest] Repositioned anchor on converge: world={anchorPos}");
        }

        // v1.1: SLAM 발산 감지 — |camPos| > 30m 가 2초 이상 연속이면 diverged 판정.
        //   재앵커는 최대 10초당 1회 (발산 지속 중 매 frame 재앵커 방지).
        //   SLAM 재토글(Disable/Enable)은 검증 전이라 금지 — 콘텐츠 재앵커만 (시야 복귀).
        if (xrCam != null)
        {
            float camMag = xrCam.transform.position.magnitude;
            if (camMag > 30f)
            {
                if (divergedSince < 0) divergedSince = Time.time;
                if (Time.time - divergedSince >= 2f && Time.time - lastReanchorTime >= 10f)
                {
                    divergenceRecoveries++;
                    lastReanchorTime = Time.time;
                    Debug.LogError($"[SpatialAnchorTest] SLAM DIVERGED |camPos|={camMag:F1}m → re-anchor #{divergenceRecoveries}");
                    ReanchorContentToCamera();
                }
            }
            else
            {
                divergedSince = -1f;
            }
        }

        // FPS / velocity
        float now = Time.unscaledTime;
        if (now - fpsSampleStart >= 0.5f)
        {
            fpsLast = frameCount / (now - fpsSampleStart);
            frameCount = 0;
            fpsSampleStart = now;
        }
        if (xrCam != null)
        {
            float dt = now - prevSampleTime;
            if (dt > 0.01f)
            {
                Vector3 cp = xrCam.transform.position;
                Vector3 ce = xrCam.transform.eulerAngles;
                linearVel = (cp - prevCamPos) / dt;
                Vector3 dEuler = ce - prevCamEuler;
                for (int i = 0; i < 3; i++) { if (dEuler[i] > 180) dEuler[i] -= 360; if (dEuler[i] < -180) dEuler[i] += 360; }
                angularVelDps = dEuler / dt;
                prevCamPos = cp; prevCamEuler = ce; prevSampleTime = now;
            }
        }

        // HUD update
        UpdateHud();
    }

    // v1.1: 발산 시 콘텐츠를 현재 카메라 앞으로 재앵커 (SpawnProvisional 의 배치 수식 재사용).
    //   발산한 world 좌표계에서도 콘텐츠가 시야로 복귀 → 데모 계속 가능.
    void ReanchorContentToCamera()
    {
        if (xrCam == null) return;
        Vector3 camPos   = xrCam.transform.position;
        Vector3 camFwd   = xrCam.transform.forward;
        Vector3 camUp    = xrCam.transform.up;
        Vector3 anchorPos = camPos + camFwd * anchorDistanceM;
        Quaternion faceUser = Quaternion.LookRotation(-camFwd, camUp);

        if (anchorQuad != null)
        {
            anchorQuad.transform.position = anchorPos;
            anchorQuad.transform.rotation = faceUser;
        }
        // v1.4: 누적된 광고 quad 전부를 카메라 정면(응시 지점)으로 재배치.
        //   ShowAdBesideMatch 와 일관되게 정면(anchorPos) 으로 통일 (이전엔 camRight 옆칸).
        foreach (var q in adQuads)
        {
            if (q == null) continue;
            q.transform.position = anchorPos;
            q.transform.rotation = faceUser;
        }
        // v1.5: HUD 는 카메라 parent head-lock 이라 발산 시에도 재앵커 불필요 (항상 시야 상단 고정).
        anchorWorldPos = anchorPos;
        anchorAtPlacementCamPos = camPos;
        anchorAtPlacementCamFwd = camFwd;
    }

    void UpdateHud()
    {
        if (xrCam == null) return;
        // v1.5: HUD 는 카메라 parent head-lock — 위치/회전은 parenting 이 담당. 여기선 텍스트만 갱신.

        Vector3 camPos = xrCam.transform.position;
        Vector3 camFwd = xrCam.transform.forward;

        // ── 상단 좌측: 카운터 4개 prominent. HelloAR(같은 GameObject) lazily 캐시. ──
        if (helloAr == null) helloAr = GetComponent<HelloAR>();
        if (clipExt == null) clipExt = GetComponent<ClipExtractor>();
        if (hudCounters != null)
        {
            // v1.6: 최상단 = CLIP 컴파일 완료 플래그("멈춤?" 헷갈림 해소), 그 아래 funnel 카운터 5개.
            string clipFlag = clipExt == null ? "CLIP: --"
                : (clipExt.isReady ? $"CLIP: READY ({clipExt.compileSeconds:F0}s)"
                                   : $"CLIP: COMPILING {clipExt.compileSeconds:F0}s");
            if (helloAr != null)
                hudCounters.text =
                    clipFlag + "\n" +
                    $"TRIG: {helloAr.triggerCount}   MATCH: {helloAr.matchCount}\n" +
                    $"COLA: {helloAr.colaCount}\n" +
                    $"COKE: {helloAr.cokeCount}   PEPSI: {helloAr.pepsiCount}";
            else
                hudCounters.text = clipFlag;   // HelloAR 없어도 CLIP 플래그는 표시
        }

        // ── 상단 우측: SLAM 진단 ──
        if (hudDiag == null) return;

        float age = anchorQuad != null ? Time.time - spawnTime : 0f;
        float dist = anchorQuad != null ? Vector3.Distance(camPos, anchorWorldPos) : 0f;
        Vector3 drift = camPos - anchorAtPlacementCamPos;
        float driftMag = drift.magnitude;
        float fwdDot = Vector3.Dot(camFwd, anchorAtPlacementCamFwd);
        float fwdAngleDeg = Mathf.Acos(Mathf.Clamp(fwdDot, -1f, 1f)) * Mathf.Rad2Deg;

        string slamLabel;
        // v1.1: 발산 판정 (|camPos|>30m 가 2초 이상) 이면 상태와 복구 횟수 표시
        if (divergedSince >= 0 && Time.time - divergedSince >= 2f)
            slamLabel = $"DIVERGED(re-anchored x{divergenceRecoveries})";
        else if (lastSlamStatus == 1) slamLabel = repositionedOnConverge ? "CONVERGED" : "TRACKING";
        else if (lastSlamStatus == 0) slamLabel = "SEEKING";
        else if (lastSlamStatus == -2) slamLabel = "EXCEPTION";
        else slamLabel = $"?({lastSlamStatus})";

        float convStr = slamConvergeSeconds >= 0 ? slamConvergeSeconds : (Time.time - slamSeekStartTime);
        string convPrefix = slamConvergeSeconds >= 0 ? "converged" : "seeking";

        hudDiag.text =
            $"SpatialAnchorTest v2\n" +
            $"SLAM: {slamLabel}  (raw={lastSlamStatus})\n" +
            $"{convPrefix}: {convStr:F1}s\n" +
            $"uptime: {Time.time:F1}s  fps: {fpsLast:F1}\n" +
            $"-- 6DoF camera --\n" +
            $"pos: {camPos.x:F2}, {camPos.y:F2}, {camPos.z:F2}\n" +
            $"rot: {xrCam.transform.eulerAngles.x:F0}, {xrCam.transform.eulerAngles.y:F0}, {xrCam.transform.eulerAngles.z:F0}\n" +
            $"v:   {linearVel.magnitude:F2} m/s\n" +
            $"w:   {angularVelDps.magnitude:F0} dps\n" +
            $"-- anchor --\n" +
            $"world: {anchorWorldPos.x:F2}, {anchorWorldPos.y:F2}, {anchorWorldPos.z:F2}\n" +
            $"age: {age:F1}s  dist: {dist:F2}m\n" +
            $"drift: {driftMag:F2}m  fwd∠: {fwdAngleDeg:F0}°";

        // v1.6: [MONITOR] 구조화 로그 — eagle-monitor 스킬이 logcat 에서 파싱해 터미널 대시보드로 렌더.
        EmitMonitorLog(camPos, slamLabel, dist, driftMag, fwdAngleDeg);
    }

    // v1.6: 디버그 모니터링용 단일라인 JSON. monitorLogInterval 주기로만 emit.
    //   필드: 카운터(트리거/매치/콜라/코크/펩시) + CLIP 컴파일 상태 + SLAM 센서(자세/속도/상태) + 소환 물체 위치(anchor + ads).
    void EmitMonitorLog(Vector3 camPos, string slamLabel, float dist, float driftMag, float fwdAngleDeg)
    {
        if (Time.time - lastMonitorEmit < monitorLogInterval) return;
        lastMonitorEmit = Time.time;

        if (clipExt == null) clipExt = GetComponent<ClipExtractor>();
        if (helloAr == null) helloAr = GetComponent<HelloAR>();
        string clipState = clipExt == null ? "?" : (clipExt.isReady ? "READY" : "COMPILING");
        float clipS = clipExt != null ? clipExt.compileSeconds : 0f;
        int trig  = helloAr != null ? helloAr.triggerCount : 0;
        int match = helloAr != null ? helloAr.matchCount   : 0;
        int cola  = helloAr != null ? helloAr.colaCount    : 0;
        int coke  = helloAr != null ? helloAr.cokeCount    : 0;
        int pepsi = helloAr != null ? helloAr.pepsiCount   : 0;

        var adsb = new System.Text.StringBuilder();
        for (int i = 0; i < adQuads.Count; i++)
        {
            var q = adQuads[i];
            if (q == null) continue;
            if (adsb.Length > 0) adsb.Append(',');
            var p = q.transform.position;
            adsb.Append($"[{p.x:F2},{p.y:F2},{p.z:F2}]");
        }
        Vector3 rot = xrCam.transform.eulerAngles;
        Debug.Log(
            "[MONITOR] {" +
            $"\"t\":{Time.time:F1},\"clip\":\"{clipState}\",\"clipS\":{clipS:F0}," +
            $"\"trig\":{trig},\"match\":{match},\"cola\":{cola},\"coke\":{coke},\"pepsi\":{pepsi}," +
            $"\"slam\":\"{slamLabel}\",\"raw\":{lastSlamStatus}," +
            $"\"pos\":[{camPos.x:F2},{camPos.y:F2},{camPos.z:F2}]," +
            $"\"rot\":[{rot.x:F0},{rot.y:F0},{rot.z:F0}]," +
            $"\"v\":{linearVel.magnitude:F2},\"w\":{angularVelDps.magnitude:F0}," +
            $"\"anchor\":[{anchorWorldPos.x:F2},{anchorWorldPos.y:F2},{anchorWorldPos.z:F2}]," +
            $"\"dist\":{dist:F2},\"drift\":{driftMag:F2},\"fwdAng\":{fwdAngleDeg:F0}," +
            $"\"nads\":{adQuads.Count},\"ads\":[{adsb}]" +
            "}");
    }
}
