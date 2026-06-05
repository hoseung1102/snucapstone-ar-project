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
ACTIVITY="com.unity3d.player.UnityPlayerActivity"

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
echo "[2/5] Assets 복사 (HelloAR.cs + BuildHelloAR.cs)"
echo "============================================================"
mkdir -p "$PROJECT_DIR/Assets/Scripts"
mkdir -p "$PROJECT_DIR/Assets/Editor"
cp "$ASSETS_PREP/Scripts/HelloAR.cs" "$PROJECT_DIR/Assets/Scripts/"
cp "$ASSETS_PREP/Editor/BuildHelloAR.cs" "$PROJECT_DIR/Assets/Editor/"
ls -la "$PROJECT_DIR/Assets/Scripts/" "$PROJECT_DIR/Assets/Editor/"

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
echo "[5/5] 안경에서 앱 실행"
echo "============================================================"
adb shell am start -n "${PACKAGE_NAME}/${ACTIVITY}"
echo ""
echo "  실행 명령 보냈음. 안경 디스플레이 확인."
echo "  로그 모니터:  adb logcat -s Unity:V HelloAR:V"
echo "  종료:         adb shell am force-stop ${PACKAGE_NAME}"
