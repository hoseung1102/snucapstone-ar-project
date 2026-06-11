#!/usr/bin/env python3
"""Eagle Eye AR-glasses host-side live debug dashboard.

Streams `adb logcat -v threadtime -s Unity:I` from the device, parses the
app's [MONITOR] heartbeat JSON (~2x/sec) into a full-screen dashboard, and
surfaces a scrolling feed of recent EVENT lines.

Pure stdlib. colorama is optional (used only to enable ANSI on old Windows
consoles); falls back gracefully if missing.

Usage:
    python eagle_monitor.py
    python eagle_monitor.py --serial A06B4A95B784973
    python eagle_monitor.py --adb "C:/.../adb.exe" --serial XXXX
    python eagle_monitor.py --demo            # read sample lines from stdin
"""

import argparse
import json
import os
import subprocess
import sys
import time
from collections import deque

# Portable defaults: adb on PATH, serial empty = use the first connected device.
# Override with --adb <path> / --serial <SER> (e.g. on a different dev machine).
DEFAULT_ADB = "adb"
DEFAULT_SERIAL = ""

MONITOR_MARKER = "[MONITOR] "
# Substrings that mark an interesting EVENT line in the Unity tag stream.
EVENT_MARKERS = (
    "color mean=",
    "✅ MATCH",   # ✅ MATCH
    "MATCH",          # also catch plain MATCH (✅ may be stripped in some shells)
    "no match",
    "[ClipExtractor]",
    "DIVERGED",
    "re-anchor",
    "reanchor",
)

EVENT_FEED_LEN = 8


# --------------------------------------------------------------------------- #
# Terminal helpers
# --------------------------------------------------------------------------- #

class Style:
    """ANSI styling. Auto-disables to empty strings if color unavailable."""

    enabled = True

    RESET = "\x1b[0m"
    BOLD = "\x1b[1m"
    DIM = "\x1b[2m"

    RED = "\x1b[31m"
    GREEN = "\x1b[32m"
    YELLOW = "\x1b[33m"
    BLUE = "\x1b[34m"
    MAGENTA = "\x1b[35m"
    CYAN = "\x1b[36m"
    WHITE = "\x1b[37m"
    GREY = "\x1b[90m"

    BG_GREEN = "\x1b[42m"
    BG_RED = "\x1b[41m"
    BG_YELLOW = "\x1b[43m"
    BLACK = "\x1b[30m"

    @classmethod
    def wrap(cls, text, *codes):
        if not cls.enabled or not codes:
            return text
        return "".join(codes) + text + cls.RESET


def ensure_utf8_stdout():
    """Force stdout to UTF-8 so box-drawing/emoji glyphs don't crash on a
    non-UTF8 Windows code page (e.g. cp949). Best-effort, never raises."""
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")  # py3.7+
    except Exception:
        pass


def enable_vt():
    """Try to enable ANSI/VT processing on Windows; fall back to plain text."""
    ensure_utf8_stdout()
    if os.name != "nt":
        return True
    # colorama is the most reliable; optional.
    try:
        import colorama  # noqa
        colorama.just_fix_windows_console()
        return True
    except Exception:
        pass
    # Empty os.system call flips the console into VT mode on Win10+.
    try:
        os.system("")
        return True
    except Exception:
        return False


def clear_screen():
    sys.stdout.write("\x1b[2J\x1b[H")


# --------------------------------------------------------------------------- #
# State
# --------------------------------------------------------------------------- #

class MonitorState:
    def __init__(self):
        self.data = None          # last parsed [MONITOR] dict
        self.last_update = 0.0     # wall-clock of last heartbeat
        self.heartbeats = 0
        self.parse_errors = 0
        self.events = deque(maxlen=EVENT_FEED_LEN)

    def update_monitor(self, payload):
        self.data = payload
        self.last_update = time.time()
        self.heartbeats += 1

    def add_event(self, ts, msg):
        self.events.append((ts, msg))


# --------------------------------------------------------------------------- #
# Parsing
# --------------------------------------------------------------------------- #

def parse_monitor(line):
    """Return the parsed dict from a [MONITOR] line, or None if not/invalid.

    Tolerates trailing logcat garbage by slicing to the last '}'.
    """
    idx = line.find(MONITOR_MARKER)
    if idx < 0:
        return None
    tail = line[idx + len(MONITOR_MARKER):]
    end = tail.rfind("}")
    if end < 0:
        return None
    blob = tail[: end + 1]
    try:
        obj = json.loads(blob)
        if isinstance(obj, dict):
            return obj
    except (ValueError, TypeError):
        return None
    return None


def extract_threadtime_ts(line):
    """Pull the leading 'MM-DD HH:MM:SS.mmm' time from a -v threadtime line.

    Returns a short 'HH:MM:SS' string, or '' if not parseable.
    """
    parts = line.split()
    # threadtime: <date> <time> <pid> <tid> <prio> <tag>: <msg>
    if len(parts) >= 2 and ":" in parts[1]:
        t = parts[1]
        return t.split(".")[0]  # drop millis
    return ""


def event_message_tail(line):
    """Strip the logcat threadtime prefix, returning just the message body."""
    # threadtime body begins after "<tag>: ". For Unity:I that's "Unity : msg"
    # or "...I Unity   : msg" depending on filtering. Find ": " after tag.
    # Robust approach: locate the first event marker and keep from a sensible
    # boundary before it (the start of the message), else return after the
    # last ' : ' / ': ' separator.
    sep = line.find("Unity")
    if sep >= 0:
        after = line[sep:]
        colon = after.find(": ")
        if colon >= 0:
            return after[colon + 2:].strip()
    # generic fallback: text after last ': '
    colon = line.rfind(": ")
    if colon >= 0:
        return line[colon + 2:].strip()
    return line.strip()


def is_event_line(line):
    return any(m in line for m in EVENT_MARKERS)


def route_line(line, state):
    """Send a raw logcat line to monitor-state or the event feed."""
    payload = parse_monitor(line)
    if payload is not None:
        state.update_monitor(payload)
        return
    if MONITOR_MARKER in line:
        # had the marker but failed to parse
        state.parse_errors += 1
        return
    if is_event_line(line):
        ts = extract_threadtime_ts(line)
        msg = event_message_tail(line)
        if msg:
            state.add_event(ts, msg)


# --------------------------------------------------------------------------- #
# Rendering
# --------------------------------------------------------------------------- #

def _num(v, default="?"):
    if v is None:
        return default
    if isinstance(v, float):
        return ("%.2f" % v)
    return str(v)


def _vec(v, prec=2):
    if not isinstance(v, (list, tuple)):
        return "[?]"
    fmt = "%." + str(prec) + "f"
    return "[" + ", ".join((fmt % x) if isinstance(x, (int, float)) else str(x)
                            for x in v) + "]"


def render(state, serial):
    lines = []
    S = Style

    title = S.wrap(" EAGLE EYE  -  AR Live Monitor ", S.BOLD, S.CYAN)
    sep = S.wrap("=" * 64, S.GREY)

    d = state.data
    if d is None:
        lines.append(title)
        lines.append(sep)
        lines.append("")
        lines.append(S.wrap(
            "  waiting for [MONITOR] heartbeat… "
            "(app must be b16+ and running)", S.YELLOW))
        lines.append("")
        lines.append(S.wrap("  device  : " + serial, S.GREY))
        lines.append(S.wrap("  events  : %d in feed" % len(state.events), S.GREY))
        if state.parse_errors:
            lines.append(S.wrap("  parse-err: %d (saw marker, bad JSON)"
                                % state.parse_errors, S.GREY))
        _render_events(lines, state, S, sep)
        return "\n".join(lines)

    # --- 1. Header --------------------------------------------------------- #
    age = time.time() - state.last_update
    stale = age > 2.0
    upt = _num(d.get("t"))
    hdr = "%s   uptime t=%ss   hb#%d" % (title, upt, state.heartbeats)
    if "fps" in d:
        hdr += "   fps=%s" % _num(d.get("fps"))
    if stale:
        hdr += S.wrap("   [STALE %.1fs]" % age, S.BOLD, S.RED)
    lines.append(hdr)
    lines.append(sep)

    # --- 2. CLIP flag ------------------------------------------------------ #
    clip = d.get("clip", "?")
    clip_s = _num(d.get("clipS"))
    if clip == "READY":
        flag = S.wrap(" READY ", S.BOLD, S.BLACK, S.BG_GREEN)
    elif clip == "COMPILING":
        flag = S.wrap(" COMPILING ", S.BOLD, S.BLACK, S.BG_YELLOW)
    else:
        flag = S.wrap(" %s " % clip, S.BOLD, S.BLACK, S.BG_RED)
    lines.append("  CLIP   %s  (%ss)" % (flag, clip_s))
    lines.append("")

    # --- 3. Funnel --------------------------------------------------------- #
    trig = d.get("trig", 0)
    cola = d.get("cola", 0)
    match = d.get("match", 0)
    coke = d.get("coke", 0)
    pepsi = d.get("pepsi", 0)
    lines.append(S.wrap("  FUNNEL", S.BOLD, S.WHITE))
    lines.append("    TRIGGER ........ %s" % S.wrap("%4d" % trig, S.CYAN))
    lines.append("     └ COLA ........ %s" % S.wrap("%4d" % cola, S.CYAN))
    lines.append("        └ MATCH .... %s" % S.wrap("%4d" % match, S.GREEN))
    lines.append("            ├ COKE  %s"
                 % S.wrap("%4d" % coke, S.RED))
    lines.append("            └ PEPSI %s"
                 % S.wrap("%4d" % pepsi, S.BLUE))
    lines.append("")

    # --- 4. SLAM sensors --------------------------------------------------- #
    slam = str(d.get("slam", "?"))
    raw = d.get("raw", "?")
    if "TRACK" in slam or "CONVERG" in slam:
        slam_c = S.wrap(slam, S.GREEN)
    elif "SEEK" in slam:
        slam_c = S.wrap(slam, S.YELLOW)
    else:
        slam_c = S.wrap(slam, S.RED)
    lines.append(S.wrap("  SLAM SENSORS", S.BOLD, S.WHITE))
    lines.append("    status .. %s (raw=%s)" % (slam_c, raw))
    lines.append("    pos ..... %s   rot %s deg"
                 % (_vec(d.get("pos")), _vec(d.get("rot"), 0)))
    lines.append("    vel ..... %s m/s   ang %s dps"
                 % (_num(d.get("v")), _num(d.get("w"))))
    lines.append("    anchor .. %s" % _vec(d.get("anchor")))
    lines.append("    dist .... %s m   drift %s m   fwdAng %s deg"
                 % (_num(d.get("dist")), _num(d.get("drift")),
                    _num(d.get("fwdAng"), "?")))
    lines.append("")

    # --- 5. Spawned objects ------------------------------------------------ #
    nads = d.get("nads", 0)
    ads = d.get("ads") or []
    lines.append(S.wrap("  SPAWNED OBJECTS  (nads=%s)" % _num(nads), S.BOLD, S.WHITE))
    lines.append("    anchor .. %s" % _vec(d.get("anchor")))
    if ads:
        for i, p in enumerate(ads):
            lines.append("    ad[%d] ... %s" % (i, _vec(p)))
    else:
        lines.append(S.wrap("    (none spawned yet)", S.GREY))
    lines.append("")

    # --- 6. Events --------------------------------------------------------- #
    _render_events(lines, state, S, sep)

    return "\n".join(lines)


def _render_events(lines, state, S, sep):
    lines.append(sep)
    lines.append(S.wrap("  EVENTS (live, last %d)" % EVENT_FEED_LEN,
                        S.BOLD, S.MAGENTA))
    if not state.events:
        lines.append(S.wrap("    (no events yet)", S.GREY))
        return
    for ts, msg in state.events:
        m = msg
        if len(m) > 90:
            m = m[:87] + "..."
        # light coloring by content
        if "MATCH" in m and "no match" not in m:
            m = S.wrap(m, S.GREEN)
        elif "no match" in m:
            m = S.wrap(m, S.YELLOW)
        elif "DIVERGED" in m or "re-anchor" in m or "reanchor" in m:
            m = S.wrap(m, S.RED)
        lines.append("    %s  %s" % (S.wrap(ts or "--:--:--", S.GREY), m))


# --------------------------------------------------------------------------- #
# Sources
# --------------------------------------------------------------------------- #

def spawn_logcat(adb, serial):
    # serial 비면 -s 생략 → adb 의 첫(유일한) 디바이스 사용.
    cmd = [adb] + (["-s", serial] if serial else []) + ["logcat", "-v", "threadtime", "-s", "Unity:I"]
    return subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        bufsize=1,
        universal_newlines=True,
        errors="replace",
    )


def run_live(adb, serial):
    state = MonitorState()
    proc = None
    try:
        # Initial paint so the user sees something immediately.
        clear_screen()
        sys.stdout.write(render(state, serial) + "\n")
        sys.stdout.flush()

        while True:
            try:
                proc = spawn_logcat(adb, serial)
            except FileNotFoundError:
                sys.stderr.write(
                    "adb not found at: %s  (use --adb to override)\n" % adb)
                return 2

            dirty = True
            last_paint = 0.0
            for raw in proc.stdout:
                line = raw.rstrip("\n")
                if not line:
                    continue
                had_monitor = MONITOR_MARKER in line
                route_line(line, state)
                dirty = True
                now = time.time()
                # Repaint on every heartbeat; throttle event-only repaints a bit.
                if had_monitor or (now - last_paint) > 0.4:
                    if dirty:
                        clear_screen()
                        sys.stdout.write(render(state, serial) + "\n")
                        sys.stdout.flush()
                        last_paint = now
                        dirty = False

            # stdout ended -> logcat process died (disconnect / killed)
            rc = proc.poll()
            proc = None
            clear_screen()
            out = render(state, serial)
            out += "\n\n" + Style.wrap(
                "  device disconnected (logcat exited rc=%s) "
                "— retrying…" % rc, Style.BOLD, Style.RED)
            sys.stdout.write(out + "\n")
            sys.stdout.flush()
            time.sleep(2)

    except KeyboardInterrupt:
        sys.stdout.write("\n" + Style.wrap("monitor stopped.", Style.DIM) + "\n")
        sys.stdout.flush()
        return 0
    finally:
        if proc is not None:
            try:
                proc.terminate()
            except Exception:
                pass


def run_demo():
    """Read sample logcat lines from stdin and render once at the end.

    Used for offline validation: pipe a few [MONITOR]/event lines in.
    """
    state = MonitorState()
    enable_vt()
    for raw in sys.stdin:
        line = raw.rstrip("\n")
        if line:
            route_line(line, state)
    clear_screen()
    sys.stdout.write(render(state, "DEMO") + "\n")
    sys.stdout.flush()
    if state.heartbeats == 0:
        sys.stderr.write("\n[demo] no [MONITOR] heartbeat parsed from stdin\n")
        return 1
    return 0


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #

def main(argv=None):
    ap = argparse.ArgumentParser(description="Eagle Eye AR live debug monitor")
    ap.add_argument("--adb", default=DEFAULT_ADB, help="path to adb executable")
    ap.add_argument("--serial", default=DEFAULT_SERIAL, help="device serial")
    ap.add_argument("--demo", action="store_true",
                    help="read sample lines from stdin instead of a device")
    ap.add_argument("--no-color", action="store_true", help="disable ANSI color")
    args = ap.parse_args(argv)

    if args.no_color:
        Style.enabled = False

    if args.demo:
        return run_demo()

    if not enable_vt():
        Style.enabled = False
    return run_live(args.adb, args.serial)


if __name__ == "__main__":
    sys.exit(main())
