# Spatial Anchor 통합 빌드 — 개발자 핸드오프 + 빌드 런북

> 작성: 2026-06-11. 대상: `spatial_anchor_test` 통합 빌드를 **지금 당장 이어서 개발할** 카메라/파이프라인 담당 개발자.
> 범위: conquest 데모(트리거 → CLIP category → 색 brand → world-anchored 경쟁사 광고)의 아키텍처·빌드·현재 상태·이슈·함정.
> 깊은 근본원인 진단(freeze 152초 / CLIP 0.07 마진)은 자매 문서 [`freeze-accuracy-diagnosis.md`](freeze-accuracy-diagnosis.md) 가 담당 — 본 문서는 그 위에서 **"어떻게 빌드하고 무엇을 이어서 할지"** 에 집중한다.
> 모든 주장은 코드 file:line / git log / 기존 docs 근거. 검증 안 된 추정은 "미확정" 으로 표기.

---

## 0. 한눈에

- **프로젝트**: `spatial_anchor_test/` (Unity 2022.3.62f3, RayNeo X3 Pro / Snapdragon AR1 Gen1 / Hexagon v73 NPU)
- **현재 빌드**: `b14` (versionName=`b14`, `BuildSpatialAnchorTest.cs:27`), 패키지 `com.eagleeye.spatialanchor.bisection`
- **데모**: 콜라/펩시 페트병을 응시 → 반대 진영 경쟁사 광고가 정면 시야에 world-anchored 로 뜸 (conquest)
- **on-device 검증됨**: freeze 해결(b9~b11), 색 brand(b11), 정면배치(b12), max2 FIFO + 가로반전 + 평균색(b13/b14)
- **★ 빌드는 반드시 Unity 2022.3.62f3** — Unity 6 (6000.0.76f1) 로 빌드하면 검은화면 + 프로젝트 오염 (§2.0)

---

## 1. 아키텍처 / 데이터 플로우

### 1.1 conquest 파이프라인 (한 줄)

```
gaze-dwell 트리거 → 카메라 프레임 → CLIP category("콜라병?")
   → 색(평균 RGB) brand 판별 (빨강→coca-cola / 파랑→pepsi)
   → 경쟁사 매핑 (코크→펩시광고 / 펩시→코크광고)
   → 정면(응시 지점)에 world-anchored 광고 quad spawn (최대 2개, FIFO)
```

### 1.2 컴포넌트 + 역할 + 핵심 파일

| 컴포넌트 | 역할 | 파일 |
|---|---|---|
| **GyroTrigger** | IMU 자이로(`Input.gyro.rotationRateUnbiased`, rad/s)로 "시선 안정" 감지. 3축 절대값이 `stableThreshold` 이하로 `stableDuration` 지속 시 `OnTrigger` 발화. 데모 셋팅 = threshold 0.5 / duration 1.0s / cooldown 5s (cooldown 모드) | `Scripts/GyroTrigger.cs` (셋팅 강제는 `HelloAR.cs:108-113`) |
| **CameraPreview** | RayNeo **ShareCamera**(병렬 채널 RGB 카메라) 라이프사이클. `OpenCamera`/`CloseCamera` 분리. `cam.webCamTex` 로 최신 프레임 노출. SLAM 6DoF(`CameraAttitudeType=8193`) 활성 시 표준 WebCamTexture 가 black frame 만 내보내던 문제를 ShareCamera 로 우회 | `Scripts/CameraPreview.cs` |
| **HelloAR** | **파이프라인 오케스트레이터.** 트리거 수신 → 프레임 → CLIP → 색 brand → 경쟁사 매핑 → `spatial.ShowAdBesideMatch(...)`. 모든 데모 플래그(skipOcr, clipOnlyMode, brandDisambiguator, 색 마진)의 단일 셋업 지점 | `Scripts/HelloAR.cs` |
| **ClipExtractor** | MobileCLIP-S2 INT8 임베딩(512-d). `Embed()` 가 텍스처 → 중앙 crop 256² → NCHW normalize → JNI. **같은 전처리 루프에서 중앙 박스 평균색(`lastMeanR/G/B`)을 산출** — 색 brand 판별의 입력 | `Scripts/ClipExtractor.cs`, `Plugins/Android/QnnClipEngine.java` |
| **ProductMatcher** | CLIP 임베딩 → category 매칭(`MatchCategory`, top-K 코사인, threshold 0.45). brand 해석(`ResolveBrand`)도 있으나 데모 경로는 색 판별이 대체. `GetBrandByName` 로 "coca-cola"/"pepsi" → Brand 객체 매핑(read-only) | `Scripts/ProductMatcher.cs` |
| **SpatialAnchorTest** | RayNeo OpenXR ARDK **6DoF SLAM** + world-anchored 광고 spawn. `ShowAdBesideMatch` 가 응시 지점(camPos + camFwd·0.5m)에 광고 quad 를 spawn(world 고정, head re-follow X). max2 FIFO. SLAM 발산 감지(`|camPos|>30m`) → 콘텐츠 재앵커. HUD(world-space TextMesh) | `Scripts/SpatialAnchorTest.cs` |

### 1.3 색 brand 판별 (핵심 로직)

CLIP 은 **category 만**("콜라병인가?") 판단하고, **brand(코크/펩시)는 색으로** 가른다.
근거: 온디바이스 CLIP 의 코크↔펩시 분리 마진 0.07 ≪ 양자화 노이즈 0.3 → 항상 코크로 오판. 색 마진은 ~1.0 (코크 빨강 96.7% / 펩시 파랑 97.7%). 상세 정량근거는 `freeze-accuracy-diagnosis.md` §2.

- `ClipExtractor.PreprocessTexture` (`ClipExtractor.cs:231-280`): NCHW 정규화와 **같은 루프**에서 중앙 `colorSampleFraction`(0.8) 박스의 채널 합을 누적 → `lastMeanR/G/B` (별도 readback 없음).
- `HelloAR.ResolveBrandByColor` (`HelloAR.cs:329-364`):
  - `blueLean = mB - mR`, `redLean = mR - mB`
  - `blueLean > colorBlueMargin(5)` → **pepsi** (코크는 파랑 거의 0이라 평균이 조금만 파랑이어도 펩시)
  - `redLean > colorRedMargin(25)` → **coca-cola** (FP 차단 위해 빨강은 강하게 우세해야)
  - 둘 다 약하면 **null → 광고 X** (빈 책상/노트북 false positive 2차 게이트). 여기서 OCR/CLIP fallback 으로 떨어지면 안 됨 — `enableClipBrandFallback=true` 인 인스턴스가 환경편향으로 "항상 coca-cola" 를 재발(`HelloAR.cs:236-240` 주석).
- 경쟁사 매핑 (`HelloAR.cs:158-163` `CompetitorAdVideo`): `coca-cola → pepsi 광고`, `pepsi → coke 광고`.

---

## 2. ★ BUILD RUNBOOK (가장 중요)

### 2.0 ⚠️ 반드시 Unity 2022.3.62f3 — Unity 6 빌드 절대 금지

- **올바른 에디터**: `C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe`
- **`ProjectSettings/ProjectVersion.txt` 가 `2022.3.62f3` 임을 빌드 전에 확인.**
- **Unity 6000.0.76f1 로 열거나 빌드하지 말 것.** RayNeo ARDK 비호환으로:
  - 안경에서 **Unity 로고조차 안 뜨는 검은화면** (실제 발생)
  - `ProjectVersion.txt` 와 일부 settings 가 6000 으로 오염됨 (실제 발생 → `git checkout` 으로 복구함)
- 만약 실수로 6000 으로 열어 오염됐다면: `git -C <repo> checkout -- spatial_anchor_test/ProjectSettings/` 로 되돌리고 다시 2022.3.62f3 로 연다.

### 2.1 batchmode 빌드 명령

```bash
"C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" \
  -batchmode -quit -nographics -silent-crashes \
  -projectPath C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test \
  -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/Build/build.log
```

- 진입점 = `BuildSpatialAnchorTest.PerformBuild` (`Editor/BuildSpatialAnchorTest.cs:30`). 출력 = `Build/EagleEye-SA-b14.apk` (`OUTPUT_APK`, line 24).
- 성공 시 로그에 `=== SUCCEEDED ===`, 실패 시 `=== FAILED ===` + `EditorApplication.Exit(1)`.
- 빌드 hook 이 자동 처리하는 것(직접 손대지 말 것):
  - `EnsureOpenXRLoader` — Android XR 로더를 진짜 `UnityEngine.XR.OpenXR.OpenXRLoader` 로 재할당 (잘못되면 6DoF 죽음, dead-6DoF 커밋 167380d 의 fix).
  - `ConfigureExternalTools` — JDK/SDK/NDK/Gradle 경로를 **실행 에디터 기준**으로 강제 set (batchmode 는 GUI Preferences 를 못 봄).
  - `EnsureRayNeoSettingsPreloaded` — `RayNeoGeneralSettings.asset` 을 PreloadedAssets 에 주입 (없으면 런타임 SLAM pose 죽음).
  - `RayNeoBootConfigPatcher` (IPostGenerateGradleAndroidProject) — `boot.config` 에 `xrsdk-pre-init-library=UnityOpenXR` + `gfx-disable-mt-rendering=1` 주입 + `libUnityOpenXR.so` 강제 복사 (vendor 작동 APK 와 일치시켜 head-locked 방지).

### 2.2 설치 / 권한 / 실행

```bash
SER=A06B4A95B784973   # 안경 시리얼 (adb devices 로 확인)
PKG=com.eagleeye.spatialanchor.bisection

adb -s $SER shell am force-stop $PKG
adb -s $SER install -r .../spatial_anchor_test/Build/EagleEye-SA-b14.apk
adb -s $SER shell pm grant $PKG android.permission.CAMERA
adb -s $SER shell am start --activity-clear-task \
  -n $PKG/com.rayneo.openxradapter.UnityOpenXrActivity

# logcat
adb -s $SER logcat -s "Unity:V" "QnnClipEngine:V" "RayNeoXR:I"
```

> ⚠️ ADB는 안경팀과 공유 환경. install/push/force-stop 등 **writing 작업은 사전 허락**. reading 은 자유. 무선 ADB 끊지 말 것.

### 2.3 빌드 후 검증 (Bee 캐시 stale 방지 — 반드시)

Unity 의 Bee 빌드 캐시가 stale 하면 APK 가 코드 변경을 반영 못 한 채 빌드된다. **설치된 빌드가 실제 그 빌드인지 versionName 으로 확인:**

```bash
# (a) APK 자체의 versionName
aapt dump badging .../Build/EagleEye-SA-b14.apk | grep versionName
# (b) 설치된 앱의 versionName
adb -s $SER shell dumpsys package $PKG | grep versionName
```

- 둘 다 **`BUILD_TAG` 와 일치**해야 함 (`b14`). 불일치면 Bee 캐시 stale → `Library/Bee` 정리 후 재빌드.
- **`ProjectVersion.txt == 2022.3.62f3` 유지** 도 빌드 후 재확인 (6000 오염 회귀 방지).

### 2.4 `ConfigureExternalTools` 가 하는 일 (이 머신 한정 빌드 환경 폴백)

batchmode 가 GUI Preferences 를 못 보므로 `BuildSpatialAnchorTest.cs:199-256` 이 도구 경로를 직접 set 한다:

- **JDK**: 실행 에디터 번들 OpenJDK → 없으면 Temurin `jdk-17.0.16.8-hotspot` 폴백.
- **SDK**: 실행 에디터 번들 SDK 를 **cmake 유무와 무관하게 무조건 set** (line 233~239). 과거엔 cmake 3.22.1 존재 여부로 게이트했으나, 이 프로젝트는 **네이티브 C++ 소스(CMakeLists)가 없고 모든 native 가 prebuilt `.so`** 라 cmake 가 불필요 → 게이트 때문에 SDK 미설정 → "Android SDK not found" 실패하던 것을 제거함.
- **NDK**: 번들 NDK set.
- **Gradle**: `EditorPrefs("GradleUseEmbedded", true)` — 번들 Gradle 사용 (custom Gradle 이 구버전 가리켜 실패하던 것 방지).

> 이 폴백은 **실행한 에디터 기준** 이라, 2022.3.62f3 로 실행하면 그 에디터 번들 도구를 쓴다. 따라서 §2.0 의 "올바른 에디터로 실행" 이 도구 경로 정합성까지 보장한다.

### 2.5 ⚠️ 버전 부채 — BUILD_TAG / OUTPUT_APK 2곳 동기 수정

새 빌드 버전을 낼 때 `BuildSpatialAnchorTest.cs` 에서 **두 상수를 같이** 바꿔야 한다 (안 하면 APK 파일명과 versionName 이 어긋남):

```csharp
const string OUTPUT_APK = "Build/EagleEye-SA-b14.apk";  // line 24  ← 파일명
const string BUILD_TAG  = "b14";                         // line 27  ← versionName 스탬프
```

`bundleVersionCode` 는 `ConfigurePlayerSettings` 에서 매 빌드 +1 자동 증가(`line 266`).

---

## 3. 현재 상태 (on-device 검증됨, b9 → b14)

| 버전 | 변경 | 검증 |
|---|---|---|
| **b9resilient** | freeze 1차 완화: `skipOcr=true`(OCR 27초 init 제거), `adShowingUntil`(재트리거 게이트), `saveTriggerFrames=false`(핫패스 디스크쓰기 제거), `OnApplicationPause`→카메라 close(provider SIGPIPE 예방), `\|camPos\|>30m` 발산 감지→재앵커 | 빌드/설치 (착용 검증 대기였음) |
| **b10** | (계획) CLIP 컴파일 백그라운드 스레드화 + 색 brand | — |
| **b11** | **단일 경쟁사 광고 + 색 brand conquest 검증** (커밋 ed4037f). CLIP 컴파일을 Java single-thread `ExecutorService` 로 격리 → 메인스레드 동결 제거(provider 생존). 색 brand(코크 red→펩시 / 펩시 blue→코크) | ✅ on-device |
| **b12** | 광고 배치 옆칸 → **정면(응시 지점)**, world-anchored (커밋 feb64f0) | ✅ on-device |
| **b13+b14** | **max2 FIFO** 광고 누적, 텍스처 **가로 반전** 교정, **평균색(mean RGB)** brand 판별로 전환 (커밋 e3f24ef) | ✅ on-device |

핵심 fix 메커니즘:

- **Freeze 해결** = CLIP HTP 컴파일(첫 실행 ~125초)을 `QnnClipEngine` 의 single-thread `ExecutorService` worker 로 옮김 (`QnnClipEngine.java:59-60, 71-110`). `initialize/initializeFromContextBin` 은 submit 후 즉시 true 반환, 실제 준비는 `isReady()`(volatile flag) 폴링. `ClipExtractor` 는 0.5s 간격으로 `isReady` 폴링하며 "CLIP 컴파일 중..." 표시(180초 타임아웃) (`ClipExtractor.cs:160-179`). → **메인스레드(렌더) 안 멈춤, 카메라 provider 생존**. TFLite Interpreter 의 생성·실행 동일 스레드 바인딩 제약 때문에 `embed()` 도 같은 worker 에서 실행(`QnnClipEngine.java:212-225`).
- **색 brand** = §1.3. 평균색 lean + `blueMargin(5)`/`redMargin(25)`.
- **정면 배치** = `ShowAdBesideMatch` 의 `adPos = objectPos = camPos + camFwd·0.5m`, spawn 시점 1회 `LookRotation(-camFwd)` (billboard X, 진짜 world-anchored) (`SpatialAnchorTest.cs:269-273`).
- **max2 FIFO** = `adQuads` 리스트, `maxAds=2` 초과 시 앞(오래된 것)부터 Destroy (`SpatialAnchorTest.cs:297-302`).
- **가로 반전** = quad 가 사용자 향해 180° 회전 시 텍스처 거울상 → `mainTextureScale=(-1,1)`, `offset=(1,0)` (`SpatialAnchorTest.cs:333-335`).

---

## 4. 알려진 이슈 + 계획

### (a) cold 실행마다 NPU 컴파일 ~95-168초
- 매 cold start 마다 CLIP HTP 그래프 재컴파일(`Interpreter init` 로그 기준 ~125초, 관측 범위 95-168초). **백그라운드 스레드라 freeze 는 아님** — 컴파일 동안 렌더는 ~15fps 로 계속 돌고 "CLIP 컴파일 중..." 표시.
- 디스크 캐시가 작동 안 하는 이유: qnn-litert-delegate 2.47.0 의 `setCacheDir`+`setModelToken` 조합이 디스크에 .bin 을 안 쓰고 **휘발성 DSP 커널 캐시**만 효과 → process death/reboot 시 evict (상세 `freeze-accuracy-diagnosis.md` §1.3).
- **계획**: (1) QNN context **프리베이크** — AI Hub v73 타겟 `.qnn_context.bin` 생성 → StreamingAssets 번들. `ClipExtractor` 와 `QnnClipEngine.initializeFromContextBin` 경로는 **이미 구현돼 있음** — 자산만 없을 뿐(`ClipExtractor.cs:95-112`, `QnnClipEngine.java:92-110`). 미확정: 2.47.0 delegate 가 프리베이크 .bin 을 실제 deserialize 하는지, AI Hub 에 정확 v73 타겟이 있는지(검증 후 적용). (2) 프리워밍.

### (b) SLAM ~8Hz 로 느림
- world-anchor pose 갱신이 ~8Hz 로 느려 앵커가 머리 움직임을 매끄럽게 못 따라옴.
- **원인 미확정**: ShareCamera 연속 프리뷰가 SLAM 카메라 채널과 경합하는지 vs 펌웨어/SDK mismatch(§5) 인지 불명.
- **저위험 방향(측정 우선)**: 프리뷰를 640×480@10fps 로 낮춰 RGB 채널 부하 감소 후 SLAM Hz 재측정 → 효과 확인되면 트리거 시점만 단발 `takePicture` 로 전환(상시 프리뷰 대신). 측정 없이 takePicture 로 바로 가지 말 것.

### (c) 펩시 빨간 뚜껑 색 오판
- 펩시 병뚜껑이 빨강이라 과거 dominant-count 방식에서 `red=0.41 blue=0.00` 으로 코크 오판됨.
- **현재 보정**: 평균색(면적 큰 파란 몸통이 평균을 파랑으로 끌어줌) + `colorBlueMargin=5`/`colorRedMargin=25` (파랑은 약하게도 통과, 빨강은 강해야 통과). 추가 실측 보정 진행 중.

---

## 5. 함정 (gotchas)

- **Unity 6 금지** — §2.0. 검은화면 + ProjectVersion 오염. 빌드는 무조건 2022.3.62f3.
- **adb v40/v41 충돌** — 머신에 다른 adb 가 공존하면 버전 충돌로 디바이스가 끊겼다 재연결됨. `adb devices` 로 시리얼 재확인. 안경팀 공유 — writing 사전 허락.
- **doze / XR 착용 게이트** — XR 앱은 **착용(XR 세션 FOCUSED) 상태에서만 렌더 루프가 돈다.** `adb shell input keyevent KEYCODE_WAKEUP` 만으로는 `mCurrentFocus=null` 이라 startup/freeze 측정 불가 → **반드시 착용 상태로 측정**.
- **카메라 FOV ≠ 눈높이** — ShareCamera RGB 의 화각이 눈 시선과 정확히 일치하지 않음. 데모 시 **병을 노트북(테이블) 높이로** 들어 카메라 FOV 안에 확실히 들어오게 해야 트리거/색 판별이 안정적.
- **provisional 마커 숨김** — `cola_anchor` provisional 마커는 `showAnchorMarker=false` (`SpatialAnchorTest.cs:26-27`) 로 숨김. 위치 추적/HUD drift 계산은 유지(quad 는 살아있고 invisible). conquest 데모는 경쟁사 광고만 보여야 하므로 true 로 바꾸지 말 것(디버그 시만).
- **RayNeo SDK 1.1.6 ↔ Runtime 1.1.7.9 mismatch** — 안경 펌웨어 Runtime(1.1.7.9)과 번들 ARDK SDK(1.1.6) minor 버전 불일치 로그가 뜸(`integration_log.md` §2 BLOCKER). standalone 빌드에서도 동일하게 재현 → 우리 코드 무관, system service level. 정통 해법은 ARDK 1.1.7+ SDK 입수(RayNeo portal). 현재는 즉시 spawn + native polling 으로 우회 중.
- **충돌 시 우선순위** (루트 CLAUDE.md): 클라이언트 동작은 `client-spec.md`, 비즈니스/시나리오는 `vision.md`. 본 문서는 통합 빌드의 구현 현황.

---

## 6. 핵심 파일 맵 + 다음 작업 우선순위

### 6.1 파일 맵

| 파일 | 무엇 |
|---|---|
| `spatial_anchor_test/Assets/Scripts/HelloAR.cs` | 파이프라인 오케스트레이터. 모든 데모 플래그 단일 셋업. 색 brand 통합 지점(`ResolveBrandByColor`, `:329`) |
| `spatial_anchor_test/Assets/Scripts/ClipExtractor.cs` | CLIP 임베딩 + 중앙 crop 전처리 + **평균색 산출**(`:248-279`). worker `isReady` 폴링(`:160-179`) |
| `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java` | CLIP HTP 컴파일을 **single-thread worker 로 격리**(`:59-110`). 프리베이크 경로(`:92`). HTP 캐시 옵션(`:151-162`, 디스크 미작동) |
| `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs` | 6DoF SLAM, world-anchored 광고 spawn(`ShowAdBesideMatch :259`), max2 FIFO(`:297`), 가로반전(`:333`), 발산 재앵커(`:422-443`), HUD |
| `spatial_anchor_test/Assets/Scripts/CameraPreview.cs` | ShareCamera 라이프사이클, `OnApplicationPause` close(`:210`) |
| `spatial_anchor_test/Assets/Scripts/ProductMatcher.cs` | category 매칭(`MatchCategory :185`), `GetBrandByName`(`:326`), CLIP brand fallback(`:266`, 데모 경로에선 우회) |
| `spatial_anchor_test/Assets/Scripts/GyroTrigger.cs` | gaze-dwell 트리거 |
| `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs` | batchmode 빌드 진입점 + OpenXR 로더 fix + 빌드환경 폴백 + boot.config 패처 |
| `docs/freeze-accuracy-diagnosis.md` | freeze/정확도 근본원인 정량 진단(자매 문서) |
| `docs/integration_log.md` | 통합 timeline + SDK mismatch BLOCKER |

### 6.2 다음 작업 우선순위

1. **(b) SLAM Hz 측정 → 프리뷰 해상도 낮춰보기** — 640×480@10fps 로 SLAM Hz 재측정(저위험). 효과 확인 후에만 takePicture 단발 전환. world-anchor 부드러움이 데모 체감에 직결.
2. **(a) QNN 프리베이크 검증** — AI Hub v73 `.qnn_context.bin` 생성 → StreamingAssets 번들 → `initializeFromContextBin` 가 실제 deserialize 하는지 on-device 확인. 성공 시 cold 컴파일 ~125초 → ~수백 ms. (구현은 이미 있음, 자산·검증만 남음.)
3. **(c) 색 마진 실측 보정** — 펩시 빨간뚜껑/조명 변화 케이스 추가 캡처로 `colorBlueMargin`/`colorRedMargin` 튜닝.
4. 새 버전 빌드 시 §2.5 (BUILD_TAG/OUTPUT_APK 2곳) + §2.3 (versionName 검증) 루틴 준수.
</content>
</invoke>
