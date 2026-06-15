"""
ONNX 파일을 QNN/SNPE 컴파일러용으로 정리.

PyTorch 2.x torch.onnx.export(또는 ultralytics export) 결과에 자주 발생하는 이슈:
  > Tensors {...} occur in value_info but also in model IO.

원인: 출력 텐서가 graph.value_info(중간 텐서)와 graph.output 양쪽에 동시 등재됨.
Qualcomm AI Hub/QNN/SNPE 컴파일러는 이걸 거부함 (ONNX IR 명세 위반).

사용법:
    python clean_onnx_for_qnn.py model.onnx [model2.onnx ...]
"""

import sys
import onnx


def clean(path):
    m = onnx.load(path)
    io_names = set(t.name for t in m.graph.input) | set(t.name for t in m.graph.output)
    before = len(m.graph.value_info)
    new_value_info = [v for v in m.graph.value_info if v.name not in io_names]
    removed = before - len(new_value_info)
    if removed == 0:
        print(f"{path}: nothing to clean")
        return
    del m.graph.value_info[:]
    m.graph.value_info.extend(new_value_info)
    onnx.checker.check_model(m)
    onnx.save(m, path)
    print(f"{path}: removed {removed} duplicate value_info entries (kept {len(new_value_info)})")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    for p in sys.argv[1:]:
        clean(p)
