using System.Collections;
using UnityEngine;

// 안경 카메라 라이브 프리뷰.
// Unity의 WebCamTexture 사용 (내부적으로 Android Camera2 호출).
// 권한: Application.RequestUserAuthorization(WebCam) — 처음 실행 시 다이얼로그.
//
// RayNeo X3 Pro가 표준 Camera2 API를 따르면 이대로 동작.
// 안 따르면 디바이스가 비어있거나 webCamTex.Play() 실패 → RayNeo SDK 필요.
public class CameraPreview : MonoBehaviour
{
    [Header("요청할 카메라 설정 (실제로는 안경이 가장 가까운 값 선택)")]
    public int requestedWidth = 1280;
    public int requestedHeight = 720;
    public int requestedFps = 30;

    public WebCamTexture webCamTex;
    public bool isReady;
    public string statusMessage = "초기화 중...";
    public string[] deviceList;

    IEnumerator Start()
    {
        statusMessage = "권한 요청 중...";
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            statusMessage = "❌ 카메라 권한 거부";
            Debug.LogError("[CameraPreview] Camera permission denied.");
            yield break;
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        deviceList = new string[devices.Length];
        for (int i = 0; i < devices.Length; i++)
        {
            deviceList[i] = $"{devices[i].name} (front={devices[i].isFrontFacing})";
            Debug.Log($"[CameraPreview] Device {i}: {deviceList[i]}");
        }

        if (devices.Length == 0)
        {
            statusMessage = "❌ 카메라 디바이스 0개 — 표준 Camera2 미지원 가능성, RayNeo SDK 필요";
            Debug.LogError("[CameraPreview] No WebCam devices found.");
            yield break;
        }

        statusMessage = $"카메라 시작 중: {devices[0].name}";

        webCamTex = new WebCamTexture(devices[0].name, requestedWidth, requestedHeight, requestedFps);
        webCamTex.Play();

        // 텍스처 크기는 Play 후 1~2 프레임 지나야 확정
        float timeout = Time.time + 5f;
        while (webCamTex.width <= 16 && Time.time < timeout)
        {
            yield return null;
        }

        if (webCamTex.width <= 16)
        {
            statusMessage = "❌ 카메라 시작 timeout (5초)";
            Debug.LogError("[CameraPreview] WebCamTexture failed to start.");
            yield break;
        }

        isReady = true;
        statusMessage = $"✅ {webCamTex.width}×{webCamTex.height} @ {webCamTex.requestedFPS}fps  rot={webCamTex.videoRotationAngle}° mir={webCamTex.videoVerticallyMirrored}";
        Debug.Log($"[CameraPreview] {statusMessage}  device={devices[0].name}");
    }

    void OnDestroy()
    {
        if (webCamTex != null && webCamTex.isPlaying)
        {
            webCamTex.Stop();
        }
    }
}
