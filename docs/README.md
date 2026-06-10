# 📚 Eagle Eye 프로젝트 문서 (source of truth)

> 노션 무료 기간 만료로 2026-06-08 전체 export 후 노션 폐기.
> **모든 문서 갱신은 노션이 아니라 이 디렉토리의 `.md` 파일에 직접 한다.**
> 읽는 순서·갱신 규칙은 루트 [`CLAUDE.md`](../CLAUDE.md) 참조.

## 문서 구성 (6개)

| 파일 | 역할 | 갱신 시점 |
|---|---|---|
| [vision.md](vision.md) | **메인.** 시스템 비즈니스 정체성 + v1 PoC 사양 + 결정 로그. 맨 위 "현재 상태 스냅샷"만 읽어도 현황 파악 | 새 기술 결정 / 마일스톤 (끝의 결정 로그 패턴으로 추가) |
| [client-spec.md](client-spec.md) | 클라이언트(RayNeo X3 Pro) **기술 스택 / 파이프라인**의 진실의 원천 (= 옛 기획안 v1.2) | 하드웨어 / 추론 스택 / 파이프라인 사양 변경 |
| [dev-guide.md](dev-guide.md) | **구현/빌드/모델 레퍼런스.** 셋업·빌드·코드 아키텍처·모델 swap·NPU 현실·ONNX 분석·트러블슈팅 (옛 루트 README/INTEGRATION_GUIDE/MODELS/README_NPU/ONNX_ANALYSIS 통합) | 코드/빌드/모델 변경 시 |
| [progress-log.md](progress-log.md) | 일자별 진행 로그 + APK 버전 history + 핵심 인사이트 dump | 일일 진행 (새 날짜는 새 섹션) |
| [archive.md](archive.md) | superseded 된 옛 문서 (기획안 v1, Tech Tree 초기 인사이트) — 역사적 맥락용, **현재 사양 아님** | 갱신 안 함 (동결) |
| README.md | 이 인덱스 | 구조 변경 시 |

> 루트에는 진입점용 얇은 [`README.md`](../README.md) (→ docs/ 안내) 와 [`CLAUDE.md`](../CLAUDE.md) (읽는 법 가이드) 만 둔다. 그 외 기술 문서는 전부 여기 `docs/` 로 통합됨 (2026-06-08).

## 책임 분리 (충돌 시 우선순위)

`vision.md` 와 `client-spec.md` 는 의도적으로 분리됨 (vision.md §10.3):
- **클라이언트 동작** (카메라 / NPU / 모델 / 파이프라인) → **client-spec.md** 우선
- **시스템 비즈니스 / 시나리오 / 매칭 전략** → **vision.md** 우선

## 빠른 현황 파악

→ [vision.md](vision.md) 맨 위 **"⚡ 현재 상태 스냅샷"** 한 섹션 (현재 v0.7.2, hierarchical CLIP+OCR 매칭).

## 운영 정책

1. **새 기술 결정 / 마일스톤** → `vision.md` 끝의 "기술 결정 로그" 패턴으로 추가하고, 맨 위 스냅샷도 동기화.
2. **일일 진행** → `progress-log.md` 에 날짜 섹션 추가.
3. **노션은 더 이상 갱신하지 않음.**
4. 표기: 일반 GFM 으로 작성 (Notion 전용 `<table>`/`<page>` 문법 잔재가 일부 남아있으나 새 내용엔 쓰지 않음).
5. 옛 export 트리 구조(`docs/notion/`, Team BETA / AR Application Ideation 컨테이너 등)는 2026-06-08 정리되어 위 5개로 통합됨.
