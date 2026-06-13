# Eagle Eye — AR Contextual Information Layer (PoC)

AR 글라스에서 사용자가 보는 객체를 인식하고 관련 정보를 화면에 띄우는 시스템의 PoC.

> Status: **v1 PoC — 온디바이스 end-to-end 동작 확인** (6DoF SLAM 공간고정 + CLIP/OCR 매칭 + world-anchored 광고). 현황 스냅샷은 [`docs/vision.md`](docs/vision.md) 맨 위, 실험 history 는 [`docs/EXPERIMENTS.md`](docs/EXPERIMENTS.md).
> 타겟 하드웨어: **RayNeo X3 Pro** (Snapdragon AR1 Gen 1, Hexagon v73 NPU)
> 표시 방식: **3D world-anchored 광고** + 2D HUD 병행
> 자세한 시스템 비전은 [`docs/`](docs/) 참고

---

## 파이프라인

```
[안경 카메라] → [IMU 안정 트리거] → [YOLO 객체 검출]
              → [MobileCLIP 정밀 식별] → [HUD 광고 표시]
```

맥북 프로토타입 단계에선 IMU를 Optical Flow로, Spatial Anchor를 2D bbox 좌표 고정으로 치환.

---

## 디렉토리 구조

> **안경 앱 본체는 [`spatial_anchor_test/`](spatial_anchor_test/)** (자기완결 Unity 프로젝트). 루트의 `*.py` 와 `db/`·`products/` 는 **Mac 측 오프라인 도구/데이터**다. 전체 맥락·history 는 [`docs/`](docs/) 참조 ([CLAUDE.md](CLAUDE.md) 의 "문서 읽는 법").

```
.
├── spatial_anchor_test/          # ★ 안경 앱 (Unity). Assets/Scripts(C#) · Assets/Plugins/Android(Java NPU)
│                                 #   · Assets/StreamingAssets(모델+db) · *.ps1(Windows 빌드) · tools/monitor
├── docs/                         # source of truth. vision / EXPERIMENTS(실험 history) / client-spec / dev-guide / progress-log
│
├── yolo.py                       # (Mac) PoC 프로토타입 파이프라인
├── eagle_eye_step2.py            # (Mac) YOLO 검출 단독 실행
├── create_embeddings.py          # (Mac) 상품 이미지 → 임베딩 DB 생성 (오프라인)
├── eagle_eye_ad_db.py            # (Mac) 광고 DB CRUD 헬퍼
├── create_ad.py                  # (Mac) 광고 카드 이미지 생성 헬퍼
├── diag_stability.py             # (Mac) Optical Flow 안정 감지 디버깅
├── export_mobileclip_s2.py       # (Mac) PyTorch → ONNX export (MobileCLIP-S2)
├── verify_onnx.py                # (Mac) PyTorch ↔ ONNX 출력 동등성 검증
├── visualize_onnx.py             # (Mac) ONNX 그래프 텍스트 요약
├── clean_onnx_for_qnn.py         # (Mac) ONNX export 결과를 QNN 컴파일러용으로 정리
├── qai_hub_compile.py            # (Mac) Qualcomm AI Hub로 ONNX → QNN 컴파일 (단발)
├── qai_hub_pipeline.py           # (Mac) 컴파일 + profile + 다운로드 (백그라운드 러너)
│
├── db/
│   ├── metadata.json             # 상품 메타데이터 (id, name, embedding 경로, 광고 이미지)
│   ├── embeddings/*.npy          # CLIP 임베딩 (오프라인 사전 계산)
│   └── ads/                      # 광고 이미지 리소스
├── products/                     # 상품 레퍼런스 이미지
├── output/                       # 출력 비디오 + 통계 (gitignored)
└── qai_hub_results.json          # Step B QNN 컴파일 + profile 결과
```

---

## 모델 파일 다운로드 (별도)

대용량 모델은 git에 포함되어 있지 않습니다. 아래 순서대로 받으세요.

### 1. YOLO11n (5.4MB)

가장 쉬운 방법은 `ultralytics`가 자동 다운로드하게 두는 것 — `yolo.py` 첫 실행 시 자동으로 받아옴.

수동 다운로드를 원하면:

```bash
curl -L -o yolo11n.pt https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo11n.pt
```

저장 위치: 프로젝트 루트 (`./yolo11n.pt`)

### 2. Apple ml-mobileclip 레포 (코드)

MobileCLIP 모델 정의가 들어있는 Apple 공식 레포를 clone:

```bash
git clone https://github.com/apple/ml-mobileclip.git
cd ml-mobileclip
pip install -e .
cd ..
```

저장 위치: `./ml-mobileclip/`

### 3. MobileCLIP-S2 체크포인트 (380MB)

옵션 A — ml-mobileclip의 스크립트 사용 (모든 변형 모델 한 번에 다운):

```bash
cd ml-mobileclip
bash get_pretrained_models.sh
cd ..
```

옵션 B — S2만 직접 다운로드:

```bash
mkdir -p ml-mobileclip/checkpoints
curl -L -o ml-mobileclip/checkpoints/mobileclip_s2.pt \
  https://docs-assets.developer.apple.com/ml-research/datasets/mobileclip/mobileclip_s2.pt
```

다른 변형 (S0/S1/B/B-LT) 도 같은 경로 패턴:
`https://docs-assets.developer.apple.com/ml-research/datasets/mobileclip/mobileclip_{s0,s1,s2,b,blt}.pt`

### 4. ONNX 변환된 모델 (자체 생성)

위 1~3까지 받은 후, 다음 명령으로 ONNX 변환 (Step A):

```bash
# YOLO ONNX export (ultralytics가 자체적으로 처리)
python -c "from ultralytics import YOLO; YOLO('yolo11n.pt').export(format='onnx', imgsz=320, opset=17, simplify=True)"

# MobileCLIP-S2 image encoder ONNX export
python export_mobileclip_s2.py

# QNN 컴파일러 호환성 보정
python clean_onnx_for_qnn.py yolo11n.onnx mobileclip_s2_image.onnx

# (선택) PyTorch ↔ ONNX 출력 동등성 검증
python verify_onnx.py
```

생성 파일: `yolo11n.onnx`, `mobileclip_s2_image.onnx` (+ `.onnx.data` 외부 가중치 파일)

### 5. QNN 컴파일 바이너리 (Qualcomm AI Hub 사용)

Step B는 클라우드 컴파일이라 Qualcomm AI Hub 계정 필요.

```bash
# 1) 가입: https://aihub.qualcomm.com
# 2) Settings → API Token 발급
# 3) 로컬 설정
qai-hub configure --api_token <YOUR_TOKEN>

# 4) 컴파일 + profile (XR2 Gen 2 Proxy 타겟, AR1 Gen 1의 가장 가까운 proxy)
python qai_hub_pipeline.py
```

생성 파일: `yolo11n.qnn_context.bin` (6MB), `mobileclip_s2_image.qnn_context.bin` (117MB)

자세한 분석은 [`docs/dev-guide.md`](docs/dev-guide.md) (ONNX/NPU 섹션), [`qai_hub_results.json`](qai_hub_results.json) 참고.

---

## Python 환경

```bash
# 권장: Python 3.10~3.12
pip install ultralytics open-clip-torch torch onnx onnxruntime onnxslim onnxscript qai-hub h5py
```

`ml-mobileclip` 은 README의 3번 항목에서 별도 설치.

---

## 메인 파이프라인 실행 (맥북)

```bash
python yolo.py input/sample.mp4 output/result.mp4
```

옵션:
- `--log stats.json` — 프레임별 통계 JSON 출력

---

## 하드웨어 타겟 (RayNeo X3 Pro)

| 항목 | 값 |
|------|------|
| Chipset | Snapdragon AR1 Gen 1 |
| NPU | Hexagon v73 (FP16 유닛 없음 → w8a8 필수) |
| OS | Android 12, API 32, arm64-v8a |
| RAM | 4GB |
| 디스플레이 | 양안 MicroLED 웨이브가이드, 640×480 |
| 무게 | 76g |

AI Hub에 AR1 Gen 1 직접 등록 없음 → **XR2 Gen 2 (Proxy)** 를 가장 가까운 대체 타겟으로 사용 (Hexagon v69, XR class).

---

## 진행 상태

진행 history(빌드별 시도·결과·교훈)는 한곳에 모았다 → **[`docs/EXPERIMENTS.md`](docs/EXPERIMENTS.md)**. 현재 상태 스냅샷은 [`docs/vision.md`](docs/vision.md) 맨 위.

요약: Step A(ONNX export)·B(QNN 컴파일)·C(Unity APK 빌드)·D(안경 설치+검증)·E(3D world-anchored 표시) 모두 온디바이스 동작 확인됨. 현 병목은 매칭 정확도/조준(EXPERIMENTS.md "살아있는 교훈" 참조).

---

## 라이선스

내부 PoC. 외부 배포 금지.
