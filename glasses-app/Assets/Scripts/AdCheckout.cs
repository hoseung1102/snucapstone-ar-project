using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// AdCheckout (b26) — world-anchored mock checkout flow attached to a spawned competitor ad.
//
// 목적: conquest→purchase 스토리 완성. 광고를 select 하면 in-AR mock "결제사이트풍" 패널이
//   열린다 (제품 썸네일 + 이름 + 가격 + BUY 버튼 → ✓ ORDER PLACED). 전부 in-app mock —
//   실제 결제 SDK X, QR X(글라스 시점 스캔 불가), 외부 앱(Alipay/브라우저) 실행 X
//   (OpenXR 세션 이탈 시 black-screen/세션 단절 위험 — 이전에 겪음).
//
// === ADDITIVE DISPLAY 제약 (RayNeo X3 Pro MicroLED see-through) ===
//   BLACK = TRANSPARENT (어두운 패널/텍스트는 안 보임). BRIGHT/emissive 만 보임.
//   → 패널 배경은 dark fill 금지. 밝은 frame(라인)·밝은 글자·밝은 BUY fill 만 사용.
//   → Unlit/Texture 또는 TextMesh(GUI/Text) 만. PBR/Standard 는 검게 나옴.
//   → 그림자 없음, GLES3, Single Pass Instanced.
//
// === 상태 기계 ===
//   AD_SHOWN --(select 광고)--> CHECKOUT --(select BUY)--> CONFIRMED --(timeout)--> 광고 복귀
//   각 패널은 spawn 시점 billboard(정면 1회 고정, head re-follow 안 함 — 진짜 world-anchored).
//   select = RayNeoRingController homeButton(thumbstick/click) 또는 gaze/grip-ray dwell.
//   ray 가 없으면 head-forward(camera.forward) 를 reticle 로 사용해 dwell — 컨트롤러 없이도 동작.
//
// SpatialAnchorTest.ShowAdBesideMatch 가 ad quad 를 만든 직후 이 컴포넌트를 붙여 호출:
//   var ck = adQuad.AddComponent<AdCheckout>();
//   ck.Init(xrCam, adQuad, productName, priceText, productThumb);
public class AdCheckout : MonoBehaviour
{
    // ---- 상태 ----
    public enum State { AdShown, Checkout, Confirmed }
    State state = State.AdShown;

    // ---- 외부 주입 ----
    Camera cam;
    GameObject adQuad;                 // 기존 광고 quad (AD 패널 역할 — select 대상)
    string productName = "PEPSI";      // a-series placeholder
    string priceText   = "$1.50";
    MeshRenderer adRenderer;           // 광고 quad 의 MeshRenderer — 썸네일 텍스처를 lazily 읽음
                                       //   (영상/PNG 로드가 attach 후 완료되므로 build 시점에 읽어야 non-null)

    // ---- 레이아웃 파라미터 (additive 친화) ----
    [Header("배치")]
    public float panelWidthM = 0.26f;      // 광고(0.22) 보다 약간 큼 — checkout 이 전면화됨
    public float panelGapM   = 0.02f;      // 광고와 checkout 카드 수직 간격
    [Header("색 (전부 BRIGHT — additive 에서 black=투명)")]
    public Color frameColor   = new Color(1f, 1f, 1f, 1f);          // 흰 frame 라인
    public Color textColor    = new Color(1f, 1f, 1f, 1f);          // 흰 글자
    public Color priceColor   = new Color(1f, 0.95f, 0.4f, 1f);     // 밝은 amber 가격
    public Color buyColor     = new Color(0.2f, 1f, 0.45f, 1f);     // 밝은 green BUY fill
    public Color confirmColor = new Color(0.3f, 1f, 0.55f, 1f);     // 밝은 green 확정
    public Color glowColor    = new Color(0.5f, 0.9f, 1f, 1f);      // selection/dwell glow (밝은 cyan)

    [Header("dwell (gaze/head ray 로 select)")]
    [Tooltip("reticle 가 대상에 머물러야 하는 시간 (초). 이 동안 fill-ring 이 차오름.")]
    public float dwellSeconds = 1.2f;
    [Tooltip("ray-대상 각도 허용치 (도). reticle 가 이 안이면 대상에 hover 로 간주.")]
    public float dwellConeDeg = 8f;
    [Tooltip("confirmed 패널 자동 dismiss 까지 (초).")]
    public float confirmHoldSeconds = 3.5f;

    // ---- 빌드된 오브젝트 ----
    GameObject checkoutCard;   // CHECKOUT 패널 root (제품썸네일+이름+가격+BUY)
    GameObject buyButton;      // BUY fill quad (select 대상 2)
    GameObject confirmedCard;  // CONFIRMED 패널 root
    GameObject glowHalo;       // 현재 hover 대상 뒤의 밝은 halo (selection highlight)
    GameObject dwellRing;      // dwell 진행 fill-ring (billboard, reticle 위치)
    TextMesh dwellPctText;     // dwell % (디버그/명료성)

    // ---- 입력 상태 ----
    InputAction selectAction;  // homeButton (thumbstick/click) — discrete select
    GameObject hoverTarget;    // 현재 reticle 이 향한 select 대상 (adQuad 또는 buyButton)
    float dwellElapsed;        // 현재 hover 누적 시간
    float confirmedAt = -1f;

    public void Init(Camera xrCam, GameObject ad, string name, string price, MeshRenderer thumbSource)
    {
        cam = xrCam != null ? xrCam : Camera.main;
        adQuad = ad;
        if (!string.IsNullOrEmpty(name))  productName = name.ToUpperInvariant();
        if (!string.IsNullOrEmpty(price)) priceText  = price;
        adRenderer = thumbSource;

        // homeButton(thumbstick/click) discrete select 액션. ring/cellphone 둘 다 가능하게 와일드.
        try
        {
            selectAction = new InputAction("checkoutSelect", InputActionType.Button);
            selectAction.AddBinding("<RayNeoRingController>/homeButton");
            selectAction.AddBinding("<XRController>/{PrimaryButton}");   // 폴백 (다른 프로파일)
            selectAction.performed += _ => OnDiscreteSelect();
            selectAction.Enable();
        }
        catch (System.Exception e) { Debug.LogWarning($"[AdCheckout] select action: {e.Message}"); }

        state = State.AdShown;
        Debug.Log($"[AdCheckout] init on adQuad — product={productName} price={priceText} thumbSrc={(thumbSource!=null)}");
    }

    void OnDestroy()
    {
        try { if (selectAction != null) { selectAction.Disable(); selectAction.Dispose(); } } catch { }
        // checkout/confirmed/glow/ring 패널은 adQuad 자식이 아닌 top-level GameObject —
        //   adQuad 가 FIFO 제거/Destroy 될 때 이들도 함께 정리해야 orphan leak 방지.
        if (checkoutCard  != null) Destroy(checkoutCard);
        if (confirmedCard != null) Destroy(confirmedCard);
        if (glowHalo      != null) Destroy(glowHalo);
        if (dwellRing     != null) Destroy(dwellRing);
    }

    void Update()
    {
        if (cam == null) return;

        // ── reticle = gaze/head-forward ray. 어떤 select 대상에 hover 중인지 판정 ──
        UpdateHover();

        // dwell 진행 (gaze/head-dwell select). discrete homeButton 도 병행 (OnDiscreteSelect).
        if (state != State.Confirmed && hoverTarget != null)
        {
            dwellElapsed += Time.deltaTime;
            UpdateDwellRing(hoverTarget, Mathf.Clamp01(dwellElapsed / dwellSeconds));
            if (dwellElapsed >= dwellSeconds)
            {
                dwellElapsed = 0f;
                Activate(hoverTarget);
            }
        }
        else
        {
            dwellElapsed = 0f;
            HideDwellRing();
        }

        // confirmed 자동 dismiss
        if (state == State.Confirmed && confirmedAt >= 0f && Time.time - confirmedAt >= confirmHoldSeconds)
            DismissAll();
    }

    // homeButton 클릭 = 즉시 select (dwell 기다리지 않음).
    void OnDiscreteSelect()
    {
        if (state == State.Confirmed) { DismissAll(); return; }
        if (hoverTarget != null) Activate(hoverTarget);
    }

    // reticle(현재는 head-forward; gaze pose 있으면 그걸로 교체)에 어떤 대상이 걸리는지.
    void UpdateHover()
    {
        Vector3 origin = cam.transform.position;
        Vector3 dir    = cam.transform.forward;

        GameObject newHover = null;
        if (state == State.AdShown && adQuad != null)
        {
            if (WithinCone(origin, dir, adQuad.transform.position)) newHover = adQuad;
        }
        else if (state == State.Checkout && buyButton != null)
        {
            if (WithinCone(origin, dir, buyButton.transform.position)) newHover = buyButton;
        }

        if (newHover != hoverTarget)
        {
            dwellElapsed = 0f;
            hoverTarget = newHover;
            UpdateGlow(hoverTarget);   // selection highlight = 더 밝은 glow
        }
    }

    bool WithinCone(Vector3 origin, Vector3 dir, Vector3 target)
    {
        Vector3 to = target - origin;
        if (to.sqrMagnitude < 1e-4f) return true;
        float ang = Vector3.Angle(dir, to);
        return ang <= dwellConeDeg;
    }

    void Activate(GameObject target)
    {
        if (state == State.AdShown && target == adQuad)      OpenCheckout();
        else if (state == State.Checkout && target == buyButton) ConfirmOrder();
    }

    // ════════ STATE: CHECKOUT ════════
    // 광고 바로 아래에 결제사이트풍 카드: [제품 썸네일] PEPSI  $1.50  [ BUY NOW ]
    //   billboard = 광고와 동일 rotation(faceUser) 사용 → 같은 평면 정렬.
    void OpenCheckout()
    {
        if (state != State.AdShown) return;
        state = State.Checkout;
        hoverTarget = null; dwellElapsed = 0f; HideDwellRing();
        Debug.Log("[AdCheckout] → CHECKOUT");

        Quaternion rot = adQuad.transform.rotation;       // 광고와 같은 facing (spawn 시점 고정)
        Vector3 right  = rot * Vector3.right;
        Vector3 up     = rot * Vector3.up;
        float adHalfH  = adQuad.transform.localScale.y * 0.5f;
        float cardH    = panelWidthM * 0.55f;
        // 광고 아래쪽으로 카드 중심 배치
        Vector3 center = adQuad.transform.position - up * (adHalfH + panelGapM + cardH * 0.5f);

        checkoutCard = new GameObject("CheckoutCard");
        checkoutCard.transform.position = center;
        checkoutCard.transform.rotation = rot;

        // 밝은 frame (네 변 라인 — dark fill 대신 테두리만, additive 가독).
        BuildFrame(checkoutCard.transform, panelWidthM, cardH, frameColor);

        // 제품 썸네일 (광고 텍스처 재사용) — 왼쪽. 밝은 영상/이미지라 그대로 보임.
        //   build 시점에 lazily 읽음 (영상/PNG 로드가 그새 완료됐을 것).
        Texture productThumb = adRenderer != null && adRenderer.material != null ? adRenderer.material.mainTexture : null;
        if (productThumb != null)
        {
            float thumbSz = cardH * 0.8f;
            var thumb = BuildQuad("Thumb", checkoutCard.transform,
                -right * (panelWidthM * 0.5f - thumbSz * 0.6f),
                thumbSz, thumbSz, Color.white, productThumb);
        }

        // 제품명 (중앙 상단) + 가격 (중앙, amber 강조).
        BuildText("Name",  checkoutCard.transform, right * 0.01f + up * (cardH * 0.18f),
                  productName, textColor, 0.4f, TextAnchor.MiddleCenter);
        BuildText("Price", checkoutCard.transform, right * 0.01f - up * (cardH * 0.10f),
                  priceText, priceColor, 0.55f, TextAnchor.MiddleCenter);

        // BUY NOW 버튼 — 밝은 green fill quad + frame + 글자. select 대상.
        float buyW = panelWidthM * 0.34f, buyH = cardH * 0.30f;
        Vector3 buyPos = right * (panelWidthM * 0.5f - buyW * 0.62f) - up * (cardH * 0.22f);
        buyButton = BuildQuad("BuyButton", checkoutCard.transform, buyPos, buyW, buyH, buyColor, null);
        BuildFrame(buyButton.transform, buyW, buyH, frameColor);
        BuildText("BuyLabel", buyButton.transform, Vector3.zero, "BUY NOW", Color.white, 0.32f, TextAnchor.MiddleCenter);
    }

    // ════════ STATE: CONFIRMED ════════
    // ✓ ORDER PLACED + celebratory glow/pop (scale punch). 그 후 confirmHoldSeconds 뒤 auto-dismiss.
    void ConfirmOrder()
    {
        if (state != State.Checkout) return;
        state = State.Confirmed;
        confirmedAt = Time.time;
        hoverTarget = null; dwellElapsed = 0f; HideDwellRing();
        Debug.Log("[AdCheckout] → CONFIRMED (✓ ORDER PLACED)");

        // checkout 카드 숨김 (confirmed 패널이 대체).
        if (checkoutCard != null) checkoutCard.SetActive(false);

        Quaternion rot = adQuad.transform.rotation;
        Vector3 center = adQuad.transform.position;   // 광고 위치에 확정 패널을 겹쳐 띄움

        confirmedCard = new GameObject("ConfirmedCard");
        confirmedCard.transform.position = center;
        confirmedCard.transform.rotation = rot;

        float w = panelWidthM, h = panelWidthM * 0.5f;
        BuildFrame(confirmedCard.transform, w, h, confirmColor);
        BuildText("Check",   confirmedCard.transform, Vector3.up * (h * 0.18f), "✓", confirmColor, 1.1f, TextAnchor.MiddleCenter);
        BuildText("Placed",  confirmedCard.transform, -Vector3.up * (h * 0.16f), "ORDER PLACED", confirmColor, 0.5f, TextAnchor.MiddleCenter);

        // celebratory glow halo + scale pop.
        StartCoroutine(ConfirmPop(confirmedCard.transform));
    }

    IEnumerator ConfirmPop(Transform t)
    {
        // 밝은 glow halo (확정 패널 뒤 더 큰 quad, 밝은 green, 짧게 페이드).
        var halo = BuildQuad("ConfirmGlow", t, t.forward * 0.001f, panelWidthM * 1.25f, panelWidthM * 0.65f, glowColor, null);
        // scale punch: 0.7 → 1.06 → 1.0.
        float dur = 0.32f, e = 0f;
        Vector3 baseScale = t.localScale;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = e / dur;
            float s = k < 0.6f ? Mathf.Lerp(0.7f, 1.06f, k / 0.6f)
                               : Mathf.Lerp(1.06f, 1.0f, (k - 0.6f) / 0.4f);
            t.localScale = baseScale * s;
            yield return null;
        }
        t.localScale = baseScale;
        // halo 페이드 아웃. additive 디스플레이에선 alpha 가 아니라 "밝기"가 가시성 —
        //   색을 black(=투명) 으로 lerp 하면 자연스럽게 사라진다 (Unlit/Color).
        var mr = halo.GetComponent<MeshRenderer>();
        float fd = 0.6f, fe = 0f;
        while (fe < fd && mr != null && mr.material != null)
        {
            fe += Time.deltaTime;
            mr.material.color = Color.Lerp(glowColor, Color.black, fe / fd);
            yield return null;
        }
        if (halo != null) Destroy(halo);
    }

    void DismissAll()
    {
        Debug.Log("[AdCheckout] dismiss → AD_SHOWN (광고만 남김)");
        if (checkoutCard  != null) Destroy(checkoutCard);
        if (confirmedCard != null) Destroy(confirmedCard);
        if (glowHalo != null) Destroy(glowHalo);
        HideDwellRing();
        buyButton = null;
        state = State.AdShown;
        confirmedAt = -1f;
    }

    // ════════ selection highlight: hover 대상 뒤에 더 밝은 glow halo ════════
    void UpdateGlow(GameObject target)
    {
        if (glowHalo != null) { Destroy(glowHalo); glowHalo = null; }
        if (target == null) return;
        // target 보다 18% 큰 밝은 halo 를 그 뒤(살짝 앞쪽 z)에 world-space 로 배치.
        //   top-level GameObject 라 target scale 영향 안 받음. OnDestroy/DismissAll 에서 정리.
        float w = target.transform.localScale.x * 1.18f;
        float h = target.transform.localScale.y * 1.18f;
        glowHalo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glowHalo.name = "HoverGlow";
        var col = glowHalo.GetComponent<Collider>(); if (col != null) Destroy(col);
        glowHalo.transform.position = target.transform.position + target.transform.forward * 0.002f;
        glowHalo.transform.rotation = target.transform.rotation;
        glowHalo.transform.localScale = new Vector3(w, h, 1f);
        var glowMr = glowHalo.GetComponent<MeshRenderer>();
        glowMr.material = GlowMaterial();
    }

    // ════════ dwell fill-ring: reticle 위치에 차오르는 밝은 호 ════════
    //   진짜 ring mesh 대신 "8 조각 tick" 을 progress 비율만큼 켜 fill 을 표현 (라이트웨이트, 셰이더 추가 X).
    GameObject[] ringTicks;
    void UpdateDwellRing(GameObject target, float progress)
    {
        if (dwellRing == null)
        {
            dwellRing = new GameObject("DwellRing");
            ringTicks = new GameObject[8];
            float r = 0.03f;
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 8f) * Mathf.PI * 2f;
                var tick = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = tick.GetComponent<Collider>(); if (col != null) Destroy(col);
                tick.name = $"tick{i}";
                tick.transform.SetParent(dwellRing.transform, false);
                tick.transform.localPosition = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                tick.transform.localScale = new Vector3(0.006f, 0.012f, 1f);
                var mr = tick.GetComponent<MeshRenderer>();
                mr.material = GlowMaterial();
                ringTicks[i] = tick;
            }
            var d = new GameObject("DwellPct");
            d.transform.SetParent(dwellRing.transform, false);
            d.transform.localPosition = Vector3.zero;
            dwellPctText = d.AddComponent<TextMesh>();
            dwellPctText.fontSize = 60; dwellPctText.characterSize = 0.0012f;
            dwellPctText.anchor = TextAnchor.MiddleCenter; dwellPctText.alignment = TextAlignment.Center;
            dwellPctText.color = glowColor; dwellPctText.richText = false;
        }
        dwellRing.SetActive(true);
        // reticle 위치: 대상 표면 약간 앞.
        dwellRing.transform.position = target.transform.position + target.transform.forward * 0.003f;
        dwellRing.transform.rotation = Quaternion.LookRotation(
            dwellRing.transform.position - cam.transform.position, cam.transform.up);
        int lit = Mathf.CeilToInt(progress * 8f);
        for (int i = 0; i < 8; i++) if (ringTicks != null && ringTicks[i] != null) ringTicks[i].SetActive(i < lit);
        if (dwellPctText != null) dwellPctText.text = Mathf.RoundToInt(progress * 100f) + "%";
    }

    void HideDwellRing() { if (dwellRing != null) dwellRing.SetActive(false); }

    // ════════ 빌드 헬퍼 (전부 Unlit, 밝은색, collider Destroy) ════════
    // 셰이더 선택 주의 (JOURNEY.md 확인): Unlit/Color·Unlit/Texture·GUI/Text 는 빌드에서 stripped 됨.
    //   GraphicsSettings AlwaysIncludedShaders 에 Sprites/Default(10770) 만 보장됨 → solid-color/halo
    //   에는 Sprites/Default 를 1순위로 (material.color 로 tint, vertex-color 경로라 build-safe).
    //   안전 폴백: Unlit/Color → Standard.
    Shader UnlitShader()
    {
        return Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color")
               ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
    }
    Shader UnlitTexShader()
    {
        // 텍스처(썸네일/영상)는 기존 ad quad 와 동일 경로 — Unlit/Texture 우선, Sprites/Default 폴백.
        return Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
    }
    Material GlowMaterial()
    {
        var m = new Material(UnlitShader()); m.color = glowColor; return m;
    }

    GameObject BuildQuad(string name, Transform parent, Vector3 localOffset, float w, float h, Color color, Texture tex)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        var col = q.GetComponent<Collider>(); if (col != null) Destroy(col);
        q.transform.SetParent(parent, false);
        q.transform.localPosition = localOffset;
        q.transform.localRotation = Quaternion.identity;
        q.transform.localScale = new Vector3(w, h, 1f);
        var mr = q.GetComponent<MeshRenderer>();
        var sh = tex != null ? UnlitTexShader() : UnlitShader();
        var mat = new Material(sh);
        if (tex != null) mat.mainTexture = tex; else mat.color = color;
        mr.material = mat;
        return q;
    }

    // frame = 네 변을 얇은 밝은 quad 로 — dark fill 없이 테두리만 (additive 가독).
    void BuildFrame(Transform parent, float w, float h, Color color)
    {
        float t = 0.004f;
        BuildQuad("frameT", parent, new Vector3(0,  h * 0.5f, 0), w, t, color, null);
        BuildQuad("frameB", parent, new Vector3(0, -h * 0.5f, 0), w, t, color, null);
        BuildQuad("frameL", parent, new Vector3(-w * 0.5f, 0, 0), t, h, color, null);
        BuildQuad("frameR", parent, new Vector3( w * 0.5f, 0, 0), t, h, color, null);
    }

    // 밝은 TextMesh (HUD 와 동일 렌더 경로 — GUI/Text 셰이더). charScale 은 상대 크기.
    TextMesh BuildText(string name, Transform parent, Vector3 localOffset, string text, Color color, float charScale, TextAnchor anchor)
    {
        var o = new GameObject(name);
        o.transform.SetParent(parent, false);
        o.transform.localPosition = localOffset + new Vector3(0, 0, -0.001f);
        o.transform.localRotation = Quaternion.identity;
        var tm = o.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 90;
        tm.characterSize = 0.0016f * charScale;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.richText = false;
        return tm;
    }
}
