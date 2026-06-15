<!-- source of truth. 구현/빌드/모델 레퍼런스. 현행 코드 기준으로 갱신한다. -->
<!-- 옛 루트 문서 통합: README.md(레포) + INTEGRATION_GUIDE.md + MODELS.md + README_NPU.md + ONNX_ANALYSIS.md (2026-06-08). -->
<!-- 기획/비전은 vision.md, 클라 사양은 client-spec.md, 진행 이력은 progress-log.md. 읽는 법은 루트 CLAUDE.md. -->

# 🛠️ 개발 가이드 (구현 / 빌드 / 모델 레퍼런스)

> **현재 v0.7.3 기준** (hierarchical CLIP category + OCR brand 매칭, LiteRT + QNN delegate NPU 추론).
> 이 문서는 "코드가 실제로 어떻게 빌드·동작하는가"의 레퍼런스다. 시스템이 **무엇이고 왜 이렇게 결정됐는가**는 [vision.md](vision.md), 클라이언트 **사양**은 [client-spec.md](client-spec.md), **시간순 이력**은 [progress-log.md](progress-log.md).
>
> ⚠️ **폐기된 경로 주의** (옛 문서에 남아있던 내용 — 더 이상 사실 아님):
> - NPU 는 Hexagon **v73** (옛 "v68/v69 추정"은 정정됨). AR1 은 cost-reduced 라 **FP16 unit 없음 → w8a8 양자화 필수**.
> - 추론은 **TFLite Interpreter + QNN delegate** 경로. 옛 "QNN direct (qnn_yolo_engine.cpp / libQnnHtp.so 직접 호출)" 경로는 3rd-party 앱 보안 제약으로 폐기 ([vision.md](vision.md) "QNN direct → LiteRT + QNN Delegate 피벗" 결정 로그 참조).
> - 매칭은 **hierarchical (CLIP category → OCR brand)**. 옛 "YOLO crop → CLIP best-match 1:1" 단순 경로는 CLIP zero-shot 의 brand 분별 한계로 전환됨.

---

## 1. 시스템 한눈에 — 현재 파이프라인 (v0.7.3)

```
[안경 카메라 라이브 프리뷰 (WebCamTexture)]
    ↓
[GyroTrigger — IMU 각속도 안정 감지]  (0.5 rad/s · 1.0s 유지 시 트리거)
    ↓
[Stage 1] CLIP-only 모드면 YOLO skip (현재 기본) / 아니면 YOLO11l 640² TFLite+QNN → Hexagon v73 NPU
    ↓
[Stage 2] frame(또는 bbox crop) → MobileCLIP-S2 INT8 TFLite → v73 NPU (~3ms) → 512-d 임베딩
    ↓        ProductMatcher: CLIP 코사인 유사도로 category 결정 (콜라 vs 노트북) — coarse
    ↓
[Stage 3] MLKit OCR (~50ms) → 라벨 글자 추출 → brand 확정 (deterministic, primary)
    ↓        OCR fail 시: enableClipBrandFallback=true 일 때만 CLIP brand-specific top-1 (기본 OFF)
    ↓        매칭 없으면 광고 X (strict, false positive 방지)
[Stage 4] AdRenderer 2D HUD — 비교 카드 + 영상 광고(mp4) + Sponsored, fade-in 0.5s / active 5s / fade-out
[Stage 5] AmbientInterestProfile — 트리거 누적 로그 (aip.json, 100% 온디바이스, v2+ 활용)
```

**latency 실측** (progress-log §6): CLIP-only 35ms / YOLO+CLIP top-3 150ms / OCR +50ms(sequential) — 전부 200ms 목표 내 (client-spec.md §3 latency 분석).

---

## 2. 코드 / 자산 구조

**안경 앱 = `glasses-app/`** (자기완결 Unity 프로젝트). 아래 경로는 그 안의 상대경로.

| 경로 (`glasses-app/` 기준) | 내용 |
|---|---|
| `Assets/Scripts/SpatialAnchorTest.cs` | **SLAM/6DoF 앵커 + 콘텐츠 world-anchoring + 발산 복구.** 씬 루트 MonoBehaviour. `bisectionCase` B0~B8 (컴포넌트 점진 추가 토글) |
| `Assets/Scripts/HelloAR.cs` | **파이프라인 오케스트레이션.** `clipOnlyMode` 토글 (현재 true — YOLO 우회) |
| `Assets/Scripts/QnnYoloDetector.cs` | YOLO11l NPU 추론 + class whitelist + bbox area 필터 (`minAreaRatio`) |
| `Assets/Scripts/ClipExtractor.cs` | MobileCLIP-S2 임베딩 (512-d, L2-normalized) + 중앙 crop 평균색 측정 |
| `Assets/Scripts/ProductMatcher.cs` | hierarchical 매칭 (CLIP category + OCR brand). `strictBrandRequired`, `enableClipBrandFallback` |
| `Assets/Scripts/OCRExtractor.cs` | OCR 텍스트 추출. 회전 보정(`rotationOverride`) + 중앙 crop(`cropFraction`) + 업스케일(`upscaleFactor`) |
| `Assets/Scripts/AdRenderer.cs` | 2D HUD 비교 카드 + 영상 광고(VideoPlayer) + Sponsored |
| `Assets/Scripts/AdCheckout.cs` | tap-to-checkout 인터랙션 (b26) |
| `Assets/Scripts/AmbientInterestProfile.cs` | AIP 누적 로깅 (aip.json) |
| `Assets/Scripts/GyroTrigger.cs` | IMU 각속도 안정 트리거 |
| `Assets/Scripts/CameraPreview.cs` | **ShareCamera** RGB 프리뷰 (SLAM 6DoF 와 병행). WebCamTexture 는 SLAM 활성 시 black frame 이라 폐기 — 카메라 모델 상세는 [client-spec](client-spec.md)/EXPERIMENTS "살아있는 교훈" |
| `Assets/Plugins/Android/QnnYoloEngine.java` | YOLO JNI (TFLite+QNN delegate) |
| `Assets/Plugins/Android/QnnClipEngine.java` | CLIP JNI (async init) |
| `Assets/Plugins/Android/EasyOCREngine.java` | EasyOCR JNI (CRAFT detector NPU + recognizer). recognizer NPU 비호환 → EXPERIMENTS b22 |
| `Assets/StreamingAssets/*.tflite` | 런타임 모델 (`mobileclip_s2`, `easyocr_*`) |
| `Assets/StreamingAssets/db/` | `metadata.json` + `embeddings/` + `ads/` + `ads_video/*.mp4` (런타임 번들) |
| `tools/monitor/eagle_monitor.py` | eagle-monitor 온디바이스 텔레메트리 대시보드 |

(루트 `db/`·`products/` 와 `refs/`·`simulate_*.py` 등은 Mac 측 임베딩/DB 빌드 재료·도구 — 안경 런타임 아님.)

**Mac 시뮬레이터 / 스크립트** (안경 없이 검증):
- `simulate_pipeline.py` — end-to-end Mac 시뮬 (YOLO+CLIP+비교카드 PNG 생성)
- `simulate_clip_only.py` — CLIP-only 모드 시뮬 (안경 흐름 동일)
- `test_adversarial_match.py` — 매칭 정확도 검증 (test_cans 8/8)
- `build_adversarial_db.py`, `build_unity_db.py` — DB / 임베딩 빌드
- `yolo.py` — 맥북 PoC YOLO 파이프라인 (Optical Flow 기반)

---

## 3. 빌드 & 실행

### 안경 (Unity → Android APK)

빌드는 **Windows + Unity 2022.3.62f3 전용**, `glasses-app/` 의 PowerShell 스크립트로 한다:

```powershell
# glasses-app/ 에서
./setup_2022.ps1     # (최초 1회) Unity 2022 프로젝트 셋업
./build_2022.ps1     # batch 빌드 → APK
```
모델·DB·광고는 `Assets/StreamingAssets/` 에 이미 번들돼 있어 별도 복사 불필요.
설치/권한 grant/launch + logcat 태그 등 현재 빌드·실행 절차의 정확한 명령은 [`integration_log.md`](integration_log.md) §2 "Build / Install / Run" 참조.

> (옛 `build_hello_ar.sh` + `unity_assets_prep/` → `EagleEye_Unity/` 계보는 2026-06-13 폐기. 현재는 위 `glasses-app/` 가 유일.)

```bash
# 수동 install / 실행
adb install -r EagleEye-vX.Y.Z.apk
adb shell pm grant com.eagleeye.helloar android.permission.CAMERA
adb shell am start -n com.eagleeye.helloar/com.unity3d.player.UnityPlayerGameActivity
```

### 검증 — logcat

```bash
adb logcat -s Unity:V QnnYoloEngine:V QnnClipEngine:V MLKitOCR:V \
                ClipExtractor:V ProductMatcher:V OCRExtractor:V AdRenderer:V HelloAR:V
```
성공 로그 예: `[ProductMatcher] ✅ DB 로드: categories=2 ...` → `[OCRExtractor] rot=.. crop=.. → text='PEPSI'` → `[HelloAR] 매칭: pepsi → 광고 ...`.

### Mac 시뮬

```bash
python yolo.py input/sample.mp4 output/result.mp4     # 맥북 PoC
python simulate_clip_only.py                           # CLIP-only 흐름 (안경 동일)
python test_adversarial_match.py                       # 매칭 정확도
```

### 시연 절차

1. 코카콜라/펩시 **페트병** (500mL~1.5L) 준비 (캔 X — COCO bottle 인식 불안정).
2. 안경 착용 → 책상 위/손에 들고 응시 (30~50cm).
3. IMU 안정 → CLIP category → OCR brand → ~1초 후 시야에 비교 카드/영상 광고 (5초 유지).
4. 광고 표시 중엔 다음 트리거 무시 (`ad.IsShowing`).

### 디스크 관리

디스크 여유가 빠듯하면 빌드 캐시 정리 (재생성됨):
```bash
rm -rf glasses-app/Library glasses-app/Temp glasses-app/Build
```

---

## 4. 추론 스택 — LiteRT + QNN Delegate

| 항목 | 값 |
|---|---|
| 런타임 | TFLite Interpreter + **Qualcomm QNN delegate** (Maven `2.47.0`) |
| NPU | Hexagon **v73** (RayNeo X3 Pro / Snapdragon AR1 Gen 1) |
| 정밀도 | **w8a8 필수** — AR1 은 cost-reduced 라 FP16 vector unit 없음. float 모델은 NPU 5.5% 위임뿐 |
| YOLO | `yolo11l_640_w8a8.tflite` — **입력 640²** (`YoloDetector.INPUT_SIZE=640`, StreamingAssets 엔 `yolo11l.tflite` 로 복사). Hexagon 위임 |
| CLIP | `mobileclip_s2_v73_int8.tflite` — ~3ms (2.12ms 실측), clean graph 100% 위임 |
| OCR | MLKit `com.google.mlkit:text-recognition:16.0.1` (Latin/영어, ~50ms) |

> ⚠️ **해상도 주의**: 현재 배포 모델·코드는 **640²** 입력 (`yolo11l_640_w8a8.tflite`). 아래 §5 변형 비교표의 "~15-30ms" 등은 **320² XR2 Gen 2 proxy 벤치마크값**이라 640 실측이 아니다 (320→640 전환은 progress-log §16 참조). `QnnYoloDetector.cs` 일부 인라인 주석에 "320*320" 잔재가 있으나 실제 상수는 640. **end-to-end 실측**(progress-log §6): CLIP-only 35ms / YOLO+CLIP top-3 150ms / +OCR ~50ms — 전부 200ms 목표 내. 단 현재 `clipOnlyMode=true` 라 YOLO 경로는 dormant.

**w8a8 의 대가**: 양자화로 detection confidence 10~50% 손실 → 안경 시점에서 YOLO 의 bottle/book conf 가 매우 낮아짐 → v0.5.11 부터 **CLIP-only 모드**로 YOLO 우회 중. w8a16 (ORT path) 은 Maven native lib 충돌로 보류.

> **이력**: 초기엔 QNN SDK 를 직접 호출(`qnn_yolo_engine.cpp` + `libQnnHtp*.so` + Hexagon firmware push)하려 했으나, **Hexagon DSP 보안 구조상 root 없는 3rd-party 앱은 QNN direct 불가** 판단 → LiteRT(TFLite)+QNN delegate 로 피벗 (v0.2.4 → v0.2.6). 상세는 vision.md 결정 로그.

---

## 5. 모델 자산 — YOLO11 변형 갈아끼우기

YOLO11 5개 변형(n/s/m/l/x) 모두 동일 입출력 인터페이스 → **파일명만 바꾸면 호환**. 변형마다 `.pt`(PyTorch) / `.onnx` / w8a8 tflite (또는 `.qnn_context.bin`).

> 아래 latency/mAP 표는 **320² XR2 Gen 2 proxy 벤치마크** (2026-06-06 m→l 결정 당시). 현재 배포는 **640²** `yolo11l_640_w8a8.tflite` 라 표의 절대 latency 는 참고용 (상대 순위만 유효). 640 안경 실측은 progress-log §6.

| 변형 | 파라미터 | XR2 latency(320²) | AR1 추정 | mAP50-95 | 비고 |
|---|---|---|---|---|---|
| n | 2.6M | 2.4 ms | 4~6 ms | 39.5 | 빠른 반복/베이스라인 |
| s | 9.4M | 3.5 ms | 5~9 ms | 47.0 | +7.5 mAP, +1ms |
| m | 20.1M | 9.4 ms | 14~23 ms | 51.5 | |
| **l** ⭐ | 25.3M | 11.8 ms | 18~30 ms | 53.4 | **현재 기본** (m→l: +2.4ms 에 +1.9 mAP) |
| x | 56.9M | 19.5 ms | 29~49 ms | 54.7 | l→x 는 +1.3 mAP 뿐 — 비효율, 데모 max 정확도용만 |

**기본 = yolo11l** (2026-06-06 결정, vision.md "YOLO m→l" 결정 로그). 모든 변형이 200ms 목표 통과 → latency 가 병목 아님, **정확도가 선택 기준**. ⚠️ COCO 80 클래스 한정 — 마트 상품 fine-tuning 없으면 한계 (도메인 fine-tuning 이 변형 업그레이드보다 큰 이득).

### 모델 다운로드 / 재생성

```bash
# 1) YOLO11 (.pt) — ultralytics 자동, 또는 5개 한번에
python -c "from ultralytics import YOLO; [YOLO(f'yolo11{v}.pt') for v in 'nsmlx']"

# 2) Apple ml-mobileclip 코드
git clone https://github.com/apple/ml-mobileclip.git && cd ml-mobileclip && pip install -e . && cd ..

# 3) MobileCLIP-S2 체크포인트 (380MB)
mkdir -p ml-mobileclip/checkpoints
curl -L -o ml-mobileclip/checkpoints/mobileclip_s2.pt \
  https://docs-assets.developer.apple.com/ml-research/datasets/mobileclip/mobileclip_s2.pt

# 4) ONNX export
python -c "from ultralytics import YOLO; YOLO('yolo11l.pt').export(format='onnx', imgsz=320, opset=17, simplify=True)"
python export_mobileclip_s2.py
python clean_onnx_for_qnn.py yolo11l.onnx mobileclip_s2_image.onnx   # QNN 컴파일 호환 보정

# 5) w8a8 양자화 / QNN 컴파일 (Qualcomm AI Hub — 계정 + API 토큰 필요)
qai-hub configure --api_token <TOKEN>
python qai_hub_pipeline.py          # XR2 Gen 2 Proxy 타겟 (AR1 의 가장 가까운 proxy)
python quantize_mobileclip.py       # MobileCLIP-S2 → INT8 TFLite
```

Python 환경: `pip install ultralytics open-clip-torch torch onnx onnxruntime onnxslim onnxscript qai-hub h5py` (Python 3.10~3.12).

---

## 6. ONNX 그래프 분석 (NPU 호환성 레퍼런스)

Step A(ONNX export) 후 사전 점검 결과 (2026-06-06, **당시 YOLO 입력 320²** — 현재 배포는 640²). PyTorch↔ONNX 동등성: YOLO11n max-diff 1.28e-3 (FP32 노이즈, PASS), MobileCLIP-S2 코사인 유사도 **1.000000** (PASS).

| 모델 | 노드 수 | FP32 크기 | 입출력 | NPU 위험 연산 |
|---|---|---|---|---|
| YOLO11n | 320 | 10.12 MB | `[1,3,320,320]` → `[1,84,2100]` | ⚠️ Resize ×2 (FPN upsample — nearest/bilinear 면 OK) |
| MobileCLIP-S2 image enc | 664 | 136.32 MB | `[1,3,256,256]` → `[1,512]` (L2 정규화 그래프 포함) | ✅ 0개 (Loop/If/NMS/TopK/GridSample/Resize 전부 없음) |

- YOLO 출력 `84 = 4(xywh)+80(COCO)`, `2100 = 40²+20²+10²` (3 stride 격자).
- MobileCLIP-S2 = FastViT 백본 (Conv-heavy stem + Transformer: MatMul/Softmax/Erf=GELU). **위험 연산 0 → 전체 그래프 NPU 배치 가능.**
- MobileCLIP-S2 image encoder 파라미터 **~35.7M** (옛 "~5M" 오기 정정 — ViT-B-32 87M 의 ~40%).

> 위 표의 "예상 NPU 배치율/레이턴시"는 **실측으로 대체됨**: YOLO11l ~15-30ms, MobileCLIP-S2 ~3ms (§4). 그래프 시각화는 https://netron.app 에 `.onnx` 드래그.

---

## 7. 트러블슈팅

| 증상 | 원인 | 해결 |
|---|---|---|
| 앱 켜고 2분 hang | GyroTrigger sensor calibration 노이즈 | threshold 0.5 rad/s · duration 1.0s (v0.5.12, 이미 적용) |
| 카메라 권한 hang | Unity `RequestUserAuthorization` stereo dialog 안 보임 | `UnityEngine.Android.Permission` + `adb shell pm grant CAMERA` (build script 자동) |
| 화면 자동회전 | screenOrientation fullSensor | manifest landscape + PlayerSettings 회전 flag (v0.5.7) |
| SBS 양안 mismatch | OnGUI 전체화면 1회만 그림 | AdRenderer 가 양안 영역 각각 draw |
| 광고 안 뜸 (매칭 로그 없음) | `clip.isReady==false` | StreamingAssets 의 mobileclip tflite 존재 확인 |
| 코크인데 펩시로 / 항상 coca-cola | CLIP 환경 bias (brand fallback) | v0.7.3: `enableClipBrandFallback=false` (brand 는 OCR 전용) + environment-aligned ref 제작 |
| OCR 라벨 인식 안 됨 | 라벨이 작거나 옆으로 누움 | v0.7.3: `cropFraction` 0.55 + `upscaleFactor` 2.0 + `rotationOverride` |
| 한글 라벨 깨짐 | MLKit Latin 모델 + Unity 폰트 | 한국 시장용 `text-recognition-korean` 패키지 + NanumGothic 폰트 (백로그) |

---

## 8. 구현 백로그

- **OCR 라벨 영역 crop** — YOLO box → upscale → OCR (정확도 ↑, latency ↓)
- **Fine-tuned CLIP** (가장 근본적) — coke/pepsi contrastive learning 으로 embedding space 가 brand 분리 (v1.5+)
- **YOLO crop + CLIP** — bbox 만 CLIP → 환경 bias 제거
- **CLIP 모델 교체 후보** — SigLIP-2 (fine-grained ↑), DINO v2
- **다국어 OCR** — 한글 라벨
- **CropTexture GPU 최적화** — `GetPixels32` → `RenderTexture + Graphics.Blit`
- **trigger-based 단발 추론** 정합 — 현재 주기적, 기획안은 IMU 안정 시에만
- **w8a16 정밀도** — ORT+QNN EP path 재시도 (Maven native lib 충돌 해결 시)
- **Spatial Anchor** — 2D HUD → 3D 객체 옆 anchor (RayNeo ARDK 입수 후, v2+)
- **광고 인벤토리 확장** — 현재 1:1 conquest → 카테고리별 N:M

---

## 9. 실험 후보 — 양안 vergence(거리감) 보정 (미검증, 2026-06-08 기록)

> **상태: 아이디어 기록만. 미구현·미검증.** 시연 중 발견한 광학 이슈와 그 후보 해법.

**증상**: 양안에 동일 좌표로 광고를 그려서(`AdRenderer.cs:252`, disparity=0) 가상 화면이 **광학 무한대**로 느껴짐. 실제 객체(콜라병)는 0.3~0.5m라, 근거리에 눈을 모으면 HUD가 융합 안 됨(복시) → "한쪽 보면 다른 쪽 안 보임".

**원인 분리** (거리감 = 독립된 두 단서):
- **Vergence(폭주)** = 양안 horizontal disparity → **SW 조절 가능** ✅
- **Accommodation(초점)** = waveguide 고정 초점면 → **HW 고정, varifocal 없어 불가** ❌ (잔여 VAC)

**실험 후보 (#1, vergence만 해결)**: per-eye 그리기에 가로 disparity 주입 → 가상 카드를 유한 거리(시작값 ~1.0~1.5m)에 수렴.
```
α = atan((IPD/2)/d) ;  한 눈 코 방향 픽셀 이동 Δ = (α / HFOV_한눈) × W_한눈
좌안 +Δ (오른쪽), 우안 −Δ (왼쪽)  # crossed disparity = 근거리
```
- 적용 위치: `AdRenderer.DrawAdInEye` 의 `x` 계산에 `± NasalShiftPx` 추가. SDK 신기능 불필요(순수 2D 오프셋).
- **검증 필요 2가지**: ① SBS 좌/우 절반 ↔ 좌/우 눈 매핑 방향(부호) — 반대면 1줄 뒤집기 ② 크기는 `virtualDistanceMeters` 슬라이더로 스윕 튜닝(정확한 HFOV 몰라도 됨).
- **빠른 검증**: 픽셀 shift 하드코딩(예 좌+50/우−50) 빌드 → 0.5m 객체와 카드가 같이 융합되면 방향 OK.

**한계**: #1은 복시(vergence)만 해결. 초점 흐림(accommodation)은 HW 고정이라 못 고침 → 카드를 객체 거리에 붙이지 말고 **~1.0~1.5m 절충 거리**에 두고 시선 전환 UX 수용 (HoloLens 방식). 근본 해결은 varifocal/multifocal 광학 = v2+ HW 의제.

**확인 의뢰 항목**: RayNeo X3 Pro 고정 초점면 거리(comfort zone 결정) + SDK 의 IPD / 가상거리 / 스테레오 XR(per-eye projection) API 유무 (있으면 수동 시차 대신 world-space 3D quad 렌더가 정석).

**레퍼런스**: Kramida 2016 (VAC 서베이, IEEE TVCG) / Shibata et al. 2011 (zone of comfort) / Hoffman et al. 2008 (VAC fatigue) / 제품 절충: HoloLens 고정 초점면+comfort zone, Magic Leap One 이중 초점면 / 근본 HW: Dunn 2017·Oculus Half Dome(varifocal), Maimone 2017(holographic).
