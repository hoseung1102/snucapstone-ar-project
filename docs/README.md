# 📚 Eagle Eye 프로젝트 문서 (source of truth)

> 노션 무료 기간 만료로 2026-06-08 전체 export 후 노션 폐기.
> **모든 문서 갱신은 노션이 아니라 이 디렉토리의 `.md` 파일에 직접 한다.**
> 읽는 순서·갱신 규칙은 루트 [`CLAUDE.md`](../CLAUDE.md) 참조.

## 문서 구성

**읽는 순서**: 현황만 빠르게 → **STATUS.md** → 그 다음 EXPERIMENTS / vision / 나머지.

| 파일 | 역할 | 갱신 시점 |
|---|---|---|
| [STATUS.md](STATUS.md) | **현황 단일 정본 (source of truth for "지금 상태").** 최신 빌드·매칭 아키텍처·다음 작업을 한 곳에. 빠른 현황 파악은 여기부터 | 현황이 바뀔 때 (최신 빌드 / 다음 작업) |
| [vision.md](vision.md) | **메인.** 시스템 비즈니스 정체성 + v1 PoC 사양 + 결정 로그. 맨 위 "현재 상태 스냅샷"만 읽어도 현황 파악 | 새 기술 결정 / 마일스톤 (끝의 결정 로그 패턴으로 추가) |
| [EXPERIMENTS.md](EXPERIMENTS.md) | **실험 history 의 source of truth.** 빌드별 시도·결과(✅⚠️❌)·교훈의 평면 로그. git 대신 여기서 과거를 본다. 신뢰도 배지(🟢git검증/🟠재구성) 부착 | **실험/빌드 한 번 = 한 행** (실패·롤백도 ❌ 로 반드시) |
| [platform-rayneo.md](platform-rayneo.md) | **플랫폼 사실·gotcha 의 주제별 living 레퍼런스** (RayNeo SDK API / NPU·QNN / OpenXR / 권한 / 빌드). EXPERIMENTS 가 "언제 뭘 시도"라면 이건 "지금 뭐가 진실". `🆕`=코드에서 처음 구조함 | 플랫폼 사실/제약을 새로 알 때 (해당 § 덮어쓰기) |
| [client-spec.md](client-spec.md) | 클라이언트(RayNeo X3 Pro) **기술 스택 / 파이프라인**의 진실의 원천 (= 옛 기획안 v1.2) | 하드웨어 / 추론 스택 / 파이프라인 사양 변경 |
| [dev-guide.md](dev-guide.md) | **구현/빌드/모델 레퍼런스.** 셋업·빌드·코드 아키텍처·모델 swap·NPU 현실·ONNX 분석·트러블슈팅 (옛 루트 README/INTEGRATION_GUIDE/MODELS/README_NPU/ONNX_ANALYSIS 통합) | 코드/빌드/모델 변경 시 |
| [progress-log.md](progress-log.md) | 일자별 진행 로그 + APK 버전 history + 핵심 인사이트 dump. ⚠️ v0.5/v0.7 구간은 🟠 재구성분(인용 해시 부재, 코드 복원 불가) | 일일 진행 (새 날짜는 새 섹션) |
| [archive/](archive/) | **보관소 디렉토리.** 작성 시점 스냅샷인 옛 핸드오프/진단/기획안 9건 + 옛 `archive.md`. 현황 아님(현황은 STATUS.md). 인덱스는 [archive/README.md](archive/README.md) | 갱신 안 함 (동결) |
| README.md | 이 인덱스 | 구조 변경 시 |

> 루트에는 진입점용 얇은 [`README.md`](../README.md) (→ docs/ 안내) 와 [`CLAUDE.md`](../CLAUDE.md) (읽는 법 가이드) 만 둔다. 그 외 기술 문서는 전부 여기 `docs/` 로 통합됨 (2026-06-08). 작성 시점 스냅샷(핸드오프·진단·옛 기획안)은 `docs/archive/` 로 분리됨 (2026-06-15).

## 책임 분리 (충돌 시 우선순위)

`vision.md` 와 `client-spec.md` 는 의도적으로 분리됨 (vision.md §10.3):
- **클라이언트 동작** (카메라 / NPU / 모델 / 파이프라인) → **client-spec.md** 우선
- **시스템 비즈니스 / 시나리오 / 매칭 전략** → **vision.md** 우선

## 빠른 현황 파악

→ **[STATUS.md](STATUS.md)** — 현황 단일 정본(최신 빌드·매칭 아키텍처·다음 작업). 여기부터.
→ 배경/사양은 [vision.md](vision.md) 맨 위 **"⚡ 현재 상태 스냅샷"** (hierarchical CLIP category + OCR brand 매칭).
→ 지금까지 뭘 시도했고 뭐가 실패했나는 [EXPERIMENTS.md](EXPERIMENTS.md) (🟢git검증 / 🟠재구성 배지).

## 운영 정책

1. **실험/빌드 한 번 = `EXPERIMENTS.md` 한 행** (실패·롤백도 ❌ 로 반드시). 작업은 `main` 한 줄에서 — 장기 feature branch 금지 (CLAUDE.md "브랜치 / 작업 흐름").
2. **새 기술 결정 / 마일스톤** → `vision.md` 끝의 "기술 결정 로그" 패턴으로 추가하고, 맨 위 스냅샷도 동기화.
3. **일일 진행 상세** → `progress-log.md` 에 날짜 섹션 추가.
4. **노션은 더 이상 갱신하지 않음.**
5. 표기: 일반 GFM 으로 작성 (Notion 전용 `<table>`/`<page>` 문법 잔재가 일부 남아있으나 새 내용엔 쓰지 않음).
6. 옛 export 트리 구조(`docs/notion/`, Team BETA / AR Application Ideation 컨테이너 등)는 2026-06-08 정리되어 위 핵심 문서들로 통합됨. 이후 작성 시점 스냅샷(핸드오프·진단·옛 기획안)은 2026-06-15 `docs/archive/` 로 분리(인덱스: [archive/README.md](archive/README.md)).
