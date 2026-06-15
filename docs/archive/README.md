<!-- docs/archive/ 인덱스. 보관 문서(작성 시점 스냅샷) 목록. 현황 정본은 docs/STATUS.md. -->

# 🗄️ docs/archive — 보관소 인덱스

> ⚠️ **여기는 보관소다. 모든 문서는 작성 시점의 스냅샷이며 현황이 아니다.**
> 현재 상태(최신 빌드·매칭 아키텍처·다음 작업)는 → **[docs/STATUS.md](../STATUS.md)**.
> 실험 history 의 평면 로그는 → [docs/EXPERIMENTS.md](../EXPERIMENTS.md) (🟢git검증 / 🟠재구성 배지).
>
> 이 문서들은 특정 빌드/브랜치 시점의 핸드오프·진단·옛 기획안이라 **그 시점 이후 거의 다 superseded** 됐다. 인용된 브랜치명(`feature/b24-integrated`·`feature/npu-ocr-slam-b22` 등)·빌드 태그·SDK 버전·현황 서술은 **현재로 인용 금지**. 역사적 맥락·재현 절차·"왜 그렇게 결정했나"를 볼 때만 읽는다.
>
> 신뢰도 배지(각 문서 맨 위에도 부착): 🔴=stale·모순(현황 인용 금지) · 🟠=재구성(서사 맥락) · 🟢=git검증 · 🟡=코드검증.

## 보관 문서 (10건)

| 문서 | 무엇 / 어느 시점 기준 | 왜 superseded |
|---|---|---|
| [B25_DEMO_HANDOFF.md](B25_DEMO_HANDOFF.md) 🔴 | 빌드 **b25**(`b25-color-video`, branch `feature/b24-integrated`, `d36e813`) 데모 핸드오프 — 구조·결정·근거·이어받기 | 작성 시점(2026-06-11) 현황 스냅샷. 빌드는 EXPERIMENTS b25 행으로, 현황은 STATUS.md 로 승계 |
| [B22_TEST_RESULTS.md](B22_TEST_RESULTS.md) 🔴 | 빌드 **b22**(`feature/npu-ocr-slam-b22`) 온디바이스 테스트 결과 + 진단 (실기 트리거 측정) | NPU EasyOCR 경로가 이후 폐기됨(recognizer NPU 비호환). 결론만 EXPERIMENTS b22 ⚠️ 행에 요약 |
| [BUILD_OCR_SLAM_HANDOFF.md](BUILD_OCR_SLAM_HANDOFF.md) 🔴 | 빌드 **b22**(`b22-slam-ocr`) NPU OCR + SLAM 통합 빌드 셋업/실행 런북 (팀원용) | 위 b22 OCR 경로 폐기와 함께 stale. 빌드 절차는 dev-guide.md 로 승계 |
| [JOURNEY.md](JOURNEY.md) 🟠 | 빌드 **v0.3.0(Step D)** SLAM 첫 성공까지의 하루치 디버깅 cycle (2026-06-08, 재구성) | 서사적 cycle 기록. 빌드별 교훈은 EXPERIMENTS, build-fail 루틴은 EXPERIMENTS "살아있는 교훈" 으로 승계 |
| [ONBOARDING.md](ONBOARDING.md) 🔴 | conquest AR 데모 "이어받기 설명서" (AI 세션 붙여넣기용, `feature/b24-integrated`/`b25-color-video` 기준) | 브랜치/빌드 태그가 stale. 온보딩은 CLAUDE.md + STATUS.md 로 대체 |
| [integration_log.md](integration_log.md) 🔴 | HelloAR(CLIP/OCR) ⨉ SpatialAnchor 통합 로그 (2026-06-09, `feature/integrate-spatial-side`, base `9acd75f`) | SDK 1.1.6↔1.1.7.9 BLOCKER 등 당시 막힌 문제 기록. 사실은 platform-rayneo.md 로 승계 |
| [freeze-accuracy-diagnosis.md](freeze-accuracy-diagnosis.md) 🔴 | "시작 직후 freeze" + "coke/pepsi 오인식" 근본원인 진단 + b9/b10 계획 (2026-06-11, `feature/slam-clip-worldanchor`) | b9/b10 계획·현황 stale. 진단 사실 일부는 platform-rayneo.md / EXPERIMENTS 로 승계 |
| [spatial-anchor-handoff.md](spatial-anchor-handoff.md) 🔴 | Spatial Anchor 통합 빌드 핸드오프 + 빌드 런북 (2026-06-11, 빌드 `b16`, bisection 패키지) | 현황 스냅샷 stale. 자매 진단은 [freeze-accuracy-diagnosis.md](freeze-accuracy-diagnosis.md) |
| [findings-2026-06-11-crash-slam-openxr.md](findings-2026-06-11-crash-slam-openxr.md) 🟠 | 빌드 **b17** 진단 — 디바이스 크래시 / SLAM 8Hz / RayNeo OpenXR 표면 (서브에이전트 4건 + logcat 포렌식) | CDSP/SLAM 8Hz 사실은 platform-rayneo.md 로 승계. 진단 요약은 EXPERIMENTS b17 행 |
| [archive.md](archive.md) 🔴 | 옛 기획안 v1 + Tech Tree 초기 인사이트 (RayNeo X2 / XR2 Gen1 / confidence 0.7 / Snapdragon Spaces 등) | 초기 결정의 변천사 기록. 전부 superseded — 현재 사양 아님 |

> 위 표의 빌드 ID(b16/b17/b22/b25/v0.3.0)는 [docs/EXPERIMENTS.md](../EXPERIMENTS.md) 의 해당 행에서 commit 해시와 함께 인덱싱된다.
