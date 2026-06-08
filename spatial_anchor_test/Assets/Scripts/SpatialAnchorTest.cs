using System.Collections;
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
    public string textureResourceName = "tiger_anchor";

    // ----- state -----
    Camera xrCam;
    GameObject anchorQuad;
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
        // XR loader (idempotent — UnityOpenXrActivity 가 이미 init 했어도 무해)
        try
        {
            var xrSettings = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            if (xrSettings != null && xrSettings.Manager != null)
            {
                xrSettings.Manager.InitializeLoaderSync();
                xrSettings.Manager.StartSubsystems();
            }
        }
        catch (System.Exception e) { Debug.LogError($"[SpatialAnchorTest] XR init: {e.Message}"); }

        try
        {
            com.rayneo.xr.extensions.XRInterfaces.EnableSlamHeadTracker();
            slamEnableCalled = true;
            Debug.Log("[SpatialAnchorTest] EnableSlamHeadTracker() called.");
        }
        catch (System.Exception e) { Debug.LogError($"[SpatialAnchorTest] EnableSlam: {e.Message}"); }

        try
        {
            com.rayneo.xr.extensions.XRInterfaces.EnablePlaneDetection();
            Debug.Log("[SpatialAnchorTest] EnablePlaneDetection() called.");
        }
        catch { }
#endif

        slamSeekStartTime = Time.time;

        // 즉시 provisional anchor + HUD spawn. SLAM 미수렴 상태에서도 visible.
        SpawnProvisional();
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
        var unlit = Shader.Find("Unlit/Texture");
        if (unlit == null) unlit = Shader.Find("Sprites/Default");
        if (unlit == null) unlit = Shader.Find("Standard");
        if (unlit != null)
        {
            var mat = new Material(unlit);
            if (tex != null) mat.mainTexture = tex;
            mr.material = mat;
        }
        else
        {
            Debug.LogWarning("[SpatialAnchorTest] Unlit/Sprites/Standard shader 다 stripped — default material 유지");
            if (tex != null) mr.material.mainTexture = tex;
        }

        var col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);
        return q;
    }

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
