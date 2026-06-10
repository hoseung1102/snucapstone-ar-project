---
name: eagle-monitor
description: >
  Eagle Eye AR-glasses 앱의 host-side 라이브 디버그 대시보드를 새 터미널 창에서 띄운다.
  디바이스 logcat(tag Unity)의 [MONITOR] heartbeat JSON을 파싱해 CLIP 상태,
  TRIGGER→COLA→MATCH→(COKE/PEPSI) 퍼널, SLAM 센서, 스폰된 광고 quad, 이벤트 피드를
  실시간으로 보여준다.
  Use when the user asks to "모니터 띄워", "대시보드 열어", "monitor the glasses",
  "watch the AR app", "logcat 대시보드", "is the clip ready", "앱 상태 보여줘".
  NOT for: 이미 끝난 캡처 로그 분석(파일 직접 읽기), Unity C# 코드 수정.
user-invocable: true
allowed-tools: Bash
---

# Eagle Monitor

Launch a **new terminal window** running the live AR debug dashboard so it
streams independently of this chat. The window keeps running until the user
closes it (or presses Ctrl-C inside it).

## Prerequisites
- Device connected (`A06B4A95B784973`) and the app **running the b16+ build**
  (only b16+ emits the `[MONITOR]` heartbeat). If the app isn't running yet,
  the dashboard will sit on "waiting for [MONITOR] heartbeat…" — that's fine,
  it picks up automatically once the app starts.
- Does NOT run `adb logcat -c`, does NOT install/push/force-stop. Read-only
  logcat stream.

## What to do when invoked

Spawn the dashboard in its own window. Try these in order, use the first that
works on the host:

**1. Windows Terminal (preferred, if `wt.exe` exists):**
```
wt.exe -- powershell -NoExit -Command "python C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/tools/monitor/eagle_monitor.py"
```

**2. Start-Process powershell (reliable fallback):**
```
Start-Process powershell -ArgumentList '-NoExit','-Command','python C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/tools/monitor/eagle_monitor.py'
```

**3. cmd start (last resort):**
```
cmd /c start "EagleMonitor" powershell -NoExit -Command "python C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/tools/monitor/eagle_monitor.py"
```

Or, equivalently, call the wrapper which sets UTF-8 first:
```
Start-Process powershell -ArgumentList '-NoExit','-File','C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/tools/monitor/launch_monitor.ps1'
```

Run exactly ONE of the above (don't open multiple windows). Then tell the user
the window is open and streaming.

## Options the user may ask for
- Different device: append `--serial <SERIAL>` to the python command.
- Different adb: append `--adb "<path-to-adb.exe>"`.
- No color (dumb terminal): append `--no-color`.
- Offline test (no device): `... eagle_monitor.py --demo` then pipe sample
  `[MONITOR]`/event lines via stdin.

## Dashboard layout (for reference when explaining output)
1. Header — title, uptime `t`, heartbeat count, [STALE] flag if >2s silent
2. CLIP — big READY/COMPILING/? flag + compile seconds ("is it stuck?")
3. FUNNEL — TRIGGER → COLA → MATCH → (COKE / PEPSI), aligned tree
4. SLAM SENSORS — status(raw), pos, rot, vel, ang, anchor, dist, drift, fwdAng
5. SPAWNED OBJECTS — nads + each ad quad world pos, plus anchor pos
6. EVENTS — scrolling last 8 (color decisions, MATCH/no match, ClipExtractor,
   DIVERGED/re-anchor)

## Notes
- The dashboard auto-reconnects if the device drops (prints "device
  disconnected — retrying…", relaunches logcat in a loop). Never exits on its
  own; user closes the window.
- Pure stdlib python. No pip install needed.
