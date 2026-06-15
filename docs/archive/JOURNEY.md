> 🗄️ **보관 문서(archived)** — 작성 시점 스냅샷. 현황 아님 → 현재 상태는 [docs/STATUS.md](../STATUS.md). 🟠 (2026-06-08 빌드 cycle 재구성 기록)

# SpatialAnchorTest — v1 → v2 의 실패·성공 cycle 기록

> ↩ 실험 history 인덱스: [../EXPERIMENTS.md](../EXPERIMENTS.md) — 이 문서는 빌드 **v0.3.0(Step D)** SLAM 첫 성공까지의 cycle 상세.

2026-06-08 하루에 걸친 디버깅. Unity batch 빌드 → audit → 디바이스 logcat → 수정 → 빌드 …
의 반복으로 결국 호랑이 quad + 6DoF HUD 가 안경에 보이는 첫 성공 빌드 (v2) 에 도달.

## 빌드/실행 cycle 표

| # | 시각 | 빌드 결과 | 디바이스 결과 | 다음 결정 |
|---|---|---|---|---|
| 1 | 13:54 | ✅ DOF3 4097 (v1) 5분 13초 | 화면 검정. logcat: `setFocusStatus: 0; openxr session had not being inited` | audit 진행 |
| 2 | 14:49 | ✅ SLAM 8193 (v1) 11분 fresh build | 같은 black screen | autoload yaml 시도 |
| 3 | ~ | ❌ autoload:1 + IL2CPP 467/902 crash | — | 환경 unstable, retry |
| 4 | ~ | ❌ autoload:1 + Initial Refresh crash | — | rollback + 코드 init |
| 5 | ~ | ❌ ScriptCompilation 후 bee_backend hang | — | full Library reset |
| 6 | ~ | ❌ BuildPipeline.BuildPlayer ExtensionModule throw | — | GUI 빌드 전환 |
| 7 | 20:42 | ✅ GUI 빌드 (autoload:1) | (test 안 함) | input issue 발견 |
| 8 | 20:51 | ✅ GUI 빌드 (autoload:0 + 코드 init) | logcat exception spam: `StandaloneInputModule InvalidOperationException` 매 frame | activeInputHandler 1→2→1 + Awake disable |
| 9 | 21:14 | ✅ v2 빌드 (SpawnProvisional + TextMesh HUD) | `ArgumentNullException: shader` | shader find fallback 추가 |
| 10 | 21:42 | ✅ **v2 shaderfix — 첫 성공** | 호랑이 quad + HUD 둘 다 visible. SLAM 미수렴 (motion 부족) | 검증 단계 |

총 빌드 시도 ~10회, 성공 5회, 디바이스 visible 1회 (v2 shaderfix).

## 발견된 issue 와 fix (시간순)

### A. CameraAttitudeType 가 `DOF3` (3DoF only) 였음

- `Assets/XR/Settings/OpenXR Package Settings.asset` 의 RayNeoSupportFeature 의 `CameraAttitudeType: 4097`
- `RayNeoSupportFeature.cs:192-197`:
  ```
  DOF3 = 0x1001 = 4097    ← 우리 설정 (3DoF — rotation only)
  SLAM = 0x2001 = 8193    ← 6DoF target
  ```
- 4-way audit (Claude Opus × 3 + Codex/OpenAI) 가 동일 결론
- `RayNeoSupportFeature.OnInstanceCreate()` 가 `settings.Add("trackerAlgorithm", (int)CameraAttitudeType)` 로 native 에 전달 → 알고리즘이 3DoF 로 fixed → `EnableSlamHeadTracker()` 호출해도 6DoF 모드 진입 X
- **Codex 의 mechanism 통찰**: `HeadTrackedPoseDriver` 가 OpenXR `<XRHMD>/centerEyePosition` 사용. 3DoF runtime 이면 회전은 들어와도 position 무효 → quad 가 world-anchor 처럼 안 보임
- **Fix**: yaml 1줄 `CameraAttitudeType: 4097 → 8193`

### B. `m_AutomaticLoading: 1` 가 빌드를 깨뜨림

- 초기 가설: `XRGeneralSettingsPerBuildTarget` 의 Android Providers 의 `m_AutomaticLoading: 0` 이라 XR loader 자동 init 안 됨
- 시도: `0 → 1` 변경
- 결과: 빌드가 IL2CPP / Initial Refresh / ScriptCompilation 의 **다른 단계마다 일관되게 crash** (cycle 3~6)
- **원인 추정**: `UnityOpenXrActivity` 의 native init + Unity auto-loader 의 충돌 가능
- **Fix**: `m_AutomaticLoading: 0` 복원. 코드에서 `XRGeneralSettings.Instance.Manager.InitializeLoaderSync()` 명시 호출 (idempotent — runtime 의 `XR Management has already initialized an active loader` 경고만 뜨고 무해)

### C. `StandaloneInputModule` 가 매 frame Exception spam

- 디바이스 logcat:
  ```
  E Unity: InvalidOperationException: You are trying to read Input using the UnityEngine.Input class,
           but you have switched active Input handling to Input System package in Player Settings.
     at UnityEngine.EventSystems.StandaloneInputModule.UpdateModule()
  ```
- ARDK `XR Plugin` prefab 의 EventSystem 이 legacy `StandaloneInputModule` 사용
- 우리 PlayerSettings: `activeInputHandler: 1` (New Input System only)
- 매 frame Update 마다 throw → render pipeline 진행 안 됨 → 사용자 시야에 splash 만 보이고 SpatialAnchorTest.Update/OnGUI 진행 X
- 시도 1: `activeInputHandler: 2` (Both) — **Unity warning: Both is unsupported on Android**
- 시도 2 (채택): `SpatialAnchorTest.Awake()` 에서 `FindObjectsOfType<StandaloneInputModule>()` 모두 `.enabled = false`. UI input 안 쓰니 무해

### D. OpenXR stereo 에서 `OnGUI` 가 invisible

- 초기 v1 의 HUD: `OnGUI` 의 IMGUI screen-overlay + `stereoHud` 로 좌/우 분할
- 디바이스에서 HUD 가 안 보임 — Unity 의 OpenXR stereo 모드에서 IMGUI screen-overlay 는 알려진 limitation
- 8분간 logcat 만 보고 추측만 한 셈 — 진단 자체 unreadable 이 root issue
- **Fix**: World-space `TextMesh` 로 대체. quad 위쪽 0.6m 거리에 spawn, 매 frame 갱신, billboard 회전. stereo display 자동 호환

### E. SLAM 수렴 wait 가 사용자 피드백 차단

- v1: `while (true) { if (status==1) break; yield WaitForSeconds(0.1f); }` — 수렴까지 quad spawn X
- 문제: 수렴 안 되면 사용자가 아무것도 못 봐서 무엇이 잘못됐는지 모름
- **Fix (v2)**: `SpawnProvisional()` 즉시 spawn (provisional anchor). SLAM 수렴 도달 시 1회 reposition → true world-anchor. ARKit/ARCore 의 표준 `place anchor then refine` 패턴

### F. IL2CPP build 가 `Shader.Find` 의 일부 shader stripping

- v2 BuildImageQuad 호출 시 `ArgumentNullException: Value cannot be null. Parameter name: shader`
- `Shader.Find("Unlit/Texture")`, `Shader.Find("GUI/Text Shader")` 둘 다 stripped
- **Fix**: 다단계 fallback (`Unlit/Texture → Sprites/Default → Standard → default material 유지`). TextMesh 의 material override 도 제거 (component 가 font default 사용)

### G. Build wrapper exit 1 의 false signal

- cycle 4~6 의 PowerShell wrapper 가 exit 1 reported 하나 Unity.exe 는 살아있어 log 계속 grow
- 진단 시 `wc -l logfile` 만으로는 부족 — Unity process state 동시 확인 필요
- zombie process (`netcorerun.exe`, `Unity.exe`, `UnityCrashHandler64.exe` 등) 가 다음 빌드 lock 충돌 일으킴
- **메타 lesson**: build fail 시 `tasklist` 로 zombie 확인 → 모두 종료 → Library/Bee fresh delete → 재빌드 가 robust path

## 4-way audit 요약 (Claude Opus 3 + Codex)

직전까지 검증된 후 디바이스 실험 직전에 진행한 정적 audit. evidence_pack/01_apk_manifest.txt ~ 13_goal.md 를 4개 모델에 독립 평가 위탁.

| 모델 | Π (전체 성공 확률) | Bottleneck |
|---|---|---|
| Claude Opus — code logic | 24.8% | Camera.main null, tiger_anchor 누락, CameraAttitudeType X2 의심 |
| Claude Opus — APK static | 29% | RayNeo runtime missing, scene XR rig, installLocation |
| Claude Opus — RayNeo pattern | 6.8% | CameraAttitudeType, ATWSupport=0, IPostGenerate callback 부재 |
| **Codex (OpenAI cross-family)** | — | "CameraAttitudeType DOF3 가 1순위 fix. 그 외 누락 없음" |

메인 재검증으로 false alarm 5건 정리 (Camera.main, tiger_anchor, scene XR rig, XR Plugin silent fallback, MonitorService 권한). 진짜 critical 1건 = **CameraAttitudeType DOF3 → SLAM**. Codex 가 `HeadTrackedPoseDriver` 의 `centerEyePosition` mechanism 으로 cross-검증.

audit 자체의 메타 lesson:
- 같은 모델 패밀리 (Opus) 만으로는 confirmation bias 위험. cross-family (Codex/OpenAI) 가 enum 우연 일치 (X3_Normal=0x1001 vs DOF3=0x1001) 의 함정도 짚어줌
- 4-way 합의가 **확정 critical 의 신뢰도** 를 만들고, **false alarm 5건 정리** 가 효율 증가

## 알려진 미검증

- SLAM 수렴 시간 (motion parallax 필요): 사용자가 안경 끼고 좌우 평행 이동 필수. 텍스처 있는 환경
- `Runtime is not in XR_SESSION_STATE_READY now!` warning: 앱 transition 시 잠시. 무해 추정
- screencap PNG 가 mirror 로 보이는 건 안경 광학 prism 특성. 사용자 시야에서는 정상으로 추정 (사용자 confirm 필요)
- `RayNeoXR Runtime 1.1.7 vs SDK 1.1.6 minor mismatch` warning: `Load jni libs successfully` 로 진행하므로 무해 추정
- v2 첫 성공 시점에서 SLAM 수렴 (`FFVINS state = 0 → 1`) 까지 도달 못 함 — 디바이스 motion 부족

## 백업

`backups/2022.3.62f3/cycle_Final_dof_fix/` 에 cycle 별 APK + yaml 보존:
- `EagleEye-SpatialAnchor.dof3.apk` (DOF3, v1 첫 성공 빌드)
- `EagleEye-SpatialAnchor.slam8193.apk` (SLAM, v1 fix)
- `EagleEye-SpatialAnchor.slam8193_forceinit.apk` (autoload:1 시도)
- `EagleEye-SpatialAnchor.slam8193_inputfix.apk` (StandaloneInputModule fix)
- `EagleEye-SpatialAnchor-v2.apk` (provisional + TextMesh, shader null)
- `EagleEye-SpatialAnchor-v2_shaderfix.apk` (**첫 성공**)
