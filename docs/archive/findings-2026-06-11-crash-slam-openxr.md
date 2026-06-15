# 2026-06-11 조사 결과 — 디바이스 크래시 / SLAM 8Hz / RayNeo OpenXR 표면

> ↩ 실험 history 인덱스: [EXPERIMENTS.md](EXPERIMENTS.md) — 이 문서는 빌드 **b17** 의 진단 상세.

> 이 문서는 2026-06-11 세션의 심층 조사(서브에이전트 워크플로우 4건 + 온디바이스 logcat 포렌식)를 한 곳에 박제한다. **다른 개발자/Codex가 시간 손해 없이 이어가기 위한 단일 출처.**
> 표기: **[확정]** = 코드/.so/로그 직접 증거로 결론남(디바이스 불필요). **[측정필요]** = 디바이스 1회 관측으로만 닫히는 미지수.
> 관련: [`spatial-anchor-handoff.md`](spatial-anchor-handoff.md)(빌드 런북·changelog), 루트 [`../AGENTS.md`](../AGENTS.md).

---

## TL;DR (한 문단)

앱 실행 시 **기기가 꺼졌다 재시작**하는 건 **공유 Hexagon CDSP 충돌** 때문이다 — 우리 CLIP(QNN HTP)이 RayNeo SLAM(FFVINS)과 같은 CDSP를 써서 CDSP user-PD를 터뜨리고(SSR), XR 런타임의 FastRPC 핸들이 영구 stale → `system_server` 사망 → 프레임워크 재시작 → 리부트로만 복구. **수정 = CLIP을 CPU(XNNPACK)로 내려 CDSP를 안 건드리기**(코드 준비됨, `useNpu=false`). 별개로, world-anchored 콘텐츠가 **~8Hz로 끊기는 건 고칠 수 있는 "레이트"가 아니라 RayNeo 런타임 고정 FFVINS 갱신율**이고, ATW가 **회전전용**(depth 미제출 + 컴포지터에 spacewarp 경로 없음)이라 **병진(translation)이 재투영 안 돼** 생기는 parallax 문제다. 8Hz 숫자는 거의 못 올리지만, **parallax 완화 + gyro 회전보간**으로 체감 끊김은 줄일 수 있고, **단 1빌드(b17)**로 크래시·DSP경합·8Hz·카메라경합을 전부 검증한다.

---

## 1. 디바이스 크래시 — 공유 CDSP 충돌 **[확정: strongly-supported]**

### 증상
- 앱 실행 → **검은 화면(Unity 로고도 안 뜸)** + 곧 **기기 꺼졌다 재시작** → `adb reboot` 해야 복구.
- **우리 앱만** 그럼(벤더 큐브 데모는 멀쩡). **리부트 후 첫 실행은 정상, 이후 실행부터 크래시.** (사용자 관찰)
- 이전 버전에서도 가끔, b15/b16에서 거의 매번.

### 직접 증거
| # | 사실 | 출처 |
|---|---|---|
| 1 | `com.rayneo.xr.runtime`가 `remote_handle64_invoke failed ... method 12 on domain 3` ~30회/초 폭주 | logcat 03:45:23~ |
| 2 | → `DeadSystemException: The system died`(system_server 사망 → 프레임워크 재시작) | logcat 03:45:52 |
| 3 | 프로세스/앱 재시작 무효, **리부트로만 복구** | logcat 다중 런치 + `/proc/uptime` |
| 4 | 폭주 시작이 우리 앱 첫 런치보다 **27초 앞섬** | logcat 타임스탬프 |
| 5 | 캡처 전체에 우리 CLIP/QnnDelegate/Interpreter 로그 **0줄**(그 에피소드들에서 CLIP 실행조차 안 됨) | logcat 부재 |
| 6 | 우리 manifest가 **`libcdsprpc.so` 명시 선언**(QnnClipEngine HTP가 dlopen) | `AndroidManifest.xml:24` |
| 7 | QnnClipEngine HTP_BACKEND + BURST, **prebuilt 캐시 없음** → 매 콜드런치 풀 HTP 컴파일(~125초) | `QnnClipEngine.java`, 파일시스템 0건 |
| 8 | `clipOnlyMode=true` 강제 → **CLIP이 우리의 유일한 CDSP 클라이언트**(YOLO 미부착) | `HelloAR.cs:135,142` |

> `domain 3` = CDSP(Compute DSP). `vendor.cdsprpcd` 데몬 존재 확인.

### 인과 체인 (위 증거의 유일한 일관 설명)
벤더 데모(SLAM, NPU 미사용)=멀쩡 + 우리만 CDSP 사용 + 우리만 크래시 → **CDSP에 우리가 얹는 게 트리거**. 첫 실행 OK → **공존 자체는 됨**(깨끗할 때). 이후 크래시 + 리부트로만 복구 → **우리 첫 실행이 CDSP를 종료 후에도 남는 깨진 상태로 만듦**(세션 미해제 / CDSP user-PD SSR). 캡처는 이미 깨진 CDSP로 진입한 "이후" 런치의 cascade.

> 정밀 표현: "DSP 리소스 **경쟁**"이 아니라 **"공유 CDSP 세션 정리 실패/오염"**. 첫 실행이 되므로 동시-경쟁 실패가 아니라 사후-오염이다.

### 증폭 원인 ("가끔→매번")
b14→b16에서 바뀐 **유일한 XR 런타임 변수 = b15의 `ATWSupport 0→1`**(`OpenXR Package Settings.asset:671`). ATW=1은 컴포지터가 매 스캔아웃마다 SLAM pose로 reproject → 같은 CDSP FastRPC를 상시 실시간-마감 상태로 올림 → 예전엔 흡수되던 CDSP hiccup이 dead-handle로 직결. (b16은 HUD/로그만 추가 = DSP 무관.)

### 수정 (우선순위)
1. **근본**: CLIP을 **CPU(XNNPACK)**로 — `useNpu=false`. 우리 CDSP 사용 0 → cascade 구조적 차단 + **125초 컴파일 소멸**. **코드 준비됨**(아래 §5). CLIP은 트리거당 단발 256²라 CPU 수십~수백 ms로 충분.
2. (불필요해질 가능성) ATW=0 폴백 — CPU 전환으로 CDSP가 비면 ATW=1 유지해도 안전. 드물게 또 꺼질 때만.

### GPU는 왜 안 쓰나 **[확정]**
- `libQnnGpu.so` 동봉 + QnnDelegate에 `GPU_BACKEND` 있음(1줄 스왑 가능). **그러나 QNN GPU 백엔드는 INT8 미지원**(FP32/FP16 전용, `GpuPrecision` enum에 INT8 없음). 우리 모델은 INT8 → **FP16 재export + 재번들 + 3-way enum 필요** = 별도 워크스트림, b17에 공짜로 못 얹힘.
- git 이력상 **CLIP은 GPU에서 돈 적 없음**. GPU(Unity Sentis GPUCompute/Adreno)에서 돈 건 초기 YOLO뿐, 그것도 **~200ms대**(NPU YOLO11l ~15-30ms 대비 7-20배 느림). 즉 Adreno 추론은 느리고, CLIP GPU 이점도 불확실. → **CPU가 정답.**

### 디바이스 진단 체크리스트 (ADB 복귀 시, reboot 사이마다)
0. `adb reboot` → `adb shell uptime`로 부팅 직후 확인(리부트만 CDSP 클린 복구). `pm clear` 1회.
1. `adb logcat -v threadtime -s adsprpc cdsprpcd RayNeoXR FFalconXRClient AndroidRuntime UnityOpenXrActivity` 를 launch **전부터** 켠다. 시그니처: `fastrpc_apps_user.c:1513 Error 0x27 ... method 12 on domain 3` ~30회/초.
2. cascade 순서 확인: 0x27 flood → `DeadSystemException` → `FFalconXRClient.loadProfile NPE` → `Need to set FrameLayout`/`openxr session had not being inited`. flood가 우리 launch보다 앞서면 = 이전 실행이 깨놓은 것 → reboot 후 재실행.
3. `adb shell ps -A | grep -E 'cdsprpcd|rayneo.xr.runtime'` 생존, `ls /sys/class/remoteproc/`로 CDSP SSR 흔적.
4. `adb shell ls -lt /data/tombstones/` 최신 tombstone → backtrace에 libcdsprpc/fastrpc 프레임 확인.
5. **Rung A 확정**: b17(CPU CLIP) reboot 후 ≥5회 launch → `QnnDelegate (HTP)` 로그 없음 + `Interpreter init` sub-second + 0x27 flood 없음 + 0/5 크래시 → **공유-CDSP 충돌 = root cause 100% 확정 + 동시 수정**.

---

## 2. SLAM ~8Hz judder — 레이트가 아니라 reprojection/parallax **[확정]**

### 확정 진단
1. Unity는 head pose를 **단 한 경로**로 받음: OpenXR centerEye → `TrackedPoseDriver` → `RayNeo.HeadTrackedPoseDriver.SetLocalTransform` → `OnPostUpdate` (`HeadTrackedPoseDriver.cs:47-54`). (대체 폴링 `RayNeoApi_GetHeadTrackerPose`는 **.so에 미export = 죽은 심볼**, `SpatialAnchorTest.cs:103-105` 무효.)
2. base driver는 **UpdateAndBeforeRender**(매 렌더 프레임 centerEye 재read)인데도 pose **변화율이 ~8Hz**(headPoseCallCount, `SpatialAnchorTest.cs:190-194`) — 즉 런타임이 **솔버 틱 사이엔 같은 latched FFVINS 맵 pose 반환**. **8Hz = FFVINS 맵 pose 전달 케이던스, Unity throttle 아님, 설정 노브 없음**(`SetBasicXRConfigs`는 ATW/renderMode/depthSubmissionMode/trackerAlgorithm만; 어떤 .so에도 rate/Hz 키 0건).
3. 유일한 틱-사이 보간 = **ATW, 회전전용**. `depthSubmissionMode:0`(`OpenXR Package Settings.asset:628`) + **RayNeo 컴포지터에 spacewarp/positional 경로 자체가 없음**(.so 5개+classes.jar에서 spacewarp/motionvector/reproject/depth 토큰 **0건**). → 회전은 매끄럽게 리워프, **위치(translation)는 8Hz로 그대로 끊김**.
4. 우리 콘텐츠는 **0.5~1.2m 코앞 단일 quad = parallax 최대**(`SpatialAnchorTest.cs:15,19`) → 끊김 최대 가시.
5. **벤더 큐브 데모는 더 빠른 게 아님** — 동일 `EnableSlamHeadTracker`, 동일 8Hz. 멀리 깔린 큐브 100개 = parallax 작음 + CLIP/카메라 부하 0.

**→ judder = 회전전용 ATW(depth off, no spacewarp)가 translation을 미재투영 + 최대 parallax. reprojection/parallax 문제이지 설정으로 고칠 레이트 문제가 아님.**

### 레버 랭킹
| 레버 | 효과 | 빌드 |
|---|---|---|
| **parallax 완화**(콘텐츠 멀리/분산) | 레이트 0변화, **체감 끊김 대폭↓**(벤더가 매끄러운 이유). 단 conquest 데모 구성과 트레이드오프 | no-build / b17 field |
| **gyro 회전 오버레이**(틱 사이 회전 보간) | 회전 **거의 풀픽스**, 네이티브 0줄(~1-2h). translation은 여전 | b17 fold-in |
| **CLIP→CPU**(=크래시 수정) | 주효과 안정성. 레이트 0~+2~3Hz(대개 ~0) | b17 |
| ~~depth 제출~~ | **무효**(positional 경로 없음) + fps 손실 + 컴포지터 죽을 위험 | ❌ OFF |
| ~~trackerAlgorithm DOF3~~ | 회전↑지만 **6DoF translation 상실 = 월드앵커 파괴** | ❌ |
| ARDK ≥1.1.7 버전매치 | **0Hz 블로커** 해결(8Hz와 별개). portal 계정 | 별도(device) |
| 네이티브 IMU 브리지 | translation 끊김 고칠 **유일** 수단, 고난도·drift·1.1.7 선결 | 최후 escalate |

### [측정필요] 남은 미지수 2개 (각 1관측으로 닫힘)
1. **CPU에서 8Hz가 오르나?**(우리 DSP 부하가 FFVINS를 눌렀나) → b17의 `useNpu` A/B로 `headPoseCallCount/초` 비교(status==1일 때만). 오르면 우리 부하 탓, ~8 그대로면 런타임 고유 floor.
2. **버전매치 런타임에선 다른가?** 네이티브 클라이언트 1.1.6 vs 기기 런타임 1.1.7.9 mismatch → handshake 실패 시 **0Hz 블로커**(camPos (0,0,0), -30001). 8Hz와 별개 문제. ARDK ≥1.1.7 flash로만 닫힘(코드 무관).

---

## 3. RayNeo OpenXR이 정확히 제공/미제공하는 것 **[확정, high]**

(C# 소스 + .so 5개 nm -D/strings + OpenXR settings 교차검증)

### 제공함
- **헤드/SLAM**: 6DoF FFVINS on/off/리센터/상태(`EnableSlamHeadTracker`/`Recenter`/`GetHeadTrackerStatus`). 포즈 값은 `OnPostUpdate` 이벤트로만. 보조 `NineAxisAzimuth`(yaw).
- **카메라**: ShareCamera RGB(cam0) 연속 + **단발 TakePicture**, VGA(cam1=SLAM 스테레오), 해상도/방향, 네이티브 rotate/mirror/scale. **하드코딩 intrinsics: fx376.686 / fy376.1188 / cx319.3743 / cy241.355**.
- **렌더/ATW**: Single Pass 스테레오, ATW 회전전용.
- **입력**: Ring(grip pose+Home), 폰 컨트롤러, **시선(gaze_ext pose)**.
- **평면검출**: `EnablePlaneDetection`/`GetPlaneInfo` + 폴리곤 메시 + 수평/수직 분류 — **제공되나 앱 미사용**.
- **시스템**: SendCommand/SetProp/GetProp, 밝기, CPU온도, 오디오포커스, 폰GPS, 얼굴검출.
- **로더/부트**: boot.config 자동 init, OpenGLES3 강제(Vulkan ❌).

### 제공 안 함 (= 막힌 것)
- ❌ **VIEW-space 컴포지션 레이어** → 진짜 고정 HUD 불가(TextMesh parent로 흉내만, SLAM 따라 흔들림).
- ❌ **예측/직접 포즈 폴링** → `RayNeoApi_GetHeadTrackerPose` C# 선언만, .so 미export(EntryPointNotFound).
- ❌ **depth 제출 / positional spacewarp** → 회전전용 ATW만 = §2 8Hz judder 근본.
- ❌ **SLAM 컴퓨트 배치 제어** → GPU/DSP 못 옮김(trackerAlgorithm 3DOF↔SLAM 선택만).
- ❌ **공간/영속 앵커** → 앱 자체 앵커링.
- ❌ **IMU 콜백** → `RayNeoApi_RegisterIMUEventCallback` .so export되나 C# 미바인딩. 앱은 Unity `Input.gyro` 사용.
- ❌ 핸드트래킹/foveation/scene mesh/이미지트래킹/패널 리프레시/표준 eye-gaze 확장.

### 통제 경계
만질 수 있는 면 = `libRayNeoXRUnityInterfaces.so` export + C# 선언된 `RayNeoApi_*` ABI까지. 그 너머(컴포지터 프레임루프·리프로젝션·포즈 타이밍/예측·SLAM 배치·패널) = 벤더 블랙박스. export-but-unbridged(IMU/Mag) 또는 declared-but-not-exported(GetHeadTrackerPose)는 **자체 네이티브 shim AAR 없이는** 벽 너머.

### ★ 안 쓰던 기회 3개 (데모에 당길 수 있음)
1. **카메라 intrinsics 노출** → 미뤘던 "광고를 객체 실제 depth에 배치"(핀홀 역투영) **구현 가능**(bbox+intrinsics로 거리 추정).
2. **평면검출 제공되나 미사용** → 광고를 **실제 책상 표면에 앵커** 가능 → parallax 자연 증가로 §2 8Hz judder까지 완화(시너지).
3. **시선 pose(gaze_ext)** → 현 IMU 자이로 트리거를 실제 시선 pose로 업그레이드 가능.

### 주의 — 펌웨어 게이팅
독립 리뷰(skarredghost 2025-12): X3 Pro가 다수 앱에서 "현재 3DOF만". 6DoF가 펌웨어 버전 게이팅일 수 있음(1.1.6↔1.1.7.9 mismatch + 0Hz 블로커와 연결). 우리 기기가 6DoF 안정 surfacing하는지는 실측(b17에서 `raw==1` 확인).

---

## 4. Unity 6 = NO (재확인) **[확정]**

진짜 고정 HUD를 위한 composition layer는 Unity-6 전용 패키지인데, ① RayNeo ARDK 1.1.2가 Unity 6 비호환(검은화면, `setFrameLayout(UnityPlayer)` 계약 제거됨), Unity6 호환 ARDK 부재(2026.6) ② RayNeo 런타임이 앱 제출 레이어 자체를 안 받음. → 비용 4~8일·위험 very-high·작동 SLAM 파괴. **추진 금지.** 유일 대안은 Unity 2022 커스텀 OpenXRFeature(xrEndFrame VIEW-space quad)+1h 스파이크지만, §3에서 런타임이 secondary layer 미지원 확인 → 사실상 막힘.

---

## 5. ★ 통합 최소-빌드 계획 — 단 1빌드(b17)

크래시 수정 + SLAM 경합 측정 + 카메라 측정 + 8Hz 측정이 **전부 b17 하나**로 수렴.

### b17에 들어간/들어갈 것
- **[코드 준비됨]** CLIP CPU 경로: `QnnClipEngine.java`에 `initializeCpu` + `initInternal(...,boolean useNpu)`(HTP delegate를 `if(useNpu)`로 감쌈, else `delegate=null`=XNNPACK). `ClipExtractor.cs`에 `public bool useNpu=false`(기본 CPU) + init 분기. → **빌드/디바이스 검증 대기**.
- **[fold-in 예정, 저위험]** `Application.targetFrameRate=90`+`QualitySettings.vSyncCount=0`(Awake) = 측정 깨끗하게. `[MONITOR]`에 `poseHz` 필드. 카메라 연속/단발 토글. (선택) gyro 회전 오버레이.
- **[넣지 말 것]** depthSubmissionMode(이 런타임 무효+위험). trackerAlgorithm DOF3(월드앵커 파괴).

### 디바이스 테스트 시퀀스 (ADB 복귀 시)
- **A (0빌드)**: 기존 b16 reboot 후 logcat 캡처 → §1 인과 박제(0x27 flood가 CLIP 라이프사이클과 맞물리는지).
- **B (1빌드 = b17)**: reboot 후 ≥5회 launch. 크래시 0/5 + `Interpreter init` sub-second 확인 = 크래시 수정 + 원인 재확인. 이어서 `useNpu` true/false A/B로 §2 8Hz가 CPU에서 오르는지, 카메라 토글로 카메라 경합 측정.

> 빌드 0회로 여기까지 분석 완료. b17 1빌드가 크래시·DSP경합·8Hz·카메라경합을 한 번에 검증.
