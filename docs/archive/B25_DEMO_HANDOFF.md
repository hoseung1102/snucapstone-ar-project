# b25 데모 핸드오프 — 구조 · 결정 · 근거 · 이어받기

> ↩ 실험 history 인덱스: [../docs/EXPERIMENTS.md](../docs/EXPERIMENTS.md) — 이 문서는 빌드 **b25** 의 핸드오프 상세.

> 작성: 2026-06-11. 대상: 이 conquest AR 데모를 **이어서 개발할 다른 개발자**.
> 빌드: `b25-color-video` (branch `feature/b24-integrated`, commit `d36e813`). RayNeo X3 Pro / Unity 2022.3.62f3.
> 상태: **온디바이스에서 동작 확인됨** — 콜라/펩시 페트병 응시 → 경쟁사 **영상**이 정면에 world-anchored 로 재생. 크래시 없음.

---

## 0. 한눈에 (현재 동작)

```
GyroTrigger(머리 1초 정지)  →  카메라 프레임(ShareCamera RGB)
  →  CLIP category("콜라병인가?", threshold 0.45)
  →  [콜라면] 중앙 crop 평균색(mean RGB)으로 brand: 빨강→coca-cola / 파랑→pepsi
  →  경쟁사 매핑(coca-cola→펩시영상 / pepsi→코크영상)
  →  6DoF SLAM world-anchored quad 에 경쟁사 영상(VideoPlayer) 재생 (정면, 최대 2개 FIFO)
```
- 검증(2026-06-11): CLIP READY ~21s, 색상 브랜드 판별, 경쟁사 **영상** spawn·재생, 22분+ 무크래시.
- `[MONITOR]` heartbeat(0.5s) 로 host 대시보드(`tools/monitor/eagle_monitor.py`, `/eagle-monitor`)에서 실시간 펀널 관측 가능.

---

## 1. 브랜치 계보 (왜 이 브랜치인가)

- `feature/npu-ocr-slam-b22` (팀원): SLAM 파이프라인 + NPU EasyOCR(unrolled recognizer). **OCR 브랜드 판별이 온디바이스에서 실패**(아래 §3.1).
- `feature/slam-clip-worldanchor` (우리): b15(ATW/저해상도/head-locked HUD) + b16(CLIP-ready 플래그·5카운터·`[MONITOR]`·eagle-monitor) + b17(crash/SLAM/OpenXR 진단 `docs/findings-2026-06-11-crash-slam-openxr.md`).
- **`feature/b24-integrated`** = 위 둘 머지(commit 809a3ff). 그 위에 **b25**(d36e813) = OCR 제거 + 색상 브랜드 + 영상 + adb 디버그 훅.
- 패키지 `com.eagleeye.helloar` (팀원 b22 와 동일 — 옆 세션과 충돌 회피).

---

## 2. 아키텍처 / 핵심 파일 (`Assets/` 하위)

| 컴포넌트 | 역할 | 파일 |
|---|---|---|
| **HelloAR** | 파이프라인 오케스트레이터. 트리거→프레임→CLIP→**색상 brand**(`ResolveBrandByColor`)→경쟁사 매핑(`CompetitorAdVideo`)→`spatial.ShowAdBesideMatch`. 모든 데모 플래그의 단일 셋업 지점. **adb 디버그 훅**(`PollDebugHook`). | `Scripts/HelloAR.cs` |
| **SpatialAnchorTest** | RayNeo OpenXR 6DoF SLAM + **영상 광고 spawn**(`ShowAdBesideMatch`→`SetupAdVideo`→`OnAdVideoPrepared`). per-quad VideoPlayer+RenderTexture, max2 FIFO(`EvictAdQuad`), head-locked 2D HUD, `[MONITOR]` 로그(`EmitMonitorLog`), 발산 재앵커. | `Scripts/SpatialAnchorTest.cs` |
| **ClipExtractor** | MobileCLIP-S2 INT8 512-d 임베딩(NPU/HTP). 전처리 루프에서 **중앙 박스 평균색(`lastMeanR/G/B`)** 산출 = 색상 brand 입력. `useNpu`(기본 true). | `Scripts/ClipExtractor.cs`, `Plugins/Android/QnnClipEngine.java` |
| **ProductMatcher** | CLIP 임베딩 → category 매칭(top-K 코사인, threshold 0.45). `GetBrandByName`/`GetCategoryByName`. | `Scripts/ProductMatcher.cs` |
| **CameraPreview** | RayNeo ShareCamera RGB 라이프사이클(640×480@10). | `Scripts/CameraPreview.cs` |
| **GyroTrigger** | IMU 자이로 "시선 안정" 감지 → `OnTrigger` (threshold 0.5 / 1s / cooldown 5s). | `Scripts/GyroTrigger.cs` |
| **(비활성) OCRExtractor / EasyOCREngine** | b22 NPU OCR. **b25 에선 `skipOcr=true` 라 인스턴스화 안 됨**(코드는 남아있으나 런타임 미사용). | `Scripts/OCRExtractor.cs`, `Plugins/Android/EasyOCREngine.java` |
| **빌드 진입점** | `BUILD_TAG`/`OUTPUT_APK` 상수, OpenXR 로더 fix, boot.config 패처. | `Editor/BuildSpatialAnchorTest.cs` |

- **영상 에셋**: `Assets/StreamingAssets/db/ads_video/coke_bottle_ad.mp4`, `pepsi_bottle_ad.mp4`.
- 색상 판별: `HelloAR.ResolveBrandByColor` — `blueLean=mB-mR > colorBlueMargin` → pepsi, `redLean=mR-mB > colorRedMargin` → coca-cola, 둘 다 약하면 null(광고X = false-positive 게이트).

---

## 3. 핵심 결정 + 근거 (이 데모가 왜 이렇게 됐나)

### 3.1 OCR 제거 → 색상 브랜드 판별 ★
- **근거(온디바이스 측정, b22 트리거 #33~87)**: NPU EasyOCR recognizer 가 로드·실행은 되지만 **출력이 빈값 또는 쓰레기**("COCA-COLA"/"PEPSI" 인식 0회). 콜라/펩시 로고는 흘림체라 인쇄체 학습 OCR 가 못 읽음. → brand 확정 실패 → 광고 0.
- **대안**: 중앙 crop 평균색. 코크 빨강 / 펩시 파랑 분리 마진 ~1.0 (CLIP brand 분리 마진 0.07 ≪ 양자화 노이즈 0.3 대비 견고). 상세: `B22_TEST_RESULTS.md`, `../docs/freeze-accuracy-diagnosis.md`.
- **부수효과**: `skipOcr=true` → OCR 엔진 init 자체 제거 → **159초 첫-실행 HTP 컴파일 소멸 + OCR 의 Hexagon CDSP 클라이언트 0**(시작 빠름·크래시 위험↓).

### 3.2 광고 = 영상 (정지 PNG 아님)
- 과거 영상 경로는 **"spawn 직후 크래시"** 로 비활성화됐었음. **원인**: Android 에서 StreamingAssets mp4 가 APK 내부(`jar:file://…!/assets/…`)라 Unity VideoPlayer 가 재생 불가.
- **수정**: mp4 를 `persistentDataPath` 로 복사 후 `file://` 로 재생(`SetupAdVideo`, ClipExtractor 의 tflite 복사 패턴 미러). per-quad VideoPlayer. **에러 시 정지 PNG 폴백**(`errorReceived`/try-catch) → 코덱 문제로도 데모 안 죽음.

### 3.3 Unity 2022.3.62f3 전용 (Unity 6 절대 금지)
- Unity 6 빌드 = 안경에서 **검은화면**(Unity 로고도 안 뜸). RayNeo ARDK 의 `setFrameLayout(UnityPlayer)` 계약을 Unity 6 가 제거(GameActivity 기본) → SLAM/렌더 부팅 실패. 실제로 팀원 b20(Unity6 빌드)이 이 케이스였음. **반드시 2022.3.62f3.**

### 3.4 크래시(기기 꺼짐) 근본원인 + 완화
- **공유 Hexagon CDSP 세션 leak**: 우리 HTP 세션이 종료 시 깨끗이 release 안 됨 → 다음 실행이 오염된 CDSP 만남 → RayNeo SLAM FastRPC 핸들 stale → `system_server` 사망 → 리부트로만 복구. "리부트 후 첫 launch OK, 이후 크래시" 시그니처. 진단: `../docs/findings-2026-06-11-crash-slam-openxr.md`.
- **b25 완화**: OCR 제거로 CDSP 클라이언트 3→1(CLIP만). **데모/테스트는 리부트 직후 첫 실행**으로. (동기 release crash-fix 는 `feature/npu-ocr-slam-b22` working tree 에 준비됨 — b25엔 미포함.)

### 3.5 SLAM 8Hz / "값 안 변함" 의 진실
- 8Hz 는 RayNeo 런타임 고정 FFVINS 갱신율(설정 노브 없음). ATW 는 **회전 전용**(depth 미제출) 이라 병진(translation)은 8Hz 로 끊김. 상세: `../docs/findings-2026-06-11-crash-slam-openxr.md` §2.
- **FFVINS 는 정지 시 INITIALIZING/SEEKING(state 0) 에서 멈춤** — 카메라 움직임+특징점 있어야 수렴(TRACKING). 착용·이동하면 수초 내 수렴. (정지 상태의 status=0 은 고장 아님.)

### 3.6 adb 디버그 훅 (테스트 보조)
- `Application.persistentDataPath/eyad_debug.txt` 폴링(~0.5s). brand 쓰면 트리거/CLIP/색상 우회하고 경쟁사 영상 직접 spawn → 착용·트리거 없이 영상 경로 검증.
- `adb shell "echo coca-cola > /sdcard/Android/data/com.eagleeye.helloar/files/eyad_debug.txt"` → 펩시영상 / `echo pepsi` → 코크영상.

---

## 4. ★ 알려진 이슈 (이어받는 dev 이 볼 것)

1. ~~**한 물체에 광고가 여러 번 뜸 (중복 spawn)**~~ → **✅ b26 해결.** 브랜드당 1개로 dedup: `adBrands` 키 리스트(`coke-ad`/`pepsi-ad`, adQuads 와 1:1)를 두고, spawn 전 같은 키가 있으면 `EvictAdQuad` 후 현재 응시 위치에 새로(재소환). 같은 병 반복 응시해도 누적 0, 코크-광고 1 + 펩시-광고 1 최대. 입력 의존 0(자동). (`SpatialAnchorTest.cs` adBrands 선언 + ShowAdBesideMatch dedup 블록 + EvictAdQuad). 향후 보너스: 터치패드/링 클릭으로 전체 앵커 리셋(입력 검증 후).
2. **SLAM 은 움직여야 수렴** — 정지 시 SEEKING. 데모 시작 시 잠깐 머리 움직여 TRACKING 만든 뒤 사용.
3. **버전 mismatch 경고** — `SDK 1.1.6 ↔ Runtime 1.1.7.9`(minor 불일치). 현재 비치명적(FFVINS 동작). 안정성 위해 ARDK ≥1.1.7 flash 고려(portal 계정 필요).
4. **재실행 크래시(리부트 없이)** — §3.4 CDSP leak. crash-fix(동기 release) 적용 전까진 테스트 사이 리부트 권장.
5. **CLIP category 마진 얇음** — cola ~0.58 vs laptop ~0.55. 병을 시야 중앙에 확실히 넣어야 cola 잡힘.

---

## 5. 빌드 · 실행 런북

### 빌드 (Unity 2022.3.62f3 — 다른 머신이면 경로만 교체)
```bash
"<UnityHub>/2022.3.62f3/Editor/Unity.exe" -batchmode -quit -nographics -silent-crashes \
  -projectPath <repo>/glasses-app -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile <repo>/glasses-app/Build/build.log
# → Build/EagleEye-b25-color-video.apk. 로그에 === SUCCEEDED === 확인.
```
- 새 버전 시 `Editor/BuildSpatialAnchorTest.cs` 의 `BUILD_TAG`+`OUTPUT_APK` 2곳 동기 수정.
- ⚠️ Unity 6 금지. `ProjectSettings/ProjectVersion.txt == 2022.3.62f3` 빌드 전 확인.

### 설치 / 실행 (공유 ADB — writing 사전 허락, 무선 끊지 말 것)
```bash
SER=A06B4A95B784973 ; PKG=com.eagleeye.helloar
adb -s $SER install -r Build/EagleEye-b25-color-video.apk   # 캐시 보존(같은 패키지 서명)
adb -s $SER shell pm grant $PKG android.permission.CAMERA
adb -s $SER reboot          # ★ 테스트 전 리부트 = 클린 CDSP (크래시 회피)
adb -s $SER shell am start -n $PKG/com.rayneo.openxradapter.UnityOpenXrActivity
```

### 모니터링
```bash
# host 대시보드 (b16+ [MONITOR] heartbeat 파싱 — 펀널/CLIP/SLAM/영상)
python glasses-app/tools/monitor/eagle_monitor.py --serial A06B4A95B784973
# 또는 raw logcat 핵심만
adb -s $SER logcat Unity:I | grep -E "color mean|category=|MATCH|OnAdVideoPrepared|SLAM status|\[MONITOR\]"
```

### 색상 마진 튜닝 (코크/펩시 오판 시)
`HelloAR.cs` 의 `colorBlueMargin`(펩시 파랑 민감도)·`colorRedMargin`(코크 빨강). logcat `color mean=(R,G,B) blueLean= redLean=` 실측값 보고 조정.

---

## 6. 다음 작업 (우선순위)
1. **중복 spawn 수정**(§4.1) — 데모 체감 1순위.
2. crash-fix(동기 release) b25 에 병합 → 리부트 없이도 안정.
3. (선택) 평면 감지(`PlaneProbe.cs`, b22 working tree)로 광고를 책상 표면 앵커 → 8Hz judder 체감 완화.
4. (선택) 색상 마진 데이터 수집 후 고정.

> 깊은 근거: [`../docs/findings-2026-06-11-crash-slam-openxr.md`](../docs/findings-2026-06-11-crash-slam-openxr.md)(크래시/SLAM/OpenXR 표면), [`B22_TEST_RESULTS.md`](B22_TEST_RESULTS.md)(OCR 실패 측정), [`BUILD_OCR_SLAM_HANDOFF.md`](BUILD_OCR_SLAM_HANDOFF.md)(b22 빌드 런북), 루트 [`../AGENTS.md`](../AGENTS.md)(Codex/Claude 자동로드 지침).
