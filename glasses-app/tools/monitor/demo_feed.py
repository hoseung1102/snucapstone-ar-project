#!/usr/bin/env python3
"""더미 [MONITOR] heartbeat 피더 — 대시보드 시각화 확인용(디바이스 불필요).
   부팅→CLIP READY→SLAM 수렴→트리거→콜라 인식→MATCH/광고 spawn 시나리오를 ~3Hz 로 흘려보낸다.
   사용: python demo_feed.py | python eagle_monitor.py --demo-live
"""
import json, sys, time, math

# stdout 즉시 flush (파이프 버퍼링 회피 — 안 그러면 대시보드가 띄엄띄엄/멈춘 듯 보임).
try:
    sys.stdout.reconfigure(line_buffering=True)
except Exception:
    pass

def emit(d):
    sys.stdout.write("[MONITOR] " + json.dumps(d) + "\n")
    sys.stdout.flush()

DT = 0.35   # 빠른 데모 (~3Hz)
t = 0.0
trig = cola = match = coke = pepsi = 0
clip = "COMPILING"; clip_s = 0
slam = "SEEKING"; raw = 0
ads = []
step = 0
while True:
    step += 1
    t += DT
    # CLIP: ~4초 컴파일 → READY
    if clip == "COMPILING":
        clip_s += DT
        if clip_s >= 4: clip = "READY"; clip_s = 4
    # SLAM: step>10(약 3.5초)부터 "움직임" → 수렴
    moving = step > 10
    v = (0.12 + 0.06 * math.sin(step / 3.0)) if moving else 0.01
    w = (22 + 15 * math.sin(step / 2.0)) if moving else 1
    if moving and step > 16: slam, raw = "TRACKING", 1
    elif moving: slam, raw = "SEEKING", 0
    # 트리거/인식: READY+TRACKING 후 자주 (3스텝마다 트리거, 그 중 절반은 콜라 인식→MATCH)
    if clip == "READY" and raw == 1 and step % 3 == 0:
        trig += 1
        if step % 6 == 0:
            cola += 1
            match += 1
            if (match % 2) == 1:
                pepsi += 1; ads = (ads + [[0.10, 0.00, 1.40]])[-2:]
            else:
                coke += 1; ads = (ads + [[-0.05, 0.02, 1.38]])[-2:]
    px = 0.02 * math.sin(step / 4.0); pz = 1.45 + 0.03 * math.cos(step / 5.0)
    emit({
        "t": round(t, 1), "clip": clip, "clipS": round(clip_s),
        "trig": trig, "match": match, "cola": cola, "coke": coke, "pepsi": pepsi,
        "slam": slam, "raw": raw,
        "pos": [round(px, 2), -0.03, round(pz, 2)], "rot": [5, 178, 1],
        "v": round(v, 2), "w": round(w),
        "anchor": [0.10, 0.00, 1.40], "dist": 1.20, "drift": round(abs(px), 2), "fwdAng": 2,
        "nads": len(ads), "ads": ads,
    })
    time.sleep(DT)
