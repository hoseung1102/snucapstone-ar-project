# AGENTS.md — Codex / 타 에이전트용 얇은 포인터 (Eagle Eye conquest AR)

> 이 파일은 Codex CLI 가 이 레포에서 자동 로드하는 프로젝트 지침이다. **본문은 일부러 짧다** — 정본을 중복하지 않는다.
> **정본(source of truth)은 [`CLAUDE.md`](CLAUDE.md) + `docs/`** 다. 같은 사실을 두 군데 두면 한쪽이 stale 해진다는 게 이 프로젝트의 반복된 실수였다. 의심되면 항상 코드 `file:line` 과 `docs/` 를 직접 확인하라 — 추정 금지.

---

## 무엇을 어디서 읽나 (정본 맵)

| 알고 싶은 것 | 정본 |
|---|---|
| **읽는 순서 전체 / 문서·갱신 규칙 / 코드·자산 구조** | [`CLAUDE.md`](CLAUDE.md) — 먼저 이걸 읽어라. Codex 도 이 규칙을 그대로 따른다. |
| **현황 (최신 빌드·매칭 아키텍처·다음 작업)** | [`docs/STATUS.md`](docs/STATUS.md) ← **현황 정본**. 현황이 바뀌면 여기 한 곳만 갱신. |
| **빌드 (Unity 2022.3.62f3 batchmode 명령·BUILD_TAG/OUTPUT_APK·설치·권한·versionName 검증·트러블슈팅)** | [`docs/dev-guide.md`](docs/dev-guide.md). 빌드 명령을 여기 복붙하지 말고 그 문서를 따라라. |
| **플랫폼 사실·제약·gotcha (RayNeo SDK API / NPU·QNN / OpenXR / 권한 / 검은화면·CDSP 충돌·8Hz SLAM judder)** | [`docs/platform-rayneo.md`](docs/platform-rayneo.md). 카메라/SLAM/NPU 위임을 만지기 전에 여기부터. |
| **실험 history (빌드별 시도·결과·교훈, commit 해시)** | [`docs/EXPERIMENTS.md`](docs/EXPERIMENTS.md). |
| **아키텍처·핵심 파일 맵 / 시스템 비전 / 결정 로그** | [`docs/vision.md`](docs/vision.md) · [`docs/client-spec.md`](docs/client-spec.md) · [`docs/dev-guide.md`](docs/dev-guide.md). |
| **eagle-monitor 디버그 대시보드** | `glasses-app/tools/monitor/eagle_monitor.py` + 스킬 `/eagle-monitor`. |
| 동결된 옛 핸드오프·기획안 | [`docs/archive/`](docs/archive/) — 현재 사양으로 인용 금지. |

> 충돌 시 우선순위 ([`docs/vision.md`](docs/vision.md) §10.3): 클라이언트 동작(카메라/NPU/모델/파이프라인) → `client-spec.md`, 비즈니스/시나리오/매칭 전략 → `vision.md`. 두 파일이 모순되면 코드·`docs/` 가 정본이다.

---

## 한 문단 컨텍스트

**Eagle Eye** — AR 글라스(**RayNeo X3 Pro**, Snapdragon AR1 Gen 1, Hexagon v73 NPU, **FP16 유닛 없음 → w8a8 양자화 필수**, Android 12)용 온디바이스 광고/정보 인프라의 v1 PoC. "경쟁사 상품을 손에 든 순간 비교 정보를 시야에 띄운다"(**conquest**)가 핵심. 빌드되는 안경 앱 = **`glasses-app/`** (Unity 2022.3.62f3 전용), 오프라인 도구/데이터 = **`offline/`** (Mac 측 Python·임베딩·ONNX/QNN). 데모 시연 대상은 **콜라/펩시 페트병 + 노트북** 한 장면 — 라면·마트로 좁히지 말 것. 매칭은 **color-brand**(CLIP 으로 category 분류 → 중앙 crop 평균 RGB 로 brand: 빨강→coca-cola / 파랑→pepsi) — 상세·근거는 [`docs/STATUS.md`](docs/STATUS.md) + [`docs/dev-guide.md`](docs/dev-guide.md).

---

## Codex 특이사항 (이 파일에만 있는 것)

- **샌드박스 승인** — Codex 샌드박스에서 **Unity 빌드·adb 실행은 네트워크/디바이스 접근 승인이 필요**할 수 있다 (장시간 batchmode 빌드, USB/무선 디바이스). 차단되면 사용자에게 승인을 요청하라. 빌드 절차 자체는 [`docs/dev-guide.md`](docs/dev-guide.md) 를 따른다 — 경로를 하드코딩하지 말고 그 문서의 명령을 쓴다.
- **ADB 는 안경팀과 공유 환경** — `install`/`push`/`force-stop`/`kill-server` 등 **writing 작업은 사전 허락**, reading 은 자유. **무선 ADB 를 끊지 말 것** (`adb kill-server` 직접 금지 — 자동 재시작 + `adb wait-for-device` 로 복귀). 상세는 [`docs/platform-rayneo.md`](docs/platform-rayneo.md).
- **`--force` / `--no-verify` 금지.** 비밀정보(`.env`, `*.keystore`, `secrets.*`, `credentials.*`, `sealed/`) 커밋 금지.
- **커밋 규칙** — 첫 줄 영문 동사(Add/Fix/Update/…), 본문 한국어 OK. 빌드 **성공 시에만** 커밋. 작업은 `main` 한 줄에서 (옛 `feature/*` 브랜치는 통합 완료, 이후 분기 금지 — [`CLAUDE.md`](CLAUDE.md) "브랜치/작업 흐름"). 푸터:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- **신뢰도 배지** — 주장/해시에 배지를 붙이는 컨벤션이 도입됐다 (🟢 git검증 / 🟡 코드검증 / 🟠 재구성 / 🔴 stale). 규칙은 [`CLAUDE.md`](CLAUDE.md) "문서 갱신 규칙". 인용 commit 해시는 `git cat-file -t <hash>` 로 실재를 확인하라.

> 중첩 디렉토리의 `AGENTS.md`(예: [`glasses-app/AGENTS.md`](glasses-app/AGENTS.md))도 Codex 가 함께 읽으니, 서브프로젝트 빌드 핵심은 그쪽을 본다.
