# CLAUDE.md

Eagle Eye — AR 글라스(RayNeo X3 Pro)용 온디바이스 광고/정보 인프라 시스템의 v1 PoC.
"경쟁사 상품을 손에 든 순간 비교 정보를 시야에 띄운다"(conquest)가 핵심. 코드는 Unity(C#) + Android native(Java) + 온디바이스 TFLite/QNN 추론.

## 문서를 읽는 법 (중요)

프로젝트 맥락은 `docs/` 5개 `.md` 에 있다. **`docs/` 가 노션을 대체한 source of truth** (노션은 2026-06-08 폐기). 갱신은 항상 노션이 아니라 이 파일들에 직접 한다.

**읽는 순서:**
1. **현황만 빠르게** → [`docs/vision.md`](docs/vision.md) 맨 위 **"⚡ 현재 상태 스냅샷"** 한 섹션. 현재 버전·매칭 아키텍처·다음 작업이 여기 다 있다.
2. **시스템이 뭔지 / 왜 이렇게 결정됐는지** → [`docs/vision.md`](docs/vision.md) 전체 (비즈니스 정체성 §1~3, v1 사양 §4, 끝의 "기술 결정 로그"들).
3. **클라이언트가 기술적으로 어떻게 도는지 (설계)** → [`docs/client-spec.md`](docs/client-spec.md) (하드웨어 / 4-Step 파이프라인 / 레이턴시·배터리 분석).
4. **코드/빌드/모델을 실제로 만지려면 (구현)** → [`docs/dev-guide.md`](docs/dev-guide.md) (셋업·빌드·코드 아키텍처·모델 swap·NPU 현실·ONNX·트러블슈팅).
5. **무슨 일이 있었는지 (시간순)** → [`docs/progress-log.md`](docs/progress-log.md) (APK 버전 history + 핵심 인사이트 dump).
6. [`docs/archive.md`](docs/archive.md) 는 **읽지 않아도 됨** — superseded 된 옛 기획안. 현재 사양으로 인용 금지.

`client-spec.md`(설계 사양) vs `dev-guide.md`(실제 구현)는 altitude 차이: 무엇을 만들기로 했나 vs 코드가 실제로 어떻게 도나.

**충돌 시 우선순위** (vision.md §10.3):
- 클라이언트 동작(카메라/NPU/모델/파이프라인) → `client-spec.md` 우선
- 비즈니스/시나리오/매칭 전략 → `vision.md` 우선

**주의 — 라면/마트에 현혹되지 말 것**: v1 시연은 "콜라/펩시 페트병 + 노트북"이고 라면·마트는 원 기획 시나리오일 뿐이다. 프로젝트 방향을 "마트 쇼핑 앱"으로 좁히면 안 됨 (vision.md §1.2).

## 문서 갱신 규칙

- **새 기술 결정 / 마일스톤** → `vision.md` 끝에 기존 "기술 결정 로그" 패턴으로 추가하고, **반드시 맨 위 "현재 상태 스냅샷"도 같이 동기화**한다 (스냅샷이 stale 해지는 게 과거의 실수였음).
- **일일 진행** → `progress-log.md` 에 날짜 섹션 추가.
- `archive.md` 는 동결 — 갱신하지 않는다.
- 인덱스/구조가 바뀌면 [`docs/README.md`](docs/README.md) 갱신.

## 코드 / 자산 구조

| 경로 | 내용 |
|---|---|
| `EagleEye_Unity/` | Unity 프로젝트 (안경 앱 본체). `Library/Bee`·`Build/` 는 빌드 캐시라 디스크 부족 시 정리 가능 |
| `unity_assets_prep/Scripts/` | C# 핵심 로직 — `HelloAR.cs`(파이프라인 오케스트레이션, `clipOnlyMode` 토글), `QnnYoloDetector.cs`, `ProductMatcher.cs`, `AdRenderer.cs`, `OCRExtractor.cs`, `AmbientInterestProfile.cs` |
| `unity_assets_prep/Plugins/Android/` | Java native — `QnnYoloEngine.java`, `MLKitOCR.java` |
| `db/` | `metadata.json`, `unity_db.json` (hierarchical category+brand schema v0.7.1), 임베딩, `ads_video/*.mp4` |
| `refs/` | CLIP reference 이미지 (category + brand-specific). 구조는 progress-log "Refs 폴더 구조" 참조 |
| `tools/recording/` | 1인칭 시야 녹화 시스템 (시연 영상 제작) |
| `build_hello_ar.sh` | 안경 APK 빌드 + 모델/DB 복사 + 카메라 권한 자동 grant |
| `simulate_*.py`, `build_*_db.py` | Mac 측 시뮬레이터 / DB 빌드 스크립트 |

옛 루트 기술 노트(`README.md` 레포 개요 / `INTEGRATION_GUIDE.md` / `MODELS.md` / `README_NPU.md` / `ONNX_ANALYSIS.md`)는 2026-06-08 [`docs/dev-guide.md`](docs/dev-guide.md) 로 통합·현행화됨. 루트엔 진입점용 얇은 `README.md` 와 이 `CLAUDE.md` 만 남김. (옛 문서의 "Hexagon v68/v69" "QNN direct" "v0.3.6 1:1 매칭" 등은 전부 정정/폐기 — dev-guide 상단 "폐기된 경로 주의" 참조.)

## 작업 분담 / 환경

- 개발 머신: Mac (Apple Silicon). 안경: RayNeo X3 Pro (Snapdragon AR1 Gen 1, Hexagon v73 NPU, **FP16 유닛 없음** → w8a8 양자화 필수).
- Claude = 코드 작성/디버깅/스크립트 짝꿍. 사용자 = 안경 빌드·실행·관찰·의사결정·데이터 수집.
- 맥북 프로토타입 단계에서는 **레이턴시 측정·최적화 불필요, 동작 검증만 목표** (추론 합계 ~18-30ms 로 이미 200ms 목표 대비 여유).
