"""
YOLO bounding box only — 모든 프레임에 bbox 표시.
"""

import cv2
import torch
from ultralytics import YOLO

import os

BASE    = "/Users/choehoseung/Desktop/AR_project"
INPUT   = f"{BASE}/input/IMG_6915.MOV"
OUTPUT  = f"{BASE}/output/output_boundingbox.mp4"
TMP_OUT = f"{BASE}/output/output_boundingbox_tmp.mp4"
MODEL   = f"{BASE}/yolo11n.pt"
CONF              = 0.70
DEVICE            = "mps" if torch.backends.mps.is_available() else "cpu"
FONT              = cv2.FONT_HERSHEY_SIMPLEX
LAPTOP_CLS        = 63
CENTER_ZONE_RATIO = 0.40


def in_center_zone(x1, y1, x2, y2, fw, fh):
    mx = fw * (1 - CENTER_ZONE_RATIO) / 2
    my = fh * (1 - CENTER_ZONE_RATIO) / 2
    cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
    return mx <= cx <= fw - mx and my <= cy <= fh - my

model = YOLO(MODEL)
names = model.names

cap = cv2.VideoCapture(INPUT)
if not cap.isOpened():
    raise RuntimeError(f"열 수 없음: {INPUT}")

fps    = cap.get(cv2.CAP_PROP_FPS)
width  = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
total  = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
print(f"입력: {INPUT}  {width}x{height}  {fps:.1f}fps  {total}frames")

fourcc = cv2.VideoWriter_fourcc(*"avc1")
writer = cv2.VideoWriter(TMP_OUT, fourcc, fps, (width, height))

frame_idx = 0
while True:
    ok, frame = cap.read()
    if not ok:
        break
    frame_idx += 1

    results = model(frame, device=DEVICE, conf=CONF, classes=[LAPTOP_CLS], imgsz=640, verbose=False)[0]

    for box in results.boxes:
        x1, y1, x2, y2 = map(int, box.xyxy[0])
        conf   = float(box.conf[0])
        cls_id = int(box.cls[0])

        if not in_center_zone(x1, y1, x2, y2, width, height):
            continue

        label = f"{names[cls_id]} {conf:.2f}"
        cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 255, 0), 2)
        (tw, th), _ = cv2.getTextSize(label, FONT, 0.55, 1)
        cv2.rectangle(frame, (x1, y1 - th - 6), (x1 + tw + 4, y1), (0, 255, 0), -1)
        cv2.putText(frame, label, (x1 + 2, y1 - 4), FONT, 0.55, (0, 0, 0), 1)

    writer.write(frame)

    if frame_idx % 50 == 0:
        print(f"  {frame_idx}/{total} ({frame_idx/total*100:.1f}%)")

cap.release()
writer.release()
os.rename(TMP_OUT, OUTPUT)
print(f"\n완료: {OUTPUT}")
