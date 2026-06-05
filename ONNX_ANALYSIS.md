# ONNX 그래프 분석 — YOLO11n + MobileCLIP-S2

> 생성일: 2026-06-06
> Step A 완료 후 분석 (Step B QNN 컴파일 전 사전 점검)

---

## 요약

| 모델 | 노드 수 | 파일 크기 (FP32) | INT8 추정 | NPU 호환성 |
|------|--------|----------------|-----------|----------|
| **YOLO11n** | 320 | 10.12 MB | ~2.51 MB | ⚠️ Resize 2회 (확인 필요) |
| **MobileCLIP-S2 Image Encoder** | 664 | 136.32 MB | ~34.04 MB | ✅ 위험 연산 없음 |

PyTorch ↔ ONNX 출력 동등성 검증 결과:

| 모델 | Output Shape | Max abs diff | Mean abs diff | 코사인 유사도 | 판정 |
|------|--------------|--------------|---------------|--------------|------|
| YOLO11n | (1, 84, 2100) | 1.28e-3 | 2.27e-6 | — | 사실상 PASS (FP32 부동소수점 노이즈) |
| MobileCLIP-S2 | (1, 512) | 1.68e-6 | 3.56e-7 | **1.000000** | ✅ PASS (소수점 6자리까지 일치) |

---

## YOLO11n 상세

### 메타데이터

| 항목 | 값 |
|------|-----|
| ONNX opset | 17 |
| IR version | 8 |
| Producer | pytorch 2.11.0 |
| 파일 크기 | 10.12 MB |

### 입출력

```
입력:  images   shape=[1, 3, 320, 320]  dtype=FLOAT
출력:  output0  shape=[1, 84, 2100]     dtype=FLOAT
```

- `84 = 4 (xywh) + 80 (COCO 클래스)`
- `2100 = 1600 (40×40) + 400 (20×20) + 100 (10×10)` — 3개 stride 격자

### 연산자 분포 (총 320개)

```
Conv                    88  ████████████████████████████████████████
Mul                     79  ████████████████████████████████████████
Sigmoid                 78  ████████████████████████████████████████
Concat                  21  █████████████████████
Add                     16  ████████████████
Reshape                 11  ███████████
Split                   10  ██████████
MaxPool                  3  ███
Transpose                3  ███
MatMul                   2  ██
Softmax                  2  ██
Resize                   2  ██   ⚠️
Slice                    2  ██
Sub                      2  ██
Div                      1  █
```

전형적인 CNN backbone + FPN(Feature Pyramid Network) 패턴.

### NPU 위험 신호

| 연산 | 횟수 | 문제 가능성 |
|------|------|-----------|
| **Resize** | 2 | FPN의 upsample 단계. nearest/bilinear면 NPU OK, cubic이면 CPU fallback 위험. Step B 컴파일 결과로 확인 |

### 가중치

- 초기화 텐서 수: 196
- FP32 가중치: 10.02 MB
- INT8 양자화 후 예상: ~2.51 MB

---

## MobileCLIP-S2 Image Encoder 상세

### 메타데이터

| 항목 | 값 |
|------|-----|
| ONNX opset | 18 |
| IR version | 10 |
| Producer | pytorch 2.11.0 |
| 파일 크기 | 136.32 MB |

### 입출력

```
입력:  image      shape=[1, 3, 256, 256]  dtype=FLOAT (pre-normalized)
출력:  embedding  shape=[1, 512]          dtype=FLOAT (L2-normalized)
```

> L2 정규화가 그래프에 포함됨 (`ReduceL2` + `Div`). 앱에서 별도 정규화 불필요.

### 연산자 분포 (총 664개)

```
Conv                   189  ████████████████████████████████████████
Mul                    163  ████████████████████████████████████████
Add                    102  ████████████████████████████████████████
Div                     55  ████████████████████████████████████████
Erf                     54  ████████████████████████████████████████  (GELU activation)
Reshape                 20  ████████████████████
Transpose               20  ████████████████████
MatMul                  13  █████████████
Slice                   12  ████████████
Squeeze                 12  ████████████
BatchNormalization       4  ████
Softmax                  4  ████
Gemm                     4  ████
ReduceMean               3  ███
Relu                     3  ███
Sigmoid                  3  ███
AveragePool              1  █
ReduceL2                 1  █
Clip                     1  █
```

CNN(Conv-heavy stem) + Transformer(MatMul, Softmax, Erf=GELU) 하이브리드 패턴.
MobileCLIP의 FastViT 백본 특성.

### NPU 위험 신호

| 항목 | 결과 |
|------|------|
| Loop / If (동적 제어) | ✅ 없음 |
| NonMaxSuppression | ✅ 없음 |
| TopK / ScatterND / GatherND | ✅ 없음 |
| RoiAlign / GridSample | ✅ 없음 |
| Resize | ✅ 없음 |

**위험 연산 0개.** QNN INT8 컴파일에서 전체 그래프 NPU 배치 가능성 매우 높음.

### 가중치 — 중요 정정

| 항목 | 값 |
|------|-----|
| 초기화 텐서 수 | 472 |
| FP32 가중치 | 136.17 MB |
| 추정 파라미터 수 | **~35.7M** (image encoder만) |
| INT8 양자화 후 예상 | ~34.04 MB |

> ⚠️ 처음 노션 결정 로그에 "~5M"으로 잘못 적었던 부분 정정 완료. 실제로는 ViT-B-32(87M)의 약 40% 수준. "1/17 작음"이 아니라 "2.5배 작음".

---

## NPU 컴파일 결과 예측

Step B (Qualcomm AI Hub QNN 컴파일) 진행 시 예상 결과:

| 모델 | 예상 NPU 배치율 | 예상 INT8 레이턴시 (AR1 Gen 1) | 기획안 목표 대비 |
|------|---------------|------------------------------|------------------|
| YOLO11n | 95~100% (Resize 확인 필요) | **10~30 ms** | 목표 10~20ms 부합 가능 |
| MobileCLIP-S2 | 95~100% (clean graph) | **50~150 ms** | 목표 50~150ms 정합 |

전체 파이프라인 (Step 2 YOLO + Step 3 CLIP) 합계: **약 60~180 ms**. 기획안 200ms 목표 안에 들어올 가능성 높음.

---

## 그래프 시각화 (인터랙티브)

CLI 텍스트로는 그래프 구조를 보기 어렵습니다. 다음 중 하나로 시각화하세요:

### 옵션 1: 웹 Netron (가장 간단)
```
https://netron.app
```
브라우저에서 위 주소 접속 → `yolo11n.onnx` 또는 `mobileclip_s2_image.onnx` 드래그앤드롭.

### 옵션 2: 로컬 Netron
```bash
pip install netron
netron ~/Desktop/AR_project/yolo11n.onnx
netron ~/Desktop/AR_project/mobileclip_s2_image.onnx
```
브라우저가 자동으로 localhost를 띄움.

### 옵션 3: 텍스트 dump (전체 그래프 한 줄씩)
```bash
python -c "import onnx; print(onnx.helper.printable_graph(onnx.load('yolo11n.onnx').graph))" > yolo_graph.txt
```

---

## Step B 진입 체크리스트

- [x] yolo11n.onnx 생성 + PT 동등성 검증
- [x] mobileclip_s2_image.onnx 생성 + PT 동등성 검증
- [x] NPU 호환성 사전 점검
- [ ] Qualcomm AI Hub 가입 + API 토큰 발급
- [ ] qai-hub Python 패키지 설치
- [ ] 두 모델 compile + profile 잡 제출
- [ ] 실측 레이턴시 결과 확인 → 기획안 v1.2 미검증 항목 해소
