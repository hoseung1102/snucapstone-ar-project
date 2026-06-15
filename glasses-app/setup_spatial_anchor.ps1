# SpatialAnchorTest — 1단계: Unity 프로젝트 생성 + 의존성 import
# 빌드는 별도 (build_spatial_anchor.ps1).
# 이 스크립트 끝나면 사용자가 Unity Editor 에서 XR Plug-in Management 수동 셋업해야 함.

$ErrorActionPreference = "Stop"

$UNITY_BIN     = "C:\Program Files\Unity\Hub\Editor\6000.0.76f1\Editor\Unity.exe"
$PROJECT_DIR   = "C:\claude\staging\spatial_anchor_unity"
$ASSETS_PREP   = "C:\claude\staging\snucapstone-ar\repo\glasses-app"
$ARDK_SRC      = "C:\claude\staging\rayneo-ardk\extracted\RayNeo OpenXR Unity ARDK"
$ARDK_PKG_NAME = "com.unity.xr.rayneo.openxr"
$LOG_DIR       = "$PROJECT_DIR\logs"

New-Item -ItemType Directory -Force -Path $LOG_DIR | Out-Null

Write-Host ""
Write-Host "============================================================"
Write-Host "[1/4] Unity 프로젝트 생성: $PROJECT_DIR"
Write-Host "============================================================"
if (-not (Test-Path "$PROJECT_DIR\Assets")) {
    & $UNITY_BIN -batchmode -quit -nographics `
        -createProject "$PROJECT_DIR" `
        -logFile "$LOG_DIR\01_create.log"
    Write-Host "  Project created."
} else {
    Write-Host "  Project already exists."
}

Write-Host ""
Write-Host "============================================================"
Write-Host "[2/4] ARDK 패키지 embedded 복사"
Write-Host "  $ARDK_SRC"
Write-Host "  → $PROJECT_DIR\Packages\$ARDK_PKG_NAME"
Write-Host "============================================================"
$ardkDst = "$PROJECT_DIR\Packages\$ARDK_PKG_NAME"
if (Test-Path $ardkDst) {
    Write-Host "  이미 존재. 재복사하려면 폴더 먼저 삭제."
} else {
    Copy-Item -Recurse $ARDK_SRC $ardkDst
    Write-Host "  ARDK 복사 완료."
}

Write-Host ""
Write-Host "============================================================"
Write-Host "[3/4] Assets + Packages/manifest.json 복사"
Write-Host "============================================================"
New-Item -ItemType Directory -Force -Path "$PROJECT_DIR\Assets\Scripts"   | Out-Null
New-Item -ItemType Directory -Force -Path "$PROJECT_DIR\Assets\Editor"    | Out-Null
New-Item -ItemType Directory -Force -Path "$PROJECT_DIR\Assets\Resources" | Out-Null
New-Item -ItemType Directory -Force -Path "$PROJECT_DIR\Assets\Scenes"    | Out-Null

Copy-Item "$ASSETS_PREP\Assets\Scripts\*.cs"   "$PROJECT_DIR\Assets\Scripts\"   -Force
Copy-Item "$ASSETS_PREP\Assets\Editor\*.cs"    "$PROJECT_DIR\Assets\Editor\"    -Force
Copy-Item "$ASSETS_PREP\Assets\Resources\*.*"  "$PROJECT_DIR\Assets\Resources\" -Force
Copy-Item "$ASSETS_PREP\Packages\manifest.json" "$PROJECT_DIR\Packages\manifest.json" -Force

Write-Host "  복사된 파일:"
Get-ChildItem "$PROJECT_DIR\Assets\Scripts","$PROJECT_DIR\Assets\Editor","$PROJECT_DIR\Assets\Resources" | Select-Object Name

Write-Host ""
Write-Host "============================================================"
Write-Host "[4/4] Unity 패키지 import (registry 의존성 다운로드)"
Write-Host "  처음이라 ~3~5분 소요"
Write-Host "============================================================"
& $UNITY_BIN -batchmode -quit -nographics `
    -projectPath "$PROJECT_DIR" `
    -logFile "$LOG_DIR\02_import.log"

Write-Host ""
Write-Host "============================================================"
Write-Host "  SETUP 완료"
Write-Host "============================================================"
Write-Host ""
Write-Host "  다음 단계 — 사용자가 GUI 로 한 번 셋업:"
Write-Host "    1. Unity Hub 열고 'Open' → $PROJECT_DIR 선택"
Write-Host "    2. 프로젝트 열린 후 Edit → Project Settings"
Write-Host "    3. 왼쪽 메뉴 'XR Plug-in Management' 클릭"
Write-Host "       (만약 'Install XR Plug-in Management' 버튼 보이면 클릭해서 설치)"
Write-Host "    4. 상단 탭 중 Android (◻) 선택"
Write-Host "    5. 'Plug-in Providers' 목록에서 'RayNeo OpenXR' 체크박스 ON"
Write-Host "       (만약 OpenXR 만 있고 RayNeo 가 안 보이면, 그것만 체크해도 됨 — Loader 가 OpenXR 의 feature 로 들어가는 경우 있음)"
Write-Host "    6. 그 아래 OpenXR Feature Groups 에서 RayNeo 관련 항목들 체크"
Write-Host "    7. 저장 후 Unity Editor 종료"
Write-Host ""
Write-Host "  그 다음:"
Write-Host "    pwsh build_spatial_anchor.ps1"
