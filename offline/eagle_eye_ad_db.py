"""
Eagle Eye - Ad DB
=================
광고 DB 정의 및 조회 모듈.

파이프라인에서의 역할:
    YOLO bbox 검출 → [이 모듈] → CLIP 정밀 식별 → 광고 오버레이

YOLO가 물체를 잡았더라도 광고 DB에 해당 카테고리 광고가 없으면
CLIP 추론을 건너뛰고 대기 상태로 복귀한다.

DB 상태: 구축 중
    - laptop 카테고리 1개 항목 (Samsung Galaxy Book6 Pro) 예시로 등록
    - product_images/ 와 db/embeddings/ 는 실제 상품 이미지·임베딩 추가 시 채워짐
    - 신규 광고주 상품 추가: AD_DB 리스트에 항목 append 후 embed_all() 실행
"""

from __future__ import annotations
from pathlib import Path

# ============================================================
# 광고 DB
# 각 항목 필드:
#   ad_id          : 고유 식별자
#   product_name   : 광고 상품명
#   brand          : 광고주 브랜드
#   coco_class_ids : 이 상품에 해당하는 YOLO COCO 클래스 ID 목록
#                    (YOLO 검출 클래스 → 광고 후보 매핑에 사용)
#   product_images : CLIP 임베딩 생성용 상품 참조 이미지 경로 목록
#                    (products/ 폴더 기준. DB 구축 시 채울 것)
#   embedding_path : 사전 계산된 CLIP 임베딩 .npy 저장 경로
#                    (Step 3 시작 시 로드)
#   ad_image_path  : 광고 오버레이 소재 경로 (Step 4에서 사용)
#   ad_copy        : 광고 텍스트 (타이틀, 본문, CTA)
# ============================================================

AD_DB: list[dict] = [
    {
        "ad_id": "laptop_samsung_galaxybook6",
        "product_name": "Samsung Galaxy Book6 Pro",
        "brand": "Samsung",
        "coco_class_ids": [63],                           # COCO: laptop
        "product_images": [
            # "products/samsung_galaxybook6_front.jpg",   # 실제 이미지 추가 시 주석 해제
            # "products/samsung_galaxybook6_angle.jpg",
        ],
        "embedding_path": "db/embeddings/laptop_samsung_galaxybook6.npy",
        "ad_image_path": "db/ads/laptop_ad.png",
        "ad_copy": {
            "title": "Samsung Galaxy Book6 Pro",
            "body": "2nd Gen NPU · 32GB RAM · 16\" 3K AMOLED",
            "cta": "Shop Now",
        },
    },
    # 신규 상품 추가 예시 (주석 해제 후 product_images 채울 것):
    # {
    #     "ad_id": "phone_apple_iphone16",
    #     "product_name": "Apple iPhone 16",
    #     "brand": "Apple",
    #     "coco_class_ids": [67],                         # COCO: cell phone
    #     "product_images": [],
    #     "embedding_path": "db/embeddings/phone_apple_iphone16.npy",
    #     "ad_image_path": "db/ads/phone_ad.png",
    #     "ad_copy": {"title": "iPhone 16", "body": "...", "cta": "Buy"},
    # },
]


# ============================================================
# 내부 인덱스: COCO class_id → AD_DB 항목 목록 (O(1) 조회)
# ============================================================

_CLASS_INDEX: dict[int, list[dict]] = {}


def _build_index() -> None:
    _CLASS_INDEX.clear()
    for entry in AD_DB:
        for cls_id in entry["coco_class_ids"]:
            _CLASS_INDEX.setdefault(cls_id, []).append(entry)


_build_index()  # 모듈 로드 시 자동 실행


# ============================================================
# 공개 조회 함수
# ============================================================

def is_class_advertised(coco_class_id: int) -> bool:
    """해당 YOLO 클래스에 대한 광고가 DB에 존재하는지 반환.

    파이프라인 필터: True면 CLIP으로 진행, False면 스킵.
    """
    return coco_class_id in _CLASS_INDEX


def get_ads_for_class(coco_class_id: int) -> list[dict]:
    """해당 YOLO 클래스에 매핑된 광고 항목 목록 반환."""
    return _CLASS_INDEX.get(coco_class_id, [])


def get_ad_by_id(ad_id: str) -> dict | None:
    """ad_id로 광고 항목 직접 조회."""
    for entry in AD_DB:
        if entry["ad_id"] == ad_id:
            return entry
    return None


def advertised_class_ids() -> set[int]:
    """현재 DB에 광고가 등록된 전체 COCO class_id 집합 반환.
    YOLO의 classes= 파라미터 동적 생성에 활용 가능.
    """
    return set(_CLASS_INDEX.keys())


def db_summary() -> None:
    """현재 DB 상태를 콘솔에 출력 (디버그용)."""
    print(f"=== Ad DB 현황 ({len(AD_DB)}개 상품) ===")
    for entry in AD_DB:
        emb_exists = Path(entry["embedding_path"]).exists()
        ad_exists = Path(entry["ad_image_path"]).exists()
        imgs = len(entry["product_images"])
        print(
            f"  [{entry['ad_id']}]  brand={entry['brand']}"
            f"  class_ids={entry['coco_class_ids']}"
            f"  product_imgs={imgs}"
            f"  embedding={'✓' if emb_exists else '✗ (미생성)'}"
            f"  ad_asset={'✓' if ad_exists else '✗ (없음)'}"
        )


if __name__ == "__main__":
    db_summary()
