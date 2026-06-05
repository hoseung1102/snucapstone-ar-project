using System.Collections.Generic;
using UnityEngine;

// Eagle Eye PoC v0.2.1
// - Stereo SBS 렌더링
// - GyroTrigger (one-shot)
// - CameraPreview (v0.2.0 그대로 유지, 검증용)
// - **NEW**: Sentis YOLO11n 추론 — 사전 번들 테스트 이미지에서 박스 검출
//
// 동작:
//   Awake 시 테스트 이미지 로드 + 1회 YOLO 추론 → 결과 박스를 영구히 표시
//   카메라 프리뷰는 배경으로 그대로 (라이브 프레임에 추론 적용은 v0.2.2에서)
public class HelloAR : MonoBehaviour
{
    GyroTrigger gyro;
    CameraPreview cam;
    YoloDetector yolo;
    float triggerFlashUntil = -1f;

    Texture2D testImage;
    List<Detection> detections = new List<Detection>();
    bool ranInitialInference;

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

        cam = gameObject.AddComponent<CameraPreview>();
        yolo = gameObject.AddComponent<YoloDetector>();

        testImage = Resources.Load<Texture2D>("test_image");
        if (testImage == null)
        {
            Debug.LogError("[HelloAR] Resources/test_image 못 찾음");
        }
        else
        {
            Debug.Log($"[HelloAR] test_image 로드 OK: {testImage.width}×{testImage.height}");
        }

        Debug.Log("[HelloAR] Init complete (v0.2.1). Gyro + Camera + YOLO attached.");
    }

    void Update()
    {
        // YOLO 준비 + 테스트 이미지 준비되면 1회 추론
        if (!ranInitialInference && yolo != null && yolo.isReady && testImage != null)
        {
            detections = yolo.Detect(testImage);
            ranInitialInference = true;
            Debug.Log($"[HelloAR] Initial inference done: {detections.Count} detections, {yolo.lastInferenceMs}ms");
            foreach (var d in detections)
            {
                Debug.Log($"  - {d.Label} ({d.confidence:F2}) box=({d.xCenter:F1},{d.yCenter:F1},{d.width:F1},{d.height:F1})");
            }
        }
    }

    void HandleTrigger()
    {
        triggerFlashUntil = Time.time + 1.0f;
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

        // 1×1 흰 텍스처 (박스 외곽선용)
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
        // 1. 배경: 테스트 이미지 (YOLO 추론 대상)
        if (testImage != null)
        {
            GUI.DrawTexture(new Rect(x0, y0, w, h), testImage, ScaleMode.ScaleAndCrop);
        }
        else
        {
            GUI.DrawTexture(new Rect(x0, y0, w, h), Texture2D.blackTexture);
        }

        // 2. YOLO 박스 (검출됐을 때만)
        DrawDetections(x0, y0, w, h);

        int cx = x0 + w / 2;
        int cy = y0 + h / 2;
        int labelW = Mathf.Min(w - 40, 600);
        int xOff = cx - labelW / 2;

        // 3. 타이틀
        GUI.Label(new Rect(xOff, y0 + 16, labelW, 32), "Eagle Eye v0.2.1", big);

        // 4. 상태 (YOLO + camera)
        string yoloStatus = yolo != null ? yolo.statusMessage : "(yolo none)";
        string camStatus = cam != null ? cam.statusMessage : "(cam none)";
        GUI.Label(new Rect(xOff, y0 + 50, labelW, 18), $"yolo: {yoloStatus}", statusStyle);
        GUI.Label(new Rect(xOff, y0 + 70, labelW, 18), $"  → {detections.Count} det, {yolo?.lastInferenceMs}ms", statusStyle);
        GUI.Label(new Rect(xOff, y0 + 90, labelW, 18), $"cam: {camStatus}", statusStyle);

        // 5. 자이로 (하단)
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

    // YOLO 박스를 화면 좌표로 변환해서 그림
    // 박스 좌표는 INPUT_SIZE(320×320) 기준 → 우리 화면의 (x0, y0, w, h) 영역으로 매핑
    void DrawDetections(int x0, int y0, int w, int h)
    {
        if (detections == null || detections.Count == 0) return;

        float scaleX = (float)w / YoloDetector.INPUT_SIZE;
        float scaleY = (float)h / YoloDetector.INPUT_SIZE;

        foreach (var d in detections)
        {
            float bx = x0 + (d.xCenter - d.width / 2) * scaleX;
            float by = y0 + (d.yCenter - d.height / 2) * scaleY;
            float bw = d.width * scaleX;
            float bh = d.height * scaleY;

            DrawBoxOutline(bx, by, bw, bh, 2);
            GUI.Label(new Rect(bx + 2, by - 18, 200, 18),
                $"{d.Label} {d.confidence:F2}", boxLabelStyle);
        }
    }

    void DrawBoxOutline(float x, float y, float w, float h, float thickness)
    {
        GUI.DrawTexture(new Rect(x, y, w, thickness), boxTex);                    // top
        GUI.DrawTexture(new Rect(x, y + h - thickness, w, thickness), boxTex);    // bottom
        GUI.DrawTexture(new Rect(x, y, thickness, h), boxTex);                    // left
        GUI.DrawTexture(new Rect(x + w - thickness, y, thickness, h), boxTex);    // right
    }
}
