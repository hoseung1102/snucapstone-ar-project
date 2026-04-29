"""
Eagle Eye - Prototype (Steps 1-4 통합)
======================================
동영상 입력 → YOLO 검출 → 시선 트리거 → CLIP 식별 → 광고 오버레이 출력

기획안 맥북 치환:
- Step 1 IMU 안정  → bbox IoU 2초 안정
- Step 4 Spatial Anchor → 2D 고정 좌표 오버레이 + Anchor Lifecycle 상태머신

사용법:
    python yolo.py input.mp4 output.mp4
    python yolo.py input.mp4 output.mp4 --log stats.json
"""

import json
import sys
import time
import warnings
import numpy as np
from collections import defaultdict
from enum import Enum
from pathlib import Path

import cv2
import open_clip
import torch
from PIL import Image
from ultralytics import YOLO

warnings.filterwarnings("ignore")

# ============================================================
# 설정
# ============================================================

COMMODITY_LIST = {63: "laptop", 67: "cell phone"}

CONF_THRESHOLD    = 0.30
DEVICE            = "mps" if torch.backends.mps.is_available() else "cpu"
MODEL_NAME        = "yolo11n.pt"
INFER_SIZE        = 640
CENTER_ZONE_RATIO = 0.40

FONT_SCALE     = 0.6
FONT_THICKNESS = 2

# 시선 트리거 (기획안 Step 1 맥북 치환)
# Optical Flow로 카메라 안정 감지 → YOLO 단발 실행
STABLE_SECONDS    = 2.0    # 안정 유지 시간 (기획안 헤드 모션 2초)
MOTION_THRESHOLD  = 14.0   # 배경 특징점 이동 중앙값 임계값 (px/frame)

# Anchor Lifecycle (기획안 Step 4)
ANCHOR_ACTIVE_SEC  = 4.0   # 광고 표시 유지 시간
ANCHOR_FADEIN_SEC  = 0.5   # fade-in 길이
ANCHOR_FADEOUT_SEC = 0.5   # fade-out 길이
ANCHOR_OPACITY     = 0.75  # 최대 불투명도 (1.0 = 완전 불투명)

# CLIP (기획안 Step 3 맥북 치환: open_clip ViT-B-32)
CLIP_MODEL_NAME  = "ViT-B-32"
CLIP_PRETRAINED  = "openai"
CLIP_SIM_THRESH  = 0.15    # 코사인 유사도 최소 임계값

METADATA_PATH = Path("db/metadata.json")

FONT           = cv2.FONT_HERSHEY_SIMPLEX
BOX_THICKNESS  = 2


# ============================================================
# Anchor 상태머신
# ============================================================

class AnchorState(Enum):
    IDLE     = "IDLE"
    ACTIVE   = "ACTIVE"
    FADE_OUT = "FADE_OUT"


# ============================================================
# 헬퍼 함수
# ============================================================

def in_center_zone(x1, y1, x2, y2, fw, fh):
    mx = fw * (1 - CENTER_ZONE_RATIO) / 2
    my = fh * (1 - CENTER_ZONE_RATIO) / 2
    cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
    return mx <= cx <= fw - mx and my <= cy <= fh - my


# ── Optical Flow (기획안 Step 1 IMU 치환) ──────────────────────

_LK_PARAMS = dict(
    winSize=(15, 15), maxLevel=2,
    criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 10, 0.03),
)
_FEATURE_PARAMS = dict(maxCorners=200, qualityLevel=0.3, minDistance=7, blockSize=7)


def make_bg_mask(detections: list[dict], fw: int, fh: int) -> np.ndarray:
    """bbox 영역을 제외한 배경 마스크. 객체 자체 움직임이 안정성 판단을 오염시키지 않게."""
    mask = np.full((fh, fw), 255, dtype=np.uint8)
    for det in detections:
        x1, y1, x2, y2 = det["bbox"]
        pad = 10
        mask[max(0, y1-pad):min(fh, y2+pad), max(0, x1-pad):min(fw, x2+pad)] = 0
    return mask


def compute_global_motion(prev_gray: np.ndarray, curr_gray: np.ndarray,
                           bg_mask: np.ndarray) -> float:
    """Sparse Optical Flow로 배경 특징점 이동량 중앙값 반환 (px/frame)."""
    pts = cv2.goodFeaturesToTrack(prev_gray, mask=bg_mask, **_FEATURE_PARAMS)
    if pts is None or len(pts) == 0:
        return 0.0
    next_pts, status, _ = cv2.calcOpticalFlowPyrLK(prev_gray, curr_gray, pts, None, **_LK_PARAMS)
    if next_pts is None:
        return 0.0
    valid = status.ravel() == 1
    if valid.sum() < 4:
        return 0.0
    return float(np.median(np.linalg.norm(next_pts[valid] - pts[valid], axis=2).ravel()))


def overlay_ad(frame, ad_bgra, x, y, alpha_mul=1.0):
    """RGBA 광고 이미지를 frame의 (x, y) 위치에 alpha blend."""
    ah, aw = ad_bgra.shape[:2]
    fw, fh = frame.shape[1], frame.shape[0]

    # 프레임 경계 clamp
    x = max(0, min(x, fw - aw))
    y = max(0, min(y, fh - ah))
    x2, y2 = x + aw, y + ah

    roi = frame[y:y2, x:x2].astype(np.float32)
    ad_rgb  = ad_bgra[:, :, :3].astype(np.float32)
    ad_alpha = (ad_bgra[:, :, 3:4].astype(np.float32) / 255.0) * alpha_mul

    blended = roi * (1 - ad_alpha) + ad_rgb * ad_alpha
    frame[y:y2, x:x2] = blended.astype(np.uint8)


def clip_identify(crop_bgr, clip_model, clip_preprocess, db_entries, clip_device):
    """
    YOLO crop → CLIP 임베딩 → Vector DB 코사인 유사도 검색.
    최고 유사도 항목 반환. 임계값 미달 시 None.
    """
    pil_img = Image.fromarray(cv2.cvtColor(crop_bgr, cv2.COLOR_BGR2RGB))
    tensor = clip_preprocess(pil_img).unsqueeze(0).to(clip_device)
    with torch.no_grad():
        emb = clip_model.encode_image(tensor)
        emb = emb / emb.norm(dim=-1, keepdim=True)
    emb_np = emb.cpu().float().numpy()

    best_sim, best_entry = -1.0, None
    for entry in db_entries:
        ref = np.load(entry["embedding"])  # (1, 512)
        sim = float((emb_np * ref).sum())
        if sim > best_sim:
            best_sim, best_entry = sim, entry

    if best_sim >= CLIP_SIM_THRESH:
        return best_entry, best_sim
    return None, best_sim


# ============================================================
# 메인 파이프라인
# ============================================================

def annotate_video(input_path: str, output_path: str,
                   log_path: str | None = None) -> None:

    # --- 모델 로드 ---
    yolo_model = YOLO(MODEL_NAME)
    print(f"CLIP 모델 로드 중: {CLIP_MODEL_NAME} / {CLIP_PRETRAINED} → {DEVICE}")
    clip_model, _, clip_preprocess = open_clip.create_model_and_transforms(
        CLIP_MODEL_NAME, pretrained=CLIP_PRETRAINED
    )
    clip_model = clip_model.to(DEVICE).eval()

    # --- Vector DB 로드 ---
    db_entries = json.loads(METADATA_PATH.read_text()) if METADATA_PATH.exists() else []
    print(f"Vector DB: {len(db_entries)}개 상품 로드")

    # --- 광고 이미지 캐시 ---
    ad_cache: dict[str, np.ndarray] = {}
    for entry in db_entries:
        p = entry["ad_image"]
        img = cv2.imread(p, cv2.IMREAD_UNCHANGED)
        if img is None:
            continue
        if img.shape[2] == 3:                      # BGR → BGRA
            img = cv2.cvtColor(img, cv2.COLOR_BGR2BGRA)
        # 광고 패널 크기 고정
        img = cv2.resize(img, (380, 240))
        ad_cache[p] = img

    # --- 영상 I/O ---
    cap = cv2.VideoCapture(input_path)
    if not cap.isOpened():
        raise RuntimeError(f"동영상을 열 수 없음: {input_path}")

    fps        = cap.get(cv2.CAP_PROP_FPS)
    width      = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height     = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    fourcc = cv2.VideoWriter_fourcc(*"avc1")
    writer = cv2.VideoWriter(output_path, fourcc, fps, (width, height))
    if not writer.isOpened():
        writer = cv2.VideoWriter(output_path, cv2.VideoWriter_fourcc(*"mp4v"),
                                 fps, (width, height))

    # --- 통계 ---
    stats = {
        "frames_total": 0,
        "frames_with_detection": 0,
        "detections_total": 0,
        "confidences": [],
        "per_class_count": defaultdict(int),
        "inference_times_ms": [],
    }
    frame_log: list[dict] = []

    # --- 시선 트리거 상태 (Optical Flow 기반) ---
    stable_frames_needed = max(1, int(fps * STABLE_SECONDS))
    stable_count  = 0
    prev_gray     = None
    last_detections: list[dict] = []   # 마지막 YOLO 결과 (bg_mask·시각화용)
    trigger_fired = False

    # --- Anchor 상태머신 ---
    anchor_state      = AnchorState.IDLE
    anchor_start_frame = 0
    anchor_bbox       = None   # 트리거 시점 bbox (2D 고정)
    anchor_ad_img     = None   # 표시할 광고 이미지 (BGRA)
    anchor_ad_label   = ""
    anchor_active_frames  = int(fps * ANCHOR_ACTIVE_SEC)
    anchor_fadein_frames  = max(1, int(fps * ANCHOR_FADEIN_SEC))
    anchor_fadeout_frames = max(1, int(fps * ANCHOR_FADEOUT_SEC))

    frame_idx = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        frame_idx += 1
        stats["frames_total"] += 1

        curr_gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        frame_detections: list[dict] = []

        # ── Optical Flow: 매 프레임 실행 (IMU 안정 감지 대체, 경량) ──
        bg_mask = make_bg_mask(last_detections, width, height)
        motion = (compute_global_motion(prev_gray, curr_gray, bg_mask)
                  if prev_gray is not None else 0.0)

        if anchor_state == AnchorState.IDLE:
            if motion < MOTION_THRESHOLD:
                stable_count += 1
            else:
                stable_count = 0
                trigger_fired = False

            # 안정 조건 충족 → YOLO 단발 실행 (기획안 Step 2)
            if stable_count >= stable_frames_needed and not trigger_fired:
                stable_count = 0
                results = yolo_model(
                    frame,
                    device=DEVICE,
                    conf=CONF_THRESHOLD,
                    classes=list(COMMODITY_LIST.keys()),
                    imgsz=INFER_SIZE,
                    verbose=False,
                )[0]
                stats["inference_times_ms"].append(0.0)

                primary_bbox = None
                for box in results.boxes:
                    cls_id = int(box.cls[0])
                    conf   = float(box.conf[0])
                    if cls_id not in COMMODITY_LIST:
                        continue
                    stats["detections_total"] += 1
                    stats["confidences"].append(conf)
                    stats["per_class_count"][COMMODITY_LIST[cls_id]] += 1
                    x1, y1, x2, y2 = map(int, box.xyxy[0])
                    is_center = in_center_zone(x1, y1, x2, y2, width, height)
                    frame_detections.append({
                        "class": COMMODITY_LIST[cls_id],
                        "conf": round(conf, 4),
                        "bbox": [x1, y1, x2, y2],
                        "in_center_zone": is_center,
                    })
                    if primary_bbox is None or conf > primary_bbox[1]:
                        primary_bbox = ([x1, y1, x2, y2], conf, is_center)

                last_detections = frame_detections
                if frame_detections:
                    stats["frames_with_detection"] += 1

                # 조건 B: 중앙 40% 내 객체 존재 → CLIP 호출
                if primary_bbox is not None and primary_bbox[2] and not trigger_fired:
                    trigger_fired = True
                    x1, y1, x2, y2 = primary_bbox[0]
                    crop = frame[max(0,y1):min(height,y2), max(0,x1):min(width,x2)]

                    # ── CLIP 식별 (기획안 Step 3) ────────────────
                    matched_entry, sim = clip_identify(
                        crop, clip_model, clip_preprocess, db_entries, DEVICE
                    )
                    print(f"  [CLIP] frame={frame_idx}  sim={sim:.3f}  "
                          f"→ {matched_entry['name'] if matched_entry else 'no match'}")

                    if matched_entry:
                        ad_path = matched_entry["ad_image"]
                        anchor_ad_img      = ad_cache.get(ad_path)
                        anchor_ad_label    = matched_entry.get("ad_label", matched_entry["name"])
                        anchor_bbox        = primary_bbox[0][:]
                        anchor_state       = AnchorState.ACTIVE
                        anchor_start_frame = frame_idx

        # ── Anchor Lifecycle 드로잉 (기획안 Step 4 맥북 치환) ─
        if anchor_state == AnchorState.ACTIVE:
            elapsed = frame_idx - anchor_start_frame

            alpha = elapsed / anchor_fadein_frames if elapsed < anchor_fadein_frames else 1.0

            # 광고 패널: 트리거 시점 위치 고정 (world-lock 시뮬레이션)
            # 오브젝트가 이동해도 광고는 anchor_bbox 기준 좌표에 유지됨
            if anchor_ad_img is not None:
                bx1, by1, bx2, by2 = anchor_bbox
                aw = anchor_ad_img.shape[1]
                ad_x = bx2 + 10 if bx2 + 10 + aw <= width else bx1 - aw - 10
                ad_y = max(0, by1)
                overlay_ad(frame, anchor_ad_img, ad_x, ad_y, alpha_mul=alpha * ANCHOR_OPACITY)

            if elapsed >= anchor_active_frames:
                anchor_state = AnchorState.FADE_OUT
                anchor_start_frame = frame_idx

        elif anchor_state == AnchorState.FADE_OUT:
            elapsed = frame_idx - anchor_start_frame
            alpha = max(0.0, 1.0 - elapsed / anchor_fadeout_frames)

            if anchor_ad_img is not None:
                bx1, by1, bx2, by2 = anchor_bbox
                aw = anchor_ad_img.shape[1]
                ad_x = bx2 + 10 if bx2 + 10 + aw <= width else bx1 - aw - 10
                ad_y = max(0, by1)
                overlay_ad(frame, anchor_ad_img, ad_x, ad_y, alpha_mul=alpha * ANCHOR_OPACITY)

            if elapsed >= anchor_fadeout_frames:
                anchor_state  = AnchorState.IDLE
                anchor_bbox   = None
                anchor_ad_img = None
                stable_count  = 0
                trigger_fired = False

        # ── HUD ───────────────────────────────────────────────
        if anchor_state == AnchorState.IDLE:
            stable_sec = stable_count / fps
            status = f"Gaze: {stable_sec:.1f}s / {STABLE_SECONDS:.1f}s  motion:{motion:.1f}px"
        else:
            status = f"[{anchor_state.value}]"

        hud = f"Frame {frame_idx}/{total_frames}  {status}"
        cv2.putText(frame, hud, (10, 30), FONT, 0.6, (255, 255, 255), 2)

        frame_log.append({
            "frame": frame_idx,
            "motion_px": round(motion, 3),
            "stable_count": stable_count,
            "anchor_state": anchor_state.value,
            "detections": frame_detections,
        })

        prev_gray = curr_gray
        writer.write(frame)

        if frame_idx % 100 == 0:
            pct = frame_idx / total_frames * 100 if total_frames else 0
            print(f"  진행: {frame_idx}/{total_frames} ({pct:.1f}%)")

    cap.release()
    writer.release()
    print_stats(stats, fps)
    if log_path:
        save_log(stats, frame_log, log_path, input_path, fps)


# ============================================================
# 통계 출력 / 저장
# ============================================================

def print_stats(stats: dict, fps: float) -> None:
    print("\n" + "=" * 50)
    n = stats["frames_total"]
    print(f"총 프레임: {n}")
    print(f"검출 발생 프레임: {stats['frames_with_detection']} ({stats['frames_with_detection']/n*100:.1f}%)")
    print(f"총 검출 수: {stats['detections_total']}")
    if stats["confidences"]:
        confs = stats["confidences"]
        print(f"\nConfidence — min:{min(confs):.3f}  max:{max(confs):.3f}  mean:{sum(confs)/len(confs):.3f}")
    if stats["inference_times_ms"]:
        times = stats["inference_times_ms"]
        print(f"YOLO 레이턴시 — 평균:{sum(times)/len(times):.1f}ms  max:{max(times):.1f}ms")


def save_log(stats, frame_log, log_path, input_path, fps):
    confs = stats["confidences"]
    times = stats["inference_times_ms"]
    n     = stats["frames_total"]
    out = {
        "meta": {"input": input_path, "model": MODEL_NAME,
                 "infer_size": INFER_SIZE, "conf_threshold": CONF_THRESHOLD,
                 "center_zone_ratio": CENTER_ZONE_RATIO, "fps": fps},
        "summary": {
            "frames_total": n,
            "frames_with_detection": stats["frames_with_detection"],
            "detection_rate": round(stats["frames_with_detection"] / n, 4) if n else 0,
            "detections_total": stats["detections_total"],
            "conf_min":  round(min(confs), 4) if confs else None,
            "conf_max":  round(max(confs), 4) if confs else None,
            "conf_mean": round(sum(confs)/len(confs), 4) if confs else None,
            "latency_mean_ms": round(sum(times)/len(times), 2) if times else None,
            "latency_max_ms":  round(max(times), 2) if times else None,
            "per_class_count": dict(stats["per_class_count"]),
        },
        "frames": frame_log,
    }
    Path(log_path).write_text(json.dumps(out, ensure_ascii=False, indent=2))
    print(f"JSON 로그 저장: {log_path}")


# ============================================================
# 진입점
# ============================================================

if __name__ == "__main__":
    if len(sys.argv) not in (3, 5):
        print("Usage: python yolo.py <input.mp4> <output.mp4> [--log stats.json]")
        sys.exit(1)
    log_path = sys.argv[4] if len(sys.argv) == 5 and sys.argv[3] == "--log" else None
    annotate_video(sys.argv[1], sys.argv[2], log_path)
    print(f"\n완료: {sys.argv[2]}")
