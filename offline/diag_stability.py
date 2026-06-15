"""
Eagle Eye - Stability Diagnostic
=================================
Optical Flow만 실행해서 stable_count 누적 흐름을 시각화.
YOLO/CLIP 없이 순수하게 "2초 안정 달성이 얼마나 어려운가" 확인용.

사용법:
    python diag_stability.py input/IMG_6915.MOV output/diag_stability.mp4
"""

import sys
import cv2
import numpy as np

MOTION_THRESHOLD    = 10.0  # yolo.py와 동일
STABLE_SECONDS      = 2.0
CENTER_ZONE_RATIO   = 0.40

_LK_PARAMS = dict(
    winSize=(15, 15), maxLevel=2,
    criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 10, 0.03),
)
_FEATURE_PARAMS = dict(maxCorners=200, qualityLevel=0.3, minDistance=7, blockSize=7)

FONT = cv2.FONT_HERSHEY_SIMPLEX


def compute_motion(prev_gray, curr_gray):
    pts = cv2.goodFeaturesToTrack(prev_gray, **_FEATURE_PARAMS)
    if pts is None or len(pts) == 0:
        return 0.0
    next_pts, status, _ = cv2.calcOpticalFlowPyrLK(prev_gray, curr_gray, pts, None, **_LK_PARAMS)
    if next_pts is None:
        return 0.0
    valid = status.ravel() == 1
    if valid.sum() < 4:
        return 0.0
    return float(np.median(np.linalg.norm(next_pts[valid] - pts[valid], axis=2).ravel()))


def draw_frame(frame, frame_idx, total_frames, fps, motion, stable_count,
               stable_frames_needed, motion_history, width, height):

    stable_sec = stable_count / fps
    is_stable  = motion < MOTION_THRESHOLD

    # ── 좌상단 HUD ────────────────────────────────────────────
    bar_color = (0, 220, 0) if is_stable else (50, 50, 200)
    lines = [
        f"Frame {frame_idx}/{total_frames}  ({frame_idx/fps:.1f}s)",
        f"Motion: {motion:.2f} px/f  (thr {MOTION_THRESHOLD})  "
          + ("STABLE" if is_stable else "MOVING"),
        f"Gaze stable: {stable_sec:.1f}s / {STABLE_SECONDS:.1f}s",
    ]
    for i, line in enumerate(lines):
        cv2.putText(frame, line, (12, 34 + i * 26), FONT, 0.65, (0,0,0), 4)
        cv2.putText(frame, line, (12, 34 + i * 26), FONT, 0.65, (255,255,255), 2)

    # ── 안정도 바 (우상단) ────────────────────────────────────
    bx, by, bw, bh = width - 220, 14, 200, 22
    cv2.rectangle(frame, (bx, by), (bx + bw, by + bh), (40, 40, 40), -1)
    fill = int(bw * min(stable_count / max(stable_frames_needed, 1), 1.0))
    cv2.rectangle(frame, (bx, by), (bx + fill, by + bh), bar_color, -1)
    cv2.rectangle(frame, (bx, by), (bx + bw, by + bh), (200, 200, 200), 1)
    label = f"{stable_sec:.1f}s / {STABLE_SECONDS:.1f}s"
    cv2.putText(frame, label, (bx + 4, by + bh - 5), FONT, 0.5, (255, 255, 255), 1)



def run(input_path, output_path):
    cap = cv2.VideoCapture(input_path)
    fps          = cap.get(cv2.CAP_PROP_FPS)
    width        = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height       = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    stable_frames_needed = max(1, int(fps * STABLE_SECONDS))
    print(f"fps={fps:.0f}  frames={total_frames}  ({total_frames/fps:.1f}s)")
    print(f"stable_frames_needed={stable_frames_needed}  ({STABLE_SECONDS}초)")
    print(f"MOTION_THRESHOLD={MOTION_THRESHOLD} px/frame")

    fourcc = cv2.VideoWriter_fourcc(*"avc1")
    writer = cv2.VideoWriter(output_path, fourcc, fps, (width, height))
    if not writer.isOpened():
        writer = cv2.VideoWriter(output_path, cv2.VideoWriter_fourcc(*"mp4v"),
                                 fps, (width, height))

    prev_gray      = None
    stable_count   = 0
    motion_history = []
    lock_events    = []   # (frame_idx, time_sec, stable_count_at_lock)

    frame_idx = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        frame_idx += 1

        curr_gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        motion    = compute_motion(prev_gray, curr_gray) if prev_gray is not None else 0.0
        motion_history.append(round(motion, 3))

        if motion < MOTION_THRESHOLD:
            stable_count += 1
        else:
            stable_count = 0

        if stable_count == stable_frames_needed:
            t = frame_idx / fps
            lock_events.append((frame_idx, round(t, 2)))
            print(f"  [GAZE LOCK] frame={frame_idx}  t={t:.2f}s")

        draw_frame(frame, frame_idx, total_frames, fps, motion,
                   stable_count, stable_frames_needed,
                   motion_history, width, height)

        writer.write(frame)
        prev_gray = curr_gray

    cap.release()
    writer.release()

    # ── 요약 출력 ─────────────────────────────────────────────
    print(f"\n=== 안정도 분석 결과 ===")
    stable_frames_count = sum(1 for m in motion_history if m < MOTION_THRESHOLD)
    print(f"전체 {len(motion_history)}프레임 중 motion<{MOTION_THRESHOLD}: "
          f"{stable_frames_count}프레임 ({stable_frames_count/len(motion_history)*100:.1f}%)")
    print(f"mean motion: {sum(motion_history)/len(motion_history):.2f} px/f  "
          f"max: {max(motion_history):.2f} px/f")
    print(f"2초 누적 달성 횟수: {len(lock_events)}")
    for fi, t in lock_events:
        print(f"  t={t:.2f}s (frame {fi})")
    print(f"\n저장: {output_path}")


if __name__ == "__main__":
    inp = sys.argv[1] if len(sys.argv) > 1 else "input/IMG_6915.MOV"
    out = sys.argv[2] if len(sys.argv) > 2 else "output/diag_stability.mp4"
    run(inp, out)
