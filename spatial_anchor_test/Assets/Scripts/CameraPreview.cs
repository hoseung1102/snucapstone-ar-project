using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using RayNeo.API;
using com.rayneo.xr.extensions;
using static com.rayneo.xr.extensions.XRCamera;
#endif

// 안경 카메라 라이브 프리뷰.
// v0.9.0: WebCamTexture → RayNeo ShareCamera 로 전환.
//   SLAM 6DOF (CameraAttitudeType=8193) 활성 시 표준 Android Camera2 (WebCamTexture) 가
//   black frame 만 내보내는 문제 해결. ShareCamera 는 RayNeo SDK 의 parallel-channel RGB 카메라로
//   SLAM 과 동시 사용 가능.
//
// 외부 API 호환:
//   - public Texture webCamTex { get; }  ← 기존 코드가 cam.webCamTex 로 접근하는 것 유지
//   - public bool isReady
//   - public string statusMessage
public class CameraPreview : MonoBehaviour
{
    [Header("요청할 카메라 설정 (실제로는 안경이 가장 가까운 값 선택)")]
    public int requestedWidth = 1280;
    public int requestedHeight = 720;
    public int requestedFps = 30;

    // 외부에서 cam.webCamTex 로 접근하는 호환 property.
    // ShareCamera 의 XRCameraHandler.texture (Texture2D) 를 그대로 노출.
    public Texture webCamTex { get; private set; }

    public bool isReady;
    public string statusMessage = "초기화 중...";
    public string[] deviceList;

#if UNITY_ANDROID && !UNITY_EDITOR
    private XRCameraHandler _handler;
    private const string RGB_CAMERA_ID = "0";   // ShareCamera.RGB_CAMERA_ID 와 동일
#endif

    // v0.9.1: lifecycle 분리. HelloAR match → SLAM 위해 close, 광고 N초 후 reopen.
    bool _isOpening;
    public bool isOpening => _isOpening;

    IEnumerator Start()
    {
        yield return OpenCameraInternal();
    }

    public Coroutine OpenCamera()
    {
        if (isReady || _isOpening) return null;
        return StartCoroutine(OpenCameraInternal());
    }

    public void CloseCamera()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_handler != null)
        {
            try { ShareCamera.CloseCamera(_handler); }
            catch (System.Exception e) { Debug.LogWarning("[CameraPreview] CloseCamera 예외: " + e.Message); }
            _handler = null;
        }
#endif
        webCamTex = null;
        isReady = false;
        statusMessage = "closed (SLAM mode)";
        Debug.Log("[CameraPreview] CloseCamera — SLAM 위해 RGB pipeline release");
    }

    IEnumerator OpenCameraInternal()
    {
        _isOpening = true;
        // 카메라 권한 (안경에는 보통 ADB pre-grant 됨)
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
            UnityEngine.Android.Permission.Camera))
        {
            statusMessage = "권한 요청 중...";
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Camera);
            float deadline = Time.time + 3f;
            while (Time.time < deadline &&
                   !UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                       UnityEngine.Android.Permission.Camera))
            {
                yield return null;
            }
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Camera))
            {
                statusMessage = "❌ 카메라 권한 거부 (adb shell pm grant ... CAMERA 로 부여 가능)";
                Debug.LogError("[CameraPreview] Camera permission denied.");
                _isOpening = false;
                yield break;
            }
        }

        // ShareCamera 가 지원하는 해상도 탐색.
        // requestedWidth/Height 와 일치하는 게 있으면 그걸 쓰고, 없으면 SDK default (640×400) 로 폴백.
        XRResolution selected = null;
        XRResolution[] supported = null;
        try
        {
            supported = ShareCamera.getSupportResolutions(XRCameraType.RGB);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[CameraPreview] getSupportResolutions 실패: " + e.Message);
        }

        if (supported != null && supported.Length > 0)
        {
            var devList = new System.Collections.Generic.List<string>();
            foreach (var r in supported)
            {
                devList.Add($"{r.width}x{r.height}");
                if (r.width == requestedWidth && r.height == requestedHeight)
                    selected = r;
            }
            deviceList = devList.ToArray();
            // 가장 가까운 (큰 쪽) 선택 — 정확한 match 없으면.
            if (selected == null)
            {
                int bestDelta = int.MaxValue;
                foreach (var r in supported)
                {
                    int delta = System.Math.Abs(r.width - requestedWidth) +
                                System.Math.Abs(r.height - requestedHeight);
                    if (delta < bestDelta) { bestDelta = delta; selected = r; }
                }
            }
        }

        statusMessage = selected != null
            ? $"카메라 시작 중: ShareCamera RGB {selected.width}×{selected.height}"
            : "카메라 시작 중: ShareCamera RGB (default 640×400)";

        try
        {
            if (selected != null)
                _handler = ShareCamera.OpenCamera(XRCameraType.RGB, selected, null, requestedFps);
            else
                _handler = ShareCamera.OpenCamera(XRCameraType.RGB, null, requestedFps);
        }
        catch (System.Exception e)
        {
            statusMessage = "❌ ShareCamera.OpenCamera 예외: " + e.Message;
            Debug.LogError("[CameraPreview] " + statusMessage);
            _isOpening = false;
            yield break;
        }

        if (_handler == null)
        {
            statusMessage = "❌ ShareCamera.OpenCamera 실패 (handler=null) — 지원 해상도/카메라 상태 확인";
            Debug.LogError("[CameraPreview] " + statusMessage);
            yield break;
        }

        // 첫 프레임 도착 대기 (최대 5초).
        float timeout = Time.time + 5f;
        while ((_handler.texture == null || _handler.width <= 16) && Time.time < timeout)
        {
            yield return null;
        }

        if (_handler.texture == null || _handler.width <= 16)
        {
            statusMessage = "❌ ShareCamera 첫 프레임 timeout (5초)";
            Debug.LogError("[CameraPreview] " + statusMessage);
            _isOpening = false;
            yield break;
        }

        webCamTex = _handler.texture;

        int rot = 0;
        try { rot = XRCameraHelper.getOrientation(RGB_CAMERA_ID); }
        catch (System.Exception e) { Debug.LogWarning("[CameraPreview] getOrientation 실패: " + e.Message); }

        isReady = true;
        _isOpening = false;
        statusMessage = $"✅ {_handler.width}×{_handler.height} @ {requestedFps}fps  rot={rot}° mir=False  device=ShareCamera/RGB";
        Debug.Log($"[CameraPreview] {statusMessage}");
        yield break;
#else
        statusMessage = "❌ ShareCamera Android only";
        Debug.LogWarning("[CameraPreview] " + statusMessage);
        _isOpening = false;
        yield break;
#endif
    }

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // _handler.texture 는 onImageAvailable 콜백에서 재할당될 수 있어
        // (XRChannel.bufferSize=3 ring buffer) 매 프레임 최신 reference 로 갱신.
        if (_handler != null && _handler.texture != null)
        {
            webCamTex = _handler.texture;
        }
#endif
    }

    void OnDestroy()
    {
        CloseCamera();
    }

    void OnDisable()
    {
        // OnDestroy 에서 close. 일시적 disable 에는 굳이 카메라 끄지 않음 (재시작 비용 큼).
    }
}
