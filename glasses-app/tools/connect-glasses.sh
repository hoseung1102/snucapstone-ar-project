#!/usr/bin/env bash
# connect-glasses.sh — RayNeo X3 Pro 무선 ADB 연결 + scrcpy 미러링 (macOS/Linux, 팀 공용)
#
# Windows 판 connect-glasses.ps1 의 bash 포트. 무선 로직(tcpip/connect/지속성/scrcpy)은 OS 무관.
# ※ macOS 는 Windows 의 "Google USB Driver" 함정이 없다(adb 기본 인식). 그 단계만 불필요.
#
# 사전: brew install scrcpy android-platform-tools   (또는 Android SDK platform-tools)
#       안경과 이 Mac 이 같은 공유기 WiFi(같은 서브넷). 폰 테더링은 PC LAN 과 달라서 안 됨.
#
# 사용법:
#   ./connect-glasses.sh                 # 알려진 IP 로 무선 연결 + 미러 (이미 tcpip 모드일 때)
#   ./connect-glasses.sh --reinit-usb    # ★ USB 꽂고: IP 자동검출 + tcpip + 지속성설정 + 연결 + 미러 (재부팅/다른 네트워크/IP 변경 후)
#   ./connect-glasses.sh --no-mirror     # 연결만 (scrcpy 안 띄움)
#   ./connect-glasses.sh --ip 192.168.0.5
#
# 끊겼을 때: (1) 잠든 거면 안경 깨우고 그냥 다시 실행(USB 불필요)  (2) 재부팅이면 --reinit-usb
# ⚠️ 공유 adb 환경이면 'adb kill-server'/무선 임의 끊기 금지.

IP="192.168.219.108"   # 안경 WiFi IP (보통 고정; 바뀌면 --reinit-usb 로 자동 재검출). 공유기 DHCP 예약 권장.
PORT=5555
REINIT=0
NOMIRROR=0
SERIAL=""

while [ $# -gt 0 ]; do
  case "$1" in
    --reinit-usb) REINIT=1 ;;
    --no-mirror)  NOMIRROR=1 ;;
    --ip)         IP="$2"; shift ;;
    --port)       PORT="$2"; shift ;;
    --serial)     SERIAL="$2"; shift ;;
    -h|--help)    grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "알 수 없는 인자: $1 (--help 참고)"; exit 2 ;;
  esac
  shift
done

# --- adb 탐색 (PATH 우선 → 표준 SDK 경로) ---
ADB="$(command -v adb || true)"
[ -z "$ADB" ] && [ -x "$HOME/Library/Android/sdk/platform-tools/adb" ] && ADB="$HOME/Library/Android/sdk/platform-tools/adb"
[ -z "$ADB" ] && { echo "adb 를 못 찾음 — 'brew install android-platform-tools' 또는 Android SDK 설치"; exit 1; }

# --- scrcpy 탐색 (PATH) ---
SCRCPY="$(command -v scrcpy || true)"

export ADB   # scrcpy 가 이 adb 를 쓰게 (버전 일관성)

if [ "$REINIT" = 1 ]; then
  [ -z "$SERIAL" ] && SERIAL="$("$ADB" devices | awk '$2=="device" && $1 !~ /:/ {print $1; exit}')"
  [ -z "$SERIAL" ] && { echo "USB 로 연결된 안경이 없음 — 케이블 + USB 디버깅 확인"; exit 1; }
  echo "[USB] 대상 시리얼: $SERIAL"
  IP_DET="$("$ADB" -s "$SERIAL" shell "ip -f inet addr show wlan0 | grep inet" 2>/dev/null | awk '{print $2}' | cut -d/ -f1 | tr -d '\r')"
  [ -z "$IP_DET" ] && { echo "안경 wlan0 IP 없음 — 안경을 같은 WiFi(공유기)에 연결하세요 (폰 테더링 불가)"; exit 1; }
  IP="$IP_DET"; echo "[USB] 검출된 안경 WiFi IP: $IP"
  # 지속성: sleep/절전으로 무선이 끊기는 것 방지 (stay_on 은 충전 중에만 — 무선 사용 시 충전 연결 권장)
  "$ADB" -s "$SERIAL" shell "settings put global stay_on_while_plugged_in 7" >/dev/null 2>&1 || true
  "$ADB" -s "$SERIAL" shell "settings put global wifi_sleep_policy 2"        >/dev/null 2>&1 || true
  "$ADB" -s "$SERIAL" shell "dumpsys deviceidle disable"                     >/dev/null 2>&1 || true
  "$ADB" -s "$SERIAL" shell "settings put global adb_wifi_enabled 1"         >/dev/null 2>&1 || true
  echo "[persist] 충전 중 안 잠듦 + Doze off + WiFi 절전 off 적용 (안 끊기려면 안경을 충전 연결 권장)"
  "$ADB" -s "$SERIAL" tcpip "$PORT"; sleep 2
fi

echo "[WiFi] (re)connect ${IP}:${PORT}"
"$ADB" disconnect "${IP}:${PORT}" >/dev/null 2>&1 || true   # 묵은 offline 엔트리 정리 후 깨끗하게 재연결
"$ADB" connect "${IP}:${PORT}" || true
sleep 1
"$ADB" devices -l

# online('device') 확인 — 아니면 명확히 안내하고 종료
if ! "$ADB" devices | awk -v t="${IP}:${PORT}" '$1==t && $2=="device"{f=1} END{exit f?0:1}'; then
  echo ""
  echo "⚠️  무선 기기 ${IP}:${PORT} 에 연결 안 됨 (offline / 무응답)."
  echo "  1) 안경이 잠든 것이면(흔함): 안경 깨우고  ./connect-glasses.sh  다시 실행 — USB 불필요."
  echo "  2) 그래도 안 되면(재부팅으로 tcpip 풀림 / IP 변경): USB 꽂고  ./connect-glasses.sh --reinit-usb"
  exit 1
fi

if [ "$NOMIRROR" = 1 ]; then echo "연결 완료 (미러 생략). 직접 미러: scrcpy -s ${IP}:${PORT}"; exit 0; fi
[ -z "$SCRCPY" ] && { echo "scrcpy 미설치 → 'brew install scrcpy'. adb 무선 연결은 됨 (${IP}:${PORT})."; exit 0; }
echo "[mirror] scrcpy -s ${IP}:${PORT}   (창 닫으면 종료)"
exec "$SCRCPY" -s "${IP}:${PORT}" --window-title "RayNeo X3 Pro (WiFi)"
