# AGENTS.md — spatial_anchor_test 빌드 지침 (요약)

> 전체 지침(아키텍처·파이프라인·함정·워크플로우)은 레포 루트 [`../AGENTS.md`](../AGENTS.md) 참조.
> 이 파일은 **이 서브프로젝트 한정 빌드 핵심**만 요약한다.

## ★ Unity 2022.3.62f3 전용 — Unity 6 절대 금지

- 올바른 에디터: `C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe`
- `ProjectSettings/ProjectVersion.txt` 가 `2022.3.62f3` 인지 빌드 전 확인.
- **Unity 6000.0.76f1 로 열지 말 것** — RayNeo ARDK 비호환 → 안경에서 검은화면(Unity 로고도 안 뜸) + `ProjectVersion.txt`/settings 오염 (실제 발생). 오염 시 `git checkout -- ProjectSettings/` 로 복구.

## batchmode 빌드 명령

```bash
"C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe" \
  -batchmode -nographics -quit -silent-crashes \
  -projectPath C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test \
  -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile C:/claude/staging/snucapstone-ar/repo/spatial_anchor_test/Build/build.log
```

- 진입점·버전 상수: `Assets/Editor/BuildSpatialAnchorTest.cs` 의 `BUILD_TAG` + `OUTPUT_APK` (새 버전 시 **둘 다** bump).
- 빌드 hook(OpenXR 로더 fix / 도구 경로 폴백 / RayNeo settings preload / boot.config 패처)은 자동 — 손대지 말 것.
- 동봉 `build_2022.ps1` 의 `PROJECT_DIR` 는 옛 경로를 가리키니, 이 레포 경로로 빌드하려면 위 명령을 직접 쓸 것.

## 빌드 후 검증

- `aapt dump badging <apk>` 와 `adb shell dumpsys package <pkg>` 의 versionName 이 둘 다 `BUILD_TAG` 와 일치 (Bee 캐시 stale 방지). 불일치면 `Library/Bee` 정리 후 재빌드.
- `ProjectVersion.txt == 2022.3.62f3` 유지 재확인.

## 런타임 검은화면 (빌드는 됐는데 안경에 아무것도 안 뜸)

- Unity 로고조차 안 뜨면 두 원인: (1) **Unity 6 빌드**(위 §), (2) **RayNeo XR 시스템 서비스 사망** → `adb reboot` 로만 복구. 시그니처(`DeadSystemException`/`FFalconXRClient.loadProfile` NPE/`Need to set FrameLayout`/`openxr session had not being inited`)와 대응은 루트 [`../AGENTS.md`](../AGENTS.md) §4 "검은화면 = 두 원인 구분" 참조.
- 빌드 자체가 안 되거나 HUD 고정/Unity 6 관련 결정은 루트 `../AGENTS.md` §4b 참조 (Unity 6 = NO 확정).
