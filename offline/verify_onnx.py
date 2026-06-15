"""
PyTorch ↔ ONNX 출력 동등성 검증

YOLO11n: yolo11n.pt vs yolo11n.onnx
MobileCLIP-S2: PyTorch encode_image+L2 vs mobileclip_s2_image.onnx

기준: max abs diff < 1e-3 (FP32, NPU 양자화 전이므로 거의 일치해야 함)
"""

import sys
import numpy as np
import onnx
import onnxruntime as ort
import torch
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / "ml-mobileclip"))

torch.manual_seed(0)
np.random.seed(0)


def verify_yolo():
    print("=" * 60)
    print("YOLO11n: PyTorch (.pt) vs ONNX (.onnx)")
    print("=" * 60)
    from ultralytics import YOLO

    model = YOLO("yolo11n.pt")
    pt_model = model.model.eval()

    dummy_input = torch.randn(1, 3, 320, 320)
    with torch.no_grad():
        pt_out = pt_model(dummy_input)
    pt_arr = pt_out[0].cpu().numpy() if isinstance(pt_out, tuple) else pt_out.cpu().numpy()

    sess = ort.InferenceSession("yolo11n.onnx", providers=["CPUExecutionProvider"])
    onnx_out = sess.run(None, {"images": dummy_input.numpy()})[0]

    print(f"PyTorch  output shape: {pt_arr.shape}")
    print(f"ONNX     output shape: {onnx_out.shape}")
    assert pt_arr.shape == onnx_out.shape, "Shape mismatch!"

    diff = np.abs(pt_arr - onnx_out)
    print(f"Max abs diff: {diff.max():.6e}")
    print(f"Mean abs diff: {diff.mean():.6e}")
    print(f"PASS" if diff.max() < 1e-3 else f"FAIL (max diff > 1e-3)")
    print()


def verify_mobileclip():
    print("=" * 60)
    print("MobileCLIP-S2 Image Encoder: PyTorch vs ONNX")
    print("=" * 60)
    import mobileclip

    model, _, _ = mobileclip.create_model_and_transforms(
        "mobileclip_s2", pretrained="ml-mobileclip/checkpoints/mobileclip_s2.pt"
    )
    model = model.eval()

    dummy_input = torch.randn(1, 3, 256, 256)
    with torch.no_grad():
        emb = model.encode_image(dummy_input)
        emb = emb / emb.norm(dim=-1, keepdim=True).clamp(min=1e-12)
    pt_arr = emb.cpu().numpy()

    sess = ort.InferenceSession("mobileclip_s2_image.onnx", providers=["CPUExecutionProvider"])
    onnx_out = sess.run(None, {"image": dummy_input.numpy()})[0]

    print(f"PyTorch  output shape: {pt_arr.shape}")
    print(f"ONNX     output shape: {onnx_out.shape}")
    assert pt_arr.shape == onnx_out.shape, "Shape mismatch!"

    diff = np.abs(pt_arr - onnx_out)
    print(f"Max abs diff: {diff.max():.6e}")
    print(f"Mean abs diff: {diff.mean():.6e}")

    cos = float(np.dot(pt_arr.flatten(), onnx_out.flatten()))
    print(f"Cosine similarity (PT vs ONNX): {cos:.6f}")
    print(f"L2 norm of PyTorch output: {np.linalg.norm(pt_arr):.6f}")
    print(f"L2 norm of ONNX output:    {np.linalg.norm(onnx_out):.6f}")
    print(f"PASS" if diff.max() < 1e-3 else f"FAIL (max diff > 1e-3)")
    print()


if __name__ == "__main__":
    verify_yolo()
    verify_mobileclip()
