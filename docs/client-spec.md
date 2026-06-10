<!-- source of truth. 노션에서 export 후 폐기 (2026-06-08). 갱신은 이 파일에 직접 한다. -->
<!-- 클라이언트(RayNeo X3 Pro) 기술 스택 / 파이프라인의 진실의 원천. 비즈니스/v1 사양은 vision.md. 읽는 법은 루트 CLAUDE.md 참조. -->
<!-- 구 파일명: planning-v1.2.md (= 기획안 v1.2). 옛 기획안 v1 / Tech Tree 는 archive.md. -->

# 기획안 v1.2

# [Internal Tech Spec] On-Device AR Recommendation System

## Project: "Eagle Eye" (Lightweight Context-Aware AR)

### Version: 1.2 | Status: Pre-Prototype / Hardware Pending

---

## ⚠️ 현재 미결 사항 (구매 전 반드시 확인)

| 항목 | 상태 | 액션 |
|---|---|---|
| RayNeo X3 Pro Spatial Anchor API 지원 여부 | ❓ 미검증 | RayNeo 이메일 답변 대기 중 |
| MobileCLIP SNPE 변환 가능 여부 | ❓ 미검증 | Qualcomm AI Hub 테스트 필요 |
| 200ms 목표 레이턴시 실측값 | ❓ 미검증 | 기기 확보 후 벤치마크 필요 |

> **갱신 (2026-06-08)**: 위 3개 항목 모두 해소됨 — ① Spatial Anchor 부분 지원 확인 (세션 내 6DoF ✅ / persistence ❌, v1 은 2D HUD 채택으로 의존성 제거) ② MobileCLIP-S2 INT8 TFLite 변환 완료 (Hexagon v73, 2.12ms — SNPE 가 아니라 TFLite+QNN delegate 경로) ③ 추론 레이턴시 실측 — YOLO11l ~15-30ms + CLIP ~3ms (200ms 목표 대비 큰 여유). 상세는 [vision.md](vision.md) 결정 로그 참조.

---

## 1. 하드웨어 스택 (Hardware Stack)

**1차 후보 기기: RayNeo X3 Pro**

| 항목 | 사양 |
|---|---|
| Core SoC | Qualcomm Snapdragon AR1 Gen 1 |
| RAM / Storage | 4GB / 32GB |
| 디스플레이 | 양안 MicroLED 웨이브가이드, 640×480, 6,000 nits |
| 무게 | 76g |
| 배터리 | 245mAh |
| 개발 SDK | Unity ARDK / Android ARDK (공식 권장) |
| OS | RayNeo AIOS (AOSP 기반 커스텀 Android) |

**하드웨어 선택 근거 및 리스크**

RayNeo X3 Pro는 내장 SLAM 기반 6DoF 트래킹을 지원하는 글라스 폼팩터 기기다. 핵심 의존성인 Spatial Anchor API의 개발자 공개 여부가 현재 미검증 상태이며, RayNeo 측에 공식 문의를 발송했다. 답변에 따라 다음과 같이 분기한다.

- **Spatial Anchor API 지원 확인 시**: RayNeo X3 Pro 확정 구매
- **미지원 확인 시**: XREAL Air 2 Ultra (Spatial Anchor 검증 완료, 단 생산 종료로 재고 소진 중) 또는 2026년 출시 예정인 XREAL Project Aura (Android XR 기반)로 전환

---

## 2. 파이프라인 설계 철학

Eagle Eye는 **트리거 기반 단발성 추론** 구조다. 이것이 전체 설계의 근간이다.

```plain text
[대기 상태]  →  IMU 감시만 실행  →  배터리 소모 극히 낮음
      ↓
[트리거 발동]  →  YOLO + CLIP 1회 실행 (약 200ms)  →  종료
      ↓
[Anchor 표시]  →  렌더링 유지  →  배터리 소모 발생
      ↓
[Anchor 소멸]  →  렌더링 중단  →  대기 상태로 복귀
```

YOLO와 CLIP은 **사용자의 시선이 특정 상품에 멈출 때에만 드물게 한 번 호출**된다. 매 프레임 추론이 아니므로 AI 추론 자체는 배터리의 주 소비원이 아니다. 이 구조 덕분에 파이프라인은 단순한 동기(직렬) 실행으로 충분하다.

---

## 3. 기술 파이프라인 (The 4-Step Pipeline)

### Step 1. IMU 기반 '시선 트리거' (Pre-Processing)

**작동 방식**

IMU 센서(100Hz+)를 모니터링하다가 다음 두 조건을 동시에 만족할 때 '인식 모드'에 진입한다.

- **조건 A**: 헤드 모션 델타 값이 2.0초 이상 임계값 이하로 유지 (고개 안정)
- **조건 B**: 직전 경량 YOLO 스캔 결과, 화면 중앙 영역(전체 프레임의 중앙 40%) 내에 Commodity_List 포함 객체 존재

두 조건 모두 만족해야 다음 단계로 진입한다. 조건 A만 충족하는 경우(허공 응시, 대화 상대 얼굴 응시 등)는 트리거하지 않는다.

**설계 근거**

IMU 폴링 자체의 전력 소모는 극히 낮다. 조건 B를 AND 조건으로 추가함으로써 false positive를 줄이고 불필요한 카메라 활성화를 방지한다. 카메라 활성화 자체가 상대적으로 비싼 연산이므로, 트리거 진입 조건을 엄격하게 설정하는 것이 배터리 관리에 효과적이다.

---

### Step 2. YOLOv11n 기반 '객체 검출 & 필터링' (Detection)

**작동 방식**

카메라 프레임을 320×320으로 리사이징하여 NPU(SNPE)에서 YOLO 추론을 실행한다.

필터 로직:
- `Confidence Score > 0.45` AND `Class_ID in [Commodity_List]`

조건 미충족 시 즉시 대기 상태로 복귀. 카메라를 다시 끄고 IMU 감시로 돌아간다.

**목표 레이턴시**: 10~20ms (NPU INT8 기준 예상치. 기기 확보 후 실측 필요)

**Confidence 임계값 근거**

YOLOv11n nano 모델은 320×320 입력과 실제 환경(조명 변화, 부분 가림, 비스듬한 각도)에서 0.5~0.65 범위를 자주 출력한다. 0.7 이상을 요구하면 실제 상품 앞에서도 광고가 나오지 않는 false negative가 빈번해진다. 0.45에서 시작하여 실측 데이터를 기반으로 상향 조정한다.

**모델 변환 계획**

YOLOv11n → ONNX → DLC(Qualcomm Deep Learning Container) → SNPE INT8 양자화. 변환 후 레이어별 NPU/CPU 배치 확인 필수.

---

### Step 3. MobileCLIP 기반 '정밀 식별' (Identification)

**작동 방식**

YOLO가 제공한 Bounding Box를 Crop → Letterboxing(비율 유지) → MobileCLIP Image Encoder에 투입한다.

텍스트 인코더는 기기에서 실행하지 않는다. 사전 임베딩된 상품 벡터(Vector DB)와 기기 내부에서 코사인 유사도만 비교한다.

**목표 레이턴시**: 50~150ms (NPU INT8 기준 목표치)

> **⚠️ 중요**: MobileCLIP의 공식 벤치마크는 Apple Neural Engine(Core ML) 기준이다. Snapdragon AR1 Gen 1의 SNPE에서 ViT 연산이 NPU로 완전히 배치되는지 현재 미검증이다. 일부 레이어가 CPU로 fallback될 경우 레이턴시가 300ms 이상으로 증가할 수 있다. 기기 확보 후 Qualcomm AI Hub를 통한 SNPE 변환 가능 여부 확인이 필수 선행 조건이다.
> **200ms 달성 실패 시 대안**: EfficientNet 기반 커스텀 분류기 교체, 또는 YOLO 클래스 기반으로 Vector DB 후보를 사전 필터링하여 코사인 유사도 비교 대상을 축소.

**Vector DB 운영 전략**

- 상품 벡터 DB는 앱 설치 시 동봉. OTA 업데이트로 주기적 갱신
- B2B 파트너사(브랜드/리테일)가 상품 이미지를 제공하면 오프라인에서 임베딩 생성 후 배포
- 벡터 규모별 용량: 1만 상품 × 512차원 float32 ≈ 20MB, 10만 상품 ≈ 200MB
- 검색 최적화: FAISS 또는 ScaNN을 사용하여 대규모 DB에서도 코사인 유사도 검색 속도 유지

---

### Step 4. SLAM 기반 '시공간 정합' (Anchoring & Rendering)

**작동 방식**

1. Step 1 트리거 발생 시점(T=0)의 6DoF Pose를 저장한다.
2. Steps 1~3이 실행되는 동안 화면에는 경량 '인식 중' 로딩 인디케이터 Anchor를 T=0 위치에 즉시 생성하여 표시한다.
3. 추론 완료 후 로딩 Anchor를 광고 UI로 교체한다. T=0 Pose와 현재 Pose의 차이를 Anchor 좌표계로 보정하여 3D 공간의 올바른 위치에 배치한다.
4. Anchor 생명주기는 아래 정책에 따른다.

**레이턴시 은폐 전략 (200ms 공백 처리)**

AI 추론 200ms 동안의 공백을 사용자가 느끼지 못하게 하는 방법은 T=0에 Anchor를 즉시 생성하는 것이다. 결과가 나오면 해당 좌표에 올려놓기 때문에 사용자 입장에서는 객체 옆에 자연스럽게 나타나는 것처럼 보인다. 이 전략은 추론 완료까지 대기하지 않고 즉시 피드백을 제공한다는 점에서 UX상 중요하다.

**SDK 의존성**

본 Step은 Spatial Anchor API의 공식 지원에 전적으로 의존한다. 다음 기능이 반드시 지원되어야 한다.

- T=0 시점 Pose 저장
- 저장된 Pose 기반 Spatial Anchor 생성
- Anchor의 지속적 world-locking (사용자가 이동해도 Anchor가 현실 공간의 고정 좌표에 유지)

---

### Anchor Lifecycle 정책

```plain text
[Anchor 생성] → [fade-in 진입 애니메이션, 0.5초]
        ↓
  [Active 상태]
        ↓
  [시선이 Anchor 영역에 2초 이상 머무름?]
        ├── Yes → [Extended 상태: 추가 10초 유지, 상세 정보 확장]
        │                 ↓
        │          [fade-out 소멸 → 렌더링 중단]
        │
        └── No  → [5초 후 fade-out 소멸 → 렌더링 중단]
```

Anchor가 소멸하면 GPU 렌더링을 즉시 중단하고 대기 상태로 복귀한다.

---

## 4. 배터리 소모 분석 (재검토)

이전 버전에서 AI 추론 빈도를 주요 배터리 리스크로 오진단했다. Eagle Eye는 트리거 기반 단발성 구조이므로 YOLO와 CLIP은 배터리의 주 소비원이 아니다. 실제 소비 구조는 다음과 같다.

**지속 소모 (항상 켜짐)**

| 항목 | 전력 수준 | 비고 |
|---|---|---|
| 내장 SLAM 엔진 | 중간~높음 | 6DoF 트래킹 위해 상시 구동, 개발자가 끌 수 없음 |
| IMU 폴링 (100Hz) | 극히 낮음 | 배터리 영향 미미 |
| 기본 시스템 | 낮음 | OS, BT, WiFi 등 |

**단발 소모 (트리거 발동 시에만)**

| 항목 | 전력 수준 | 비고 |
|---|---|---|
| 카메라 활성화 | 높음 | 트리거당 약 200ms만 켜짐 |
| YOLO + CLIP 추론 | 높음 | 트리거당 1회, 약 200ms |

**간헐 소모 (Anchor 표시 중)**

| 항목 | 전력 수준 | 비고 |
|---|---|---|
| AR 렌더링 | 중간 | Anchor 표시 5~15초 동안만 |
| 디스플레이 밝기 | 중간~높음 | 실외 환경에서 높은 nits 필요 |

**결론**

Eagle Eye의 배터리 소모는 AI 추론 빈도가 아닌 **SLAM 상시 구동**이 지배한다. 번화가 쇼핑몰에서 1시간 사용 시나리오에서 SLAM만으로도 상당한 배터리를 소모한다. AI 추론은 전체 사용 시간 대비 극히 짧은 순간(트리거당 약 200ms)만 발생하므로 배터리 관점에서 부차적인 요소다.

**배터리 리스크 대응 전략**

- SLAM 전력 소모는 SDK 내부에서 관리되므로 직접 제어 불가. 다만 Anchor 표시 빈도와 시간을 줄이면 간접적으로 절약 가능
- Anchor 최대 노출 시간을 Extended 포함 15초로 제한
- 연속 사용 목표: 30분 이상. 기기 확보 후 실측 후 USB-C 동시 충전 지원 여부로 보완 가능 여부 확인

---

## 5. 엔지니어링 선택 근거 (Rationale)

**왜 YOLO + CLIP 하이브리드인가?**

YOLO 단독은 객체 위치와 카테고리를 알지만 세밀한 상품 식별이 불가능하다. CLIP 단독은 이미지-텍스트 유사도를 알지만 객체 위치를 모른다. YOLO(위치/사전 필터) + CLIP(정밀 식별)의 조합이 모바일 NPU 환경에서 속도와 정확도의 최적 균형을 제공한다. 트리거 기반 단발 추론 구조이므로 연산 부담은 허용 가능한 범위다.

**왜 동기(직렬) 파이프라인인가?**

Eagle Eye는 시선이 멈출 때만 드물게 트리거되는 구조다. 트리거 발동 시 메인 스레드가 처리해야 할 다른 작업이 없으므로 비동기 분리의 실익이 없다. 동기 직렬 실행이 코드 복잡도와 디버깅 비용을 줄이는 더 나은 선택이다.

**왜 텍스트 임베딩을 온디바이스에 캐싱하는가?**

텍스트 인코더를 매 추론마다 돌리면 레이턴시와 전력 소모가 크게 증가한다. 상품 텍스트 임베딩은 상품 DB가 변경될 때만 갱신되면 되므로 사전 계산하여 Vector DB로 저장하는 것이 합리적이다. 온디바이스 코사인 유사도 비교만 수행하면 되므로 네트워크 의존성도 제거된다.

---

## 6. 프라이버시 및 법적 검토

본 시스템은 카메라로 주변 환경을 스캔하여 상업적 추천을 제공한다.

**설계상 프라이버시 보호 요소**

- 모든 AI 추론은 온디바이스에서 수행. 카메라 프레임 및 추론 결과가 외부 서버로 전송되지 않음
- 카메라는 트리거 발동 시에만 활성화되며 상시 스트리밍하지 않음

**검토 필요 항목**

- 한국 개인정보보호법: 타인의 소지품을 촬영·분석하는 행위의 법적 해석
- 광고 노출 이벤트 로그의 서버 전송 여부 및 범위
- EU 진출 시 GDPR 동의 요건

출시 전 법률 전문가 검토 및 서비스 약관에 카메라 사용 목적 명시 필수.

---

## 7. 기술 검증 로드맵

**Phase 0 — 구매 전 (현재 진행 중)**
- [ ] RayNeo Spatial Anchor API 지원 여부 — 이메일 답변 대기
- [ ] Qualcomm 개발자 페이지 직접 확인
- [ ] MIT Reality Hack 2026 팀 GitHub에서 SDK 사용 코드 확인

**Phase 1 — 기기 확보 후 즉시 (1~2주)**
- [ ] Unity ARDK에서 Spatial Anchor 생성 및 world-locking 동작 확인
- [ ] YOLOv11n SNPE 변환 및 NPU 배치 확인, 레이턴시 실측
- [ ] MobileCLIP SNPE 변환 가능 여부 확인
- [ ] SLAM 상시 구동 시 배터리 소모량 실측

**Phase 2 — 파이프라인 통합 (3~6주)**
- [ ] 전체 4-Step 파이프라인 end-to-end 레이턴시 실측
- [ ] 200ms 목표 달성 여부에 따라 모델 교체 또는 최적화 진행
- [ ] Confidence 임계값 0.45 기준 false positive/negative 실측 및 조정

**Phase 3 — 사용성 검증 (2~3개월)**
- [ ] 실제 상점 환경에서 Anchor 안정성 테스트
- [ ] 연속 사용 30분 이상 배터리 목표 달성 여부 확인
- [ ] Anchor Lifecycle UX 사용자 테스트

---

## 8. 열린 리스크

| 리스크 | 확률 | 영향 | 대응 |
|---|---|---|---|
| RayNeo Spatial Anchor API 미지원 | 중간 | 치명적 | XREAL Air 2 Ultra로 전환 |
| MobileCLIP SNPE 변환 불가 | 중간 | 높음 | EfficientNet 기반 대체 모델로 교체 |
| 200ms 목표 미달성 | 중간 | 중간 | Vector DB 후보 사전 필터링으로 CLIP 연산량 축소 |
| SLAM 상시 구동으로 배터리 30분 미만 | 높음 | 높음 | USB-C 동시 충전 지원 확인 후 보완 |
| AR1 Gen 1 발열로 인한 NPU 성능 저하 | 중간 | 중간 | 트리거 간 최소 쿨다운 인터벌(예: 30초) 설정 |
