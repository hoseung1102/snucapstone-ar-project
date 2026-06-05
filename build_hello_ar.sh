#!/usr/bin/env bash
# Hello AR — Unity 프로젝트 생성 + 빌드 + 안경 설치 + 실행, 전부 CLI
#
# 전제: Unity 6000.1.11f1 + Android Build Support 모듈 설치 완료
# 사용: bash build_hello_ar.sh

set -euo pipefail

UNITY_BIN="/Applications/Unity/Hub/Editor/6000.1.11f1/Unity.app/Contents/MacOS/Unity"
PROJECT_DIR="$HOME/Desktop/AR_project/EagleEye_Unity"
ASSETS_PREP="$HOME/Desktop/AR_project/unity_assets_prep"
LOG_DIR="$HOME/Desktop/AR_project/unity_logs"
APK_PATH="$PROJECT_DIR/Build/EagleEye-HelloAR.apk"
PACKAGE_NAME="com.eagleeye.helloar"
# Unity 6 IL2CPP 기본 액티비티: UnityPlayerGameActivity (구버전은 UnityPlayerActivity)
# 어느 쪽이든 monkey로 LAUNCHER intent만 보내면 정확한 클래스 몰라도 실행됨

mkdir -p "$LOG_DIR"

echo ""
echo "============================================================"
echo "[1/5] Unity 프로젝트 생성: $PROJECT_DIR"
echo "============================================================"
if [ ! -d "$PROJECT_DIR/Assets" ]; then
    "$UNITY_BIN" -batchmode -quit -nographics \
        -createProject "$PROJECT_DIR" \
        -logFile "$LOG_DIR/01_create.log"
    echo "  Project created."
else
    echo "  Project already exists, skipping creation."
fi

echo ""
echo "============================================================"
echo "[2/5] Assets + Packages 복사"
echo "============================================================"
mkdir -p "$PROJECT_DIR/Assets/Scripts"
mkdir -p "$PROJECT_DIR/Assets/Editor"
mkdir -p "$PROJECT_DIR/Assets/Resources"
mkdir -p "$PROJECT_DIR/Packages"

# C# 스크립트
cp "$ASSETS_PREP/Scripts/"*.cs "$PROJECT_DIR/Assets/Scripts/"
cp "$ASSETS_PREP/Editor/"*.cs "$PROJECT_DIR/Assets/Editor/"

# Sentis 패키지 추가된 Packages/manifest.json
if [ -f "$ASSETS_PREP/Packages/manifest.json" ]; then
    cp "$ASSETS_PREP/Packages/manifest.json" "$PROJECT_DIR/Packages/manifest.json"
    echo "  manifest.json 복사됨 (Sentis 포함)"
fi

# 모델 파일 (Resources에 두면 Unity Sentis가 ModelAsset로 자동 import)
if [ -f "$HOME/Desktop/AR_project/yolo11n.onnx" ]; then
    cp "$HOME/Desktop/AR_project/yolo11n.onnx" "$PROJECT_DIR/Assets/Resources/yolo11n.onnx"
    echo "  yolo11n.onnx → Assets/Resources/"
fi

# 테스트 이미지 (Resources에 test_image.jpg로 복사)
if [ -f "$HOME/Desktop/AR_project/products/laptop_ref.jpg" ]; then
    cp "$HOME/Desktop/AR_project/products/laptop_ref.jpg" "$PROJECT_DIR/Assets/Resources/test_image.jpg"
    echo "  test_image.jpg → Assets/Resources/"
fi

ls -la "$PROJECT_DIR/Assets/Scripts/" "$PROJECT_DIR/Assets/Editor/" "$PROJECT_DIR/Assets/Resources/"

echo ""
echo "============================================================"
echo "[3/5] Unity batch-mode APK 빌드 (최초 10~20분, 의존성 import)"
echo "============================================================"
"$UNITY_BIN" -batchmode -quit -nographics \
    -projectPath "$PROJECT_DIR" \
    -executeMethod BuildHelloAR.PerformBuild \
    -buildTarget Android \
    -logFile "$LOG_DIR/03_build.log"

if [ ! -f "$APK_PATH" ]; then
    echo "  BUILD FAILED. Log tail:"
    tail -50 "$LOG_DIR/03_build.log"
    exit 1
fi
echo "  APK built: $(ls -lh "$APK_PATH" | awk '{print $5, $9}')"

echo ""
echo "============================================================"
echo "[4/5] 안경에 APK 설치"
echo "============================================================"
adb devices | grep -q "device$" || { echo "  안경 연결 안됨"; exit 1; }
adb install -r "$APK_PATH"

echo ""
echo "============================================================"
echo "[5/5] 안경에서 앱 실행 (am start, Unity 6 IL2CPP 기본 액티비티)"
echo "============================================================"
adb shell am start -n "${PACKAGE_NAME}/com.unity3d.player.UnityPlayerGameActivity" 2>&1 | tail -3 || \
    adb shell am start -n "${PACKAGE_NAME}/com.unity3d.player.UnityPlayerActivity" 2>&1 | tail -3
echo ""
echo "  실행 명령 보냈음. 안경 디스플레이 확인."
echo "  로그 모니터:  adb logcat -s Unity:V HelloAR:V CameraPreview:V"
echo "  종료:         adb shell am force-stop ${PACKAGE_NAME}"
