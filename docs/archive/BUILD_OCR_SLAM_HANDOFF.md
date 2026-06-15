# NPU OCR + SLAM 통합 빌드 — 팀원 핸드오프 (b22-slam-ocr)

> 목표: 원래 SLAM 파이프라인(IMU 트리거 → CLIP category → **NPU OCR brand** → 광고 + **6DoF world-anchored 3D 영상**)에 NPU EasyOCR 을 얹어 실기 검증.
> 측정 대상: ① 광고가 실제로 뜨나 ② per-trigger latency 가 실시간 OK 인가 ③ 다양한 환경에서 brand 인식되나.
>
> **이 문서대로만 따라오면 빌드 → 설치 → 실행까지 됩니다.** 막히면 맨 아래 "트러블슈팅" 참조.

---

## 0. 한 줄 요약

```
git fetch && git checkout feature/npu-ocr-slam-b22
# (아래 1번) Unity 2022.3.62f3 + Android Build Support 설치
# (아래 3번) 모델 1개만 확인 — easyocr_recognizer_unroll_qcs8450.tflite (repo 에 force-add 돼 있음)
# (아래 4번) batchmode 빌드 → APK
# (아래 5번) adb install + 실행
```

---

## 1. 필수 환경 (코드에 없는 것 — 직접 준비)

| 항목 | 버전/내용 | 비고 |
|---|---|---|
| **Unity Editor** | **2022.3.62f3** (changeset `96770f904ca7`) | ⚠️ **반드시 이 버전.** 프로젝트가 2022.3.62f3 로 고정(`ProjectSettings/ProjectVersion.txt`). Unity 6 에서는 RayNeo OpenXR/SLAM 이 `Need to set FrameLayout in advance!` → shutdown 으로 깨짐(실기 확인됨). |
| **Android Build Support** | 위 에디터의 모듈 (OpenJDK + Android SDK/NDK 포함) | Unity Hub 에서 에디터 설치 시 체크 |
| **RayNeo OpenXR ARDK** | `Packages/com.unity.xr.rayneo.openxr` (로컬 패키지, **repo 에 포함**) | 별도 설치 불필요 — pull 로 받아짐(446 파일) |
| **안경** | RayNeo X3 Pro (Snapdragon AR1 Gen1, Hexagon v73) | `adb` 연결 |
| **adb** | platform-tools | `adb devices` 로 안경 보여야 함 |

### Unity 2022.3.62f3 설치 방법
- **권장**: Unity Hub → Installs → Install Editor → "2022.3.62f3" 선택 → **Android Build Support** 모듈 체크.
  - Hub 목록에 안 보이면(구버전 LTS 라 숨겨질 수 있음) "Archive" / [Unity download archive](https://unity.com/releases/editor/archive) 에서 2022.3.62f3 의 "Unity Hub" 버튼.
- **CLI 직접 다운로드(맥 Apple Silicon)** — Hub 가 버전 resolve 못 할 때:
  ```bash
  # 에디터(arm64) + Android 모듈 pkg 직접 받기 (changeset = 96770f904ca7)
  curl -L -o Unity-2022.3.62f3-arm64.pkg \
    "https://download.unity3d.com/download_unity/96770f904ca7/MacEditorInstallerArm64/Unity.pkg"
  curl -L -o Unity-Android-2022.3.62f3.pkg \
    "https://download.unity3d.com/download_unity/96770f904ca7/MacEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-2022.3.62f3.pkg"
  sudo installer -pkg Unity-2022.3.62f3-arm64.pkg   -target /
  sudo installer -pkg Unity-Android-2022.3.62f3.pkg -target /
  # → /Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app
  ```
  (Windows/Intel 은 `MacEditorInstallerArm64` 대신 각 플랫폼 경로. Hub GUI 설치가 가장 안전.)

---

## 2. 코드 받기

```bash
git clone https://github.com/hoseung1102/snucapstone-ar-project.git   # 이미 있으면 생략
cd snucapstone-ar-project
git fetch origin
git checkout feature/npu-ocr-slam-b22
```
- 프로젝트 루트: `glasses-app/`
- 이 브랜치 = **ade5bca(검증된 Unity 2022 SLAM 파이프라인) + NPU OCR 통합**. (cold-start/Unity6 라인과 별개)

---

## 3. 모델 파일 (코드에 없는 것 — 준비 방법)

대부분의 모델은 **이미 repo 에 tracked** 라 `git checkout` 으로 자동으로 받아집니다:
`easyocr_detector.tflite`, `easyocr_recognizer.tflite`(구), `mobileclip_s2.tflite`, `db/embeddings/*.npy`, `db/metadata.json`, `db/ads/*`, `db/ads_video/*.mp4`.

**단 하나 예외** — repo `.gitignore` 에 `*.tflite` 규칙이 있어, NPU OCR recognizer 만 별도 취급:

| 파일 | 위치 | 크기 | SHA-256 | 상태 |
|---|---|---|---|---|
| `easyocr_recognizer_unroll_qcs8450.tflite` | `glasses-app/Assets/StreamingAssets/` | 30,483,240 B | `e58f438d40b469de124dbea78258b9c8dd0c1370772b9c8564173f37d13f2c35` | **이 브랜치에 `git add -f` 로 강제 포함** → pull 로 받아짐 |

→ **즉 이 브랜치를 checkout 하면 이 모델도 같이 받아집니다.** 별도 다운로드 불필요.
checkout 후 확인:
```bash
shasum -a 256 glasses-app/Assets/StreamingAssets/easyocr_recognizer_unroll_qcs8450.tflite
# e58f438d40b469de... 와 일치해야 함
```

### (참고) unroll recognizer 재생성 방법 — 위 파일이 없거나 다시 만들 때만
Qualcomm AI Hub 계정 + Python venv 필요. (Mac 기본 conda 는 NumPy 2.x ABI 충돌 → 격리 venv 권장)
```bash
python -m venv .venv-qaihub --system-site-packages
.venv-qaihub/bin/pip install "numpy<2" "scipy>=1.13.1" "huggingface_hub>=0.34,<1.0" qai-hub-models
.venv-qaihub/bin/python -m qai_hub_models.models.easyocr.export \
  --components recognizer --quantize w8a8 --unroll-lstm \
  --target-runtime tflite --device "QCS8450 (Proxy)" --device-os 13 \
  --output-dir ocr_export/easyocr-recognizer-qcs8450-tflite-unroll-lstm
# 산출 .tflite 를 StreamingAssets/easyocr_recognizer_unroll_qcs8450.tflite 로 복사
```
- `--unroll-lstm` 필수: Qualcomm 가 "Unrolled LSTM is required to run on NPU via LiteRT(TFLite)" 라고 명시. 이게 있어야 QNN HTP delegate 로 NPU-only 동작(19902/19902 layers, CPU 0).
- AI Hub profile 에서 NPU-only gate(`primary_compute_unit=NPU`, CPU op 0, fully delegated) 통과한 유일한 recognizer 경로.

---

## 4. 빌드 (batchmode)

```bash
UNITY=/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity   # 경로는 환경에 맞게
cd glasses-app
"$UNITY" -batchmode -quit -nographics \
  -projectPath "$(pwd)" \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -buildTarget Android \
  -logFile "$(pwd)/Build/build-b22.log"
# 성공 시: Build/EagleEye-HelloAR-b22-slam-ocr.apk
```
- 빌드 설정(`Assets/Editor/BuildSpatialAnchorTest.cs`)이 자동으로: package `com.eagleeye.helloar`, scene `SpatialAnchorScene.unity`, OpenXR loader/RayNeo SLAM 설정 주입.
- 첫 빌드는 패키지 import 로 10~20분. 이후 incremental 수 분.
- Windows 는 `Unity.exe` 경로 + 동일 인자.

### Editor GUI 로 빌드해도 됨
Unity 로 `glasses-app` 열기 → 메뉴 `Build > SpatialAnchor APK`.

---

## 5. 설치 + 실행 (RayNeo X3 Pro)

```bash
adb install -r glasses-app/Build/EagleEye-HelloAR-b22-slam-ocr.apk
adb shell input keyevent KEYCODE_WAKEUP        # 화면 깨우기 (슬립이면 앱이 surface 못 얻어 멈춤)
adb shell svc power stayon true                # USB 중 화면 유지
adb shell am start -n com.eagleeye.helloar/com.rayneo.openxradapter.UnityOpenXrActivity
# (위 activity 가 안 먹으면) com.eagleeye.helloar/com.unity3d.player.UnityPlayerActivity
```

### 로그 보기 (adb 끊김 잦으면 on-device 파일 기록 권장)
```bash
adb shell "nohup logcat -v time > /sdcard/run.log 2>/dev/null &"
# ... 사용 후
adb shell "grep -aE 'OCRExtractor|EasyOCREngine|HelloAR|brandSource|TIMING' /sdcard/run.log" 
```

---

## 6. 동작 / 측정 포인트

### 파이프라인 (HelloAR.cs)
```
GyroTrigger(머리 1초 정지, 5초 쿨다운)
  → CameraPreview frame
  → ClipExtractor.Embed → ProductMatcher.MatchCategory ("cola?" 등)
  → (category 매칭 시) OCRExtractor.ExtractText  ← NPU EasyOCR (detector+recognizer)
  → ProductMatcher.ResolveBrand(ocrText)         ← brand = OCR 키워드("coca-cola"/"pepsi")
  → AdRenderer + SpatialAnchorTest               ← 경쟁사 광고 mp4 를 6DoF world-anchored 3D quad 로
```

### 이번 빌드의 측정용 설정 (HelloAR.cs)
| 플래그 | 값 | 이유 |
|---|---|---|
| `skipOcr` | **false** | NPU OCR 켬 |
| `enableClipBrandFallback` | **false** | 광고 뜸 ⟺ OCR 이 brand 읽음 (CLIP 환경편향 추정 차단 → 측정 해석 가능) |
| `brandDisambiguator` | **"ocr"** | 색이 아니라 OCR 키워드로 brand 확정 |
| `clipOnlyMode` | true | YOLO 우회 |
| SLAM(SpatialAnchorTest) | on | 3D world-anchored 광고 |

### 측정 3가지
1. **광고가 뜨나**: 콜라/펩시 라벨을 카메라에 잡으면 경쟁사 광고 3D quad 가 뜨는지. `brandSource=OCR` 로그로 OCR 이 결정했는지 확인.
2. **latency**: `EasyOCREngine TIMING ms: ... total=...` 로그. 사전 실측 ≈ decode~175 + detector~320 + recognizer~230(6박스) = **~0.8s/트리거** (글자 많으면 ↑). 200ms spec 초과 — 트리거 모델(gaze당 1회)이라 "응시 후 ~0.8s 지연" 체감.
3. **환경 robustness**: 거리/각도/조명 바꿔가며 인식률. 실패 프레임은 `/sdcard/Android/data/com.eagleeye.helloar/files/ocr_crops/` (saveOcrInput) 에 저장 → 조준 vs detector vs recognizer 귀속.

---

## 7. 트러블슈팅

| 증상 | 원인 / 해결 |
|---|---|
| 앱 시작 후 화면 멈춤(석양/splash) ~2.5분 | **정상** — NPU OCR recognizer 첫 launch QNN 컴파일 ~159초(캐시 없음). 기다리면 ready. (HTP cache 미구현 = 알려진 과제) |
| `Need to set FrameLayout in advance!` → 기기 꺼짐 | Unity 6 에서 빌드한 경우. **반드시 2022.3.62f3 로 빌드.** |
| 광고가 안 뜸 | logcat 에 `[HelloAR] Trigger` 있나? 없으면 트리거 미발화(머리 정지/카메라 surface). 있는데 `brandSource` 없으면 category 미매칭 또는 OCR 글자 못 읽음 → `ocr_crops` 확인. |
| OCR 글자 깨짐 | recognizer 모델 SHA 확인(3번). word-box(CRAFT region+link) 가 `EasyOCREngine.extractBoxes` 에 들어있어야 함(없으면 글자 과분할 → 깨짐). |
| `com.eagleeye.helloar` 설치 충돌 | 기존 앱 `adb uninstall com.eagleeye.helloar` 후 재설치. |
| adb device offline/사라짐 | `adb kill-server && adb start-server` 는 다른 세션과 공유 서버라 주의. 보통 케이블 재연결 또는 잠깐 후 재탐지. |

---

## 8. 코드 변경 요약 (이 브랜치 = ade5bca + 아래)
- `Assets/Plugins/Android/EasyOCREngine.java`: CRAFT **region+link(affinity) word-grouping** 추가(`extractBoxes`) — 글자 과분할 → 단어 박스로 병합(실기 NPU 에서 "COCA-COLA"/"PEPSI" 정확 read 확인). + recognizer shape/charset sanity check.
- `Assets/Scripts/OCRExtractor.cs`: recognizer 기본 = `easyocr_recognizer_unroll_qcs8450.tflite`(NPU-only). `rotationOverride>=0` 이면 `XRCameraHelper.getOrientation` 호출 skip(XR 세션 없는 scene 에서 native SIGSEGV 회피).
- `Assets/Scripts/HelloAR.cs`: `skipOcr=false`, `enableClipBrandFallback=false`, `brandDisambiguator="ocr"`.
- `Assets/Editor/BuildSpatialAnchorTest.cs`: package `com.eagleeye.helloar` (원본 spatialanchor.bisection 는 다른 실험이 점유), tag `b22-slam-ocr`.
- `Assets/Scripts/StartupProbe.cs`: OCRExtractor 가 참조하는 경량 시간 로깅 유틸.

자세한 검증 이력은 루트 `docs/progress-log.md` 2026-06-11 섹션 참조.
