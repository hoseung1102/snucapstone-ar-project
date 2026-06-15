"""
Samsung Galaxy Book 광고 이미지 생성 (테스트용 placeholder)
실행: python create_ad.py
"""

from PIL import Image, ImageDraw, ImageFont

W, H = 400, 260
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# 배경 (Samsung 브랜드 컬러: 짙은 네이비)
draw.rounded_rectangle([(0, 0), (W - 1, H - 1)], radius=16,
                        fill=(26, 28, 64, 230), outline=(70, 130, 200, 255), width=2)

# 상단 브랜드 바
draw.rounded_rectangle([(0, 0), (W - 1, 48)], radius=16, fill=(26, 28, 64, 255))
draw.rectangle([(0, 32), (W - 1, 48)], fill=(26, 28, 64, 255))

# Samsung 워드마크 (텍스트로 표현)
try:
    font_brand = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 22)
    font_title = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 18)
    font_sub   = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 13)
    font_price = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 20)
    font_cta   = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 14)
except Exception:
    font_brand = font_title = font_sub = font_price = font_cta = ImageFont.load_default()

draw.text((20, 12), "SAMSUNG", font=font_brand, fill=(255, 255, 255, 255))

# 노트북 아이콘 (단순 사각형으로 표현)
lx, ly = 20, 65
draw.rounded_rectangle([(lx, ly), (lx + 120, ly + 78)], radius=4,
                        fill=(40, 44, 90, 255), outline=(100, 150, 220, 200), width=1)
# 화면
draw.rounded_rectangle([(lx + 6, ly + 6), (lx + 114, ly + 66)], radius=2,
                        fill=(15, 20, 60, 255))
# 화면 내 그라디언트 효과 (수평선)
for i in range(10):
    y = ly + 15 + i * 5
    alpha = int(60 + i * 15)
    draw.line([(lx + 15, y), (lx + 105, y)], fill=(80, 140, 255, alpha), width=1)
# 베젤 하단 (힌지)
draw.rounded_rectangle([(lx + 8, ly + 80), (lx + 112, ly + 86)], radius=2,
                        fill=(50, 55, 100, 255))

# 제품명 및 설명
draw.text((160, 68), "Galaxy Book6 Pro", font=font_title, fill=(255, 255, 255, 255))
draw.text((160, 96), "2nd Gen NPU · 32GB RAM", font=font_sub, fill=(160, 180, 220, 255))
draw.text((160, 114), "16\" 3K AMOLED Display", font=font_sub, fill=(160, 180, 220, 255))

# 구분선
draw.line([(160, 136), (380, 136)], fill=(70, 90, 140, 200), width=1)

# 가격
draw.text((160, 145), "From $1,449.99", font=font_price, fill=(100, 200, 255, 255))

# CTA 버튼
draw.rounded_rectangle([(160, 178), (310, 206)], radius=8, fill=(0, 120, 212, 255))
draw.text((185, 185), "Shop Now →", font=font_cta, fill=(255, 255, 255, 255))

# 하단 disclaimer
draw.text((20, 232), "© 2026 Samsung Electronics America, Inc.",
          font=ImageFont.load_default(), fill=(100, 110, 150, 180))

out = "db/ads/laptop_ad.png"
img.save(out, "PNG")
print(f"저장 완료: {out}  ({W}x{H}px, RGBA)")
