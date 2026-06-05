# Eagle Eye — AR Contextual Information Layer (PoC)

AR 글라스에서 사용자가 보는 객체를 인식하고 관련 정보를 화면에 띄우는 시스템의 PoC.

> Status: **v1 PoC 진행 중**
> 타겟 하드웨어: **RayNeo X3 Pro** (Snapdragon AR1 Gen 1)
> 표시 방식: **2D HUD** (Spatial Anchor 3D 표시는 v2+에서)
> 자세한 시스템 비전은 별도 기획 문서 참고

---

## 파이프라인

```
[안경 카메라] → [IMU 안정 트리거] → [YOLO 객체 검출]
              → [MobileCLIP 정밀 식별] → [HUD 광고 표시]
```

맥북 프로토타입 단계에선 IMU를 Optical Flow로, Spatial Anchor를 2D bbox 좌표 고정으로 치환.

---

## 디렉토리 구조

```
.
├── yolo.py                       # 메인 파이프라인 (4-step 통합)
├── eagle_eye_step2.py            # Step 2 단독 실행 (YOLO 검출만)
├── create_embeddings.py          # 상품 이미지 → 임베딩 DB 생성 (오프라인)
├── eagle_eye_ad_db.py            # 광고 DB CRUD 헬퍼
├── create_ad.py                  # 광고 카드 이미지 생성 헬퍼
├── diag_stability.py             # Optical Flow 안정 감지 디버깅
├── export_mobileclip_s2.py       # PyTorch → ONNX export (MobileCLIP-S2)
├── verify_onnx.py                # PyTorch ↔ ONNX 출력 동등성 검증
├── visualize_onnx.py             # ONNX 그래프 텍스트 요약
├── clean_onnx_for_qnn.py         # PyTorch ONNX export 결과를 QNN 컴파일러용으로 정리
├── qai_hub_compile.py            # Qualcomm AI Hub로 ONNX → QNN 컴파일 (단발)
├── qai_hub_pipeline.py           # 컴파일 + profile + 다운로드 (백그라운드 러너)
│
├── db/
│   ├── metadata.json             # 상품 메타데이터 (id, name, embedding 경로, 광고 이미지)
│   ├── embeddings/*.npy          # CLIP 임베딩 (오프라인 사전 계산)
│   └── ads/*.png|.jpg            # 광고 이미지 리소스
├── products/                     # 상품 레퍼런스 이미지
├── input/                        # 입력 비디오 (gitignored)
├── output/                       # 출력 비디오 + 통계 (gitignored)
│
├── unity_assets_prep/            # Unity 프로젝트로 옮길 C# 스크립트 준비물
├── ONNX_ANALYSIS.md              # ONNX 그래프 분석 / NPU 호환성 사전 점검
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

자세한 분석은 [`ONNX_ANALYSIS.md`](ONNX_ANALYSIS.md), [`qai_hub_results.json`](qai_hub_results.json) 참고.

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
| NPU | Hexagon (v68 또는 v69, 미공식) |
| OS | Android 12, API 32, arm64-v8a |
| RAM | 4GB |
| 디스플레이 | 양안 MicroLED 웨이브가이드, 640×480 |
| 무게 | 76g |

AI Hub에 AR1 Gen 1 직접 등록 없음 → **XR2 Gen 2 (Proxy)** 를 가장 가까운 대체 타겟으로 사용 (Hexagon v69, XR class).

---

## 진행 상태 (2026-06-06 기준)

- [x] **Step A** PyTorch → ONNX export + 동등성 검증
- [x] **Step B** QNN 컴파일 + 클라우드 프로파일 (XR2 Gen 2 Proxy 기준 18.4ms 실측)
- [ ] **Step C** Unity 프로젝트 + Android APK 빌드 (진행 중, 2D HUD 방식)
- [ ] **Step D** 안경에 APK 설치 + 실측 검증
- [ ] **Step E** 3D Spatial Anchor 표시로 업그레이드 (RayNeo SDK 입수 후)

---

## 라이선스

내부 PoC. 외부 배포 금지.
