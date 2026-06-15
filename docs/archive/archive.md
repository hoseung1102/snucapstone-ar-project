<!-- 아카이브: 이미 superseded 된 옛 문서들. 역사적 맥락 보존용. 현재 사양 아님. -->

> 🗄️ **보관 문서(archived)** — 작성 시점 스냅샷. 현황 아님 → 현재 상태는 [docs/STATUS.md](../STATUS.md). 🔴 (RayNeo X2 / XR2 Gen1 / confidence 0.7 / Snapdragon Spaces 등 전부 superseded)

# 📦 아카이브 (옛 문서 — 역사적 맥락용)

> ⚠️ **이 문서의 내용은 모두 superseded 되었다.** 현재 사양이 아니라 프로젝트 초기 결정의 변천사를 보여주는 기록이다.
>
> - 클라이언트 기술 스택의 현재 진실의 원천 → [client-spec.md](../client-spec.md)
> - 시스템 비전 / v1 사양 / 결정 로그 → [vision.md](../vision.md)
>
> 아래 내용을 현재 사양으로 인용하지 말 것. (예: RayNeo X2 / XR2 Gen 1 / confidence 0.7 / Snapdragon Spaces 등은 전부 바뀜)

원래 노션 페이지 트리: `Team BETA → Tech Tree → 기획안(v1)`. 노션 무료 기간 만료로 2026-06-08 export, 이후 이 두 문서는 갱신하지 않고 아카이브로 보존.

---

# 1. Tech Tree (초기 디바이스 후보 / 인사이트)

> 원본: 노션 `Tech Tree` 페이지. **대부분 outdated** — 최종 기기는 RayNeo X3 Pro (Snapdragon AR1 Gen 1) 로 확정됨 ([client-spec.md](../client-spec.md) §1 참조).

## 공유 정보

### 1. AR 디바이스 (초기 검토)
- 기존 후보: XREAL Air 2 Ultra, VITURE Luma Ultra → Standalone 아님이라 변경 필요
- Standalone AR 디바이스를 사용해야 함 (PC/스마트폰 연결 없이 on-device 연산으로 처리)
    - Low latency & 착용 편의성 높음
    - Standalone AR 이 2026년 key trend 가 될 것이라는 전제
    - 참고 글 (2026.02.09): https://inairspace.com/blogs/learn-with-inair/ar-glasses-as-a-standalone-device-the-next-computing-revolution
- 초기 신규 후보: **RayNeo X2** ($499, Snapdragon XR2, 128GB SSD / 6GB RAM)
    - → 이후 Sold Out, 최종적으로 RayNeo **X3 Pro** (AR1 Gen 1) 로 확정

## 기술적 인사이트 (현재도 유효한 부분)

### YOLO 상시 추론은 전력상 불가 → 시선 고정 시에만 추론
- YOLO 작은 모델도 연산량이 많아 AR 기기에서 상시 구동 시 ~1시간 30분이면 배터리 방전
- → **가속도/자이로 센서로 시야가 2초 이상 고정된 경우에만 YOLO 실행** (이 IMU 트리거 전략은 현재 사양에도 계승됨, [client-spec.md](../client-spec.md) §3 Step 1)

---

# 2. 기획안 v1 (옛 버전 — v1.2 가 대체)

> 원본: 노션 `기획안` 페이지. **[client-spec.md](../client-spec.md) (= 기획안 v1.2) 가 완전히 대체함.** 아래는 v1 → v1.2 변경 전 스냅샷.

## [Internal Tech Spec] On-Device AR Recommendation System — Project "Eagle Eye"

### 1. 하드웨어 스택 (옛)
- **Target Device:** RayNeo X2 (Standalone AR Glasses) → 특이사항: Sold Out
- **Core SoC:** Snapdragon XR2 Gen 1 (Adreno GPU / Hexagon NPU / Dedicated DSP)
- **Constraints:** 6GB RAM / 590mAh Battery (열효율 및 전력 최적화가 제1원칙)

> 변경: 최종 기기 RayNeo X3 Pro / Snapdragon AR1 Gen 1 / 4GB RAM / 245mAh.

### 2. 기술 파이프라인 (4-Step, 옛 수치)
- **Step 1 — IMU 시선 트리거:** IMU 100Hz+ 모니터링, 델타 2.0s 이상 임계값 이하 유지 시 인식 모드 진입. Zero-GPU & Low-NPU 전략.
- **Step 2 — YOLOv11n 검출:** 320×320(or 640) 리사이즈 NPU 추론. Filter: `Confidence Score > 0.7` AND `Class_ID in [Commodity_List]`.
  > 변경: v1.2 에서 confidence 임계값 **0.7 → 0.45** 로 하향 (false negative 방지).
- **Step 3 — MobileCLIP 정밀 식별:** YOLO BB Crop → Letterboxing → MobileCLIP Image Encoder. 텍스트 인코더는 미실행, 사전 임베딩 Vector DB 와 코사인 유사도만 비교.
- **Step 4 — SLAM 시공간 정합:** T=0 Pose 저장 → 0.2초 뒤 Spatial Anchor 생성 → 현재 포즈와 동기화하여 3D 배치. 10초 뒤 자동 소멸.
  > 변경: v1 PoC 는 Spatial Anchor 대신 **2D HUD** 채택 ([vision.md](../vision.md) 2D HUD 결정 로그).

### 3. 엔지니어링 선택 이유 (옛)
1. **YOLO + CLIP 하이브리드:** YOLO 단독은 '의미'를, CLIP 단독은 '위치'를 모름. 조합이 모바일 NPU 에서 최적.
2. **Snapdragon Spaces(SDK) 사용:** 직접 SLAM 구현 불가, 공식 SDK 의 Spatial Anchor / Asynchronous Timewarp 로 0.2초 레이턴시 은폐.
   > 변경: 실제로는 Unity 표준 Android 빌드 + TFLite + QNN delegate 경로로 진행 (Snapdragon Spaces 미사용).
3. **10초 노출 후 삭제:** 장기 SLAM 정합(Relocalization) 연산량 폭증 + 배터리 절약.
