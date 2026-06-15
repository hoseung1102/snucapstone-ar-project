<!-- ⭐ 현황의 유일 정본(Single Source of Truth). 다른 문서의 "현재 상태" 서술은 본문을 복제하지 말고 이 파일을 링크한다. -->
<!-- 갱신 규칙: 빌드/실험을 끝낼 때마다 아래 표를 코드 file:line 기준으로 재확인하고 날짜 헤더를 올린다. history(시도/실패)는 EXPERIMENTS.md, 결정 배경은 vision.md 결정 로그. -->

# 📍 Eagle Eye — 현재 상태 (SoT)

> **이 파일이 현황의 유일 정본이다.** 다른 문서(`vision.md`, `dev-guide.md`, `client-spec.md` 등)의 "현재 상태/스냅샷"은 여기를 가리키는 포인터일 뿐, 본문을 복제하지 않는다. 현황이 바뀌면 **여기만** 고친다.
>
> - 지금까지 뭘 시도했고 뭐가 실패했나(빌드별 history) → [`EXPERIMENTS.md`](EXPERIMENTS.md)
> - 왜 이렇게 결정됐나(결정 배경) → [`vision.md`](vision.md) 끝의 "기술 결정 로그"
> - 코드가 실제로 어떻게 빌드·동작하나 → [`dev-guide.md`](dev-guide.md)

**기준일: 2026-06-16**

신뢰도 배지: 🟢 git검증(인용 해시가 git에 실재·일치) · 🟡 코드검증(코드 file:line 으로 확인) · 🟠 재구성(해시 부재, APK 타임스탬프/주석 기반) · 🔴 stale·모순.

---

## 1. 권위 사실 표 (authoritative facts)

> 경로는 모두 레포 루트 기준. 코드 위치는 rename 후 `glasses-app/` 기준.

| 항목 | 현재 값 | 출처 (file:line / 상수명) | 신뢰도 |
|---|---|---|---|
| **빌드 태그** | `b28-mockup-clean` (데모 목업 모드; 실 파이프라인 복귀 = `mockupMode=false`) | `glasses-app/Assets/Editor/BuildSpatialAnchorTest.cs:28` (`BUILD_TAG`) · `glasses-app/Assets/Scripts/HelloAR.cs` (`mockupMode`/`mockupAssets`) | 🟢 / 🟡 |
| **최신 빌드 마일스톤** | b28-mockup-clean (컨셉 데모영상용 목업, commit `958f3ed`). 직전 앱 빌드 = b26 | [`EXPERIMENTS.md`](EXPERIMENTS.md) | 🟢 |
| **패키지** | `com.eagleeye.helloar` | `BuildSpatialAnchorTest.cs:22` (`PACKAGE_NAME`) → `:264` `SetApplicationIdentifier` · `glasses-app/ProjectSettings/ProjectSettings.asset` `applicationIdentifier` | 🟡 |
| **브랜치** | `main` (단일 main 원칙; 옛 `feature/*`·팀원별 브랜치는 통합 완료) | 레포 정책 (루트 `CLAUDE.md`) — 현재 이 작업은 `worktree-repo-restructure` 에서 진행 중이나 **정본은 main** | 🟢 |
| **매칭 아키텍처** | **color-brand**: CLIP 으로 category 분류(콜라 vs 노트북) → cola 면 평균 RGB 통계로 brand 판별(코카콜라=red / 펩시=blue). `clipOnlyMode=true` 강제, `enableClipBrandFallback=false`, OCR 은 `skipOcr=true` 로 비활성(보조 경로로만 코드 잔존) | `glasses-app/Assets/Scripts/HelloAR.cs:135` (`clipOnlyMode=true` 강제) · `:149` (`enableClipBrandFallback=false`) · `:72` (`skipOcr=true`) · `:86` (`brandDisambiguator="color"`) · `:365`~`:392` `ResolveBrandByColor` | 🟡 |
| **카메라** | RayNeo **ShareCamera** (RGB parallel channel). RawImage 자동바인딩 대신 `_handler.texture` 직접 노출. WebCamTexture 는 SLAM 활성 시 black frame 이라 폐기 | `glasses-app/Assets/Scripts/CameraPreview.cs:4-5` (`using RayNeo.API` / `com.rayneo.xr.extensions`) · `:141` `ShareCamera.OpenCamera` | 🟡 |
| **SLAM** | RayNeo OpenXR ARDK **v1.1.2**, `XRInterfaces.EnableSlamHeadTracker()` + `HeadTrackedPoseDriver.OnPostUpdate` 구독 (vendor `SlamDemoCtrl` 최소 시퀀스) | `glasses-app/Assets/Scripts/SpatialAnchorTest.cs:139` (`EnableSlamHeadTracker`) · `:148` (`OnPostUpdate`) | 🟡 |
| **기기** | RayNeo X3 Pro — Snapdragon AR1 Gen 1, Hexagon **v73** NPU, **FP16 유닛 없음** → w8a8 양자화 필수, Android 12 | 하드웨어 사양 · [`platform-rayneo.md`](platform-rayneo.md) | 🟢 |
| **추론 런타임** | TFLite Interpreter + Qualcomm QNN delegate (Maven 2.47.0), MobileCLIP-S2 INT8 · YOLO11l w8a8 640²(dormant, clipOnlyMode) | [`dev-guide.md`](dev-guide.md) §4 | 🟡 |

---

## 2. 현재 데모 / 다음 작업 / 알려진 이슈

- **현 데모 시나리오**: 코카콜라/펩시 **페트병** + **노트북** (conquest). 라면·마트는 원 기획 시나리오일 뿐 — 방향을 마트 앱으로 좁히지 말 것 ([`vision.md`](vision.md) §1.2).
- **brand 확정 = color 휴리스틱**: cola category 안에서 평균색으로 코크(빨강)/펩시(파랑)를 가른다. ⚠️ **일반해 아님** — coke/pepsi 색이 우연히 달라서 됨. 30 SKU·동색 브랜드엔 실패 → v1.5+ fine-tuned CLIP / 로고 detector 로 대체 예정.
- **OCR 은 보조 경로로만 잔존**: `skipOcr=true` 라 엔진 init 자체를 안 함(159s HTP compile/CDSP 미발생). `brandDisambiguator != "color"` 인 다른 category 에서만 OCR + CLIP fallback 경로 가용 (코드 `HelloAR.cs:262`). Coca-Cola 필기체는 OCR·CLIP-brand 둘 다 못 읽어서 color 로 전환된 것.
- **알려진 이슈**:
  - 성공률이 **조준**(병을 카메라 중앙·정면, 배경 혼입 최소)에 의존 — color 통계는 배경이 섞이면 무너짐.
  - 온디바이스 CLIP sim 이 Mac 오프라인 대비 ~0.3 낮음(전처리/양자화 차이) — 근본 규명 미완.
  - 카메라 FOV ≠ 사용자 시선(카메라가 아래·팔 방향) — 정면 눈높이 객체가 프레임 밖일 수 있음.
- **다음 작업**: ① 조준 가이드(중앙 박스) + 타이트 center crop 으로 color 안정화 ② 온디바이스↔Mac 0.3 격차 근본 규명 ③ fine-tuned CLIP(v1.5+) / 다국어 OCR.

> 빌드/실행 명령은 [`dev-guide.md`](dev-guide.md) §3, 플랫폼 제약(ADB/미러링/NPU/권한)은 [`platform-rayneo.md`](platform-rayneo.md).
