# Unity 2022.3 LTS + ARDK 1.1.2 setup
# 전제: Unity 2022.3.62f3 (또는 비슷한 2022.3.x patch) + Android Build Support 설치 완료
# 또한 spatial_anchor_unity_2022 의 모든 file 이 이미 staging 에 있음:
#   - Assets/Scripts/SpatialAnchorTest.cs
#   - Assets/Editor/BuildSpatialAnchorTest.cs
#   - Assets/Resources/tiger_anchor.png
#   - Assets/Plugins/Android/AndroidManifest.xml (RayNeoUnityPlayerActivity override)
#   - Packages/manifest.json
#   - Packages/com.unity.xr.rayneo.openxr/ (ARDK embedded)

$ErrorActionPreference = "Stop"

# Unity 2022 실행 파일 — 사용자가 install 한 정확한 patch version 으로 수정 필요
$UNITY_VERSION = "2022.3.62f3"
$UNITY_BIN     = "C:\Program Files\Unity\Hub\Editor\$UNITY_VERSION\Editor\Unity.exe"

$PROJECT_DIR = "C:\claude\staging\spatial_anchor_unity_2022"
$LOG_DIR     = "$PROJECT_DIR\logs"

if (-not (Test-Path $UNITY_BIN)) {
    Write-Host "  Unity 2022 실행 파일 못 찾음: $UNITY_BIN"
    Write-Host "  설치된 Unity 2022.3.x patch version 을 \$UNITY_VERSION 에 수정 필요"
    exit 1
}

New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null

Write-Host ""
Write-Host "============================================================"
Write-Host "[1/2] Unity 2022.3 LTS 패키지 import (Asset/Plugins/Packages 다 이미 있음)"
Write-Host "  처음이라 ~3~5분 소요"
Write-Host "============================================================"
& $UNITY_BIN -batchmode -quit -nographics `
    -projectPath "$PROJECT_DIR" `
    -logFile "$LOG_DIR\01_import.log"

Write-Host ""
Write-Host "============================================================"
Write-Host "  SETUP 완료"
Write-Host "============================================================"
Write-Host ""
Write-Host "  다음 단계 — Unity Editor GUI 에서 XR Plug-in Management 셋업:"
Write-Host "    1. Unity Hub → spatial_anchor_unity_2022 프로젝트 열기"
Write-Host "    2. Edit → Project Settings → XR Plug-in Management"
Write-Host "    3. Android 탭 → 'OpenXR' 체크 + 그 아래 'RayNeo XR' feature group 체크"
Write-Host "    4. Fix All (validation issues)"
Write-Host "    5. Editor 종료 후 build_2022.ps1 실행"
