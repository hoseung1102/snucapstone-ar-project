# AGENTS.md — Codex 작업 지침 (Eagle Eye conquest AR)

> 이 파일은 Codex CLI 가 이 레포에서 작업할 때 자동 로드하는 프로젝트 지침이다 (Claude 의 `CLAUDE.md` 와 동일 역할).
> 중첩 디렉토리의 `AGENTS.md`(예: `spatial_anchor_test/AGENTS.md`)도 함께 읽힌다.
> **사실 정확성 우선** — 여기 적힌 모든 수치/경로/동작은 코드·docs 에서 검증된 것이다. 의심되면 `docs/` 의 상세 문서와 `file:line` 을 직접 확인하라.

---

## 1. 프로젝트 개요

**Eagle Eye** — AR 글라스(**RayNeo X3 Pro**, Snapdragon AR1 Gen 1, Hexagon v73 NPU, **FP16 유닛 없음 → w8a8 양자화 필수**)용 온디바이스 광고/정보 인프라의 v1 PoC. "경쟁사 상품을 손에 든 순간 비교 정보를 시야에 띄운다"(**conquest**)가 핵심.

현재 활발히 개발 중인 통합 데모는 `spatial_anchor_test/` 이며, 핵심 플로우는:

```
gaze-dwell 트리거 → 카메라 프레임 → CLIP category("콜라병인가?")
  → 중앙 crop 평균색(mean RGB)으로 brand 판별 (빨강→coca-cola / 파랑→pepsi)
  → 경쟁사 매핑 (coca-cola→펩시 광고 / pepsi→코크 광고)
  → 응시 지점에 world-anchored 광고 quad spawn (최대 2개, FIFO)
```

데모 시연 대상은 **콜라/펩시 페트병 + 노트북** 한 장면이다. 라면·마트 시나리오는 원 기획 잔재이니 프로젝트를 "마트 쇼핑 앱"으로 좁히지 말 것 (근거: `docs/vision.md` §1.2, 루트 `CLAUDE.md`).

> **왜 색으로 brand 를 가르나**: 온디바이스 CLIP 의 코크↔펩시 분리 마진은 0.07 로 양자화 노이즈(~0.3)에 완전히 잠식 → 항상 코크로 오판. 반면 색 분리 마진은 ~1.0 (코크 빨강 96.7% / 펩시 파랑 97.7%). 그래서 **CLIP 은 category 만, brand 는 색으로** 가른다. 정량 근거: `docs/freeze-accuracy-diagnosis.md` §2.

---

## 2. ★ 빌드 (가장 중요)

### 2.0 ⚠️ 반드시 Unity 2022.3.62f3 — Unity 6 절대 금지

- **올바른 에디터**: `C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe`
- **`spatial_anchor_test/ProjectSettings/ProjectVersion.txt` 가 `2022.3.62f3` 임을 빌드 전에 확인** (현재 그렇게 되어 있음).
- **Unity 6000.0.76f1 로 열거나 빌드하지 말 것.** RayNeo ARDK 와 비호환이라 실제로 다음이 발생했다:
  - 안경에서 **Unity 로고조차 안 뜨는 검은화면**
  - `ProjectVersion.txt` 및 일부 settings 가 `6000.0.76f1` 로 **오염**됨
- 실수로 6000 으로 열어 오염됐다면: `git -C C:/claude/staging/snucapstone-ar/repo checkout -- spatial_anchor_test/ProjectSettings/` 로 되돌리고 다시 2022.3.62f3 로 연다.

### 2.1 batchmode 빌드 명령

```bash
"C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" \
  -batchmode -nographics -quit -silent-crashes \
  -projectPath C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test \
  -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/Build/build.log
```

- 진입점 = `BuildSpatialAnchorTest.PerformBuild` (`spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs`). 출력 APK 경로는 같은 파일의 `OUTPUT_APK` 상수.
- 성공 시 로그에 `=== SUCCEEDED ===`, 실패 시 `=== FAILED ===` + `EditorApplication.Exit(1)`.
- PowerShell 에서는 동봉된 `spatial_anchor_test/build_2022.ps1` 도 있으나 **`PROJECT_DIR` 가 옛 경로(`C:\claude\staging\spatial_anchor_unity_2022`)를 가리킨다** — 이 레포 경로로 빌드하려면 위 명령을 직접 쓰는 게 안전하다.
- 빌드 hook 이 자동 처리하므로 **직접 손대지 말 것**:
  - `EnsureOpenXRLoader` — Android XR 로더를 진짜 `UnityEngine.XR.OpenXR.OpenXRLoader` 로 재할당 (잘못되면 6DoF 죽음).
  - `ConfigureExternalTools` — JDK/SDK/NDK/Gradle 경로를 **실행 중인 에디터 기준**으로 강제 set (batchmode 는 GUI Preferences 를 못 봄). 2022.3.62f3 로 실행하면 그 에디터 번들 도구를 쓴다.
  - `EnsureRayNeoSettingsPreloaded` — `RayNeoGeneralSettings.asset` 을 PreloadedAssets 에 주입 (없으면 런타임 SLAM pose 죽음).
  - `RayNeoBootConfigPatcher` — `boot.config` 에 `xrsdk-pre-init-library=UnityOpenXR` + `gfx-disable-mt-rendering=1` 주입 + `libUnityOpenXR.so` 강제 복사 (head-locked 방지).

### 2.2 새 빌드 버전 — BUILD_TAG / OUTPUT_APK **2곳 동기 수정**

코드 수정 후 새 버전을 낼 때 `BuildSpatialAnchorTest.cs` 의 **두 상수를 같이** 바꿔야 한다 (안 하면 APK 파일명과 versionName 이 어긋남):

```csharp
const string OUTPUT_APK = "Build/EagleEye-SA-bNN.apk";  // ← 파일명
const string BUILD_TAG  = "bNN";                         // ← versionName 스탬프
```

`bundleVersionCode` 는 `ConfigurePlayerSettings` 에서 매 빌드 +1 자동 증가.

### 2.3 빌드 후 검증 (Bee 캐시 stale 방지 — 반드시)

Unity Bee 빌드 캐시가 stale 하면 APK 가 코드 변경을 반영 못 한 채 빌드된다. 설치된 빌드가 실제 그 빌드인지 **versionName 으로 확인**:

```bash
# (a) APK 자체의 versionName  (aapt: Android SDK build-tools)
aapt dump badging .../Build/EagleEye-SA-bNN.apk | grep versionName
# (b) 설치된 앱의 versionName
adb -s $SER shell dumpsys package $PKG | grep versionName
```

- 둘 다 `BUILD_TAG` 와 **일치**해야 함. 불일치면 Bee 캐시 stale → `spatial_anchor_test/Library/Bee` 정리 후 재빌드.
- **`ProjectVersion.txt == 2022.3.62f3` 유지** 도 빌드 후 재확인 (6000 오염 회귀 방지).

### 2.4 설치 / 권한 / 실행

```bash
SER=A06B4A95B784973   # 안경 시리얼 (adb devices 로 확인)
PKG=com.eagleeye.spatialanchor.bisection

adb -s $SER shell am force-stop $PKG
adb -s $SER install -r .../spatial_anchor_test/Build/EagleEye-SA-bNN.apk
adb -s $SER shell pm grant $PKG android.permission.CAMERA
adb -s $SER shell am start --activity-clear-task \
  -n $PKG/com.rayneo.openxradapter.UnityOpenXrActivity

adb -s $SER logcat -s "Unity:V" "QnnClipEngine:V" "RayNeoXR:I"
```

> ⚠️ ADB 는 안경팀과 **공유 환경**. install/push/force-stop/kill-server 등 **writing 작업은 사전 허락**, reading 은 자유. **무선 ADB 를 끊지 말 것.**

---

## 3. 아키텍처 / 핵심 파일 맵

conquest 파이프라인 컴포넌트 (모두 `spatial_anchor_test/Assets/` 하위):

| 컴포넌트 | 역할 | 파일 |
|---|---|---|
| **HelloAR** | 파이프라인 **오케스트레이터** — 트리거 수신 → 프레임 → CLIP → 색 brand → 경쟁사 매핑 → `spatial.ShowAdBesideMatch(...)`. 모든 데모 플래그의 단일 셋업 지점. 색 brand 판별(`ResolveBrandByColor`)·경쟁사 매핑(`CompetitorAdVideo`) 여기. | `Scripts/HelloAR.cs` |
| **SpatialAnchorTest** | RayNeo OpenXR ARDK **6DoF SLAM** + world-anchored 광고 spawn(`ShowAdBesideMatch`). 응시 지점(camPos+camFwd·0.5m)에 광고 quad world-고정, **max2 FIFO**, 가로반전 텍스처 교정, SLAM 발산 감지(`|camPos|>30m`)→재앵커, world-space HUD. | `Scripts/SpatialAnchorTest.cs` |
| **ClipExtractor** | MobileCLIP-S2 INT8 512-d 임베딩. 텍스처→중앙 crop 256²→NCHW normalize→JNI. **같은 전처리 루프에서 중앙 박스 평균색(`lastMeanR/G/B`) 산출** = 색 brand 입력. worker `isReady` 폴링. | `Scripts/ClipExtractor.cs` |
| **ProductMatcher** | CLIP 임베딩 → category 매칭(`MatchCategory`, top-K 코사인, threshold 0.45). `GetBrandByName` 로 brand 객체 매핑(read-only). brand fallback 도 있으나 데모 경로는 색이 대체. | `Scripts/ProductMatcher.cs` |
| **CameraPreview** | RayNeo **ShareCamera**(병렬 채널 RGB) 라이프사이클. SLAM 6DoF 활성 시 표준 WebCamTexture 가 black frame 만 내던 문제를 우회. `OnApplicationPause` 에서 카메라 close(provider SIGPIPE 예방). | `Scripts/CameraPreview.cs` |
| **GyroTrigger** | IMU 자이로(`Input.gyro.rotationRateUnbiased`)로 "시선 안정" 감지 → `OnTrigger`. 데모 셋팅 threshold 0.5 / duration 1.0s / cooldown 5s. | `Scripts/GyroTrigger.cs` |
| **QnnClipEngine** (Java/NPU) | CLIP HTP 컴파일을 **single-thread `ExecutorService` worker 로 격리** → 메인스레드(렌더) 동결 제거. `initialize`/`embed` 모두 같은 worker (TFLite Interpreter 스레드 바인딩 제약). HTP 캐시 옵션(`setCacheDir`/`setModelToken`). | `Plugins/Android/QnnClipEngine.java` |
| **BuildSpatialAnchorTest** (Editor) | batchmode 빌드 진입점 + OpenXR 로더 fix + 빌드환경 폴백 + boot.config 패처. `BUILD_TAG`/`OUTPUT_APK` 상수. | `Editor/BuildSpatialAnchorTest.cs` |

> 색 brand 판별 디테일: `HelloAR.ResolveBrandByColor` 에서 `blueLean = mB-mR > colorBlueMargin(5)` → **pepsi**, `redLean = mR-mB > colorRedMargin(25)` → **coca-cola**, 둘 다 약하면 **null → 광고 X**(빈 책상/노트북 false-positive 2차 게이트). 여기서 OCR/CLIP fallback 으로 떨어지면 환경편향으로 "항상 coca-cola" 가 재발하니 주의.

---

## 4. 함정 (gotchas — Codex 가 빠지기 쉬운 것)

- **Unity 6 금지** (§2.0). 빌드는 무조건 2022.3.62f3.
- **adb v40/v41 충돌** — 머신에 다른 adb 가 공존하면 버전 충돌 경고가 뜨고 디바이스가 끊겼다 재연결될 수 있다. **디바이스는 재연결되므로 경고는 무시 가능**, `adb devices` 로 시리얼만 재확인.
- **XR 앱은 착용(FOCUSED) 시에만 렌더** — `adb shell input keyevent KEYCODE_WAKEUP` 만으로는 `mCurrentFocus=null` 이라 렌더 루프가 안 돈다. startup/freeze 측정·실행은 **반드시 안경을 착용한 상태**에서 해야 한다 (adb wake 불충분).
- **카메라 FOV ≠ 눈높이** — ShareCamera RGB 화각이 눈 시선과 정확히 일치하지 않는다. 데모 시 **병을 노트북(테이블) 높이로** 들어 카메라 FOV 안에 확실히 넣어야 트리거/색 판별이 안정적.
- **첫 cold 실행 ~95-168초 NPU 컴파일** — 매 cold start 마다 CLIP HTP 그래프 재컴파일(~125초, 관측 95-168초). **백그라운드 스레드라 freeze 가 아니다** — 컴파일 동안 렌더는 계속 돌고 "CLIP 컴파일 중..." 표시. 측정 중 멈춘 듯 보여도 죽은 게 아님.
- **비밀정보 커밋 금지** — `.env`, `*.keystore`, `secrets.*`, `credentials.*`, `sealed/` 는 커밋하지 않는다.
- **`--force` / `--no-verify` 금지.**

---

## 5. 개발 워크플로우

1. 코드 수정.
2. `BuildSpatialAnchorTest.cs` 의 `BUILD_TAG` + `OUTPUT_APK` **2곳 동시 bump** (§2.2).
3. **Unity 2022.3.62f3** batchmode 빌드 (§2.1). 로그에서 `=== SUCCEEDED ===` 확인.
4. `adb install -r` (§2.4) — writing 이므로 공유 환경 사전 허락.
5. **versionName 검증** (§2.3): aapt + dumpsys 가 둘 다 `BUILD_TAG` 와 일치하는지. `ProjectVersion.txt == 2022.3.62f3` 유지 확인.
6. **착용 상태로 테스트** (§4).

### 커밋 규칙

- 커밋 메시지 첫 줄은 **영문 동사**로 시작 (Add/Fix/Update/Remove/Refactor), 본문은 필요시 한국어 OK.
- 풋터: `Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>`
- 빌드 **성공 시에만** 커밋 (fail 이면 commit X). 버전 marker `bNN` / `vX.Y.Z`.
- 작업 중인 브랜치: `feature/slam-clip-worldanchor`. 기본/원격: GitHub `hoseung1102/snucapstone-ar-project`.

---

## 6. 상세 문서 포인터

핵심만 여기 자급식으로 담았다. 깊이가 필요하면:

- **`docs/spatial-anchor-handoff.md`** — followable 핸드오프 + 빌드 런북. 본 통합 데모를 **이어서 개발**할 때 첫 번째로 읽을 문서 (아키텍처·빌드·현재 상태 b9→b14·이슈·다음 작업 우선순위).
- **`docs/freeze-accuracy-diagnosis.md`** — freeze(152초 NPU 컴파일) / 정확도(CLIP 0.07 마진) 근본원인 **정량 심층 진단** (15 서브에이전트 + 온디바이스 포렌식).
- **`docs/vision.md`** — 시스템 비즈니스 정체성 + v1 사양 + 결정 로그. 맨 위 "⚡ 현재 상태 스냅샷" 한 섹션이 현황 요약.
- **`docs/client-spec.md`** — 클라이언트 기술 스택 / 4-Step 파이프라인 / 레이턴시·배터리 설계.
- **`docs/dev-guide.md`** — 셋업·빌드·코드 아키텍처·모델 swap·NPU 현실·ONNX·트러블슈팅 (Eagle Eye 본체).
- **`docs/progress-log.md`** — 일자별 진행 + APK 버전 history + 인사이트.
- **`docs/integration_log.md`** — 통합 timeline + SDK mismatch BLOCKER.

> 충돌 시 우선순위 (`docs/vision.md` §10.3): 클라이언트 동작(카메라/NPU/모델/파이프라인) → `client-spec.md`, 비즈니스/시나리오/매칭 전략 → `vision.md`.
> `docs/archive.md` 는 superseded 된 옛 기획안 — 현재 사양으로 인용 금지.

---

## 7. Codex 특이사항

- Codex 샌드박스에서 **Unity 빌드·adb 실행은 네트워크/승인이 필요**할 수 있다 (장시간 batchmode 빌드, USB/무선 디바이스 접근). 차단되면 사용자에게 승인을 요청하라.
- 이 `AGENTS.md` 는 Codex 가 자동 로드하므로 핵심은 자급식으로 담았다. 깊은 근거·정량 수치는 위 `docs/` 링크를 따라가라 — 추정하지 말고 코드 `file:line` 으로 확인하라.
- 본 레포는 Claude(`CLAUDE.md`) 작업에도 최적화돼 있다. **같은 사실**을 공유하니 두 파일이 모순되면 코드·`docs/` 가 정본이다.
