<!-- source of truth. 노션에서 export 후 폐기 (2026-06-08). 일일 진행 기록은 이 파일에 추가한다. -->
<!-- 부모 문서: vision.md (시스템 비전 + v1 PoC 사양). 읽는 법은 루트 CLAUDE.md 참조. -->
<!-- 구 파일명: progress-log-2026-06-07.md. 파일명의 날짜는 최초 작성일이며, 06-08 v0.7.2 까지의 후속 기록 포함. -->

# ⚙️ 진행 로그 2026-06-07 (loop 모드)

## 현재 상태 (Claude 자동 진행)
사용자 1시간 자리비운 동안 /loop 모드로 진행. 안경 연결 끊긴 상태로 빌드까지만 완성.

## 빌드 산출물 (안경 연결 시 install 가능)
6개 APK 백업, 각 237MB:
- `EagleEye-YOLOplusCLIP.apk` — v0.5.0 초기 (clipOnlyMode=false, coke/pepsi 2 products)
- `EagleEye-CLIPonly-laptop.apk` — v0.5.0 CLIP only (3 products with laptop)
- `EagleEye-v0.5.1-CLIPonly.apk` — v0.5.1 (Sponsored 라벨 + AIP + 5s active, CLIP only)
- `EagleEye-v0.5.1-YOLOplusCLIP.apk` — v0.5.1 (YOLO+CLIP)
- `EagleEye-v0.5.2-comparison.apk` — **최신: 비교 카드 UI 적용 (노션 4.6 spec)**

## v0.5.1 ~ v0.5.2 변경 사항 (이번 loop 세션)

### v0.5.1
- AdRenderer: Sponsored 라벨 추가 (노션 8장 결정사항, 한국 광고법)
- AdRenderer: active 시간 4s → **5s** (노션 8장 제안값)
- 새 컴포넌트: `AmbientInterestProfile.cs` (AIP 스키마 + 누적 로깅)
    - 노션 3.3 spec: passing-by 데이터 온디바이스 누적
    - 100% 온디바이스 (노션 10.1 원칙)
    - 트리거마다 class/conf/product 누적 → persistentDataPath/aip.json
    - v1 = schema 정의만, v2+ 활용

### v0.5.2 (최신)
- 비교 카드 UI 적용 (노션 4.6 spec):
    - 1줄: `📍 식별된 상품` (확인용)
    - 2줄: **브랜드 평점★ 가격** (Conquest 비교 정보, 노란 강조)
    - 3줄: `"차별점"` (한 줄)
    - 4줄: 위치 hint
    - 하단: `Sponsored` (광고 disclosure)
- db/metadata.json + unity_db.json 에 `comparison` 필드 추가:
    - identified_name, ad_brand, rating, price, differentiator, location
- ProductMatcher.Product + AdRenderer.ShowComparison() 메서드

## 안경 연결 후 작업 (사용자)
```bash
# 추천: 최신 비교 카드 UI 빌드 install
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.2-comparison.apk
adb shell am start -n com.eagleeye.helloar/com.unity3d.player.UnityPlayerGameActivity
```

## 미구현 / 다음 단계 후보
- 비교 카드 UI **안경 시연 검증** (Mac Editor 검증 안 함)
- yolo11x export 시도 (정확도 ↑, latency trade-off)
- ORT + QNN EP path 재시도 (v0.4.0 보류, INT16 정밀도)
- 시뮬레이터 (simulate_pipeline.py) 강화 — 새 comparison 카드 검증
- AIP 데이터 활용 (v2+) — 노션 3.3
- 안경 카메라 회전 보정의 fix (기존 270 hardcode, 일관성 확인 필요)

## 알려진 이슈
- 디스크 빠듯 (현재 4.3GB). 빌드 한 번 더 가능. 더 정리 시 export_assets/yolo11l_w8a16_* 폐기 후보
- 안경 카메라 sensor orientation 일관성 불확실 (rot_00 정상 vs rot_01 90도). hardcode 270 으로 현재 우회
- YOLO 의 bottle/book 매우 약함 (conf 0.05 수준). CLIP 으로 우회 시도 중 (검증 안 됨)

## /loop 모드 동작
- 10분 fallback, 빌드 알림 시 즉시 wake
- 추가 미구현 진행 또는 빌드 fail 시 노션에 기록

---

## 2번째 loop iteration (19:00 경)

### 추가 작업
1. **Mac CLIP 매칭 정확도 검증** — `test_adversarial_match.py`
    - 8/8 = **100%** (test_cans 이미지로 coke vs pepsi 분류)
    - sim 범위: coke 0.93~0.97, pepsi 0.94~0.97
2. **`simulate_pipeline.py` 강화** — Mac 측 비교 카드 UI 시각 검증
    - 한국어 폰트 (AppleSDGothicNeo) 적용
    - 비교 카드 UI 노션 4.6 spec 시뮬 PNG 생성
    - 결과 파일:
        - `output/sim_coke_v052b.png` — coke → Pepsi Classic 카드
        - `output/sim_pepsi_v052b.png` — pepsi → Coca-Cola 카드

### v0.5.2 비교 카드 UI 시각 (시뮬)
카드 내용 (예 — coke 감지 시):
- `📍 Coca-Cola Classic` (식별, 회색)
- `Pepsi Classic 4.3★ · ₩1,200` (Conquest, 노란 강조)
- `"코카콜라보다 단맛이 강함"` (차별점, 이탤릭)
- `음료 코너 좌측 2칸` (위치 hint, 청색)
- 우하단 `Sponsored` (광고 disclosure)

### 안경 한국어 폰트 우려
Mac sim 한국어 OK. **안경 Unity GUI 의 기본 폰트는 한국어 미지원 가능성**.
- 안경 install 후 박스에 한국어 깨지면 NanumGothic.ttf 같은 폰트 asset 추가 필요
- 우선순위 낮음 (사용자 직접 확인 후 결정)

### 검증 안 한 항목
- 안경 실시간 카메라 ↔ CLIP 일반화 (Mac test_cans 와 다른 분포)
- 박스 위치 정확도 (안경 카메라 view ↔ 디스플레이 매핑)
- 비교 카드 UI 한국어 (안경)

---

## 3번째 loop iteration (19:35 경)

### v0.5.3 빌드 — 영어 metadata
**이유**: Unity 기본 GUI 폰트 한국어 미지원 → 안경 install 시 박스 카드 깨짐 위험. PoC 시연용으로 영어 변경.
**변경 사항**:
- `db/metadata.json` 의 `comparison.differentiator/location/price` 한국어 → 영어
- 예: "코카콜라보다 단맛이 강함" → "Sweeter and less caffeine"
- `db/unity_db.json` 재생성
- 빌드 후 `EagleEye-v0.5.3-english.apk` 백업

### 현재 백업된 APK (디스크 정리 후)
- `EagleEye-v0.5.2-comparison.apk` — 비교 카드 UI, **한국어**
- `EagleEye-v0.5.3-english.apk` — 비교 카드 UI, **영어** (안경 install 권장)

### 안경 연결 시 install
```bash
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.3-english.apk
adb shell am start -n com.eagleeye.helloar/com.unity3d.player.UnityPlayerGameActivity
```

### 디스크 상태
- 4.4 GB 여유 (옛 v0.5.1 정리 후)
- 추가 fresh build 가능 (incremental 안전, fresh 빠듯)

### 다음 작업 후보 (안경 install 후 결정)
1. **한국어 박스 결과 본 후** — 깨지면 Unity Font asset 추가 또는 영어 유지
2. **안경 실시간 카메라 검증** — CLIP 의 안경 시점 일반화
3. **박스 위치 정확도** — letterbox vs stretch 비교 (사용자 선택 없음, 박스 mismatch 진단)
4. **AIP 누적 데이터 확인** — `adb pull /storage/emulated/0/Android/data/com.eagleeye.helloar/files/aip.json`

---

## 4번째 loop iteration (19:40 경)

### 발견 — laptop reference embedding 잘못됨
`simulate_clip_only.py` 작성 후 시뮬:
- `products/laptop_ref.jpg` → **coke_bottle** 로 잘못 매칭 (sim=0.51)
- 원인: db/embeddings/laptop.npy 의 source 불명 (metadata reference_images 비어있음)
- 검증: new laptop emb (laptop_ref.jpg) vs old laptop emb = **sim 0.014** (orthogonal — 완전 다른 source)

### Fix
`products/laptop_ref.jpg` 로 새 laptop embedding 재생성:
- new laptop emb 저장 (`db/embeddings/laptop.npy` 덮어쓰기)
- `unity_db.json` 재생성
- sim 재시도: laptop_ref → **laptop matched** (sim=1.01, 정상)
- coke 도 그대로 잘 매칭 (sim=0.958)

### 새 도구
`simulate_clip_only.py` — Mac CLIP-only 시뮬레이터. YOLO 우회, frame 통째 → CLIP → best match.
안경 측 clipOnlyMode=true 흐름 동일.

### v0.5.4 빌드 진행 중 (incremental, bg9tms05s)
새 unity_db.json (laptop ref 수정) 적용. 빌드 완료 시 백업 + 노션 update.

### 다음 안경 install 권장
```bash
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.4-fixed-laptop.apk  # 빌드 완료 후
```

### 안경 시점 laptop 검증 미완
- new laptop emb 가 reference 1장 만으로 만들어짐 (laptop_ref.jpg)
- 안경 카메라의 다양한 laptop view 와 generalize 어떨지 미지
- 안경 install + 실측 후 결정

---

## v0.5.4 빌드 완료 (20:06)
- APK: `EagleEye-v0.5.4-fixed-laptop.apk` (237M)
- 변경: laptop reference embedding 수정 (`products/laptop_ref.jpg` 자체로 재생성)
- 다른 모든 v0.5.3 features 포함 (Sponsored + AIP + 5s + comparison + 영어 metadata)

### 안경 install 시 (사용자 안경 연결 후)
```bash
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.4-fixed-laptop.apk
```

### 현재 백업된 APK (최신순)

| APK | 변경 |
|---|---|
| **v0.5.4-fixed-laptop** | laptop embedding 수정 (정확도 ↑) |
| v0.5.3-english | 한국어 → 영어 |
| v0.5.2-comparison | 비교 카드 UI 첫 적용 (한국어) |

### loop 작업 상태 — 안경 없이 가능한 모든 작업 완료
- ✅ 비교 카드 UI (노션 4.6 spec)
- ✅ Sponsored 라벨 (한국 광고법)
- ✅ Auto dismiss 5s (노션 8장)
- ✅ AIP 스키마 (노션 3.3)
- ✅ 영어 metadata (Unity 폰트 호환)
- ✅ laptop embedding 수정
- ✅ Mac CLIP 매칭 정확도 100% (test_cans 8/8)
- ✅ simulate_pipeline + simulate_clip_only 시뮬레이터 강화

### 사용자 복귀 후 결정 필요 사항
1. **v0.5.4 install + 안경 시연 검증**:
    - 비교 카드 UI 시각 (한국어 깨짐 X, 영어 OK 여부)
    - laptop 시점 매칭 (안경 카메라 ↔ laptop_ref.jpg 일반화)
    - 박스 위치 정확도 (parallax + stretch mismatch)
2. **CLIP 안경 시점 일반화**:
    - test_cans (책상 위 정상 시점) 와 안경 (사용자 시점) 분포 차이
    - 정확도 떨어지면 reference 더 다양화 (test_cans 더 많이 추가)
3. **YOLO 보류 결정**:
    - YOLO 의 안경 시점 bottle/book 못 잡음 — 모델 fine-tuning 또는 yolo11x 시도
    - 또는 CLIP-only 모드 유지

### 디스크
- 4.5 GB 여유 (옛 v0.5.1 정리 후)
- 추가 fresh build 가능 (한 번 더 OK)

---

## 5번째 loop iteration (20:35 경)

### v0.5.5 빌드 — 노션 4.5 Tier 3 spec 준수
**이유**: 노션 4.5 spec 의 "Tier 3 trigger 조건 = 객체 크기 25%+" 적용. 기존 코드는 minAreaRatio=0.01 (1%, 노이즈까지 통과).
**변경**:
- `unity_assets_prep/Scripts/QnnYoloDetector.cs`: minAreaRatio 0.01 → 0.25
- Tooltip 에 "PoC 시연에서 detection 약하면 0.05 정도로 완화 가능" 명시 (Inspector 조정용)
- 빌드 후 `EagleEye-v0.5.5-tier3-spec.apk` (237M) 백업

### 사전 측정 — test_cans 15장 area_ratio
Mac 에서 YOLO11l → area_ratio 산출 (안경 시연 detection rate 예측):
- **8/15 통과** (>= 25%): coke_3/4/5, dave_pepsi_1/2/3, jetson_coke_2/3 (25~44%)
- **4/15 fail** (< 25%, 카메라 멀거나 작게 찍힌 분포):
    - jetson_coke_1 (19.6%)
    - jetson_coke_4 (14.5%)
    - jetson_coke_5 (11.8%)
    - coke_11 (22.5%)
- **3/15 no det** (YOLO 자체 못 잡음)

### 시연 risk 진단
spec 25% 는 conservative — 안경 카메라가 살짝 멀면 detection drop. 두 가지 옵션:
1. 시연 시 안경을 product 에 충분히 가까이 (1m 이내) → 통과 확률 ↑
2. spec 완화 필요 시 Unity Inspector 에서 `minAreaRatio` 0.10~0.15 로 조정

### 현재 백업된 APK (최신순)
- **v0.5.5-tier3-spec** — Tier 3 area 25% (노션 4.5 spec) — strict
- v0.5.4-fixed-laptop — laptop embedding 수정 — area 1% (loose)
- v0.5.3-english — 영어 metadata — area 1% (loose)

### 안경 install 권장
```bash
# spec 준수 (시연 stage 가 product 와 가까울 때)
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.5-tier3-spec.apk

# 또는 loose fallback (멀리서도 detection 필요할 때)
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.4-fixed-laptop.apk
```

### 다음 결정 필요 사항 (사용자 복귀 후)
- v0.5.5 시연 후 detection rate 부족하면 Inspector 로 minAreaRatio 0.10~0.15 조정 (재빌드 없이 가능)
- 또는 v0.5.4 (1% loose) 로 fallback
- 시연 거리에 따라 strict vs loose 선택

## 사용자 복귀 시 행동 가이드 (20:40 기준 최신)

### 1. 최우선 결정: 어느 APK 로 시연할 것인가?
**시연 거리 (안경 ↔ product) 에 따라 선택:**

| 거리 | APK | minAreaRatio | 근거 |
|---|---|---|---|
| **가까이 (~30cm, 손에 든 상태)** | `EagleEye-v0.5.5-tier3-spec.apk` | 0.25 (spec) | 노션 4.5 Tier 3 spec 그대로 |
| **멀리 (50cm~1m)** | `EagleEye-v0.5.4-fixed-laptop.apk` | 0.01 (loose) | spec 위반이지만 detection rate ↑ |

**Mac 측 사전 측정 (15장 test_cans 기준)**:
- v0.5.5 (25% 기준): **8/15 통과** (53%) — 가까운 사진만 통과
- v0.5.4 (1% 기준): **12/15 통과** (80%) — no det 3장 제외 모두 통과

**즉 — v0.5.5 는 strict, 시연 환경이 안경을 product 에 가까이 가져갈 수 있을 때만 권장.**
시연이 멀리서면 v0.5.4 가 안전.

### 2. install 명령
```bash
# spec 준수 (가까운 시연)
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.5-tier3-spec.apk

# 또는 spec 위반 loose (먼 시연)
adb install -r ~/Desktop/AR_project/EagleEye-v0.5.4-fixed-laptop.apk

# 실행
adb shell am start -n com.eagleeye.helloar/com.unity3d.player.UnityPlayerGameActivity
```

### 3. Inspector 미세 조정 (재빌드 없이)
만약 v0.5.5 install 후 시연에서 detection rate 부족하면 Unity Editor 의:
- `QnnYoloDetector` 컴포넌트 → `Min Area Ratio` 슬라이더 → **0.10~0.15** 로 완화
- 빌드 다시 → install 다시
(또는 그냥 v0.5.4 fallback 사용)

### 4. 시연 흐름 (안경에서 보일 것)
콜라 페트병을 시야에 가까이 가져가면:
1. YOLO 가 bottle 감지 (Tier 3 = 25% 이상일 때)
2. CLIP 으로 코카콜라 vs 펩시 분류 (sim >= 0.30)
3. 비교 카드 UI 표시 (5초간):
    - `Coca-Cola Classic` (식별)
    - `Pepsi Classic 4.3★ · $1.20` (Conquest)
    - `"Sweeter and less caffeine"` (차별점)
    - `Beverage aisle, left 2` (위치)
    - `Sponsored` (광고 disclosure)

---
---

## 6번째 loop iteration (20:43 경)

### AIP spec 3.3 정합성 분석 (코드 변경 X)
`AmbientInterestProfile.cs` + `HelloAR.cs` 의 로깅 흐름 검토:

**현재 상태 (v0.5.5)**:
- ✅ AIPEvent schema 정의 (timestamp_ms, class_id, class_name, confidence, duration_sec, product_name, product_sim)
- ✅ AIPProfile 누적 + persistentDataPath/aip.json 저장
- ✅ 100% 온디바이스 (외부 전송 X, spec 10.1 부합)

**spec 3.3 정합성 갭** (`v2+ 활성화 시 변경 필요`):
- spec 3.3: "**스쳐가는** 순간 → 광고 표시 X but 로그는 남긴다"
- **현재 흐름**: YOLO 의 minAreaRatio=0.25 필터 통과한 box 만 AIP 로 들어감
- **누락**: Tier 3 미달 box (area < 25%) = passing-by — 현재 누락됨
- spec 본문 "v1 = schema 정의만, 활용은 v2+" 이라 v1 PoC 위반 아님
- v2+ 활용 시 변경 필요 사항:
    - QnnYoloDetector 가 raw box (필터 미달 포함) 도 노출
    - HelloAR 에서 passing-by 별도 로깅 (광고 X, AIP only)
    - AIPEvent 에 `is_passing_by` flag 추가

### 결정 (사용자 복귀 후)
v1 PoC 는 현재 schema 정의로 충분. 빌드 추가 안 함. v2+ 도전 시점에 위 갭 fill.

### 현재 안경 연결 상태
- adb devices: 미연결
- v0.5.5 빌드 산출물 install 대기

### 6번째 loop 진척 요약
- ✅ AIP spec 정합성 분석
- ✅ `simulate_pipeline.py` MIN_AREA_RATIO 0.01 → 0.25 sync (v0.5.5 일관성)
- ✅ Unity build log 검증 (QnnYoloDetector 워닝 없음 — 깨끗 컴파일)
- ⏸️ 빌드 추가 안 함 (디스크 + APK 누적 risk vs 가치 trade-off)

### 추가 진척 후보 (사용자 복귀 후 결정)
1. **AIP passing-by 활성화** → v0.5.6 빌드 (spec 3.3 완전 정합성)
2. **`analyze_aip.py` 작성** → 안경 시연 후 누적 데이터 시각화
3. **YOLO 의 안경 시점 bottle detection 약함** → fine-tuning 또는 yolo11x 시도

---

## 7번째 loop iteration (20:55 경)

### 디스크 cleanup (3.7G → 6.8G)
**위험 신호**: 디스크 여유 3.7G 까지 떨어짐 — 다음 빌드 (237MB APK + Library 캐시 수GB) 위협.
**정리**:
- `EagleEye_Unity/Library/Bee` (3.5G) — Unity incremental build cache. 재생성 가능.
- `EagleEye_Unity/Build/` (237M) — APK 빌드 산출물. v0.5.5 백업 별도 보존됨.
**결과**: 6.8G 여유. 추가 빌드 1-2번 안전.
**Trade-off**: 다음 빌드 시 Bee cache 재생성으로 **풀 빌드 (10-20분 예상)**. 기존 incremental (~5분) 보다 느림. 사용자 복귀 시 안경 install 후 Inspector tweak + 재빌드 흐름 인지 필요.

### 안경 미연결 유지
- adb devices: 없음
- 노션 변경: 없음 (직전 iteration 의 내 update 외 외부 변경 없음)

### 보존된 APK
- `EagleEye-v0.5.5-tier3-spec.apk` (237M, 20:36) — strict spec 준수
- `EagleEye-v0.5.4-fixed-laptop.apk` (237M, 20:06) — loose fallback

### 추가 진척 없음 (이번 iteration)
- AIP passing-by 활성화 = v2+ 시 진행 (advisor 권고)
- 새 빌드 자제 = 디스크 + APK 누적 risk
- 기획안 v1.2 의 v1 PoC 범위 spec = 대부분 v0.5.5 에 반영됨
- 안경 시연 후 (사용자 복귀) 데이터 기반 결정 필요

---

## 12번째 iteration (22:09 경) — 사용자 복귀 + v0.5.6 결정

### 사용자 결정 (시연 직접)
- v0.5.5 (strict 25%) → 작게 보여도 인정 필요 → **minAreaRatio 0.05 로 완화**
- 노션 4.5 spec 의 "25%" 기준 완화 결정. PoC 시연 우선.
- 코드 변경:
    - `QnnYoloDetector.cs`: minAreaRatio 0.25 → **0.05**
    - `simulate_pipeline.py`: MIN_AREA_RATIO 0.25 → 0.05

### v0.5.6 빌드 완료 (22:10)
- `EagleEye-v0.5.6-loose-5pct.apk` (237M)
- 안경 install + 실행 완료
- 디스크: 7.7Gi → (빌드 후) 측정 필요. 충분 마진

### 현재 안경에 install 된 버전
**v0.5.6 (loose 5%)** — YOLO+CLIP 모드 (clipOnlyMode=false)
- 콜라/펩시/노트북 단독 detection 가능
- 광고 3개 매칭: 콜라→Pepsi, 펩시→Coke, 노트북→MacBook
- 비교 카드 UI + Sponsored

### APK 백업 (디스크에 보존)
- **v0.5.6-loose-5pct** (최신, 시연 중) — strict 25% 완화
- v0.5.5-tier3-spec — spec 준수 strict
- v0.5.4-fixed-laptop — laptop embedding 수정

---

## 후속 업데이트 (2026-06-07 밤 ~ 06-08) — loop 종료 후 세션들

> 2026-06-08 .md 이관 시 추가. APK 타임스탬프 + git 이력 + 코드 주석 기준 재구성.

### v0.5.7 ~ v0.5.13 (06-07 22:27 ~ 23:41)

| APK | 시각 | 변경 |
|---|---|---|
| v0.5.7-rotlock-sbs | 22:27 | 화면 회전잠금 landscape (안경 기울임 회전 방지, commit `318695f`) + SBS |
| v0.5.8-perm-fix | 22:41 | 카메라 Permission API 변경 + install 시 자동 grant (commit `2740509`) |
| (v0.5.9) | — | YOLO detection class+conf 로깅 (false positive 진단용, APK 백업 없음) |
| v0.5.10-thr055 | 23:05 | clipMatchThreshold **0.20 → 0.55** Awake hardcode (v0.5.6 분포: laptop 0.60~0.75 vs coke 오탐 0.57~0.70 — 0.55 가 차단 적정선) |
| v0.5.11-clip-only | 23:20 | **clipOnlyMode=true 강제** — YOLO 우회. 안경 시점 bottle/book class 분류 신뢰성 낮음. 매 트리거마다 frame 통째 → CLIP → best-match |
| v0.5.12-gyro-fast | 23:23 | gyro 트리거 완화 (stableThreshold 0.3→**0.5 rad/s**, duration 2.0→**1.0s**) — startup sensor calibration 노이즈로 첫 트리거 2분+ 지연 fix |
| v0.5.13-pet-refs | 23:41 | reference 임베딩 다양화 — `refs/coke`, `refs/pepsi` 각 4장 (multi-ref) |

### 06-08 — 1인칭 시야 녹화 시스템 (commit `6452d2a`, `af3b413`)
- 카메라 dump (FirstPersonRecorder.cs, adb broadcast 토글) + 디스플레이 녹화 + screen-blend 합성 → 착용자 시야 근사 영상
- `tools/recording/` — record.sh / merge.py / calib.json / README.md
- merge 파이프라인은 합성 데이터 검증 완료. 기기 쪽 코드는 다음 빌드에서 첫 실행 (README 체크리스트 참조)
- 메인 문서 결정 로그에 상세 기록

### 06-08 — 광고 영상(mp4) 빌드 단계 (commit `e99ecef`)
- build_hello_ar.sh 에 db/ads_video/*.mp4 복사 단계 추가 (v0.4.x 영상 광고 prep)

### 미커밋 진행 중 (06-08 00시 기준 working tree)
- AdRenderer/HelloAR: v0.4.1 — 매칭 시 광고 카드 PNG 대신 **mp4 영상 재생** 시도 (VideoPlayer)
- ProductMatcher: multi-ref 임베딩 지원 확장 (pepsi_bottle.npy 1개 → 4개 임베딩)
- simulate_*.py 동기화

### 문서 이관 (06-08)
- **노션 무료 기간 만료 대비 전체 export → `docs/*.md` 가 source of truth** (commit `7795330`. 이후 `docs/notion/` → `docs/` 직속으로 정리 + 영문 슬러그 + 8개 → 5개 통합)
- 이후 진행 기록은 이 디렉토리의 .md 에 한다 (README.md 정책)

---

## 🔑 핵심 인사이트 (06-08 01시 세션 종료 dump)

### 1. CLIP zero-shot 의 brand 분별 한계 — 본질적

- 시각 유사 객체 (coke vs pepsi 페트병) 의 brand 분별 매우 약함
- 환경 (배경, 책상색, 조명) 이 dominant feature → embedding 의 brand 신호보다 환경 신호가 강함
- 증거: **pepsi 시연 15 trigger 전체 pepsi 매칭 0건** (coke 7, laptop 8)
- coke top1 항상 pepsi top1 보다 ↑ (사용자 환경 = jetson_coke refs 의 빨간 책상 분포와 매칭)
- **결론**: CLIP zero-shot 만으로 fine-grained brand 매칭은 본질적으로 불가능

### 2. Reference 분포 mismatch 가 핵심

- coke refs (jetson_coke 4,5 + coke 4,12 — 빨간 책상 동일) → 사용자 시연 환경과 매칭 ↑
- pepsi refs (Wikimedia PET — 손에 든 outdoor + 매장) → 사용자 환경 mismatch → sim ↓
- 어떤 brand 의 ref 든 사용자 시연 환경과 같은 분포여야만 매칭 가능
- **권고**: 안경 카메라로 frame 캡처 → 그 환경의 ref 만들기 (v0.5.16 frame capture 기능 추가됨, 미사용)

### 3. Top-k 매칭의 trade-off

- top-3 평균 = 환경 평균 (broad coverage) → false positive 多
- top-1 = 가장 가까운 ref (environment-aligned) → noise 가능
- **brand 마다 N_refs 다르면 unfair** (laptop n=1 → top-1 자동 0.66 vs coke n=4 → top-3 평균 0.55 — laptop 항상 강함)
- **결론**: 모든 product 동일 K, refs 균등 권장. 또는 K=1 + brand-specific embedding 으로 균형.

### 4. Hierarchical 아키텍처가 본질적 해결 (v0.7.x)

```
Stage 1: CLIP → category (cola vs laptop) — coarse, 환경 영향 OK
Stage 2 fallback chain:
  ① OCR keyword 매칭 → brand 확정 (primary, deterministic)
  ② OCR fail + brand 다수 (cola) → CLIP brand-specific top-1
  ③ OCR fail + brand 1개 (laptop=macbook) → 광고 X (false positive 방지, default 안 함)
```

- CLIP 가 페트병 vs 노트북 같은 큰 카테고리 차이는 잘 잡음
- OCR 가 라벨 글자 (브랜드 결정타) deterministic 분별
- **default fallback 제거 결정 (v0.7.2)**: brand 1개 (macbook) 라도 OCR 매칭 없으면 광고 X. 다른 brand 노트북 (Dell, HP) 보고 있을 때 macbook 광고 표시되는 false positive 방지.

### 5. OCR 기술 선택

- **MLKit Text Recognition v2** (Latin/영어, Android native, ~50ms)
- Maven: `com.google.mlkit:text-recognition:16.0.1`
- `unity_assets_prep/Plugins/Android/MLKitOCR.java` (Java wrapper) + `unity_assets_prep/Scripts/OCRExtractor.cs` (C#)
- 다국어 (한글, 중국어 등) 는 별도 model 패키지

### 6. 시연 latency 실측

| Mode | median | spec 200ms |
|------|--------|------------|
| CLIP-only (v0.5.11+) | 35ms | ✅ 17% |
| YOLO+CLIP top-3 (v0.5.14) | 150ms | ✅ 75% |
| OCR 추가 (현재 sequential) | +50ms | ✅ ~100ms |

NPU 워밍 outlier 제거 (앱 시작 dummy invoke) 권장. 다음 빌드에서 OCR 병렬화 가능.

### 7. 환경 issues 해결 사례

| 증상 | 원인 | fix |
|------|------|-----|
| 앱 켜고 2분 hang | GyroTrigger sensor calibration noise | threshold 0.3→0.5, duration 2.0→1.0 |
| 카메라 권한 5분 hang | Unity `RequestUserAuthorization` 의 stereo dialog 안 보임 | `UnityEngine.Android.Permission` + `adb shell pm grant CAMERA` 자동 (build_hello_ar.sh) |
| 회전 자동회전 | screenOrientation fullSensor | manifest landscape + PlayerSettings 4개 flag |
| SBS 양안 mismatch | OnGUI 가 전체 화면 한 번만 그림 | AdRenderer.DrawAdInEye 양안 영역 각각 |

### 8. Refs 폴더 구조 (v0.7.x)

```
refs/cola/                # category embedding (coke 4 + pepsi 2 = 6장)
refs/cola_brands/coke/    # brand-specific (jetson_coke 4,5 + coke 4,12 = 4)
refs/cola_brands/pepsi/   # brand-specific (pepsi_new 1, 4 = 2장 PET)
refs/laptop/              # category + brand (laptop_ref.jpg 1장)
refs/pepsi/archive/       # 폐기 안 한 옛 refs (pepsi_new 2, 3)
```

### 9. unity_db.json schema (v0.7.1)

```json
{
  "schema_version": "v0.7.1-hierarchical-brand-fallback",
  "categories": [{
    "name": "cola",
    "embeddings_flat": [...],
    "n_refs": 6,
    "brands": [{
      "name": "coca-cola",
      "keywords": ["coca-cola", "coke"],
      "negative_keywords": ["zero", "diet"],
      "embeddings_flat": [...],   // brand-specific (옵션)
      "n_refs": 4,
      "ad_image": "...",
      "comparison": {...}
    }]
  }]
}
```

### 10. v1+ 본질 fix (시연 후 작업)

1. **Fine-tuned CLIP** (가장 근본적): coke/pepsi (또는 30 SKU 라면) contrastive learning. embedding space 가 brand 분리하도록.
2. **YOLO crop + CLIP**: bbox 영역만 CLIP → 환경 제거, 라벨 영역 dominant
3. **CLIP 모델 교체**: SigLIP-2 (fine-grained ↑), DINO v2 (self-supervised)
4. **OCR 의 라벨 영역 crop**: YOLO box → upscale → OCR. 정확도 ↑, latency ↓.
5. **다국어 OCR**: 한국 시장 한글 라벨 → `text-recognition-korean` 추가 필요

### 11. APK 버전 history (06-07 ~ 06-08)

- v0.5.5: Tier 3 area 25% (strict spec, 노션 4.5)
- v0.5.6: area 5% loose (시연 detection rate 우선)
- v0.5.7: 회전잠금 + SBS sync + 광고 1/5 축소
- v0.5.8: 카메라 권한 fix (Android API + auto grant)
- v0.5.9: YOLO conf + all-product sim 진단 로그
- v0.5.10: CLIP threshold 0.55 hardcode (Inspector override 차단)
- v0.5.11: CLIP-only mode (YOLO 우회)
- v0.5.12: gyro warmup fix (앱 시작 2분 hang 해결)
- v0.5.13: PET pepsi 4 refs (Wikimedia commons)
- v0.5.14: top-3 매칭 + AdRenderer 영상 광고 (mp4) + 모든 ref MobileCLIP-S2 재 encoding
- v0.5.15: topK=1 hardcode, pepsi refs 2장 (archive 보존)
- v0.5.16: frame capture (ref 만들기용 — 시연 후 adb pull)
- v0.6.0: MLKit OCR + conditional boost (가중치)
- **v0.7.0**: hierarchical (CLIP category + OCR brand) — 큰 refactor
- **v0.7.1**: brand fallback chain (OCR → CLIP brand → default)
- **v0.7.2** (최신): default fallback 제거 (strict)

### 12. 마지막 install 대기

```bash
adb install -r ~/Desktop/AR_project/EagleEye-v0.7.2-strict.apk
adb shell pm grant com.eagleeye.helloar android.permission.CAMERA
adb shell am start -n com.eagleeye.helloar/com.unity3d.player.UnityPlayerGameActivity
```

### 13. 미해결 / 다음 세션 작업

1. **v0.7.2 시연** — OCR 실제 동작 검증 (안경 카메라 1280x720 에서 "PEPSI"/"COCA-COLA" 라벨 글자 인식되는지)
2. **사용자 안경 frame 으로 brand refs 만들기** — v0.5.16 의 frame capture 기능 활용. `adb pull /storage/emulated/0/Android/data/com.eagleeye.helloar/files/captures/`. environment-aligned ref → false positive 본질 해결.
3. **YOLO crop + CLIP** 시도 — 환경 bias 제거
4. **Fine-tuned CLIP** 본격 시작 (v1.5+)
5. **uncommitted 변경 분할 commit** (v0.5.11 ~ v0.7.2 의 약 10개 chunk) + push

### 14. 디스크 위험

- macOS root data volume: 200/228GB 사용, 1~6GB 만 여유
- Unity 빌드 cache: 4-5GB. 매 빌드 사이 정리 권장
  ```bash
  rm -rf EagleEye_Unity/Library/Bee EagleEye_Unity/Build
  ```
- APK 백업 keep: v0.7.0, v0.7.1, v0.7.2 (245MB 각각). 옛 v0.5.x 폐기 가능.

### 15. YOLO+CLIP 모드 복원 방법 (코드 아카이빙 위치)

**별도 아카이빙 폴더/브랜치 없음.** YOLO+CLIP 코드는 main tree 에 그대로 keep, mode toggle 만 hardcode.

**YOLO+CLIP 활성화**:
```csharp
// unity_assets_prep/Scripts/HelloAR.cs Awake() 안
clipOnlyMode = true;   // ← false 로 변경 또는 이 줄 주석 처리
```
public field 라 Unity Inspector 에서도 toggle 가능 (다만 hardcode 가 override).

**관련 파일 (전부 keep 됨)**:
- `unity_assets_prep/Scripts/QnnYoloDetector.cs` — YOLO11l NPU 추론
- `unity_assets_prep/Scripts/HelloAR.cs` line 47 의 `public bool clipOnlyMode`
- `unity_assets_prep/Plugins/Android/QnnYoloEngine.java` — YOLO Java native
- `yolo11l_640_w8a8.tflite` (model file)

**YOLO+CLIP 동작하는 마지막 빌드**:
- v0.5.10 (CLIP threshold 0.55 hardcode) — APK 백업은 cleanup 으로 삭제됨 (디스크 부족)
- 복원 시: HelloAR.cs 의 `clipOnlyMode = true` 제거 → rebuild

**git 에 commit 된 YOLO+CLIP 마지막 commit**:
- `2740509 fix(camera): Permission API 변경 + install 시 자동 grant` (v0.5.8 시점, clipOnlyMode default = false)
- 즉 `git checkout 2740509 -- unity_assets_prep/Scripts/HelloAR.cs` 로 그 시점 HelloAR 복원 가능 (YOLO+CLIP 모드 default).
- 단 그 시점은 v0.5.14 의 top-k, v0.7.x 의 hierarchical 매칭 없음.

**현재 HelloAR.cs 흐름** (v0.7.2, CLIP-only):
```
Stage 1 (YOLO): clipOnlyMode=true 면 skip
Stage 2 (CLIP): 모든 trigger 마다 호출
Stage 3a (OCR): MLKit text 추출
Stage 3b (Matcher): CLIP category + OCR brand
Stage 4 (AdRenderer): brand 의 mp4 영상 + 비교 카드
Stage 5 (AIP): 누적 로그
```

YOLO 다시 켜면 Stage 1 활성, det > 0 일 때만 Stage 2 진행 (배터리 절약).

### 16. Git 상태

이번 세션 commit 6개 push (v0.5.5~v0.5.10):
- chore: YOLO 320→640
- feat(ads): 비교 카드 + Sponsored + AIP
- fix(yolo): minAreaRatio loose
- fix(ui): 회전잠금
- fix(camera): Permission API + 자동 grant
- tool: Mac 시뮬레이터

v0.5.11~v0.7.2 변경사항 모두 **uncommitted**. 다음 세션 commit 분할 + push 필요.

---

## 2026-06-08 (오후 세션) — v0.7.3 / v0.7.4 + 안경 시연 검증

> v0.5.11~v0.7.2 미커밋분은 이 세션 초반에 4개 chunk(docs / db / unity / sim)로 분할 커밋함.
> 이어서 안경 실기기 시연하며 v0.7.3·v0.7.4 를 만들고, OCR·CLIP·조준 문제를 차례로 진단.

### v0.7.2 첫 실기기 시연 (13 트리거)
- end-to-end 동작 확인. **OCR brand 경로 best-case 검증**: 펩시를 가까이·라벨 정면으로 들었을 때 MLKit 이 "pepsi" 읽어 deterministic 하게 정확 매칭(→ Coke conquest 광고). strict 정상(진짜 노트북 → 광고 X).
- 한계: OCR 13개 중 1개만 성공. 멀거나 배경 글자(세탁기 "TROMM") 오독. CLIP brand fallback 은 환경 bias 로 항상 coca-cola 를 찍어 펩시를 코크로 오판(#13).

### v0.7.3 — OCR 전처리 + 구조 명시 + CLIP-only 시작 최적화
- **OCR 전처리**: `videoRotationAngle`(=90 자동) 회전 보정 + 중앙 crop(0.55) + 업스케일(2x) → MLKit 입력. 배경 글자 제거·라벨 보강. OCR 입력 이미지를 `ocr_crops/` 에 저장(디버그). 파일: `OCRExtractor.cs`, `MLKitOCR.java`(rotationDegrees 인자).
- **매칭 구조 분리**: `ProductMatcher.MatchCategory()` → (category 매칭된 것만) OCR → `ResolveBrand()`. "CLIP 이 먼저 무슨 물체인지 → 그 다음 OCR 로 brand" 흐름을 코드로 명시 + category 미매칭이면 OCR skip(낭비 제거).
- **brand = OCR 전용**: `enableClipBrandFallback=false` 기본. CLIP brand fallback(환경 bias) 차단.
- **CLIP-only 면 YOLO 컴포넌트 skip**: 앱 시작 시 QNN 의 yolo11l 그래프 컴파일(10000+ 노드, 수십 초)을 통째로 제거 → startup 수초로 단축.

### v0.7.4 — CLIP 중앙 crop (query + ref) + threshold 0.45
- **환경 의존 문제**: full-frame CLIP 은 배경(주방/침대/책상)이 dominant → 환경 바뀌면 cola 가 laptop 에 짐. environment-aligned ref(`refs/cola/dev_*`, `refs/laptop/dev_*`) 추가해도 그 환경에서만 유효(주방 ref ↔ 침대 시연 mismatch).
- **해결**: `ClipExtractor` 가 중앙 crop(0.5)만 embedding + `build_adversarial_db.py` 도 동일 crop 으로 ref 재인코딩 → query↔ref 둘 다 중앙 물체 집중, 배경 제거(환경 무관). `CLIP_CROP`·`cropFraction` 반드시 일치.
- **ref 로딩 glob 화**: `refs/<category>/*` 폴더 통째 → 사진 드롭만으로 ref 확장.
- **threshold 0.55 → 0.45**: 아래 "온디바이스 격차" 흡수.

### 🔑 시연으로 확인된 핵심 사실
1. **온디바이스 CLIP sim 이 Mac 대비 ~0.3 낮다** — 같은 프레임(frame_0011): Mac 오프라인 cola 0.85 vs 안경 0.55. 전처리(`ClipExtractor` 의 RenderTexture Blit/ReadPixels vs PIL — Y-flip 가능성)/NPU 양자화 차이 추정. **오프라인 ref 검증 숫자가 실기기를 예측 못 함** → threshold 0.45 + 중앙 crop 으로 우회. 근본 규명 미완(다음 작업).
2. **category 변별력 약함(온디바이스)** — cola·laptop 둘 다 0.5~0.6 좁은 구간. 병 없는 책상/벽 장면도 cola 오탐(0.58). 단 OCR 게이트가 막아 잘못된 광고는 안 뜸(strict 안전).
3. **안경 카메라 FOV ≠ 사용자 시선** — 가장 큰 실전 함정. 콜라를 "눈높이 정면"에 들면 카메라(아래·팔 방향을 봄) 시야 밖이라 프레임에 병이 아예 안 잡힘. `FirstPersonRecorder` 로 연속 캡처해 캘리브레이션 → **병을 팔 뻗어 몸 앞 아래쪽(노트북/무릎 높이)에 둬야** 잡힘(calib s2·s3 에 펩시·코크 선명히 포착). 조준만 맞으면 category=cola + OCR 라벨 읽기 검증 가능.

### ✅ v0.7.4 end-to-end 성공 (AIP 로그로 확인)
- **brand fallback OFF 상태에서 `aip.json` 에 `product_name:"pepsi"`(sim 0.586/0.587) + `"coca-cola"`(0.66~0.68) 매칭 누적** → fallback 이 꺼져 있으므로 brand 는 **오직 OCR keyword 로만** 확정됨 = **OCR 이 "pepsi"/"coca-cola" 라벨을 실제로 읽었다는 증거**.
- 즉 **CLIP category=cola → OCR brand → conquest 광고 표시** 파이프라인이 안경에서 실제 동작(광고 영상 1회 이상 표시 확인). v0.7.x 의 핵심 가설(CLIP=무엇, OCR=브랜드)이 실기기에서 성립.
- 단 성공률은 조준(병이 중앙 + 라벨 정면)에 크게 의존. logcat 버퍼는 롤오버되므로 **검증은 `aip.json` (product_name) + `ocr_crops/` 로 해야 정확**(logcat tail 만 보면 성공 트리거를 놓칠 수 있음 — 이 세션의 교훈).

### 산출물 / 자산
- APK: `EagleEye-v0.7.3-clip-cat-ocr-brand.apk`, `EagleEye-v0.7.4-clip-centercrop-thr045.apk` (v0.7.2 도 보존). 옛 v0.5.10·v0.7.0·v0.7.1 + `yolo11l.onnx` 는 디스크 확보로 삭제(.pt 에서 재생성 가능).
- 안경 1인칭 캡처 `captures/device_20260608/` (ref 제작·디버그용).
- 재인코딩 환경: `~/.gradle/caches`(17GB) 정리로 빌드 디스크 확보. Mac base TF 가 protobuf 충돌로 깨져 **`/tmp/ee_venv` 격리 venv**(tf 2.21)에서 `build_adversarial_db.py` 실행.

### 다음 작업
1. **조준 맞춰 재시연** — s3 위치(팔 뻗어 아래)로 콜라/펩시 → category=cola + OCR brand(coke/pepsi) 읽히는지 최종 확인.
2. **온디바이스↔Mac 0.3 격차 근본 규명** — `ClipExtractor.PreprocessTexture` 의 Y-flip/정규화/리사이즈가 `build_adversarial_db.py` 와 정확히 일치하는지. 일치시키면 threshold 우회 없이 정상화 가능.
3. **카메라-시선 오프셋** — 디스플레이에 카메라 프리뷰/조준 가이드 표시(현재 프리뷰 안 보임) 또는 FOV 보정.
4. fine-tuned CLIP (v1.5+), 한글 OCR.
