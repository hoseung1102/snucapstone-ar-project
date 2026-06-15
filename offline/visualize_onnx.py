"""
ONNX 그래프 구조 시각화 — 텍스트 요약 + Netron 안내

NPU 컴파일 시 fallback 가능성 있는 연산을 사전 파악하는 게 목적.
"""

import onnx
from collections import Counter
from pathlib import Path


def summarize_graph(path):
    print("=" * 70)
    print(f"모델: {path}")
    print("=" * 70)

    model = onnx.load(path)
    graph = model.graph

    print(f"\n[ 메타데이터 ]")
    print(f"  ONNX opset: {model.opset_import[0].version}")
    print(f"  IR version: {model.ir_version}")
    print(f"  producer:   {model.producer_name} {model.producer_version}")

    file_size_mb = Path(path).stat().st_size / 1024 / 1024
    print(f"  파일 크기:  {file_size_mb:.2f} MB")

    print(f"\n[ 입력 ]")
    for inp in graph.input:
        shape = [d.dim_value if d.dim_value > 0 else d.dim_param or "?" for d in inp.type.tensor_type.shape.dim]
        elem_type = onnx.TensorProto.DataType.Name(inp.type.tensor_type.elem_type)
        print(f"  {inp.name}  shape={shape}  dtype={elem_type}")

    print(f"\n[ 출력 ]")
    for out in graph.output:
        shape = [d.dim_value if d.dim_value > 0 else d.dim_param or "?" for d in out.type.tensor_type.shape.dim]
        elem_type = onnx.TensorProto.DataType.Name(out.type.tensor_type.elem_type)
        print(f"  {out.name}  shape={shape}  dtype={elem_type}")

    op_counts = Counter(node.op_type for node in graph.node)
    total_ops = sum(op_counts.values())
    print(f"\n[ 연산자 분포 — 총 {total_ops}개 노드 ]")
    for op, count in op_counts.most_common():
        bar = "█" * min(40, count)
        print(f"  {op:<20} {count:>4}  {bar}")

    npu_unfriendly = {
        "NonMaxSuppression": "NMS는 보통 NPU에서 CPU fallback",
        "TopK": "동적 K 사용 시 fallback",
        "ScatterND": "메모리 접근 패턴 복잡, fallback 가능성 ↑",
        "GatherND": "메모리 접근 패턴 복잡, fallback 가능성 ↑",
        "RoiAlign": "대부분의 NPU에서 미지원",
        "Loop": "동적 제어 흐름 — NPU 미지원",
        "If": "동적 분기 — NPU 미지원",
        "Resize": "특정 모드(cubic 등)는 fallback",
        "GridSample": "QNN 일부 버전에서 미지원",
    }
    flagged = [(op, op_counts[op], reason) for op, reason in npu_unfriendly.items() if op in op_counts]
    if flagged:
        print(f"\n[ ⚠️ NPU fallback 위험 연산 ]")
        for op, count, reason in flagged:
            print(f"  {op}  ({count}회)  →  {reason}")
    else:
        print(f"\n[ ✅ NPU fallback 위험 연산 없음 ]")

    weight_total = 0
    for init in graph.initializer:
        size = 1
        for d in init.dims:
            size *= d
        elem_bytes = 4 if init.data_type == 1 else 2 if init.data_type == 10 else 4
        weight_total += size * elem_bytes
    print(f"\n[ 가중치 통계 ]")
    print(f"  초기화 텐서 수: {len(graph.initializer)}")
    print(f"  추정 가중치 크기 (FP32): {weight_total/1024/1024:.2f} MB")
    print(f"  → INT8 양자화 후 예상 크기: ~{weight_total/4/1024/1024:.2f} MB")

    print()


if __name__ == "__main__":
    summarize_graph("yolo11n.onnx")
    summarize_graph("mobileclip_s2_image.onnx")

    print("=" * 70)
    print("그래프 시각화 (인터랙티브)")
    print("=" * 70)
    print("""
다음 중 하나로 그래프를 시각적으로 볼 수 있어요:

[옵션 1] 웹 Netron (가장 간단, 다운로드 불필요)
  → https://netron.app 접속 → .onnx 파일 드래그앤드롭

[옵션 2] 로컬 Netron (브라우저 자동으로 열림)
  $ pip install netron
  $ netron yolo11n.onnx                  # 브라우저에서 localhost로 열림
  $ netron mobileclip_s2_image.onnx

[옵션 3] CLI dump (옵셔널, 한 노드씩)
  $ python -c "import onnx; m=onnx.load('yolo11n.onnx'); print(onnx.helper.printable_graph(m.graph))"
""")
