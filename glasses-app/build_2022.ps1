# Unity 2022.3 LTS + ARDK 1.1.2 batch build  (canonical 빌드 스크립트)
# 전제: setup_2022.ps1 실행 완료 + Unity Editor 에서 XR Plug-in Management 셋업 완료
#
# 이 스크립트는 glasses-app/ 프로젝트 폴더에 동봉되어 자기탐색($PSScriptRoot)한다.
# 따라서 staging 으로 복사할 필요 없이 레포 내 위치에서 그대로 빌드된다.

$ErrorActionPreference = "Stop"

$UNITY_VERSION = "2022.3.62f3"
$UNITY_BIN     = "C:\Program Files\Unity\Hub\Editor\$UNITY_VERSION\Editor\Unity.exe"

# 스크립트가 들어있는 폴더 = Unity 프로젝트 루트 (glasses-app)
$PROJECT_DIR  = $PSScriptRoot
$LOG_DIR      = "$PROJECT_DIR\logs"
$PACKAGE_NAME = "com.eagleeye.helloar"
$LAUNCH_ACTIVITY = "com.rayneo.openxradapter.UnityOpenXrActivity"

if (-not (Test-Path "$PROJECT_DIR\Assets")) {
    Write-Host "  $PROJECT_DIR 가 Unity 프로젝트가 아님 (Assets 없음). 먼저 setup_2022.ps1 실행."
    exit 1
}

if (-not (Test-Path $UNITY_BIN)) {
    Write-Host "  Unity 2022 실행 파일 못 찾음: $UNITY_BIN"
    Write-Host "  설치된 Unity 2022.3.x patch version 을 `$UNITY_VERSION 에 수정 필요"
    exit 1
}

# ★ Unity 6 오염 방지 — ProjectVersion.txt 가 2022.3.62f3 인지 빌드 전 검증
$VERSION_FILE = "$PROJECT_DIR\ProjectSettings\ProjectVersion.txt"
if (Test-Path $VERSION_FILE) {
    $projVer = Get-Content $VERSION_FILE | Where-Object { $_ -match "m_EditorVersion:" } | Select-Object -First 1
    if ($projVer -notmatch "2022\.3\.62f3") {
        Write-Host ""
        Write-Host "  ProjectVersion.txt 가 2022.3.62f3 가 아님: $projVer"
        Write-Host "  Unity 6 등으로 오염됐을 수 있음. git checkout -- ProjectSettings/ 로 복구 후 재시도."
        exit 1
    }
}

New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null

Write-Host ""
Write-Host "============================================================"
Write-Host "  Unity 2022.3 LTS batch APK 빌드"
Write-Host "  프로젝트: $PROJECT_DIR"
Write-Host "  처음이면 IL2CPP + Gradle 로 10~20분 소요"
Write-Host "  로그: $LOG_DIR\02_build.log"
Write-Host "============================================================"

& $UNITY_BIN -batchmode -quit -nographics `
    -projectPath "$PROJECT_DIR" `
    -executeMethod BuildSpatialAnchorTest.PerformBuild `
    -buildTarget Android `
    -logFile "$LOG_DIR\02_build.log"

# APK 는 고정 파일명 대신 Build/*.apk 중 최신을 찾는다 (버전 bump 마다 파일명이 바뀜)
$apkInfo = Get-ChildItem "$PROJECT_DIR\Build\*.apk" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $apkInfo) {
    Write-Host ""
    Write-Host "  BUILD FAILED (Build/*.apk 없음). Log tail:"
    Get-Content "$LOG_DIR\02_build.log" -Tail 80
    exit 1
}

$APK_PATH = $apkInfo.FullName
Write-Host ""
Write-Host "============================================================"
Write-Host "  BUILD SUCCEEDED"
Write-Host "============================================================"
Write-Host "  APK:  $APK_PATH"
Write-Host "  Size: $([math]::Round($apkInfo.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "  install + run (안경 ADB 연결 후):"
Write-Host "    adb install -r `"$APK_PATH`""
Write-Host "    adb shell pm grant $PACKAGE_NAME android.permission.CAMERA"
Write-Host "    adb shell am start -n $PACKAGE_NAME/$LAUNCH_ACTIVITY"
Write-Host "  (정확한 activity 는 빌드 후 'adb shell dumpsys package $PACKAGE_NAME' 로 확인 권장)"
Write-Host "  로그:"
Write-Host "    adb logcat -s Unity:V SpatialAnchorTest:V"
