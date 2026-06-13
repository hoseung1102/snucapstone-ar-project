<!-- 실험 평면 로그 (lab notebook). 이 파일이 "지금까지 뭘 시도했고 뭐가 됐나"의 source of truth. -->
<!-- git 은 코드 백업, 이 파일은 history. 과거를 보려고 git 을 돌아다니지 말고 여기를 읽는다. -->

# 🧪 EXPERIMENTS — 실험 평면 로그

> **이 파일을 읽는 법**: 위에서 아래로 = 최신→과거. 한 행 = 한 빌드/시도.
> "지금까지 뭘 했고 뭐가 실패했나"는 **여기만** 보면 된다. git checkout 안 해도 됨.
>
> **왜 이렇게 하나**: 이 프로젝트는 try → fail → rollback 이 잦다. branch 로 시도를 나누면
> 실패한 시도와 "왜 실패했나"가 사라진다(=가장 값진 정보 손실). 그래서 **시도를 평면에 쌓고,
> 실패도 ❌ 로 영구 보존**한다. branch 가 아니라 이 로그가 history다.

## 🔧 작업 규칙 (실험할 때마다)

1. **작업은 `main` 한 줄에서** 한다. (try-fail-rollback 용 장기 branch 를 만들지 않는다)
2. 시도가 의미 있으면 (빌드/검증) → 아래 표에 **한 행 추가** + 커밋. 빌드 번호 `bNN` 을 ID 로 쓴다.
3. **실패/롤백도 반드시 ❌ 행으로 남긴다** — "다시 시도 금지 + 이유" 한 줄이 핵심.
4. 코드를 진짜 되살려야 하면 그때만 `git checkout <commit>`. 평소엔 git 을 탐색하지 않는다.
5. 마일스톤(데모 성공 등)은 `git tag exp/bNN` 로 박제해두면 복원이 쉽다. (선택)
6. 여러 접근을 **동시에** 시험하려면(fleet) worktree 로 평행 실행하되, **결과는 모두 이 파일 한 곳에 기록**한다.

`결과` 기호: ✅ 채택/성공 · ⚠️ 부분 성공/제약 발견 · ❌ 실패/롤백 · 🔬 진단/측정

---

## 빌드 타임라인 (최신 → 과거)

| ID | 날짜 | 목적 / 변경 | 결과 | 교훈 · 결정 | commit |
|----|------|-------------|------|-------------|--------|
| **b26** | 06-11 | 광고 브랜드당 1개 중복 spawn 수정 + tap-to-checkout 인터랙션(`AdCheckout`) | ✅ | 데모 인터랙션 추가. 현 head | `ffcfd28` |
| **b25** | 06-11 | color-video 빌드: OCR off + 색상 브랜드 + world-anchored 영상광고(mp4) + adb debug hook | ✅ | 데모 빌드. 상세: [B25_DEMO_HANDOFF](../spatial_anchor_test/B25_DEMO_HANDOFF.md) | `d36e813`·`8115909` |
| **b24** | 06-11 | 통합: worldanchor(b15–17) ⨉ npu-ocr(b22) 머지 → 단일 라인 | ✅ | 두 팀원 작업 합류 지점. **현 main 의 베이스** | `809a3ff` |
| **b22** | 06-11 | NPU EasyOCR(word-box)를 SLAM 파이프라인에 통합 | ⚠️ | EasyOCR **recognizer 가 NPU 비호환** (detector 만 위임). 상세: [B22_TEST_RESULTS](../spatial_anchor_test/B22_TEST_RESULTS.md), [integration_log §5](integration_log.md) | `5bfc51e` |
| **b17** | 06-11 | CDSP crash 우회 위해 CLIP 을 CPU 로 빼는 fix 준비 | 🔬❌ | CDSP crash / **SLAM 8Hz 저하** / OpenXR surface 문제 진단. CPU-CLIP 경로는 SLAM 을 죽임 → NPU 경로 유지. 상세: [findings-2026-06-11](findings-2026-06-11-crash-slam-openxr.md) | `d5e5431` |
| **b16** | 06-11 | CLIP-ready 플래그 + HUD 카운터 5개 + `[MONITOR]` 로그 + eagle-monitor 대시보드 + ad mirror fix | ✅ | 온디바이스 가시성 확보(모니터링). | `1e44756` |
| **b15** | 06-11 | 광고 축소/원거리 + SLAM ATW + 저해상도 preview + head-locked 2D HUD 카운터 | ✅ | 상시-open RGB preview 가 SLAM 지연 유발 → 저해상도로 완화 | `80f2d52` |
| **b13+b14** | 06-11 | max-2 FIFO 광고 + 수평 flip + mean-color 브랜드 분류 | ✅ | count 방식 실패 → **평균색(mean RGB)** 으로 brand 판별 전환 | `e3f24ef` |
| **b12** | 06-11 | 광고 위치 beside → front(gaze center), world-anchored | ✅ | 시선 정면 배치가 자연스러움 | `feb64f0` |
| **b11** | 06-11 | 단일 경쟁사 광고 + color-brand conquest **온디바이스 검증** | ✅ | **conquest 첫 온디바이스 성공** | `ed4037f` |
| v0.5–0.7 | 06-07~08 | 매칭 아키텍처 진화 (아래 "v0.x 매칭 진화" 참조) | ✅ | CLIP zero-shot brand 분별 불가 → **CLIP category + OCR brand 계층화** | 다수 |
| **v0.3.0**<br>(Step D) | 06-08 | RayNeo 6DoF SLAM — 호랑이 quad 공간 고정 **첫 성공** | ✅ | SLAM 첫 성공. 하루치 디버깅 cycle 상세: [JOURNEY](../spatial_anchor_test/JOURNEY.md) | `de0be34` |
| v0.2.1 | 06-06 | Sentis YOLO11n 정적 추론 + bbox 표시 | ✅ | — | `10fdbdc` |
| v0.2.0 | 06-06 | 카메라 라이브 프리뷰 + 자이로 표시 + 90° 회전 보정 | ✅ | — | `055e5bd`·`fff106f` |

---

## 살아있는 교훈 (다시 하지 말 것 / 확정된 제약)

미래의 나/팀원/Claude 가 같은 막다른 길을 다시 파지 않도록.

- ❌ **CPU-CLIP 으로 빼기 (b17)** — CDSP crash 는 우회되지만 SLAM 이 8Hz 로 떨어짐. NPU CLIP 유지가 정답.
- ⚠️ **EasyOCR recognizer 는 NPU 비호환 (b22)** — detector(CRAFT)만 위임 가능. recognizer 는 CPU/대체 필요.
- ⚠️ **온디바이스 CLIP sim 이 Mac 오프라인 대비 ~0.3 낮음** — 오프라인 ref 숫자로 실기기 예측 불가. threshold 0.45 + 중앙 crop 으로 우회 중, 근본 원인 미규명.
- ⚠️ **카메라 FOV ≠ 사용자 시선** — 눈높이 정면의 물체는 카메라(아래·팔 방향) 시야 밖. 팔 뻗어 몸 앞 아래에 둬야 잡힘.
- ⚠️ **상시-open ShareCamera RGB → SLAM 프레임레이트 저하** — 근본 해법은 "연속 preview → 트리거 시 단발 takePicture" 전환(미구현, 백로그). 현재는 안 닫고 저해상도로 완화.
- ❌ **앱 pause 중 preview 채널 열어두기** — 미소비 버퍼가 camera provider SIGPIPE 유발 → SLAM 595m 발산. `OnApplicationPause → CloseCamera` 로 보호.
- 빌드 메타: build fail 시 `tasklist` 로 zombie(Unity.exe 등) 확인 → 종료 → `Library/Bee` fresh delete → 재빌드 (JOURNEY §G).

---

## v0.x 매칭 진화 (요약 — 상세는 [vision.md](vision.md) 스냅샷 / [progress-log.md](progress-log.md))

CLIP zero-shot 만으로 coke vs pepsi 같은 fine-grained brand 분별이 본질적으로 불가능(환경 신호가 brand 신호보다 dominant) → **계층화**로 전환된 과정:

- **v0.6.0** MLKit OCR 통합 → **v0.7.0** hierarchical(CLIP category + OCR brand) 대형 refactor
- **v0.7.2** default fallback 제거(strict — brand 1개여도 OCR 매칭 없으면 광고 X, false positive 차단)
- **v0.7.3** OCR 전처리(회전+crop+upscale) + 매칭 구조 분리 + CLIP-only 시 YOLO skip
- **v0.7.4** CLIP 중앙 crop query+ref + threshold 0.45 (현 최신 매칭). `ClipExtractor.cropFraction` ↔ `build_adversarial_db.py CLIP_CROP` 반드시 일치
- (v0.5.x: threshold hardcode / CLIP-only 모드 / multi-ref / mp4 광고 등 — progress-log 참조)

---

## 참고: `bisectionCase` 는 실험 ID 가 아님

`SpatialAnchorTest.cs` 의 `bisectionCase` enum (`B0`~`B8`) 은 **런타임 컴포넌트 점진 추가 토글**이다(디버깅용). 위 빌드 ID `bNN` 과 다른 축이므로 혼동 금지.
- `B0` baseline only · `B1` +Gyro · `B2` +Camera(ShareCamera) · … · `B8` 전체 파이프라인(HelloAR)
