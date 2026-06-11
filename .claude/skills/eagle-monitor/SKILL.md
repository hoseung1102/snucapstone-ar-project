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

> **앱을 기기에 launch 할 때 이 모니터도 같이 띄우는 것이 기본 동작이다** — 트리거/CLIP/색상/SLAM/영상 spawn 을 실시간으로 봐야 디버깅이 된다.
> **OS 판별 먼저**: Windows → 아래 1~3 중 첫 성공하는 것. macOS/Linux(예: RayNeo SDK Mac 머신) → 맨 아래 python3 직접 실행. 한글/박스문자는 UTF-8 자동 처리(스크립트 내장).

## Prerequisites
- Device connected (`adb devices` 로 시리얼 확인 — 머신마다 다름) and the app
  **running the b16+ build** (b16+ / b24-integrated 이상만 `[MONITOR]` heartbeat 를
  emit). 앱이 아직 안 떠 있으면 대시보드는 "waiting for [MONITOR] heartbeat…" 에서
  대기하다가 앱이 뜨면 자동으로 잡는다.
- Does NOT run `adb logcat -c`, does NOT install/push/force-stop. Read-only
  logcat stream.

## What to do when invoked

먼저 **이 레포의 절대경로**를 구한다(= `spatial_anchor_test/` 를 담은 디렉토리).
아래 명령의 `<REPO>` 를 그 경로로 치환하고, 디바이스 시리얼을 `--serial <SERIAL>`
로 붙여라(여러 대면 필수, 한 대면 생략 가능). adb 가 PATH 에 없으면
`--adb "<path-to-adb.exe>"` 도 붙인다.

스크립트 경로: `<REPO>/spatial_anchor_test/tools/monitor/eagle_monitor.py`

Spawn the dashboard in its own window. Try these in order, use the first that
works on the host:

**1. Windows Terminal (preferred, if `wt.exe` exists):**
```
wt.exe -- powershell -NoExit -Command "python <REPO>/spatial_anchor_test/tools/monitor/eagle_monitor.py --serial <SERIAL>"
```

**2. Start-Process powershell (reliable fallback):**
```
Start-Process powershell -ArgumentList '-NoExit','-Command','python <REPO>/spatial_anchor_test/tools/monitor/eagle_monitor.py --serial <SERIAL>'
```

**3. cmd start (last resort):**
```
cmd /c start "EagleMonitor" powershell -NoExit -Command "python <REPO>/spatial_anchor_test/tools/monitor/eagle_monitor.py --serial <SERIAL>"
```

**macOS/Linux** (RayNeo SDK 머신이 Mac 인 경우):
```
python3 <REPO>/spatial_anchor_test/tools/monitor/eagle_monitor.py --serial <SERIAL>
```
(별도 터미널 탭에서 직접 실행. 순수 stdlib python3 라 pip 불필요.)

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
