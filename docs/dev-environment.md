<!-- 새 Windows 머신에서 "0 → 빌드·설치·실행·미러링" 까지 가는 셋업 런북. 코드/빌드 아키텍처는 dev-guide.md, 현황은 STATUS.md, 플랫폼 사실/제약은 platform-rayneo.md. -->

# 🖥️ 개발 환경 셋업 (Windows → RayNeo X3 Pro)

> 새 머신에서 안경 앱을 **빌드·설치·관찰·미러링**까지 가는 런북.
> 코드/빌드 아키텍처 → [`dev-guide.md`](dev-guide.md) · 현황 → [`STATUS.md`](STATUS.md) · 플랫폼 사실/제약 → [`platform-rayneo.md`](platform-rayneo.md).
>
> 신뢰도: 🟢 **이 머신(2026-06-16, 안경 serial `A06B4A95B784973`, ARGF20/Android 12)에서 실제 검증** · 🟡 표준 절차(미검증) · 🟠 기기/네트워크 조건 충족 시.

## 0. 한눈에

| 도구 | 용도 | 상태 |
|---|---|---|
| Unity Hub + **2022.3.62f3** (+Android Build Support) | 안경 앱 빌드 | 🟡 설치 필요 |
| Android **platform-tools** (`adb`) | 설치/로그캣/기기 제어 | 🟢 `C:\Users\wondo\AppData\Local\Android\sdk\platform-tools` (v35.0.2) |
| **Google USB Driver** | ★ Win11 에서 adb 가 X3 Pro 를 **인식하게 하는 필수 드라이버** | 🟢 검증 |
| **scrcpy** | 안경 화면 실시간 미러링 | 🟢 검증 (winget, v4.0) |

---

## 1. Unity (빌드) 🟡

1. **Unity Hub** 설치: <https://unity.com/download>
2. Hub 에서 **정확히 `Unity 2022.3.62f3`** 설치. 일반 목록엔 최신 패치만 떠서, 정확한 패치는 **다운로드 아카이브**에서: <https://unity.com/releases/editor/archive> → `Unity 2022.3 (LTS)` → `2022.3.62` 의 **"Install with Unity Hub"**.
   - ⚠️ 버전 정확히 맞춰야 함 — 다른 패치면 프로젝트 열 때 비가역 업그레이드 프롬프트가 뜨고, `build_2022.ps1` 이 `ProjectVersion.txt == 2022.3.62f3` 를 assert 한다. (RayNeo 공식은 2022.3.36f1c1 권장이나 **레포는 62f3 고정** — 레포 값 따른다.)
3. 설치 시 **반드시 체크**: ☑ **Android Build Support** → ☑ **Android SDK & NDK Tools** + ☑ **OpenJDK** (이 셋이 있어야 APK 빌드 가능).
4. **★ 라이선스 활성화 (무료 Personal)** — 안 하면 batchmode 빌드가 `No valid Unity Editor license found / No ULF license found` 로 즉시 실패한다(프로젝트 로드 전 단계라 빌드 에러처럼 안 보임 — 실측 함정). **에디터만 단독 설치하면 라이선스가 없다.**
   - **권장**: Unity Hub 로 로그인 → 자동 활성. (Hub 없으면 <https://unity.com/download> 에서 설치) → Hub 우상단 계정 **Sign in** → 필요시 **⚙️ Preferences → Licenses → Add → "Get a free personal license"**. → `C:\ProgramData\Unity\Unity_lic.ulf` 생성되면 OK.
   - **Hub 없이(CLI 수동)**: `Unity.exe -batchmode -createManualActivationFile` → 생성된 `.alf` 를 <https://license.unity3d.com/manual> 에 업로드(Unity Personal 선택) → 받은 `.ulf` 를 `Unity.exe -batchmode -manualLicenseFile <file.ulf>` 로 적용.
   - 확인: `Test-Path "$env:ProgramData\Unity\Unity_lic.ulf"` 가 True.

> 빌드 명령은 [`dev-guide.md`](dev-guide.md) §3 / 본 문서 §5 (`glasses-app/build_2022.ps1` — Unity 경로 자동탐색). 첫 빌드/오픈 시 `AdCheckout.cs.meta` 가 자동 생성됨 → 커밋한다.

---

## 2. adb 연결 — ★ Windows 11 핵심 함정 🟢

**증상**: 안경을 USB 로 연결 + 안경에서 USB 디버깅 ON 했는데 `adb devices` 가 **빈 목록**(`unauthorized` 조차 안 뜸).

**원인** (이 머신에서 확정): Windows 11 이 ADB 인터페이스에 **generic WinUSB(`winusb.inf`, Microsoft, v10.0.26100)** 를 물리는데, 이 드라이버는 adb 가 기기를 찾을 때 쓰는 **device-interface GUID `{F72FE0D4-CBCB-407d-8814-9ED673D0DD6B}` 를 노출하지 않는다.** Device Manager 엔 "ADB Interface — 정상(OK)" 으로 보여서 더 헷갈린다 (드라이버는 "있는데" adb 가 그 인터페이스를 못 연다).

**진단**:
```powershell
# ADB Interface 에 어떤 드라이버가 물렸나 (winusb.inf 10.x 면 이 함정)
$id = (Get-PnpDevice | ? FriendlyName -match 'ADB Interface').InstanceId
Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_Service, DEVPKEY_Device_DriverVersion
#  Service=WINUSB, Version=10.0.26100.x  → adb 가 못 봄 (아래 해결)
```

**해결 (🟢 검증 — `zadig` 불필요, Google 공식 드라이버가 더 깔끔)**:
1. Google USB Driver 다운로드: <https://dl.google.com/android/repository/usb_driver_r13-windows.zip> → 압축 해제.
   - (이 `android_winusb.inf` 에 `USB\VID_18D1&PID_4EE2&MI_01` 와 `PID_4EE7` 매칭이 들어있어 X3 Pro 의 ADB 인터페이스에 바인딩되고, adb GUID 를 등록한다.)
2. **관리자 권한** PowerShell/cmd 에서:
   ```
   pnputil /add-driver "C:\...\usb_driver\android_winusb.inf" /install
   ```
   → "드라이버 패키지를 설치한 장치: USB\VID_18D1&PID_4EE2&MI_01\..." 가 나오면 성공.
   - (비관리자 셸에서 권한 상승: `Start-Process cmd '/c pnputil /add-driver "...\android_winusb.inf" /install' -Verb RunAs` → UAC "예".)
3. 확인:
   ```
   adb devices -l
   #  A06B4A95B784973   device   product:RayNeoX3Pro model:ARGF20 device:MercuryLiteXR
   ```
   `device` 로 뜨면 끝. (`unauthorized` 면 안경 화면의 "USB 디버깅 허용?" 다이얼로그에서 항상 허용.)

> ⚠️ **공유 adb 주의**: 안경팀 공유 환경이면 `adb kill-server` 금지(다른 세션 끊김), 무선 ADB 임의 끊기 금지. `platform-tools` 버전은 머신당 하나로 고정.
> 📝 이전 문서가 zadig 를 권했으나, Google USB Driver + `pnputil` 이 더 깔끔/공식이고 이 머신에서 검증됨 → [`platform-rayneo.md`](platform-rayneo.md) ADB 섹션 갱신함.

---

## 3. 화면 미러링 (scrcpy) 🟢

```
winget install Genymobile.scrcpy        # 설치 (v4.0 검증)
scrcpy -s A06B4A95B784973               # 미러 창 띄움
```

⚠️ **adb 버전 충돌 주의**: scrcpy 번들 adb(**37.0.0**) ≠ platform-tools(**35.0.2**). scrcpy 가 실행 중 서버를 죽이고 재시작하지 않게 **시스템 adb 를 강제**:
```powershell
$env:ADB = "C:\Users\wondo\AppData\Local\Android\sdk\platform-tools\adb.exe"
scrcpy -s A06B4A95B784973
```

**관찰된 사실**: scrcpy 가 잡는 `Texture 1280x480` = X3 Pro **디스플레이 프레임버퍼(SBS 스테레오, ≈640×480/eye)**. 즉 미러는 **안경이 렌더한 UI/광고 오버레이 프레임버퍼**이지 '현실 배경 + AR 합성' 영상이 아니다. 사용자 시점 합성영상이 필요하면 온디바이스 **RecordManager**(platform-rayneo 참조).

검은 화면이면: 다른 `--display-id`, 또는 렌더링을 **Multi-pass** 로 (RayNeo 공식 안내).

---

## 4. 무선 ADB + 무선 미러링 🟢 검증 — 안경이 PC 와 같은 WiFi 에 있어야 함

**전제**: 안경이 PC 와 **같은 공유기 WiFi**(같은 서브넷, 예: `192.168.219.x`)에 접속. 폰 테더링으로 인터넷만 쓰면 PC LAN 과 달라서 안 됨.

> 💡 **편의 스크립트**: [`glasses-app/tools/connect-glasses.ps1`](../glasses-app/tools/connect-glasses.ps1) — adb/scrcpy 경로 자동탐색 + 안경 IP 자동검출 + 무선연결 + 미러를 한 번에. **첫 사용 / 다른 네트워크 / 재부팅 후**엔 USB 꽂고 `.\connect-glasses.ps1 -ReinitUsb`, 그 뒤엔 그냥 `.\connect-glasses.ps1`.

USB 로 연결된 상태에서:
```
adb -s A06B4A95B784973 shell ip -f inet addr show wlan0   # 안경 WiFi IP (예: 192.168.219.108)
adb -s A06B4A95B784973 tcpip 5555                          # adbd 를 TCP 모드로 재시작
adb connect 192.168.219.108:5555
adb devices                                                # usb + wireless(IP:5555) 둘 다 보임
# 이제 USB 뽑아도 됨 → 무선 미러링:
set ADB=...\platform-tools\adb.exe
scrcpy -s 192.168.219.108:5555
```
⚠️ `tcpip` 은 adbd 재시작이라 **진행 중인 USB scrcpy 미러가 한 번 끊긴다**(정상 — 무선으로 다시 `scrcpy` 하면 됨). 공유 환경이면 무선 연결을 임의로 끊지 말 것.

> 🟢 2026-06-16 이 머신 실측: 안경 WiFi `192.168.219.108`(PC `.101` 동일 서브넷) → `tcpip 5555` → `connect` → `scrcpy -s 192.168.219.108:5555` 무선 미러 동작 (**USB 분리 후 순수 WiFi 로 유지**, 지연만 약간 큼).
>
> ⚠️ **안경이 잠들면(화면 off) 무선 adb 가 `offline` 으로 끊긴다.** tcpip 모드 자체는 재부팅 전까지 유지되므로 — 안경을 깨운 뒤 `adb connect 192.168.219.108:5555`(또는 스크립트) 만으로 복구된다(USB 불필요). **디스플레이 sleep 을 길게/never 로 두면 안 끊긴다**(기본 짧은 sleep=30s 가 끊김의 원인이었음, 실측 확인). 재부팅으로 tcpip 가 풀리면 USB 꽂고 `-ReinitUsb`.

---

## 5. 빌드 → 설치 → 실행 → 관찰 (전체 루프)

```powershell
# 빌드 (canonical)
glasses-app\build_2022.ps1

# 설치 / 권한 / 실행  (PKG=com.eagleeye.helloar, launch=UnityOpenXrActivity)
adb -s A06B4A95B784973 install -r glasses-app\Build\*.apk
adb -s A06B4A95B784973 shell pm grant com.eagleeye.helloar android.permission.CAMERA
adb -s A06B4A95B784973 shell am start -n com.eagleeye.helloar/com.rayneo.openxradapter.UnityOpenXrActivity

# 라이브 관찰 (logcat [MONITOR] 대시보드)
#   /eagle-monitor 스킬  또는:
python glasses-app\tools\monitor\eagle_monitor.py --serial A06B4A95B784973
```

> 권한 grant 누락 시 stereo 권한 다이얼로그가 안 보여 hang — CAMERA 는 설치 직후 pre-grant. 정확한 launch activity 는 빌드 후 `adb shell dumpsys package com.eagleeye.helloar | grep -A2 Activity` 로 확인 권장.
