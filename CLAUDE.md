# CLAUDE.md

Eagle Eye — AR 글라스(RayNeo X3 Pro)용 온디바이스 광고/정보 인프라 시스템의 v1 PoC.
"경쟁사 상품을 손에 든 순간 비교 정보를 시야에 띄운다"(conquest)가 핵심. 코드는 Unity(C#) + Android native(Java) + 온디바이스 TFLite/QNN 추론.

## 문서를 읽는 법 (중요)

프로젝트 맥락은 `docs/` 의 `.md` 들에 있다. **`docs/` 가 노션을 대체한 source of truth** (노션은 2026-06-08 폐기). 갱신은 항상 노션이 아니라 이 파일들에 직접 한다.

**읽는 순서:**
1. **현황만 빠르게** → [`docs/vision.md`](docs/vision.md) 맨 위 **"⚡ 현재 상태 스냅샷"** 한 섹션. 현재 버전·매칭 아키텍처·다음 작업이 여기 다 있다.
2. **지금까지 뭘 시도했나 / 뭐가 실패했나 (실험 history)** → [`docs/EXPERIMENTS.md`](docs/EXPERIMENTS.md). 빌드별 시도·결과·교훈의 평면 로그. **이게 history의 source of truth** (git 을 돌아다니며 과거를 재구성하지 말 것).
3. **시스템이 뭔지 / 왜 이렇게 결정됐는지** → [`docs/vision.md`](docs/vision.md) 전체 (비즈니스 정체성 §1~3, v1 사양 §4, 끝의 "기술 결정 로그"들).
4. **클라이언트가 기술적으로 어떻게 도는지 (설계)** → [`docs/client-spec.md`](docs/client-spec.md) (하드웨어 / 4-Step 파이프라인 / 레이턴시·배터리 분석).
5. **코드/빌드/모델을 실제로 만지려면 (구현)** → [`docs/dev-guide.md`](docs/dev-guide.md) (셋업·빌드·코드 아키텍처·모델 swap·NPU 현실·ONNX·트러블슈팅).
6. **플랫폼 사실·제약·gotcha (RayNeo SDK API / NPU·QNN / OpenXR / 권한 / 빌드)** → [`docs/platform-rayneo.md`](docs/platform-rayneo.md). 주제별 living 레퍼런스 + **맨 위에 RayNeo 공식 문서 링크(Feishu 위키 / Qualcomm 미러 / open.rayneo.com)**. 카메라·SLAM·NPU 위임·권한·빌드 함정을 만질 땐 **여기 먼저** — 이미 깨진 길을 다시 안 깨려고. (시간순이 아니라 주제순. `🆕`=코드에만 있던 걸 처음 문서화한 것.)
7. **시간순 상세 진행** → [`docs/progress-log.md`](docs/progress-log.md) (APK 버전 history + 핵심 인사이트 dump). EXPERIMENTS.md 의 각 행을 더 깊게 보고 싶을 때.
8. [`docs/archive.md`](docs/archive.md) 는 **읽지 않아도 됨** — superseded 된 옛 기획안. 현재 사양으로 인용 금지.

`client-spec.md`(설계 사양) vs `dev-guide.md`(실제 구현)는 altitude 차이: 무엇을 만들기로 했나 vs 코드가 실제로 어떻게 도나.

**충돌 시 우선순위** (vision.md §10.3):
- 클라이언트 동작(카메라/NPU/모델/파이프라인) → `client-spec.md` 우선
- 비즈니스/시나리오/매칭 전략 → `vision.md` 우선

**주의 — 라면/마트에 현혹되지 말 것**: v1 시연은 "콜라/펩시 페트병 + 노트북"이고 라면·마트는 원 기획 시나리오일 뿐이다. 프로젝트 방향을 "마트 쇼핑 앱"으로 좁히면 안 됨 (vision.md §1.2).

## 문서 갱신 규칙

- **실험/빌드 한 번 = `EXPERIMENTS.md` 에 한 행** (성공이든 실패든). 특히 **실패·롤백은 ❌ 행으로 반드시 남긴다** — "왜 실패했고 다시 하지 말 것"이 이 프로젝트에서 가장 값진 정보다. 규칙은 EXPERIMENTS.md 상단 "작업 규칙" 참조.
- **새 기술 결정 / 마일스톤** → `vision.md` 끝에 기존 "기술 결정 로그" 패턴으로 추가하고, **반드시 맨 위 "현재 상태 스냅샷"도 같이 동기화**한다 (스냅샷이 stale 해지는 게 과거의 실수였음).
- **일일 진행 상세** → `progress-log.md` 에 날짜 섹션 추가.
- `archive.md` 는 동결 — 갱신하지 않는다.
- 인덱스/구조가 바뀌면 [`docs/README.md`](docs/README.md) 갱신.

## 브랜치 / 작업 흐름

- **작업은 `main` 한 줄에서** 한다. try-fail-rollback 용 장기 feature branch 를 만들지 않는다 — 실패한 시도가 branch 와 함께 사라져 history 가 끊기는 게 과거의 문제였다.
- **history = `EXPERIMENTS.md` (평면 로그), git = 코드 백업.** 과거 상태를 "보려고" git 을 checkout 하며 돌아다니지 않는다. 코드를 진짜 되살릴 때만 `git checkout <commit>` (commit 은 EXPERIMENTS.md 표에 적혀 있음).
- 마일스톤은 `git tag exp/bNN` 로 박제(선택). 여러 접근을 동시에 시험하면(fleet) worktree 로 평행 실행하되 결과는 모두 EXPERIMENTS.md 한 곳에 모은다.
- (2026-06-13) 과거 팀원별 feature branch(`b24-integrated`/`npu-ocr-slam-b22`/`slam-clip-worldanchor`)는 `main` 으로 통합 완료. 이후 분기 금지.

## ✅ 작업 마무리 체크리스트 (self-maintenance)

빌드/실험/코드 변경을 끝낼 때마다 **이 순서로 자문**한다. 이게 문서가 stale 해지지 않는 유일한 방법:

1. **시도했나?** → [`docs/EXPERIMENTS.md`](docs/EXPERIMENTS.md) 표에 한 행 추가 (성공 ✅ / 제약 ⚠️ / 실패·롤백 ❌ + 한 줄 교훈 + commit 해시).
2. **현황이 바뀌었나?** → [`docs/vision.md`](docs/vision.md) 맨 위 "⚡ 현재 상태 스냅샷" 동기화 (최신 버전·다음 작업).
3. **새 기술 결정이었나?** → `vision.md` 끝 "기술 결정 로그" 패턴으로 추가.
4. **플랫폼 사실·제약·gotcha 를 새로 알았나?** (RayNeo API/NPU 위임/권한/OpenXR/빌드 함정, "X 는 안 되더라" 류) → [`docs/platform-rayneo.md`](docs/platform-rayneo.md) 해당 § 갱신/추가. 사실이 바뀌면 옛 엔트리를 **덮어쓰고**(living), 그 변화의 계기는 EXPERIMENTS 행으로. 코드에만 있던 걸 처음 적으면 제목에 `🆕`.
5. **코드 구조/파일이 옮겨졌나?** → 이 `CLAUDE.md` 의 "코드/자산 구조" 표 + [`docs/dev-guide.md`](docs/dev-guide.md) §2 갱신.
6. **문서를 새로 만들었나?** → [`docs/README.md`](docs/README.md) 인덱스 등재 + 관련 문서에서 상호 링크.
7. 커밋은 `main` 에. 마일스톤이면 `git tag exp/bNN`.

> 원칙: **코드는 git, history·맥락은 문서.** 둘 중 하나만 갱신하고 끝내지 말 것 — 다음 사람(또는 다음 세션의 나)이 git 고고학 없이 문서만 읽고 따라올 수 있어야 한다.

## 코드 / 자산 구조

> **빌드되는 안경 앱 = `glasses-app/`** (자기완결적 Unity 프로젝트). 여기를 만진다.
> 루트의 `*.py` 와 `db/`·`products/` 는 **Mac 측 오프라인 도구/데이터**(모델 export·QNN 컴파일·임베딩 빌드)이지 안경 런타임 코드가 아니다.

| 경로 | 내용 |
|---|---|
| `glasses-app/` | **안경 앱 본체 (Unity 프로젝트).** `Library/`·`Temp/`·`Build/` 는 빌드 캐시(gitignore). |
| `glasses-app/Assets/Scripts/` | C# 핵심 로직. **SLAM/앵커**=`SpatialAnchorTest.cs` · **파이프라인 오케스트레이션**=`HelloAR.cs`(`clipOnlyMode` 토글) · **추론**=`ClipExtractor.cs`/`QnnYoloDetector.cs`/`OCRExtractor.cs` · **매칭**=`ProductMatcher.cs` · **렌더**=`AdRenderer.cs` · **결제 인터랙션**=`AdCheckout.cs` · **트리거/카메라**=`GyroTrigger.cs`/`CameraPreview.cs`(ShareCamera) · **프로필**=`AmbientInterestProfile.cs` |
| `glasses-app/Assets/Plugins/Android/` | Java native NPU 엔진 — `QnnClipEngine.java`, `QnnYoloEngine.java`, `EasyOCREngine.java` (TFLite + QNN delegate). |
| `glasses-app/Assets/StreamingAssets/` | 런타임 번들 — 모델(`*.tflite`) + `db/`(`metadata.json`, `embeddings/`, `ads/`, `ads_video/*.mp4`). |
| `glasses-app/*.ps1` | Windows 빌드/셋업 (`setup_2022.ps1`, `build_2022.ps1`, `build_spatial_anchor.ps1`). **Unity 2022.3.62f3 전용.** |
| `glasses-app/tools/monitor/` | eagle-monitor 온디바이스 텔레메트리 대시보드 (`eagle_monitor.py`). |
| `db/`, `products/` (루트) | Mac 측 임베딩/메타데이터 빌드 재료 + 상품 reference 이미지. |
| 루트 `*.py` | Mac 측 오프라인 도구 — `create_embeddings.py`(임베딩 DB) · `qai_hub_compile.py`/`qai_hub_pipeline.py`(QNN 컴파일) · `export_mobileclip_s2.py`/`verify_onnx.py`/`clean_onnx_for_qnn.py`(ONNX) · `yolo.py`/`eagle_eye_step2.py`(Mac PoC 프로토타입). |

(2026-06-13 정리: 옛 빌드 계보 `EagleEye_Unity/` + `unity_assets_prep/` + `build_hello_ar.sh` 제거됨 — 현재 앱은 위 `glasses-app/` 가 유일. 옛 루트 기술 노트 `ONNX_ANALYSIS.md` 등은 [`docs/dev-guide.md`](docs/dev-guide.md) 로 통합·삭제됨. "Hexagon v68/v69" 표기는 **v73 으로 정정** — dev-guide 상단 "폐기된 경로 주의" 참조.)

## 작업 분담 / 환경

- 머신: Mac(Apple Silicon) = Python 오프라인 도구(임베딩/ONNX/QNN 컴파일). **Windows = 안경 앱 Unity 빌드** (`glasses-app/*.ps1`, Unity 2022.3.62f3 전용). 안경: RayNeo X3 Pro (Snapdragon AR1 Gen 1, Hexagon v73 NPU, **FP16 유닛 없음** → w8a8 양자화 필수).
- Claude = 코드 작성/디버깅/스크립트 짝꿍. 사용자 = 안경 빌드·실행·관찰·의사결정·데이터 수집.
- 맥북 프로토타입 단계에서는 **레이턴시 측정·최적화 불필요, 동작 검증만 목표** (추론 합계 ~18-30ms 로 이미 200ms 목표 대비 여유).
