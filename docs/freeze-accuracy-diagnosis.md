# Eagle Eye — Freeze & Accuracy 근본원인 진단 + b9/b10 계획

> 작성: 2026-06-11. 대상: 카메라/파이프라인 담당 개발자 + 그쪽 Claude.
> 범위: `spatial_anchor_test` (trigger→CLIP→OCR→world-anchored 경쟁사 광고) 통합 빌드의
> "시작 직후 화면 멈춤" + "코크/펩시 오인식" 두 문제의 근본원인과 해결 방향.
> 이 문서는 15개 서브에이전트 조사 + 온디바이스 포렌식(logcat/캡처/스크린샷)으로 검증된 사실만 담는다.

---

## 0. TL;DR — 별개의 두 근본원인

| # | 증상 | 진짜 원인 | 해결 |
|---|---|---|---|
| **A** | 로고 직후 화면 멈춤 / 빨간 박스 고정 / 글씨 깜빡 | **첫 실행 NPU 그래프 컴파일이 Unity 메인스레드를 152초 동결** (CLIP HTP 125초 + EasyOCR detector 27초). 이게 카메라 provider를 기아시켜 SIGPIPE 사망 → SLAM 발산(595m) | CLIP 컴파일 **백그라운드 스레드화**(즉효) + QNN context **프리베이크**(근본). OCR 경로 제거(skipOcr) |
| **B** | 펩시를 들어도 코크로 인식 → 반대 광고 / 빈 책상도 매칭 | **온디바이스 CLIP이 코크/펩시를 못 가름**(분리 마진 0.07 ≪ 양자화 노이즈 0.3). OCR(원래 판별기)은 모델 구조상 NPU 거부로 깨짐 | **색 히스토그램** brand 판별기(코크=빨강/펩시=파랑, 분리 마진 ~1.0). CLIP은 category(콜라병?)에만 사용 |

핵심: **A(멈춤)와 B(오인식)는 완전히 다른 문제다.** "화면 멈춤"은 코드 로직이 아니라 첫 실행 컴파일 블록이고, "오인식"은 CLIP zero-shot의 본질적 한계다.

---

## 1. 근본원인 A — 시작 직후 멈춤 = 152초 NPU 컴파일 (포렌식 확정)

### 1.1 무엇이 멈추나
`HelloAR.Update()`가 호출하는 CLIP/OCR 추론은 **동기 JNI `Interpreter.run`**이고, 그 인터프리터 **생성(=HTP 그래프 컴파일)**도 Unity 메인스레드에서 동기로 일어난다. 첫 실행에 캐시가 없으면:

- **CLIP HTP 컴파일: 125.2초** (logcat: `Interpreter init: 125228.6 ms`)
- **EasyOCR detector 컴파일: 27.1초**
- 합계 **~152초 메인스레드 동결**

검증(cap.log 포렌식): launch(00:08:52) 후 게임 uptime이 **160초 벽시계 동안 3.7s→4.7s(=1.0초)만 진행**. SensorService 큐 포화(00:09:06~00:11:30)가 동결 구간을 정확히 괄호친다. **앱은 pause된 적 없다**(XR FOCUSED 유지·착용중·충전중·screenOn). ANR도 안 뜸 — Android main thread가 아니라 Unity 스레드 동결이라서.

### 1.2 왜 SLAM이 발산하나 (2차 효과)
152초 동기 컴파일이 DSP/메모리/스케줄러를 점유 → 카메라 provider(`vendor.camera-provider-2-7`)의 실시간 스레드를 기아 → IMU data loss·frame drop 급증 → **00:10:44 SIGPIPE로 provider 사망**(Camera 0+1 동시 Broken pipe). provider는 즉시 재기동하지만 **`com.rayneo.xr.runtime`이 카메라를 재연결하지 않음**(retry 로그 0건) → SLAM이 vision 없이 IMU 단독 적분 → 동결 풀릴 때(47초 후) 이미 camPos ~250m, 이후 595m까지 발산.
- 주의: 발산해도 `GetHeadTrackerStatus`는 계속 **0(=초기화 미완)**으로만 보고됨. status는 pose 신뢰도 지표가 아니므로 앱이 신뢰하면 안 됨. → 앱 자체 `|camPos|>임계` 휴리스틱이 유일하게 유효.

### 1.3 QNN 캐시가 작동하지 않는 이유
- `ClipExtractor.cs`는 StreamingAssets에서 `mobileclip_s2.qnn_context.bin`(프리베이크) 을 찾는 코드가 **이미 있으나, 그 자산이 없다** → 항상 컴파일 경로.
- `QnnClipEngine`은 `setCacheDir`+`setModelToken`으로 디스크 캐시를 시도하지만, **qnn-litert-delegate 2.47.0의 이 조합은 디스크에 .bin을 쓰지 않는다**(device-level **휘발성 DSP 커널 캐시**만 효과). 디바이스에 남은 캐시 파일은 20B/1.6KB **stub뿐** — 실제 수 MB 컴파일 바이너리가 아님.
- 커널 캐시는 process death/DSP 전원/모델 swap/reboot 시 evict → 매 cold start 125초 재컴파일. 빌드·재설치가 캐시를 지우는 것은 **아님**(애초에 디스크 캐시가 없음).
- `EasyOCREngine`은 `setCacheDir`조차 호출 안 함 → detector 27초도 같은 캐시미스.

---

## 2. 근본원인 B — 코크/펩시 brand 판별 불가 (정량 확정)

### 2.1 CLIP brand fallback의 분별력 (db/embeddings 실측, MobileCLIP-S2 512-d)
| 측정 | 코사인 |
|---|---|
| 같은 코크끼리 평균 | 0.871 |
| 같은 펩시끼리 평균 | 0.883 |
| **코크↔펩시 평균** | **0.805** (max 0.824) |
| **분리 마진** | **0.067~0.079** |

- **온디바이스 CLIP sim은 Mac 대비 ~0.3 낮다**(w8a8 양자화 + 전처리 불일치). 노이즈(0.3) ≫ 마진(0.07) → 마진이 완전히 잠식됨.
- **환경 편향**: 코크 ref 4장 전부 "빨간 책상"(jetson_coke), 펩시 ref 2장은 야외/매장 → 데모 책상 환경이 코크 분포와 매칭 → 펩시를 들어도 코크 sim이 높게 나옴.
- **실측 실패**: 과거 "펩시 15 trigger 전체 매칭 0건(코크 7, laptop 8)". CLIP 단독 brand 판별은 **사실상 항상 코크**.

### 2.2 category(콜라병?)도 약함
- **cola↔laptop centroid 코사인 0.887** — 콜라와 노트북이 코크↔펩시보다 더 닮음.
- 온디바이스 실측: 콜라 없는 천장/모니터 장면이 `cola=0.605`, `laptop=0.627` (마진 0.022). threshold 0.45를 넘으므로 **빈 장면 오탐** 가능. threshold 0.45는 온디바이스 격차를 가리는 임시 보정값.
- 원래 OCR strict 게이트가 이 오탐을 막았으나, OCR이 죽어(아래) 안전망이 사라짐.

### 2.3 OCR(원래 brand 판별기)이 깨진 이유
- recognizer(CRNN)는 **2단 BiLSTM이 TFLite `WHILE` control-flow op**(82개 LSTM 토큰)로 구현됨 → **QNN HTP delegate가 위임 거부**(`Error applying delegate`). detector(CRAFT, conv-only)는 정상.
- CPU(XNNPACK)로 강등하면 로드는 가능하나, **양식화 로고(Coca-Cola 필기체)+곡면 PET 라벨+안경 시점 각도**라 인식률 낮음(과거 1/13). 단독 판별기로 부적합.

### 2.4 해결 = 색 히스토그램 (광고/제품 이미지 실측)
| | 빨강 픽셀 | 파랑 픽셀 |
|---|---|---|
| 코크 | **96.7%** | 0% |
| 펩시 | 0% | **97.7%** |

- **분리 마진 ~1.0** (CLIP의 0.07 대비 10배+). 양자화·조명에 거의 안 뒤집힘.
- NPU 불필요, CLIP이 이미 GetPixels32로 읽는 중앙 crop 픽셀에서 R/B 카운트 한 번 = O(픽셀).
- **통합층(HelloAR) additive** — ProductMatcher 코어 비변경(팀 코드 존중).
- 빨강·파랑 둘 다 약하면 brand 미확정 → 광고 X (laptop/빈 장면 2차 게이트, OCR 게이트 공백 보완).
- 한계: "클래식 빨강 코크 vs 파랑 펩시" 가정(Zero/Diet 변종 제외) — 데모 범위엔 충분.

---

## 3. b9resilient — 적용된 fix (빌드/설치 완료, 착용 검증 대기)

`feature/slam-clip-worldanchor`, versionName=`b9resilient`, versionCode 14.

1. **skipOcr=true** — OCR 엔진 init(27초)+추론 메인스레드 블록 제거. CLIP brand fallback으로 우회(임시).
2. **adShowingUntil** — 광고 표시 중 트리거 무시(재트리거 폭주 차단).
3. **saveTriggerFrames=false** — 핫패스 jpg encode+disk write 제거.
4. **CameraPreview.OnApplicationPause** — pause시 카메라 close/resume시 reopen (provider SIGPIPE 예방).
5. **SpatialAnchorTest 발산 감지** — `|camPos|>30m` 2초 지속 시 콘텐츠를 카메라 앞으로 재앵커 + HUD `DIVERGED` 표시(10초당 1회). SLAM 자체 리셋은 아님(SDK에 리셋 API 없음).
6. **BuildSpatialAnchorTest.cs** — 빌드환경 폴백(Temurin JDK 17 / 6000.0.76f1 SDK의 cmake·NDK / embedded Gradle 8.13). 이게 없으면 이 머신에서 빌드 실패.

**한계(중요)**: b9는 OCR 27초만 제거하고 **CLIP 125초 컴파일은 그대로** → 첫 cold 실행은 여전히 ~125초 동결한다. 그리고 brand 판별을 (불안정한) CLIP fallback에 맡긴 상태. → **b10이 둘 다 해결해야 데모 성립.**

> 검증 메모: XR 앱은 **착용(XR 세션 FOCUSED) 상태에서만 렌더 루프가 돈다.** adb wake만으로는 `mCurrentFocus=null`이라 측정 불가 → b9/b10 startup 측정은 착용 필요.

---

## 4. b10 계획 — Freeze + Accuracy 동시 해결

| 우선 | 작업 | 효과 | 위치 |
|---|---|---|---|
| 1 | **색 brand 판별기** (CLIP=category, 색=brand). 플래그 `brandDisambiguator`로 색↔clip↔ocr 교체 | 코크/펩시 신뢰 판별(마진 1.0) | HelloAR + ClipExtractor (additive) |
| 2 | **CLIP 컴파일 백그라운드 스레드화** (Java single-thread ExecutorService에서 init+run, isReady 콜백) | 125초 동결 제거(로딩 스피너로 격하) | QnnClipEngine + ClipExtractor |
| 3 | (root) **QNN context 프리베이크** — qai_hub_pipeline로 v73 타겟 `.qnn_context.bin` 생성→StreamingAssets 번들 | 컴파일 자체 0초 | 자산 + delegate 로드 검증 필요 |
| 4 | b9 fix 유지 (skipOcr/pause-camera/발산-재앵커) | provider 보호·발산 복구 | — |

**미확정(확인 필요)**: 프리베이크 .bin을 delegate가 실제로 deserialize하는지(2.47.0 API), v73 정확 타겟이 AI Hub에 있는지. → 1차는 백그라운드 스레드화(확실)로 가고 프리베이크는 검증 후 적용.

---

## 5. 서브에이전트 조사 색인 (증거 출처)

- 영상크래시 H1 → CloseCamera/MediaCodec 경합 가설(포렌식이 정정)
- CloseCamera/SLAM 결합 → 이미 비활성, HAL 가설
- 작동버전 diff → baseline=`de0be34`(standalone, 카메라/CLIP 없음)
- post-match 블로커 → NPU 동기 블록 지목
- 디바이스 HAL 상태 → provider 1회 크래시·자가복구, xr.runtime이 양 카메라 점유
- **cap.log 포렌식 → 152초 컴파일 동결(결정타)**
- 아키텍처 브레인스톰 → 7옵션
- NPU 스레딩 타당성 → Interpreter 스레드 바인딩, Java executor 최소변경
- QNN 캐시 → disk 캐시 미작동(stub만), 백그라운드+프리베이크
- SLAM 복구 API → 리셋 API 없음, `|camPos|` 휴리스틱 검증
- **CLIP brand 정확도 → 마진 0.07, 색 마진 1.0(결정타)**
- OCR 타당성 → BiLSTM WHILE → HTP 거부, CPU폴백 가능하나 로고 불안정

## 6. 핵심 파일
- `Assets/Scripts/HelloAR.cs` — 파이프라인 오케스트레이션, 트리거, b9 fix, (b10) 색 판별 통합 지점
- `Assets/Scripts/ClipExtractor.cs` — CLIP 전처리(중앙 crop GetPixels32 — 색 판별 재사용), 동기 init(:120, 메인스레드 블록), 프리베이크 탐색(:81-95)
- `Assets/Plugins/Android/QnnClipEngine.java` — Interpreter 동기 컴파일(:153), 캐시 옵션(:137, disk 미작동)
- `Assets/Plugins/Android/EasyOCREngine.java` — recognizer BiLSTM WHILE(HTP 거부), 캐시 옵션 없음
- `Assets/Scripts/ProductMatcher.cs` — category/brand 매칭, CLIP brand fallback(:266)
- `Assets/Scripts/CameraPreview.cs` — ShareCamera 라이프사이클, OnApplicationPause
- `Assets/Scripts/SpatialAnchorTest.cs` — SLAM 6DoF, 발산 감지/재앵커, world-anchored ad spawn
- `Assets/Editor/BuildSpatialAnchorTest.cs` — 빌드(OpenXR 로더 fix + 빌드환경 폴백)
- `db/embeddings/*.npy` — category/brand CLIP ref (코크 4·펩시 2, 환경편향), `db/ads/*.png` — 색 분리 96.7%/97.7%
