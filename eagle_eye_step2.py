"""
Eagle Eye - Step 2 Prototype
============================
Step 1 YOLO 검출 위에 IMU 트리거 시뮬레이션 추가.

기획안 Step 1 치환 (원본 → 맥북 2D):
  조건 A (헤드 모션 2.0초 안정)
    → Sparse Optical Flow: 배경 특징점(bbox 영역 제외)의 이동량 중앙값 < MOTION_THRESHOLD
      카메라가 들고 찍은 영상이면 2.0~4.0px, 고정 촬영이면 0.5~1.0px 예상. 실측 후 조정.
  조건 B (화면 중앙 40% 내 Commodity 객체 존재)
    → Step 1의 in_center_zone() 재사용

트리거 발동 시: 콘솔 출력 + 영상 플래시 + JSON 이벤트 기록.
Step 3 MobileCLIP 연결 전까지는 트리거 타이밍 검증용.

사용법:
    python eagle_eye_step2.py input.mp4 output.mp4
    python eagle_eye_step2.py input.mp4 output.mp4 --log stats.json
"""

import json
import sys
import time
from collections import defaultdict
from pathlib import Path

import cv2
import numpy as np
from ultralytics import YOLO

# Step 1 공유 상수·유틸 (yolo.py를 모듈로 재사용)
from eagle_eye_ad_db import is_class_advertised, get_ads_for_class
from yolo import (
    COMMODITY_LIST,
    CONF_THRESHOLD,
    CENTER_ZONE_RATIO,
    DEVICE,
    FONT,
    FONT_SCALE,
    FONT_THICKNESS,
    BOX_THICKNESS,
    INFER_SIZE,
    MODEL_NAME,
    in_center_zone,
)

# ============================================================
# Step 2 파라미터 (기획안 Step 1 치환값)
# ============================================================

# 기획안 조건 A 치환: 배경 특징점 이동량 중앙값 임계값 (픽셀/프레임)
MOTION_THRESHOLD = 2.0

# 기획안 조건 A: 2.0초 연속 안정 (fps × STABLE_SECONDS 프레임이 기준)
STABLE_SECONDS = 2.0

# 트리거 후 재발동 억제 쿨다운.
# 기획안 리스크 항목: AR1 Gen 1 발열 → 트리거 간 최소 30초 인터벌.
# 맥북 시뮬레이션에서는 5초로 단축해 반복 검증이 가능하게 함.
TRIGGER_COOLDOWN_SECONDS = 5.0

# Lucas-Kanade Sparse Optical Flow 파라미터
_LK_PARAMS = dict(
    winSize=(15, 15),
    maxLevel=2,
    criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 10, 0.03),
)
_FEATURE_PARAMS = dict(
    maxCorners=200,
    qualityLevel=0.3,
    minDistance=7,
    blockSize=7,
)


# ============================================================
# Optical Flow 유틸
# ============================================================

def make_bg_mask(detections: list[dict], frame_w: int, frame_h: int) -> np.ndarray:
    """
    검출된 bbox 영역을 제외한 배경 마스크 반환.
    객체 자체의 움직임이 카메라 안정 판단을 오염시키지 않게 하기 위해 제외.
    """
    mask = np.full((frame_h, frame_w), 255, dtype=np.uint8)
    for det in detections:
        x1, y1, x2, y2 = det["bbox"]
        pad = 10  # bbox 경계 노이즈 완충
        mask[max(0, y1 - pad):min(frame_h, y2 + pad),
             max(0, x1 - pad):min(frame_w, x2 + pad)] = 0
    return mask


def compute_global_motion(
    prev_gray: np.ndarray,
    curr_gray: np.ndarray,
    bg_mask: np.ndarray,
) -> float:
    """
    Sparse Optical Flow로 배경 영역의 전역 카메라 이동량(픽셀/프레임) 반환.
    기획안 조건 A의 'IMU 헤드 모션 델타' 2D 치환.
    중앙값을 사용해 이상 추적점에 강건하게 처리.
    """
    pts = cv2.goodFeaturesToTrack(prev_gray, mask=bg_mask, **_FEATURE_PARAMS)
    if pts is None or len(pts) == 0:
        return 0.0

    next_pts, status, _ = cv2.calcOpticalFlowPyrLK(
        prev_gray, curr_gray, pts, None, **_LK_PARAMS
    )
    if next_pts is None:
        return 0.0

    valid = status.ravel() == 1
    if valid.sum() < 4:  # 추적점 부족 시 신뢰 불가
        return 0.0

    displacements = np.linalg.norm(
        next_pts[valid] - pts[valid], axis=2
    ).ravel()
    return float(np.median(displacements))


# ============================================================
# 시각화 헬퍼
# ============================================================

def _draw_overlays(
    frame: np.ndarray,
    frame_idx: int,
    total_frames: int,
    motion: float,
    stable_count: int,
    stable_frames_needed: int,
    cooldown_count: int,
    cooldown_frames: int,
    last_detections: list[dict],  # 마지막 YOLO 결과 (매 프레임 갱신 안 됨)
    yolo_ran: bool,
    triggered: bool,
    width: int,
    height: int,
) -> None:
    # 중앙 40% 경계선 (기획안 조건 B 영역 시각화)
    mx = int(width * (1 - CENTER_ZONE_RATIO) / 2)
    my = int(height * (1 - CENTER_ZONE_RATIO) / 2)
    cv2.rectangle(frame, (mx, my), (width - mx, height - my), (200, 200, 200), 1)

    # 마지막 YOLO 결과 bbox 표시 (YOLO가 돌았을 때만 갱신됨)
    for det in last_detections:
        x1, y1, x2, y2 = det["bbox"]
        color = (0, 255, 0) if det["in_center_zone"] else (0, 180, 255)
        cv2.rectangle(frame, (x1, y1), (x2, y2), color, BOX_THICKNESS)
        label = f"{det['class']} {det['conf']:.2f}"
        (tw, th), _ = cv2.getTextSize(label, FONT, FONT_SCALE, FONT_THICKNESS)
        cv2.rectangle(frame, (x1, y1 - th - 8), (x1 + tw + 4, y1), color, -1)
        cv2.putText(frame, label, (x1 + 2, y1 - 4),
                    FONT, FONT_SCALE, (0, 0, 0), FONT_THICKNESS)

    # HUD (좌상단)
    hud_lines = [
        f"Frame {frame_idx}/{total_frames}",
        f"Motion: {motion:.2f}px  ({'stable' if motion < MOTION_THRESHOLD else 'moving'})",
        f"YOLO: {'FIRED' if yolo_ran else 'waiting'}",
    ]
    for i, line in enumerate(hud_lines):
        cv2.putText(frame, line, (10, 30 + i * 22),
                    FONT, 0.55, (255, 255, 255), 2)

    # 안정도/쿨다운 바 (우상단)
    bx, by, bw, bh = width - 170, 10, 150, 16
    cv2.rectangle(frame, (bx, by), (bx + bw, by + bh), (60, 60, 60), -1)

    if cooldown_count > 0:
        fill = int(bw * (1 - cooldown_count / max(cooldown_frames, 1)))
        cv2.rectangle(frame, (bx, by), (bx + fill, by + bh), (200, 100, 50), -1)
        bar_label = f"COOLDOWN {cooldown_count}f"
    else:
        ratio = min(stable_count / max(stable_frames_needed, 1), 1.0)
        fill = int(bw * ratio)
        color = (0, int(100 + 155 * ratio), 0)
        cv2.rectangle(frame, (bx, by), (bx + fill, by + bh), color, -1)
        bar_label = f"STABLE {stable_count}/{stable_frames_needed}"

    cv2.rectangle(frame, (bx, by), (bx + bw, by + bh), (180, 180, 180), 1)
    cv2.putText(frame, bar_label, (bx, by + bh + 14),
                FONT, 0.42, (220, 220, 220), 1)

    # 트리거 플래시 (반투명 빨간 오버레이 + 텍스트)
    if triggered:
        overlay = frame.copy()
        cv2.rectangle(overlay, (0, 0), (width, height), (0, 0, 220), -1)
        cv2.addWeighted(overlay, 0.28, frame, 0.72, 0, frame)
        cv2.putText(frame, "TRIGGER", (width // 2 - 100, height // 2),
                    FONT, 2.2, (255, 255, 255), 5)


# ============================================================
# 메인 파이프라인
# ============================================================

def annotate_video(input_path: str, output_path: str,
                   log_path: str | None = None) -> None:
    model = YOLO(MODEL_NAME)

    cap = cv2.VideoCapture(input_path)
    if not cap.isOpened():
        raise RuntimeError(f"동영상을 열 수 없음: {input_path}")

    fps = cap.get(cv2.CAP_PROP_FPS)
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    stable_frames_needed = max(1, int(fps * STABLE_SECONDS))
    cooldown_frames = max(1, int(fps * TRIGGER_COOLDOWN_SECONDS))

    fourcc = cv2.VideoWriter_fourcc(*"avc1")
    writer = cv2.VideoWriter(output_path, fourcc, fps, (width, height))
    if not writer.isOpened():
        writer = cv2.VideoWriter(
            output_path, cv2.VideoWriter_fourcc(*"mp4v"), fps, (width, height)
        )

    stats = {
        "frames_total": 0,
        "yolo_runs": 0,           # YOLO가 실제로 실행된 횟수
        "detections_total": 0,
        "trigger_count": 0,
        "confidences": [],
        "per_class_count": defaultdict(int),
        "motion_values": [],
    }
    frame_log: list[dict] = []
    trigger_events: list[dict] = []

    prev_gray: np.ndarray | None = None
    stable_count = 0
    cooldown_count = 0
    last_detections: list[dict] = []  # 마지막 YOLO 결과 (시각화용으로 유지)

    frame_idx = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        frame_idx += 1
        stats["frames_total"] += 1

        curr_gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)

        # --- Optical Flow: 매 프레임 실행 (IMU 대체, 경량) ---
        bg_mask = make_bg_mask(last_detections, width, height)
        motion = (
            compute_global_motion(prev_gray, curr_gray, bg_mask)
            if prev_gray is not None else 0.0
        )
        stats["motion_values"].append(round(motion, 3))

        triggered_this_frame = False
        yolo_ran = False
        frame_detections: list[dict] = []

        if cooldown_count > 0:
            cooldown_count -= 1
            stable_count = 0
        else:
            # 조건 A: 카메라 이동량 < 임계값 → stable_count 누적
            if motion < MOTION_THRESHOLD:
                stable_count += 1
            else:
                stable_count = 0

            # 조건 A가 STABLE_SECONDS 동안 유지됐을 때만 YOLO 실행 (단발)
            if stable_count >= stable_frames_needed:
                stable_count = 0  # 결과와 무관하게 리셋 (재누적 필요)
                yolo_ran = True
                stats["yolo_runs"] += 1

                results = model(
                    frame,
                    device=DEVICE,
                    conf=CONF_THRESHOLD,
                    classes=list(COMMODITY_LIST.keys()),
                    imgsz=INFER_SIZE,
                    verbose=False,
                )[0]

                for box in results.boxes:
                    cls_id = int(box.cls[0])
                    conf = float(box.conf[0])
                    if cls_id not in COMMODITY_LIST:
                        continue

                    stats["detections_total"] += 1
                    stats["confidences"].append(conf)
                    stats["per_class_count"][COMMODITY_LIST[cls_id]] += 1

                    x1, y1, x2, y2 = map(int, box.xyxy[0])
                    ad_candidate = is_class_advertised(cls_id)
                    frame_detections.append({
                        "class": COMMODITY_LIST[cls_id],
                        "class_id": cls_id,
                        "conf": round(conf, 4),
                        "bbox": [x1, y1, x2, y2],
                        "in_center_zone": in_center_zone(x1, y1, x2, y2, width, height),
                        "ad_candidate": ad_candidate,
                        "matched_ad_ids": [a["ad_id"] for a in get_ads_for_class(cls_id)]
                                          if ad_candidate else [],
                    })

                last_detections = frame_detections

                # 조건 B: 중앙 40% 내 Ad DB 등록 객체 존재 → TRIGGER
                cond_b = any(
                    d["in_center_zone"] and d["ad_candidate"]
                    for d in frame_detections
                )
                if cond_b:
                    triggered_this_frame = True
                    stats["trigger_count"] += 1
                    cooldown_count = cooldown_frames
                    t_sec = round(frame_idx / fps, 2)
                    trigger_events.append({
                        "frame": frame_idx,
                        "time_sec": t_sec,
                        "motion_px": round(motion, 3),
                        "detections": frame_detections,
                    })
                    print(f"  [TRIGGER #{stats['trigger_count']}]  "
                          f"frame={frame_idx}  t={t_sec}s")

        frame_log.append({
            "frame": frame_idx,
            "motion_px": round(motion, 3),
            "stable_count": stable_count,
            "yolo_ran": yolo_ran,
            "triggered": triggered_this_frame,
            "detections": frame_detections,
        })

        _draw_overlays(
            frame, frame_idx, total_frames, motion,
            stable_count, stable_frames_needed,
            cooldown_count, cooldown_frames,
            last_detections, yolo_ran, triggered_this_frame,
            width, height,
        )

        writer.write(frame)

        if frame_idx % 100 == 0:
            pct = frame_idx / total_frames * 100 if total_frames else 0
            print(f"  진행: {frame_idx}/{total_frames} ({pct:.1f}%)")

        prev_gray = curr_gray

    cap.release()
    writer.release()

    _print_stats(stats, fps, stable_frames_needed)

    if log_path:
        _save_log(stats, frame_log, trigger_events,
                  log_path, input_path, fps, stable_frames_needed)


# ============================================================
# 통계 출력 / JSON 저장
# ============================================================

def _print_stats(stats: dict, fps: float, stable_frames_needed: int) -> None:
    print("\n" + "=" * 55)
    print("실측 통계 (Step 2: Sparse Optical Flow 트리거 시뮬레이션)")
    print("=" * 55)

    n = stats["frames_total"]
    print(f"총 프레임: {n}  ({n / fps:.1f}초)")
    print(f"YOLO 실행 횟수: {stats['yolo_runs']}  "
          f"(전체 프레임의 {stats['yolo_runs'] / n * 100:.1f}%)")
    print(f"총 검출 수: {stats['detections_total']}")
    print(f"트리거 발동 횟수: {stats['trigger_count']}")

    if stats["motion_values"]:
        mv = stats["motion_values"]
        stable_ratio = sum(1 for v in mv if v < MOTION_THRESHOLD) / len(mv) * 100
        print(f"\n카메라 이동량 (Optical Flow 중앙값, px/frame):")
        print(f"  평균: {sum(mv)/len(mv):.2f}  최대: {max(mv):.2f}")
        print(f"  MOTION_THRESHOLD={MOTION_THRESHOLD} 이하 비율: {stable_ratio:.1f}%")


def _save_log(
    stats: dict,
    frame_log: list,
    trigger_events: list,
    log_path: str,
    input_path: str,
    fps: float,
    stable_frames_needed: int,
) -> None:
    confs = stats["confidences"]
    mv = stats["motion_values"]
    n = stats["frames_total"]

    output = {
        "meta": {
            "input": input_path,
            "model": MODEL_NAME,
            "infer_size": INFER_SIZE,
            "conf_threshold": CONF_THRESHOLD,
            "center_zone_ratio": CENTER_ZONE_RATIO,
            "motion_threshold": MOTION_THRESHOLD,
            "stable_seconds": STABLE_SECONDS,
            "stable_frames_needed": stable_frames_needed,
            "trigger_cooldown_seconds": TRIGGER_COOLDOWN_SECONDS,
            "fps": fps,
        },
        "summary": {
            "frames_total": n,
            "yolo_runs": stats["yolo_runs"],
            "yolo_run_rate": round(stats["yolo_runs"] / n, 4) if n else 0,
            "detections_total": stats["detections_total"],
            "trigger_count": stats["trigger_count"],
            "conf_mean": round(sum(confs) / len(confs), 4) if confs else None,
            "motion_mean_px": round(sum(mv) / len(mv), 3) if mv else None,
            "motion_max_px": round(max(mv), 3) if mv else None,
            "per_class_count": dict(stats["per_class_count"]),
        },
        "trigger_events": trigger_events,
        "frames": frame_log,
    }

    Path(log_path).write_text(json.dumps(output, ensure_ascii=False, indent=2))
    print(f"\nJSON 로그 저장: {log_path}")


# ============================================================
# 진입점
# ============================================================

if __name__ == "__main__":
    if len(sys.argv) not in (3, 5):
        print("Usage: python eagle_eye_step2.py <input.mp4> <output.mp4> [--log stats.json]")
        sys.exit(1)

    _log = None
    if len(sys.argv) == 5 and sys.argv[3] == "--log":
        _log = sys.argv[4]

    annotate_video(sys.argv[1], sys.argv[2], _log)
    print(f"\n완료: {sys.argv[2]}")
