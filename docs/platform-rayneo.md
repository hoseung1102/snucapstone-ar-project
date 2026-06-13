<!-- RayNeo / Android / NPU / OpenXR 플랫폼의 "항구적 사실 · gotcha" 주제별 레퍼런스 (living document). -->

# 🛰️ platform-rayneo — RayNeo / Android / NPU / OpenXR 플랫폼 레퍼런스

> **이 파일의 역할**: RayNeo X3 Pro(Snapdragon AR1 Gen 1) 글라스 플랫폼의 **항구적 사실·제약·gotcha** 를 주제별로 모은 *living reference*.
>
> [`EXPERIMENTS.md`](EXPERIMENTS.md)(시간순 lab notebook = "무엇을 언제 시도했나")와 달리, **여기는 "현재 무엇이 진실인가"** 를 정교화·덮어쓴다. 같은 사실의 옛 설명을 발견하면 그 빌드 행을 EXPERIMENTS 에 남기되 *이 파일의 해당 섹션을 갱신*한다.
>
> **상태 기호**: ✅ 확정(confirmed) · 🔬 추정(inferred) · ⚠️ 주의(gotcha)
> **🆕 (코드에서 구조)**: 그 사실이 산문 문서엔 없고 코드/asset 에만 있던 것 — 처음으로 문서화됨.
> 각 엔트리는 **사실 / 근거(왜) / 출처 / 상태**. 근거가 없으면 생략.

---

## 📷 카메라

### SLAM 6DoF 활성 시 표준 WebCamTexture 는 black frame 만 — ShareCamera RGB 로 우회
- **사실**: OpenXR `CameraAttitudeType=8193`(SLAM 6DOF) 활성 시 표준 Android Camera2(`WebCamTexture`)는 black frame 만 반환한다. RayNeo SDK 의 ShareCamera(병렬 RGB 채널, `OpenCamera(XRCameraType.RGB,...)`)는 SLAM 과 동시 사용 가능해 이 문제를 우회한다. ShareCamera RGB 관측 해상도: 1280×720@30(b22) 또는 640×480@10(저부하).
- **근거**: Android 카메라는 single-owner — 상시 동작하는 SLAM 스택이 센서를 점유해 앱 레벨 `WebCamTexture` 는 실제 프레임을 못 받는다. ShareCamera 는 별도 병렬 RGB 채널.
- **출처**: `spatial_anchor_test/Assets/Scripts/CameraPreview.cs:9-13`; `docs/dev-guide.md:56`; `docs/integration_log.md:127`; `docs/spatial-anchor-handoff.md:36,188`
- **상태**: ✅ 확정

### 카메라 FOV ≠ 사용자 시선 — 병을 눈높이 정면으로 들면 프레임 밖
- **사실**: 글라스 카메라는 시선이 아니라 아래·팔 방향을 본다. "눈높이 정면"으로 든 콜라병은 카메라 프레임 완전 밖이라 절대 검출 안 됨. 캘리브레이션 결과 물체를 **팔 뻗어 몸 앞 무릎/노트북 높이**로 둬야 잡힌다. 실세계 최대 실패 모드로 명시됨. (ShareCamera FOV 도 눈 시선과 정확히 일치 안 함.)
- **근거**: 광각 world 카메라가 gaze 가 아니라 SLAM 커버리지용으로 장착·조준돼 광축이 시선축에서 크게 아래로 벌어짐.
- **출처**: `docs/vision.md:18,33`; `docs/progress-log.md:658`; `docs/spatial-anchor-handoff.md:36`
- **상태**: ⚠️ 주의

### Qualcomm 카메라 HAL 이 앱 클라이언트 없어도 SIGPIPE crash loop (firmware 레벨)
- **사실**: `vendor.camera-provider-2-7` 가 매 10~90초 `signal 13(SIGPIPE)` 를 spam 하고 Camera 0(RGB)+Camera 1(SLAM stereo) 모두 cycling. 앱 클라이언트가 없어도 cycling → system/firmware 레벨(ShareCamera 무관). provider 는 자가 재기동하나 `com.rayneo.xr.runtime` 이 카메라 재연결을 안 함(retry 로그 0건) → SLAM 이 vision 없이 IMU 단독 적분 → 발산. device reboot 권장.
- **근거**: 펌웨어 레벨 provider 불안정 + xr.runtime 의 카메라 reconnect 로직 부재.
- **출처**: `docs/integration_log.md:144`; `docs/freeze-accuracy-diagnosis.md:32-33,117`
- **상태**: ✅ 확정

### 앱 pause 중 preview 채널을 열어두면 SIGPIPE → SLAM 595m 발산
- **사실**: `OnApplicationPause(true)` 때 preview 가 열려 있으면 미소비 preview 버퍼가 camera provider 를 `SIGPIPE(signal 13)` 로 사망시켜 SLAM(camera 1)이 발산(camPos 595m, 06-11 00:10:44 관측). pause 시 `CloseCamera`, resume 시 reopen 으로 예방.
- **근거**: provider 가 미소비 버퍼 큐로 인해 SIGPIPE 사망 → SLAM 카메라 입력 끊김 → pose 발산.
- **출처**: `spatial_anchor_test/Assets/Scripts/CameraPreview.cs:206-222`
- **상태**: ⚠️ 주의

### 매칭 직후 `CloseCamera()` 가 메인스레드 hang → 비활성화
- **사실**: v1.1 H1 fix — 매칭 직후 `cam.CloseCamera()` 가 네이티브 카메라 HAL teardown 을 SLAM/디코더와 동시 실행해 메인스레드 hang(정지 이미지에서도 freeze, PID alive·state S·runInBackground=1). 그래서 `CloseCamera()`/`ReopenCameraAfter` 코루틴을 주석 처리로 비활성. ShareCamera RGB 와 SLAM mono 는 별도 채널이라 카메라를 안 닫아도 quad 는 world-anchored 유지.
- **근거**: 카메라 HAL teardown 과 SLAM RGB pipeline 이 동시 접근하며 네이티브 락 경합.
- **출처**: `HelloAR.cs:323-328`
- **상태**: ⚠️ 주의

### 카메라 sensor 가 CW 90도 누워있어 전처리 rotation=270 하드코딩 (videoRotationAngle 무시)
- **사실**: `QnnYoloDetector.PreprocessTexture` 는 `videoRotationAngle` 등 런타임 보고값을 무시하고 `rotation=270`(=CCW 90도)으로 하드코딩한다('Mac A/B 실험으로 확정', 'hardware 일관'). 회전은 320×320 readback 픽셀을 `dstX=srcY, dstY=N-1-srcX` 로 직접 재배치. OS 회전 메타데이터가 불안정(rot_00 정상 vs rot_01 90도)이라 하드코딩이 더 신뢰성 높음.
- **근거**: RayNeo X3 Pro 카메라 센서가 물리적으로 CW 90도 누워 장착돼 보정 필요하고, OS 회전 메타데이터가 캡처마다 불일치.
- **출처**: `spatial_anchor_test/Assets/Scripts/QnnYoloDetector.cs:248-265`; `docs/progress-log.md:55,59,644`; `docs/vision.md:34`; `docs/dev-guide.md:51,221`
- **상태**: ✅ 확정

### `_handler.texture` 는 매 프레임 재할당 — Update 마다 다시 잡아야 함 🆕 (코드에서 구조)
- **사실**: ShareCamera 의 `XRChannel` 은 `bufferSize=3` ring buffer 라 `_handler.texture` 가 `onImageAvailable` 콜백에서 재할당된다. `webCamTex` 를 한 번 캐시하면 stale 해지므로 `Update()` 에서 매 프레임 `_handler.texture` 를 다시 읽어 재대입한다.
- **근거**: 3-슬롯 ring buffer 라 프레임마다 다른 `Texture2D` 인스턴스를 가리킴.
- **출처**: `spatial_anchor_test/Assets/Scripts/CameraPreview.cs:194-204`
- **상태**: ⚠️ 주의

### ShareCamera 첫 프레임 판정은 texture!=null 만으로 불충분 — width<=16 가드 + 5초 timeout 🆕 (코드에서 구조)
- **사실**: `ShareCamera.OpenCamera` 후 첫 프레임 대기 루프는 `_handler.texture==null` 뿐 아니라 `_handler.width<=16` 도 검사하며 5초 timeout 을 둔다. 지원 해상도 못 찾으면 SDK default 640×400 으로 폴백.
- **근거**: texture 가 non-null 이어도 width 가 ~16 인 초기 stub(placeholder) 단계가 먼저 할당된 뒤 실제 프레임이 채워지는 듯.
- **출처**: `spatial_anchor_test/Assets/Scripts/CameraPreview.cs:160-173`
- **상태**: 🔬 추정

### OnDisable 에선 카메라를 끄지 않음 — OnDestroy 에서만 close 🆕 (코드에서 구조)
- **사실**: `OnDisable` 에서는 의도적으로 `CloseCamera` 를 호출하지 않는다(카메라 재시작 비용이 큼). close 는 `OnDestroy` 에서만 한다.
- **근거**: ShareCamera open 이 무거워 토글 비용 회피.
- **출처**: `spatial_anchor_test/Assets/Scripts/CameraPreview.cs:229-232`
- **상태**: ✅ 확정

### saveTriggerFrames 핫패스 디스크 쓰기 제거 + ShareCamera Texture2D 는 GetPixels32 직접 호출 불가
- **사실**: v1.1 — 매 trigger 시 frame jpg 저장(`saveTriggerFrames`)을 데모에선 off(GPU readback+jpg encode+File write 가 트리거 직후 프레임 블록). 또 v0.9.0 부터 `webCamTex` 가 `WebCamTexture` → ShareCamera `Texture2D` 로 바뀌어 `GetPixels32()` 직접 호출 불가(WebCamTexture 전용) → RenderTexture readback(`Graphics.Blit`+`ReadPixels`)으로 통일.
- **근거**: `GetPixels32` 는 WebCamTexture 전용 메서드. 일반 `Texture2D`(BGRA32)는 RenderTexture blit 후 ReadPixels 만 가능.
- **출처**: `HelloAR.cs:74-76,202-204,504-528`
- **상태**: ✅ 확정

### ShareCamera 채널은 RGB(id"0")/VGA(id"1") 두 개뿐 — VGA(cam1)가 SLAM 스테레오 입력
- **사실**: `XRCameraType.RGB`→camera id `"0"`, `XRCameraType.VGA`→camera id `"1"`. RGB 가 앱용 컬러 프리뷰, VGA(mono)는 SLAM 이 쓰는 스테레오 트래킹 카메라. `CurrentCameraType` 기본값 RGB.
- **근거**: RGB 는 컬러 출력용, VGA 는 SLAM VINS 트래킹 전용 채널로 분리.
- **출처**: `spatial_anchor_test/Packages/com.unity.xr.rayneo.openxr/SDK/Runtime/Scripts/APIs/ShareCamera/ShareCamera.cs:56-65`; `XCameraParams.cs:11-14`
- **상태**: ✅ 확정

### RGB 프리뷰 기본 해상도/포맷 640×400 RGBA, crop region 은 8px 정렬 강제 🆕 (코드에서 구조)
- **사실**: `DefaultCameraResolution=640×400`. `OpenCamera` 는 `XR_CAMERA_PROPERTY_MEMORY_SOFTWARE_CROP_REGION(0xF001)` 로 crop 을 주고 포맷은 `kImageMemoryRGBA(0x3004)` 로 고정. `CameraCorpInfo.Align()` 이 x/y/w/h 를 모두 8 의 배수로 올림(`(v+7)&~7`) → 요청 해상도가 조용히 바뀜.
- **근거**: 하드웨어/ISP crop 은 8픽셀 정렬 블록 단위라 비정렬 값은 올림됨.
- **출처**: `.../ShareCamera/ShareCamera.cs:84,132,155-162`; `XCameraParams.cs:18,31-41`; `XRDefines.cs:65`
- **상태**: ✅ 확정

### 요청 해상도가 지원 목록에 없으면 OpenCamera 는 null 반환(폴백 없음) 🆕 (코드에서 구조)
- **사실**: `JudgeIsSupportThisResolution()` 이 width AND height 둘 다 일치하는 항목을 못 찾으면 `OpenCamera` 는 로그만 찍고 null 반환. 지원 목록은 `getSupportResolutions(RGB/VGA)` 로 조회하며 lazy 캐시. 근사 해상도 자동 폴백 없음 → 호출 측이 직접 nearest-match 매칭해야 함(`CameraPreview.cs` 가 별도 구현).
- **근거**: SDK 가 정확 일치만 허용.
- **출처**: `.../ShareCamera/ShareCamera.cs:113-116,291-317,72-79`
- **상태**: ✅ 확정

### 프리뷰 프레임은 BGRA32 Texture2D 3개 ring buffer 를 native 가 직접 채움 🆕 (코드에서 구조)
- **사실**: `XRChannel` 은 `bufferSize=3`(takePicture 는 1)개의 `Texture2D(TextureFormat.BGRA32)` 를 만들고 `GetRawTextureData<byte>()` 의 unsafe 포인터 배열을 native 에 넘긴다. native 가 이 외부 스토리지에 직접 round-robin 으로 써서 콜백으로 슬롯을 알려줌. `kImageMemoryRGBA` 를 요청해도 실제 텍스처는 **BGRA32**.
- **근거**: GL/ISP 메모리 레이아웃이 BGRA 이고, zero-copy 를 위해 미리 할당한 텍스처 풀에 native 가 기록.
- **출처**: `.../OXR/Runtime/Scripts/Extensions/public/device/XRCamera.cs:39,74-85,104-122,383`; `CameraPreview.cs:198-203`
- **상태**: ✅ 확정

### 프레임 콜백은 native 스레드에서 옴 — lock(this)+메인스레드 펌프로 분리 🆕 (코드에서 구조)
- **사실**: `cbPreviewDispatcher` 는 `[MonoPInvokeCallback]` 으로 native 스레드에서 호출되며 `XRCameraHandler.onImageAvailable` 은 `lock(this){ texture=image; m_ImgUpdate=true }` 만 한다. 실제 `texture.Apply()` 와 RawImage 바인딩은 Updater 메인스레드 콜(`UpdateT2d`)에서 `didUpdateThisFrame` 플래그로 처리. 콜백에서 `texture.Apply()` 직접 호출 금지.
- **근거**: Unity 텍스처 GPU 업로드는 메인스레드 전용이라 플래그+메인스레드 펌프로 분리.
- **출처**: `.../device/XRCamera.cs:179-200`; `.../ShareCamera/XCameraParams.cs:25-53`
- **상태**: ✅ 확정

### XRCameraFlag: AE 수렴 전엔 출도 안 함 — DO_NOT_FILTER_BY_AE_STATE 로 우회 🆕 (코드에서 구조)
- **사실**: 기본 상태에서는 AE(자동노출)가 수렴해야만 첫 프레임이 나온다(주석: '当且仅当AE收敛才会出图'). `XR_CAMERA_FLAG_DO_NOT_FILTER_BY_AE_STATE(1<<3)` 를 켜면 수렴 중 드롭을 건너뛰고 첫 프레임 즉시 출도. `PREFER_CAMERA_LOW_POWER(1<<2)` 는 프레임레이트를 낮춰 출도 지연/저fps 유발.
- **근거**: ISP 가 노출 안정화 전 프레임을 의도적으로 필터링.
- **출처**: `.../OXR/Runtime/Scripts/Extensions/public/core/XRDefines.cs:43-59`
- **상태**: ⚠️ 주의

### MEMORY_ROTATION_AUTO_CORRECT 켜지면 orientation 90/180 에서 출력 w/h swap 🆕 (코드에서 구조)
- **사실**: `XR_CAMERA_FLAG_MEMORY_ROTATION_AUTO_CORRECT(1<<1)` 사용 시 `XRChannel` 생성자가 `getOrientation(cameraID)` 가 90 또는 180 이면 width/height 를 바꿔 텍스처를 만든다(주석: 'sensor 설치 위치 보정, 非필요시 사용 금지'). 요청 해상도와 실제 출력 텍스처 가로/세로가 뒤바뀔 수 있음.
- **근거**: 카메라 센서가 물리적으로 회전 장착돼 native 가 보정 회전을 적용하면 차원이 transpose 됨.
- **출처**: `.../device/XRCamera.cs:55-72`; `XRDefines.cs:47-49`; `XRCameraHelper.cs:55-58`
- **상태**: ⚠️ 주의

### OpenCamera 는 cameraType 을 무시하고 width×height 만으로 dedup (버그 소지) 🆕 (코드에서 구조)
- **사실**: `OpenCamera()` 는 `m_Cameras` 안에서 `item.width==w && item.height==h` 인 핸들을 찾으면 그대로 반환한다. `cameraType`(RGB/VGA)을 비교하지 않으므로 같은 해상도로 VGA 를 열면 먼저 열린 RGB 핸들이 반환될 수 있다.
- **근거**: dedup 키가 (w,h) 뿐이고 `CurrentCameraType` 을 포함하지 않음.
- **출처**: `.../ShareCamera/ShareCamera.cs:134-140`
- **상태**: ⚠️ 주의

### CloseCamera 는 타입과 무관하게 항상 m_RGBCamera 로 close 🆕 (코드에서 구조)
- **사실**: `CloseCamera(handler)` 는 VGA 로 연 채널이라도 `m_RGBCamera.stopPreviewChannel(channelId)` 와, 마지막 채널이면 `m_RGBCamera.close()` 를 호출한다. `m_CurrentXRCamera`/VGA 핸들을 쓰지 않아 VGA 채널 close 가 어긋날 수 있음.
- **근거**: 구현이 RGB 우선이라 close 경로가 RGB 인스턴스에 고정됨.
- **출처**: `.../ShareCamera/ShareCamera.cs:181-192`
- **상태**: ⚠️ 주의

### RayNeo 카메라 intrinsics 하드코딩 노출 + 평면검출/시선 pose 제공하나 앱 미사용
- **사실**: 하드코딩 intrinsics `fx=376.686 fy=376.1188 cx=319.3743 cy=241.355`(cam1 SLAM 스테레오 기준, `RayNeoInfo.GetPhysicalCameraParams()`). `EnablePlaneDetection`/`GetPlaneInfo`(폴리곤 메시+수평/수직 분류)와 `gaze_ext` 시선 pose, `NineAxisAzimuth`(yaw)도 제공되나 데모 파이프라인 미사용. → bbox+intrinsics 핀홀 역투영 거리추정, 표면 앵커(parallax↑로 8Hz judder 완화 시너지), 시선 트리거 업그레이드 여지.
- **근거**: ARDK 가 intrinsics/plane/gaze API 를 노출하나 데모 파이프라인이 IMU 트리거+정면 1.2m placement 만 씀.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:98,101,117-120`; `RayNeoInfo.cs:74-99`
- **상태**: ✅ 확정

### YUV420SP_RGB 헬퍼는 Color32 alpha 를 1 로 채우는 버그성 코드 🆕 (코드에서 구조)
- **사실**: `ShareCamera.YUV420SP_RGB()` 가 변환 픽셀을 `new Color32(R,G,B,1)` 로 콜백 — alpha=1(0~255 중 거의 0). 보통 미사용 경로지만 그대로 쓰면 결과가 사실상 투명.
- **근거**: alpha 인자에 불투명(255) 대신 1 을 하드코딩한 SDK 측 실수.
- **출처**: `.../ShareCamera/ShareCamera.cs:252-282`
- **상태**: ⚠️ 주의

---

## 🧭 SLAM · 6DoF

### SLAM 6DoF on/off 는 yaml 매직 상수 하나로 — 4097=DOF3(3DoF), 8193=SLAM(6DoF)
- **사실**: `RayNeoSupportFeature.CameraAttitudeType`(OpenXR Package Settings.asset) 값이 `4097`(0x1001=DOF3, rotation-only)이면 `EnableSlamHeadTracker()` 를 불러도 6DoF 진입 못 함. `8193`(0x2001=SLAM)이어야 6DoF. `OnInstanceCreate` 가 `settings["trackerAlgorithm"]=(int)CameraAttitudeType` 으로 native 에 fixed 전달 → 런타임 API 가 못 덮음. DOF3 로 두면 translation 을 잃어 world-anchored 콘텐츠가 파괴된다. **함정**: enum `X3_Normal=0x1001` 이 `DOF3=0x1001` 과 우연히 겹쳐 audit 함정이었음.
- **근거**: `trackerAlgorithm` 이 트래킹 솔버 모드를 선택하며 네이티브에 고정값으로 전달됨. SLAM 만 6DoF translation 제공.
- **출처**: `spatial_anchor_test/Assets/XR/Settings/OpenXR Package Settings.asset:671`; `RayNeoSupportFeature.cs:33,61,192-198`; `XRInterfaces.cs:41-48`; `spatial_anchor_test/JOURNEY.md:27-38`
- **상태**: ✅ 확정

### Spatial Anchor: in-session 6DoF 는 지원, cross-session persistence 는 미지원 — SLAM 은 always-on/끌 수 없음
- **사실**: RayNeo X3 Pro Spatial Anchor API 는 부분 지원 — in-session 6DoF world-locked 렌더는 되나 cross-session persistence 는 안 됨. 내장 SLAM 엔진은 6DoF 트래킹용으로 always-on 이며 개발자가 끌 수 없어 SLAM(AI 추론이 아니라)이 dominant 배터리 소모원. v1 은 앵커 의존을 버리고 2D head-locked HUD 로 대체.
- **근거**: SLAM 이 6DoF pose 를 위해 상시 동작, SDK 가 내부 관리하며 off 스위치 없음, anchor state 는 세션 간 직렬화 안 됨.
- **출처**: `docs/vision.md:305,369,475`; `docs/client-spec.md:182,206`; `docs/dev-guide.md:236`
- **상태**: ✅ 확정

### 6DoF 는 펌웨어 게이팅 의심 — X3 Pro 가 다수 앱에서 '현재 3DOF만'
- **사실**: 독립 리뷰(skarredghost 2025-12)에 X3 Pro 가 다수 앱에서 '현재 3DOF만'. 6DoF 가 펌웨어 버전 게이팅일 수 있음(1.1.6↔1.1.7.9 mismatch + 0Hz 블로커와 연결). 우리 기기의 6DoF 안정 surfacing 여부는 `raw==1` 실측으로만 확인 가능.
- **근거**: RayNeo 펌웨어 버전이 6DoF surfacing 을 조건부로 막을 가능성.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:122-123,82`
- **상태**: 🔬 추정

### GetHeadTrackerStatus 는 pose 신뢰도 지표가 아님 (0/1/2 3-state) — 발산해도 0
- **사실**: `Algorithm.GetSlamStatus()` 가 `GetHeadTrackerStatus()` int 를 캐스트: `FFVINS_INITIALIZING=0`, `TRACKING_SUCCESS=1`, `TRACKING_FAIL=2`. 앱은 status==1 을 '수렴'으로 쓴다. **SLAM 이 595m 로 발산해도 status 는 계속 0** 으로만 보고 — pose quality 미반영이라 신뢰 지표로 못 씀. FFVINS 는 정지 시 INITIALIZING/SEEKING(0)에 멈추고 카메라 움직임+특징점이 있어야 TRACKING 수렴(정지 status=0 은 정상).
- **근거**: FFVINS 가 init/성공/실패 3상태만 노출하고 드리프트/발산은 별도 신호 없음. SDK 에 SLAM 리셋 API 없음.
- **출처**: `.../SDK/Runtime/Scripts/APIs/Algorithm.cs:42-62`; `SpatialAnchorTest.cs:600,744-747`; `docs/freeze-accuracy-diagnosis.md:34`; `B25_DEMO_HANDOFF.md:72`; `B22_TEST_RESULTS.md:27`
- **상태**: ✅ 확정

### SLAM 발산 자체 감지: |camPos|>30m 2초 연속 → 콘텐츠 재앵커만(재토글 금지)
- **사실**: SDK status 가 신뢰 불가라 앱이 `xrCam.transform.position.magnitude > 30m` 가 2초 이상 연속이면 diverged 로 자체 판정. 재앵커(콘텐츠를 카메라 앞으로 이동)는 최대 10초당 1회. SLAM 재토글(Disable/Enable)은 검증 전이라 금지 — 콘텐츠 재앵커만 수행.
- **근거**: SDK 에 SLAM 리셋 API 없음 + status 가 pose 신뢰도 미반영 → 위치 크기로만 감지 가능.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:623-644`
- **상태**: ✅ 확정

### SLAM enable 경로: OpenSLAMOnStart 자동 토글 or 런타임 EnableSlamHeadTracker — 리셋 API 없음(Recenter 만)
- **사실**: `RayNeoSupportFeature.OpenSLAMOnStart=true` 면 `xrCreateInstance` 직후 `EnableSlamHeadTracker()` 자동 호출. 앱은 이와 별개로 런타임에 `XRInterfaces.EnableSlamHeadTracker()`/`DisableSlamHeadTracker()` 를 직접 부를 수 있고, SDK 에 SLAM '리셋' API 는 없음(Recenter 만). 발산 시 콘텐츠 재앵커로만 대응 가능.
- **근거**: 트래커 enable/disable 과 recenter 만 노출, 맵 리셋은 벤더 블랙박스.
- **출처**: `.../OXR/Runtime/Scripts/OpenXR/RayNeoSupportFeature.cs:31,64-67`; `XRInterfaces.cs:56-69`; `SpatialAnchorTest.cs:139`
- **상태**: ✅ 확정

### vendor SlamDemoCtrl 최소 시퀀스만 — InitializeLoaderSync/StartSubsystems·EnablePlaneDetection 은 divergence 유발 🆕 (코드에서 구조)
- **사실**: SLAM 셋업은 vendor `SlamDemoCtrl.Start()` 와 동일한 최소 시퀀스(`EnableSlamHeadTracker` + `OnPostUpdate` 구독)만 한다. 이전의 `InitializeLoaderSync`/`StartSubsystems`(런타임 XR 재초기화)와 `EnablePlaneDetection` 은 vendor 가 안 하는 divergence 원인이라 제거. XR 은 `automaticLoading=1` 로 이미 auto-init 됨.
- **근거**: XR 이 이미 auto-init 상태인데 재초기화하면 SLAM 파이프라인이 깨짐.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:133-143`
- **상태**: ✅ 확정

### RayNeo 평면 검출(plane detection) API 표면 존재 — 단 SLAM 발산 유발이라 의도적 비활성 🆕 (코드에서 구조)
- **사실**: `Algorithm.cs` 에 완전한 평면 검출 API 가 구현돼 있음: `EnablePlaneDetection()`/`DisablePlaneDetection()`/`GetPlaneInfo(XRPlaneInfo[])`/`ConvertPlanePosition`/`ConvertPlaneRotation`/`CreatePlaneMesh(XRPlaneInfo, GameObject, invertYZ, Material)`/`GetAzimuth()`. `XRPlaneInfo` = 위치/회전/로컬 폴리곤 버텍스 배열. **그러나 미사용은 미개발이 아니라 의도적 제거**: `SpatialAnchorTest.cs:135` 주석이 `EnablePlaneDetection` 은 vendor 가 안 하는 SLAM divergence 유발이라 뺐다고 명시.
- **근거**: 평면 검출을 켜면 SLAM 파이프라인 발산(위 SlamDemoCtrl 최소시퀀스 원칙과 동일 근거). v2 에서 평면 위 광고 배치를 하려면 이 발산 회귀부터 풀어야 함.
- **출처**: `.../SDK/Runtime/Scripts/APIs/Algorithm.cs:67-128`; `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:135`
- **상태**: ✅ 확정 (API 존재) / ⚠️ 주의 (비활성 인과)

### RayNeoApi_GetHeadTrackerPose 는 .so 에 미export 된 죽은 심볼 (EntryPointNotFound)
- **사실**: 직접 포즈 폴링용 `RayNeoApi_GetHeadTrackerPose(float[] position, float[] rotation)` 는 C# `DllImport` 선언만 있고 `libRayNeoXRUnityInterfaces.so` 에 export 안 됨 → 호출 시 `EntryPointNotFound`. `SpatialAnchorTest` 의 `htPos/htRot/htRet` centerEye 라우팅 우회나 LateUpdate native polling(통합 v0.9.0)은 effect 0. Unity 가 head pose 받는 **유일 경로** = OpenXR centerEye → TrackedPoseDriver → `RayNeo.HeadTrackedPoseDriver.OnPostUpdate`. (IMU 콜백 `RayNeoApi_RegisterIMUEventCallback` 은 .so export 되나 C# 미바인딩.)
- **근거**: ARDK C# 바인딩이 네이티브에 없는 심볼을 선언해 놓아 컴파일은 되나 런타임에 무효.
- **출처**: `.../public/core/XRInterfaces.cs:246-247`; `SpatialAnchorTest.cs:103-112`; `docs/findings-2026-06-11-crash-slam-openxr.md:67,108,111`
- **상태**: ✅ 확정

### world-anchor 콘텐츠 8Hz judder 는 RayNeo 런타임 고정 FFVINS 맵 pose 케이던스 (고칠 노브 없음)
- **사실**: base driver 가 `UpdateAndBeforeRender` 로 매 렌더 프레임 centerEye 를 재read 해도 pose 변화율이 ~8Hz(`headPoseCallCount` 측정). 8Hz = FFVINS 맵 pose 전달 케이던스이며 Unity throttle 아님, 설정 노브 없음(.so 어디에도 rate/Hz 키 0건). 솔버 틱 사이엔 같은 latched pose 반환.
- **근거**: FFVINS 솔버가 8Hz 로만 새 맵 pose 를 push 하고 그 사이엔 동일 pose 유지.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:64-68`; `B25_DEMO_HANDOFF.md:71`
- **상태**: ✅ 확정

### provisional anchor 패턴: SLAM 미수렴에도 즉시 spawn, status==1 도달 시 1회만 reposition
- **사실**: v2 패턴은 SLAM converge 를 안 기다리고 Start 에서 즉시 provisional anchor + HUD spawn 후, `lastSlamStatus==1` 도달 시 `repositionedOnConverge` 플래그로 첫 프레임에 단 1회 anchor 를 6DoF 위치로 reposition. v1(converge 대기 후 spawn)은 OpenXR stereo 에서 OnGUI 가 invisible 이라 8분 logcat 만 보는 진단 불능 상태였음.
- **근거**: stereo 렌더에서 IMGUI(OnGUI) 미지원 → world-space 객체만 보임.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:6-11, 599-621`
- **상태**: ✅ 확정

### RayNeo runtime pose 는 우손좌표계 → Unity 변환 시 quaternion x,y 와 position z 부호 반전 🆕 (코드에서 구조)
- **사실**: `XRMathUtils.RightHand2UnityLeftHand`: `rotation.x=-x, rotation.y=-y`(z,w 유지), `position.z=-z`. `GetPlaneInfo` 가 평면 pose 마다 이 변환을 적용. SLAM pose 도 동일 우손→좌손 변환 전제.
- **근거**: OpenXR/SLAM runtime 은 우손(RH) 좌표계, Unity 는 좌손(LH)이라 축 핸드니스 변환 필요.
- **출처**: `.../public/utils/XRMathUtils.cs:13-58`; `XRInterfaces.cs:130-140`
- **상태**: ✅ 확정

### 디바이스 미연결 시 HeadTrackedPoseDriver 가 회전에 z축 -90° 보정 적용 🆕 (코드에서 구조)
- **사실**: `HeadTrackedPoseParams.GetRotation` — 디바이스에서는 raw 그대로지만 `!RayNeoInfo.HasDevice()`(디바이스 없음)일 때 `euler.z -= 90` 보정을 가한다. `ResetRotation()` 은 현재 raw 의 역쿼터니언을 `CameraInverseQuaternion` 으로 잡아 recenter 구현. 에디터와 실기 자세가 다를 수 있음.
- **근거**: 디바이스 없이(시뮬/폰) 돌릴 때 landscape 기준 orientation 보정용.
- **출처**: `.../SDK/Runtime/Scripts/Comps/Inputs/HeadTrackedPoseDriver.cs:139-166`
- **상태**: ⚠️ 주의

### GetNineAxisOrientation(자력계 9축)·ActualCameraAttitude 경로는 전부 비활성
- **사실**: `Algorithm.GetNineAxisOrientation()` 과 `XRInterfaces.GetNineAxisOrientation()` 이 통째로 주석. `NineAxisAzimuth`(yaw float)만 살아있음. `HeadTrackedPoseDriver` 의 `imu2Camera` 보정 쿼터니언, `ActualCameraPositionOffset/RotationOffset`, `m_useActualCameraAttitude` 도 전부 주석처리. → yaw(Azimuth)만 보조로 쓸 수 있음.
- **근거**: 9축/카메라 자세 경로는 미완성 또는 신뢰성 문제로 비활성화된 것으로 보임.
- **출처**: `.../APIs/Algorithm.cs:283-297`; `XRInterfaces.cs:71-77,176-179`; `HeadTrackedPoseDriver.cs:18-20,55-64,153-155`
- **상태**: 🔬 추정

### DeviceType() 은 0x1000 하드코딩 stub — X2/X3 구분 안 됨
- **사실**: `XRInterfaces.DeviceType()` 은 `0x1000`(=X2_Normal) 고정 반환, `HostType()` 은 -1 고정. enum: `X2_Normal=0x1000, X3_Normal=0x1001`, 분체형(BB) 0x0020~0x00FF, 일체형(X) 0x1000~0x10FF. `HasDevice()=CurrentDeviceType()>=0`. 실제 타입은 별 경로로 옴.
- **근거**: `XRInterfaces.DeviceType` 은 stub(상수)이고 실제 타입은 별 경로로 옴.
- **출처**: `.../public/core/XRInterfaces.cs:153-161`; `RayNeoInfo.cs:51-54`
- **상태**: ✅ 확정

---

## ⚙️ NPU · QNN

### Hexagon v73 이지만 FP16 벡터 유닛 없음 (cost-reduced AR1) → w8a8 양자화 필수
- **사실**: RayNeo X3 Pro / Snapdragon AR1 Gen 1 NPU 는 Hexagon v73 인데, AR1 은 cost-reduced SoC 라 **FP16 벡터 유닛이 없다**(INT8/INT16 만). float 모델은 ~5.5% 만 NPU 위임 → w8a8(full INT8) 양자화 필수. 양자화는 detection confidence 를 10~50% 깎는다.
- **근거**: FP16 유닛이 없으면 QNN delegate 가 float op 를 NPU 에 못 올려 float 그래프가 CPU 폴백(~5.5% NPU). INT8 하드웨어 가속만이 NPU 경로.
- **출처**: `docs/vision.md:28-29,310`; `docs/dev-guide.md:11,136,143`
- **상태**: ✅ 확정

### NPU 세대는 v68/v69 가 아니라 v73 — 'libQnnHtpV73Stub.so not found' 로그로 확정
- **사실**: 옛 문서는 Hexagon v68/v69 로 가정했으나 실제 칩은 **v73**(SoC code SSG2125P) — logcat 의 `'libQnnHtpV73Stub.so not found'` 에러로 정확히 식별. v73 vs XR2 Gen 2 proxy(v69)는 4세대 차이로 AR1 이 더 신형 → 옛 XR2→AR1 ×1.5~2.5 보수 레이턴시 변환은 틀림(AR1 이 실제로 더 빠름).
- **근거**: QNN delegate 가 Hexagon 버전별 stub(`libQnnHtpV73Stub.so`)을 로드 — missing-file 이름에 요구 버전이 인코딩됨.
- **출처**: `docs/vision.md:1012-1013,1080-1112`; `docs/dev-guide.md:11`
- **상태**: ✅ 확정

### 3rd-party(untrusted_app) APK 는 QNN-direct 불가 — fastrpc 가 unsigned firmware 를 silent reject
- **사실**: 직접 QNN(JNI → `libQnnHtp.so` + Hexagon firmware 를 `persistentDataPath/hexagon-v73/` 에 push)은 리테일 글라스에서 silent 실패. fastrpc 커널 드라이버가 firmware 로딩에 3규칙 강제: (1) firmware 가 /system 또는 /vendor 에 있어야(app-private /data/data 거부), (2) Qualcomm/OEM 서명 필수(SDK 추출 unsigned 거부), (3) SELinux context 가 system_app/vendor_app(untrusted_app 거부). 3개 다 위반 → fastrpc silent-reject, 앱 코드에 에러 안 뜸.
- **근거**: Hexagon DSP 는 별도 보안 도메인 — fastrpc 가 path+signature+SELinux 를 검증하고 거부 시 untrusted caller 에게 에러를 안 돌려줌.
- **출처**: `docs/vision.md:1159-1208`; `docs/dev-guide.md:12,145`
- **상태**: ✅ 확정

### LiteRT + QNN Delegate 는 됨 — Qualcomm delegate 가 /vendor 서명 firmware 로의 trusted bridge
- **사실**: TFLite Interpreter + Qualcomm QNN delegate(.aar, Maven 2.47.0)는 QNN-direct 가 실패한 곳에서 성공: 앱이 firmware 를 안 들고 다니고, delegate 가 이미 서명된 시스템 `/vendor/lib64/libQnnHtp.so` 를 호출 → fastrpc 가 `/vendor/dsp/cdsp/` 의 OEM 서명 firmware 를 로드. vendor lib 이 public ABI 라 untrusted_app 가 통과 호출 가능. 이 pivot 으로 코드 ~1000줄(JNI C++) → ~30-50줄, delegate 오버헤드 ~5-10%. ⚠️ 옛 문서(`vision.md:1465`)의 "APK 가 V66/V68/V69/V73/V75 Skel libs 동봉" 은 **폐기된 QNN-direct(v0.2.4) 설계의 잔재** — 현재는 APK 가 Skel 을 명시 번들하지 **않고** `qnn-litert-delegate:2.47.0` AAR 이 transitive 로 제공(repo 내 `*Skel*.so` 0건, gradle 에 명시 jniLibs 선언 없음). 엔진은 런타임에 `setSkelLibraryDir(nativeLibraryDir)` 로 그 경로를 가리킬 뿐(최종 확인은 빌드 APK unzip).
- **근거**: Qualcomm vendor lib 이 이미 path/signature/SELinux 게이트를 통과하고 public ABI 를 노출해 untrusted 앱의 trusted bridge 역할.
- **출처**: `docs/vision.md:1203-1256,1465`; `docs/dev-guide.md:130-145`
- **상태**: ✅ 확정

### 기기 꺼짐(crash) 근본원인 = 공유 Hexagon CDSP(domain 3) 세션 leak/오염, 리부트로만 복구
- **사실**: 우리 CLIP(QNN HTP)이 RayNeo SLAM(FFVINS)과 같은 CDSP 를 써서, 종료 시 release 미동기로 CDSP user-PD 를 오염(SSR). `com.rayneo.xr.runtime` 이 `remote_handle64_invoke failed method 12 on domain 3` 를 ~30회/초 폭주 → `DeadSystemException`(system_server 사망) → 프레임워크 재시작. '리부트 후 첫 launch OK, 이후 launch 부터 crash' 시그니처. domain 3 = Compute DSP. 프로세스/앱 재시작 무효, `adb reboot` 만 복구.
- **근거**: HTP 세션이 종료 시 CDSP 를 깨끗이 release 안 해 다음 실행이 오염된 user-PD 를 만나 SSR → FastRPC stale → system_server 사망. 동시경쟁이 아니라 사후오염.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:13,17-39`; `B22_TEST_RESULTS.md:80`
- **상태**: ✅ 확정

### CLIP 을 CPU(XNNPACK)로 돌리면 CDSP SSR cascade 를 구조적으로 차단 (+125초 컴파일 소멸)
- **사실**: `useNpu=false`(`initializeCpu`)면 delegate=null(XNNPACK)이라 CDSP 미사용 → SLAM 과의 CDSP 충돌이 구조적으로 차단되고, 부수효과로 ~125초 콜드 HTP 컴파일도 사라짐(init sub-second). CLIP 은 트리거당 단발 256² 라 CPU 수십~수백 ms 로 충분. `useNpu=true` 면 HTP/NPU 가 빠르지만 SLAM 과 CDSP 를 공유해 우리 HTP 세션이 CDSP 를 SSR → XR 런타임 핸들 stale → system_server 사망(기기 재시작) 위험.
- **근거**: AR1 의 단일 Compute DSP(CDSP)를 SLAM(벤더)과 우리 TFLite HTP delegate 가 공유하는데, 우리 세션 오염이 CDSP user-PD SSR 을 남겨 OpenXR 런타임 핸들을 깨뜨림. AI 가속을 끄는 것이 안정성 fix 인 역설.
- **출처**: `spatial_anchor_test/Assets/Scripts/ClipExtractor.cs:28-32`; `.../QnnClipEngine.java:81-95,184-188`
- **상태**: ✅ 확정

### QNN HTP 첫 cold launch 그래프 컴파일이 메인스레드를 ~152초 동결 (CLIP ~125초 + OCR detector 27초)
- **사실**: TFLite Interpreter 생성(=HTP 그래프 컴파일)이 Unity 메인스레드 동기로 일어남. 캐시 없으면 CLIP HTP 125.2초(logcat: `Interpreter init: 125228.6 ms`) + EasyOCR detector 27.1초 = ~152초 동결. recognizer unroll 까지 켜면 첫 launch ~159초. 동결이 카메라 provider 를 기아시켜 SIGPIPE → SLAM 발산(595m)까지 유발. `skipOcr=true` 로 OCR init 자체 제거가 159초 컴파일 소멸.
- **근거**: HTP 컴파일이 동기 JNI 이고 DSP/스케줄러 점유로 실시간 카메라 스레드를 기아.
- **출처**: `docs/freeze-accuracy-diagnosis.md:21-33`; `B22_TEST_RESULTS.md:68`; `BUILD_OCR_SLAM_HANDOFF.md:169`
- **상태**: ✅ 확정

### QnnClipEngine.initialize 는 worker 에 submit 만 하고 즉시 true 반환 — isReady 폴링 필수
- **사실**: `initialize`/`initializeFromContextBin` 은 worker 스레드로 컴파일을 submit 만 하고 즉시 true 반환. 첫 실행 HTP 컴파일이 ~125초 걸리고 `isReady=true` 까지 0.5초 간격 폴링해야 하며, 영구 실패 무한루프 방지로 `COMPILE_TIMEOUT_SEC=180` 타임아웃. 컴파일이 worker 라 메인(렌더) 스레드는 안 멈춤. **init 의 true 는 '준비 완료'가 아니라 '작업 제출 성공'.**
- **근거**: 동기 init 이 152초 메인스레드 동결 → 카메라 provider 기아 → SIGPIPE/SLAM 발산을 일으켰던 과거 문제를 우회하려 컴파일을 single-thread ExecutorService 로 격리.
- **출처**: `spatial_anchor_test/Assets/Scripts/ClipExtractor.cs:169-193`
- **상태**: ✅ 확정

### TFLite Interpreter 는 생성·실행 동일 스레드 바인딩 필수 — single-thread executor 하나가 전담 🆕 (코드에서 구조)
- **사실**: TFLite Interpreter 는 생성(=HTP 컴파일, 첫 실행 ~125초)과 run 을 같은 스레드에서 해야 하는 thread-affinity 제약이 있어, `newSingleThreadExecutor` 하나가 init·embed·close 를 모두 전담한다. init 을 worker 로 비동기화해 메인스레드 동결 제거, ready 플래그는 worker 가 컴파일 끝에 set. `embed()` 는 submit 전 ready 가드로 컴파일 중 `.get()` 이 메인을 최대 125초 블록하는 것을 막음. `release()` 가 `ready=false` 를 shutdown 보다 선행해 `RejectedExecutionException` 경쟁 차단.
- **근거**: TFLite delegate 핸들이 생성 스레드에 바인딩되기 때문.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:56-60,235-247,284-296`
- **상태**: ✅ 확정

### ready 플래그를 volatile 로 — worker write / 메인 read happens-before 보장 🆕 (코드에서 구조)
- **사실**: `ready` 플래그는 worker 가 write 하고 메인이 `isReady()` 로 read 하므로 JMM happens-before 보장을 위해 `volatile` 선언. `mockMode`/`cacheBinPrewarmed` 는 비volatile(단일 경로).
- **근거**: 비volatile 이면 메인스레드가 캐시된 stale false 를 영원히 읽을 수 있음.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:51-54`
- **상태**: ✅ 확정

### tflite 파일 부재 시 mockMode 로 빠지면서도 ready=true/true 반환 — 실패가 성공처럼 보임 🆕 (코드에서 구조)
- **사실**: `QnnClipEngine.initInternal` 과 `QnnYoloEngine.initialize` 는 모델 파일 부재 시 `mockMode=true` 로 두고도 `ready=true` 및 true 를 반환한다(CLIP 은 hash 기반 의사 임베딩, YOLO 는 빈 결과). `initialize` 반환 true 의 의미도 '실제 준비'가 아니라 'worker submit 됨'일 뿐이라 호출 측은 `isReady()` 로만 판단해야 함.
- **근거**: 비동기 submit 모델 + mock fallback 이 둘 다 true 를 반환하도록 설계.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:71-79,128-136,256-267`
- **상태**: ⚠️ 주의

### QnnDelegate Options 의 setLogLevel/setHtpPerformanceMode(BURST)는 API-optional → try-catch 🆕 (코드에서 구조)
- **사실**: `setLogLevel(LOG_LEVEL_INFO)` 와 `setHtpPerformanceMode(HTP_PERFORMANCE_BURST)` 호출은 각각 `try{...}catch(Throwable){}` 로 감싼다(세 엔진 동일). AAR 버전에 따라 setter 가 없을 수 있어 `NoSuchMethodError` 등을 삼키려는 방어. 핵심 `setBackendType(HTP_BACKEND)`/`setSkelLibraryDir` 은 안 감쌈.
- **근거**: AAR 2.47.0 과 다른 버전 간 public API 표면 차이를 런타임에 흡수하려는 의도.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:174-175`
- **상태**: 🔬 추정

### HTP cache 디렉토리는 persistentDataPath/qnn_clip_cache 에 격리 생성 🆕 (코드에서 구조)
- **사실**: cacheDir 는 tflite 모델 파일의 부모 디렉토리(persistentDataPath) 아래 `qnn_clip_cache` 하위에 생성(예: `/storage/emulated/0/Android/data/<pkg>/cache/qnn_clip_cache/`). app cache 영역 안에 격리하는 의도.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:138-146`
- **상태**: 🔬 추정

#### HTP context cache — 디스크 영속화가 사실상 안 됨

- **사실**: qnn-litert-delegate 2.47.0 의 `setCacheDir`+`setModelToken` 조합은 디스크에 컴파일된 `.bin` 을 안 쓴다(파일은 20B/1.6KB stub 뿐, logcat `exists=false`). **device-level 휘발성 DSP 커널 캐시만** 생겨 같은 `model_token` 두 번째 launch 부터 instant 이나, process death/DSP 전원off/모델 swap/reboot 시 evict → 매 cold start 125초 재컴파일. 정정: delegate(`libQnnTFLiteDelegate.so`)는 SAVE/RESTORE MODE 직렬화를 실제로 구현하나 'Failed to create folder'/권한 또는 'Context blob too large to serialize safely' 로 SAVE MODE 실패해 stub 만 남는 것. **hit/miss 는 파일명 아닌 init latency 로만 추정**(Interpreter ctor <1s 면 hit, 실제 파일명 `qnn_binary_*` prefix) — 정확한 hit/miss API 미노출.
- **근거**: delegate SAVE MODE 가 폴더 생성 실패 또는 blob 과대로 직렬화를 포기하고 휘발성 커널 캐시로만 동작.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:171-218`; `docs/freeze-accuracy-diagnosis.md:36-40`; `docs/spatial-anchor-handoff.md:167-169`; `docs/integration_log.md:198-200`
- **상태**: ✅ 확정

- **사실**: 사전 빌드 바이너리를 cacheDir 안에 정확히 `CACHE_MODEL_TOKEN+".bin"`(=`mobileclip_s2_v73_int8_v1.bin`) 이름으로 복사해야 delegate 가 발견·deserialize 한다. cache_dir 와 model_token 둘 다 set 필요. tflite 도 여전히 필요(graph topology+tensor shape 메타데이터). 단 AI Hub `qnn_context_binary` 포맷 ≠ TFLite delegate cacheDir 포맷이라 단순 복사로는 deserialize 안 돼 `initializeFromContextBin` 경로는 **사실상 dead**. AI Hub `submit_compile_job` 은 input type 으로 tflite 를 안 받음(ONNX 경유 필요). 유일 변종 = device 가 생성한 캐시를 adb pull 해 동일 v73 기종 한정 번들. root 없이 `libQnnHtp` 직접 호출 불가.
- **근거**: AI Hub context-binary 와 LiteRT delegate cacheDir 이 별개 직렬화 포맷. delegate 가 `<cache_dir>/<model_token>.bin` 이름으로 캐시를 찾음.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:44-47,148-160`; `docs/spatial-anchor-handoff.md:169`; `docs/integration_log.md:202-204`; `docs/freeze-accuracy-diagnosis.md:37`
- **상태**: ✅ 확정

- **사실**: `CACHE_MODEL_TOKEN`(`mobileclip_s2_v73_int8_v1`)은 tflite 모델 graph hash 와 1:1 대응. 모델 파일이 바뀌면 토큰도 같이 바꿔야 stale cache 를 안 쓴다.
- **근거**: delegate 가 토큰만으로 캐시 식별하고 모델 내용 변경을 감지 못함.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnClipEngine.java:42-44`
- **상태**: ⚠️ 주의

### QNN GPU 백엔드는 INT8 미지원 → INT8 CLIP GPU 폴백 불가; Adreno 는 NPU 대비 7~20배 느림
- **사실**: `libQnnGpu.so` 동봉+`QnnDelegate` 에 `GPU_BACKEND` 있으나 QNN GPU 백엔드는 INT8 미지원(FP32/FP16 전용, `GpuPrecision` enum 에 INT8 없음). 우리 INT8 모델 GPU 전환은 FP16 재export+재번들+3-way enum 필요. git 이력상 GPU 에서 돈 건 초기 YOLO 뿐이고 ~200ms 대(NPU YOLO11l ~15-30ms 대비 7-20배 느림). → crash 회피책은 GPU 아닌 CPU(XNNPACK).
- **근거**: Qualcomm QNN GPU 백엔드 enum 에 INT8 정밀도 자체가 없음.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:50-52`
- **상태**: ✅ 확정

### MobileCLIP-S2 INT8 on v73 = 2.12ms (XR2/v69 FP16 16ms 대비 8배); 이미지 인코더 ~35.7M params
- **사실**: MobileCLIP-S2 INT8 TFLite 측정 2.12ms(QCS8550 proxy, Hexagon v73) — XR2 Gen 2/v69 FP16 QNN-binary 16.0ms 대비 8배 빠름(NPU v69→v73 + FP16→INT8 가속 두 요인). 이미지 인코더는 ~35.7M params(ViT-B-32 87M 의 ~40%, 옛 '~5M' 은 오타). ONNX 그래프에 NPU-risk op 0개(Loop/If/NMS/TopK/GridSample/Resize 없음) → 완전 NPU-placeable.
- **근거**: FastViT 백본이 conv-heavy 에 표준 transformer op(MatMul/Softmax/Erf/GELU)만 써서 전체 그래프가 INT8 가속 NPU 에 CPU 폴백 없이 매핑됨.
- **출처**: `docs/vision.md:1280-1282,1326-1352`; `docs/dev-guide.md:138,200-206`
- **상태**: ✅ 확정

### 레이턴시/배터리 예산: 설계 목표(client-spec) vs 온디바이스 실측
- **사실**: client-spec 설계 목표 — YOLO 10~20ms, CLIP 50~150ms, end-to-end **200ms** + T=0 즉시 anchor 표시로 200ms 공백 은폐; 배터리 245mAh, **SLAM 상시 구동이 주 소비원**(AI 추론은 단발 ~수십ms 라 부차적), 연속 30분+ 목표. 실측(vision.md 스냅샷): YOLO ~18-30ms, CLIP ~3ms → 추론 합계가 200ms 목표 대비 큰 여유. **배터리는 기기 실측 미완**.
- **근거**: 설계는 ViT 가 NPU 100% 배치 안 될 위험(CPU 폴백 시 300ms+)을 가정했으나 MobileCLIP-S2 INT8 이 v73 에서 폴백 없이 2.12ms 라 CLIP 예산은 크게 여유 → 실병목은 레이턴시가 아니라 **매칭 정확도/조준**.
- **출처**: `docs/client-spec.md:37,98,118-121,141-143,174-208`; `docs/vision.md` 스냅샷
- **상태**: 🔬 추정(설계 목표) / ✅ 추론 레이턴시 실측 / ⏳ 배터리 미실측

### EasyOCR recognizer(CRNN/BiLSTM)는 QNN HTP delegate 위임 거부 — detector(CRAFT)만 NPU
- **사실**: EasyOCR detector(CRAFT, conv-only)는 NPU 정상 위임되나 recognizer(CRNN)는 2단 BiLSTM 이 TFLite WHILE control-flow op(82개 LSTM 토큰)로 구현돼 QNN HTP delegate 가 거부(`Error applying delegate`). 우회 = Qualcomm 명시('Unrolled LSTM is required to run on NPU via LiteRT')대로 qai_hub export 에 `--unroll-lstm` 붙여 unroll recognizer 생성하면 NPU-only(19902/19902 layers, CPU 0). unroll 된 recognizer 는 grayscale input + CTC greedy decoder 와 호환 유지해야 함(sanity check).
- **근거**: HTP backend 가 동적 control-flow(WHILE) op 를 위임 못 하고, unroll 하면 정적 conv/matmul 로 펴짐.
- **출처**: `.../EasyOCREngine.java:143-144,183-195`; `docs/freeze-accuracy-diagnosis.md:63-65`; `docs/integration_log.md:182-189`; `BUILD_OCR_SLAM_HANDOFF.md:92-93`
- **상태**: ✅ 확정

### YOLO 엔진 헤더 주석(320/2100)과 실제 상수(640/8400) 불일치 🆕 (코드에서 구조)
- **사실**: `QnnYoloEngine` 헤더 주석은 input [1,320,320,3], output [1,2100,4] 로 적혀있으나 실제 상수는 `IN_H=IN_W=640, NUM_ANCHORS=8400`(=80²+40²+20²). `execute()` 의 input 변환도 640 을 쓰므로 헤더 주석이 stale. (배포 모델/코드는 640² — `yolo11l_640_w8a8.tflite`, `YoloDetector.INPUT_SIZE=640`. 널리 인용된 '~15-30ms' YOLO 레이턴시와 n/s/m/l/x 표는 320² XR2 Gen 2 proxy 추정이지 640 on-glass 실측 아님. `QnnYoloDetector.cs` 에도 stale '320*320' 주석 잔존. ONNX output 84=4(xywh)+80(COCO).)
- **근거**: 모델/입력 해상도 변경(320→640) 시 헤더 주석만 업데이트 안 됨. m→l 결정과 벤치마크가 320→640 스위치보다 앞섬.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnYoloEngine.java:1-9,34-35`; `docs/dev-guide.md:137,141,153,203`; `docs/vision.md:29`
- **상태**: ⚠️ 주의

### YOLO NPU 가 DFL+sigmoid+argmax 까지 처리 — 호스트는 NMS 만; flat layout 14700 🆕 (코드에서 구조)
- **사실**: qai_hub_models YOLOv11-Det 그래프는 NPU 에서 DFL+sigmoid+argmax 까지 전부 수행, C# 은 threshold+NMS 만. 반환 flat 배열 layout `[boxes(8400*4) | scores(8400) | class_idx(8400)]`. scores 는 양자화 scale=1/256 으로 [0,1] dequant, class_idx 는 uint8 raw 가 그대로 0~79 COCO index. (Java engine 측 32 length = `NUM_ANCHORS*4 + NUM_ANCHORS + NUM_ANCHORS`. 320 변종은 2100*4+2100+2100=14700, box 는 corner (x1,y1,x2,y2)라 center 변환 필요.)
- **근거**: qai_hub_models 가 export 시 후처리(DFL/sigmoid/argmax)를 그래프에 포함. corner 좌표라 변환 단계가 모델-특정.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnYoloEngine.java:1-9,150-152,224-241`; `QnnYoloDetector.cs:274-322`
- **상태**: ✅ 확정

### 양자화/비양자화 YOLO 를 input dtype byteSize 로 자동 감지, output 을 shape/name 으로 추론 🆕 (코드에서 구조)
- **사실**: `isQuantized` 는 input tensor `dataType().byteSize()==1`(uint8)이면 true. output 3개(boxes [1,8400,4], scores [1,8400], class_idx [1,8400])는 shape 로 분류하되 scores 와 class_idx 는 shape 가 같아 구분 불가 → tensor name 에 "class" 포함 여부 또는 `(scale==0 && quantized)` 로 class_idx 판정. float 모델이라도 class_idx output 은 uint8 일 수 있어 output 마다 개별 byteSize 로 dequant 분기.
- **근거**: qai_hub_models YOLO 그래프가 argmax 결과를 정수 dtype 으로 따로 내보냄.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnYoloEngine.java:99-133,193-200`
- **상태**: ✅ 확정

### YOLO 입력 = NHWC uint8 307200 bytes, Option C 로 Java direct buffer native 주소에 Marshal.Copy 직접 write 🆕 (코드에서 구조)
- **사실**: 양자화 모델 입력 `inputBytes = 320*320*3 = 307200`(320 라인) / `640*640*3 = 1,228,800`(640 라인) NHWC uint8. Java `allocateInputBuffer()` 가 direct ByteBuffer 의 native address(long)를 reflection 으로 private `java.nio.Buffer.address` 를 `setAccessible(true)` 해 읽어 C# 에 반환 → C# 이 `reusableInputPtr` 로 받아 `Marshal.Copy(...)` 로 직접 써넣고 `executeReusable()` 호출 → 매 프레임 JNI byte[] marshal 비용 제거. addr==0/예외면 `executeBytes(inputBytes)` 폴백. 주석: 'Android 9+ hidden API warning 가능'.
- **근거**: JNI 로 큰 배열을 매 프레임 넘기면 marshaling 사본이 생기므로, Java direct buffer 의 raw 포인터를 한 번 받아 그 위에 덮어써 copy 1회로 줄임.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/QnnYoloEngine.java:45-67,333-337`; `QnnYoloDetector.cs:62-64,144-162,197-206`
- **상태**: ⚠️ 주의 (hidden API 정책 위반 소지)

### EasyOCR recognizer 출력은 CTC logits [1,199,97], class 0 = blank, charset.length+1==97 🆕 (코드에서 구조)
- **사실**: recognizer 출력 [1,T=199,classes=97] 에서 class index 0 은 CTC blank, 실제 문자는 `charIdx=best-1`. charset 파일은 `\r\n` 제거 후 로드, 길이 96 이어야(blank 미포함) `charset.length+1==REC_CLASSES(97)` sanity check 통과. CTC greedy decode = timestep argmax 후 blank(0) 제거 + 연속 중복 제거.
- **근거**: CTC 디코더가 index 0 을 blank 로 예약.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/EasyOCREngine.java:48-50,90-92,155-158,340-356`
- **상태**: ✅ 확정

### EasyOCR recognizer(easyocr_recognizer_unroll_qcs8450.tflite)는 unroll 변형 + 타겟 칩 인코딩 🆕 (코드에서 구조)
- **사실**: OCR recognizer 파일명이 `easyocr_recognizer_unroll_qcs8450.tflite` — RNN 을 unroll 한 변형이고 파일명에 타겟 칩(qcs8450)이 박혀있음. detector=`easyocr_detector.tflite`, charset=`easyocr_charset.txt`. EasyOCR(Qualcomm AI Hub w8a8 + QNN Delegate, Hexagon v73)이 MLKitOCR(CPU ~10s) 대체.
- **근거**: QNN HTP 가 동적 RNN loop 를 잘 못 다뤄 시퀀스를 정적 unroll 한 모델 필요, AI Hub 컴파일이 qcs8450 칩 프로파일을 타겟으로 export. swap 시 호환성 함정.
- **출처**: `spatial_anchor_test/Assets/Scripts/OCRExtractor.cs:15-16`
- **상태**: ✅ 확정

#### NPU 위임 호환성 요약

| 모델 / 컴포넌트 | NPU 위임 | 제약 |
|---|---|---|
| MobileCLIP-S2 INT8 (이미지 인코더) | ✅ 완전 위임 | NPU-risk op 0개, 2.12ms. **w8a16 은 SQRT/DIV 미지원으로 실패**(int8 만) |
| YOLOv11-Det w8a8 | ✅ 위임 | DFL/sigmoid/argmax 까지 그래프에 흡수, 호스트는 NMS만. 글라스 시점 conf 붕괴라 현재 미사용(clip-only) |
| float 모델 (양자화 안 한) | ❌ ~5.5% 만 | FP16 유닛 없어 float op CPU 폴백 → w8a8 필수 |
| EasyOCR detector (CRAFT) | ✅ 위임 | conv-only |
| EasyOCR recognizer (CRNN/BiLSTM) | ❌ 거부 | WHILE control-flow op → `--unroll-lstm` 로 unroll 해야 NPU(19902/19902 layers) |
| QNN GPU 백엔드 (INT8) | ❌ | GPU 백엔드 INT8 미지원(FP32/FP16 전용). Adreno 는 NPU 대비 7~20배 느림 |
| QNN-direct (untrusted_app) | ❌ silent reject | fastrpc path/signature/SELinux 게이트. LiteRT+QNN delegate 만 합법 |

---

## 🖥️ OpenXR · 렌더

### Additive MicroLED see-through: BLACK = TRANSPARENT, 밝은(emissive) 픽셀만 렌더
- **사실**: RayNeo X3 Pro MicroLED 웨이브가이드는 additive 라 검은 픽셀 = 투명 = 현실 비침, 밝은(emissive) 픽셀만 보임. 어두운 패널/텍스트는 안 보임 → checkout 패널은 dark fill 금지, 밝은 frame 라인·글자·BUY fill 만. PBR/Standard 셰이더는 검게 나오므로 Unlit/Texture·TextMesh(GUI/Text)만. 그림자 없음, GLES3, Single Pass Instanced. 페이드아웃도 alpha 가 아니라 색을 black 으로 lerp(밝기가 곧 가시성). 'true AR' 모드 = 검은 배경에 오버레이 UI 만 합성(카메라 pass-through 는 투명성 낭비+레이턴시).
- **근거**: 웨이브가이드 combiner 가 emitted light 를 incoming 현실 빛에 더함 → 검은 픽셀 = 빛 추가 없음 = 현실 그대로 비침.
- **출처**: `AdCheckout.cs:13-18,280-288`; `docs/vision.md:1985-1995,2274-2277`
- **상태**: ✅ 확정

### 그래픽 API 는 OpenGLES3 단독 강제 (Vulkan 금지)
- **사실**: `SetGraphicsAPIs(Android, {OpenGLES3})`, `SetUseDefaultGraphicsAPIs=false`. `RayNeoSupportFeature` validation 이 `graphics[0]==OpenGLES3` 를 error 룰로 강제(주석:'opengl 3 限制. 今后有vulkan 再取消'), Vulkan first 면 RayNeo runtime 이 surface 를 못 잡아 fail(Discord 보고 일치). minSdk/targetSdk≥30 강제, defaultInterfaceOrientation=LandscapeLeft 권장(warning). ProjectSettings 도 `m_APIs=11(OpenGLES3)` 단일 + `m_Automatic=0`(수동 고정). 최신 AR 칩인데 Vulkan 불가.
- **근거**: RayNeo OpenXR 런타임이 OpenGL ES 컨텍스트만 지원, Vulkan surface 미지원.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:272-281`; `RayNeoSupportFeature.cs:105-187`; `ProjectSettings/ProjectSettings.asset:389-392`; `docs/findings-2026-06-11-crash-slam-openxr.md:103`
- **상태**: ✅ 확정

### 진짜 고정 HUD 불가 — RayNeo 런타임에 VIEW-space composition layer 경로 없음 + ATW 가 흉내 HUD 까지 워프(swimming)
- **사실**: VIEW-space 컴포지션 레이어 미제공이라 진짜 head-fixed HUD 불가. TextMesh 를 카메라 parent 해 head-lock 흉내내도 ATW(reprojection)가 그 HUD 까지 SLAM pose 로 워프해 흔들림(swimming). composition layers 는 Unity 6 전용 패키지인데 RayNeo 런타임이 앱 제출 secondary layer 자체를 안 받음. 유일 진짜 경로 = Unity 2022 커스텀 OpenXRFeature(`xrEndFrame` VIEW-space quad)지만 런타임 secondary layer 미지원으로 사실상 막힘.
- **근거**: RayNeo 컴포지터가 앱 제출 composition layer 미지원 + ATW 가 모든 콘텐츠를 head pose 로 reproject.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:25-28`; `docs/findings-2026-06-11-crash-slam-openxr.md:106,113-115`; `docs/spatial-anchor-handoff.md:232-233`
- **상태**: ✅ 확정

### RayNeo 컴포지터 ATW 는 회전 전용 (depth 미제출, spacewarp 경로 없음) → translation judder
- **사실**: `depthSubmissionMode:0` 이고 RayNeo 컴포지터 .so 5개+classes.jar 에 spacewarp/motionvector/reproject/depth 토큰 0건. 따라서 ATW 는 회전만 리워프하고 translation(병진)은 8Hz 그대로 끊김. depth 제출 시도는 무효 + fps 손실 + 컴포지터 죽을 위험이라 **금지**. 0.5~1.2m 코앞 quad 는 parallax 최대라 judder 최대 가시.
- **근거**: 컴포지터에 positional spacewarp 코드 경로가 없어 depth 를 줘도 재투영 불가.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:69-73,81`
- **상태**: ✅ 확정

### ATWSupport=1 이 b15 에서 CDSP crash 를 '가끔'→'거의 매번'으로 증폭
- **사실**: b14→b16 에서 바뀐 유일한 XR 런타임 변수 = b15 의 `ATWSupport 0→1`(OpenXR Package Settings.asset:670-671). ATW=1 은 컴포지터가 매 스캔아웃마다 SLAM pose 로 reproject → 같은 CDSP FastRPC 를 상시 실시간-마감으로 올림 → 예전엔 흡수되던 CDSP hiccup 이 dead-handle 로 직결. 또 ATW(회전전용)는 head-locked HUD 까지 워프(swimming). CPU 전환으로 CDSP 가 비면 ATW=1 유지해도 안전. (단일 boolean 토글이 crash 인과·HUD swimming·8Hz judder 보간을 동시 좌우.)
- **근거**: ATW 의 상시 실시간 reproject 가 SLAM 과 공유하는 CDSP FastRPC 를 상시 마감압박 상태로 만들어 hiccup 이 치명화. `depthSubmissionMode:0` + spacewarp/positional 경로 없음 → translation 8Hz, 회전만 워프.
- **출처**: `spatial_anchor_test/Assets/XR/Settings/OpenXR Package Settings.asset:670-671`; `docs/findings-2026-06-11-crash-slam-openxr.md:43-44,48`
- **상태**: 🔬 추정 (b15 단일 변수 추적 기반)

### Unity 6 빌드는 검은화면 — RayNeo ARDK 가 GameActivity 의 setFrameLayout(UnityPlayer) 계약 의존
- **사실**: Unity 6(6000.x)로 빌드하면 안경에서 Unity 로고도 안 뜨는 검은화면. RayNeo ARDK 1.1.2 가 Unity 6 비호환(GameActivity 기본이 `setFrameLayout(UnityPlayer)` 계약 제거) → 'Need to set FrameLayout in advance!' → shutdown 으로 SLAM/렌더 부팅 실패. Unity 6 로 열면 ProjectVersion.txt+일부 settings 도 6000 으로 오염(git checkout 복구). **반드시 2022.3.62f3.**
- **근거**: Unity 6 가 GameActivity 로 전환하며 ARDK 가 의존하던 `UnityPlayer.setFrameLayout` 진입점을 제거.
- **출처**: `docs/findings-2026-06-11-crash-slam-openxr.md:127-129`; `B25_DEMO_HANDOFF.md:63-64`; `B22_TEST_RESULTS.md:77`; `BUILD_OCR_SLAM_HANDOFF.md:26,170`; `docs/spatial-anchor-handoff.md:59-66`
- **상태**: ✅ 확정

### 런처 액티비티는 RayNeo UnityOpenXrActivity 여야 — super.onCreate 체인이 OpenXR init 🆕 (코드에서 구조)
- **사실**: main launcher 액티비티 = `com.rayneo.openxradapter.UnityOpenXrActivity`(skiing 게임 패턴). `UnityOpenXrActivity extends RayNeoUnityPlayerActivity extends OpenXRActivity` → super.onCreate 체인으로 `OpenXRActivity.Initialize()` 가 `mClient.bindService(MonitorService)` + `LauncherIPCManager.initIPC()` 를 호출해야 native loader 의 `EstablishServiceConnection` 성공 + `xrGetInstanceProcAddr` 정상. Unity 가 자동 추가하는 `com.unity3d.player.UnityPlayerActivity` 는 `tools:node=remove` 로 제거.
- **근거**: RayNeo OpenXR 런타임 init 이 액티비티 onCreate 체인에 묶여 있어, 표준 Unity 액티비티로 띄우면 서비스 바인딩/IPC 가 안 되어 OpenXR init 안 됨.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/AndroidManifest.xml:2-8,38-60`
- **상태**: ✅ 확정

### boot.config 에 xrsdk-pre-init-library + gfx-disable-mt-rendering=1 + libUnityOpenXR.so 강제 주입
- **사실**: 빌드 hook `RayNeoBootConfigPatcher`(`IPostGenerateGradleAndroidProject`, callbackOrder=999)가 gradle 생성 후 boot.config 에 `xrsdk-pre-init-library=UnityOpenXR` + `gfx-disable-mt-rendering=1` 를 없으면 추가하고, PackageCache 의 `com.unity.xr.openxr@*/Runtime/android/arm64/libUnityOpenXR.so` 를 `jniLibs/arm64-v8a` 로 강제 복사. 우리 빌드는 loader gate 때문에 `xrsdk-pre-init-library` 를 안 내보내 splash 시점 OpenXR init 실패 → view/head 트래킹 미수립 → centerEye=0 → head-locked. vendor 작동 APK 와 일치시킴.
- **근거**: OpenXR build hook 이 우리 로더를 비활성으로 오판해 native lib 와 pre-init 키 누락 → 부팅 시 OpenXR pre-init 안 됨. boot.config 의 pre-init 키가 이 .so 를 로드. single-thread 렌더(`gfx-disable-mt-rendering=1`)도 요구.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:300-357`; `docs/spatial-anchor-handoff.md:82-85`
- **상태**: ✅ 확정

### Android XR 로더가 진짜 OpenXRLoader 여야 — dangling/wrong 로더면 pre-init lib 누락→6DoF 죽음
- **사실**: `EnsureOpenXRLoader` 가 `activeLoaders` 에 `UnityEngine.XR.OpenXR.OpenXRLoader` 가 없으면 기존(잘못된/dangling NULL) 로더를 전부 제거 후 `OpenXRLoader` 재할당(커밋 167380d). 안 되면 OpenXR build hook 이 `libUnityOpenXR.so` + xrsdk-pre-init-library 를 자동 포함 안 해 6DoF 죽음. 로더 타입을 BEFORE/AFTER 로그로 찍어 dangling 진단.
- **근거**: OpenXR build hook 이 등록된 로더 타입을 보고 pre-init native lib 포함 여부 결정. dangling/다른 타입이면 native lib 미포함.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:153-199`; `docs/spatial-anchor-handoff.md:82-85`
- **상태**: ✅ 확정

### batchmode 는 RayNeoGeneralSettings 를 PreloadedAssets 에 수동 주입해야 SLAM pose 가 산다
- **사실**: `EnsureRayNeoSettingsPreloaded` 가 `Assets/XR/RayNeoGeneralSettings.asset` 을 PlayerSettings PreloadedAssets 에 주입(ProjectSettings preloadedAssets[0] GUID=df1171da065c5cc4cabced796114f258). 없으면 런타임 `[RuntimeInitializeOnLoadMethod] XRSDK.Initialize()` 가 `RayNeoXRGeneralSettings.Instance` 가 null → NullReferenceException → RayNeo pose 파이프라인 미초기화 → HeadTrackedPoseDriver 가 SLAM pose 못 받아 카메라 transform 정지(camPos=0,0,0) → head-locked, 6DoF 죽음. 정상 빌드는 RayNeo GUI 의 `ConfigPreloadInfo()` 가 주입하나 batchmode 는 GUI 미실행이라 직접 주입 필요. Unity merge 시 자주 회귀.
- **근거**: RayNeo settings singleton 이 PreloadedAssets 로 메모리에 올라와야 RuntimeInitializeOnLoad 초기화가 인스턴스를 찾음.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:126-151`; `ProjectSettings/ProjectSettings.asset:145-146`
- **상태**: ✅ 확정

### OpenXR stereo 에서 IMGUI OnGUI screen-overlay HUD 는 안경에 안 보임
- **사실**: v1 HUD 를 OnGUI IMGUI screen-overlay(stereoHud 좌/우 분할)로 그렸으나 디바이스에서 invisible — Unity OpenXR stereo 모드의 알려진 IMGUI screen-overlay limitation. 우회 = world-space TextMesh(quad 위 0.6m, 매 frame billboard 회전)로 대체하면 stereo display 자동 호환.
- **근거**: IMGUI screen-overlay 가 OpenXR single-pass stereo 컴포지션 경로에 합성되지 않음.
- **출처**: `spatial_anchor_test/JOURNEY.md:63-67`
- **상태**: ✅ 확정

### SBS stereo 에선 OnGUI 한 번 그리면 한쪽 눈만 — AdRenderer 가 눈별로 따로 그려야 함
- **사실**: 글라스는 side-by-side(SBS) stereo. Unity OnGUI 는 전체 화면 한 번 그려 좌/우 눈 불일치. 수정 = `AdRenderer.DrawAdInEye` 가 각 눈 화면-절반 영역에 따로 그림. 화면 자동회전도 잠금: AndroidManifest landscape + 4 PlayerSettings rotation flags(v0.5.7) — `screenOrientation fullSensor` 면 기운 글라스가 디스플레이를 회전.
- **근거**: SBS 가 하나의 framebuffer 를 좌/우 절반으로 나눠 눈마다 렌더 → 단일 full-screen draw 는 양쪽에 걸쳐 잘못 들어감.
- **출처**: `docs/dev-guide.md:217-218`; `docs/progress-log.md:495-496`; `docs/vision.md:23`
- **상태**: ✅ 확정

### 양쪽 눈을 같은 좌표(disparity=0)로 그리면 HUD 가 광학 무한대 → 0.3-0.5m 객체에서 복시
- **사실**: AdRenderer 가 양쪽 눈을 동일 좌표(disparity=0, `AdRenderer.cs:252`)로 그려 가상 카드가 광학 무한대에 위치. 실제 객체(콜라병)는 0.3-0.5m 라 거기 수렴하면 HUD 가 융합 안 됨(복시). vergence(수평 disparity)는 SW 교정 가능, accommodation(초점)은 웨이브가이드 단일 focal plane(varifocal 없음)이라 HW 고정 → 잔여 VAC. 완화안: per-eye nasal 픽셀 시프트로 카드를 ~1.0-1.5m 에 수렴.
- **근거**: 동일 좌/우 좌표 = zero binocular parallax = 무한 perceived depth. 웨이브가이드가 단일 focal plane 이라 accommodation 은 SW 조정 불가(VAC).
- **출처**: `docs/dev-guide.md:241-262`
- **상태**: 🔬 추정

### head-locked 2D HUD 는 카메라 parent + localRotation(0,180,0) 거울상 교정 필요 🆕 (코드에서 구조)
- **사실**: HUD 를 world 고정하면 SLAM converge 후 시선 돌릴 때 드리프트해 시야 밖으로 나가던 문제로 HUD 를 카메라에 `SetParent(.,false)` 로 head-lock. 카메라 정면을 향하되 텍스트가 정상으로 읽히게 `localRotation` 기본 (0,180,0)을 줘 거울상 교정, `hudMirror` 토글로 on-device 에서 정상 확인. (단, 위 'ATW swimming' 항목대로 진짜 head-lock 은 아님.)
- **근거**: parent 후 facing 방향과 stereo 렌더 좌표계 상호작용으로 텍스트 좌우 반전.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:25-38, 250-258`
- **상태**: ✅ 확정

### IL2CPP 빌드가 Shader.Find 대상 셰이더를 stripping → null → ArgumentNullException 🆕 (코드에서 구조)
- **사실**: 빌드에서 `Unlit/Color`·`Unlit/Texture`·`GUI/Text` 셰이더가 stripped 됨 → 런타임 `Shader.Find` 가 null 반환 → material override 시 throw. `GraphicsSettings.AlwaysIncludedShaders` 에 `Sprites/Default`(빌트인 ID 10770)만 보장되므로 solid-color/halo 는 Sprites/Default 1순위(`material.color` tint, vertex-color 경로라 build-safe). 폴백 체인: solid → `Sprites/Default → Unlit/Color → Unlit/Texture → Standard`, 텍스처용 `Unlit/Texture → Sprites/Default → Standard`. TextMesh HUD 는 default material 유지(override 제거).
- **근거**: IL2CPP/빌드 셰이더 stripping 이 씬에서 직접 참조 안 된 빌트인 셰이더를 제거.
- **출처**: `AdCheckout.cs:368-381`; `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:268-271,289-298`; `spatial_anchor_test/JOURNEY.md:76-79`
- **상태**: ⚠️ 주의

### EmptyScene 로 시작해 ARDK 'XR Plugin' prefab 을 Resources.Load 로 instantiate 🆕 (코드에서 구조)
- **사실**: `EnsureScene` 이 `DefaultGameObjects` 가 아닌 EmptyScene 으로 시작(기본 Camera 가 ARDK rig 와 충돌). ARDK 표준 XR rig 는 `Resources.Load<GameObject>("Prefab/XR Plugin")`(실경로 `SDK/Runtime/Resources/Prefab/XR Plugin.prefab`, 공백 포함 비표준 경로). 이 prefab 안에 CameraOffset/Head(MainCamera)+HeadTrackedPoseDriver+EventSystem 포함, OpenXR loader 활성 시 Head 의 단일 Camera 가 양쪽 눈에 자동 stereo 렌더.
- **근거**: 단일 카메라가 OpenXR stereo 로 양안 렌더되므로 추가 카메라가 있으면 충돌.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:88-117`
- **상태**: ✅ 확정

### world-anchored 광고 quad 는 spawn 1회 고정 — head re-follow 안 함 (billboard X) 🆕 (코드에서 구조)
- **사실**: `ShowAdBesideMatch` 는 6DoF 가 카메라를 구동하므로 광고 quad 를 spawn 순간 world 좌표에 한 번만 고정하고 head re-follow 안 함(진짜 world-anchored). 회전은 spawn 시점 `Quaternion.LookRotation(-camFwd, camUp)` 1회. 정면 배치라 거울상 아님 → `adMirrorX` 기본 false(이전 옆배치 땐 true 가 맞았으나 정면 전환으로 과교정됐었음). AdCheckout 패널(checkout/confirmed/glow/dwellRing)도 동일하게 spawn 1회 고정하되 adQuad 자식이 아니라 top-level 이라 FIFO 제거/Destroy 시 OnDestroy/DismissAll 에서 함께 정리해야 orphan leak 방지.
- **근거**: `LookRotation(-camFwd)` 로 quad 가 사용자를 향하면 normal 방향에 따라 텍스처 좌우 반전 여부가 결정됨. halo 가 target 자식이면 target localScale 에 곱해져 왜곡 → top-level 독립 배치 + 수동 lifecycle.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:305-322, 21-23`; `AdCheckout.cs:21-23,99-108,310-311`
- **상태**: ✅ 확정

### per-quad RenderTexture 는 GC 안 됨 — FIFO eviction/OnDestroy 에서 명시 Release() 필수 🆕 (코드에서 구조)
- **사실**: 각 광고 quad 가 자기 RenderTexture 를 소유(`adQuads` 와 1:1 정렬된 `adRTs`). RenderTexture 는 GC 안 되므로 max-2 FIFO eviction 과 OnDestroy 에서 명시적 `Release()` 해야 leak 안 남. VideoPlayer 는 quad GameObject 에 직접 `AddComponent` 해 quad Destroy 시 함께 사라지게 함.
- **근거**: RenderTexture 는 native GPU 리소스 핸들이라 managed GC 가 회수 못 함.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:56-60, 203-219, 386-404`
- **상태**: ✅ 확정

---

## 🔐 권한

### Android 11+ vendor namespace .so 12개를 manifest 에 명시 안 하면 CLIP NPU init 실패→광고 안 뜸
- **사실**: AndroidManifest 에 `<uses-native-library>` 12개(`libcdsprpc.so, libadsprpc.so, libhidlbase.so, libhardware.so, libutils.so, libcutils.so, libc++.so, libbase.so, libprocessgroup.so, libhwbinder.so, libbinder_ndk.so, libandroid_runtime_lazy.so`)를 모두 `required=false` 로 선언. 누락 시 `libQnnHtpV73Stub.so` 가 `libcdsprpc.so` dlopen 실패 → QnnClipEngine init 실패 → CLIP NPU 불가 → 광고 spawn 안 됨. 목록은 helloar reference APK 에서 추출. (이 명시가 manifest 에 libcdsprpc.so 를 박아 둔 것이 CDSP crash 추적 단서도 됨.)
- **근거**: Android 11+ 정책: vendor namespace .so 는 앱이 명시적으로 선언해야 dlopen 가능. fastrpc/HTP 체인이 이 라이브러리들을 dlopen.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/AndroidManifest.xml:21-35`; `docs/integration_log.md:117`; `docs/findings-2026-06-11-crash-slam-openxr.md:32`
- **상태**: ✅ 확정

### Unity RequestUserAuthorization 카메라 다이얼로그가 stereo 에서 invisible → 앱 hang → adb pm grant
- **사실**: Unity 의 `RequestUserAuthorization` 카메라 권한 다이얼로그는 글라스 stereo 디스플레이에 안 보여 사용자가 영영 못 닫는 다이얼로그를 기다리며 수분(~5min) hang. 수정 = `UnityEngine.Android.Permission` API + `adb shell pm grant com.eagleeye.helloar android.permission.CAMERA`(빌드 스크립트 자동 grant). CameraPreview 는 보통 ADB pre-grant 전제, 미허용이면 RequestUserPermission 후 3초 deadline polling, 끝내 거부면 'adb shell pm grant ... CAMERA' 안내 후 yield break.
- **근거**: RequestUserAuthorization 이 2D 시스템 다이얼로그를 stereo 웨이브가이드 컴포지터가 present 안 해 사용자가 dismiss 못 함 → 코루틴 블록. AR 글라스에 일반 권한 UX 부적합 → 빌드 스크립트가 미리 grant.
- **출처**: `docs/dev-guide.md:216`; `docs/progress-log.md:494`; `spatial_anchor_test/Assets/Scripts/CameraPreview.cs:73-96`
- **상태**: ⚠️ 주의

### XR 앱은 착용(XR 세션 FOCUSED) 상태에서만 렌더 루프 — adb wake 만으론 측정 불가
- **사실**: XR 앱은 착용(XR 세션 FOCUSED)일 때만 렌더 루프가 돈다. `adb shell input keyevent KEYCODE_WAKEUP` 만으로는 `mCurrentFocus=null` 이라 startup/freeze 측정 불가 → 반드시 착용 상태로 측정. 슬립이면 앱이 surface 못 얻어 멈춤 → 실행 전 `KEYCODE_WAKEUP` + `svc power stayon true`(USB 중 화면 유지) 필요.
- **근거**: XR 컴포지터가 세션 FOCUSED(착용)일 때만 프레임을 펌프.
- **출처**: `docs/spatial-anchor-handoff.md:187`; `docs/freeze-accuracy-diagnosis.md:94`; `BUILD_OCR_SLAM_HANDOFF.md:122-124`
- **상태**: ⚠️ 주의

### Android 13+(SDK 33) dynamic receiver 는 RECEIVER_EXPORTED 명시 필수 — adb broadcast 는 외부 uid
- **사실**: `RecordingReceiver.register()` 는 `SDK_INT >= 33` 이면 `registerReceiver(..., Context.RECEIVER_EXPORTED)` 로, 미만이면 플래그 없이 등록. `adb shell am broadcast` 는 외부 uid 라 exported 아니면 수신 불가. dynamic receiver 라 앱 foreground 일 때만 broadcast 수신.
- **근거**: Android 13 의 runtime-registered receiver export 명시 의무화 정책.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/RecordingReceiver.java:18-42`
- **상태**: ✅ 확정

### XR 세션 없는 scene 에서 getOrientation 호출 시 native SIGSEGV (C# try/catch 못 막음)
- **사실**: `rotationOverride` 가 설정돼 있으면(>=0) `XRCameraHelper.getOrientation("0")` 호출 자체를 건너뛴다. XR/카메라 세션이 없는 scene(ColdStartProbeScene self-test, static OCR 이미지 등)에서 getOrientation 은 `libRayNeoXRApiLayerClient(XRWarp_getOrientation)` 에서 null deref → SIGSEGV 로 프로세스 사망, C# try/catch 로 못 막음. 권한·null 체크가 아니라 '특정 native 호출을 아예 우회'가 유일 방어.
- **근거**: RayNeo XR API layer 의 getOrientation 이 활성 XR 세션 internal state 를 전제로 작성돼 세션 없으면 native 포인터 역참조. native crash 라 managed handler 통과 못 함.
- **출처**: `spatial_anchor_test/Assets/Scripts/OCRExtractor.cs:225-239`; `docs/integration_log.md:116`; `BUILD_OCR_SLAM_HANDOFF.md:180`
- **상태**: ⚠️ 주의

### adb 디버그 훅 eyad_debug.txt — 트리거/CLIP/색 우회하고 경쟁사 영상 직접 spawn 🆕 (코드에서 구조)
- **사실**: `persistentDataPath/eyad_debug.txt`(=`/sdcard/Android/data/com.eagleeye.helloar/files/eyad_debug.txt`)를 ~0.5s 마다 폴링. adb 로 brand 토큰(coca-cola/coke/cola/pepsi)을 쓰면 트리거·CLIP·색 판별·재트리거 게이트를 전부 우회하고 합성 MatchResult 로 경쟁사 영상을 바로 spawn(착용 없이 테스트). 읽은 즉시 파일 삭제해 1회 발화. mid-write 부분 텍스트/파일 lock 은 trim/예외처리로 다음 폴링 재시도.
- **근거**: 글라스 착용·조준 없이 렌더 경로를 검증하려고 외부 adb 명령을 파일 한 줄로 주입.
- **출처**: `HelloAR.cs:179-183,422-502`
- **상태**: ✅ 확정 (테스트용 백도어)

### Background Daemon (Foreground Service on RayNeo AIOS)은 비즈니스 모델의 미검증 hard gate
- **사실**: F6 Background Daemon — 24/7 Android Foreground Service(RayNeo AIOS, Android 12 기반) — 가 'hard gate': always-on 못 돌면 비즈니스 모델 붕괴. 미검증 항목: AIOS Foreground Service 정책, battery-optimization 면제, persistent-notification 요구, 글라스 sleep mode 중 IMU polling 생존 여부. Android 12 BG 제약의 글라스 적용성 미상.
- **출처**: `docs/vision.md:828,921-924,993-1001`
- **상태**: 🔬 추정

---

## 📦 SDK · 런타임 버전

### RayNeo SDK 1.1.6 ↔ 기기 Runtime 1.1.7.9 minor mismatch 가 6DoF 를 0Hz 로 죽일 수 있음
- **사실**: RayNeoXR 로그: SDK 1.1.6.0 vs Runtime 1.1.7.9, 'minor version ... does not match' + 'TIMEOUT when wait for service connection callback'. handshake 실패 시 `GetHeadTrackerStatus` 영구 0, `xrCam.position` 영구 (0,0,0), -30001 frame pipeline fail → 광고가 head-locked HUD 처럼 보임. standalone 빌드도 동일 재현 → 우리 코드 무관, system service level. 정통 해법 = ARDK 1.1.7+ flash(portal 계정). 단 b22 실측은 FFVINS 가 동작해 비치명이었음(케이스 변동).
- **근거**: 번들 ARDK 와 기기 펌웨어 런타임의 minor 버전 불일치로 service connection callback timeout.
- **출처**: `docs/integration_log.md:150-178`; `docs/spatial-anchor-handoff.md:190`; `B25_DEMO_HANDOFF.md:84`; `docs/findings-2026-06-11-crash-slam-openxr.md:88`
- **상태**: ✅ 확정

### 네이티브 라이브러리 = libRayNeoXRUnityInterfaces.so, 알고리즘 = FFalconXR_algorithm
- **사실**: `XRConstants.XRInterfaces="RayNeoXRUnityInterfaces"`(모든 RayNeoApi_* DllImport 대상), `XRFaceDetector="FFalconXR_algorithm"`. 카메라 리스트 최대 5개(`XR_CAMERA_LIST_MAX_LEN`), 이름 최대 64자. Java support 패키지 루트 = `com.rayneo.openxradapter`, support 는 `.support` 하위. 라이브러리/패키지명에 RayNeo 와 옛 사명 FFalcon 혼재(FFalcon=TCL 출신).
- **출처**: `.../public/core/XRConstants.cs:5-12`; `.../APIs/PlatformAndroid.cs:12-15`
- **상태**: ✅ 확정

### 공유 ADB 서버 / adb 버전 충돌 함정 — kill-server 는 다른 세션 공유, v40/v41 공존 시 디바이스 끊김
- **사실**: adb 는 안경팀 공유 환경이라 install/push/force-stop 등 writing 은 사전 허락, 무선 ADB 끊지 말 것. `adb kill-server && adb start-server` 는 공유 서버라 다른 세션에 영향. 머신에 다른 adb(v40/v41) 공존 시 버전 충돌로 디바이스가 끊겼다 재연결 → `adb devices` 로 시리얼 재확인. 실행 activity = `com.rayneo.openxradapter.UnityOpenXrActivity`.
- **근거**: 단일 adb 데몬을 여러 사용자가 공유하고 클라이언트/서버 프로토콜 버전 불일치가 재핸드셰이크 유발.
- **출처**: `docs/spatial-anchor-handoff.md:103,186`; `BUILD_OCR_SLAM_HANDOFF.md:174`; `B25_DEMO_HANDOFF.md:109`
- **상태**: ⚠️ 주의

### StreamingAssets mp4 는 jar:file://...!/assets/ 라 VideoPlayer 직접 재생 불가 → persistentDataPath 복사 후 file://
- **사실**: Android 에선 StreamingAssets mp4 가 APK 내부(`jar:file://...!/assets/...`)라 VideoPlayer 가 재생 못 함(과거 'spawn 직후 크래시'). `UnityWebRequest.Get(jar URL)` 로 바이트를 읽어 persistentDataPath 로 `WriteAllBytes` 후 `file://` URL 로 재생(SetupAdVideo, tflite 복사 패턴 미러). 복사 결과 캐시 재사용, per-quad VideoPlayer. `errorReceived`/try-catch 로 코덱 에러 시 정지 PNG 폴백(크래시 금지).
- **근거**: VideoPlayer 가 압축 jar 안의 에셋을 직접 디코드 못 하고 file:// 실경로를 요구.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:362-365, 406-463`; `B25_DEMO_HANDOFF.md:60-61`; `B22_TEST_RESULTS.md:69`
- **상태**: ✅ 확정

### AdRenderer 영상 광고는 streamingAssetsPath 경로를 그대로 VideoPlayer.url 에 줌 🆕 (코드에서 구조)
- **사실**: `AdRenderer` 영상 광고는 `StreamingAssetsPath/db/ads_video/*.mp4` 경로를 그대로 `VideoPlayer.url` 에 할당(주석: 'Android 도 그대로 OK', UnityWebRequest 로더와 비대칭). Prepare 에 3초 deadline 폴링, timeout 시 포기. RenderTexture 는 영상 dimension 을 안 뒤 lazy alloc(ARGB32). activeSec=10초는 mp4 길이에 맞춤(이미지 광고 기본 5초).
- **근거**: VideoPlayer 가 StreamingAssets 경로를 플랫폼별로 해석. 단 build 시 mp4 가 압축 제외(uncompressed)여야 함.
- **출처**: `AdRenderer.cs:20-22,98-138`
- **상태**: 🔬 추정 (위 SpatialAnchorTest 경로와 비대칭 — 라인별로 다름)

### VideoPlayer 셋업/Prepare 의 native 예외를 try/catch 로 흡수 — C# 은 catch 있는 try 안에서 yield 불가 🆕 (코드에서 구조)
- **사실**: VideoPlayer 셋업+Prepare 의 native 예외를 흡수하려면 try/catch 가 필요하나, C# 은 catch 가 있는 try 블록 안에서 yield 불가 → 코루틴에서 try 안에 yield 를 못 두고 `ok` bool 플래그만 설정한 뒤 try 밖에서 폴백. 재생 중 디코더 오류는 `errorReceived` 콜백으로 PNG 폴백.
- **근거**: C# iterator 제약: catch/finally 절을 가진 try 블록 내부에서 `yield return` 금지.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:468-503`
- **상태**: ⚠️ 주의

### Recording: 카메라 single-owner 라 in-app dump + adb broadcast, receiver 는 foreground 만
- **사실**: 별도 녹화 앱 불가(Android 카메라 single-owner, Unity 앱이 점유) → 카메라 JPEG dump 가 앱 내장(`FirstPersonRecorder`, 5fps), adb broadcast(`RecordingReceiver.java`, Android 13+ `RECEIVER_EXPORTED`)로 토글. dynamic receiver 는 앱 foreground 일 때만 수신. 시간동기는 `RECORD_START` 에 0.2s 흰 flash 렌더 후 디스플레이 녹화에서 밝기 spike(YAVG) 검출로 자동화.
- **근거**: Android 가 카메라를 한 프로세스에 배타 부여. dynamic receiver 는 foreground 일 때만 active.
- **출처**: `docs/vision.md:2315-2326`
- **상태**: ✅ 확정

### RayNeo 네이티브 RecordManager 는 카메라 센서가 아니라 Unity 렌더 surface 캡처 — 광고 overlay 포함 녹화 가능(미사용) 🆕 (코드에서 구조)
- **사실**: SDK 의 `RecordManager.StartRecord(Camera, ...)` → `CameraInput` 이 `WaitForEndOfFrame` 후 `camera.targetTexture` 에서 `AsyncGPUReadback`/`ReadPixels` 로 픽셀을 읽어 `MP4Recorder`/`HEVCRecorder.CommitFrame(buffer, ts)` 으로 네이티브 인코딩. **카메라 센서를 점유하지 않고 Unity 렌더 결과(= 광고 overlay 포함)만** MP4/HEVC 로 녹화한다. 현재 프로젝트는 미사용(5fps JPEG `FirstPersonRecorder` 만 씀).
- **근거**: 렌더 타겟 캡처라 카메라 single-owner 제약(아래 Recording 엔트리)을 우회 — 광고 overlay 포함 시연 영상을 고품질로 뽑는 데 적합.
- **출처**: `.../Extension~/com.rayneo.ext.recorder/Scripts/RecordManager.cs:22-31`; `.../Recorders/Inputs/CameraInput.cs:76-104`; `.../Recorders/MP4Recorder.cs:15-33`
- **상태**: ✅ 확정

### EasyOCR recognizer NPU 비호환 + Spencerian Coca-Cola 로고는 OCR 불가 → MLKit/색 폴백
- **사실**: EasyOCR CRAFT detector 는 NPU 위임되나 recognizer 는 NPU 비호환(b22) → 한때 OCR 을 MLKit Text Recognition v2(`com.google.mlkit:text-recognition:16.0.1`, Latin/English, ~50ms)로 출시. Korean/Chinese 는 별도 패키지. MLKit OCR 은 Coca-Cola Spencerian 필기체 로고를 전혀 못 읽음 — brand-fallback-OFF 에서 block-letter 'pepsi' 만 OCR 매칭, coca-cola=0 hits. (spatial_anchor_test 라인은 MLKit 없이 EasyOCR(TFLite+QNN)만. 두 빌드 라인의 OCR 엔진이 다름 — gradle 만 보면 헷갈림.)
- **근거**: recognizer op set 이 QNN delegate 미지원. OCR 엔진이 printed/block text 학습이라 필기체 defeat.
- **출처**: `docs/dev-guide.md:59`; `docs/vision.md:2352,2400`; `docs/progress-log.md:474-476`; `spatial_anchor_test/Assets/Plugins/Android/mainTemplate.gradle:21-22`
- **상태**: ✅ 확정

### CLIP-only 모드면 QnnYoloDetector 컴포넌트를 AddComponent 안 함 (수십초 그래프 컴파일 회피)
- **사실**: `clipOnlyMode=true` 면 yolo 컴포넌트를 아예 안 붙임 → 앱 시작 시 QNN 이 yolo11l 그래프(10000+ 노드, w8a8)를 Hexagon 용으로 컴파일하던 수십초 startup 지연 제거(launch 수초로). 런타임 YOLO 추론을 안 하므로 동작 영향 없음. `clipOnlyMode` 는 Awake 에서 코드로 강제(true). 전환 이유 = 안경 시점에서 YOLO bottle/book conf 가 ~0.05 수준(w8a8 손실 + 글라스 시점 분포)이라 신뢰성 낮음.
- **근거**: `QnnYoloDetector` 의 init(=QNN 인터프리터 생성)이 Hexagon 그래프 컴파일을 동기 수행. 컴포넌트 미부착이면 컴파일 자체가 안 일어남.
- **출처**: `HelloAR.cs:133-143`; `docs/dev-guide.md:24,143,647`; `docs/progress-log.md:410,460`; `docs/vision.md:22,33`
- **상태**: ✅ 확정

### skipOcr=true 면 OCR 엔진 init 자체를 안 함 (159초 HTP compile/CDSP 미발생)
- **사실**: `skipOcr=true` 면 `OCRExtractor` 컴포넌트를 AddComponent 안 함(b25). OCR 런타임 제거 — engine 을 init 안 해 159초 HTP compile/CDSP 가 미발생. MLKit/OCR init JNI 가 메인스레드를 블록해 시작 직후 렌더링 정지를 유발하기 때문. brand 는 color 경로로 확정.
- **근거**: OCR 인터프리터 생성이 동기 JNI 이며 HTP 그래프 컴파일을 메인스레드에서 수행. 캐시 없으면 수십~수백 초.
- **출처**: `HelloAR.cs:69-72,159-162`
- **상태**: ✅ 확정

### AdCheckout 입력 = RayNeoRingController homeButton + head-forward dwell (컨트롤러 없이 동작) 🆕 (코드에서 구조)
- **사실**: checkout select 두 경로: (1) discrete — InputAction 이 `<RayNeoRingController>/homeButton`(thumbstick/click), 폴백 `<XRController>/{PrimaryButton}`. (2) dwell — gaze pose 없으면 `camera.forward`(head-forward)를 reticle 로 써 대상과 각도 `dwellConeDeg(8도)` 이내 hover 가 `dwellSeconds(1.2초)` 지속되면 자동 select. ring mesh 대신 8조각 tick 을 progress 비율만큼 켜 fill 표현(셰이더 추가 회피).
- **근거**: RayNeo gaze_ext pose 미연동 + 컨트롤러 부재 대비. head pose 는 항상 가용.
- **출처**: `AdCheckout.cs:54-93,146-176,324-363`
- **상태**: ✅ 확정

### AdCheckout 은 외부앱/QR/실결제 모두 배제 — OpenXR 세션 이탈 시 black-screen 위험 🆕 (코드에서 구조)
- **사실**: checkout 은 전부 in-app mock. 실결제 SDK X, QR X(글라스 시점 스캔 불가), 외부 앱(Alipay/브라우저) 실행 X — OpenXR 세션 이탈 시 black-screen/세션 단절 위험을 겪었기 때문. 결제 흐름을 in-AR mock 패널(썸네일+이름+가격+BUY→ORDER PLACED)로 완성.
- **근거**: OpenXR 세션이 포그라운드 앱 전환 시 컨텍스트를 잃고 복귀 못 함 → 검은 화면.
- **출처**: `AdCheckout.cs:6-11`
- **상태**: ⚠️ 주의

### QNN/TFLite 의존성은 Maven Central 에서 — 별도 Qualcomm maven repo 불필요
- **사실**: `com.qualcomm.qti:qnn-runtime:2.47.0`, `qnn-litert-delegate:2.47.0`, `org.tensorflow:tensorflow-lite:2.15.0`, `tensorflow-lite-support:0.4.4` 를 `implementation` 선언. Maven Central 에 publish 돼 있어 root 권한/별도 repo 불필요(`google()+mavenCentral()` 만으로 충분).
- **근거**: Qualcomm QNN artifact 가 Maven Central 에 올라와 있어 SNPE SDK 로컬 설치 없이 gradle 의존성만으로 NPU delegate 사용 가능.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/mainTemplate.gradle:5-22`
- **상태**: ✅ 확정

### AI Hub --quantize_full_type 은 'w8a8' 거부 — full-INT8 은 'int8' 로 써야 함
- **사실**: AI Hub `--quantize_full_type` 은 {int8, int16, float16, w8a16, w4a8, w4a16} 만 받음. 'w8a8' 은 INVALID(첫 시도 실패), 'int8' 이 full INT8 의 올바른 토큰. (프로젝트 산문이 정밀도를 'w8a8' 로 부르는 건 개념명이지 CLI 토큰 아님.)
- **출처**: `docs/vision.md:1323-1325`
- **상태**: ⚠️ 주의

### w8a16 양자화는 MobileCLIP-S2 에서 실패 — L2-norm SQRT/DIV op 가 16x8 미지원
- **사실**: AI Hub w8a16 양자화가 'Quantization to 16x8-bit not yet supported for op: SQRT'/'DIV' 로 실패. 이는 export 그래프(`ImageEncoderWithNorm`)에 baked 된 L2-normalization 에서 옴. INT8 은 지원·통과 + 더 빠름(2.12ms vs FP 4.9ms)이라 INT8 이 우연히 더 나은 결과. w8a16 ORT/QNN-EP 경로도 Maven native-lib 충돌로 보류.
- **근거**: 16x8 mixed 양자화가 in-graph L2 norm 의 SQRT/DIV 커널 없음, full INT8 은 있음.
- **출처**: `docs/vision.md:1304,1317-1322`; `docs/dev-guide.md:143,235`
- **상태**: ✅ 확정

---

## 🏗️ 빌드

### 빌드는 Windows + Unity 2022.3.62f3 only — 옛 Mac/build_hello_ar.sh/EagleEye_Unity 경로 폐기
- **사실**: 현 글라스 빌드는 Windows + Unity 2022.3.62f3 only, `spatial_anchor_test/` PowerShell 스크립트(`setup_2022.ps1`/`build_2022.ps1`) 경유. 모델/DB/광고는 `Assets/StreamingAssets` 에 이미 번들 → 복사 단계 없음. 옛 Mac `build_hello_ar.sh` + `unity_assets_prep/` → `EagleEye_Unity/` 계보는 2026-06-13 폐기.
- **출처**: `docs/dev-guide.md:79-89`
- **상태**: ✅ 확정

### setup/build PS1 스크립트가 Unity 2022 vs 6000 두 갈래 (다른 staging 경로·다른 launch 액티비티)
- **사실**: `build_spatial_anchor.ps1`/`setup_spatial_anchor.ps1` 은 Unity 6000.0.76f1 + `C:\claude\staging\spatial_anchor_unity` 를, `build_2022.ps1`/`setup_2022.ps1` 은 2022.3.62f3 + `C:\claude\staging\spatial_anchor_unity_2022` 를 가리킨다. setup 후 사용자가 Unity Editor GUI 에서 XR Plug-in Management(OpenXR + RayNeo feature group)를 수동 셋업해야 빌드 가능. launch 액티비티도 스크립트별로 `UnityOpenXrActivity`(2022) vs `UnityPlayerGameActivity`(6000)로 다름. 6000 라인은 docs 상 폐기(검은화면).
- **근거**: Unity 6 시도(6000)와 확정 2022 라인이 별도 staging 디렉토리로 공존.
- **출처**: `spatial_anchor_test/build_2022.ps1:6-9,51`; `spatial_anchor_test/build_spatial_anchor.ps1:6-7,49`
- **상태**: ⚠️ 주의

### Unity 빌드 wrapper 의 exit 1 은 false signal — Unity.exe zombie 가 다음 빌드 lock 충돌 🆕 (코드에서 구조)
- **사실**: PowerShell build wrapper 가 exit 1 을 report 해도 Unity.exe 는 살아 log 가 계속 grow. `wc -l logfile` 만으로 판단 불가, process state 동시 확인 필요. zombie(netcorerun.exe/Unity.exe/UnityCrashHandler64.exe)가 다음 빌드 lock 충돌 유발. robust path = `tasklist` 로 zombie 확인 → 모두 종료 → `Library/Bee` fresh delete → 재빌드.
- **근거**: batchmode wrapper 가 Unity 자식 프로세스 생존과 분리되어 종료코드가 실제 상태와 불일치.
- **출처**: `spatial_anchor_test/JOURNEY.md:81-86`
- **상태**: ⚠️ 주의

### Bee 빌드 캐시 stale 이면 코드 변경 미반영 APK — versionName 으로 검증
- **사실**: Bee 빌드 캐시가 stale 하면 APK 가 코드 변경을 반영 못 한 채 빌드. `aapt dump badging APK | grep versionName` 과 `dumpsys package | grep versionName` 이 둘 다 `BUILD_TAG` 와 일치해야 함, 불일치면 `Library/Bee` 정리 후 재빌드. `BUILD_TAG`+`OUTPUT_APK` 2곳 동기 수정 안 하면 파일명과 versionName 어긋남. `bundleVersionCode` 는 매 빌드 +1 자동, `bundleVersion`=`BUILD_TAG`(예: `b26-dedup-checkout`)로 dumpsys 검증. colorSpace=Linear.
- **근거**: Bee incremental 캐시가 변경 감지 실패 시 옛 산출물 재사용.
- **출처**: `docs/spatial-anchor-handoff.md:105-117,130-139`; `BuildSpatialAnchorTest.cs:26-29,266-270`
- **상태**: ⚠️ 주의

### Inspector 직렬화 값이 코드 field default 를 override → Awake 에서 매번 강제 재할당 🆕 (코드에서 구조)
- **사실**: Unity 가 Inspector 직렬화 값으로 코드 field default 를 override 하는 동작 때문에 `clipMatchThreshold`(0.45)·`clipOnlyMode`(true) 등을 매 Awake 에서 강제 재할당. v0.7.4 는 이 줄이 0.55 로 박혀 field default 0.45 가 무시되고 실제론 0.55 로 돌던 버그가 있었음. Inspector 로 변경하려면 이 강제 줄을 주석 처리해야 함.
- **근거**: Unity 가 .meta/scene 에 컴포넌트 필드를 직렬화해 두면 그 값이 C# field initializer 보다 우선. Awake 재할당만이 코드 의도 보장.
- **출처**: `HelloAR.cs:126-135`
- **상태**: ⚠️ 주의

### scene 의 SpatialAnchorHost 가 이미 SpatialAnchorTest 를 갖고 있어 GetComponent 재사용 (이중 spawn 방지) 🆕 (코드에서 구조)
- **사실**: B8 빌드부터 scene 의 SpatialAnchorHost 가 이미 `SpatialAnchorTest` 를 갖고 HelloAR 를 AddComponent 하므로, HelloAR 는 `GetComponent<SpatialAnchorTest>()` 로 먼저 찾아 재사용하고 없을 때만 AddComponent. 두 번째 AddComponent 면 SLAM 구독·anchor·HUD 가 이중 spawn.
- **근거**: 씬에 미리 배치된 호스트와 코드 AddComponent 가 동일 컴포넌트를 두 번 만들면 OnEnable/이벤트 구독 중복.
- **출처**: `HelloAR.cs:154-157`
- **상태**: ✅ 확정

### 패키지명을 com.eagleeye.helloar 로 override — 옆 세션이 원본 패키지 점유 🆕 (코드에서 구조)
- **사실**: `PACKAGE_NAME=com.eagleeye.helloar` 강제. b22 OCR+SLAM 통합 시 원본 real-pipeline package(`spatialanchor.bisection`)는 옆 세션이 점유 중이라 helloar 로 override. (`build_2022.ps1`/`build_spatial_anchor.ps1` 의 `PACKAGE_NAME=com.eagleeye.spatialanchor` 와 불일치 — 스크립트 install/launch 명령이 stale, adb 명령 복붙 시 함정.)
- **근거**: 동시 작업 세션 간 패키지명 충돌 회피.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:22-23`
- **상태**: ⚠️ 주의

### batchmode 빌드에서 SwitchActiveBuildTarget 재호출 금지 — domain reload 가 빌드 함수를 끊음 🆕 (코드에서 구조)
- **사실**: `PerformBuild` 는 `-buildTarget Android` CLI 인자가 이미 active target 을 셋팅했다고 가정하고 `SwitchActiveBuildTarget` 을 호출하지 않는다. 다시 호출하면 domain reload 가 트리거되어 `PerformBuild` 의 나머지 코드 실행이 끊긴다.
- **근거**: build target 전환이 C# 도메인 리로드를 유발하고, 리로드 중이면 진행 중이던 메서드 콜스택이 파기됨.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:37-39`
- **상태**: ⚠️ 주의

### batchmode 는 GUI Preferences 미접근 → JDK/SDK/NDK/Gradle 경로를 코드로 강제 set
- **사실**: `ConfigureExternalTools` 가 JDK/SDK/NDK/Gradle 경로를 실행 에디터 기준으로 강제 set(batchmode 는 GUI Preferences 못 봄). 단 `AndroidExternalToolsSettings` 의 sdk/ndk/gradle setter 는 Unity 의 `OnUsbDevicesChanged` callback chain 을 트리거해 silent exit(`AndroidSDKTools.ctor:62` fail, Cycle 4 실패 원인) 유발 가능 — 그래서 'JDK 만 set' 의도였으나 실제 코드는 sdkRootPath/ndkRootPath 도 set 함.
- **근거**: setter 들이 디바이스 재스캔 콜백을 동기 트리거하고 그 안에서 예외가 나면 batchmode 가 조용히 종료. batchmode 가 Preferences 미접근.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:201-205`; `docs/spatial-anchor-handoff.md:119-127`
- **상태**: ⚠️ 주의

### SDK/NDK/Gradle 을 실행 에디터 번들로 강제 안 하면 다른 에디터 도구 가리켜 빌드 실패; cmake 게이트 제거
- **사실**: inherited EditorPrefs 가 다른 에디터(2022.3.62f3)의 SDK 를 가리키면 'Missing CMake 3.22.1' 로 실패, custom Gradle 이 2022.3 의 Gradle 7.5.1 을 가리키면 'Minimum supported Gradle version is 8.11.1' 로 실패. 그래서 sdkRootPath/ndkRootPath 를 `PlaybackEngines/AndroidPlayer/{SDK,NDK}` 로 강제 + EditorPrefs `GradleUseEmbedded=true`(6000 번들 Gradle 8.13 / 2022 번들). 단 이 프로젝트는 CMakeLists/C++ 소스 없이 모든 native 가 prebuilt .so 라 번들 SDK 에 cmake 3.22.1 없어도 무관 — 과거 cmake 존재로 SDK set 을 게이트하던 게 오히려 'Android SDK not found' 를 유발해 제거(무조건 set).
- **근거**: 여러 Unity 에디터가 EditorPrefs 를 공유해 도구 경로가 교차 오염. native 가 전부 prebuilt 라 cmake 불필요.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:225-257`; `docs/spatial-anchor-handoff.md:119-127`
- **상태**: ✅ 확정

### 이 머신엔 OpenJDK 모듈 미설치 → 시스템 Adoptium JDK 17 폴백
- **사실**: JDK 후보 순서: (1) 에디터 번들 `PlaybackEngines/AndroidPlayer/OpenJDK`, (2) 시스템 `C:\Program Files\Eclipse Adoptium\jdk-17.0.16.8-hotspot`. 6000.0.76f1 에 OpenJDK 모듈 미설치라 fallback 필요. JDK 17 은 Unity 6 / 최신 AGP 요구 버전.
- **근거**: Unity Hub 에서 Android JDK 모듈을 안 깔면 번들 OpenJDK 없음.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:206-223`
- **상태**: ✅ 확정

### PlayerSettings 변경 후 AssetDatabase.SaveAssets() 안 하면 OpenXR OnPreprocessBuild 가 캐시값 봐서 minSdk validation 실패 🆕 (코드에서 구조)
- **사실**: `ConfigurePlayerSettings`/`ConfigureAndroidSettings` 직후 `AssetDatabase.SaveAssets()` 로 `ProjectSettings.asset` 에 즉시 flush. 빠지면 OpenXR 의 `OnPreprocessBuild` 가 이전 캐시 값을 읽어 minSdk validation 으로 빌드 실패.
- **근거**: OpenXR preprocess 훅이 디스크의 ProjectSettings.asset 을 다시 읽어 메모리 변경이 flush 안 되면 stale 값을 봄.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:47-49`
- **상태**: ✅ 확정

### RayNeo X3 Pro 는 API 32 OS 인데 minSdk 30 / targetSdk 34 로 빌드 🆕 (코드에서 구조)
- **사실**: `minSdkVersion=30`(OpenXR plugin 강제), `targetSdkVersion=34`(androidx.activity:1.7.1 등이 compileSdk 33+ 요구), targetArchitectures=ARM64, ScriptingBackend=IL2CPP, buildAppBundle=false, androidBuildSystem=Gradle. RayNeo X3 Pro OS 는 Android 12(API 32)지만 targetSdk 34 앱도 정상 동작(OS 가 targetSdk 에 맞춰 behavior 적용). activeInputHandler=2(Both), color space Linear, scriptingDefineSymbols `USE_INPUT_SYSTEM_POSE_CONTROL;USE_STICK_CONTROL_THUMBSTICKS`, allowUnsafeCode=0.
- **근거**: OpenXR plugin 이 minSdk 30 미만 거부, androidx 의존성이 높은 compileSdk 요구. POSE_CONTROL define 이 켜져야 OpenXR pose control 코드 컴파일.
- **출처**: `spatial_anchor_test/Assets/Editor/BuildSpatialAnchorTest.cs:283-296`; `ProjectSettings/ProjectSettings.asset:767,267,678,50,175-176,671-674`
- **상태**: ✅ 확정

### Mac base TensorFlow 가 protobuf 충돌로 깨짐 → ref 재인코딩은 격리 venv(/tmp/ee_venv, tf 2.21)
- **사실**: Mac base TensorFlow 설치가 protobuf 충돌로 깨져 `build_adversarial_db.py` 는 격리 venv(`/tmp/ee_venv` + tf 2.21)에서 실행해야 함. 빌드 디스크는 `~/.gradle/caches`(17GB) 정리로 확보. qai-hub 는 Qualcomm AI Hub 계정+API token(`qai-hub configure --api_token`) 필요. Python env: ultralytics open-clip-torch torch onnx onnxruntime onnxslim onnxscript qai-hub h5py (Python 3.10-3.12).
- **근거**: protobuf 버전 충돌이 base TF 를 깨 embedding rebuild 에 격리 환경 필요.
- **출처**: `docs/progress-log.md:668`; `docs/dev-guide.md:184,190`
- **상태**: ✅ 확정

### gradle 템플릿이 com.android.library 플러그인 + namespace com.unity3d.player (앱이 아닌 라이브러리 모듈) 🆕 (코드에서 구조)
- **사실**: `mainTemplate.gradle` 은 `apply plugin 'com.android.library'`, `android.namespace 'com.unity3d.player'`, `consumerProguardFiles 'proguard-unity.txt'`, `lint.abortOnError false`, source/targetCompatibility VERSION_11. Unity 가 unityLibrary 모듈을 라이브러리로 빌드하고 launcher 모듈이 이를 포함하는 표준 multi-module 구조.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/mainTemplate.gradle:1,26,33-36,47,51-53`
- **상태**: ✅ 확정

### keepUnitySymbols.gradle 가 repo 에 없어 disabled — 원본 Mac 환경 잔재 🆕 (코드에서 구조)
- **사실**: `mainTemplate.gradle` 상단의 `apply from: '../shared/keepUnitySymbols.gradle'` 는 주석처리됨. 원본 Mac 환경의 별도 gradle file 인데 이 repo 에 없어서 disabled.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/mainTemplate.gradle:2`
- **상태**: ⚠️ 주의

### OpenXR feature 활성 = RayNeoSupportFeature + RayNeoControllerProfile 둘뿐, 나머지 전부 비활성 🆕 (코드에서 구조)
- **사실**: Android feature 그룹에서 `m_enabled:1` 인 것은 `RayNeoSupportFeature`(asset:661), `RayNeoControllerProfile`(asset:233) 둘뿐. Meta/Oculus Quest, Foveated Rendering, MockRuntime, EyeGaze, HandInteraction, Microsoft/HTC/Valve 컨트롤러 등은 전부 `m_enabled:0`. 활성 featureSet 은 `com.unity.xr.rayneo.openxr` 하나. renderMode=1(Single Pass Instanced), depthSubmissionMode=0.
- **근거**: RayNeo X3 Pro 런타임은 foveation/hand-tracking/eye-gaze/표준 컨트롤러 확장 미제공 → 해당 feature 무의미/충돌.
- **출처**: `spatial_anchor_test/Assets/XR/Settings/OpenXR Package Settings.asset:661,233`; `.../OpenXR Editor Settings.asset:18,20`
- **상태**: ✅ 확정

### Android 만 m_AutomaticLoading=1, Standalone 은 0 (에디터 Play 로는 SLAM/카메라 안 도는 게 정상) 🆕 (코드에서 구조)
- **사실**: `XRGeneralSettingsPerBuildTarget.asset`: Android Providers 는 `m_AutomaticLoading:1`/`m_AutomaticRunning:1` + Open XR Loader 1개. Standalone 은 둘 다 0 + `m_Loaders` 빈 리스트 → Standalone(에디터/PC)에는 OpenXR 로더가 없어 RayNeo XR 은 Android 빌드에서만 부팅. (docs/integration_log:52 는 `m_AutomaticLoading:0`(수동 init)이라 적었으나 현재 Android asset 은 1 — docs/asset 불일치.)
- **근거**: Standalone 에 OpenXR 로더 자체가 없어 에디터 Play 로는 SLAM 미동작.
- **출처**: `spatial_anchor_test/Assets/XR/XRGeneralSettingsPerBuildTarget.asset:16,33`
- **상태**: ⚠️ 주의

### m_AutomaticLoading:1 이 RayNeo 빌드를 단계마다 비결정적으로 crash 시킴 🆕 (코드에서 구조)
- **사실**: `XRGeneralSettingsPerBuildTarget` 의 Android Providers 를 `m_AutomaticLoading:0→1` 로 바꾸면 IL2CPP/Initial Refresh/ScriptCompilation 등 빌드 단계마다 일관되게 crash. 0 으로 두고 코드에서 `XRGeneralSettings.Instance.Manager.InitializeLoaderSync()` 를 명시 호출해야 함(idempotent — 'XR Management has already initialized an active loader' 경고만, 무해). UnityOpenXrActivity 의 native init 과 auto-loader 충돌 추정. **(시대 정정: 이건 JOURNEY 당시 결론이다. 현재 asset 은 Android=1 로 ship 되고 manual `InitializeLoaderSync` 호출도 제거된 채 안정 동작 — 위 "Android 만 m_AutomaticLoading=1" + SLAM 섹션 "SlamDemoCtrl 최소 시퀀스" 엔트리가 현행. 즉 =1 이 빌드를 깨던 건 옛 빌드 구성에서였고 현재는 =1 + manual init 제거로 안정. handoff 시 이 두 엔트리를 시간순으로 읽을 것.)**
- **근거**: RayNeo UnityOpenXrActivity 가 native 로더를 직접 부팅해 Unity auto-loader 와 이중 init 충돌.
- **출처**: `spatial_anchor_test/JOURNEY.md:40-46`
- **상태**: ⚠️ 주의

### OpenSLAMOnStart 실제 asset 값=0 인데 docs 는 1 이라고 적혀있음 (불일치) 🆕 (코드에서 구조)
- **사실**: asset 의 `OpenSLAMOnStart=0`(asset:669). 그러나 integration_log.md:53/:135/:169 는 '0 → 1' 로 바꿨다고 기록. SLAM 을 `SpatialAnchorTest.Start` 의 수동 init 으로 켜는 현재 구조와 일관되게 asset 값은 0 으로 되돌아가 있으나 docs 산문은 1 로 단정 — 현행 asset 과 docs 가 어긋남.
- **근거**: 수동 native polling + manual init 경로로 가면서 OpenSLAMOnStart 자동 시작을 도로 끈 것으로 보임.
- **출처**: `spatial_anchor_test/Assets/XR/Settings/OpenXR Package Settings.asset:669`
- **상태**: ⚠️ 주의

### RayNeoGeneralSettings 디바이스 테이블에 X3 Pro 없음 — X2(type 4096)가 유일 standalone, 640×480 🆕 (코드에서 구조)
- **사실**: `RayNeoGeneralSettings.asset` `m_devices`: NextViewPro/Air Plus 계열(type 32~48)은 1920×1080 FOV~21.6~23.8; X2(type 4096)=640×480 FOV 16.67944. 현 타겟 X3 Pro 항목은 테이블에 없음. touchConfigs 는 platformType 4097(=X3 계열?)까지 정의돼 있으나 device 엔트리는 없음. → X3 Pro 는 정적 테이블 대신 런타임 식별 또는 platformType 4097 경로로 처리되는 듯.
- **근거**: 이 asset 은 RayNeo SDK 번들 기본 디바이스 프로파일. X2 의 640×480 FOV16.68 은 birdbath 형 per-eye 해상도.
- **출처**: `spatial_anchor_test/Assets/XR/RayNeoGeneralSettings.asset:119-154`
- **상태**: 🔬 추정

### RayNeo touch 매직 상수 + SleepTimeOut=-1(슬립 끔) 🆕 (코드에서 구조)
- **사실**: `RayNeoGeneralSettings` touchConfigs(모든 platformType 동일): MovingThreshold 80, SwipeEndThreshold 200, FastSwipeEndThreshold 480, SwipXStepTriggerDistance 200, MulitTapSpacing 0.4s, LongPressSpacing 0.8s. baseConfig: LogLevel 4, `SleepTimeOut -1`(슬립 끔, 데모 중 꺼짐 방지), ErrorWindow 1. 안경다리 터치패드 제스처 임계값이 전부 코드 밖 asset 으로 노출돼 튜닝 가능.
- **근거**: 안경 템플(touchpad) 제스처 인식 임계값. FastSwipe(480) > Swipe(200)으로 빠른 스와이프 별도 분기.
- **출처**: `spatial_anchor_test/Assets/XR/RayNeoGeneralSettings.asset:15-117`
- **상태**: ✅ 확정

---

## 🎯 매칭 · 튜닝

### CLIP zero-shot 은 fine-grained brand 분별 불가 — 환경 신호가 brand 신호를 압도
- **사실**: MobileCLIP-S2 INT8 zero-shot 은 시각적으로 유사한 brand(코크 vs 펩시 PET 병)를 본질적으로 분리 못 함. 배경/책상색/조명이 임베딩에서 brand 신호를 압도. 증거: 15-trigger pepsi 데모에서 ZERO 가 pepsi 매칭(coke 7, laptop 8) — 사용자의 빨간 책상 환경이 jetson_coke ref 분포와 매칭돼 코크가 항상 이김. coke↔pepsi self cosine sim=0.799, 최소 마진 +0.011. top-k/threshold/ref-diversification 어떤 조합으로도 해결 안 됨 → argmax(best-match)만 의미 있음.
- **근거**: zero-shot CLIP 임베딩이 global scene context 를 강하게 인코딩 — near-identical 객체는 환경 분산이 brand 분산을 초과해 cosine sim 이 배경에 지배됨.
- **출처**: `docs/vision.md:1622-1632,2336-2338`; `docs/progress-log.md:436-449`
- **상태**: ✅ 확정

### 온디바이스 CLIP sim 이 Mac 오프라인 대비 ~0.3 낮음 — 오프라인 숫자로 실기기 예측 불가
- **사실**: 같은 프레임에서 Mac 오프라인 cola sim 0.85 vs 안경 0.55 로 ~0.3 격차(frame_0011). 원인 미규명 — `ClipExtractor.PreprocessTexture` 의 RenderTexture Blit/ReadPixels vs PIL 전처리 불일치(Y-flip 가능성)와 w8a8 양자화 노이즈 추정. 노이즈(~0.3) ≫ 매칭 마진(~0.07) 이라 마진 잠식 → threshold 0.55→0.45 + 중앙 crop 으로 우회 중. 오프라인 8/8=100% 가 실기기를 전혀 예측 못 하는 sim-to-real 격차.
- **근거**: GPU readback 픽셀 처리(Y축 방향/리사이즈 필터)와 양자화 round-off 가 Mac PIL float 경로와 달라 임베딩이 미세하게 어긋남.
- **출처**: `spatial_anchor_test/Assets/Scripts/ClipExtractor.cs:245-258`; `docs/vision.md:17,33,37`; `docs/progress-log.md:656,672`; `docs/freeze-accuracy-diagnosis.md:54`
- **상태**: ✅ 확정

### 색(평균 RGB) 마진 ~1.0 ≫ CLIP brand 마진 0.07 → brand 는 색으로 가름
- **사실**: MobileCLIP-S2 코크↔펩시 코사인 0.805, 분리 마진 0.067~0.079 ≪ 양자화 노이즈 ~0.3 → 항상 코크 오판. 반면 색 히스토그램은 코크 빨강 96.7%/펩시 파랑 97.7%, 분리 마진 ~1.0. category(콜라병?)도 cola↔laptop centroid 0.887 로 약해 threshold 0.45 는 온디바이스 격차를 가리는 임시 보정값.
- **근거**: w8a8 양자화+전처리 불일치가 임베딩에 ~0.3 노이즈를 주는데 코크/펩시 분리는 0.07 뿐이라 압도됨. 색은 환경-독립 + 마진 큼.
- **출처**: `docs/freeze-accuracy-diagnosis.md:46-62,67-73`; `B25_DEMO_HANDOFF.md:56`
- **상태**: ✅ 확정

### 색 판별을 dominant 픽셀 count → 중앙 박스 평균색 lean 으로 전환 (펩시 빨간 뚜껑 오판 때문)
- **사실**: strict count 방식(`r>g+colorMargin`, colorMargin=20)은 펩시 파란 몸통이 임계 미달로 blue=0.00, 빨간 뚜껑 때문에 coke 오판(실측: pepsi 인데 red=0.41 blue=0.00). v1.3 에서 같은 중앙 박스(`colorSampleFraction=0.8`)의 채널 합을 누적해 영역 평균색(`lastMeanR/G/B`, 0-255 float)으로 전환 — 면적 큰 파란 몸통이 평균을 파랑으로 끌어줌. ratio 3종은 로그 비교용으로만. 색 카운트는 NCHW 정규화와 같은 루프에서(별도 readback 금지). deprecated 필드 `colorBrandMinRatio`(0.15)/`colorBrandDominance`(1.5) 잔존.
- **근거**: 병의 작지만 강한 빨간 뚜껑이 dominant-count 에선 과대표되나 면적가중 평균에선 큰 파란 몸통에 희석돼 brand 를 더 안정 분리.
- **출처**: `HelloAR.cs:87-92,353-357`; `spatial_anchor_test/Assets/Scripts/ClipExtractor.cs:51-60,262-293`
- **상태**: ✅ 확정

### 색 마진 비대칭 5 대 25: blueLean>5 → 펩시, redLean>25 → 코크
- **사실**: cola brand 는 중앙 박스 평균색 lean 으로 확정. `blueLean(mB-mR) > colorBlueMargin(5)` → pepsi, `redLean(mR-mB) > colorRedMargin(25)` → coca-cola. 마진 비대칭 이유: 코크는 파랑이 거의 0 이라 조금만 파랑이어도 펩시 확정 가능하나, 코크(빨강)는 강하게 우세해야 확정해야 빈 책상/빨간 뚜껑 FP 차단. 둘 다 약하면 null → 광고 안 띄움(FP 2차 게이트).
- **근거**: 코크 캔/병에 파랑이 없으므로 파랑 신호 = 펩시 확정. 빨강은 양쪽(코크몸통·펩시뚜껑) 다 나와 약한 redLean 은 모호.
- **출처**: `HelloAR.cs:93-97,369-384`
- **상태**: ✅ 확정

### enableClipBrandFallback 은 환경 bias 로 항상 coca-cola → 기본 OFF
- **사실**: OCR 실패 시 CLIP brand-specific 임베딩으로 brand 추정하는 fallback 은 환경 bias 로 항상 coca-cola 를 찍어 펩시를 코크 오판(시연 #13) → `enableClipBrandFallback=false` 기본. 색 판별에서 brand 미확정 시에도 이 fallback 으로 떨어지면 안 됨 — 켜져 있으면 빈 책상/천장도 코크 FP 재발하므로 색 모호 시 폴백 금지하고 null 반환.
- **근거**: 온디바이스 CLIP 코크↔펩시 분리 마진 ~0.07 ≪ w8a8 양자화 노이즈 ~0.3 → 환경(빨간 책상) 분포가 지배.
- **출처**: `HelloAR.cs:147-149,258-262,380-383`; `ProductMatcher.cs:33-38,264-266`
- **상태**: ✅ 확정

### Center-crop query AND ref 가 동일 crop fraction 이어야 함 (ClipExtractor.cropFraction == build_adversarial_db CLIP_CROP)
- **사실**: full-frame CLIP 은 배경이 지배(환경 바뀌면 cola 가 laptop 에 짐). 수정: `ClipExtractor` 가 중앙 crop(0.5)만 임베드(Graphics.Blit scale(f,f), offset((1-f)/2,(1-f)/2)) AND `build_adversarial_db.py` 도 같은 crop 으로 ref 재인코딩. 두 crop 상수(`cropFraction`/`CLIP_CROP`, 둘 다 0.5)가 일치 안 하면 query↔ref 임베딩이 mismatched space → 매칭 무효(에러 없이 저하). 중앙 crop 은 Y-flip 방향 무관하지만 ref 와 같은 값이어야 비교 성립. CLIP 입력 256×256, EMBED_DIM 512(L2-normalized, 그래프 포함). OpenAI CLIP mean/std, NCHW. 환경-정렬 ref(refs/cola/dev_*)는 자기 환경에서만 동작.
- **근거**: cosine sim 은 query 와 ref 가 같은 FOV 를 봐야 의미 — 다른 crop 은 인코더에 들어가는 픽셀을 바꿈. C# 런타임 상수와 Python 빌드 스크립트 상수의 silent 결합.
- **출처**: `spatial_anchor_test/Assets/Scripts/ClipExtractor.cs:16-17,66-68,250-253`; `docs/vision.md:35,650-653`; `docs/dev-guide.md:49,141`
- **상태**: ✅ 확정

### 온디바이스 category 분별도 약함 — 빈 책상/벽이 cola(0.58) FP, OCR 게이트만이 잘못된 광고 차단
- **사실**: 온디바이스에선 coarse category 분별도 약함 — cola 와 laptop 둘 다 0.5-0.6 narrow band, bottle-free 책상/벽 scene 이 cola(0.58)로 FP. 잘못된 광고를 막는 건 strict OCR brand 게이트 — OCR 키워드 매칭 없으면 광고 X → CLIP category mis-fire 가 사용자에게 wrong ad 로 도달 안 함.
- **근거**: center-cropped 온디바이스 임베딩이 dynamic range 압축(~0.3 shift)돼 절대 category sim 이 좁게 cluster. OCR 키워드는 환경-독립·deterministic.
- **출처**: `docs/vision.md:19,657`; `docs/progress-log.md:657`
- **상태**: ✅ 확정

### topK=1 강제 — top-3 평균이 N_refs 많은 brand 에 유리해 unfair
- **사실**: category 매칭 sim 을 topK=1(가장 가까운 ref 만)로 강제. top-3 평균은 broad-coverage refs(많은 N_refs)에 유리(laptop n=1 은 top-1 자동 0.66 vs coke n=4 top-3 평균 0.55 → laptop 항상 강함). topK 는 `min(topK, n_refs)` clamp, sim 은 오름차순 정렬 후 상위 k 평균. default-fallback 은 v0.7.2 에서 제거(strict — macbook 1개여도 OCR 매칭 없으면 광고 X, Dell/HP 에 macbook 광고 방지).
- **근거**: ref 수가 많으면 상위 K 평균이 더 부드럽게/높게 나와 카테고리 간 비교 불공정. K=1 은 모든 카테고리 동일 조건.
- **출처**: `HelloAR.cs:150-152`; `ProductMatcher.cs:24-26,207-215`; `docs/progress-log.md:455-456,470`; `docs/vision.md:2347`
- **상태**: ✅ 확정

### conquest 매핑: 코크 인식 → 펩시 광고, 펩시 인식 → 코크 광고 (경쟁사 mp4)
- **사실**: `CompetitorAdVideo` 딕셔너리로 인식 brand → 경쟁사 광고 mp4 매핑: coca-cola → `db/ads_video/pepsi_bottle_ad.mp4`, pepsi → `db/ads_video/coke_bottle_ad.mp4`. 매핑 없으면 brand.ad_image 경로를 `db/ads/`→`db/ads_video/`, `_ad.png`→`_ad.mp4` 치환 파생. brand.name 은 metadata.json 의 정확한 문자열이어야 함. 정상 매칭이 아니라 의도적으로 경쟁사 광고를 띄우는 게 핵심 기능.
- **근거**: 프로젝트 핵심 컨셉 conquest: 경쟁사 상품을 손에 든 순간 자사 비교 정보를 띄움.
- **출처**: `HelloAR.cs:167-174,302-318`
- **상태**: ✅ 확정

### Tier-3 객체크기 트리거 25%→5% 완화 (minAreaRatio 0.25→0.05); 25% 는 8/15 만 통과
- **사실**: spec Tier-3 '객체 ≥25% of frame'(minAreaRatio 0.25)은 너무 strict — test_cans 8/15 만 통과 → 데모는 0.05(5%)로 완화(v0.5.6). 옛 코드 default 0.01(1%, 노이즈 통과). CLIP threshold 는 Awake 에서 하드코딩(0.20→0.55→0.45)으로 Inspector override 해 FP 차단. logcat 은 ring buffer 라 success 는 aip.json(product_name)+ocr_crops/ 로 검증해야지 logcat tail 로는 안 됨.
- **근거**: 글라스 카메라는 통상 거리에서 객체가 작게 보여 25% 게이트가 대부분 reject. logcat 은 부하 시 옛 라인을 drop.
- **출처**: `docs/progress-log.md:218,222-230,376-378,409,663`; `docs/vision.md:266,653`
- **상태**: ✅ 확정

### person(class 0) 영구 차단 — 1인칭 시점에서 손/팔이 매번 잡혀 noise 🆕 (코드에서 구조)
- **사실**: `blockedClassIds = {0}`(person) — v0.3.8 사용자 손/팔이 매번 잡혀 noise 라 차단. `allowedClassIds` 는 비워둠(전체 통과, PoC 디버깅용). bbox 크기 필터 `minAreaRatio=0.05`(노션 4.5 Tier3 25%+ 보다 완화), `maxAreaRatio=0.80`. `obs_whitelist.json` 이 화이트리스트 source of truth 라 변경 시 양쪽 동기화 필요.
- **근거**: 1인칭 안경 시점에선 사용자 손/팔이 항상 프레임에 들어와 person 오검출 → 광고 가치 없는 클래스로 영구 제외(보안/프라이버시 사유 아님).
- **출처**: `spatial_anchor_test/Assets/Scripts/QnnYoloDetector.cs:36-50`
- **상태**: ✅ 확정

### w8a8 양자화로 YOLO conf 가 book 등에서 0.16~0.47 → confThreshold 0.20 으로 낮춤
- **사실**: `QnnYoloDetector.confThreshold=0.20`. w8a8 양자화로 book 같은 객체 conf 가 0.16~0.47 까지 떨어져 0.20 으로 살림. NMS `iouThreshold=0.45`. Postprocess 에 class 73(book) max conf 별도 로깅 디버그 코드 잔존.
- **근거**: AR1 에 FP16 유닛 없어 w8a8 필수 → 양자화 노이즈가 detection confidence 를 10~50% 깎아 정상 threshold 면 약한 객체가 전부 탈락.
- **출처**: `spatial_anchor_test/Assets/Scripts/QnnYoloDetector.cs:25-31,343-344`
- **상태**: ✅ 확정

### COCO 'bottle' 은 PET 병(0.89-0.95) 검출하나 캔은 0.11 로 붕괴 → 데모는 PET 병
- **사실**: YOLO11l(COCO 80-class)이 cola/pepsi PET 병은 신뢰 검출(bottle conf 0.89-0.95, bbox 12-30% area)하나 캔은 불안정: pepsi 캔 front-on = bottle/cup 0.46/0.28, can side-on-ice = cup-only 0.11. 원인: COCO 'bottle' 학습 데이터가 PET/wine 병 위주에 캔이 적음 — 캔의 짧은 둥근 윗부분이 학습된 병 모양과 다르고 측면 캔은 'cup' 분류. 데모 객체를 캔→PET 병으로 전환.
- **근거**: COCO bottle annotation 이 PET/유리병에 편향, 캔 geometry 가 그 분포 밖 + 'cup' 클래스와 겹침.
- **출처**: `docs/vision.md:1485-1554`; `docs/dev-guide.md:116,163`
- **상태**: ✅ 확정

### CRAFT detector 는 link(affinity) map 없이 region 만 쓰면 글자 단위 과분할 🆕 (코드에서 구조)
- **사실**: CRAFT 2채널 출력 region(ch0)+link(ch1)을 결합해야 글자들이 affinity 로 이어져 단어 박스가 된다. link map 을 안 쓰면 글자 단위 과분할 → recognizer 에 글자 1개 strip 이 들어가 인식 깨짐(Mac 검증: region-only 30박스 → region+link 6박스, "COCA-COLA"가 박스 1개로 병합). 마스크=`(region>=LOW_TEXT 0.4 || link>=LINK_THRESHOLD 0.4)` 4-connected component 에 peak region>=`TEXT_THRESHOLD 0.6` 필터.
- **근거**: CRAFT 의 affinity score 가 인접 글자를 한 단어로 묶음.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/EasyOCREngine.java:53-61,359-395`
- **상태**: ✅ 확정

### EasyOCR 임계는 quantized 라 CRAFT 원본(0.7/0.4)보다 보수적, MAX_BOXES=32 로 latency 폭발 방지 🆕 (코드에서 구조)
- **사실**: CRAFT 원본 text_threshold=0.7/low_text=0.4 대신 `TEXT_THRESHOLD=0.6/LOW_TEXT=0.4/LINK_THRESHOLD=0.4`(quantized 라 약간 보수적). `MIN_BOX_AREA=40`(5×8px 이하 버림), `MAX_BOXES=32`(박스 많으면 recognizer 호출 폭발 → latency 폭발). 검출 임계가 너무 낮으면 노이즈 박스 폭발로 인식기 부하↑.
- **근거**: w8a8 양자화로 score map 동작점이 달라지고, 박스 수가 recognizer 호출 횟수에 직결.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/EasyOCREngine.java:52-61,267`
- **상태**: ✅ 확정

### EasyOCR recognizer letterbox 배경은 흰색이어야 함 (detector 는 검정) 🆕 (코드에서 구조)
- **사실**: recognizer 입력 letterbox(`letterboxGray`)는 `drawColor(Color.WHITE)` 로 배경을 흰색으로 채움('EasyOCR 학습 — 배경은 흰색'). detector letterbox 는 `Color.BLACK`. 또 CRAFT 박스는 글자에 딱 붙으므로 crop 시 `bh/6`(최소 2px) padding 추가.
- **근거**: recognizer 가 흰 배경 텍스트로 학습돼 배경색 mismatch 시 인식 저하.
- **출처**: `spatial_anchor_test/Assets/Plugins/Android/EasyOCREngine.java:289-294,430-444`
- **상태**: ⚠️ 주의

### OCR 전처리: 중앙 crop 0.9 + upscale 2x → JPG 85, crop 은 회전 무관
- **사실**: `OCRExtractor cropFraction=0.9`(v0.7.5 에서 조준 어려워 라벨이 가장자리에 걸려 0.55→0.9 완화), `upscaleFactor=2.0`(범위 1~4, 멀리 있는 작은 라벨 인식률↑). `BuildOcrJpg` 는 중앙 crop 후 Bilinear Blit 업스케일 + `EncodeToJPG(85)` JNI 전달. crop 은 중심 기준이라 회전 무관. brand 는 'pepsi'/'coca-cola' 키워드 매칭이라 배경 글자 끼어도 안전.
- **근거**: 안경 조준이 부정확해 라벨이 프레임 가장자리로 밀려 strict crop 이면 라벨을 자름. 작은 라벨은 NPU recognizer 가 못 읽어 업스케일.
- **출처**: `spatial_anchor_test/Assets/Scripts/OCRExtractor.cs:24-30,265-310`
- **상태**: ✅ 확정

### OCR self-test 는 static 이미지라 rotationOverride 를 0 으로 강제 🆕 (코드에서 구조)
- **사실**: `RunSelfTest` 는 번들 라벨 카드 + adb push 한 `persistentDataPath/ocr_selftest/*.{jpg,jpeg,png}` 에 recognize 를 자동 실행해 `[OCR-SELFTEST]` 로그로 정확도를 결정적·재현가능 검증. static 이미지엔 카메라 회전 보정이 무의미해 `rotationOverride` 를 0 으로 강제했다가 끝나면 원복. b21 에선 실파이프라인 통합 측정 위해 `runSelfTest=off`.
- **근거**: 카메라/트리거/조준 없이 NPU recognizer 자체 정확도만 격리 측정하는 self-test 인데, 회전 보정 로직이 static 이미지에 잘못 적용되면 결과 오염.
- **출처**: `spatial_anchor_test/Assets/Scripts/OCRExtractor.cs:104-154`
- **상태**: ✅ 확정

### gyro 트리거 임계값: 코드 기본 0.3 rad/s·2.0s, startup 노이즈로 데모는 0.5 rad/s·1.0s 로 완화
- **사실**: `GyroTrigger` 는 `Input.gyro.rotationRateUnbiased`(rad/s)의 3축 절대값이 `stableThreshold` 이하로 `stableDuration` 지속 시 발화. 코드 기본 0.3 rad/s(≈17°/s)·2.0s 는 관대한 편. startup IMU calibration 노이즈로 첫 트리거가 2분+ 지연돼 데모는 0.5 rad/s·1.0s 로 완화(v0.5.12). 1 rad/s ≈ 57.3 deg/s.
- **근거**: 부팅 직후 자이로 bias 가 커서 maxAbs 가 threshold 초과 상태 지속 → 'held still' 조건이 분 단위로 안 만족됨.
- **출처**: `spatial_anchor_test/Assets/Scripts/GyroTrigger.cs:14-23`; `docs/dev-guide.md:215`; `docs/progress-log.md:411,493`; `docs/vision.md:266`
- **상태**: ✅ 확정

### RayNeo 정지 시 gyro 가 정확히 (0,0,0) stuck → oneShot 모드 영구 잠김 → cooldown 모드로 우회
- **사실**: RayNeo 정지 시 `Input.gyro` 가 정확히 (0,0,0)으로 stuck → `GyroTrigger oneShotPerStableWindow=true` 모드에서 `firedInThisStableWindow` 가 영구 true → 첫 trigger 후 재발화 안 됨. v0.8.1 에서 oneShot false + `triggerCooldown=5.0s` 강제(cooldown 모드)로 우회. 트리거 셋팅: threshold 0.5 rad/s, duration 1s, cooldown 5s.
- **근거**: stable window 가 gyro 변화로 리셋돼야 재발화하는데 값이 0 고정이면 window 가 영원히 안 깨짐.
- **출처**: `spatial_anchor_test/Assets/Scripts/GyroTrigger.cs:84-101,112-121`; `HelloAR.cs:116-124`; `docs/integration_log.md:114`; `docs/spatial-anchor-handoff.md:35`
- **상태**: ✅ 확정

### RayNeo X3 Pro 자이로 하드웨어: lsm6dsr 6축 IMU, type=4, rad/s, 최대 415.97Hz·기본 50Hz 🆕 (코드에서 구조)
- **사실**: RayNeo X3 Pro 센서(2026-06-06 adb 검증): lsm6dsr STMicro 6축 IMU, `android.sensor.gyroscope type=4`, 단위 rad/s, 최대 415.97Hz 이나 기본 사용 50Hz(`com.probe.imu` 와 동일). `GyroTrigger` 의 `gyroUpdateInterval 0.02=50Hz` 도 이에 맞춤.
- **출처**: `spatial_anchor_test/Assets/Scripts/GyroTrigger.cs:10-15`
- **상태**: ✅ 확정

---

## 🧩 기타

### new InputSystem-only 환경에서 legacy StandaloneInputModule 을 Awake 에서 비활성화 안 하면 매 프레임 throw → 렌더 정지
- **사실**: ARDK 'XR Plugin' prefab 의 EventSystem 이 legacy `StandaloneInputModule` 을 쓰는데 PlayerSettings `activeInputHandler=1`(New Input System only)이면 `UpdateModule()` 이 매 frame `InvalidOperationException` → render pipeline 진행 안 됨(splash 만 보임). `activeInputHandler=2`(Both)는 Unity 가 'Android 에서 unsupported' 경고. 우회 = Awake 에서 `FindObjectsOfType<StandaloneInputModule>()` 모두 `.enabled=false`.
- **근거**: `StandaloneInputModule` 이 legacy `UnityEngine.Input` 을 읽는데 Input System 패키지로 스위치돼 매 Update 예외.
- **출처**: `spatial_anchor_test/Assets/Scripts/SpatialAnchorTest.cs:120-125`; `spatial_anchor_test/JOURNEY.md:48-60`
- **상태**: ⚠️ 주의

### AIP(AmbientInterestProfile) 스키마: COCO class_id + Unix ms timestamp, N event 마다 disk write, oldest-drop ring buffer
- **사실**: `AIPEvent` = {timestamp_ms(Unix epoch ms), class_id(COCO 0~79), class_name, confidence(YOLO conf), duration_sec(단일 frame 만이면 0), product_name(CLIP 매칭, null 가능), product_sim(미매칭 -1)}. `AIPProfile.version=1`. `saveEveryNEvents=5` 마다 disk write, `maxEvents=1000` 초과 시 oldest drop. OnApplicationQuit/OnApplicationPause(p==true)에서도 Save. clip-only 경로는 class_id=-1, label='(clip-only)'. v1 은 스키마 정의만, 실제 활용은 v2+. 원본 frame 외부 전송 금지(노션 10.1).
- **근거**: 100% 온디바이스 프라이버시 원칙. 광고를 안 띄우는 passing-by attention 누적 데이터가 v2+ 광고 경매 단가 boost 의 자산.
- **출처**: `AmbientInterestProfile.cs:21-40,47-53,114-115`; `HelloAR.cs:335-350`
- **상태**: ✅ 확정

### Windows repo 는 unity_db.json 없음 → metadata.json(array-root)+.npy 를 런타임 평탄화 🆕 (코드에서 구조)
- **사실**: Windows repo 에는 Mac `build_*_db.py` 산출물 `unity_db.json` 이 없고 `metadata.json`(array root)+분리된 `.npy` 만 존재. `ProductMatcher` 가 JSON 첫 글자가 `[` 면 metadata schema 로 감지해 .npy 를 UnityWebRequest 로 읽어 DbRoot 로 변환. JsonUtility 는 top-level 배열 미지원이라 `{"items":[...]}` 로 감싸 파싱. 변환 후 model=mobileclip-s2, dim=동적, schema_version=metadata-v1.
- **근거**: Unity JsonUtility 는 루트 JSON 배열을 역직렬화 못 함(객체 래핑이 표준 우회). Mac 빌드 산출물이 Windows 에 없어 어댑터 필요.
- **출처**: `ProductMatcher.cs:15-18,128-143,350-353,383-391`
- **상태**: ✅ 확정

### 수작업 numpy V1.0 .npy 파서 — 10B 헤더, fortran_order 미지원, shape 정규식 없이 파싱 🆕 (코드에서 구조)
- **사실**: `ProductMatcher` 가 자체 .npy 리더 구현: 6B magic `\x93NUMPY` + 1B major + 1B minor + 2B header_len(LE) + ASCII header, shape=(n,dim) float32 만 지원. header 의 `'shape':(n,dim)` 을 정규식 없이 IndexOf/Substring/Split 로 파싱, `'fortran_order': True` 면 예외(미지원), body 는 `Buffer.BlockCopy` 직접 복사(bodyOffset=10+header_len). category embeddings_flat 길이가 n_refs*dim 과 불일치하면 경고만 하고 그 ref skip.
- **근거**: C# 에 numpy 리더 없고 NuGet 도입 회피. .npy 포맷이 단순(고정 헤더)해 직접 파싱이 실용적.
- **출처**: `ProductMatcher.cs:447-493,196-197`
- **상태**: ✅ 확정

### 입력 프로파일은 Ring/CellPhone/Eye 3종 + Head(미등록); Home 버튼이 thumbstick/click 경로에 매핑
- **사실**: `RayNeoControllerProfile` 은 RayNeoRingController(homeButton+devicePose), RayNeoCellPhoneController(devicePose), RayNeoEyeController(eyePos)를 등록하고 AddHeadActionMap 은 주석처리(미등록). userPath: ring=`/user/ring`, cellphone=`/user/cellphone`, eye_gaze=`/user/eyes_ext`. homeButton 바인딩=`/input/thumbstick/click`, gaze=`/input/gaze_ext/pose`, grip=`/input/grip/pose`. 표준 버튼/트리거/thumbstick 대부분 주석처리.
- **근거**: RayNeo 컨트롤러가 실제 노출하는 입력은 Ring grip+Home, 폰 pose, eye gaze pose 로 제한적.
- **출처**: `.../OXR/Runtime/Scripts/OpenXR/RayNeoControllerProfile.cs:304,386-388,449-455,610-616`
- **상태**: ✅ 확정

### Ring 입력은 OpenXR 프로파일이 아니라 AndroidJavaProxy IPC 콜백으로 받아 InputSystem 에 수동 큐잉 🆕 (코드에서 구조)
- **사실**: `RingManager.OpenRing()` 이 `InputSystem.AddDevice<RingInputDevice>()` 후 Java `RingManager`(`SupportPackagePath+".ipc.ring.RingManager"`)에 `RingListener`(AndroidJavaProxy) 등록. `OnTouch(eventType,x,y)`/`OnRotation`/`OnTouchPadPressChange` 가 native 에서 와 `RingDeviceState`(FourCC 'RING')의 button bit0(touch)/bit1(heavy)/devicePose/touchPosition 을 수동 갱신. RingTouchType: UnityTouchEvent=0/CustomTouchEvent=1/BothTouchEvent=2. OnTouch eventType 은 Android MotionEvent 코드(DOWN=0,UP=1,MOVE=2,CANCEL=3,POINTER_DOWN=5,POINTER_UP=6).
- **근거**: 링은 BLE/IPC 디바이스라 OpenXR 컨트롤러 채널 대신 벤더 Android IPC + 커스텀 InputDevice 로 브릿지.
- **출처**: `.../SDK/Runtime/Scripts/APIs/RingManager.cs:24,37-106,138-211`; `Comps/Inputs/Ring/RingInputDevice.cs:96-181`
- **상태**: ✅ 확정

### 페어링된 폰의 GPS 스트림을 받는 IPC 채널 존재 — 위치기반 광고 컨텍스트용(미사용) 🆕 (코드에서 구조)
- **사실**: `IPC.OpenPhoneGPS()`/`ClosePhoneGPS()` + `PhoneGPSListener`(AndroidJavaProxy)의 `OnGPSPush(long time, double lat, lon, alt, speed, horAccuracy, verAccuracy)` / `PushStateChange(int code, msg)` 콜백(상태 PHONE_CONNECTED=0 / NOT_CONNECTED=1 / REGIST_PUSH=2 / PUSH_TIME_OUT=3)으로 글라스 앱이 **페어링된 스마트폰의 GPS 스트림**을 받는다(`PhoneManager.Call("OpenPhoneGPS")` + `RegCallBack`). 현재 미사용.
- **근거**: conquest/광고 시스템에서 위치기반 입찰·매장 컨텍스트는 자연스러운 v2 자산(AIP 누적과 같은 결) — 이 위치 채널이 SDK 에 이미 존재.
- **출처**: `.../SDK/Runtime/Scripts/APIs/IPC.cs:28-69`
- **상태**: 🔬 추정

### WatchGestureUtil.startDelayMoveDown: 단/더블클릭 후 호출해야 워치 슬라이드가 안 끊김(권장 1000ms) 🆕 (코드에서 구조)
- **사실**: `WatchGestureUtil` 은 Java `SupportPackagePath+".WatchGestureUtil"` 의 static `startDelayMoveDown(delayMillis)` 호출(주석: '단/더블클릭 이벤트 실행 후 startDelayMoveDown 을 부르면 워치 슬라이드가 끊기지 않음, 건의 1000ms').
- **근거**: 워치 단/더블클릭이 후속 슬라이드 스트림을 중단시켜 지연 보정 호출 필요.
- **출처**: `.../SDK/Runtime/Scripts/APIs/WatchGestureUtil.cs:34,43-63`
- **상태**: ⚠️ 주의

### SetGlassLegTouchEventExchange 로 좌우 안경다리(temple) 터치 이벤트 swap 가능 🆕 (코드에서 구조)
- **사실**: `PlatformAndroid.SetGlassLegTouchEventExchange(bool isExchanged)` 가 CurActivity 의 동명 메서드를 호출해 양쪽 镜腿(temple) 터치 이벤트 좌우 교환. false=교환 안 함, true=교환.
- **근거**: 착용 방향/손잡이에 따라 좌우 temple 입력을 뒤집을 수 있게 한 벤더 옵션.
- **출처**: `.../SDK/Runtime/Scripts/APIs/PlatformAndroid.cs:124-131`
- **상태**: ✅ 확정
