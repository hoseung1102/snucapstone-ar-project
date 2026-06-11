# b22 (npu-ocr-slam) 온디바이스 테스트 결과 + 진단

> 작성: 2026-06-11, Windows + Unity 2022.3.62f3 재빌드, RayNeo X3 Pro 실기(A06B4A95B784973) 착용 트리거 테스트.
> 대상: `feature/npu-ocr-slam-b22` 를 이어서 검증/개발할 팀원. (`BUILD_OCR_SLAM_HANDOFF.md` 의 후속 — 측정 포인트를 실제로 돌려본 결과.)
> 모든 결론은 logcat 직접 증거(트리거 #33~#87 관측). 추정은 "추정" 표기.

---

## TL;DR (한 문단)

**파이프라인 거의 다 살아있다 — 트리거·CLIP category·SLAM 6DoF·카메라·무크래시(22분+) 전부 OK. 단 하나, OCR recognizer가 브랜드 글자를 못 읽어서(빈값 또는 쓰레기) 광고가 한 번도 안 뜬다.** "COCA-COLA"/"PEPSI"를 단 한 번도 못 읽음 → `STAGE2 FAIL brand 미확정` → MATCH 0 / 광고 0. OCR로 브랜드 판별하는 b22 방식이 온디바이스에서 실패. **→ 색상 기반 브랜드 판별(coke 빨강 / pepsi 파랑)로 전환 결정.** + ⚠️ 기기에 깔려있던 팀원 b20 빌드는 **Unity 6으로 빌드된 것**이라 아무것도 안 됐던 것(아래 §5) — 반드시 Unity 2022.3.62f3.

---

## 1. 작동하는 것 (logcat 증거)

| 항목 | 결과 | 증거 |
|---|---|---|
| Unity 버전 | ✅ 2022.3.62f3 | `Built from '2022.3...' Version '2022.3.62f3'` |
| 빌드/설치 | ✅ vc11, `com.eagleeye.helloar`, 183.7MB | aapt + install Success |
| OpenXR 세션 | ✅ 생성 | `RayNeo_xrCreateSession` → `Success to create instance` |
| 카메라 | ✅ ShareCamera RGB **1280×720@30** | `[ShareCamera] OpenCamera.Success`, CamX 프레임 흐름 |
| CLIP | ✅ NPU ready, **category 작동** | `✅ CLIP Interpreter ready NPU=true`. 콜라 볼 때 `category=cola 0.58`, 노트북 `laptop 0.65` |
| OCR 엔진 | ✅ NPU **로드**됨 (det+rec) | `EasyOCR ready (NPU det=true rec=true)` — unrolled recognizer가 HTP에 뜸 |
| SLAM(FFVINS) | ✅ **수렴·추적됨**(움직일 때) | 정지 시 `state=0`, **움직이면 camPos가 머리 따라 추적**(예: -0.1↔-0.7). 6DoF world-anchor 가능 |
| 트리거 | ✅ GyroTrigger 정상 | `[GyroTrigger] >>> TRIGGER #NN` (머리 1초 정지 → fire, 5초 쿨다운) |
| 크래시 | ✅ 없음 (22분+) | uptime 1332s, pid 안정, CDSP `remote_handle64 domain 3` 폭주 0건 |
| HTP 캐시 | ✅ 생성됨 | `files/qnn_clip_cache/mobileclip_s2_v73_int8_v12.36.0_*.bin` → 2차 launch 빠름 |

> **CLIP+OCR det+OCR rec(HTP 3개) + SLAM 이 깨끗한(리부트 직후) 기기에서 22분 공존 = 무크래시.** 이는 "공존 불가"가 아니라 **재실행 시 세션 leak 오염**이 크래시 원인이라는 진단과 부합(§5).

---

## 2. ❌ 핵심 블로커 — OCR가 브랜드 글자를 못 읽음

트리거 #33~#87 전부 STAGE2(OCR brand) 실패:
```
콜라 볼 때(category=cola): text=''                         (빈 값)
노트북/기타: text='arabi: 261010109CDC002' / '02457538 BE aab' / '7e8rz annsuE...'  (쓰레기)
펩시 볼 때:  text='NOneImocat aeq3' / 'eltmerre 7755R2000'  (쓰레기)
→ 매번 STAGE2 FAIL — brand 확정 못 함 → no match → 광고 0
```
- recognizer가 NPU에서 **돌긴 함**(rec ~100~325ms) — 하지만 출력이 **빈값 또는 무의미 문자열**. "COCA-COLA"/"PEPSI" 인식 0회.
- **추정 원인**: 콜라/펩시 로고는 흘림체·스타일 폰트라 인쇄체 학습 EasyOCR가 못 읽음. 노트북 텍스트는 박스는 잡지만(boxes=2~8) CTC 디코딩이 쓰레기. 즉 **OCR로 브랜드 로고 읽기 자체가 부적합.**
- 증거 이미지: 기기 `files/ocr_crops/ocr_00NN.jpg` (OCR 입력 크롭 저장됨, `saveOcrInput`).

### OCR 레이턴시
trigger당 total **~800~1600ms** (decode ~500~800 + det ~300 + rec ~100~325). decode(카메라 프레임→텐서)가 최대 비중.

---

## 3. b22 의도된 플로우 (참고)

```
GyroTrigger(머리 1초 정지) → 카메라 → CLIP category("콜라?") 
  → [콜라] OCR로 글자 읽기 → ResolveBrand(ocrText) → 경쟁사 매핑 → 6DoF world-anchored 광고
```
플래그: `skipOcr=false`, `brandDisambiguator="ocr"`, `enableClipBrandFallback=false`. 전제: 마지막 단계가 6DoF라 SLAM TRACKING 필요(§1 확인됨). **단 §2 때문에 brand 단계에서 끊김.**

---

## 4. 결정 — 색상 브랜드 + 영상 광고로 전환

OCR가 온디바이스에서 안 되므로(§2), 우리 라인의 **색상 기반 브랜드 판별**로 전환:
- `brandDisambiguator="color"`: 중앙 crop 평균 RGB → coke 빨강 / pepsi 파랑 (마진 ~1.0, OCR 0.07 대비 견고). `ResolveBrandByColor` (`HelloAR.cs`).
- `skipOcr=true`: OCR 엔진 init 자체 제거 → **159초 HTP 컴파일 소멸 + OCR CDSP 클라이언트 0**(크래시 위험·시작지연↓).
- **광고 = 영상**(정지 PNG → mp4): coca-cola→`pepsi_bottle_ad.mp4`, pepsi→`coke_bottle_ad.mp4` (StreamingAssets/db/ads_video/ 에 존재). VideoPlayer→RenderTexture→world quad. (영상 경로는 과거 jar-URL 직접재생으로 크래시 → 로컬복사+에러폴백으로 견고화.)
- **adb 디버그 훅**: `eyad_debug.txt` 에 brand 쓰면 트리거/CLIP 우회하고 경쟁사 영상 spawn(착용 없이 테스트).
- → 통합 빌드 `b25-color-video` (branch `feature/b24-integrated`, b22 OCR + 우리 색상/HUD/MONITOR 합본).

---

## 5. ⚠️ 기기에 깔려있던 b20 = Unity 6 빌드 (중요)

테스트 전 기기엔 팀원 `b20-npuocr-wordbox` 가 깔려 있었는데 **Unity 6000.1.11f1 로 빌드된 것**(`Built from '6000.1/staging'`). → OCR/CLIP NPU init이 `Permission denied`(domain 3)로 죽고 SLAM default 화면만 떠서 "아무것도 안 됨". **팀원이 Unity 2022 가 없어 Unity 6 로 빌드한 게 원인.** Unity 2022.3.62f3 재빌드본(b22)은 위 §1 처럼 정상 동작. → **반드시 Unity 2022.3.62f3.**

## 6. 크래시 (참고)
깊은 진단: [`../docs/findings-2026-06-11-crash-slam-openxr.md`](../docs/findings-2026-06-11-crash-slam-openxr.md). 요지: 기기 꺼짐 = 공유 Hexagon CDSP 세션 leak(종료 시 release 미동기) → 재실행 오염 → SLAM FastRPC stale → system_server 사망 → 리부트로만 복구. "리부트 후 첫 launch OK, 이후 크래시" 시그니처. b22 테스트는 리부트 직후라 22분 무사. crash-fix(동기 release) + OCR 제거(CDSP 클라이언트↓)로 완화 예정.
