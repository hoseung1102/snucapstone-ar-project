<!-- source of truth. 노션에서 export 후 폐기 (2026-06-08). 갱신은 이 파일에 직접 한다. -->
<!-- 메인 문서: 비즈니스 정체성 + v1 PoC 사양 + 결정 로그. 클라이언트 기술 스택은 client-spec.md. 읽는 법은 루트 CLAUDE.md 참조. -->

# 📋 Eagle Eye — 시스템 비전 및 v1 PoC 기획

작성일: 2026-05-29
상태: Living document (대화 라운드마다 갱신)
작성 맥락: Claude 대화 세션 6라운드 합의 결과. 미래 세션 / 협업자 핸드오프용.
---
## ⚡ 현재 상태 스냅샷 (2026-06-08 갱신, v0.7.2 기준)

> 이 섹션만 읽으면 현 시점 상태 파악 가능. 상세 타임라인은 [진행 로그](progress-log.md), 결정 배경은 아래 결정 로그들 참조.

- **단계**: v1 PoC 시연 검증 단계 — **end-to-end 성공 확인됨** (v0.7.4, `aip.json` 에 OCR-driven `pepsi`/`coca-cola` 매칭 + conquest 광고 표시. brand fallback OFF 라 OCR 이 실제 라벨을 읽었다는 증거). 성공률은 **조준(병을 카메라 정중앙·라벨 정면)** 에 의존 — 그게 현재 실전 병목.
- **최신 버전**: **v0.7.4** (CLIP 중앙 crop query+ref + threshold 0.45). v0.7.3 = OCR 전처리(회전+crop+upscale) + 매칭 구조 분리(CLIP category → category 매칭 시에만 OCR brand) + CLIP-only 시 YOLO 컴포넌트 skip(시작 시 QNN 그래프 컴파일 제거). 상세는 [progress-log](progress-log.md) 2026-06-08 오후 섹션.
- **v0.7.x 시연으로 확인된 핵심 3가지** (반드시 인지):
  1. **온디바이스 CLIP sim 이 Mac 오프라인 대비 ~0.3 낮음** (전처리/양자화 차이). 오프라인 ref 검증 숫자로 실기기 예측 불가 → threshold 0.45 + 중앙 crop 으로 우회 중, 근본 규명 미완.
  2. **카메라 FOV ≠ 사용자 시선** — 콜라를 눈높이 정면에 들면 카메라(아래·팔 방향) 시야 밖이라 프레임에 안 잡힘. **팔 뻗어 몸 앞 아래(노트북 높이)** 에 둬야 잡힘 (calib 으로 확인).
  3. **CLIP category 변별력 약함** (cola·laptop 둘 다 0.5~0.6) — 단 brand=OCR 전용 + strict 라 오탐이 잘못된 광고로 안 이어짐.
- **(이전) v0.7.2 (hierarchical strict)** — git 커밋됨 (commit `e401310`). v0.5.6 이후 주요 변경:
  - v0.5.7 회전잠금 landscape + SBS / v0.5.8 카메라 Permission fix / v0.5.9 YOLO class+conf 로깅
  - v0.5.10 CLIP threshold **0.55** hardcode (false positive 차단) / v0.5.11 **CLIP-only 모드** (YOLO 우회)
  - v0.5.12 gyro 트리거 완화 (0.5 rad/s · 1.0s — 첫 트리거 2분+ 지연 fix) / v0.5.13 multi-ref (coke/pepsi 각 4장)
  - v0.5.14 top-3 매칭 + 영상 광고(mp4) / v0.5.15 topK=1 / v0.5.16 frame capture (ref 제작용)
  - v0.6.0 **MLKit OCR** 통합 / **v0.7.0 hierarchical** (CLIP category + OCR brand) 큰 refactor
  - v0.7.1 brand fallback chain (OCR → CLIP brand → default) / **v0.7.2 default fallback 제거** (strict — brand 1개여도 OCR 매칭 없으면 광고 X, false positive 방지)
- **매칭 아키텍처 (v0.7.x, 본질적 전환)**: CLIP zero-shot 만으로 coke vs pepsi 같은 fine-grained brand 분별이 본질적으로 불가능 (환경 신호가 brand 신호보다 dominant) → **계층화**: ① CLIP(중앙 crop) → category(콜라 vs 노트북) coarse ② category 매칭된 것만 OCR(MLKit) 로 라벨 글자 읽어 brand 확정 (deterministic, **유일 경로**) ③ ~~CLIP brand-specific fallback~~ **v0.7.3 기본 OFF** (`enableClipBrandFallback=false` — 환경 bias 로 펩시를 코크로 오판해서 차단; 데모 escape hatch 로만 ON)
- **추론 스택**: TFLite Interpreter + Qualcomm QNN delegate (Maven 2.47.0), Hexagon v73 NPU 100% 위임 + MLKit text-recognition v2 (~50ms)
  - YOLO11l w8a8 **640²** (`yolo11l_640_w8a8.tflite`, `INPUT_SIZE=640`) / MobileCLIP-S2 INT8 ~3ms / OCR ~50ms. ⚠️ AR1 은 cost-reduced 라 **FP16 unit 없음** → float 모델은 NPU 5.5% 위임뿐, w8a8 필수 (conf 10~50% 손실). YOLO 단일 ~15-30ms 는 320² proxy 추정값 (640 실측 아님); end-to-end 실측은 progress-log §6
  - latency: CLIP-only 35ms / YOLO+CLIP top-3 150ms / +OCR ~100ms — 전부 200ms 목표 내
- **시연 시나리오**: 콜라/펩시 페트병 conquest + 노트북 (라면+마트는 원 기획 — §4 주석 참조)
- **UI**: 진짜 AR 모드 (검은 배경 + overlay) 토글, 2D HUD 비교 카드 + 영상 광고(mp4) + Sponsored + 5s dismiss
- **알려진 이슈**: YOLO 가 안경 시점 bottle/book conf 매우 낮음 → CLIP-only 우회. w8a16 ORT path 는 보류. **온디바이스 CLIP ~0.3 격차 + 카메라 FOV 오프셋**(위 핵심 3가지) 미해결.
- **OCR 전처리 (v0.7.3, 커밋됨)**: 회전 보정(`videoRotationAngle` 자동, `rotationOverride` 강제) + 중앙 crop(`cropFraction` 0.55) + 업스케일(`upscaleFactor` 2.0) → MLKit. 디버그용 `ocr_crops/` 저장. 파일: `OCRExtractor.cs`, `MLKitOCR.java`(rotationDegrees 인자).
- **CLIP 중앙 crop (v0.7.4, 커밋됨)**: `ClipExtractor.cropFraction`(0.5) + `build_adversarial_db.py` `CLIP_CROP`(0.5) **반드시 일치**. ref 는 glob(`refs/<category>/*`) 자동 수집. environment-aligned ref(`refs/cola/dev_*`, `refs/laptop/dev_*`)는 환경 의존이라 중앙 crop 으로 대체된 보조 수단.
- **v0.7.5 계획 (brand 식별 하이브리드, 프로토타입 한정)**: Coca-Cola 필기체는 OCR·CLIP-brand 둘 다 못 읽음(시연 확인) → **brand = OCR("pepsi") OR 색(red→coca-cola / blue→pepsi)**, 단 **조준 가이드(중앙 박스) + 타이트 crop 으로 병 isolation** 전제(배경 혼입 시 색 무너짐). ⚠️ **색 휴리스틱은 일반해 아님**(coke/pepsi 색이 우연히 달라서 됨, 30 SKU·동색 브랜드엔 실패) — v1.5+ fine-tuned CLIP/로고 detector 로 대체. 상세 = 끝의 "색 휴리스틱" 결정 로그.
- **다음 작업**: ① v0.7.5 구현(위) + 조준 맞춰 재시연 ② **온디바이스↔Mac 0.3 격차 근본 규명**(`ClipExtractor.PreprocessTexture` 의 Y-flip/정규화/리사이즈를 `build_adversarial_db.py` 와 일치) ③ 카메라-시선 오프셋 보정/프리뷰 ④ fine-tuned CLIP(v1.5+) / 한글 OCR
- **도구**: 1인칭 시야 녹화 시스템 (`tools/recording/`, 맨 아래 결정 로그) / Mac 시뮬레이터 (`simulate_*.py`)
---
## 0. 이 문서의 목적
미래의 Claude 세션 또는 협업자가 프로젝트에 합류할 때, **기획안 v1.2(클라이언트 기술 스택)** 만으로는 얻을 수 없는 두 가지 맥락을 전달한다:
1. **시스템의 비즈니스 정체성과 전략적 결정의 배경** — 왜 광고 형식을 "정보"로 정의하기로 했는지, 왜 conquesting이 핵심인지 등
2. **v1 PoC의 정확한 스코프** — 무엇이 들어가고 무엇이 빠지는지, 그리고 빠진 것들이 왜 v2+로 미뤄졌는지
이 문서는 기획안 v1.2와 분리되되 보완 관계다:
- 기획안 v1.2 = **클라이언트(RayNeo X3 Pro) 위에서 동작하는 기술 스택과 파이프라인**
- 이 문서 = **그 위에 얹히는 광고 시스템의 정체성, 첫 PoC, 그리고 v2+ 백로그**
---
## 1. 프로젝트 정체성
### 1.1 Eagle Eye가 무엇인가
**"AR 글라스 시장이 형성되었을 때, 다양한 AR 상품 뒤에서 돌아가는 광고/정보 인프라 시스템."**
- 단독 소비자 상품이 아님. Meta의 인스타그램 광고 알고리즘에 비유하면 — 인스타그램이 사용자 상품이라면, 알고리즘은 그 뒤의 인프라다. Eagle Eye는 후자.
- 비즈니스 모델: 시스템 라이선싱 / 광고 인벤토리 운영 / 데이터 자산.
- 비교 대상: Meta 광고(수익 사이즈), Google 검색 광고(인터랙션 모델), Amazon(커머스 attribution).
### 1.2 Eagle Eye가 아닌 것 (혼동 방지)
- ❌ "RayNeo X3 Pro에서 돌아가는 상품" — 글라스는 PoC 플랫폼일 뿐, 최종 목표가 아님
- ❌ "마트 쇼핑 도우미 앱" — 마트 진열대는 첫 PoC 시나리오일 뿐
- ❌ "라면 추천 시스템" — 라면은 첫 PoC 카테고리일 뿐
- ❌ "전통적 광고 시스템" — 형식이 다름 (2.2 참조)
**중요**: v1 PoC가 라면 + 마트라는 사실이 프로젝트의 방향을 라면이나 마트로 좁히는 것이 아님을 반드시 인지할 것. v1은 시스템의 핵심 가치를 가장 빠르게 검증할 수 있는 단일 시나리오일 뿐.
### 1.3 시장의 전제
- AR 글라스가 상용화되어 시장이 형성되고, 사람들이 일상적으로 쓰는 AR 상품이 나오는 것을 전제
- 그 시점에 Eagle Eye를 "시장 표준 광고/정보 인프라"로 만드는 것이 목표
- 글라스 외에도 향후 확장 가능 (스마트폰 카메라, 자동차 AR HUD 등) — 단, v1에서는 글라스 시나리오에 집중
---
## 2. 시스템 비전: AR Contextual Information Layer
### 2.1 "광고"라는 단어의 함정
프로젝트 초기에 "AR 광고 시스템"으로 정의되었으나, outward-facing 용어로는 부적절하다는 결론에 도달함. 이유:
- AR 글라스에 광고를 "띄우는" 행위는 사용자 시야에 시각적 노이즈를 만듦 → 거부감 → 채택 실패
- "광고"라는 형식 자체보다 **"사용자가 원하지 않는 콘텐츠를 시스템이 푸시"** 하는 것이 문제의 본질
- 광고도 사용자가 원하는 정보 형태로 표현되면 노이즈가 아닌 가치가 됨 (Google 검색 광고 사례: 검색결과 형식의 광고는 거슬리지 않음)
### 2.2 새로운 정체성
**"AR Contextual Information Layer"** — 사용자가 보는 객체에 맥락 정보를 부여하는 시스템.
광고는 사라지지 않는다. 형식만 바뀐다:
- 이전 사고방식: "Pepsi 광고" (브랜드 푸시)
- 새 사고방식: "동가격대 Pepsi — 평점 4.5★ / 옆 칸 좌측 2번째" (비교 정보. Pepsi가 입찰 1위라서 노출됨)
본질은 동일 (광고주가 노출 비용 지불). 형식이 정보. 사용자 인지: "노이즈" → "유용한 정보".
### 2.3 정보 슬롯의 종류 (광고 인벤토리 후보)
시야 안에 띄울 수 있는 정보 형식들:
1. 상품 정보 (가격, 평점, 영양, 알레르기, 원산지)
2. **비교 정보** (다른 쇼핑몰 가격, 동가격대 평점 높은 대안) ← **v1 메인 슬롯**
3. 사회 정보 (친구 구매 이력, 지역 인기)
4. 시간 정보 (할인 기간)
5. 연관 정보 (레시피, 사용법, 매칭 추천)
6. 번역 정보 (외국 제품)
7. 건강/지속가능성 정보
각 슬롯은 광고주 입찰의 단위가 될 수 있다. v1은 #2(비교 정보)만 구현.
### 2.4 시장 사이즈 비교
- "광고만" 정체성 → Meta 사이즈 (~$700B)
- "정보 layer" 정체성 → Google + Amazon 사이즈 (~$5T+)
- 사용자의 비전 "Meta 이상의 수익"은 후자에서 자연스럽게 달성됨
---
## 3. 핵심 전략 결정사항
### 3.1 Conquest를 메인 매칭 전략으로 채택
**확정.** 다른 매칭 옵션 대비 이유:
<table header-row="true">
<colgroup>
<col width="169">
<col width="85">
<col width="457">
</colgroup>
<tr>
<td>매칭 방식</td>
<td>채택 여부</td>
<td>이유</td>
</tr>
<tr>
<td>동일 상품 (exact match)</td>
<td>❌</td>
<td>"이미 선택한 사람"에게 광고 → 가치 없음, 사용자 거부감</td>
</tr>
<tr>
<td>임의 유사 카테고리</td>
<td>❌</td>
<td>기존 contextual targeting과 차별성 X, AR이 필요 없음</td>
</tr>
<tr>
<td>**Conquest (경쟁사 공략)**</td>
<td>✅ **메인**</td>
<td>최고가 슬롯, AR이 가능케 하는 고유 모먼트 (오프라인 결정 직전)</td>
</tr>
<tr>
<td>Complement (보완재)</td>
<td>보조 (v2+)</td>
<td>일상적 안정 수익원</td>
</tr>
<tr>
<td>Upgrade (업셀)</td>
<td>보조 (v2+)</td>
<td>프리미엄 브랜드 슬롯</td>
</tr>
</table>
Conquest의 핵심 가치 명제: **사용자가 경쟁사 상품을 손에 들고 있는 순간을 잡는다.** 이는 오프라인 결정 직전 — 디지털 광고가 절대 못 하던 모먼트. 단가가 가장 높을 수밖에 없는 이유.
### 3.2 Product Relationship Graph (제품 관계 그래프)
시스템의 **데이터 자산**. Meta가 social graph로 광고하듯, Eagle Eye는 "제품 간 관계 그래프"로 광고한다.
- 노드: 개별 상품 (SKU 단위)
- 엣지: 관계 (경쟁/보완/상위모델 등). 가중치 = 입찰가 / 컨버전율
- v1: 라면 30 SKU에 대한 conquest 관계 수기 입력
- v2+: 광고주 입찰 데이터로 자동 강화 → 네트워크 효과로 moat 형성
### 3.3 Ambient Interest Profile (AIP)
**passing-by(스쳐가기) 데이터를 실시간 광고 트리거가 아닌, 온디바이스 누적 프로필로 활용.**
핵심 통찰:
- 스쳐가는 순간의 사용자 인지 대역폭은 0에 가까움 → 광고 표시하면 노이즈
- 그러나 시선이 닿았다는 사실은 무의식 attention 신호 → 데이터로 가치 있음
- → **표시는 안 하되, 로그는 남긴다.** 누적된 AIP가 추후 dwell 모먼트에서 입찰 단가를 끌어올림
이는 Meta조차 못 하는 영역 — Meta는 "클릭/시청"만 알지만, AR은 "잠재의식적으로 본 것"까지 캡처 가능.
**v1에서의 위치**: 데이터 형식 정의만 (스키마). 실제 활용은 v2+.
### 3.4 Ad Slot Ladder (광고 슬롯 계층)
의도 신뢰도에 비례한 UI 강도 매핑. 거슬림 정도는 의도 신뢰도에 비례한다는 원칙.
<table header-row="true">
<tr>
<td>레벨</td>
<td>의도 신호</td>
<td>UI</td>
<td>광고 유형</td>
</tr>
<tr>
<td>L0</td>
<td>스쳐감 (<0.5초)</td>
<td>표시 없음</td>
<td>AIP 로깅만</td>
</tr>
<tr>
<td>L1</td>
<td>살짝 머무름 (0.5~1.5초)</td>
<td>시야 가장자리 미세 표시</td>
<td>브랜드 인지형</td>
</tr>
<tr>
<td>L2</td>
<td>짧은 dwell (1.5~2초)</td>
<td>객체 옆 작은 hint</td>
<td>가벼운 정보</td>
</tr>
<tr>
<td>L3</td>
<td>완전 dwell (2초+)</td>
<td>풀 anchor (기획안 v1.2의 anchor)</td>
<td>비교 정보, conquest</td>
</tr>
</table>
**v1은 L3만 구현.** 다른 레벨은 v2+.
### 3.5 3-Tier Interaction Model
Dense shelf에서 "한 상품 정확히 응시" 식별의 본질적 한계(eye tracking이 있어도 ±1cm 오차로 진열대에서 인접 1~2개 사이 혼동)를 회피하기 위한 인터랙션 모델. 식별 신뢰도에 따라 콘텐츠 입자를 조정.
<table header-row="true">
<tr>
<td>Tier</td>
<td>사용자 상태</td>
<td>식별 단위</td>
<td>식별 정확도</td>
<td>콘텐츠</td>
</tr>
<tr>
<td>1 (Browsing)</td>
<td>진열대 앞에 서있음 (50~150cm)</td>
<td>섹션 (예: 라면 코너)</td>
<td>95%+</td>
<td>코너 단위 정보 / 광고</td>
</tr>
<tr>
<td>2 (Reaching)</td>
<td>손을 뻗는 중</td>
<td>손 끝 후보 1~3개</td>
<td>80~90%</td>
<td>hint label</td>
</tr>
<tr>
<td>3 (Inspecting)</td>
<td>상품 들고 봄 (<30cm)</td>
<td>단일 SKU</td>
<td>95%+</td>
<td>풀 비교 카드 (conquest)</td>
</tr>
</table>
**v1은 Tier 3만 구현.** 이유:
- hand tracking SDK 의존성 우회 (v1에서 검증 안 함)
- 식별 신뢰도 최고
- 비즈니스 가치 최고 (결정 직전 모먼트)
### 3.6 100% 온디바이스 추론 원칙
- 카메라 프레임은 외부 서버로 절대 전송 안 함
- AIP는 디바이스 로컬 저장
- 단, 광고주 입찰 결과 / 광고 콘텐츠 다운로드는 서버 통신 허용
- 동기: 프라이버시 + 한국 개인정보보호법 / EU GDPR 대응
- 단, "100% 온디바이스 식별"이 절대적 요구사항은 아님. **"원본 프레임이 외부로 안 나간다"가 본질.** embedding-only 서버 쿼리는 옵션으로 열려있음 (수십만 SKU 확장 시)
---
## 4. v1 PoC 사양
### 4.1 시나리오
**"한국 마트에서 사용자가 라면을 손에 들고 보는 순간, 경쟁 라면의 비교 정보를 시야에 표시한다."**

> **갱신 (2026-06-07 팀 회의)**: 실제 v1 시연은 **"코카콜라 페트병을 들면 펩시 광고가 뜬다"** (콜라/펩시 conquest) 로 진행. 같은 Tier 3 Inspecting + conquest 구조의 부분집합 — 시스템 정체성/전략 불변. 라면+마트는 원 기획 시나리오로 보존 (v1.x 복귀 후보).

### 4.2 스코프 (IN)
- 카테고리: ~~라면 1종~~ → **음료 (콜라/펩시 페트병) + 노트북** (2026-06-07 변경 — 캔은 COCO bottle 인식 불안정해 페트병 채택, 해당 결정 로그 참조)
- 시나리오: **마트 진열대 — Tier 3 (Inspecting)** (v1 한정)
- 매칭: **1:1 conquest** (식별 상품당 경쟁사 1개)
- 매칭 데이터: 30 SKU의 수기 큐레이션 conquest 매핑
- UI: 비교 카드 (가격 / 평점 / 차별점 / 위치 hint) + Sponsored 라벨
- 클라이언트: RayNeo X3 Pro (또는 XREAL 폴백)
- 추론: 100% 온디바이스 (YOLO + MobileCLIP, 기획안 v1.2 파이프라인)
### 4.3 스코프 (OUT, v2+ 백로그)
- Tier 1 (Browsing, 섹션 인식)
- Tier 2 (Reaching) — hand tracking SDK 의존
- 다른 카테고리 (음료, 과자, 즉석커피 등)
- 다른 시나리오 (메뉴판, 해외 상품, 거리 등)
- AIP **실제 활용** (v1에서는 스키마 정의만, 활용은 v2+)
- Ad Slot Ladder L0/L1/L2
- 실시간 광고주 입찰 (v1은 하드코딩된 우선순위)
- Vector DB 대규모 (v1은 30 SKU만)
- Multi-product disambiguation (v1은 단일 객체 시나리오만)
- Complement / Upgrade 매칭
### 4.4 PoC 성공 기준 (잠정) — 상태 갱신 2026-06-08
- ~~라면 30 SKU~~ 식별 정확도 80%+ → **부분 달성**: 콜라/펩시 2 product 기준 Mac 검증 8/8 = 100% (안경 시점 일반화는 검증 중 — multi-ref 작업 진행)
- end-to-end 레이턴시 **200ms** → ✅ **달성**: NPU 추론 합계 ~18-30ms 실측 (YOLO ~15-30ms + CLIP ~3ms), 목표 대비 큰 여유
- 사용자 5~10명 테스트에서 **"유용함" 응답 50%+** → ⏳ 미실시
- (옵션) Conquest 슬롯에서 30% 이상이 비교 정보를 2초 이상 응시 (engagement) → ⏳ 미측정
→ **최종 성공 기준은 사용자가 확정해야 함** (열린 결정사항 — 현재 작동 기준: "콜라를 들면 펩시 비교 카드가 뜬다")
### 4.5 Tier 3 트리거 정의 (기획안 v1.2 보완)
기획안 v1.2의 IMU 트리거 (Tier 1/2 적합)와 별도로 추가:
```javascript
[Tier 3 진입 조건]
- 깊이: 단일 객체가 30cm 이내
- 객체 크기: 화면의 25% 이상
- 객체 안정성: 1.5초 이상 view 중앙 유지
- 헤드 모션: 회전 가능 (라벨 확인 모션 허용)
```

> **갱신 (2026-06-07)**: "객체 크기 25%+" 는 시연에서 너무 strict (test_cans 사전 측정 8/15 통과) → **5% 로 완화** (v0.5.6, minAreaRatio=0.05). IMU 트리거도 v0.5.12 에서 0.5 rad/s · 1.0s 로 완화 (startup sensor noise 로 첫 트리거 2분+ 지연 fix). v0.5.11 부터는 CLIP-only 모드라 YOLO area 필터 자체를 우회.

### 4.6 UI 비교 카드 (잠정 디자인)
```javascript
┌─────────────────────┐
│ 진라면 매운맛       │  ← 식별된 상품 (확인용)
│ ─────────────────── │
│ 신라면 4.4★ ₩980    │  ← Conquest 비교 정보
│ "더 안 매운 편"     │  ← 차별점 한 줄
│ 같은 코너 좌측 2칸  │  ← 진열 위치 hint
└─────────────────────┘
[Sponsored]            ← 광고 disclosure
```
---
## 5. 데이터 수집 계획
### 5.1 필요한 4종 데이터
1. **상품 이미지** (CLIP 임베딩용): 30 SKU × 7장 = 약 210장. 마트에서 직접 촬영.
2. **상품 메타데이터** (UI 표시용): 30 SKU × \{브랜드, 상품명, 가격, 평점, 차별점, 위치\}. 수기 입력 (1~2시간).
3. **Conquest 매핑** (관계 그래프): 30 SKU 간 1:N 경쟁 관계 수기 입력 (30분).
4. **검증/테스트 영상** (정확도 측정용): RayNeo X3 Pro로 마트 5곳 촬영 (2~3시간) + 라벨링 (5~10시간).
### 5.2 데이터 수집 원칙
- v1은 본인이 직접 수집 (외주 X) — 데이터의 한계를 손으로 체득하기 위함
- 마트 1곳에서 끝까지 만든 뒤, 검증 단계에서만 다른 마트 추가
- 30 SKU로 시작, 작동 확인 후 50 SKU로 확장 가능
---
## 6. 빌드 페이즈 (기획안 v1.2 매핑) — 상태 갱신 2026-06-08

| Phase | 기간 | 작업 (기존 + Conquest 추가) | 상태 |
|---|---|---|---|
| **0** | ~~진행중~~ | 하드웨어 검증 + ~~30 SKU~~ 데이터셋 수집 | ✅ 완료 (콜라/펩시 refs 로 변형) |
| **1** | 1~2주 | SDK/Anchor 검증, 모델 변환 + **상품 이미지 임베딩** | ✅ 완료 (TFLite+QNN delegate 로 변형 — SNPE/DLC 경로 폐기, 결정 로그 참조) |
| **2a** | 3~4주 | YOLO+CLIP 통합 + **Tier 3 트리거 + Conquest 매칭 로직** | ✅ 완료 (v0.3.6~v0.5.x. 단 v0.5.11 부터 CLIP-only 변형) |
| **2b** | 5~6주 | 200ms 레이턴시 달성 + **비교 카드 UI** | ✅ 완료 (추론 ~18-30ms, 비교 카드 v0.5.2) |
| **3** | 2~3개월 | 실환경 검증 + **마트 사용자 테스트** | ⏳ 진행 전 (현재: 실내 시연 검증 단계) |
---
## 7. 하드웨어 / SDK 제약 — 전부 확인됨 (2026-06-08 갱신)

| 항목 | 상태 | v1에 미치는 영향 |
|---|---|---|
| RayNeo X3 Pro Spatial Anchor API 지원 | ✅ 부분 지원 확인 (R7, 2026-05-29 — 세션 내 6DoF ✅ / cross-session persistence ❌) | v1 은 2D HUD 채택으로 의존성 자체를 제거 (결정 로그 참조) |
| Eye tracking 지원 | 없음 (확정 가정 유지) | 정밀 응시 식별 불가 → 3-Tier 모델 채택 근거 |
| Hand tracking SDK | ❓ 미확인 (유일하게 남은 항목) | v1엔 불필요 (Tier 3만), v2+엔 필수 |
| MobileCLIP 변환 | ✅ 완료 — MobileCLIP-S2 INT8 TFLite (Hexagon v73, 2.12ms). SNPE 가 아니라 TFLite+QNN delegate 경로 | EfficientNet 대체 불필요 |
| 200ms 레이턴시 실측 | ✅ 달성 — YOLO11l ~15-30ms + CLIP ~3ms (안경 실측) | Vector DB 사전 필터링 불필요 |
| **NPU 정밀도 (추가 발견)** | ⚠️ Hexagon v73 이지만 **cost-reduced AR1 — FP16 vector unit 없음**, INT8/INT16 만 | float 모델은 NPU 5.5% 위임뿐 → w8a8 필수. 양자화 conf 10~50% 손실이 detection recall 이슈의 근원 |
---
## 8. 열린 결정사항 (2026-06-08 갱신)
- [x] UI 비교 항목 우선순위 → **결정** (v0.5.2 비교 카드: ① 식별 상품 ② 브랜드+평점+가격 ③ 차별점 한 줄 ④ 위치 hint)
- [x] Sponsored 라벨 표시 여부 → **표시** (v0.5.1, 한국 광고법 준수)
- [x] 자동 dismiss 타이밍 → **5초** (v0.5.1. 시선 이탈 즉시 dismiss 는 eye tracking 없어 미구현)
- [ ] PoC 정량적 성공 기준 확정 — 미확정 (현재 작동 기준: "콜라 들면 펩시 비교 카드", §4.4 상태 참조)
- [ ] 데이터 수집 일정 (Phase 0~1 구간) — 시나리오가 음료 시연으로 변경되어 라면 30 SKU 수집은 보류
- [ ] 마트 1곳 선정 (이마트/홈플러스/롯데마트) — 동상 (Phase 3 진입 시 재개)
---
## 9. v2+ 백로그 (현재 미구현, 향후 확장)
### 9.1 카테고리 확장
- 음료 (탄산/주스/생수/RTD커피)
- 과자
- 즉석식품
- 가공식품 전반
- (장기) 가전, 의류, 화장품
### 9.2 시나리오 확장
- 메뉴판 (인기 메뉴, 알레르기, 리뷰)
- 해외 상품 (번역, 한국 판매처)
- 거리/매장 외부 (간판, 광고판)
- 의류 매장 (코디 추천, 사이즈 매칭)
- 요리 중 (재료, 레시피)
- 사적 공간 (시스템 침묵 — 거부감 zero 보장)
### 9.3 시스템 확장
- Tier 1 (섹션 단위 인식 및 코너 광고)
- Tier 2 (hand tracking 기반 후보 좁히기)
- AIP 실제 활용 (누적 데이터 → 입찰 단가 boost)
- Ad Slot Ladder L0~L2 구현
- Complement / Upgrade 매칭 추가
- 실시간 광고주 입찰 시스템 (RTB)
- 광고주 self-serve SDK / 대시보드
- Vector DB 대규모 운영 (수만~수십만 SKU, hierarchical fallback)
- 어트리뷰션 시스템 (AR 광고 → 오프라인 구매 연결)
### 9.4 하드웨어 확장
- 글라스 외 폼팩터 (스마트폰 카메라, 자동차 HUD)
- 다른 글라스 모델 (XREAL, Meta Orion 등)
---
## 10. 운영 / 협업 원칙
### 10.1 100% 온디바이스 (프라이버시)
원본 카메라 프레임 외부 전송 금지. AIP 로컬 저장. 광고 콘텐츠 / 메타데이터 / 입찰 결과만 서버 통신.
### 10.2 사용자 신뢰가 광고 단가를 결정
"정보가 유용하다"는 사용자 신뢰가 곧 시스템의 자산. 잘못된 정보 / 거슬리는 표시는 단가 폭락 → 광고주 이탈 → 시스템 죽음. 따라서:
- 식별 신뢰도가 낮으면 콘텐츠 입자를 거칠게 함 (잘못 띄우느니 안 띄움)
- L0~L3 슬롯 강도는 의도 신뢰도에 엄격히 비례
- 사용자가 광고로 인식하더라도 "유용한 정보"로 인식되는 형식 유지
### 10.3 두 문서의 책임 분리
기획안 v1.2는 **클라이언트 기술 스택**의 진실의 원천. 이 문서는 **시스템 비즈니스 정체성과 v1 사양**의 진실의 원천. 충돌 시:
- 클라이언트 동작 관련 → 기획안 v1.2 우선
- 시스템 비즈니스 / 시나리오 / 매칭 전략 → 이 문서 우선
---
## 부록 A: 대화 라운드 요약 (참고)
이 문서는 2026-05-29 Claude 대화 6라운드를 통해 합의된 결과:
1. **R1 — 시스템 비전 명확화**: Eagle Eye는 시스템 인프라이지 글라스 앱이 아님. RayNeo X3 Pro는 PoC 플랫폼. 사용자가 기획안 v1.2 공유.
2. **R2 — 매칭 전략 결정**: "동일 매칭 vs 임의 유사" 이분법 거부. Conquest를 메인으로 채택. Product Relationship Graph 개념 도입. 사용자: "경쟁사 공략으로 가자."
3. **R3 — Passing-by 처리**: 스쳐가는 모먼트는 광고 표시 X, 로깅만. AIP 도입. Ad Slot Ladder L0~L3 정의. 사용자: "단일 dwell만으로 광고 띄우는 건 노이즈. AIP에 누적하자."
4. **R4 — "광고" → "Contextual Information Layer" 재정의**: 광고를 정보 형식으로 표현. 시장 사이즈 Meta → Google+Amazon 사이즈로 확장 가능.
5. **R5 — 첫 시나리오 선정**: 마트 진열대 채택. Dense shelf 정밀 식별의 본질적 한계 → 3-Tier Interaction Model 도입. "한 상품 응시" 목표 자체를 거부.
6. **R6 — v1 PoC 확정**: 카테고리 = 라면 (1개). 데이터 수집 4종 정의. 이 문서 생성. **"라면이 프로젝트 방향이 아님" 사용자 명시.**
7. **R7 — 개발 시작 결정 (2026-05-29)**: RayNeo 답변 수신 — Spatial Anchor "부분 지원" (세션 내 6DoF world-locked rendering ✅ / cross-session persistence ❌). **v1에 영향 없음** (단일 세션 anchor만 필요). **RayNeo X3 Pro 진행 확정, XREAL 전환 불필요**. 개발 프레임워크 = Unity ARDK 공식 경로 ([https://rayneo-en.gitbook.io/rayneo-devdoc/x-series/unity-sdk](https://rayneo-en.gitbook.io/rayneo-devdoc/x-series/unity-sdk)). 사용자 배경 = Web only → Unity AR 학습 곡선 4~5주 (병행). 개발 머신 = Mac M1/M2 Apple Silicon. §7 갱신, §11 신설.
---
## 부록 B: 미래 세션을 위한 안내
이 문서를 처음 읽는 Claude 세션 / 협업자에게:
- **먼저** 기획안 v1.2 페이지를 읽을 것. 클라이언트 기술 스택 / 파이프라인 / 하드웨어 제약 / Phase 0~3 로드맵이 거기 있다.
- **그 다음** 이 문서를 읽을 것. 시스템 비즈니스 정체성 / v1 PoC 사양 / v2+ 백로그가 여기 있다.
- **두 문서가 충돌하면** 섹션 10.3 참조.
- **사용자와 대화할 때**: 라면이나 마트는 v1 PoC 한정이라는 점 절대 잊지 말 것. 프로젝트 방향을 "마트 라면 추천 앱"으로 좁히면 안 됨.
- **추가 라운드 결과 반영**: 새로운 전략 결정이 합의되면 이 문서를 갱신할 것. Living document.
---
## 11. 개발 환경 / Week 1 Hello World 체크리스트
(2026-05-29 추가. R7 결정 반영.)

> ✅ **완료 (2026-06-08 갱신)**: Week 1 목표 전부 통과 — adb 연결, Unity 빌드/설치, 안경 실행, 카메라/자이로/NPU 추론까지 동작 (v0.2.x~v0.5.x). 이하는 기록 보존용. 단 Week 2~ 표의 "SNPE/DLC 변환" 경로는 실제로는 **TFLite + QNN delegate** 로 대체됨 (LiteRT 피벗 결정 로그 참조). RayNeo 전용 ARDK 도 결국 미사용 — 표준 Unity Android 빌드 + WebCamTexture 로 충분했음.
### 11.1 개발 프레임워크 결정: Unity ARDK
**확정.** RayNeo 공식 답변에서 권장된 경로:
- 공식 문서: [https://rayneo-en.gitbook.io/rayneo-devdoc/x-series/unity-sdk](https://rayneo-en.gitbook.io/rayneo-devdoc/x-series/unity-sdk)
- 형태: 공개 GitBook 문서 사이트. 별도 회원가입 없이 열람 가능 (SDK 다운로드 절차는 문서 내 확인 필요)
**선택 근거:**
- RayNeo가 공식 권장하는 유일한 ARDK 경로
- 6DoF rendering / 카메라 / Spatial 좌표계가 1급 API로 제공
- ML 추론 (YOLO / MobileCLIP) 은 Unity Native Plugin (C++) 으로 분리 작성 → 모듈성 확보
- 대안 (Android Native, WebXR) 대비 PoC 단계 빌드 속도 우위
**대안 옵션 (참고):**
<table header-row="true">
<tr>
<td>옵션</td>
<td>채택 여부</td>
<td>비고</td>
</tr>
<tr>
<td>Unity ARDK</td>
<td>✅</td>
<td>공식 경로</td>
</tr>
<tr>
<td>Android Native (Kotlin + NDK)</td>
<td>❌</td>
<td>AR 렌더링 직접 구현 부담, RayNeo 권장 X</td>
</tr>
<tr>
<td>WebXR</td>
<td>❌</td>
<td>NPU 접근 불가, RayNeo 미지원</td>
</tr>
</table>
### 11.2 사용자 배경 / 학습 곡선
**기존 경험**: Web only (HTML/CSS/JS 기반).
Unity AR 개발 학습 곡선 (Web 배경 기준 추정):
<table header-row="true">
<tr>
<td>학습 영역</td>
<td>예상 기간</td>
</tr>
<tr>
<td>C# 기본 (JS와 유사하지만 정적 타입)</td>
<td>1주</td>
</tr>
<tr>
<td>Unity Editor / GameObject 패러다임</td>
<td>1~2주</td>
</tr>
<tr>
<td>Unity AR 기본 (카메라, 좌표계, 렌더링)</td>
<td>1주</td>
</tr>
<tr>
<td>RayNeo SDK 특이사항</td>
<td>며칠</td>
</tr>
<tr>
<td>Native Plugin (C++ for SNPE)</td>
<td>1주 (필요 시)</td>
</tr>
<tr>
<td>**합계**</td>
<td>**4~5주 학습 + 빌드 병행**</td>
</tr>
</table>
학습은 빌드와 병행. 첫 동작 빌드는 1~2주 내 가능.
**Claude 보조 모델**: 코드 작성/디버깅/명령어/스크립트 작성을 Claude가 짝꿍 역할. 사용자는 빌드 실행/관찰/의사결정 담당. (§11.7 참조)
### 11.3 개발 머신
- Mac M1/M2 (Apple Silicon)
- Unity Hub에서 **Apple Silicon 네이티브** Editor 설치 필수 (Intel 버전 X — Rosetta 경유 시 빌드 속도 저하)
- Android Studio: Apple Silicon 네이티브 정상 지원
- 빌드 타겟은 Android ARM64 → 크로스컴파일. Mac CPU와 무관
### 11.4 사용자 준비 체크리스트
**A. 하드웨어 & 연결**
- [x] RayNeo X3 Pro
- [ ] USB-C 데이터 케이블 (데이터 지원 — 충전 전용 X)
- [ ] Mac에서 `adb devices` 실행 시 기기 인식 확인 (개발자 옵션 / USB 디버깅 활성화 필요할 수 있음)
**B. 개발자 계정 / SDK 접근**
- [ ] RayNeo 개발자 문서 접근: [https://rayneo-en.gitbook.io/rayneo-devdoc/x-series/unity-sdk](https://rayneo-en.gitbook.io/rayneo-devdoc/x-series/unity-sdk) (공개, 별도 가입 불필요 추정. SDK 다운로드 절차는 문서 내 확인)
- [ ] Qualcomm Developer 계정 (SNPE SDK 다운로드용, 무료)
- [ ] Qualcomm AI Hub 접근 신청 (MobileCLIP SNPE 변환 검증용 — §7의 두 번째 미결 항목 해결에 필요)
- [ ] Unity 무료 Personal 계정
**C. 개발 도구 설치 (Mac M1/M2)**
- [ ] Android Studio (ARM64) + Android SDK + NDK
- [ ] Unity Hub (ARM64) + Unity LTS — RayNeo SDK 권장 버전 문서에서 확인
- [ ] ADB PATH 등록 (터미널에서 `adb` 바로 실행 가능하게)
- [ ] Git
**D. 데이터 (Phase 0 병행 시작)**
- [ ] 라면 30 SKU 리스트 확정
- [ ] 시범 마트 1곳 선정 (이마트 / 홈플러스 / 롯데마트)
- [ ] 첫 데이터 수집 일정 (1~2시간)
**E. 정보 확인**
- [x] RayNeo Spatial Anchor API 답변 수신 (2026-05-29) — 부분 지원, v1 진행 가능
- [ ] RayNeo Unity SDK 문서 한 번 훑기 (Anchor / Camera / Hand / Render 섹션)
- [ ] Snapdragon AR1 Gen 1 NPU 지원 모델 포맷 / 권장 토폴로지 확인
### 11.5 Week 1 — Hello World 단계 (Day-by-Day)
<table header-row="true">
<tr>
<td>Day</td>
<td>작업</td>
<td>Deliverable</td>
</tr>
<tr>
<td>1~2</td>
<td>ADB 연결 확인, 글라스 OS/메모리 정보 수집, 개발자 옵션 / USB 디버깅 활성화</td>
<td>`adb shell` 진입 성공, `adb devices` 인식</td>
</tr>
<tr>
<td>3~4</td>
<td>Android Studio + Unity Hub + Unity LTS 설치, RayNeo SDK 패키지 import, Android 빌드 타겟 설정</td>
<td>Unity Editor에서 RayNeo 샘플 씬 로드 성공</td>
</tr>
<tr>
<td>5~6</td>
<td>RayNeo 샘플 (큐브 띄우기) 빌드 → APK → `adb install` → 글라스에서 실행</td>
<td>시야에 가상 큐브 표시 확인</td>
</tr>
<tr>
<td>7</td>
<td>**6DoF World-locked Rendering 검증** — 가상 객체 배치 후 사용자 이동 시 객체가 현실 공간 같은 위치 유지하는지</td>
<td>**§4.5 Tier 3 anchor 동작 가능성 ✅ 또는 ❌**</td>
</tr>
</table>
**Week 1 종료 조건**: Day 7에서 world-locked 렌더링 OK = 시스템 핵심 기반 확보. v1 본격 진행.
**실패 시 분기**: Day 7에서 6DoF 렌더링 안정성 미달 → RayNeo follow-up (사용한 API + 에러 로그 포함) → 1~2일 추가 디버깅 → 그래도 미해결 시 XREAL Air 2 Ultra 전환 결정.
### 11.6 Week 2 이후 개요
<table header-row="true">
<tr>
<td>주차</td>
<td>작업</td>
<td>검증 항목</td>
</tr>
<tr>
<td>Week 2~3</td>
<td>RayNeo Camera API 접근, YOLOv11n → ONNX → DLC → SNPE INT8 변환, NPU 추론 실행</td>
<td>카메라 프레임 + YOLO 추론 동작, 레이턴시 10~20ms 목표</td>
</tr>
<tr>
<td>Week 3~4</td>
<td>MobileCLIP SNPE 변환 (§7의 두 번째 미결 항목), 5 SKU Vector DB, end-to-end 매칭</td>
<td>CLIP NPU 동작 OR EfficientNet 대체 결정</td>
</tr>
<tr>
<td>Week 4~5</td>
<td>Tier 3 트리거 (§4.5) 구현, 비교 카드 UI (§4.6)</td>
<td>Tier 3 트리거 false positive/negative 측정</td>
</tr>
<tr>
<td>Week 5~6</td>
<td>30 SKU 확장, 실제 마트 1곳 테스트</td>
<td>식별 정확도 80%+ 목표 (§4.4)</td>
</tr>
</table>
기획안 v1.2의 Phase 1~2 와 정렬.
### 11.7 Claude 보조 / 사용자 분담 모델
**Claude 역할 (짝꿍):**
- Unity C# 스크립트 작성 / 디버깅
- SNPE 변환 명령어 / 양자화 스크립트 작성
- ONNX export / 모델 최적화 코드
- ADB 명령어 / 로그 분석
- 비교 카드 UI Unity Canvas 레이아웃 작성
- Native Plugin 빌드 환경 (C++ wrapper for SNPE) 구성
- 학습 자료 / 개념 설명
**사용자 역할:**
- 글라스 실제 빌드 / 실행 / 관찰
- 사용자 테스트 / 마트 현장 검증
- 데이터 수집 / 라벨링
- 시스템 의사결정 / 디자인 선택 / 우선순위 결정
- 외부 커뮤니케이션 (RayNeo / Qualcomm / 광고주 후보)
### 11.8 Week 1 진입 직전 결정 사항 (다음 라운드)
- [ ] 사용자가 옵션 A (직접 학습 + Claude 보조) 채택 확인
- [ ] RayNeo 개발자 문서 사이트 접속 → SDK 다운로드 절차 확인
- [ ] Day 1 일정 잡기 (ADB 연결 시도 시점)
---
## 기술 결정 로그 — CLIP 모델 선택 (2026-06-06)
### 결정
**ONNX export 대상으로 MobileCLIP-S2 선정.**
### 검토한 후보 3개
<table header-row="true">
<tr>
<td>모델</td>
<td>파라미터</td>
<td>입력</td>
<td>임베딩 차원</td>
<td>강점</td>
<td>약점</td>
</tr>
<tr>
<td>**MobileCLIP-S2** ✅ 선정</td>
<td>~5M</td>
<td>256×256</td>
<td>512</td>
<td>기획안 v1.2 명시 모델, 모바일 NPU 최적화, S0/S1보다 정확도 ↑</td>
<td>S0보다 무거움</td>
</tr>
<tr>
<td>MobileCLIP-S0</td>
<td>~3M</td>
<td>256×256</td>
<td>512</td>
<td>가장 빠름, ml-mobileclip 체크포인트 이미 다운됨</td>
<td>기획안의 S2와 다름, 정확도 ↓</td>
</tr>
<tr>
<td>open_clip ViT-B-32</td>
<td>~87M</td>
<td>224×224</td>
<td>512</td>
<td>맥북 PoC가 현재 사용 중, 임베딩 DB 100% 호환</td>
<td>NPU에서 너무 무거워 200ms 목표 위협</td>
</tr>
</table>
### 선정 근거
- 기획안 v1.2 정합성 (★★★★★)
- 안경 NPU 적합성 (★★★★★)
- 트레이드오프: 맥북 PoC는 ViT-B-32 사용 중이므로 S2 채택 시 임베딩 DB를 새로 빌드해야 함
### 향후 성능 개선 후보 (베이스라인 성공 후 시도할 만한 옵션)
**아래는 v1 PoC가 안경에서 동작 검증된 이후, 레이턴시·정확도 최적화 라운드에서 다시 검토할 모델들이다.**
#### 1. MobileCLIP-S0 (레이턴시 최적화 방향)
- S2(35.7M) 대비 약 1/3 크기(11.4M) → NPU에서 약 3배 빠를 가능성
- 시도 조건: S2가 200ms 목표를 못 맞추거나, 배터리 소모가 크면
- 검증 방법: S0로 동일 파이프라인 빌드 후 Top-1/Top-5 정확도 측정. 떨어지는 폭이 1~2% 이내면 채택
#### 2. MobileCLIP-S1 (중간 옵션)
- S0와 S2 사이의 균형점
- S0이 레이턴시는 좋은데 정확도가 너무 떨어질 때 대안
#### 3. MobileCLIP-B / B-LT (정확도 최대화 방향)
- S2보다 정확도 ↑ 하지만 파라미터/레이턴시 ↑
- 시도 조건: 정확도가 비즈니스 KPI에 못 미치는데 레이턴시는 여유 있을 때
- 글라스 차세대 칩(Snapdragon AR2 Gen 1 등) 도입 시점에 재평가
#### 4. MobileCLIP2 시리즈 (S3/S4) — 2025년 8월 출시
- Apple ml-mobileclip 레포의 mobileclip2 디렉토리에 V2 코드 존재
- MobileCLIP2-S4는 SigLIP-SO400M/14 수준 정확도를 2배 적은 파라미터로 달성
- DFN ViT-L/14 대비 2.5배 빠른 레이턴시 (iPhone12 Pro Max 측정 기준)
- 시도 조건: V1 시리즈로 베이스라인 확보 후 V2로 업그레이드 검토
#### 5. open_clip ViT-B-32 (정확도 천장 레퍼런스)
- NPU 배포용은 아님 — 클라우드 fallback 경로 또는 정확도 비교 기준선 용도
- 사용 시점: 핵심 SKU에 대해 클라우드에서 정밀 식별이 필요한 경우 하이브리드 추론
### 베이스라인 성공 후 측정해야 할 지표
- end-to-end latency (Step 3 CLIP 단계 부분)
- Top-1 / Top-5 accuracy (마트 상품 데이터셋 기준)
- INT8 양자화 손실 (FP32 대비 정확도 drop)
- 배터리 소모 (트리거당 mAh)
- 메모리 풋프린트 (앱 번들 크기, 런타임 RSS)
이 지표가 나오면 위 후보들 중 한두 개를 재컴파일·재측정해서 A/B 비교. 베이스라인보다 의미 있게 좋아지면 교체.
### 관련 변경 사항
- 임베딩 DB는 ViT-B-32 기준으로 만들어져 있음 → MobileCLIP-S2로 전환 시 `db/embeddings/` 전체 재생성 필요 (`create_embeddings.py` 수정 필수)
- 코사인 유사도 임계값 `CLIP_SIM_THRESH = 0.15` 는 ViT-B-32 기준치이므로 S2에서 재튜닝 필요
---
## 기술 결정 로그 — 광고 표시 방식: 2D HUD (PoC 임시안) (2026-06-06)
### 결정
**v1 PoC는 2D HUD(Head-Up Display) 방식으로 광고 표시**. 3D 공간 고정 Spatial Anchor 방식은 **미구현 항목으로 명시 보류**.
### 결정 배경
- 기획안 v1.2의 Step 4 "Spatial Anchor 기반 시공간 정합"은 RayNeo SDK의 Spatial Anchor API 공식 지원 여부에 전적으로 의존
- 2026-06-06 기준 RayNeo Spatial Anchor API 지원 여부 ❓ **여전히 미검증**
- RayNeo SDK 다운로드/사용권 확보 자체에도 불확실성 (가입 승인, 한국 접근성 등)
- Step B (QNN 컴파일) 완료로 AI 추론 파이프라인은 충분히 빠름 (18ms 실측 / AR1 추정 ~50ms, 목표 200ms 대비 여유 큼) → **추론이 아니라 표시 방식이 PoC의 새 병목**
- 빠르게 안경에서 동작하는 베이스라인을 만들고 거기서 사용성 검증하는 게 우선순위
### v1 PoC 방식 (2D HUD)
- 광고를 안경 디스플레이의 **고정 위치(head-locked)** 에 2D 오버레이로 표시
- 사용자가 고개를 돌리면 광고도 같이 움직임 (시야의 일정 영역에 항상 떠있음)
- 트리거 → YOLO → CLIP → 매칭 광고 표시까지의 전체 파이프라인은 동일
- 차이점: 광고가 객체 옆에 공간적으로 고정되지 않고, **시야 내 정해진 슬롯에 등장**
### 이 방식이 매핑되는 Ad Slot Ladder (이전 결정 참고)
- ✅ L0 (Silent Log) — AIP 누적
- ✅ L1 (Peripheral Pulse) — 시야 가장자리 미세한 신호 — **2D HUD로 완전 구현 가능**
- ✅ L2 (Object Whisper) — 객체 옆 살짝 hint — **2D HUD로 부분 구현 가능** (객체 옆은 못 가지만 화면 중앙/하단으로 대체)
- ❌ L3 (Full Anchor) — 객체 옆 풀 anchor — **2D HUD로 불가, Spatial Anchor 필요**
### 🚧 미구현 항목 (향후 작업 백로그)
**1. 3D Spatial Anchor 기반 광고 표시 (Tier 3 / L3)**
- 사용자가 보고 있는 상품 옆 정확한 위치에 광고를 3D로 고정
- 사용자가 고개를 돌리거나 이동해도 광고가 현실 공간의 그 자리에 머무름
- 진짜 "AR다운" 경험
- 필요 조건:
	- RayNeo SDK의 Spatial Anchor API 공식 지원 확인
	- 또는 raw SLAM pose 받아서 자체 anchor 시뮬레이션 구현
	- 또는 XREAL Air 2 Ultra로 하드웨어 전환
- 시도 시점: v1 PoC가 2D HUD로 동작 검증된 후, RayNeo SDK 입수 + Spatial Anchor 지원 확인 시
**2. Tier 2 Object Whisper의 정확한 객체 위치 추적**
- 손이 향하는 객체에 정확히 hint 부착 (Hand tracking + 객체 위치 매칭)
- 2D HUD에선 화면 영역 단위로만 가능, 개별 객체에 부착은 어려움
- 시도 시점: Hand tracking SDK 통합 시 (RayNeo gesture SDK 또는 MediaPipe Hands 통합)
**3. 사용자 시야 좌표계 정합**
- 2D HUD라도 디스플레이 좌표가 안경 폼팩터에 따라 다름 (눈동자 위치, FOV)
- 캘리브레이션 절차 필요 (사용자별 조정)
- 시도 시점: v1 PoC 안정화 후 사용성 테스트 단계
### v1 PoC 성공 기준 (2D HUD 베이스라인)
- [ ] 안경 카메라에서 프레임 캡처 → NPU 추론 → 디스플레이 출력 end-to-end 동작
- [ ] 마트 진열대 영상으로 시범 테스트: 객체 인식되면 화면에 광고 카드 표시
- [ ] 트리거 → 표시까지 200ms 이내
- [ ] 30초 연속 사용 시 thermal throttling으로 인한 성능 저하 측정
- [ ] Anchor lifecycle (fade in / active / fade out) 동작 확인
### 비즈니스/비전 관점 영향
- 사용자 원래 비전 ("광고를 내 전체 시야 어디든 자유롭게 띄울 수 있다") 중:
	- **"전체 시야"** = 2D HUD로 부분 만족 (head-locked로 시야 어디든 가능)
	- **"자유롭게 띄움"** = 위치 자유도는 만족, 단 **세상 공간이 아니라 사용자 시야 공간 기준**
- 진짜 풀 비전 = head-locked HUD + world-locked Anchor 둘 다 + 동적 전환. 이는 v2+ 목표.
- 비즈니스 모델 측면: Conquest 광고는 2D HUD에서도 충분히 동작 (객체 식별 → 비교 정보 표시). 다만 "상품 옆"이 아니라 "화면 슬롯"에 표시되는 차이.
---
## 기술 결정 로그 — YOLO 변형 m → l 전환 (2026-06-06)
### 결정
**기본 YOLO 모델을 yolo11m → yolo11l 로 변경.**
### 배경
초기 추천(이전 결정 로그)에서 yolo11m을 선택했음. 이후 yolo11l/x도 Qualcomm AI Hub (XR2 Gen 2 Proxy)로 실측해본 결과, l이 m 대비 latency 페널티는 매우 작은데 정확도 이득은 의미 있음.
### XR2 Gen 2 (Proxy) 실측 결과 (320×320, FP16, NPU)
<table header-row="true">
<tr>
<td>변형</td>
<td>파라미터</td>
<td>XR2 latency</td>
<td>AR1 추정</td>
<td>  • CLIP 16ms → 합계 (AR1)</td>
<td>mAP50-95</td>
<td>200ms 목표 대비</td>
</tr>
<tr>
<td>n</td>
<td>2.6M</td>
<td>2.4 ms</td>
<td>4~6 ms</td>
<td>34~36 ms</td>
<td>39.5</td>
<td>18%</td>
</tr>
<tr>
<td>s</td>
<td>9.4M</td>
<td>3.5 ms</td>
<td>5~9 ms</td>
<td>35~39 ms</td>
<td>47.0</td>
<td>20%</td>
</tr>
<tr>
<td>m</td>
<td>20.1M</td>
<td>9.4 ms</td>
<td>14~23 ms</td>
<td>44~53 ms</td>
<td>51.5</td>
<td>27%</td>
</tr>
<tr>
<td>**l** ✅</td>
<td>25.3M</td>
<td>**11.8 ms**</td>
<td>18~30 ms</td>
<td>48~60 ms</td>
<td>53.4</td>
<td>30%</td>
</tr>
<tr>
<td>x</td>
<td>56.9M</td>
<td>19.5 ms</td>
<td>29~49 ms</td>
<td>59~79 ms</td>
<td>54.7</td>
<td>40%</td>
</tr>
</table>
### 의사결정 근거
1. **m → l: latency +2.4ms, mAP +1.9.** 가성비 좋음. NPU가 medium-large 모델에 더 효율적이라 GFLOPs 비례보다 빠르게 실행.
2. **l → x: latency +7.7ms, mAP +1.3.** 점프 둔화. 비효율 영역 시작.
3. **모든 변형이 200ms 목표 안에 안전.** x조차 40% 사용. 어차피 latency가 병목이 아님.
4. **mAP 이득이 의미 있는 마지막 점** — 마트 진열대 환경에선 작은 객체, 가려진 객체가 많아서 mAP가 클수록 검출률 ↑.
### 향후 변경 가능성
- **fine-tuning 들어가면 다시 변형 재검토.** 마트 상품 도메인 fine-tuning 후엔 더 작은 변형(n/s)으로도 충분할 수 있음 (모델 크기보다 데이터셋이 정확도 결정 요인이 됨).
- **x로 올라갈 일은 거의 없음.** mAP +1.3 추가 이득이 latency +7.7ms 비용보다 작음. 데모 max-accuracy 시나리오 외엔 비추.
### 코드 / 자산 변경 내역
- `yolo.py`: `MODEL_NAME = "yolo11l.pt"` (기본값 변경)
- `build_hello_ar.sh`: 안경 빌드 시 `yolo11l.qnn_context.bin` + `yolo11l.onnx` 복사
- `unity_assets_prep/Scripts/YoloDetector.cs`: `modelResourceName = "yolo11l"`
- `unity_assets_prep/Scripts/QnnYoloDetector.cs`: `contextBinFilename = "yolo11l.qnn_context.bin"`
- `MODELS.md`: "현재 기본" 표시 m → l 이동
### 갈아끼우기 (다른 변형으로 임시 사용)
CLI 인자 한 줄로 가능:
```javascript
python yolo.py input.mp4 output.mp4 --model yolo11n.pt   # 또는 s/m/x
```
---
<!-- [CLEANUP TODO] yolo11n.onnx 잔재 항목 — 2026-06-08 정리 완료 확인 (Resources/ 에 yolo11l.onnx 만 존재), 항목 자체 지침("완료 시 이 항목 삭제")에 따라 삭제 -->
---
# 회의록 — Adversarial Ad 추천 시스템 (2026-06-06 팀 회의)
## 1차 데모 목표
**"코카콜라를 들었을 때 펩시 광고가 뜬다."** 이게 되면 1차 완성.
## 일정
- **화요일**: 제출
- **일요일 낮**: 핵심 5개 feature 완성 (중간 마감)
- **오늘 밤 ~12시**: 환경 세팅 완료
- **원칙**: 1인 1일 1 feature. 핵심 5개 중 하나라도 빠지면 제출 불가.
## 시스템 파이프라인 (6단계, 회의 합의안)
```javascript
[Step 1] Interest Capturing
    ① Trigger (IMU) → ② YOLO → ③ mobile CLIP

[Step 2]
    ④ Storing (→ DB_AIP) ← Selection no
    ⑤ Adversarial Ad Rendering ← Selection yes
    ⑥ Selection (DB_AIP 참조)
```
<table header-row="true">
<tr>
<td>#</td>
<td>단계</td>
<td>사용 DB</td>
</tr>
<tr>
<td>①</td>
<td>Trigger</td>
<td>-</td>
</tr>
<tr>
<td>②</td>
<td>YOLO</td>
<td>DB_obs (광고 가치 객체 whitelist)</td>
</tr>
<tr>
<td>③</td>
<td>mobile CLIP</td>
<td>DB_prod (제품/브랜드 임베딩)</td>
</tr>
<tr>
<td>④</td>
<td>Storing</td>
<td>DB_AIP (관심 누적)</td>
</tr>
<tr>
<td>⑤</td>
<td>Adversarial Ad</td>
<td>DB_Ad (제품 → 경쟁사 광고 매핑)</td>
</tr>
<tr>
<td>⑥</td>
<td>Selection</td>
<td>DB_AIP 참조</td>
</tr>
</table>
## 7 Features
### 🔴 핵심 5 (가라 불가)
- **F1. Triggering by IMU** — RayNeo IMU API로 각속도 기반 시선 고정 감지. 맥북의 픽셀 모션 threshold를 IMU 각속도 threshold로 치환. (담당: B)
- **F2. 4 DB Construction** — DB_obs / DB_prod / DB_AIP / DB_Ad 4개 구축. 1차 데모 범위는 코카콜라/펩시 + 노트북 시드 데이터.
- **F3. YOLO/CLIP 온디바이스 컴파일** — 컴파일만 뚫으면 맥북 검증 하이퍼파라미터 그대로 이식.
- **F4. DB CRUD** — AIP read-modify-write가 핵심.
- **F5. Spatial Anchored Rendering** — 광고가 공간에 고정. ARDK의 spatial anchor 활용 시도.
### 🟡 가라 가능 2
- **F6. Background Daemon** — 앱 안 켜둬도 상시 구동. Android Foreground Service로 시도. 안 되면 프로토타입 끝. **B가 가장 먼저 확인.**
- **F7. Selection Logic** — when/where/what 분기. WiFi(매장 감지) + AIP 카운트 기반. 마케팅 이론 추가 학습 필요. **퀄 무한히 들어가는 영역, 가라 가능.**
## 운영 규칙
- **디바이스**: RayNeo X3 Pro 1대 → 시간 분할
- **폰 페어링**: B 폰만 연결 (A 폰은 시도 안 함)
- **WiFi**: 테스트용 고정 ("호흡")
- **ADB 시리얼**: `A06B4A95B784973`
## 역할 분담
<table header-row="true">
<tr>
<td>담당</td>
<td>작업</td>
</tr>
<tr>
<td>**A (리더)**</td>
<td>YOLO/CLIP + DB + Selection/Marketing/BM (일요일 낮까지)</td>
</tr>
<tr>
<td>**B**</td>
<td>Background Daemon 확인 → IMU 트리거</td>
</tr>
<tr>
<td>C</td>
<td>합주 2건, 여력 적음</td>
</tr>
</table>
---
## 회의록 vs 기존 결정 — ⚠️ 충돌 발견
### 충돌 1: Spatial Anchor vs 2D HUD
- **이전 결정** (2026-06-06 초): "v1 PoC는 2D HUD로 진행, 3D Spatial Anchor는 v2+ 백로그" (RayNeo SDK Spatial Anchor 미검증 + RayNeo SDK 입수 불확실성 때문)
- **회의록 (오늘)**: F5 Spatial Anchored Rendering이 **핵심 5에 포함**. "ARDK spatial anchor 쓰면 수월할 수 있다"고 가능성 열어둠
**해석**: 회의에서 ARDK가 spatial anchor를 지원할 가능성을 다시 평가하기로 결정한 것으로 보임. **v1 PoC 방식 결정을 갱신할지, 또는 spatial anchor 시도 → 안 되면 2D HUD fallback할지 명확화 필요.**
### 충돌 2: 시연 시나리오 — 마트 진열대 → 콜라/펩시
- **이전 결정**: "1차 PoC 시나리오는 마트 진열대 + Conquest 비교정보"
- **회의록**: "코카콜라 들었을 때 펩시 광고" — 같은 conquest 비즈니스지만 구체 시나리오는 마트 vs 손에 들고 있는 상황
**해석**: 마트 진열대 시나리오의 부분집합. 손에 든 상태(3-Tier Model의 Tier 3 Inspecting)로 시작. 큰 충돌은 아님.
---
## 회의록 vs 이미 진행된 작업 — 진행도 sync 갭
회의록 F3/F4 등이 "확인 필요"로 적혀있지만 **이미 상당 부분 완료**:
<table header-row="true">
<tr>
<td>회의록 Feature</td>
<td>실제 진행 상태</td>
</tr>
<tr>
<td>F3 YOLO/CLIP 온디바이스 컴파일</td>
<td>✅ **Step B 완료**: yolo11l + MobileCLIP-S2 둘 다 QNN 컴파일 + AI Hub profile 완료. .bin 파일 준비됨</td>
</tr>
<tr>
<td>F1 IMU 트리거</td>
<td>🔄 v0.2 `GyroTrigger.cs` 구현 중 (task #13 진행 중)</td>
</tr>
<tr>
<td>F4 DB CRUD</td>
<td>⚠️ 기존 단일 `db/metadata.json` 만 있음. **4개 DB 분리는 미구현**</td>
</tr>
<tr>
<td>F2 4 DB Construction</td>
<td>⚠️ DB_obs / DB_AIP / DB_Ad 스키마 미정</td>
</tr>
<tr>
<td>F5 Spatial Anchored Rendering</td>
<td>⏸️ 이전 결정으로 2D HUD로 선회했음. 재논의 필요</td>
</tr>
<tr>
<td>F6 Background Daemon</td>
<td>⚠️ 미시작. 최우선 hard gate</td>
</tr>
<tr>
<td>F7 Selection Logic</td>
<td>⚠️ 미시작</td>
</tr>
</table>
**팀 다른 분들이 이 진행 상황 알고 있는지 확인 필요.** 회의록이 진행 중인 사항을 반영 안 한 채 적힌 것 같음.
---
## 🚧 불충분/미해결 항목 (회의록 + 기존 컨텍스트 종합)
### 기술적 미해결
1. **모델이 코카콜라/펩시를 실제로 식별 가능한지 미검증**
	- YOLO COCO 80 클래스에 "콜라"는 없음. "bottle" 일반 클래스로 감지 → CLIP이 콜라/펩시 구분
	- CLIP zero-shot의 정확도 미검증 (브랜드 단위 식별)
	- 실험 필요: 콜라/펩시 이미지로 CLIP 임베딩 유사도 측정
2. **4 DB 스키마 미정**
	- `DB_obs`: YOLO class ID list? 또는 class name list? format?
	- `DB_prod`: 현재 `db/embeddings/*.npy` (1×512). 확장 시 더 많은 카테고리 필요
	- `DB_AIP`: 누적 단위(SKU? 카테고리?), 보존 기간, 시간 weight decay
	- `DB_Ad`: "제품 → 경쟁사" 매핑 그래프 구조
3. **Selection Logic 임계값**
	- AIP 카운트 몇 회부터 광고 띄움? (3회? 5회?)
	- WiFi 매장 감지 → 매장 WiFi list 어떻게 얻나?
	- "처음 본 것"과 "자주 본 것" 어떻게 구분?
4. **Spatial Anchor (F5) 가능 여부 실증 안 됨**
	- RayNeo SDK / Unity ARDK의 Spatial Anchor API 실제 동작 미확인
	- 안 되면 2D HUD fallback 결정 미리 정해두기
5. **Background Daemon 배터리/제약 미확인**
	- Foreground Service 24시간 구동 가능?
	- IMU 폴링 100Hz 상시 시 배터리 영향?
	- Android 12의 BG restrictions 안경에 적용되는지
6. **MobileCLIP-S2 임베딩 호환성**
	- 현재 `db/embeddings/*.npy` 는 ViT-B-32 기준 (맥북 PoC)
	- 안경 배포 시 MobileCLIP-S2로 다시 임베딩해야 함
	- 콜라/펩시 이미지 수집 → 재임베딩 작업 필요
### 비즈니스/UX 미해결
1. **광고 표시 지속 시간 / 위치**
	- 현재 코드: 4초 active + 0.5초 fade. 안경에서 적절한 시간?
	- 위치: head-locked 어디? (시야 중앙? 가장자리?)
2. **프라이버시 / 동의 절차**
	- "사용자가 본 모든 것 기록" — 첫 실행 시 동의 화면 필요한지
	- 데모 시연 시점에선 생략 가능하지만 정식 출시는 법적 이슈
3. **Purchase CTA 처리**
	- 회의에서 "보류"로 결정. 데모에 포함할지 명확화
### 운영 미해결
1. **"제출"의 정의 미확인** (회의록 Open Question #1)
	- 화요일에 무엇을 어떤 형식으로 내야 하는지
2. **C의 합류 시점/범위**
	- 합주 2건, 가능 시 합류. 어느 feature를?
3. **B 폰 페어링 / "호흡" WiFi 운영 절차**
	- 실제 폰 모델, 페어링 방법, WiFi 비번 등 절차 문서화 필요
---
## 기술 결정 로그 — 광고 표시 방식 재확정: 2D HUD (2026-06-06, 회의 후)
### 결정
**v1 PoC는 2D HUD로 진행. Spatial Anchor는 v2+ 백로그.**
### 배경
오늘 팀 회의 회의록에서 F5 Spatial Anchored Rendering이 핵심 5에 포함되어 회의록 vs 기존 결정 사이 충돌이 발생했음. 둘의 차이를 정리한 결과:
- **2D HUD**: 시야(머리)에 고정. 구현 ~100줄, 안경에서 이미 동작 검증됨 (v0.1 HelloAR로 확인).
- **Spatial Anchor**: 현실 공간에 고정. 6DoF SLAM + RayNeo ARDK Spatial Anchor API 의존. **API 입수 + 동작 검증 미완.**
### 판단
- 화요일 데드라인을 안전하게 지키려면 검증된 경로(2D HUD)로 시작이 합리적.
- Spatial Anchor의 시각적 임팩트는 크지만, "한 번도 동작 확인 안 된 의존성"을 핵심 경로에 두는 건 리스크.
- 회의록 F5는 "도전적 목표"로 해석하되, **v1 PoC 완성 후** 시도하는 것이 안전.
### v1 PoC에서 2D HUD가 구현하는 것
- 검출된 객체에 매칭된 광고를 **화면 좌표 기준 고정 위치**에 표시
- 사용자가 고개 돌리면 광고도 같이 움직임 (시야 어디 봐도 항상 표시)
- 광고 표시 위치는 화면 상단 / 하단 / 측면 중 결정 필요 (별도 디자인 결정)
- Anchor lifecycle (fade in/active/fade out) 은 그대로 적용
### 회의록 F5 → v2+ 백로그로 강등
기존에 노션에 적어둔 "🚧 미구현 항목: 3D Spatial Anchor 기반 광고 표시 (Tier 3 / L3)" 항목과 일치. **회의에서 새로 제안된 것이 아니라 이미 v2 백로그에 있던 항목이 회의에서 핵심 5로 올라온 것이었음.** 다시 v2+로 내림.
### 회의록에 반영할 변경
F5 Spatial Anchored Rendering 의 status:
- 🔴 핵심 5 → 🟢 **v2+ 백로그** (v1 PoC 완성 후 도전)
- 핵심 5는 4개로 축소 (F1 IMU 트리거, F2 4 DB Construction, F3 YOLO/CLIP 컴파일, F4 DB CRUD)
- **F3는 이미 완료 상태 명시** (yolo11l + MobileCLIP-S2 .bin 생성 완료)
- F7 Selection Logic의 가라 허용 기조 유지
### 추후 Spatial Anchor 시도 시점
v1 PoC가 2D HUD로 동작 검증된 후 (즉 화요일 제출 후 ~ 그 다음 라운드):
1. RayNeo ARDK SDK 입수 (Spatial Anchor API 공식 지원 확인)
2. 빈 공간에 큐브 박기 PoC
3. 검출된 객체 위치에 광고 anchor 시험
4. 광고 표시 모듈을 인터페이스로 추상화 (HUD ↔ Anchor swap 가능)
---
## 내일(2026-06-07) 작업 계획
### 나의 작업 (우선순위 순)
#### 1. 콜라/펩시 식별 가능성 실증 (CLIP)
- MobileCLIP-S2 (또는 현재 사용 중인 ViT-B-32) 임베딩으로 콜라 캔과 펩시 캔 식별 가능한지 확인
- 두 브랜드 이미지 각각 임베딩 → 코사인 유사도 측정 → 분리 가능 여부 판정
- **이게 안 되면 1차 데모 시나리오 자체가 무너짐.** 30분~1시간 작업이지만 가장 먼저 해야 함.
#### 2. CLIP 관련 후속 업무
- 콜라/펩시 식별 OK 면: 임베딩 DB를 MobileCLIP-S2 기반으로 재생성 (`create_embeddings.py` 수정 + 실행)
- 임베딩 DB에 콜라/펩시 + 기존 노트북/폰 항목 통합
#### 3. DB 세팅 (4 DB Construction, F2)
- DB_obs 스키마 정의 + 시드 데이터 (YOLO 클래스 화이트리스트)
- DB_prod 스키마 + 콜라/펩시 + 기존 항목
- DB_AIP 스키마 (시간 weight decay, 누적 카운트)
- DB_Ad 스키마 (제품 → 경쟁사 광고 매핑)
- 4개 DB CRUD 모듈 (F4 일부)
### 팀 작업 — 유리 (B)
#### Background Daemon Hard Gate 검증 (F6)
- Android Foreground Service로 안경에서 24/7 상시 구동 가능한지 검증
- **Hard gate**: 이게 안 되면 비즈니스 모델 전체가 무너짐 → 다른 사람들이 만든 거 의미 없어짐
- 검증 항목:
	- Android 12 (RayNeo AIOS)의 Foreground Service 정책
	- 배터리 최적화 예외 등록 가능 여부
	- 알림 상주 요구사항
	- 안경 슬립 모드에서도 IMU 폴링 유지되는지
- 검증 결과에 따라 다음 단계 전체 분기 결정됨
### 회의 운영 사항 메모
- RayNeo X3 Pro 1대만 보유 → 시간 분할 사용
- 폰 페어링: B(유리) 폰만 연결
- WiFi: "호흡" 네트워크 고정
- ADB 시리얼: `A06B4A95B784973`
---
## 📋 다음 세션 시작 시 체크리스트 (2026-06-06 마무리)
### TL;DR — 어디서 멈췄나
v0.2.4 NPU 통합 95% 완성. 마지막 단계 (Java native lib 로드)에서 silent fail. 안경에는 mock 모드 APK 설치돼있음.
### 🎯 이번 세션 결정적 발견
- **AR1 Gen 1 안경의 실제 NPU = Hexagon v73** (SoC `SSG2125P`)
- 처음 v68/v69 가정 모두 틀렸음. logcat 에러 `libQnnHtpV73Stub.so not found` 로 정확히 짚어냄
- 모든 .bin 파일 v73 타겟으로 재컴파일 필요 (이미 yolo11l_v73.qnn_context.bin 50MB 다운 완료)
### 내일 작업 순서 (critical path)
#### 1️⃣ 가장 먼저 (30분) — Java static block silent fail 디버깅
**증상**: logcat에 `QnnYoloEngine` 태그 메시지 0개. Native lib 로드 시도 자체가 안 실행됨
**가설들** (한 줄씩 시도):
- (A) `android.util.Log.i()` 가 안 출력되는 것일 수도 → `System.out.println` 으로 교체해서 stdout 확인
- (B) Java 클래스 로딩이 lazy → 첫 호출 전엔 static block 실행 안 됨. 강제로 `Class.forName("com.eagleeye.qnn.QnnYoloEngine")` 호출
- (C) APK 안의 .class 파일 확인 — 우리 Java 코드가 진짜 컴파일됐는지 (`unzip -l ... | grep QnnYoloEngine`)
- (D) AndroidJavaObject 생성 자체가 실패한 것일 수도 → C# 측 try/catch 더 verbose 로깅
```bash
# 시작 명령
cd ~/Desktop/AR_project
git checkout v0.2.4-npu-qnn
adb devices  # 안경 연결 확인
```
#### 2️⃣ Initialize 동작 확인 (1~2시간)
Static block + nativeInitialize 까지 통과해야 함. 통과 시 logcat 예상:
```javascript
[QnnYoloEngine] loaded libcdsprpc.so
[QnnYoloEngine] loaded libadsprpc.so
[QnnYoloEngine] loaded libQnnSystem.so
[QnnYoloEngine] loaded libQnnHtp.so
[QnnYoloEngine] loaded libQnnHtpV73Stub.so
[QnnYoloEngine] loaded libqnn_yolo_engine.so
[QnnYoloEngine] context bin loaded (53043200 bytes)
[QnnYoloEngine] binary info ok
[QnnYoloEngine] graph name: yolo11l_v73   (또는 비슷)
[QnnYoloEngine] graph retrieved
[QnnYoloDetector] ✅ QNN HTP runtime ready
```
여기까지 통과하면 NPU 기반 50% 완성.
#### 3️⃣ nativeExecute 구현 (4~8시간) — 진짜 추론
현재 `unity_assets_prep/Plugins/Android/jni/qnn_yolo_engine.cpp` 의 nativeExecute는 stub. 빈 출력 반환 중.
필요한 작업:
- Graph 의 input/output tensor descriptor 추출 (`graphInfo.input_tensors`, `graphInfo.output_tensors`)
- Qnn_Tensor_t 구조 셋업 (id, name, type, shape, dataType, memType, clientBuf)
- 입력 float[] → clientBuf 복사
- `graphExecute(graph, inputs, numInputs, outputs, numOutputs)`
- output clientBuf → float[] 변환해서 Java 측에 반환
참고: QAIRT SDK의 `examples/QNN/SampleApp/SampleApp/src/Utils/IOTensor.cpp` 가 정석. 약 200~300줄 추가 예상.
#### 4️⃣ 실측 + 검증 (1시간)
NPU latency 측정. 기대치:
- Step B 측정 (XR2 v69): yolo11n 2.4ms
- 우리 v73 yolo11l 추정: **10~30ms** (v0.2.3 GPU 200ms 대비 7~20배 가속)
200ms 목표 대비 압도적 여유. 검증되면 마트 시나리오 본격 가능.
### 🔧 안경 + 환경 상태
- 안경: v0.2.4 mock 모드 APK 설치돼있음 (mock laptop 박스만 뜸)
- ~/Desktop/AR_project/ 에 모든 파일 보존됨
- QAIRT SDK: ~/Downloads/qairt/2.47.0.260601/ 에 있음
- v73 .bin: ~/Desktop/AR_project/yolo11l_v73.qnn_context.bin (50MB)
- Plugins/Android/libs/arm64-v8a/ 에 .so 7개 (qnn_yolo_engine + 6 QAIRT)
- git: v0.2.4-npu-qnn 브랜치, 마지막 커밋 "v0.2.4 NPU 큰 진전"
### 🚧 미완 사항 명시 (백로그)
1. **Java native lib silent fail** (가장 시급) — static block 진단
2. **nativeExecute 텐서 setup** — 본 추론 실현
3. **Hexagon DSP firmware deployment** — `ADSP_LIBRARY_PATH` 설정해서 안경 NPU가 hexagon-v73/libQnnHtpV73Skel.so 찾을 수 있도록
4. **회전 보정** — v0.2.5의 RotateTexture shader가 v0.2.4엔 없음. 통합 시 추가
5. **HelloAR.cs 한 줄 동기화** — v0.2.5 브랜치의 trigger-only 로직과 머지
### 만약 NPU가 끝까지 안 풀리면 — Plan B
- v0.2.5 GPU (200ms, trigger 기반)로 마트 시나리오 PoC 시연. 데모엔 충분
- NPU는 production 갈 때 다시 시도
좋은 밤 되세요. 🌙
---
## 기술 결정 로그 — NPU 세대 정정: v68/v69 추정 → **v73 실측** (2026-06-06)
### 🚨 중요 정정 사항
이전에 노션 / `ONNX_ANALYSIS.md` 등 여러 문서에서 RayNeo X3 Pro의 NPU를 "**Hexagon v68 또는 v69 추정**"으로 표기했음. **둘 다 틀림.**
### 실측된 사실 (commit `1ced27f` 발견)
- **SoC: SSG2125P** (Snapdragon AR1 Gen 1의 정확한 SoC 코드)
- **NPU: Hexagon v73** — 우리가 추정했던 것보다 한~두 세대 신형
### 영향
#### 1. AR1 Gen 1 latency 추정치 — 너무 보수적이었을 가능성
<table header-row="true">
<tr>
<td>항목</td>
<td>이전 가정</td>
<td>정정</td>
</tr>
<tr>
<td>AR1 NPU 세대</td>
<td>v68 또는 v69</td>
<td>**v73**</td>
</tr>
<tr>
<td>XR2 Gen 2 Proxy NPU</td>
<td>v69</td>
<td>v69 (그대로)</td>
</tr>
<tr>
<td>두 NPU의 세대 차이</td>
<td>0~1 세대 (XR2가 비슷하거나 약간 신형)</td>
<td>**4 세대 차이 (AR1이 신형!)**</td>
</tr>
<tr>
<td>XR2 → AR1 latency 환산</td>
<td>×1.5~2.5 배 보수적</td>
<td>**그대로이거나 오히려 더 빠를 가능성**</td>
</tr>
</table>
**즉**: 이전에 "AR1에서 m은 ~50ms, l은 ~80ms 정도 걸릴 것"으로 보수적 추정했지만, **v73 신세대 NPU 덕에 실제론 더 빠를 수 있음**. 화요일 데모 latency 부담은 더 줄어들었을 가능성.
#### 2. AI Hub 컴파일 타깃 변경
<table header-row="true">
<tr>
<td>항목</td>
<td>이전</td>
<td>정정</td>
</tr>
<tr>
<td>AI Hub 사용 device</td>
<td>`XR2 Gen 2 (Proxy)` (Hexagon v69)</td>
<td>**`QCS8550 (Proxy)`**** (Hexagon v73)**</td>
</tr>
<tr>
<td>컴파일된 .bin 파일</td>
<td>`yolo11l.qnn_context.bin` (v69 타겟)</td>
<td>**`yolo11l_v73.qnn_context.bin`**** (v73 타겟)**</td>
</tr>
<tr>
<td>안경에서 사용 가능?</td>
<td>❌ SoC 불일치로 fail</td>
<td>✅ v73 타겟 매칭</td>
</tr>
</table>
이전 5개 변형 `.bin` (n/s/m/l/x, v69 타겟)은 **안경에서 직접 실행 불가**. v73 타겟으로 재컴파일 필요. **단 검증/벤치마크용으로는 여전히 의미 있음** (XR2 Gen 2가 실재 디바이스로 측정한 값).
#### 3. NPU 호환성 / 연산 지원 범위
Hexagon v73은 v69 대비 신세대라:
- **FP16 지원**: v69도 지원했음. v73도 OK
- **INT8 지원**: 더 잘됨 (HW 가속 강화)
- **새 연산자**: 일부 v68/v69에서 fallback되던 연산이 v73에선 native 지원될 가능성
- **결론**: NPU 배치율은 동일 또는 더 좋음. fallback 위험 ↓
### 노션에서 갱신해야 할 이전 entries
- [Step B 측정 결과] AI Hub 컴파일 대상 = ~~XR2 Gen 2 (v69)~~ → **참고용**으로 유지. 안경 실측치는 별도 측정 필요.
- [ONNX 그래프 분석] "Hexagon v68 또는 v69 (미공식)" 문구 → ✅ 해소: 옛 `ONNX_ANALYSIS.md` 는 [dev-guide.md](dev-guide.md) §6 으로 통합되며 v73 으로 정정됨 (2026-06-08)
- [기존 결정 로그들 중 "v68 또는 v69" 언급된 곳] 다 v73으로 정정
### 안경에서 진짜 동작 검증은 아직 미완
v73 타겟 `.bin` + JNI 코드 작성까진 됐지만:
- 안경에서 native QNN initialize 동작 **미확인** (logcat에 init 로그 0개, silent fail 의심)
- `nativeExecute` 텐서 setup 미구현 (~300줄 추가 필요)
- 실제 NPU latency 측정 미완
즉 **"v73 타겟으로 컴파일 OK"는 확정**, **"안경에서 진짜 NPU에 디스패치되어 도는지"는 다음 세션 작업**. 이게 진짜 안경 실측치를 얻는 prerequisite.
### 다음 세션 우선순위 영향
내일 작업 계획에 추가:
- **(추가)** native QNN initialize silent fail 원인 진단 (jni/Java/JNI 로그 검토)
- nativeExecute 텐서 setup 구현 (별도 task로)
- 동작 확인되면 → 안경에서 직접 yolo11l_v73 latency 실측 → AR1 추정치를 실측치로 대체
---
## 기술 결정 로그 — QNN direct → LiteRT + QNN Delegate 피벗 (v0.2.4 → v0.2.6)
### 결정
**JNI 통한 QNN direct 경로 포기. LiteRT + QNN Delegate로 피벗.**
### 결정의 근거 — silent fail 원인 가설 진단 결과
v0.2.4에서 시도한 QNN direct (JNI C++ → libQnnHtp.so) 가 안경에서 silent fail. 가설 4개 검증:
<table header-row="true">
<tr>
<td>가설</td>
<td>발생 여부</td>
<td>결정적인가?</td>
</tr>
<tr>
<td>1. .so 의존성 깨짐</td>
<td>✅ 직접 관찰</td>
<td>부분적 막힘, 우회 가능</td>
</tr>
<tr>
<td>2. **DSP firmware path**</td>
<td>✅ **직접 관찰**</td>
<td>**결정적 막힘 — root 없이 우회 불가**</td>
</tr>
<tr>
<td>3. RayNeo vendor 차이</td>
<td>간접적 (가설 1의 원인)</td>
<td>가설 1을 악화</td>
</tr>
<tr>
<td>4. SELinux 정책</td>
<td>✅ 일부 관찰</td>
<td>가설 2와 동일 정책의 OS 레벨 표현</td>
</tr>
</table>
### 가설 2가 결정적인 이유 — Hexagon DSP 보안 구조
Hexagon DSP는 ARM CPU와 별도 보안 도메인. firmware 로딩은 fastrpc 커널 드라이버를 통하며, 다음 3가지를 강제:
1. **경로**: `/system` 또는 `/vendor` 만 신뢰. 앱 private 경로 (`/data/data/...`) reject.
2. **서명**: Qualcomm/OEM 서명된 firmware 만 통과. SDK에서 추출한 unsigned firmware reject.
3. **SELinux 컨텍스트**: `system_app`, `vendor_app` 만 허용. `untrusted_app` reject.
우리 v0.2.4 시도는 3가지 모두 위반:
- firmware를 `Application.persistentDataPath/hexagon-v73/` (untrusted_app private 경로) 에 복사
- QAIRT SDK에서 추출한 firmware는 OEM 서명 없음
- 우리 앱은 untrusted_app 컨텍스트
→ fastrpc 드라이버가 silent reject. 우리 코드에 에러도 안 옴.
### 결론: 3rd-party 앱은 root 없이 QNN direct 불가능
retail 안경 + 일반 APK 조합으로는 Hexagon NPU 직접 접근 불가. Snapdragon AI Hub sample app 같은 건 OEM 시스템 앱 권한 또는 rooted 디바이스 기준으로 동작.
### LiteRT + QNN Delegate가 이를 우회하는 원리
**Qualcomm 공식 QNN Delegate 라이브러리는 위 3가지를 모두 우회하는 path 내장:**
- 앱이 firmware를 들고 다닐 필요 X — delegate가 시스템 `/vendor/lib64/libQnnHtp.so` (이미 서명된 vendor lib) 호출
- 시스템 vendor lib가 fastrpc로 firmware 로드 시 `/vendor/dsp/cdsp/` 의 OEM 서명 firmware 사용
- untrusted_app 도 vendor lib 경유 호출 허용 (vendor 가 public ABI로 노출)
즉 **QNN Delegate = Qualcomm이 만든 trusted bridge**. 보안 통과는 delegate 라이브러리가 알아서.
### v0.2.6 (LiteRT + QNN Delegate) 핵심 변경
<table header-row="true">
<tr>
<td>항목</td>
<td>v0.2.4 (포기)</td>
<td>v0.2.6 (신규)</td>
</tr>
<tr>
<td>모델 형식</td>
<td>yolo11l_v73.qnn_context.bin (QNN context binary)</td>
<td>yolo11l.tflite (TFLite FlatBuffers)</td>
</tr>
<tr>
<td>변환 도구</td>
<td>Qualcomm AI Hub QNN 컴파일러</td>
<td>TFLite Converter / ai-edge-torch</td>
</tr>
<tr>
<td>런타임</td>
<td>JNI → libQnnHtp.so (직접)</td>
<td>TFLite Interpreter + QNN Delegate (.aar)</td>
</tr>
<tr>
<td>firmware 번들 필요?</td>
<td>✅ (그래서 막힘)</td>
<td>❌ (시스템 firmware 사용)</td>
</tr>
<tr>
<td>보안 통과</td>
<td>❌ 3단 reject</td>
<td>✅ Qualcomm 서명 경로</td>
</tr>
<tr>
<td>코드 양</td>
<td>~1000줄 (JNI C++/Java)</td>
<td>~30-50줄 (TFLite Java/Kotlin)</td>
</tr>
<tr>
<td>성능 손해</td>
<td>0% (이론)</td>
<td>~5~10% (delegate overhead, 실측 필요)</td>
</tr>
<tr>
<td>**실제 동작 기대**</td>
<td>❌ silent fail</td>
<td>✅ 동작 기대</td>
</tr>
</table>
### v0.2.4 자산의 미래
JNI/C++ 인프라 (`qnn_yolo_engine.cpp`, `libqnn_yolo_engine.so`, NDK r27c 빌드 시스템, v73 firmware 추출 등) 는 **사실상 사장**. 단 다음 용도로는 의미 남음:
- root된 디바이스에서 v0.2.4 경로 테스트 가능
- 향후 OEM 협업해서 시스템 앱으로 배포 시 재활용 가능
- DSP/NPU 학습 자료로 참고
### 다음 작업 (v0.2.6 시작)
1. yolo11l (PyTorch) → yolo11l.tflite 변환
	- ai-edge-torch 또는 ONNX → TF → TFLite 경로
2. Qualcomm QNN Delegate `.aar` 다운로드 (QAIRT 또는 Qualcomm developer 공식 채널)
3. Unity 프로젝트에 LiteRT (com.google.ai.edge.litert) 패키지 추가
4. TFLite Interpreter 인스턴스 생성 + QNN Delegate 등록
5. 안경 설치 후 동작 검증 + latency 실측
### 노션의 이전 NPU 추정치 처리
XR2 Gen 2 Proxy 측정값 (yolo11l 11.8ms, MobileCLIP-S2 16ms 등) 은 그대로 **참고용**으로 유지. LiteRT + QNN Delegate 가 동작 검증되면 안경 실측치로 대체.
---
## 진행 업데이트 — v0.2.6 MobileCLIP INT8 TFLite 완료 (2026-06-07)
### 한 줄 요약
**MobileCLIP-S2를 INT8 TFLite로 변환 + AI Hub로 latency 실측 + Unity 번들 완료.** 안경에서 동작 검증만 남음.
### 작업 흐름
```javascript
mobileclip_s2_image.onnx (136 MB FP32)
    ↓ AI Hub: --target_runtime tflite --quantize_full_type int8
    ↓ 타깃: QCS8550 (Proxy), Hexagon v73 (안경과 동일 NPU)
mobileclip_s2_v73_int8.tflite (36 MB)
    ↓ profile job
실측 latency: 2.12 ms
    ↓ copy
EagleEye_Unity/Assets/StreamingAssets/mobileclip_s2.tflite (36 MB)
```
### AI Hub 컴파일 시도 — 3변형 비교
<table header-row="true">
<tr>
<td>변형</td>
<td>옵션</td>
<td>결과</td>
<td>추론 시간</td>
<td>크기</td>
</tr>
<tr>
<td>fp (default)</td>
<td>`--target_runtime tflite`</td>
<td>✅ SUCCESS</td>
<td>4.9 ms</td>
<td>136 MB</td>
</tr>
<tr>
<td>**w8a16**</td>
<td>`--target_runtime tflite --quantize_full_type w8a16`</td>
<td>❌ **FAILED**</td>
<td>-</td>
<td>-</td>
</tr>
<tr>
<td>**int8** ⭐</td>
<td>`--target_runtime tflite --quantize_full_type int8`</td>
<td>✅ SUCCESS</td>
<td>**2.12 ms**</td>
<td>**36 MB**</td>
</tr>
</table>
### w8a16 실패 원인 (학습 포인트)
```javascript
Quantization to 16x8-bit not yet supported for op: 'SQRT'.
Quantization to 16x8-bit not yet supported for op: 'DIV'.
```
MobileCLIP-S2 의 **L2 정규화** (export 시 `ImageEncoderWithNorm` 으로 그래프에 포함시킨 부분) 의 `SQRT` + `DIV` 연산이 w8a16에서 미지원. **INT8은 지원되어 통과.** 결과적으로 INT8이 더 빠르기도 해서 의도하지 않게 더 좋은 결과.
### AI Hub `--quantize_full_type` 옵션 값 명세 (학습 포인트)
받는 값: `int8 / int16 / float16 / w8a16 / w4a8 / w4a16`
**`w8a8`**** 은 invalid.** "full INT8" 의도라면 `int8` 사용. 처음 시도 시 `w8a8` 으로 실패 → `int8` 로 수정.
### 이전 측정값 (Step B, v0.2.4) vs 이번 측정값 (v0.2.6) 비교
<table header-row="true">
<tr>
<td>시점</td>
<td>디바이스</td>
<td>정밀도</td>
<td>형식</td>
<td>추론 시간</td>
</tr>
<tr>
<td>Step B (이전)</td>
<td>XR2 Gen 2 Proxy (Hexagon v69)</td>
<td>FP16</td>
<td>QNN context binary</td>
<td>16.0 ms</td>
</tr>
<tr>
<td>**v0.2.6 (이번)**</td>
<td>**QCS8550 Proxy (Hexagon v73)**</td>
<td>**INT8**</td>
<td>**TFLite**</td>
<td>**2.12 ms** ⭐</td>
</tr>
</table>
**8배 개선.** 두 요인:
1. NPU 세대 v69 → v73 (안경 실제 칩 매칭)
2. FP16 → INT8 (NPU의 INT8 가속 활용)
### 전체 파이프라인 latency (v0.2.6 기준)
<table header-row="true">
<tr>
<td>단계</td>
<td>latency</td>
</tr>
<tr>
<td>YOLO11l INT8 TFLite (v73)</td>
<td>~12 ms</td>
</tr>
<tr>
<td>**MobileCLIP-S2 INT8 TFLite (v73)**</td>
<td>**2.12 ms**</td>
</tr>
<tr>
<td>**합계 (NPU 추론만)**</td>
<td>**~14 ms**</td>
</tr>
</table>
기획안 200ms 목표 대비 **7%**. 카메라/렌더링/SLAM에 나머지 186ms 다 쓸 수 있음.
### Unity 자산 상태
```javascript
EagleEye_Unity/Assets/StreamingAssets/
  yolo11l.tflite          26 MB  (INT8, 이미 안경에 설치된 APK에 포함)
  mobileclip_s2.tflite    36 MB  (INT8, 다음 빌드부터 포함)
```
### 미해결 (다음 작업)
- [ ] **C# 측 MobileCLIP 추론 코드 작성** (`ClipExtractor.cs` 등)
	- TFLite Interpreter + QNN Delegate
	- 입력 (1, 3, 256, 256) → 출력 (1, 512) 임베딩
	- L2 정규화는 이미 그래프에 포함됨 → 출력 그대로 코사인 유사도 계산 가능
- [ ] **임베딩 DB 재생성** (`create_embeddings.py` 수정)
	- 기존: ViT-B-32 (open_clip) 기반 임베딩
	- 새로: MobileCLIP-S2 INT8 TFLite 기반 임베딩 — 다른 임베딩 공간이라 호환 X
	- 콜라/펩시 + 기존 노트북/폰 새로 임베딩 필요
- [ ] **안경에서 동작 검증**
	- logcat으로 NPU 디스패치 확인
	- 실측 latency 확인 (AI Hub 측정값 2.12ms와 매치 여부)
- [ ] **w8a8 calibration 후 재시도 (선택)**
	- 사용자가 calibration 데이터 제공하면 진짜 풀 양자화 가능
	- 현재 INT8 충분히 빨라서 우선순위 낮음
### 이전 결정 로그 정정 사항
- "[Step B] XR2 Gen 2 Proxy 18.4ms 실측" — 여전히 유효하지만 **참고용**. 안경 실제는 QCS8550/v73 INT8 기준 ~14ms로 추정 (XR2 기준의 12% 수준)
- "[Hexagon v68 또는 v69 추정]" → **v73 확정**으로 모든 추정치 갱신 필요
- "[XR2 → AR1 latency 환산 ×1.5~2.5]" — 이 환산이 너무 보수적이었음. v73 INT8 → 실제론 XR2 FP16의 **1/8** 수준
---
## 🎉 마일스톤 — v0.3.0: 안경 NPU 추론 동작 검증 완료 (2026-06-07)
### 핵심 결과
**RayNeo X3 Pro 안경에서 yolo11l TFLite가 Hexagon NPU 위에서 실제로 추론 중.** logcat 실측 latency 평균 **~15ms**.
### logcat 증거
```javascript
05:51:40.646  tflite: [Qnn] QnnGraph_execute started. graph = 0x1
05:51:40.659  tflite: Graph qnn_delegate_graph_0 execution finished with result 0
05:51:40.659  tflite: QnnGraph_execute done. status 0x0
```
- ✅ `QnnDelegate` 정상 동작
- ✅ `QnnDsp` (Hexagon NPU/DSP) 사용
- ✅ `qnn_delegate_graph_0` 생성됨 — TFLite의 op들이 QNN graph로 변환되어 NPU 디스패치
- ✅ `result 0` / `status 0x0` — 매 추론 성공
### 안경 실측 latency vs 예측
<table header-row="true">
<tr>
<td>측정 출처</td>
<td>latency</td>
<td>비고</td>
</tr>
<tr>
<td>AI Hub 클라우드 (QCS8550 Proxy, Hexagon v73)</td>
<td>11.8 ms</td>
<td>사전 예측</td>
</tr>
<tr>
<td>**안경 실측 (RayNeo X3 Pro, Hexagon v73)**</td>
<td>**~15 ms**</td>
<td>logcat에서 7회 평균 (12~18ms 분포)</td>
</tr>
<tr>
<td>차이</td>
<td>+27%</td>
<td>안경의 메모리/RPC 오버헤드 + 다른 부하 영향</td>
</tr>
</table>
→ **사전 예측이 보수적이었지만 매우 근접.** Step B에서 만든 추정 framework가 유효함.
### 전체 파이프라인 latency (실측 + 추정)
<table header-row="true">
<tr>
<td>단계</td>
<td>추론 시간</td>
</tr>
<tr>
<td>YOLO11l INT8 TFLite (실측)</td>
<td>**~15 ms**</td>
</tr>
<tr>
<td>MobileCLIP-S2 INT8 TFLite (AI Hub 클라우드)</td>
<td>2.12 ms</td>
</tr>
<tr>
<td>MobileCLIP 안경 실측 (예상, +27% 환산)</td>
<td>~2.7 ms</td>
</tr>
<tr>
<td>**합계 (NPU 추론만, 안경 실측 기준)**</td>
<td>**~17~18 ms**</td>
</tr>
</table>
기획안 200ms 목표 대비 **9%**. 카메라/SLAM/렌더링/Anchor lifecycle에 나머지 ~180ms 사용 가능. **여유 매우 큼.**
### v0.3.0 적용 사항 (커밋 a8f6861, a11eb74)
- **공식 qai_hub_models 의 yolov11_det 사용**: NPU-friendly head 분리. C# 에서 NMS 후처리
- **JNI/C++ 인프라 폐기**: 약 5000줄 삭제, jni/ 디렉토리 제거. LiteRT + QnnDelegate Maven 통합으로 일원화
- **모델 형식**: 공식 w8a8 (full INT8) — qai_hub_models 가 사전 검증한 양자화
- **APK 자산 구성**:
	- `lib/arm64-v8a/`: libQnnHtp, libQnnHtpV66Skel, V68, V69, V73, V75 (다양한 Hexagon 세대 호환)
	- `assets/yolo11l.tflite`: 26 MB INT8
	- `assets/mobileclip_s2.tflite`: 36 MB INT8 (다음 빌드부터 포함)
### 미완성 항목 / 다음 작업
- [ ] **MobileCLIP 안경 실행 검증** — build_hello_ar.sh에 자동 복사 추가됨. C# 측 ClipExtractor 작성 + 검증 필요
- [ ] **임베딩 DB 재생성** — 기존 ViT-B-32 → MobileCLIP-S2 INT8 기반. 콜라/펩시 + 기존 항목
- [ ] **광고 표시 (Anchor lifecycle, 2D HUD)** — 트리거 → 검출 → CLIP → 매칭 → 화면 표시 end-to-end
- [ ] **trigger-based vs 주기적 추론 결정** — 현재 주기적 (1초마다). 기획안은 trigger 시 단발. 배터리 vs UX 트레이드오프
### 정정 — 이전 결정 로그 sync
이전 결정 로그에서 사용하던 표현 갱신:
- ~~"silent fail 의심"~~ → ✅ **v0.2.4 폐기, v0.3.0 QnnDelegate로 해결됨**
- ~~"AR1 Gen 1 NPU = v68 또는 v69 추정"~~ → ✅ **Hexagon v73 확정 (이전 정정 entry 유효)**
- ~~"3rd-party 앱은 QNN direct 못 씀"~~ → 여전히 유효한 결론. **단 QnnDelegate (Qualcomm 공식 plugin) 경유는 잘 됨**
- ~~"qai_hub_models 발견"~~ → **이미 v0.3.0에서 적용 완료**
### git 상태 정리 (2026-06-07 06:00 기준)
- `main` 브랜치: push 완료 (10fdbdc까지 원격 동기화)
- `v0.2.6-litert-delegate` 브랜치: 새로 push, upstream 설정
- 최신 커밋: `a11eb74 build: add MobileCLIP-S2 INT8 TFLite to StreamingAssets`
- 워킹 트리: 깨끗
---
## [DELETED] 데모 시연 형식: 캔 → 페트병 (2026-06-07)
*이 항목은 commit 회수되어 노션에서도 삭제됨. 결정 자체는 유효하지만 코드 commit 안 됨.*
<table header-row="true">
<tr>
<td>객체 형식</td>
<td>검출 클래스</td>
<td>confidence 분포</td>
<td>안정성</td>
</tr>
<tr>
<td>**콜라/펩시 페트병**</td>
<td>bottle</td>
<td>**0.89~0.95**</td>
<td>✅ 매우 안정</td>
</tr>
<tr>
<td>펩시 캔 (정면, 흰배경)</td>
<td>bottle / cup</td>
<td>0.46 / 0.28</td>
<td>⚠️ 불안정</td>
</tr>
<tr>
<td>펩시 캔 (옆, 얼음 위)</td>
<td>cup 만</td>
<td>**0.11**</td>
<td>❌ 거의 fail</td>
</tr>
</table>
원인:
- COCO "bottle" 학습 데이터는 PET병/와인병 위주, 캔 비중 작음
- 캔의 짧고 둥근 윗부분이 학습된 bottle 형상과 다름
- 옆에서 본 캔은 cup으로 분류됨
### 결과적 의미
<table header-row="true">
<tr>
<td>항목</td>
<td>값</td>
</tr>
<tr>
<td>시연 객체</td>
<td>코카콜라 페트병 (500mL 또는 1.5L)</td>
</tr>
<tr>
<td>매칭되어 표시될 광고</td>
<td>펩시 페트병 광고 (conquesting)</td>
</tr>
<tr>
<td>카메라 거리</td>
<td>30~50cm (bbox 면적 10~30%)</td>
</tr>
<tr>
<td>빛 조건</td>
<td>자연광 또는 형광등</td>
</tr>
<tr>
<td>confThreshold</td>
<td>**0.30** (현재 설정 유지)</td>
</tr>
</table>
### 부가 발견 — YOLO 검출 신뢰도
페트병 시연 안정성:
- 5장 페트병 이미지 모두 bottle 0.89+ 검출
- bbox 면적 12~30% — Layer 2 (1~50%) 필터 통과
- false positive 위험 매우 낮음
### v1.x 백로그로 이관 — 캔 식별
캔 식별이 필요하면 다음 중 하나:
1. **fine-tuning** (가장 정확) — 500~1000장 캔 이미지로 yolo11l 재학습
2. **Open Images V7 모델 사용** — `yolo11l-oiv7.pt` 는 600+ 클래스, "Tin can" 별도 클래스 있음. NPU 재컴파일 필요
3. **클래스별 differential threshold** — beverage 카테고리만 conf 0.20 — false positive 위험
4. **CLIP만 의존** — YOLO를 매우 관대하게 (모든 bottle/cup 통과) → CLIP이 진짜 캔인지 판정. 단 CLIP DB에 캔 임베딩 필요
### 노션 결정 로그 정리
- ✅ 데모 시나리오: **페트병** (불안정 캔 우회)
- ✅ confThreshold: **0.30**
- ✅ 다음 라운드 (v1.x): fine-tuning 또는 OIv7 모델 시도
### 관련 자산 (Mac PoC, gitignored 권장)
`~/Desktop/AR_project/test_cans/` 에 검증용 콜라/펩시 이미지 ~15장. 향후 fine-tuning 시드로 활용 가능. 단 PoC 핵심 자산 아니므로 git에는 미포함.
---
## 마일스톤 — Adversarial Ad DB 구축 완료 (v0.3.6, 2026-06-07)
### 결과
기획안 F2의 **DB_prod (제품 임베딩) + DB_Ad (광고 매핑)** 완성. 데모 시연 시나리오 "코카콜라 페트병 → 펩시 광고" / "펩시 페트병 → 코카콜라 광고" (conquesting) 즉시 가능.
### 자산
```javascript
db/
├── embeddings/
│   ├── coke_bottle.npy    MobileCLIP-S2 INT8 TFLite 임베딩 (1, 512), L2-norm
│   └── pepsi_bottle.npy   동일
├── ads/
│   ├── coke_bottle_ad.png  광고 카드 380×240 빨강배경 "COCA-COLA · Original Taste · 1+1"
│   └── pepsi_bottle_ad.png 광고 카드 380×240 파랑배경 "PEPSI · Refresh Now · 30% OFF"
└── metadata.json           conquesting 매핑 + match_strategy 필드
```
### Reference 이미지
<table header-row="true">
<tr>
<td>Product</td>
<td>이미지</td>
<td>출처</td>
</tr>
<tr>
<td>coke_bottle</td>
<td>`test_cans/jetson_coke_1.jpg`</td>
<td>PiotrG1996/JetsonNano YOLOv5 GitHub repo</td>
</tr>
<tr>
<td>pepsi_bottle</td>
<td>`test_cans/pepsi_12.jpg`</td>
<td>vinommathewz/image_classification_tensorflow GitHub repo</td>
</tr>
</table>
### 임베딩 모델
- **MobileCLIP-S2 INT8 TFLite** (`mobileclip_s2_v73_int8.tflite`, 36MB)
- 안경 NPU 모델과 **동일** → 임베딩 공간 호환 (이전 ViT-B-32 임베딩과는 호환 X)
- L2 정규화 그래프에 포함됨
### 매칭 정확도 검증
8장의 별도 test 이미지로 best-match 시뮬레이션:
<table header-row="true">
<tr>
<td>카테고리</td>
<td>정확도</td>
<td>마진 분포</td>
</tr>
<tr>
<td>coke (4장)</td>
<td>4/4 = 100%</td>
<td>+0.011 ~ +0.167</td>
</tr>
<tr>
<td>pepsi (4장)</td>
<td>4/4 = 100%</td>
<td>+0.072 ~ +0.185</td>
</tr>
<tr>
<td>**전체**</td>
<td>**8/8 = 100%** ✅</td>
<td>평균 +0.103</td>
</tr>
</table>
### ⚠️ 발견 — 임베딩 분리 한계
- coke ↔ pepsi 자체 코사인 유사도 = **0.799** (상당히 비슷)
- 마진 가장 작은 케이스: jetson_coke_4 (coke 0.846 vs pepsi 0.835) **마진 +0.011**
**원인:**
- 둘 다 "탄산음료 페트병" → 시각적 매우 유사 (병 모양, 라벨 위치)
- MobileCLIP-S2 INT8 양자화로 표현력 감소
- Reference 이미지 1장씩만 — 다양성 부족
**의미:**
- ✅ Best-match (가장 가까운 것) 전략은 **100% 정확**
- ❌ 임계값 기반 매칭 (예: sim ≥ 0.5) 은 **사실상 무의미** — 거의 다 통과
- ⚠️ 양자화 노이즈로 마진 0.011 케이스 같은 거 뒤집힐 위험
### 매칭 전략 결정
**Best-match 채택**:
1. 검출된 객체의 임베딩 q 계산
2. DB의 모든 product 임베딩과 코사인 유사도 계산
3. 가장 가까운 product 선택
4. **임계값 사용 X** (또는 매우 낮게 0.3 정도 — "둘 다 너무 다르면 매칭 안 함")
5. 매칭된 product의 conquest 광고 표시
### v1.x 개선 백로그
1. **Reference 다양화** — 각 product에 3~5장 reference 이미지, 평균 임베딩 사용 → 마진 ↑
2. **다양한 배경/조명/각도** reference 모으기
3. **MobileCLIP-S2 fine-tuning** on 콜라/펩시 데이터 → brand-level 분리 ↑
4. **w8a8 calibration 데이터** 받으면 정확도 ↑ 가능
### 생성 스크립트
`build_adversarial_db.py` — reference 추가 / 다른 product 추가 시 이 스크립트 수정 후 재실행.
`test_adversarial_match.py` — 매칭 정확도 검증.
### 안경 통합 (다음 작업, 별도)
현재 만든 DB는 Python 단의 자산. 안경 (C#) 측 통합 필요:
- C# ClipExtractor 작성 — MobileCLIP-S2 TFLite로 query 임베딩
- C# 측 코사인 유사도 + best-match 로직
- 매칭된 광고 이미지를 UI 캔버스에 표시
- StreamingAssets에 db/ 자산 복사 (build_hello_ar.sh 업데이트 필요)
---
## 🎯 마일스톤 — v0.3.6 Adversarial Ad 파이프라인 완성 (2026-06-07)
### 한 줄 요약
**기획안의 6단계 파이프라인 (Trigger → YOLO → CLIP → Matching → Storing/Rendering) 의 모든 핵심 컴포넌트가 안경 NPU에서 동작 가능한 상태로 작성됨.** 안경 빌드 + HelloAR 통합만 남음.
### 진행도
<table header-row="true">
<tr>
<td>Step</td>
<td>회의록 Feature</td>
<td>상태</td>
</tr>
<tr>
<td>① Trigger (IMU)</td>
<td>F1</td>
<td>✅ GyroTrigger.cs 동작 검증</td>
</tr>
<tr>
<td>② YOLO</td>
<td>F3 (모델)</td>
<td>✅ yolo11l INT8 TFLite, 안경 NPU 실측 ~15ms</td>
</tr>
<tr>
<td>② Layer 1+2 필터</td>
<td>F2 (DB_obs)</td>
<td>✅ obs_whitelist.json + QnnYoloDetector 필터 적용</td>
</tr>
<tr>
<td>③ MobileCLIP-S2</td>
<td>F3 (모델)</td>
<td>✅ INT8 TFLite, 안경 NPU 예상 ~3ms</td>
</tr>
<tr>
<td>③ CLIP 통합 (C#/Java)</td>
<td>-</td>
<td>✅ ClipExtractor + QnnClipEngine (v0.3.6 Phase 1)</td>
</tr>
<tr>
<td>④ Storing (AIP)</td>
<td>F2 (DB_AIP)</td>
<td>❌ v1.x 이후</td>
</tr>
<tr>
<td>⑤ Ad Rendering</td>
<td>F5</td>
<td>✅ AdRenderer (2D HUD, fade lifecycle)</td>
</tr>
<tr>
<td>⑤ 광고 매칭</td>
<td>F2 (DB_Ad)</td>
<td>✅ ProductMatcher best-match</td>
</tr>
<tr>
<td>⑥ Selection</td>
<td>F7</td>
<td>⚠️ 단순화 (이미 광고 표시 중이면 skip)</td>
</tr>
<tr>
<td>F4 DB CRUD</td>
<td>F4</td>
<td>✅ build_adversarial_db.py</td>
</tr>
<tr>
<td>F6 Background Daemon</td>
<td>F6</td>
<td>❌ Hard gate, 별도 작업</td>
</tr>
</table>
### v0.3.6 새 자산
```javascript
unity_assets_prep/
├── Scripts/
│   ├── ClipExtractor.cs      (165줄) C# CLIP 추론 인터페이스
│   ├── ProductMatcher.cs     (155줄) best-match 코사인 유사도
│   └── AdRenderer.cs         (162줄) 2D HUD 광고 카드 + 라이프사이클
└── Plugins/Android/
    └── QnnClipEngine.java    (144줄) TFLite + QnnDelegate (Hexagon NPU)

db/
├── unity_db.json             통합 DB (임베딩 inline)
├── embeddings/{coke,pepsi}_bottle.npy   5-ref 평균 임베딩
└── ads/{coke,pepsi}_bottle_ad.png       광고 카드 380×240

simulate_pipeline.py          end-to-end Mac 시뮬레이터
INTEGRATION_GUIDE.md          HelloAR 통합 가이드
```
### 검증 결과 (Mac end-to-end 시뮬레이션)
`simulate_pipeline.py`로 콜라/펩시 이미지 입력 → 광고 합성 출력:
<table header-row="true">
<tr>
<td>입력</td>
<td>검출</td>
<td>CLIP 매칭</td>
<td>광고</td>
</tr>
<tr>
<td>`jetson_coke_1.jpg` (코카콜라 페트병)</td>
<td>bottle 0.89</td>
<td>coke_bottle sim=0.751</td>
<td>**Pepsi Classic** ✅</td>
</tr>
<tr>
<td>`pepsi_12.jpg` (펩시 페트병)</td>
<td>bottle 0.83</td>
<td>pepsi_bottle sim=0.930</td>
<td>**Coca-Cola Original** ✅</td>
</tr>
</table>
→ **End-to-end conquesting 동작 검증**. 안경 빌드 시 동일 흐름 기대.
### Reference 다양화 효과 (v0.3.6 개선)
<table header-row="true">
<tr>
<td>항목</td>
<td>Single ref</td>
<td>5-ref 평균</td>
</tr>
<tr>
<td>jetson_coke_4 마진</td>
<td>+0.011 (위험)</td>
<td>**+0.081** ⭐ 7배</td>
</tr>
<tr>
<td>평균 마진</td>
<td>+0.103</td>
<td>+0.131</td>
</tr>
<tr>
<td>coke cohesion</td>
<td>-</td>
<td>0.879</td>
</tr>
<tr>
<td>pepsi cohesion</td>
<td>-</td>
<td>0.923</td>
</tr>
<tr>
<td>정확도 (8장 test)</td>
<td>100%</td>
<td>**100%** (안정성 ↑)</td>
</tr>
</table>
양자화 노이즈 영향 받을 가능성 크게 감소. 향후 reference 더 추가 (10~20장) 시 더 안정.
### 안경 실측 latency (전체 파이프라인 예상)
<table header-row="true">
<tr>
<td>단계</td>
<td>시간</td>
</tr>
<tr>
<td>YOLO11l NPU</td>
<td>**~15 ms** (실측)</td>
</tr>
<tr>
<td>bbox crop + CLIP 전처리</td>
<td>~5~10 ms</td>
</tr>
<tr>
<td>MobileCLIP-S2 NPU</td>
<td>~3 ms (AI Hub 2.12ms + 안경 +27%)</td>
</tr>
<tr>
<td>Best-match</td>
<td><1 ms</td>
</tr>
<tr>
<td>Ad 표시 (이미지 캐시)</td>
<td><1 ms</td>
</tr>
<tr>
<td>**합계**</td>
<td>**~25 ms**</td>
</tr>
</table>
기획안 200ms 목표 대비 12.5%. 카메라/렌더링/Anchor lifecycle에 ~175ms 사용 가능.
### Phase 1+2 commit 이력
```javascript
e8b4feb v0.3.6 Phase 2: ProductMatcher + AdRenderer + db 자산 자동 번들
987a4d5 v0.3.6 Phase 1: CLIP 안경 통합 인프라 (QnnClipEngine + ClipExtractor)
4b11bcc v0.3.6: Adversarial Ad DB — 콜라/펩시 페트병 임베딩 + 광고 카드
0a95a13 (옆 세션 작업)
...
```
### 남은 작업 — Phase 3 (사용자 직접)
[INTEGRATION_](../)GUIDE.md 참고:
1. HelloAR.cs 에 ClipExtractor/ProductMatcher/AdRenderer 통합 코드 ~10줄 추가
2. CropTexture 헬퍼 함수 추가 (bbox 영역 잘라내기)
3. `bash build_hello_ar.sh` 실행 → 안경에 자동 설치
4. 콜라 페트병 들고 시연
5. logcat 확인:
	```javascript
[HelloAR] 매칭: coke_bottle sim=0.94 → 광고 pepsi_bottle_ad.png
[AdRenderer] Show: pepsi_bottle_ad.png ...
	```
### v1.x 백로그
- [ ] CropTexture GPU 최적화 (현재 GetPixels32 느림)
- [ ] trigger-based 단발 추론 (현재 주기적 1초)
- [ ] Reference 이미지 10~20장 (마진 더 ↑)
- [ ] w8a8 calibration data (정확도 ↑)
- [ ] Spatial Anchor (2D HUD → 3D 객체 옆)
- [ ] DB_AIP (관심 누적)
- [ ] Background Daemon (F6 hard gate)
- [ ] 광고 인벤토리 N:M (현재 1:1 conquesting)
---
## 백로그 — 광고 영상 스트리밍 (v0.4.x ~ v0.6.x) (2026-06-07 검토)
### 비전
이미지 광고 카드 → 짧은 영상 광고 (5~15초, 720p, H.264, mute) 로 업그레이드. **시각 임팩트 ↑ + 광고 단가 ↑ + 진짜 광고주 영상 자산 활용**.
### 기술적 실현성
**✅ 매우 가능.** 안경 (Snapdragon AR1 Gen 1) 이 하드웨어 H.264/H.265 디코더 보유. Unity 6 `VideoPlayer` 컴포넌트가 native 지원. 광고 단발 5~15초는 배터리 영향 무시 가능.
### 단계적 계획
**Phase 1 — v0.4.x — 로컬 MP4 (검증)**
- `db/ads/{coke,pepsi}_bottle_ad.mp4` (~500KB, 5초, mute)
- AdRenderer.cs에 VideoPlayer 추가
- 매칭 → 로컬 mp4 재생 → 끝나면 fade-out
- 네트워크 의존 X (시연 안정성)
- 작업량: 반나절 (AdRenderer +50줄, mp4 자산 준비)
**Phase 2 — v0.5.x — HTTPS 스트리밍**
- 광고 매칭 → URL → `VideoPlayer.url = "https://cdn.eagleeye.ai/ads/pepsi.mp4"`
- CDN/S3 호스팅 (CloudFront 정도 충분)
- 첫 frame 전까지 placeholder 이미지 (현재 광고 카드) → 영상 로드되면 전환
- 로컬 캐시 — 같은 광고 두 번째부터 즉시
**Phase 3 — v0.6.x — 광고 서버 연동 (비즈니스 핵심)**
```javascript
매칭 결과 → POST /adserver/match
         → 광고주 RTB 입찰
         → 응답: {"url": "...", "duration": 7, "label": "..."}
         → 재생
```
- conquesting 외 context (timing, 위치, 사용자 프로파일) 기반 동적 매칭
- 진짜 비즈니스 모델 완성형 (이게 Meta 광고 사이즈 가능)
### 영상 권장 사양
- 길이: **5~15초** (광고 거슬림 한계)
- 해상도: **720p** (640×480 디스플레이에 down-scale)
- 코덱: **H.264 baseline + faststart flag** (메타데이터 앞)
- 비트레이트: **1~3 Mbps**
- 음성: **기본 mute** (안경 스피커 거슬림. opt-in 가능)
### 스트리밍 방식 결정
<table header-row="true">
<tr>
<td>방식</td>
<td>첫 frame 지연</td>
<td>평가</td>
</tr>
<tr>
<td>**MP4 over HTTPS**</td>
<td>100~500ms</td>
<td>⭐ 채택</td>
</tr>
<tr>
<td>HLS</td>
<td>1~3초</td>
<td>△ 광고에 과함</td>
</tr>
<tr>
<td>RTMP/RTSP</td>
<td>500ms~</td>
<td>❌ 실시간용</td>
</tr>
<tr>
<td>WebView HTML5 video</td>
<td>1~2초</td>
<td>❌ 무거움</td>
</tr>
</table>
### 위험 / 대응
<table header-row="true">
<tr>
<td>위험</td>
<td>영향</td>
<td>대응</td>
</tr>
<tr>
<td>네트워크 불안정 (매장 WiFi 등)</td>
<td>영상 끊김</td>
<td>Phase 1 로컬 mp4. Phase 2 image fallback</td>
</tr>
<tr>
<td>첫 frame 지연</td>
<td>광고 표시 끊김</td>
<td>placeholder 이미지 → 영상 로드되면 swap</td>
</tr>
<tr>
<td>배터리 (영상 디코딩 추가 전력)</td>
<td>사용자 불편</td>
<td>광고 frequency cap, 짧은 광고 (5~10초), HW 디코더 활용</td>
</tr>
<tr>
<td>음성 거슬림</td>
<td>UX 거부감</td>
<td>기본 mute, 시각 임팩트로 충분</td>
</tr>
<tr>
<td>Unity VideoPlayer 안경 호환성</td>
<td>미검증</td>
<td>Phase 1 첫 검증 시 logcat 확인</td>
</tr>
</table>
### 진행 트리거
이 백로그는 **v1 시연 안정화 (이미지 광고 동작 검증) 완료 후** 진입. 우선순위:
1. ⏳ **v0.3.x 안정화** — HelloAR 통합 + 안경에서 이미지 광고 시연 성공 (현재 진행)
2. 🔜 **v0.4.x** — 로컬 mp4 광고 (반나절 작업)
3. 🔜 **v0.5.x** — CDN 스트리밍 (1~2일)
4. 🔜 **v0.6.x** — 광고 서버 + RTB (별도 backend 프로젝트, 며칠~주 단위)
### 영향 — 비즈니스 모델 측면
영상 광고로 전환하는 의미:
- 광고 단가 ★★★★★ (영상 광고는 이미지 대비 10~50배 단가, Meta 기준)
- 광고주 진입 장벽 ↓ (이미 만들어둔 TV 광고 / 유튜브 광고 그대로 활용 가능)
- AR 광고의 진짜 차별성 발휘 — 시야 안의 짧고 임팩트 있는 영상
- 광고주 입찰 결정 직전에 "어떤 영상 보낼지" 동적 결정 가능 (v0.6.x)
---
## 기술 결정 로그 — 진짜 AR 전환 + 좌표 정합 단순화 (2026-06-07)
### 결정
**카메라 pass-through (디스플레이에 카메라 영상 렌더링) → 진짜 AR (검은 배경 + 합성 UI만)** 으로 전환.
**좌표 정확도 (parallax/calibration) 는 무시.** 광고 UX엔 픽셀 정렬 불필요.
### 이전 잘못된 가정
검토 시점에 "정확한 좌표 정합이 어렵다" 고 판단:
- Calibration (1~3일 작업)
- Depth-aware parallax 보정 (며칠~주, RayNeo SDK depth API 필요)
- 양안 stereo 정합
- IPD 캘리브레이션
사용자 통찰로 무효화됨: **광고는 객체 옆에 약간 떨어져 표시되는 게 자연스러움. 100px 오차도 사용자 두뇌가 자연스럽게 "객체와 광고 연결" 인지함.**
### 본질적 통찰
광고 시스템의 production AR은 사실 매우 단순:
- AR 광고의 가치 = **"객체와 광고의 의미적 연결"** (어느 객체에 대한 광고인지)
- 그 연결은 **공간적 근접만 있어도 인지됨** (정확한 정렬 불필요)
- 진짜 AR (depth-aware Spatial Anchor) 의 가장 큰 복잡도가 **광고 UX엔 없음**
→ Apple Vision Pro 같은 풀 6DoF 시스템 불필요. **저가 AR 안경에도 우리 시스템 그대로 동작 가능.**
### 비즈니스 의미
- **디바이스 호환성 ↑** — depth API 없는 저가 AR 안경도 타깃 가능
- **시장 진입 장벽 ↓** — calibration/SDK 의존 X
- **개발 비용 ↓** — production-ready 까지 며칠 → 반나절
- **광고 시스템의 본질**: "어디에 정확히" 가 아니라 "이 객체와 관련된 광고가 떴다" 만 전달하면 가치 발생
### 새 시스템 정의 — 진짜 AR
```javascript
[현재 v0.3.x — pass-through 카메라]
카메라 영상 → 디스플레이 가득 채움 → bbox 합성 → 사용자는 사실 VR 같은 화면 봄
   ↑ 웨이브가이드 투명성 무용지물, 카메라 지연

[목표 — 진짜 AR]
카메라 영상 → YOLO 분석용으로만 (디스플레이에 안 그림)
디스플레이 → 검은 배경 (= 웨이브가이드 투명) + bbox/광고만 합성
   ↑ 사용자가 보는 것 = 현실 + 디스플레이의 떠있는 정보
```
### 구현 작업량 — 반나절
```javascript
변경 1: 카메라 background 렌더링 제거 (HelloAR.cs 한두 줄)
변경 2: bbox 단순 카메라→디스플레이 비례 매핑 (FOV 차이 대략 보정)
변경 3: 광고 위치 = bbox center + 30px offset (객체 근처)
```
### 단계적 적용
**v0.3.7 — 진짜 AR 첫 단계 (이번)**
- 카메라 background 제거
- bbox만 표시 (CLIP/광고 X — 검증용)
- 사용자가 "박스가 현실 객체 위에 떠있다" 자체 시각 확인
**v0.3.8 — 광고 통합 (다음)**
- bbox 자리에 광고 카드 합성
- CLIP 매칭 → 광고 표시
- 박스는 디버깅 모드만 (`showDebugBbox = false` 기본)
**v0.4.x ~ v0.5.x — 영상 광고 / 스트리밍**
- 이미지 광고 → 영상 광고
### 정렬 정확도 정책
- ✅ 광고 위치 = bbox center + 일관된 offset (예: 오른쪽 30px)
- ✅ 오차 50~100px 자연 흡수 (광고 380×240, 사용자 두뇌 보정)
- ❌ Calibration 불필요
- ❌ Depth-aware parallax 불필요
- ❌ 양안 stereo 정합 불필요
### v1.x 백로그 (광고 외 시나리오)
진짜 정확한 spatial anchor가 필요한 경우 (정보 라벨, 가이드 화살표 등):
- RayNeo SDK Spatial Anchor API 도입 시 별도 모드로 추가
- 광고는 단순 모드 유지 (복잡도 X)
---
## 비교 자산 — openai_clip 공식 INT8 TFLite 준비 완료 (2026-06-07)
### 목적
정확도 한계 보일 때 즉시 swap 가능하도록 Qualcomm 공식 최적화된 CLIP 모델 준비.
### 자산 위치
```javascript
clip_alternatives/openai_clip/
├── openai_clip_image_encoder.onnx       (330 MB FP32, qai_hub_models OpenAIClip.encode_image + L2 norm)
├── openai_clip_image_v73.tflite         (329 MB FP, QCS8550 v73 컴파일)
├── openai_clip_image_v73_int8.tflite    ( 86 MB INT8)
├── export_image_encoder.py              (PyTorch → ONNX, image encoder만 분리)
├── compile_clip_tflite.py               (AI Hub 컴파일)
└── results.json                         (job IDs + 측정값)
```
### 측정 결과 (QCS8550 Proxy, Hexagon v73)
<table header-row="true">
<tr>
<td>항목</td>
<td>현재 MobileCLIP-S2 INT8</td>
<td>openai_clip INT8</td>
<td>openai_clip FP</td>
</tr>
<tr>
<td>크기</td>
<td>**36 MB**</td>
<td>86 MB (2.4×)</td>
<td>329 MB</td>
</tr>
<tr>
<td>Latency</td>
<td>**2.12 ms**</td>
<td>11.92 ms (5.6×)</td>
<td>16.27 ms</td>
</tr>
<tr>
<td>입력</td>
<td>256×256</td>
<td>224×224</td>
<td>224×224</td>
</tr>
<tr>
<td>출력</td>
<td>(1, 512) L2-norm</td>
<td>(1, 512) L2-norm</td>
<td>(1, 512) L2-norm</td>
</tr>
<tr>
<td>정확도 (테스트셋 8장)</td>
<td>100%</td>
<td>미측정</td>
<td>미측정</td>
</tr>
</table>
### 결론
**MobileCLIP-S2가 압도적으로 가볍고 빠름.** Apple이 처음부터 모바일 NPU + distillation으로 설계한 강점이 Qualcomm 사후 최적화보다 본질적으로 큼.
**openai_clip 자체는 준비 완료 — 정확도 한계 보일 때 즉시 비교 측정 가능.**
### Swap 절차
1. `ClipExtractor.cs` 의 `INPUT_SIZE 256 → 224` 변경
2. `EagleEye_Unity/Assets/StreamingAssets/` 에 `openai_clip_image_v73_int8.tflite` 복사
3. `build_adversarial_db.py` 의 `MODEL_PATH` 변경 + 실행 (DB 임베딩 공간 다름, 재생성 필수)
4. 안경 빌드 + logcat 확인
### 다른 CLIP 변형 리서치 결과 (C)
<table header-row="true">
<tr>
<td>모델</td>
<td>qai_hub_models 지원</td>
<td>작업량 (자체 컴파일)</td>
<td>추천</td>
</tr>
<tr>
<td>**openai_clip**</td>
<td>✅</td>
<td>0 (완료)</td>
<td>비교 측정용 (이 entry)</td>
</tr>
<tr>
<td>**MobileCLIP2-S3/S4** (Apple 2025)</td>
<td>❌</td>
<td>반나절 (ml-mobileclip 레포 활용, 우리 워크플로 동일)</td>
<td>**v1.x 다음 시도 1순위**</td>
</tr>
<tr>
<td>SigLIP (Google 2023)</td>
<td>❌</td>
<td>며칠 (ONNX export 도전)</td>
<td>v1.x 다국어/정확도 라운드</td>
</tr>
<tr>
<td>MetaCLIP (Meta 2023)</td>
<td>❌</td>
<td>며칠</td>
<td>v1.x 후순위</td>
</tr>
<tr>
<td>DINOv2 (Meta 2023)</td>
<td>❌</td>
<td>며칠</td>
<td>v1.x self-supervised 실험</td>
</tr>
<tr>
<td>EVA-CLIP (BAAI 2024)</td>
<td>❌</td>
<td>일주일 (모델 매우 큼)</td>
<td>v2+ 클라우드 fallback</td>
</tr>
</table>
### 진행 우선순위
**현재 (v1 시연용)**: MobileCLIP-S2 그대로 유지 ✅
**v1.x 정확도 라운드**:
1. **MobileCLIP2-S4** 시도 (가성비 1순위)
2. openai_clip 비교 측정 (이미 준비됨)
**v2+ 확장**:
- SigLIP 자체 컴파일 — 다국어 광고 가능성
- 클라우드 fallback — EVA-CLIP 정밀 식별 (안경 NPU 무리)
---
## 자산 — 광고 영상 mp4 (v0.4.x 준비) (2026-06-07)
### 목적
영상 광고 스트리밍 백로그 (v0.4.x) 의 Phase 1 (로컬 mp4 검증) 진입 즉시 가능하도록 자산 사전 확보.
### 자산 위치
```javascript
db/ads_video/
├── coke_bottle_ad.mp4   1.0 MB  10.01초
└── pepsi_bottle_ad.mp4  1.1 MB  10.01초
```
### 원본
<table header-row="true">
<tr>
<td>광고</td>
<td>출처</td>
<td>원본 길이</td>
<td>trim</td>
</tr>
<tr>
<td>코카콜라</td>
<td>YouTube 공식 채널 — "Coca-Cola \</td>
<td>For Everyone :30"</td>
<td>30초</td>
</tr>
<tr>
<td>펩시</td>
<td>YouTube 공식 채널 — "Pepsi Max TV Commercial, '15 Seconds'"</td>
<td>15초</td>
<td>앞 10초</td>
</tr>
</table>
다운로드: yt-dlp ytsearch 첫 결과 → 진짜 공식 광고 확인 (preview frame 시각 검증)
### 안경 사양 트랜스코딩 (FFmpeg)
<table header-row="true">
<tr>
<td>항목</td>
<td>권장</td>
<td>실제</td>
</tr>
<tr>
<td>컨테이너</td>
<td>MP4 + faststart</td>
<td>✅ +faststart</td>
</tr>
<tr>
<td>코덱</td>
<td>H.264 Baseline</td>
<td>✅ Constrained Baseline (더 안전)</td>
</tr>
<tr>
<td>Profile/Level</td>
<td>level 3.1</td>
<td>✅</td>
</tr>
<tr>
<td>Pixel format</td>
<td>yuv420p</td>
<td>✅</td>
</tr>
<tr>
<td>해상도</td>
<td>720p ↓ down-scale OK</td>
<td>640×360 (안경 640×480에 적합)</td>
</tr>
<tr>
<td>비트레이트</td>
<td>1~2 Mbps</td>
<td>830~910 kbps</td>
</tr>
<tr>
<td>음성</td>
<td>mute</td>
<td>✅ `-an`</td>
</tr>
<tr>
<td>길이</td>
<td>5~15초</td>
<td>10.01초</td>
</tr>
<tr>
<td>파일 크기</td>
<td><2 MB</td>
<td>1.0~1.1 MB</td>
</tr>
</table>
명령:
```javascript
ffmpeg -y -i SRC.mp4 \
    -t 10 \
    -c:v libx264 -profile:v baseline -level 3.1 -pix_fmt yuv420p \
    -b:v 1M -maxrate 1.5M -bufsize 2M \
    -movflags +faststart \
    -an \
    OUT.mp4
```
### Phase 1 통합 절차 (v0.4.x 진입 시)
```javascript
1. Unity 빌드에 자산 복사 (build_hello_ar.sh에 추가)
   cp db/ads_video/*.mp4 EagleEye_Unity/Assets/StreamingAssets/

2. AdRenderer.cs에 VideoPlayer 추가 (~50줄)
   - Unity VideoPlayer 컴포넌트
   - RenderTexture target → Canvas 표시
   - prepareCompleted → Play
   - playOnAwake = false, audioOutputMode = None

3. 매칭 시 mp4 파일명을 광고 카드 PNG 대신 사용
   AdRenderer.Show("coke_bottle_ad.mp4", "Coca-Cola Original Taste")
   (현재는 .png 받음 → .mp4 도 받게 확장자별 분기)

4. 안경 빌드 + 시연
   - 콜라 페트병 응시 → 펩시 광고 영상 재생 (10초 + fade out)
   - 펩시 페트병 응시 → 코카콜라 광고 영상 재생
```
### 안경 동작 영향 — 0 (현재)
- 자산은 `db/ads_video/` 에 보관만 — 빌드 스크립트가 복사 안 함 (v0.4.x 진입 시 명시적으로 추가)
- 안경에서 도는 v0.3.x APK 변화 X
### ⚠️ 라이선스 주의
- 출처: YouTube 공식 채널 다운로드 (yt-dlp)
- 용도: **PoC 시연 / 내부 검증 한정**
- ❌ 외부 배포 / 상업적 production 시연 시 사용 금지
- ✅ Production 시 광고주 영상 라이선스 직접 받거나 자체 제작 필요
### 다음 (v0.4.x 진입 시)
1. **로컬 mp4 검증** — 안경 VideoPlayer 동작 확인 (반나절)
2. **S3 + CloudFront HTTPS** — 광고 URL 동적 교체 가능 (1~2일)
3. **광고 서버 RTB** — 비즈니스 모델 완성 (2~4주)
### 자산 재생성 (필요 시)
다른 광고로 교체하거나 더 짧게 / 다른 사양:
```javascript
yt-dlp "ytsearch1:<query>" -o raw.%(ext)s
ffmpeg -y -i raw.mp4 -t 10 -c:v libx264 -profile:v baseline -pix_fmt yuv420p \
    -b:v 1M -movflags +faststart -an OUT.mp4
```
<page url="https://app.notion.com/p/378e7827dafa815981a3ca55cc4882f7">진행 로그 2026-06-07 (loop 모드)</page>
## 기술 결정 로그 — 1인칭 시야 녹화 시스템 (2026-06-08)
### 결정
**착용자 시야(현실 + waveguide overlay)를 "카메라 dump + 디스플레이 녹화 → 사후 screen-blend 합성"으로 근사 재구성.** commit `6452d2a`.
### 배경 / 원리
- AR 글라스 시야는 디스플레이와 현실이 광학적으로 겹쳐 있어 직접 녹화 불가
- waveguide 디스플레이는 **빛을 더하는(additive)** 방식 → 검은 픽셀 = 투명 = 현실 그대로 보임
- 따라서 디스플레이 녹화(overlay-on-black)를 카메라 영상 위에 **screen blend** (`1-(1-a)(1-b)`) 하면 눈으로 본 시야의 근사 영상 1개를 얻음
- 한계 인지: 카메라 FOV ≠ 눈 FOV — 동일시 불가, 어디까지나 기록/시연/디버깅용 근사
### 구성요소
<table header-row="true">
<tr>
<td>파일</td>
<td>역할</td>
</tr>
<tr>
<td>`unity_assets_prep/Scripts/FirstPersonRecorder.cs`</td>
<td>안경 쪽: 카메라 JPEG dump (5fps) + 동기화 플래시 + meta.json. 자동 스폰 — 씬 수정 불필요</td>
</tr>
<tr>
<td>`unity_assets_prep/Plugins/Android/RecordingReceiver.java`</td>
<td>adb broadcast 수신 (RECORD_START/STOP). Android 13+ RECEIVER_EXPORTED</td>
</tr>
<tr>
<td>`tools/recording/record.sh`</td>
<td>Mac 원클릭: 디스플레이 녹화(scrcpy 우선/screenrecord fallback) + broadcast + pull</td>
</tr>
<tr>
<td>`tools/recording/merge.py`</td>
<td>시간 정렬 + screen-blend → merged.mp4 (1인칭 근사) + sbs.mp4 (좌우 비교)</td>
</tr>
<tr>
<td>`tools/recording/calib.json`</td>
<td>공간 정렬 상수 4개 (디스플레이 → 카메라 프레임 매핑, 1회 캘리브레이션)</td>
</tr>
</table>
### 사용법
```javascript
cd tools/recording
./record.sh start        // 디스플레이 녹화 + 카메라 dump 동시 시작
./record.sh stop         // 종료 + 안경에서 pull
python3 merge.py ../../output/recordings/rec_<날짜>/
// → merged.mp4, sbs.mp4, camera.mp4
```
### 핵심 설계 결정
1. **별도 녹화 앱 ❌** — Android 카메라는 한 앱 독점. Unity 앱이 카메라를 점유 중이므로 dump 기능을 앱 내장 + adb broadcast 토글로 해결
2. **시간 동기화 자동화** — RECORD_START 수신 직후 디스플레이에 0.2초 흰 플래시 렌더 + draw 시점 wall-clock 을 meta.json 에 기록. merge.py 가 display.mp4 의 밝기 스파이크(YAVG)로 플래시 자동 탐지 → 수동 sync 0. 정밀도 ~1프레임
3. **녹화 중 AR 모드 강제** — screen-blend 는 "디스플레이 = overlay-on-black" 전제. passthrough(showCameraPreview=true)로 녹화하면 이중노출 → FirstPersonRecorder 가 녹화 동안 showCameraPreview=false 강제, 종료 시 복원. passthrough 세션은 merge.py 가 감지해 blend 생략
4. **SBS 스테레오 대응** — 디스플레이 녹화는 좌우 양안 → merge 시 좌안만 crop
### 제약 / 주의
- 앱 foreground 일 때만 broadcast 수신 (dynamic receiver)
- 녹화는 관측 대상을 교란 (GetPixels32 main-thread readback) — 동작 검증용, latency 측정용 아님
- scrcpy 미설치 시 screenrecord fallback = 3분 제한 (`brew install scrcpy` 권장)
- 검증: **merge 파이프라인만** 합성 데이터로 검증 완료 (플래시 탐지/오프셋 계산/blend 위치 정확). **기기 쪽 코드(FirstPersonRecorder/RecordingReceiver)는 미실행** — 첫 실기기 체크리스트는 `tools/recording/README.md` (commit `af3b413`) 참조: baseDir 경로 / broadcast 수신 / 플래시 포함 여부 / AR 모드 강제 4가지
### 안경 동작 영향 — 다음 빌드부터
- `build_hello_ar.sh` 가 *.cs / *.java 자동 복사 → 다음 빌드에 자동 포함, 별도 통합 작업 없음
- 녹화 미사용 시 오버헤드 ~0 (0.25초마다 JNI bool polling 1회)

---

## 기술 결정 로그 — Hierarchical 매칭 (CLIP category + OCR brand) (2026-06-08)

### Why — CLIP zero-shot 의 brand 분별 한계 발견

v0.5.x 시리즈 시연 (~15 빌드) 에서 본질적 문제 확인:

- **CLIP (MobileCLIP-S2 INT8) 가 fine-grained brand 분별을 본질적으로 못 한다.** coke vs pepsi 페트병처럼 시각 유사 객체의 brand 신호 (라벨 색/글자) 보다 환경 신호 (배경, 책상색, 조명) 가 dominant.
- **결정적 증거**: pepsi 시연 15 trigger 전체 pepsi 매칭 **0건** (coke 7, laptop 8). pepsi top1 항상 coke top1 보다 낮음. 사용자 환경 = jetson_coke refs 의 빨간 책상 분포와 매칭되어 coke 가 강함.
- top-k 튜닝, threshold 조정, ref 다양화 어떤 조합도 fix 안 됨. 본질적 한계.

### Decision — 매칭 architecture 전환

```
Stage 1: CLIP → category (cola vs laptop vs ...) — coarse, 환경 영향 OK
Stage 2 fallback chain:
  ① OCR keyword 매칭 → brand 확정 (primary, deterministic)
  ② OCR fail + brand 다수 (cola) → CLIP brand-specific top-1 매칭
  ③ OCR fail + brand 1개 (laptop=macbook) → 광고 X (false positive 방지)
```

### 기술 스택 추가

- **OCR**: MLKit Text Recognition v2 (Latin, Android native, ~50ms). `com.google.mlkit:text-recognition:16.0.1`.
- **Wrapper**: `MLKitOCR.java` (Java) + `OCRExtractor.cs` (C#).
- 다국어 (한글, 중국어) 는 별도 model 패키지 (v1.5+ 시장 확장 시).

### Refs / DB schema 변경

- **refs/** 폴더 hierarchical 화:
  - `refs/cola/` — category embedding (coke 4장 + pepsi 2장 합쳐서 6장)
  - `refs/cola_brands/coke/` (4장), `refs/cola_brands/pepsi/` (2장) — brand-specific
  - `refs/laptop/` (1장) — laptop_ref.jpg
- **unity_db.json schema v0.7.1**: `categories[]` + `brands[]` 중첩. brand 마다 `keywords`, `negative_keywords`, optional `embeddings_flat`.

### v1+ 본질 fix (시연 후)

1. **Fine-tuned CLIP** (가장 근본적) — coke/pepsi (또는 30 SKU 라면) contrastive learning. embedding space 가 brand 분리하도록 학습. PoC 후 첫 작업.
2. **YOLO crop + CLIP** — bbox 영역만 CLIP. 환경 제거.
3. **CLIP 모델 교체** — SigLIP-2 (fine-grained ↑), DINO v2.
4. **OCR 라벨 영역 crop** — YOLO box → upscale → OCR. 정확도 ↑.

### Eagle Eye 시스템 비전 관점

- "AR Contextual Information Layer" 의 핵심 = **정확한 brand-level 식별**. CLIP zero-shot 만으로 절대 한계.
- Hierarchical (CLIP category + OCR brand) + Fine-tuned CLIP 이 v1+ 의 필수 component.
- 확장성: 새 brand 추가 시 keyword 1줄 추가 (학습 불필요). 30 SKU 라면 카테고리도 동일 패턴.
- 한국 광고법 audit 시 유리: 매칭 근거 = "keyword text match + visual similarity" 로 설명 가능.

### 관련 파일

- `unity_assets_prep/Scripts/ProductMatcher.cs` (v0.7.x)
- `unity_assets_prep/Scripts/OCRExtractor.cs`
- `unity_assets_prep/Plugins/Android/MLKitOCR.java`
- `unity_assets_prep/Plugins/Android/mainTemplate.gradle` (MLKit dependency)
- `build_adversarial_db.py`, `build_unity_db.py`
- `db/metadata.json`, `db/unity_db.json` (categories + brands)

상세 인사이트 + APK 버전 history → [progress-log.md](progress-log.md) 의 "🔑 핵심 인사이트 (06-08 01시 세션 종료 dump)" 섹션.

---

## 기술 결정 로그 — brand 식별: 색 휴리스틱 (Coca-Cola) + OCR (Pepsi), 프로토타입 한정 (2026-06-08)

### 결정
v0.7.5 프로토타입에서 brand 확정을 **하이브리드**로:
- **OCR keyword** — 블록체 라벨 브랜드 (예: "PEPSI") 에 사용. deterministic, 잘 됨.
- **색 휴리스틱** — Coca-Cola 처럼 **필기체(Spencerian script) 로고라 OCR 이 못 읽는** 브랜드는 **라벨 색(콜라=빨강 vs 펩시=파랑)** 으로 판정.
- 전제: **타이트한 중앙 crop + 조준 가이드** 로 병을 배경에서 분리한 뒤 색/글자 측정 (배경 혼입 시 색 신호 무너짐).

### 왜 (이번 시연 데이터로 확정된 근거)
- **OCR 은 Coca-Cola 필기체를 못 읽음** — v0.7.4(brand fallback OFF, OCR 전용)에서 매칭된 brand 는 **pepsi(블록체) 뿐, coca-cola 0건**. v0.7.2 의 coca-cola "성공" 은 OCR 이 아니라 CLIP fallback 이 환경 bias 로 항상 coca-cola 를 찍던 것 (= 진짜 인식 아님).
- **CLIP brand-specific 임베딩도 fine-grained 분별 불가** — cropped CLIP 으로 coke vs pepsi 판정 1/5 (사실상 랜덤, sim 전부 0.76~0.83 박빙). 문서 인사이트 #1 그대로.
- **naive 색도 배경 혼입 시 무너짐** — 전체 프레임 red/blue 비율은 따뜻한 배경(타일·나무·피부)에 펩시(파랑)가 빨강으로 오판(5/7). → **병만 isolation 해야 색이 robust**.
- 결국 OCR·CLIP·색 모든 신호가 "병이 배경에서 분리되어야" 작동 → **조준 가이드 + 타이트 crop** 이 공통 전제.

### ⚠️ 한계 — 이건 일반해가 아니다 (프로토타입 hack)
- **색(빨강/파랑) 휴리스틱은 코카콜라/펩시가 우연히 색이 달라서 되는 것.** 모든 카테고리·모든 브랜드에 공통 적용 불가:
  - 같은 색 경쟁 브랜드 (예: 빨강 라벨 둘), 색이 브랜드 신호가 아닌 카테고리, 다색 패키지 → 즉시 실패.
  - 30+ SKU 로 확장하면 색만으로 분별 불가.
- **OCR 도 필기체/특수 서체/곡면/다국어(한글 코카콜라) 라벨에서 한계.**
- **일반해 (v1.5+)**: ① fine-tuned CLIP (brand contrastive learning — embedding 이 brand 분리하도록) ② 로고 전용 detector (template/feature matching — 알려진 로고에 강함) ③ robust 다국어 OCR + 라벨영역 crop ④ 객체 detection(YOLO/SAM)으로 병 bbox isolation 후 위 신호 적용.
- **이번 결정은 "coke/pepsi 2-브랜드 데모를 동작시키는" v1 PoC 스코프 한정.** 시스템 정체성(§1~3)·일반 매칭 전략은 불변이며, 색 휴리스틱을 제품 방향으로 일반화하지 말 것.

### 적용 범위 / 다음
- v0.7.5: 조준 가이드(디스플레이 중앙 박스) + 타이트 center crop + brand = OCR("pepsi") OR 색(red→coca-cola). 
- v1.5+ 에서 위 "일반해" 로 대체.
