> 🗄️ **보관 문서(archived)** — 작성 시점 스냅샷. 현황 아님 → 현재 상태는 [docs/STATUS.md](../STATUS.md). 🔴 (브랜치 `feature/integrate-spatial-side`·v0.9.x·SDK 1.1.6↔1.1.7.9 BLOCKER 등 2026-06-09 시점 stale 현황)

# Integration Log — HelloAR (CLIP/OCR) + SpatialAnchor 통합

> 2026-06-09. branch `feature/integrate-spatial-side`. base commit `9acd75f` (Step E v0.8.0).
> 본 문서는 통합 작업의 dev guide + 변경 history + 현재 막힌 문제 정리.

---

## 1. 통합 목표

`feature/spatial-anchor-test-v2` (SpatialAnchorTest 단독 — RayNeo 6DoF SLAM world-anchored video quad) 위에 helloar 의 CLIP/OCR/ProductMatcher detection pipeline 을 통합. 광고 표시만 기존 2D HUD 대신 SpatialAnchor 의 world-anchored video quad 로 swap. cola/pepsi conquest demo 가 v1 시연 목표.

핵심 원칙:
- **오버엔지니어링 금지** — helloar pipeline 의미적 변경 X
- **기존 코드 그대로 + 광고 표시만 anchor**
- cola/pepsi 데모만 (라면/마트 시나리오 무시)
- 빌드 성공 시마다 자동 commit + push (`feedback_spatial_anchor_versioning` rule)

---

## 2. Dev Guide (현재 상태)

### Project structure
```
glasses-app/
├─ Assets/
│  ├─ Scripts/
│  │  ├─ HelloAR.cs               pipeline orchestrator (gyro trigger → clip → ocr → matcher → spatial)
│  │  ├─ GyroTrigger.cs           IMU 안정 트리거 (cooldown 5s)
│  │  ├─ CameraPreview.cs         RayNeo ShareCamera (RGB) lifecycle. OpenCamera / CloseCamera 분리 (v0.9.1)
│  │  ├─ ClipExtractor.cs         MobileCLIP-S2 + QNN HTP delegate cache
│  │  ├─ OCRExtractor.cs          EasyOCR wrapper (detector NPU + recognizer)
│  │  ├─ ProductMatcher.cs        hierarchical match (CLIP category + OCR/CLIP brand). enableClipBrandFallback default ON
│  │  ├─ SpatialAnchorTest.cs     SLAM head tracker + provisional anchor + converge reposition (v0.9.2)
│  │  ├─ QnnYoloDetector.cs       (clipOnlyMode=true 라 dormant)
│  │  ├─ AdRenderer.cs            2D HUD (현재 SpatialAnchor 대체로 skip)
│  │  ├─ AmbientInterestProfile.cs
│  │  └─ YoloDetector.cs          (Sentis stub)
│  ├─ Plugins/Android/
│  │  ├─ EasyOCREngine.java       TFLite detector + recognizer + CTC decode (NPU via QNN)
│  │  ├─ QnnClipEngine.java       MobileCLIP HTP context cache (setCacheDir + setModelToken)
│  │  ├─ QnnYoloEngine.java       (dormant)
│  │  ├─ mainTemplate.gradle      TFLite 2.15.0 + QNN 2.47.0 + (MLKit 제거됨)
│  │  └─ AndroidManifest.xml      uses-native-library × 12 (libcdsprpc.so 등) + CAMERA + UnityOpenXrActivity
│  ├─ StreamingAssets/
│  │  ├─ mobileclip_s2.tflite     38 MB w8a8
│  │  ├─ easyocr_detector.tflite  21 MB (CRAFT)
│  │  ├─ easyocr_recognizer.tflite 10 MB (CRNN)
│  │  ├─ easyocr_charset.txt
│  │  └─ db/ (metadata.json + embeddings/*.npy + ads_video/*.mp4)
│  ├─ XR/
│  │  ├─ RayNeoGeneralSettings.asset           preloaded in ProjectSettings.asset:146
│  │  ├─ XRGeneralSettingsPerBuildTarget.asset  m_AutomaticLoading: 0 (manual init in SpatialAnchorTest.Start)
│  │  └─ Settings/OpenXR Package Settings.asset CameraAttitudeType: 8193 (SLAM 6DOF), OpenSLAMOnStart: 1
│  └─ Editor/BuildSpatialAnchorTest.cs       batch build entry
├─ Packages/com.unity.xr.rayneo.openxr/      RayNeo ARDK 1.1.2 (committed 3.1MB)
└─ Build/EagleEye-SpatialAnchor-v2.apk        latest build output (≈154 MB)
```

### Build / Install / Run
```bash
# Batch build (Unity 2022.3.62f3)
"C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" \
  -batchmode -quit -nographics -silent-crashes \
  -projectPath glasses-app \
  -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile glasses-app/audit_pack/build.log

# Install + grant + launch
adb -s A06B4A95B784973 shell am force-stop com.eagleeye.spatialanchor.v2
adb -s A06B4A95B784973 install -r glasses-app/Build/EagleEye-SpatialAnchor-v2.apk
adb -s A06B4A95B784973 shell pm grant com.eagleeye.spatialanchor.v2 android.permission.CAMERA
adb -s A06B4A95B784973 shell am start --activity-clear-task \
  -n com.eagleeye.spatialanchor.v2/com.rayneo.openxradapter.UnityOpenXrActivity

# logcat tag
adb -s A06B4A95B784973 logcat -s "Unity:V" "QnnClipEngine:V" "EasyOCREngine:V" "RayNeoXR:I"
```

### Pipeline (현재 v0.9.2, clipOnlyMode=true)
```
[DETECTING] (ShareCamera ON)
  GyroTrigger fire (5s cooldown, threshold 0.5 rad/s, duration 1s)
    → CLIP NPU (~30 ms warm, ~4 min cold compile)
    → ProductMatcher STAGE 1 (category, threshold 0.45)
    → if cola: STAGE 2 OCR (EasyOCR detector NPU, recognizer fail → text="")
    → if STAGE 2 fail + cat.brands.Length > 1 + embedding:
       Stage2 CLIP brand fallback (coca-cola.npy / pepsi.npy cosine top-1)
    → result.brand.ad_image (.png → .mp4 swap)
    → spatial.ShowAdBesideMatch(vidPath, result, detections, W, H)
      └─ detections 비어있으면 ShowAd fallback (정면 1.2m provisional)
    → cam.CloseCamera()                                  ← v0.9.1 (SLAM RGB pipeline 회복)
    → StartCoroutine(ReopenCameraAfter(adShowSeconds=10))

[ANCHORING] (ShareCamera OFF, 10s)
  Update() polling lastSlamStatus = GetHeadTrackerStatus()
  if lastSlamStatus == 1 + !repositionedOnConverge + anchorQuad != null:
    quad.position = camPos + camFwd * 1.2m       ← v0.9.2 자동 reposition (provisional → refine)
    repositionedOnConverge = true (1회만)

[DETECTING 복귀] (10s 후)
  cam.OpenCamera() (cache hit instant)
```

---

## 3. 통합 변경 timeline

### Phase 1 (v0.8.x) — 기본 통합 + dependency 정리

| Version | 변경 | 이유 |
|---|---|---|
| **v0.8.0** | helloar pipeline copy (HelloAR.cs, ClipExtractor.cs, ProductMatcher.cs, GyroTrigger.cs, OCRExtractor.cs, QnnClipEngine.java, MLKitOCR.java, mainTemplate.gradle). HelloAR 가 `gameObject.AddComponent<SpatialAnchorTest>()` 로 부착. ShowAd path 가 2D HUD → SpatialAnchor.ShowAdBesideMatch | Step E base 통합 |
| **v0.8.1** | GyroTrigger `oneShotPerStableWindow=true` → `false`, `triggerCooldown=5s` 강제. SpatialAnchorTest 의 touchpad input trigger 제거 | RayNeo 정지 시 `Input.gyro` 가 `(0,0,0)` stuck → oneShot 모드에서 firedInThisStableWindow 영구 true → 첫 trigger 후 재발화 X. cooldown 모드로 우회 |
| **v0.8.2** | `Assets/StreamingAssets/mobileclip_s2.tflite` 38 MB 포함 | ClipExtractor 가 `UnityWebRequest.Get(streamingAssetsPath)` 로 fetch — APK 안에 packed 안 되면 404 |
| **v0.8.3** | `MLKitOCR.java` timeout 200ms → 5000ms | RayNeo CPU MLKit 가 10초+ 걸려 200ms timeout 영구 → text="" |
| **v0.8.4** | AndroidManifest 에 `<uses-native-library>` 12개 추가 (libcdsprpc.so, libadsprpc.so 등) | Android 11+ 정책: vendor namespace `.so` 사용 시 명시 필수. 누락 시 `libQnnHtpV73Stub.so` 가 `libcdsprpc.so` dlopen 실패 → CLIP NPU init fail |
| **v0.8.5** | `matcher.enableClipBrandFallback = true` 강제. EasyOCR 후에도 안전망 | OCR fail (text='') 시 ProductMatcher STAGE 2 fail. fallback ON 시 CLIP brand-specific (coca-cola.npy / pepsi.npy cosine top-1) 으로 brand 결정 |

### Phase 2 (Task #25–#28) — sub-agent 위임 통합

| Task | Agent 결과 | 변경 file |
|---|---|---|
| **#25 SLAM init root cause** | ProjectSettings.asset:146 `preloadedAssets[0]` GUID 회귀 (null → df1171da... RayNeoGeneralSettings). OpenXR Package Settings.asset:345 `CameraAttitudeType: 4097 (DOF3) → 8193 (SLAM 6DOF)` | ProjectSettings.asset + OpenXR Package Settings.asset (2 line) |
| **#26 OCR engine swap** | MLKit (~10초 CPU) → EasyOCR (AI Hub w8a8 TFLite + QNN NPU). EasyOCREngine.java 작성 (Detector CRAFT + Recognizer CRNN + CTC decode). MLKitOCR.java 삭제, mainTemplate.gradle 에서 MLKit dep 제거 | EasyOCREngine.java (new), OCRExtractor.cs, mainTemplate.gradle, StreamingAssets/easyocr_*.tflite + charset.txt (32 MB) |
| **#27 MobileCLIP QNN cache** | QnnDelegate `setCacheDir + setModelToken("mobileclip_s2_v73_int8_v1")`. ClipExtractor `initializeFromContextBin(...)` overload (pre-built bin 있으면 first launch instant) | QnnClipEngine.java, ClipExtractor.cs |
| **#28 Camera refactor** | WebCamTexture → RayNeo ShareCamera (`OpenCamera(XRCameraType.RGB, ...)`). SLAM 6DOF (8193) 활성 시 standard WebCamTexture 가 black frame 만 내보내던 문제 해결 | CameraPreview.cs (rewrite) |
| **#29 bbox extension** | `feature/integrate-spatial-side-bbox` branch (commit 0be27ea) — `useOcrBboxForDepth=false` opt-in toggle. EasyOCREngine.recognizeWithBoxes, OCRExtractor.OCRBox, SpatialAnchorTest.BRAND_TEXT_REAL_W_M={coca-cola:0.095, pepsi:0.065}. 옵트인 시 ShowAdBesideBbox 로 same-depth side placement | (별도 branch, 통합 안 됨) |

### Phase 3 (v0.9.x) — SLAM workaround 시도

| Version | 변경 | 이유 |
|---|---|---|
| **v0.9.0** | quadWidthM 0.80 → 0.30 (stereo binocular overlap ~25° 안) | 광고가 양안 overlap zone 벗어나 좌우 잘림 |
| **v0.9.0** (SLAM agent #31) | SpatialAnchorTest.cs LateUpdate 에 `RayNeoApi_GetHeadTrackerPose` 직접 native polling (TrackedPoseDriver 우회). `OpenSLAMOnStart: 0 → 1` | InputSystem `<XRHMD>/centerEyePosition` 가 RayNeoXR runtime 의 -30001 frame pipeline fail 로 정체 |
| **v0.9.1** | CameraPreview 의 `OpenCamera()` / `CloseCamera()` lifecycle 분리. HelloAR match 후 `cam.CloseCamera()` + `StartCoroutine(ReopenCameraAfter(10s))`. **DETECTING ↔ ANCHORING** state machine | ShareCamera (Camera 0) 와 SLAM 의 frame pipeline 공유 가능성 — 광고 spawn 동안 RGB pipeline release 로 SLAM 우선 시도 |
| **v0.9.2** | SpatialAnchorTest.cs Update 에 `lastSlamStatus==1 && !repositionedOnConverge && anchorQuad != null` 시 자동 reposition. ShowAd 의 `repositionedOnConverge = true` → `false` (provisional) | v2 design ("즉시 spawn + SLAM converge 도달 시 1회 reposition") 의 정통적 implementation 누락. provisional → refine path 회복 |

### Phase 4 (Task #31, #32) — SLAM service-level 진단

| Task | 진단 결과 |
|---|---|
| **#31 SLAM tracking** | HeadTrackedPoseDriver 정상 attach. `<XRHMD>/centerEyePosition` binding OK. 그러나 RayNeo SLAM native runtime frame pipeline dead (-30001) → InputSystem 가 받을 pose 없음 → xrCam stuck. **OpenSLAMOnStart=1 + native polling fallback 추가** (effect 0) |
| **#32 SLAM service-level** | 🚨 **Qualcomm camera HAL (vendor.camera-provider-2-7) crash loop** — `received signal 13 (SIGPIPE)` 매 10-90초 spam. Camera 0 (RGB) + Camera 1 (SLAM stereo) 모두 cycling. **App 클라이언트 없어도 cycling = system/firmware level**. ShareCamera 무관. **device reboot 권장** |

---

## 4. 현재 막힌 문제 (BLOCKER)

### 🚨 RayNeo SDK 1.1.6 ↔ Runtime 1.1.7.9 minor version mismatch

**증상** (모든 device reboot 후 재현):
```
RayNeoXR: SDK:1.1.6.0.20250210  Runtime:1.1.7.9.20251230
RayNeoXR: The minor version of SDK(6) does not match the minor version of RayNeoXR Runtime(7).
RayNeoXR: TIMEOUT when wait for service connection callback
```

**결과**:
- `GetHeadTrackerStatus()` 영구 `0 (FFVINS_INITIALIZING)`
- `xrCam.transform.position` 영구 `(0, 0, 0)`
- `SpatialAnchorTest.ShowAd` 의 `world=(0.00, 0.00, 1.20)` 매번 동일 → 광고 head-locked HUD 처럼 보임
- v0.9.2 의 provisional reposition logic 도 발동 안 함 (status != 1)

**Standalone 비교** (`com.eagleeye.spatialanchor`, base commit de0be34): **같은 mismatch + timeout**. 즉 우리 통합 빌드의 변경 무관, RayNeo system service level issue.

**시도한 우회 (모두 effect 0)**:
- 안경 reboot
- `OpenSLAMOnStart: 0 → 1`
- `RayNeoApi_GetHeadTrackerPose` native polling
- ShareCamera lifecycle 분리 (frame pipeline 회복 시도)
- `com.rayneo.xr.runtime` force-stop + restart

### 가능 path (decision pending)
1. **ARDK 1.1.7+ SDK 받기** (RayNeo developer portal). `Packages/com.unity.xr.rayneo.openxr/` 교체 + 재빌드. 정통 path. 시간 30분-1시간
2. **안경 firmware downgrade** (Runtime 1.1.6 으로). OEM 권한 필요. 위험
3. **IMU rotation-only fallback** — `Input.gyro.attitude` 또는 native gyro 로 head rotation 만 track. translation 없음. PoC 시연 정도 가능
4. **새 device** (Runtime 1.1.6 fresh boot)

---

## 5. EasyOCR recognizer NPU 비호환 (minor)

```
EasyOCREngine: recognizer load 실패: Internal error: Error applying delegate
java.lang.IllegalArgumentException: Internal error: Error applying delegate
```

detector (CRAFT) 는 NPU 정상 load. recognizer (CRNN) 의 어떤 op (BiLSTM 또는 sequence op) 가 QNN HTP delegate 비호환. 결과: OCR 전체 skip (`ocr=0ms text=''`). ProductMatcher 의 `enableClipBrandFallback=true` 가 brand 결정 책임. **demo blocker 아님** (cola/pepsi conquest 작동).

해결 후보:
- recognizer 만 plain TFLite (delegate 제거, CPU). latency ~100-200ms
- recognizer w8a8 → fp32 export (큰 size, NPU 안 됨)
- AI Hub `submit_compile_job` 으로 recognizer 의 QNN-compatible 변형 export

---

## 6. MobileCLIP QNN context binary pre-build (deferred)

Task #27 의 `QnnDelegate.setCacheDir` 가 사실 disk cache write 안 함 (logcat `exists=false`). NPU 의 device-level kernel cache 만 효과 (같은 model_token 두 번째 launch 부터 instant).

진짜 first-launch instant 위해 prebuild `.qnn_context.bin`:
- AI Hub `submit_compile_job(tflite)` 거부 (input type 으로 tflite 안 받음)
- ONNX export → AI Hub compile 권장 (`../dev-guide.md` §5 의 pipeline). 시간 30-60분
- PyTorch checkpoint `C:/claude/staging/clip-separation-test/ml-mobileclip/checkpoints/mobileclip_s2.pt` 활용

deferred — SLAM 정상화가 우선.

---

## 7. 별도 branch — bbox depth/placement (opt-in)

`feature/integrate-spatial-side-bbox` (commit `0be27ea`) 가 OCR bbox 의 (u, v) center 기반 same-depth side placement 제공. `useOcrBboxForDepth=true` toggle on 시 cola pet 옆에 광고 spawn (current ShowAd 의 정면 1.2m fallback 대신).

cherry-pick 시 `SpatialAnchorTest.cs` 의 v0.9.2 변경 (LateUpdate native polling + 자동 reposition) 과 conflict — manual resolve 필요.

---

## 8. 진행 중 / 후속 작업

- [ ] ARDK 1.1.7 upgrade — RayNeo portal access 또는 동업자 확인
- [ ] EasyOCR recognizer plain TFLite fallback (NPU 비호환 op 우회)
- [ ] MobileCLIP ONNX → QNN ctx binary prebuild (first launch instant)
- [ ] bbox branch cherry-pick (1차 SLAM success 후)
- [ ] Touchpad / Bluetooth remote trigger 추가 (현재 GyroTrigger 만)
- [ ] AmbientInterestProfile.cs 의 trigger 누적 활용 (v2+)
