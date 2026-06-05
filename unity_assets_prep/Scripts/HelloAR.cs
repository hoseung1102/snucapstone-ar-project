using UnityEngine;

// Eagle Eye PoC v0.2.0
// - Stereo SBS 렌더링 (좌/우 절반에 동일 UI)
// - GyroTrigger (one-shot per stable window)
// - **NEW**: 카메라 라이브 프리뷰 (배경)
public class HelloAR : MonoBehaviour
{
    GyroTrigger gyro;
    CameraPreview cam;
    float triggerFlashUntil = -1f;

    GUIStyle big, small, flash, statusStyle;

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

        Debug.Log("[HelloAR] Init complete (v0.2.0). Gyro + Camera attached.");
    }

    void HandleTrigger()
    {
        triggerFlashUntil = Time.time + 1.0f;
    }

    void EnsureStyles()
    {
        if (big != null) return;

        big = new GUIStyle();
        big.fontSize = 36;
        big.fontStyle = FontStyle.Bold;
        big.alignment = TextAnchor.MiddleCenter;
        big.normal.textColor = Color.white;

        small = new GUIStyle();
        small.fontSize = 16;
        small.alignment = TextAnchor.MiddleCenter;
        small.normal.textColor = Color.white;

        flash = new GUIStyle();
        flash.fontSize = 44;
        flash.fontStyle = FontStyle.Bold;
        flash.alignment = TextAnchor.MiddleCenter;
        flash.normal.textColor = new Color(0.2f, 1f, 0.3f);

        statusStyle = new GUIStyle();
        statusStyle.fontSize = 14;
        statusStyle.alignment = TextAnchor.MiddleCenter;
        statusStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);  // 노란 — 상태 메시지
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
        // 1. 배경: 카메라 라이브 프리뷰 (있으면)
        if (cam != null && cam.isReady && cam.webCamTex != null)
        {
            Rect rect = new Rect(x0, y0, w, h);
            int rotation = cam.webCamTex.videoRotationAngle;  // Android 센서 orientation 보정값
            bool mirror = cam.webCamTex.videoVerticallyMirrored;

            if (rotation != 0 || mirror)
            {
                Matrix4x4 saved = GUI.matrix;
                Vector2 pivot = new Vector2(x0 + w / 2f, y0 + h / 2f);

                // 회전 (90°/180°/270°)
                if (rotation != 0)
                {
                    GUIUtility.RotateAroundPivot(rotation, pivot);
                }

                // 수직 미러 (필요 시)
                if (mirror)
                {
                    Matrix4x4 m = GUI.matrix;
                    GUI.matrix = m * Matrix4x4.TRS(
                        new Vector3(0, 2f * pivot.y, 0),
                        Quaternion.identity,
                        new Vector3(1, -1, 1));
                }

                GUI.DrawTexture(rect, cam.webCamTex, ScaleMode.ScaleAndCrop);
                GUI.matrix = saved;
            }
            else
            {
                GUI.DrawTexture(rect, cam.webCamTex, ScaleMode.ScaleAndCrop);
            }
        }
        else
        {
            GUI.DrawTexture(new Rect(x0, y0, w, h), Texture2D.blackTexture);
        }

        int cx = x0 + w / 2;
        int cy = y0 + h / 2;
        int labelW = Mathf.Min(w - 40, 600);
        int xOff = cx - labelW / 2;

        // 2. 타이틀
        GUI.Label(new Rect(xOff, y0 + 20, labelW, 48), "Eagle Eye v0.2.0", big);

        // 3. 카메라 상태
        string camStatus = cam != null ? cam.statusMessage : "(camera component none)";
        GUI.Label(new Rect(xOff, y0 + 70, labelW, 22), $"cam: {camStatus}", statusStyle);

        // 4. 자이로 텔레메트리 (하단)
        Vector3 g = gyro.currentGyro;
        int yBottom = y0 + h - 100;
        GUI.Label(new Rect(xOff, yBottom + 0,  labelW, 22),
            $"gyro: ({g.x:F2}, {g.y:F2}, {g.z:F2})  max: {gyro.currentMaxAbs:F2}", small);
        GUI.Label(new Rect(xOff, yBottom + 24, labelW, 22),
            $"stable: {gyro.isStable}  elapsed: {gyro.stableElapsed:F2}s / {gyro.stableDuration:F1}s", small);
        GUI.Label(new Rect(xOff, yBottom + 48, labelW, 22),
            $"triggers: {gyro.totalTriggers}", small);

        // 5. 트리거 깜빡
        if (Time.time < triggerFlashUntil)
        {
            GUI.Label(new Rect(xOff, cy - 30, labelW, 60), "▶ TRIGGER ◀", flash);
        }
    }
}
