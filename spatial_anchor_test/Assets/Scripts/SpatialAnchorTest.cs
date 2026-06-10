using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    // bisection: helloar component 추가 case. B0=baseline only, B1=+Gyro, B2=+Camera (ShareCamera),
    // B3=+Clip, B4=+OCR, B5=+Gyro+Camera, B6=+Gyro+Clip, B7=+Camera+Clip, B8=full helloar.
    public string bisectionCase = "B0";

    // ----- state -----
    Camera xrCam;
    GameObject anchorQuad;
    GameObject adQuad;
    TextMesh hudText;
    GameObject hudObj;

    float spawnTime;
    Vector3 anchorWorldPos;
    Vector3 anchorAtPlacementCamPos;
    Vector3 anchorAtPlacementCamFwd;

    int lastSlamStatus = -1;
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
        // DIAG(greentest): 배포→화면 체인 검증용 — 텍스처 무시하고 강제 형광 초록 단색.
        // 화면에 초록 quad 가 보이면 빌드/배포/렌더 체인 정상(이전 'cola'는 텍스처였음).
        var solid = Shader.Find("Unlit/Color");
        if (solid == null) solid = Shader.Find("Sprites/Default");
        if (solid == null) solid = Shader.Find("Standard");
        if (solid != null)
        {
            var mat = new Material(solid);
            mat.color = new Color(0f, 1f, 0f, 1f);
            mr.material = mat;
        }
        else
        {
            Debug.LogWarning("[SpatialAnchorTest] solid shader 다 stripped — default material");
            mr.material.color = new Color(0f, 1f, 0f, 1f);
        }

        var col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);
        return q;
    }

    // v0.8: 매칭된 제품 옆 공간에 world-anchored 광고 spawn (conquest 데모 핵심).
    // HelloAR 전체 pipeline (B8) 에서만 호출됨 — B0~B7 baseline 에선 미실행.
    public void ShowAdBesideMatch(string vidPath, ProductMatcher.MatchResult result,
                                  List<Detection> detections, int W, int H)
    {
        if (xrCam == null) return;

        // 제품 앵커 오른쪽으로 quad 한 칸 떨어진 곳에 광고 quad 배치.
        Vector3 camFwd = xrCam.transform.forward;
        Vector3 camUp  = xrCam.transform.up;
        Vector3 camRight = xrCam.transform.right;
        Vector3 adPos = anchorWorldPos + camRight * (quadWidthM * 1.2f);
        Quaternion adRot = Quaternion.LookRotation(-camFwd, camUp);

        if (adQuad != null) Destroy(adQuad);
        adQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        adQuad.name = "AdQuad";
        adQuad.transform.position = adPos;
        adQuad.transform.rotation = adRot;
        adQuad.transform.localScale = new Vector3(quadWidthM, quadWidthM * 0.75f, 1f);
        var col = adQuad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // TODO(B8): VideoPlayer 로 vidPath(.mp4) 재생. 현재는 brand 정지 광고 텍스처.
        var mr = adQuad.GetComponent<MeshRenderer>();
        Texture2D adTex = Resources.Load<Texture2D>(textureResourceName);
        var sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
        if (sh != null)
        {
            var m = new Material(sh);
            if (adTex != null) m.mainTexture = adTex;
            mr.material = m;
        }

        string brandName = result != null && result.brand != null ? result.brand.name : "AD";
        Debug.Log($"[SpatialAnchorTest] ShowAdBesideMatch brand={brandName} vid='{vidPath}' at {adPos} (W={W} H={H} dets={(detections != null ? detections.Count : 0)})");
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
            Vector3 hr = lastHeadPose.rotation.eulerAngles;
#if !UNITY_EDITOR
            try { htRet = com.rayneo.xr.extensions.XRInterfaces.RayNeoApi_GetHeadTrackerPose(htPos, htRot); }
            catch (System.Exception ex) { htRet = -1; Debug.LogError($"[SpatialAnchorTest][DIAG2] GetHeadTrackerPose: {ex.Message}"); }
#endif
            Debug.Log($"[SpatialAnchorTest] SLAM status={lastSlamStatus} camPos=({p.x:F3},{p.y:F3},{p.z:F3}) camRot=({e.x:F1},{e.y:F1},{e.z:F1}) | headDrv calls={headPoseCallCount} pos=({hp.x:F3},{hp.y:F3},{hp.z:F3}) | nativePose ret={htRet} pos=({htPos[0]:F3},{htPos[1]:F3},{htPos[2]:F3}) rot=({htRot[0]:F3},{htRot[1]:F3},{htRot[2]:F3},{htRot[3]:F3}) uptime={Time.time:F1}s");
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
        if (lastSlamStatus == 1) slamLabel = repositionedOnConverge ? "CONVERGED" : "TRACKING";
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
