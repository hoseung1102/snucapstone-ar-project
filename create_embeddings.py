"""
Eagle Eye - 상품 임베딩 생성 (오프라인 1회 실행)
================================================
products/ 안의 레퍼런스 이미지를 CLIP Image Encoder로 인코딩하여
db/embeddings/*.npy 로 저장하고 db/metadata.json 을 갱신한다.

맥북 프로토타입: open_clip ViT-B-32 사용
실제 하드웨어:   MobileCLIP-S2 (SNPE 변환 후 교체)

사용법:
    python create_embeddings.py
"""

import json
import warnings
import numpy as np
import open_clip
import torch
from pathlib import Path
from PIL import Image

warnings.filterwarnings("ignore")

# ── 설정 ──────────────────────────────────────────────────────
MODEL_NAME  = "ViT-B-32"
PRETRAINED  = "openai"
DEVICE      = "mps" if torch.backends.mps.is_available() else "cpu"

EMBEDDINGS_DIR  = Path("db/embeddings")
METADATA_PATH   = Path("db/metadata.json")
ADS_DIR         = Path("db/ads")

EMBEDDINGS_DIR.mkdir(parents=True, exist_ok=True)

# 레퍼런스 = 광고 이미지 자체.
# "영상 속 물체가 이 광고의 상품과 얼마나 비슷한가"를 CLIP이 판단.
PRODUCT_DEFS = {
    # key: 레퍼런스로 쓸 이미지 경로 / value: (id, 이름, 광고 이미지 파일명)
    "db/ads/laptop_ad.png": (0, "laptop", "laptop_ad.png"),
    "db/ads/iphone_ad.jpg": (1, "cell phone", "iphone_ad.jpg"),
}
# ─────────────────────────────────────────────────────────────

print(f"모델 로드: {MODEL_NAME} / {PRETRAINED}  →  {DEVICE}")
model, _, preprocess = open_clip.create_model_and_transforms(MODEL_NAME, pretrained=PRETRAINED)
model = model.to(DEVICE).eval()

metadata = []

for ref_filename, (prod_id, name, ad_filename) in PRODUCT_DEFS.items():
    ref_path = Path(ref_filename)
    if not ref_path.exists():
        print(f"  [SKIP] {ref_path} 없음")
        continue

    img = preprocess(Image.open(ref_path).convert("RGB")).unsqueeze(0).to(DEVICE)
    with torch.no_grad():
        emb = model.encode_image(img)
        emb = emb / emb.norm(dim=-1, keepdim=True)  # L2 정규화

    emb_np = emb.cpu().float().numpy()  # (1, 512)
    emb_path = EMBEDDINGS_DIR / f"{name}.npy"
    np.save(emb_path, emb_np)

    ad_labels = {
        "laptop":     "Samsung Galaxy Book6 Pro",
        "cell phone": "iPhone 17 Pro",
    }
    metadata.append({
        "id":        prod_id,
        "name":      name,
        "embedding": str(emb_path),
        "ad_image":  str(ADS_DIR / ad_filename),
        "ad_label":  ad_labels.get(name, name),
    })

    print(f"  [{name}] 임베딩 저장: {emb_path}  shape={emb_np.shape}")

METADATA_PATH.write_text(json.dumps(metadata, ensure_ascii=False, indent=2))
print(f"\nmetadata 저장: {METADATA_PATH}")
print("완료.")
