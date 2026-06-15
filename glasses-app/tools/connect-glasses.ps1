<#
  connect-glasses.ps1 — RayNeo X3 Pro 무선 ADB 연결 + scrcpy 미러링 헬퍼 (Windows, 팀 공용)

  adb/scrcpy 경로를 자동 탐색하고 안경 WiFi IP 를 자동 검출한다. 다른 팀원/다른 네트워크에서도
  -ReinitUsb 로 USB 한 번 꽂으면 그 환경의 IP 를 잡아 무선 전환한다.

  사전 조건:
    - 안경이 adb 에 인식돼야 함. Windows 11 에서 안 잡히면 Google USB Driver 설치 (docs/dev-environment.md §2).
    - 미러링하려면 scrcpy 필요:  winget install Genymobile.scrcpy
    - 안경과 PC 가 같은 공유기 WiFi(같은 서브넷)에 있어야 함. 폰 테더링은 PC LAN 과 달라서 안 됨.

  사용법:
    .\connect-glasses.ps1                 # 이미 tcpip 모드면: 알려진 IP 로 무선 연결 + 미러
    .\connect-glasses.ps1 -ReinitUsb      # ★ USB 꽂고: IP 자동검출 → tcpip → 무선연결 (재부팅 후 / 다른 네트워크 / IP 변경 시)
    .\connect-glasses.ps1 -NoMirror       # 연결만 (scrcpy 안 띄움)
    .\connect-glasses.ps1 -Ip 192.168.0.5 # IP 직접 지정

  참고 (실측 거동):
    - 안경이 잠들면(화면 off / 벗어둠) 무선 adb 가 'offline' 으로 끊긴다 → 안경 깨운 뒤 이 스크립트 재실행(plain connect)으로 복구.
    - 안경을 재부팅하면 tcpip 모드 자체가 풀린다 → USB 꽂고 -ReinitUsb 한 번이면 복구.
  ⚠️ ADB 가 팀 공유 서버면 'adb kill-server' / 무선 임의 끊기 금지 (이 스크립트는 둘 다 안 함).
#>
[CmdletBinding()]
param(
  # 기본 IP = 우리 공유기에서 안경 DHCP 주소(보통 고정적). 바뀌면 -ReinitUsb 로 자동 재검출.
  [string]$Ip = "192.168.219.108",
  [int]$Port = 5555,
  [string]$Serial,            # -ReinitUsb 대상 USB 시리얼 (미지정 시 USB 기기 자동 선택)
  [switch]$ReinitUsb,         # USB 로 tcpip 재초기화 (재부팅 후 / IP 변경 / 다른 네트워크)
  [switch]$NoMirror
)
$ErrorActionPreference = "Stop"

# --- adb 탐색 (Android platform-tools 우선, 없으면 PATH) ---
$adb = Join-Path $env:LOCALAPPDATA "Android\sdk\platform-tools\adb.exe"
if (-not (Test-Path $adb)) { $adb = (Get-Command adb -ErrorAction SilentlyContinue).Source }
if (-not $adb) { throw "adb 를 못 찾음. Android platform-tools 설치 필요 (docs/dev-environment.md)." }

# --- scrcpy 탐색 (winget 패키지 우선, 없으면 PATH) ---
$scrcpy = $null
$pkg = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Filter "Genymobile.scrcpy*" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pkg) { $scrcpy = (Get-ChildItem $pkg.FullName -Recurse -Filter scrcpy.exe -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
if (-not $scrcpy) { $scrcpy = (Get-Command scrcpy -ErrorAction SilentlyContinue).Source }

# ★ scrcpy 가 자기 번들 adb(버전 다름) 대신 이 adb 를 쓰게 → 공유 adb 서버 버전 충돌 방지
$env:ADB = $adb

if ($ReinitUsb) {
  if (-not $Serial) {
    # 'device' 상태이면서 IP:port 형태가 아닌(=USB) 첫 기기 자동 선택
    $Serial = (& $adb devices | Select-String "`tdevice$" |
      ForEach-Object { ($_.ToString() -split "\s+")[0] } |
      Where-Object { $_ -and ($_ -notmatch ":\d+$") } | Select-Object -First 1)
  }
  if (-not $Serial) { throw "USB 로 연결된 안경이 없음. USB 케이블 + USB 디버깅 확인 (docs/dev-environment.md §2)." }
  Write-Host "[USB] 대상 시리얼: $Serial"
  $wlan = (& $adb -s $Serial shell "ip -f inet addr show wlan0") 2>$null | Out-String
  if ($wlan -match "inet (\d+\.\d+\.\d+\.\d+)") {
    $Ip = $Matches[1]; Write-Host "[USB] 검출된 안경 WiFi IP: $Ip"
  } else {
    throw "안경 wlan0 IP 없음 — 안경을 PC 와 같은 WiFi(공유기)에 연결하세요 (폰 테더링 불가)."
  }
  & $adb -s $Serial tcpip $Port | Out-Host   # adbd TCP 모드 재시작 (진행 중 USB scrcpy 는 한 번 끊김)
  Start-Sleep -Seconds 2
}

Write-Host "[WiFi] adb connect ${Ip}:${Port}"
& $adb connect "${Ip}:${Port}" | Out-Host
Start-Sleep -Seconds 1
& $adb devices -l | Out-Host

# 실제로 online('device') 인지 확인 — offline/무응답이면 scrcpy 로 안 넘어가고 명확히 안내
$devLine = (& $adb devices | Select-String ([regex]::Escape("${Ip}:${Port}"))).Line
if (-not ($devLine -match "device\s*$")) {
    Write-Host ""
    Write-Warning "무선 기기 ${Ip}:${Port} 에 연결 안 됨 (offline / 무응답)."
    Write-Host "  원인: 안경 재부팅(tcpip 모드 풀림) / IP 변경 / 안경 꺼짐·잠듦 중 하나."
    Write-Host "  복구:  USB 케이블 꽂고  ->  .\connect-glasses.ps1 -ReinitUsb   (IP 자동 재검출 + tcpip 재설정)"
    Write-Host "  (안경이 깨어있는데도 안 되면 재부팅된 것이므로 반드시 -ReinitUsb)"
    return
}

if ($NoMirror) { Write-Host "연결 완료 (미러 생략). 직접 미러: scrcpy -s ${Ip}:${Port}"; return }
if (-not $scrcpy) { Write-Warning "scrcpy 미설치 → 'winget install Genymobile.scrcpy'. adb 무선 연결은 됨 (${Ip}:${Port})."; return }
Write-Host "[mirror] scrcpy -s ${Ip}:${Port}   (창을 닫으면 종료)"
& $scrcpy -s "${Ip}:${Port}" --window-title "RayNeo X3 Pro (WiFi)"
