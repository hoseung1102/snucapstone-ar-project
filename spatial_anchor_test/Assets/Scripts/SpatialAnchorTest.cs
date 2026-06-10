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

    [Header("HUD (world-space)")]
    public float hudDistanceM = 0.6f;          // quad 와 거의 같은 거리, 약간 더 멀게
    public Vector3 hudOffsetLocal = new Vector3(0f, 0.20f, 0f);  // quad 위쪽
    public float hudCharSize = 0.0015f;
    public int hudFontSize = 80;

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
    GameObject adQuad;
    // v1.0: world-anchored 광고 영상 (VideoPlayer→RenderTexture, AdRenderer 셋업 미러).
    VideoPlayer adVp;
    RenderTexture adRT;
    TextMesh hudText;
    GameObject hudObj;

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
        if (adVp != null) { adVp.prepareCompleted -= OnAdVideoPrepared; try { adVp.Stop(); } catch { } }
        if (adRT != null) { adRT.Release(); adRT = null; }
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

        // HUD: world-space TextMesh, quad 위쪽
        hudObj = new GameObject("HudText");
        Vector3 hudPos = camPos + camFwd * hudDistanceM
                       + camUp * hudOffsetLocal.y
                       + xrCam.transform.right * hudOffsetLocal.x;
        hudObj.transform.position = hudPos;
        hudObj.transform.rotation = Quaternion.LookRotation(-camFwd, camUp);

        hudText = hudObj.AddComponent<TextMesh>();
        hudText.text = "INIT";
        hudText.fontSize = hudFontSize;
        hudText.characterSize = hudCharSize;
        hudText.anchor = TextAnchor.UpperLeft;
        hudText.alignment = TextAlignment.Left;
        hudText.color = Color.white;
        hudText.richText = false;
        // TextMesh 의 default material 그대로 사용 — GUI/Text Shader 가 build 에서
        // stripped 된 경우 Shader.Find 가 null 이라 override 시도하면 throw.
        // TextMesh component 자체가 font 의 default material 를 set 함.

        Debug.Log($"[SpatialAnchorTest] Provisional anchor + HUD spawned at cam={camPos} fwd={camFwd}");
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
        Vector3 objectPos = camPos + camFwd * anchorDistanceM;             // 응시 물체
        Vector3 adPos     = objectPos + camRight * (quadWidthM * 1.2f);    // 그 오른쪽 한 칸
        Quaternion faceUser = Quaternion.LookRotation(-camFwd, camUp);     // spawn 시점 1회 (billboard X)

        // 앵커 마커(quad)를 응시 물체로 이동 (world 고정).
        if (anchorQuad != null)
        {
            anchorQuad.transform.position = objectPos;
            anchorQuad.transform.rotation = faceUser;
            anchorWorldPos = objectPos;
        }

        // 광고 quad spawn (world 고정).
        if (adQuad != null) Destroy(adQuad);
        adQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        adQuad.name = "AdQuad";
        adQuad.transform.position = adPos;
        adQuad.transform.rotation = faceUser;
        adQuad.transform.localScale = new Vector3(quadWidthM, quadWidthM * 0.75f, 1f);
        var col = adQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var mr = adQuad.GetComponent<MeshRenderer>();

        // material 기본 셋업 (이미지 로드 완료 시 mainTexture 교체).
        var sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
        if (sh != null) mr.material = new Material(sh);

        // Candidate A: 영상(VideoPlayer→world quad) 경로가 spawn 직후 앱 크래시 → 정지 PNG 로 대체.
        // vidPath(db/ads_video/x.mp4) → 이미지(db/ads/x.png). AdRenderer.LoadAndShow 의 UnityWebRequest 로딩 미러.
        string imgPath = (vidPath ?? "").Replace("ads_video", "ads").Replace(".mp4", ".png");
        StartCoroutine(LoadCompetitorImage(imgPath, mr));

        string brandName = result != null && result.brand != null ? result.brand.name : "AD";
        Debug.Log($"[SpatialAnchorTest] ShowAdBesideMatch brand={brandName} img='{imgPath}' obj={objectPos} ad={adPos} (dets={(detections != null ? detections.Count : 0)})");
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
            // 광고 가로세로비에 맞춰 quad 스케일 보정 (찌그러짐 방지).
            if (adQuad != null && tex.height > 0)
            {
                float a = (float)tex.height / tex.width;
                adQuad.transform.localScale = new Vector3(quadWidthM, quadWidthM * a, 1f);
            }
            Debug.Log($"[SpatialAnchorTest] 광고 이미지 적용 {tex.width}x{tex.height}: {imgRelPath}");
        }
    }

    // VideoPlayer prepare 완료 → 영상 해상도 RenderTexture 할당 후 quad material 에 바인딩.
    void OnAdVideoPrepared(VideoPlayer vp)
    {
        if (adQuad == null) { try { vp.Stop(); } catch { } return; }
        int w = (int)vp.width, h = (int)vp.height;
        if (w <= 0 || h <= 0) { Debug.LogWarning("[SpatialAnchorTest] ad video 0 dim"); return; }
        if (adRT == null || adRT.width != w || adRT.height != h)
        {
            if (adRT != null) adRT.Release();
            adRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            adRT.Create();
        }
        vp.targetTexture = adRT;
        var mr = adQuad.GetComponent<MeshRenderer>();
        if (mr != null && mr.material != null) mr.material.mainTexture = adRT;
        vp.Play();
        Debug.Log($"[SpatialAnchorTest] ad video playing {w}x{h}");
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

            if (hudObj != null)
            {
                hudObj.transform.position = camPos + camFwd * hudDistanceM + camUp * hudOffsetLocal.y;
                hudObj.transform.rotation = Quaternion.LookRotation(-camFwd, camUp);
            }
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
        Vector3 camRight = xrCam.transform.right;
        Vector3 anchorPos = camPos + camFwd * anchorDistanceM;
        Quaternion faceUser = Quaternion.LookRotation(-camFwd, camUp);

        if (anchorQuad != null)
        {
            anchorQuad.transform.position = anchorPos;
            anchorQuad.transform.rotation = faceUser;
        }
        if (adQuad != null)
        {
            // ShowAdBesideMatch 와 동일: 응시 지점 오른쪽 한 칸
            adQuad.transform.position = anchorPos + camRight * (quadWidthM * 1.2f);
            adQuad.transform.rotation = faceUser;
        }
        if (hudObj != null)
        {
            hudObj.transform.position = camPos + camFwd * hudDistanceM + camUp * hudOffsetLocal.y;
            hudObj.transform.rotation = faceUser;
        }
        anchorWorldPos = anchorPos;
        anchorAtPlacementCamPos = camPos;
        anchorAtPlacementCamFwd = camFwd;
    }

    void UpdateHud()
    {
        if (hudText == null || xrCam == null) return;

        // HUD 가 항상 카메라를 향하게 (billboard) — provisional 상태에선 head-locked 효과
        Vector3 camPos = xrCam.transform.position;
        Vector3 camFwd = xrCam.transform.forward;
        Vector3 camUp  = xrCam.transform.up;

        // SLAM 미수렴 동안엔 HUD 도 매 frame 따라옴 (head-locked). 수렴 후엔 fixed world pos.
        if (!repositionedOnConverge)
        {
            hudObj.transform.position = camPos + camFwd * hudDistanceM + camUp * hudOffsetLocal.y;
            hudObj.transform.rotation = Quaternion.LookRotation(-camFwd, camUp);
        }
        else
        {
            // 수렴 후엔 billboard 회전만 (위치 fixed)
            hudObj.transform.rotation = Quaternion.LookRotation(hudObj.transform.position - camPos, Vector3.up);
        }

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

        hudText.text =
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
    }
}
