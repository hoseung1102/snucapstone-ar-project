using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

// AdRenderer — 매칭된 광고 카드를 화면에 표시. 2D HUD 방식 (head-locked).
// 기획안 Step 4 (Anchor Lifecycle) 의 단순화된 형태:
//   IDLE → ACTIVE (fade-in) → 유지 → FADE_OUT → IDLE
//
// 사용:
//   var ad = gameObject.AddComponent<AdRenderer>();
//   yield return null;
//   ad.Show("pepsi_bottle_ad.png");   // StreamingAssets/db/ads/ 에서 로드
//
// 이미지 캐시 — 같은 파일 두 번째부터는 즉시 표시.
public class AdRenderer : MonoBehaviour
{
    [Header("표시 라이프사이클 (sec)")]
    public float fadeInSec  = 0.5f;
    // v0.4.1: 영상 광고 10초 (mp4 길이) — 이미지 광고 기본 5초.
    public float activeSec  = 10.0f;
    public float fadeOutSec = 0.5f;

    [Header("Sponsored 라벨 (한국 광고법 준수)")]
    [Tooltip("광고 disclosure. 노션 8장 결정사항.")]
    public bool showSponsored = true;
    public string sponsoredText = "Sponsored";

    [Header("표시 위치/크기 — 한 눈(eye) 기준 비율")]
    [Tooltip("v0.5.7: SBS stereo — widthRatio 는 한 눈 영역 기준. 양안 동일 광고 그림.\n" +
             "이전 v0.5.6 의 0.40(전체화면) ≈ 0.80(한 눈) 너무 큼 → 0.20 으로 1/5 축소")]
    [Range(0f, 1f)] public float xNormalized = 0.5f;     // 한 눈 가운데
    [Range(0f, 1f)] public float yNormalized = 0.15f;    // 한 눈 위 15%
    [Range(0f, 1f)] public float widthRatio  = 0.20f;    // 한 눈의 20%

    [Header("라벨 / 비교 카드 (노션 4.6 spec)")]
    [Tooltip("이미지 아래에 표시할 텍스트 (광고 라벨)")]
    public string currentLabel = "";

    // v0.5.2: 비교 카드 정보
    public string identifiedName = "";    // "Coca-Cola Classic" (확인용)
    public string adBrand = "";           // "Pepsi Classic"
    public float rating = 0f;             // 4.3
    public string priceText = "";         // "₩1,200"
    public string differentiator = "";    // "단맛이 강함"
    public string location = "";          // "음료 코너 좌측"
    public bool showComparisonCard = true;

    enum State { Idle, FadeIn, Active, FadeOut }
    State _state = State.Idle;
    float _stateStartTime;

    Texture2D _currentTex;
    Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

    // v0.4.1: 영상 광고 — VideoPlayer + RenderTexture.
    // 활성 시 _useVideo=true → OnGUI 가 _videoRT 표시. _useVideo=false → _currentTex (PNG).
    VideoPlayer _vp;
    RenderTexture _videoRT;
    bool _useVideo;

    GUIStyle _labelStyle;

    /// <summary>
    /// 광고 이미지를 화면에 표시. StreamingAssets/db/ads/{filename} 에서 로드.
    /// 이미 표시 중이면 중단 후 새 광고로 전환.
    /// </summary>
    public void Show(string filename, string label = "")
    {
        currentLabel = label;
        StartCoroutine(LoadAndShow(filename));
    }

    // v0.7.0: Brand 받음 (Category 의 한 brand). comparison 필드는 Brand 에 directly.
    public void ShowComparison(string filename, ProductMatcher.Brand b)
    {
        ApplyBrand(b);
        StartCoroutine(LoadAndShow(filename));
    }

    public void ShowComparisonVideo(string videoFilename, ProductMatcher.Brand b)
    {
        ApplyBrand(b);
        StartCoroutine(LoadAndPlayVideo(videoFilename));
    }

    void ApplyBrand(ProductMatcher.Brand b)
    {
        currentLabel = b.ad_label;
        identifiedName = b.identified_name;
        adBrand = b.ad_brand;
        rating = b.rating;
        priceText = b.price;
        differentiator = b.differentiator;
        location = b.location;
    }

    IEnumerator LoadAndPlayVideo(string filename)
    {
        if (_vp == null)
        {
            _vp = gameObject.AddComponent<VideoPlayer>();
            _vp.playOnAwake = false;
            _vp.isLooping = false;
            _vp.audioOutputMode = VideoAudioOutputMode.None;
            _vp.renderMode = VideoRenderMode.RenderTexture;
            _vp.aspectRatio = VideoAspectRatio.FitInside;
        }

        // StreamingAssetsPath/db/ads_video/xxx.mp4 → file:// URL (Android 도 그대로 OK in Unity 6)
        string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, filename);
        _vp.url = srcPath;
        _vp.Prepare();
        float deadline = Time.time + 3f;
        while (!_vp.isPrepared && Time.time < deadline) yield return null;
        if (!_vp.isPrepared)
        {
            Debug.LogWarning($"[AdRenderer] 영상 prepare timeout: {filename}");
            yield break;
        }

        // RenderTexture lazy alloc (영상 dimension 알게 된 후)
        int w = (int)_vp.width;
        int h = (int)_vp.height;
        if (_videoRT == null || _videoRT.width != w || _videoRT.height != h)
        {
            if (_videoRT != null) _videoRT.Release();
            _videoRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _videoRT.Create();
        }
        _vp.targetTexture = _videoRT;
        _vp.Play();

        _useVideo = true;
        _state = State.FadeIn;
        _stateStartTime = Time.time;
        Debug.Log($"[AdRenderer] ShowVideo: {filename} ({w}x{h}) label='{currentLabel}'");
    }

    public void Hide()
    {
        _state = State.FadeOut;
        _stateStartTime = Time.time;
    }

    IEnumerator LoadAndShow(string filename)
    {
        Texture2D tex;
        if (!_cache.TryGetValue(filename, out tex))
        {
            string srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, filename);
            byte[] bytes;

            if (Application.platform == RuntimePlatform.Android)
            {
                var req = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[AdRenderer] 광고 이미지 로드 실패: {filename} — {req.error}");
                    yield break;
                }
                bytes = req.downloadHandler.data;
            }
            else
            {
                if (!System.IO.File.Exists(srcPath))
                {
                    Debug.LogError($"[AdRenderer] 광고 이미지 없음 (Editor): {srcPath}");
                    yield break;
                }
                bytes = System.IO.File.ReadAllBytes(srcPath);
            }

            tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            _cache[filename] = tex;
        }

        _currentTex = tex;
        _useVideo = false;
        _state = State.FadeIn;
        _stateStartTime = Time.time;
        Debug.Log($"[AdRenderer] Show: {filename} ({tex.width}x{tex.height})  label='{currentLabel}'");
    }

    void Update()
    {
        if (_state == State.Idle) return;
        float elapsed = Time.time - _stateStartTime;

        switch (_state)
        {
            case State.FadeIn:
                if (elapsed >= fadeInSec) { _state = State.Active; _stateStartTime = Time.time; }
                break;
            case State.Active:
                if (elapsed >= activeSec) { _state = State.FadeOut; _stateStartTime = Time.time; }
                break;
            case State.FadeOut:
                if (elapsed >= fadeOutSec) { _state = State.Idle; _currentTex = null; }
                break;
        }
    }

    float CurrentAlpha()
    {
        float elapsed = Time.time - _stateStartTime;
        switch (_state)
        {
            case State.FadeIn:  return Mathf.Clamp01(elapsed / fadeInSec);
            case State.Active:  return 1f;
            case State.FadeOut: return Mathf.Clamp01(1f - elapsed / fadeOutSec);
            default:            return 0f;
        }
    }

    void OnGUI()
    {
        if (_state == State.Idle) return;
        if (!_useVideo && _currentTex == null) return;
        if (_useVideo && _videoRT == null) return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle();
            _labelStyle.fontSize = 24;
            _labelStyle.alignment = TextAnchor.MiddleCenter;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.fontStyle = FontStyle.Bold;
        }

        float alpha = CurrentAlpha();
        Color prevColor = GUI.color;
        GUI.color = new Color(1, 1, 1, alpha);

        // v0.5.7: SBS stereo — 양안에 동일 광고 동일 좌표계로 그림.
        int halfW = Screen.width / 2;
        DrawAdInEye(0,     0, halfW, Screen.height, alpha);
        DrawAdInEye(halfW, 0, halfW, Screen.height, alpha);

        GUI.color = prevColor;
    }

    void DrawAdInEye(int x0, int y0, int eyeW, int eyeH, float alpha)
    {
        // 광고 — 한 눈 widthRatio 기준. video 모드면 RenderTexture 사용.
        Texture src = _useVideo ? (Texture)_videoRT : (Texture)_currentTex;
        float w = eyeW * widthRatio;
        float aspect = (float)src.height / src.width;
        float h = w * aspect;
        float x = x0 + eyeW * xNormalized - w / 2;
        float y = y0 + eyeH * yNormalized;

        GUI.DrawTexture(new Rect(x, y, w, h), src, ScaleMode.ScaleToFit);

        float infoY = y + h + 8;

        // v0.5.2: 비교 카드 (노션 4.6 spec)
        if (showComparisonCard && !string.IsNullOrEmpty(identifiedName))
        {
            // 1) 식별된 상품 (확인용)
            var idStyle = new GUIStyle();
            idStyle.fontSize = 14;
            idStyle.alignment = TextAnchor.UpperCenter;
            idStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, alpha);
            GUI.Label(new Rect(x, infoY, w, 22), $"📍 {identifiedName}", idStyle);
            infoY += 22;

            // 2) 분리선 (실제로는 빈 영역)
            infoY += 4;

            // 3) Conquest 비교 정보: 브랜드 + 평점★ + 가격
            var conquestStyle = new GUIStyle();
            conquestStyle.fontSize = 18;
            conquestStyle.alignment = TextAnchor.UpperCenter;
            conquestStyle.fontStyle = FontStyle.Bold;
            conquestStyle.normal.textColor = new Color(1f, 1f, 0.4f, alpha);  // 노란 강조
            string ratingStar = rating > 0 ? $" {rating:F1}★" : "";
            string priceStr = string.IsNullOrEmpty(priceText) ? "" : $" · {priceText}";
            GUI.Label(new Rect(x, infoY, w, 28), $"{adBrand}{ratingStar}{priceStr}", conquestStyle);
            infoY += 28;

            // 4) 차별점 한 줄
            if (!string.IsNullOrEmpty(differentiator))
            {
                var diffStyle = new GUIStyle();
                diffStyle.fontSize = 14;
                diffStyle.alignment = TextAnchor.UpperCenter;
                diffStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
                diffStyle.fontStyle = FontStyle.Italic;
                GUI.Label(new Rect(x, infoY, w, 22), $"\"{differentiator}\"", diffStyle);
                infoY += 22;
            }

            // 5) 위치 hint
            if (!string.IsNullOrEmpty(location))
            {
                var locStyle = new GUIStyle();
                locStyle.fontSize = 12;
                locStyle.alignment = TextAnchor.UpperCenter;
                locStyle.normal.textColor = new Color(0.7f, 0.9f, 1f, alpha);
                GUI.Label(new Rect(x, infoY, w, 18), location, locStyle);
                infoY += 18;
            }
        }
        else if (!string.IsNullOrEmpty(currentLabel))
        {
            _labelStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(x, infoY, w, 40), currentLabel, _labelStyle);
            infoY += 40;
        }

        // v0.5.1: Sponsored 라벨 (한국 광고법 준수, 노션 8장)
        if (showSponsored)
        {
            var sponsorStyle = new GUIStyle();
            sponsorStyle.fontSize = 12;
            sponsorStyle.alignment = TextAnchor.MiddleRight;
            sponsorStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, alpha * 0.85f);
            GUI.Label(new Rect(x, infoY + 4, w - 4, 16), sponsoredText, sponsorStyle);
        }
    }

    public bool IsShowing => _state != State.Idle;
}
