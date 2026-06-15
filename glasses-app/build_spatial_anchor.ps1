# SpatialAnchorTest — 2단계: batch-mode APK 빌드
# 전제: setup_spatial_anchor.ps1 실행 완료 + Unity Editor 에서 XR Plug-in Management 셋업 완료

$ErrorActionPreference = "Stop"

$UNITY_BIN    = "C:\Program Files\Unity\Hub\Editor\6000.0.76f1\Editor\Unity.exe"
$PROJECT_DIR  = "C:\claude\staging\spatial_anchor_unity"
$LOG_DIR      = "$PROJECT_DIR\logs"
$APK_PATH     = "$PROJECT_DIR\Build\EagleEye-SpatialAnchor.apk"
$PACKAGE_NAME = "com.eagleeye.spatialanchor"

if (-not (Test-Path "$PROJECT_DIR\Assets")) {
    Write-Host "  $PROJECT_DIR 가 비어있음. 먼저 setup_spatial_anchor.ps1 실행 필요."
    exit 1
}

New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null

Write-Host ""
Write-Host "============================================================"
Write-Host "  Unity batch-mode APK 빌드"
Write-Host "  처음이면 IL2CPP 컴파일 + Gradle 로 10~20분 소요"
Write-Host "  로그: $LOG_DIR\03_build.log"
Write-Host "============================================================"

& $UNITY_BIN -batchmode -quit -nographics `
    -projectPath "$PROJECT_DIR" `
    -executeMethod BuildSpatialAnchorTest.PerformBuild `
    -buildTarget Android `
    -logFile "$LOG_DIR\03_build.log"

if (-not (Test-Path $APK_PATH)) {
    Write-Host ""
    Write-Host "  BUILD FAILED. Log tail:"
    Get-Content "$LOG_DIR\03_build.log" -Tail 80
    exit 1
}

$apkInfo = Get-Item $APK_PATH
Write-Host ""
Write-Host "============================================================"
Write-Host "  BUILD SUCCEEDED"
Write-Host "============================================================"
Write-Host "  APK: $APK_PATH"
Write-Host "  Size: $([math]::Round($apkInfo.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "  install (안경 ADB 연결 후, 본인 직접):"
Write-Host "    adb install -r `"$APK_PATH`""
Write-Host "    adb shell am start -n `"${PACKAGE_NAME}/com.unity3d.player.UnityPlayerGameActivity`""
Write-Host "  log:"
Write-Host "    adb logcat -s Unity:V SpatialAnchorTest:V"
Write-Host "  종료:"
Write-Host "    adb shell am force-stop $PACKAGE_NAME"
