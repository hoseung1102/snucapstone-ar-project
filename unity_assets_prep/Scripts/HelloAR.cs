using UnityEngine;
using UnityEngine.UI;

// Hello AR — Eagle Eye PoC v0.1
// 런타임에 Canvas + Text를 동적 생성. 검은 배경(=웨이브가이드 투명) + 흰 텍스트.
// AR 글라스 디스플레이에 "Hello AR" 텍스트가 공중에 떠있는 것처럼 보이도록.
public class HelloAR : MonoBehaviour
{
    void Awake()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;

        GameObject canvasObj = new GameObject("HelloCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasObj.AddComponent<GraphicRaycaster>();

        AddText(canvas.transform, "Hello AR", 80, new Vector2(0, 40), FontStyle.Bold);
        AddText(canvas.transform, "Eagle Eye PoC v0.1", 32, new Vector2(0, -40), FontStyle.Normal);

        Debug.Log("[HelloAR] Initialized. Canvas + Text created.");
    }

    void AddText(Transform parent, string content, int size, Vector2 offset, FontStyle style)
    {
        GameObject obj = new GameObject($"Text_{content}");
        obj.transform.SetParent(parent, false);

        Text text = obj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(800, size + 10);
    }
}
