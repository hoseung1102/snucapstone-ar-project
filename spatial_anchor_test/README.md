# SpatialAnchorTest — RayNeo X3 Pro 6DoF SLAM 공간 고정 검증

Unity + RayNeo OpenXR ARDK 1.1.2 로, **6DoF SLAM 으로 world-anchored 이미지(호랑이 quad)** 를
띄우는 최소 검증 앱. 머리 회전/걷기에 대해 같은 자리에 떠 있는지 시각 확인.

**Status**: v2 = 첫 성공 빌드 (2026-06-08). 안경에서 호랑이 quad + 실시간 6DoF HUD 모두 visible.

## 동작

1. App launch → Unity splash → ARDK XR Plugin rig 로드
2. `SpatialAnchorTest.Awake`/`Start`:
   - ARDK XR Plugin prefab 의 EventSystem 의 legacy `StandaloneInputModule` 비활성화 (new InputSystem only 환경에서 매 frame `InvalidOperationException` 차단)
   - `XRGeneralSettings.Manager.InitializeLoaderSync()` 명시 호출 (idempotent — `UnityOpenXrActivity` native init 과 충돌 X)
   - `XRInterfaces.EnableSlamHeadTracker()` + `EnablePlaneDetection()` 호출
3. **`SpawnProvisional()` — 즉시 spawn** (SLAM 수렴 대기 X):
   - 호랑이 quad: `cam.position + cam.forward × 0.5m`, 30cm 폭, world-space
   - HUD `TextMesh`: quad 위쪽 0.6m, 매 frame 갱신
4. `Update()` 매 frame:
   - `GetHeadTrackerStatus()` polling
   - 수렴 도달 시 1회 anchor + HUD reposition (true world-anchor 로 전환)
   - HUD billboard 회전 + 실시간 6DoF 값 표시

## HUD 표시 항목

```
SpatialAnchorTest v2
SLAM: SEEKING / TRACKING / CONVERGED  (raw=0/1/-2)
seeking: 12.3s   (수렴 후엔 converged: X.Xs)
uptime: ...   fps: ...
-- 6DoF camera --
pos: x, y, z
rot: rx, ry, rz
v: m/s   w: deg/s
-- anchor --
world: x, y, z
age: ...s   dist: ...m
drift: ...m   fwd∠: ...°
```

## 성공한 핵심 fix (history 는 `JOURNEY.md`)

| # | 항목 | 변경 |
|---|---|---|
| 1 | `CameraAttitudeType` | `DOF3 (0x1001=4097)` → `SLAM (0x2001=8193)` — 6DoF SLAM 활성화 |
| 2 | `StandaloneInputModule` | Awake 에서 disable — Input System exception spam 차단 |
| 3 | `OnGUI` HUD → `TextMesh` HUD | OpenXR stereo 에서 IMGUI screen-overlay 가 invisible. world-space `TextMesh` 로 대체 |
| 4 | SLAM 수렴 wait → 즉시 spawn + 점진 refine | 진단 가능 + 사용자 즉시 visible feedback |
| 5 | `Shader.Find` null fallback | IL2CPP build 의 shader stripping 대응 |
| 6 | `XRGeneralSettings.InitializeLoaderSync()` 명시 호출 | `UnityOpenXrActivity` 의 native init 보강 (idempotent) |

## 빌드

### 환경
- Unity **2022.3.62f3** (Personal 호환 LTS)
- RayNeo OpenXR ARDK **1.1.2** (`Packages/com.unity.xr.rayneo.openxr/`, license 따라 별도 다운로드 — repo 에 미포함)
- Android Build Support + IL2CPP + ARM64 + OpenGLES3
- JDK + SDK 34 + NDK r21 = Unity bundle 사용

### Editor GUI
1. `Edit → Project Settings → XR Plug-in Management` 의 Android 탭에서 **OpenXR** loader 체크 (한 번)
2. `Edit → Project Settings → XR Plug-in Management → OpenXR → Android` 의 **RayNeo Support Feature** 체크, `CameraAttitudeType = SLAM`
3. 메뉴 **`Build → SpatialAnchor APK`** 클릭. 출력: `Build/EagleEye-SpatialAnchor-v2.apk`

### Batch (CLI)
```powershell
.\build_2022.ps1
```
첫 빌드 10~15분, incremental 1~3분.

## 디바이스 install + launch

```bash
adb install -r release/EagleEye-SpatialAnchor-v2.apk
adb shell am start -n com.eagleeye.spatialanchor.v2/com.rayneo.openxradapter.UnityOpenXrActivity
adb logcat -s Unity:V RayNeoXR:V
```

기존 v1 (`com.eagleeye.spatialanchor`) 가 install 돼있어도 v2 는 별도 package 라 같이 install 가능.

## 검증 방법

1. 안경 끼고 정면 보기
2. 호랑이 quad 가 0.5m 앞에 떠있는지 확인
3. HUD 의 `SLAM: SEEKING` 이 보임 — 수렴 대기 중
4. **머리를 좌우로 30cm 정도 천천히 평행 이동** (회전만 X, translation 필수) — motion parallax 가 SLAM 수렴의 필수 입력
5. 환경: 책상/책장/벽지 무늬 있는 곳 (단색 흰벽 X)
6. 수렴 시 HUD 가 `SLAM: CONVERGED, converged: X.Xs` 로 전환, 호랑이 1회 reposition
7. 그 후 머리 움직여도 호랑이 같은 자리에 머무는지 = world-anchor 검증

## 알려진 주의사항

- **RayNeoXR Runtime 1.1.7 vs SDK 1.1.6 minor version mismatch warning** — 실제 동작에 무영향 (`Load jni libs successfully` 로 진행)
- screencap 의 텍스트가 mirror 로 보이는 건 안경 광학 prism 특성. 안경 시야에서는 정상
- `installLocation=preferExternal` (Unity default) — RayNeo 내부 storage 로 자동 fallback, 무해
- SLAM 미수렴 (state=0) 상태에서 quad 는 cam=(0,0,0) 기준 placed. SLAM 수렴 직후 reposition → 잠시 jump 가능

## 파일 구조

```
spatial_anchor_test/
├─ Assets/
│  ├─ Scripts/SpatialAnchorTest.cs        SLAM init + spawn + HUD
│  ├─ Editor/BuildSpatialAnchorTest.cs    Batch-mode 빌드 entry
│  ├─ Plugins/Android/AndroidManifest.xml UnityOpenXrActivity launcher + mercury meta-data
│  ├─ Resources/tiger_anchor.png          호랑이 텍스처 (mono_r06, 640×480)
│  ├─ Scenes/SpatialAnchorScene.unity     XR Plugin prefab + SpatialAnchorHost
│  └─ XR/                                  OpenXR + RayNeoSupportFeature settings
├─ ProjectSettings/                        PlayerSettings, GraphicsSettings 등
├─ Packages/manifest.json                  의존성 (OpenXR 1.7.0, XR Management 4.2.1, InputSystem 1.14.0, etc.)
├─ release/EagleEye-SpatialAnchor-v2.apk   첫 성공 빌드 결과물 (16.4 MB)
├─ build_2022.ps1                         Unity batch build script
├─ setup_2022.ps1                         초기 프로젝트 setup
├─ README.md                              이 문서
└─ JOURNEY.md                             v1 → v2 의 실패/성공 cycle history + audit
```

`Packages/com.unity.xr.rayneo.openxr/` 는 RayNeo SDK 라 별도 install 필요.
