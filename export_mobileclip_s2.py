"""
MobileCLIP-S2 Image Encoder → ONNX export

설계 결정 (Notion 기술 결정 로그 참고):
- L2 정규화를 그래프에 포함 (앱 코드 단순화)
- 입력 고정 shape (1, 3, 256, 256) — NPU 최적화 위해 dynamic=False
- 텍스트 인코더는 export 안 함 (사전 임베딩 방식)
- Letterboxing은 그래프 외부 (앱에서 처리)

출력:
    mobileclip_s2_image.onnx
    - input:  image  (1, 3, 256, 256)  float32  [pre-normalized]
    - output: embedding  (1, 512)  float32  [L2-normalized unit vector]
"""

import sys
from pathlib import Path
import torch
import torch.nn as nn

sys.path.insert(0, str(Path(__file__).parent / "ml-mobileclip"))
import mobileclip


CHECKPOINT = "ml-mobileclip/checkpoints/mobileclip_s2.pt"
OUT_PATH = "mobileclip_s2_image.onnx"
IMG_SIZE = 256


class ImageEncoderWithNorm(nn.Module):
    """encode_image 호출 후 L2 정규화까지 그래프에 포함."""

    def __init__(self, clip_model):
        super().__init__()
        self.clip = clip_model

    def forward(self, image):
        emb = self.clip.encode_image(image)
        emb = emb / emb.norm(dim=-1, keepdim=True).clamp(min=1e-12)
        return emb


def main():
    print(f"=== Loading MobileCLIP-S2 from {CHECKPOINT} ===")
    model, _, preprocess = mobileclip.create_model_and_transforms(
        "mobileclip_s2", pretrained=CHECKPOINT
    )
    model = model.eval()

    print(f"=== Building wrapper module ===")
    wrapped = ImageEncoderWithNorm(model).eval()

    dummy = torch.randn(1, 3, IMG_SIZE, IMG_SIZE)
    with torch.no_grad():
        out = wrapped(dummy)
    print(f"Sanity forward: input {tuple(dummy.shape)} → output {tuple(out.shape)}")
    print(f"Output L2 norm (should be ~1.0): {out.norm(dim=-1).item():.6f}")
    print(f"Embedding dim: {out.shape[-1]}")

    print(f"\n=== Exporting to {OUT_PATH} ===")
    torch.onnx.export(
        wrapped,
        dummy,
        OUT_PATH,
        export_params=True,
        opset_version=17,
        do_constant_folding=True,
        input_names=["image"],
        output_names=["embedding"],
        dynamic_axes=None,
    )
    print(f"Saved: {OUT_PATH}")

    # onnxslim 최적화
    print("\n=== Simplifying with onnxslim ===")
    import onnxslim
    slimmed = onnxslim.slim(OUT_PATH)
    import onnx
    onnx.save(slimmed, OUT_PATH)
    print(f"Simplified: {OUT_PATH}")

    # PyTorch 2.x 익스포터 버그 우회:
    # 출력 텐서가 value_info와 graph.output 양쪽에 중복으로 들어가서
    # QNN/SNPE 컴파일러가 거부. value_info에서 IO 텐서 제거.
    print("\n=== Cleaning duplicate value_info entries (QNN compatibility) ===")
    m = onnx.load(OUT_PATH)
    io_names = set(t.name for t in m.graph.input) | set(t.name for t in m.graph.output)
    before = len(m.graph.value_info)
    new_value_info = [v for v in m.graph.value_info if v.name not in io_names]
    del m.graph.value_info[:]
    m.graph.value_info.extend(new_value_info)
    onnx.checker.check_model(m)
    onnx.save(m, OUT_PATH)
    print(f"Removed {before - len(new_value_info)} duplicate entries.")


if __name__ == "__main__":
    main()
