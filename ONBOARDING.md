# 🦅 Eagle Eye conquest AR — 이어받기 설명서

**레포**: https://github.com/hoseung1102/snucapstone-ar-project
**브랜치**: `feature/b24-integrated` · **빌드**: `b25-color-video` · **기기**: RayNeo X3 Pro · **Unity**: 2022.3.62f3 (전용)

> **다른 개발자에게**: 이 파일을 그대로 전달하세요. 레포를 열고 아래 "🤖 붙여넣기" 블록을 당신의 Claude Code(또는 Codex) 세션에 붙여넣으면 AI가 현황을 파악하고 바로 이어서 개발합니다.

---

## 한 줄 현황
콜라/펩시 페트병 응시 → CLIP이 "콜라병" 인식 → **색상**(빨강=코크/파랑=펩시)으로 브랜드 확정 → **경쟁사 영상**을 정면에 6DoF world-anchored로 재생. 온디바이스 동작 확인됨(2026-06-11).

## 🤖 당신의 Claude Code / Codex 에 이 문장을 붙여넣으세요
```
이 레포(snucapstone-ar-project)의 Eagle Eye conquest AR 데모를 이어받아 개발할 거야.
이 순서로 읽고 현황 파악해줘:
1) spatial_anchor_test/B25_DEMO_HANDOFF.md  ← 구조·결정·근거·알려진 이슈·빌드 런북 (제일 먼저)
2) docs/findings-2026-06-11-crash-slam-openxr.md  ← 크래시/SLAM 8Hz/OpenXR 근본 진단
3) AGENTS.md(루트, 자동 로드) + spatial_anchor_test/B22_TEST_RESULTS.md(OCR 실패 측정)
파악 끝나면 알려진 이슈 §4-1(한 물체에 광고 여러 번 뜨는 중복 spawn)부터 고치자.
⚠️ 빌드는 반드시 Unity 2022.3.62f3 (Unity 6 쓰면 안경에서 검은화면). 브랜치 feature/b24-integrated.
```

## 받기 + 빌드 (3분)
```bash
git clone https://github.com/hoseung1102/snucapstone-ar-project.git
cd snucapstone-ar-project && git checkout feature/b24-integrated
# Unity 2022.3.62f3 로 spatial_anchor_test 열기 (Unity 6 절대 금지 — 검은화면)
"<UnityHub>/2022.3.62f3/Editor/Unity.exe" -batchmode -quit -nographics -silent-crashes \
  -projectPath "$(pwd)/spatial_anchor_test" -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile "$(pwd)/spatial_anchor_test/Build/build.log"
# → spatial_anchor_test/Build/EagleEye-b25-color-video.apk  (모델·영상 브랜치에 포함, 따로 받을 것 없음)
```

## 설치 + 실행 (RayNeo X3 Pro adb)
```bash
SER=<adb devices 시리얼> ; PKG=com.eagleeye.helloar
adb -s $SER install -r spatial_anchor_test/Build/EagleEye-b25-color-video.apk
adb -s $SER shell pm grant $PKG android.permission.CAMERA
adb -s $SER reboot   # ★ 테스트 전 리부트 = 클린 CDSP (안 하면 재실행 시 기기 꺼질 수 있음)
adb -s $SER shell am start -n $PKG/com.rayneo.openxradapter.UnityOpenXrActivity
```
- 착용 → **머리 잠깐 움직여 SLAM 수렴**(정지면 SEEKING) → 콜라/펩시병 시야 중앙 → 머리 1초 정지 = 트리거
- 실시간 모니터: `python spatial_anchor_test/tools/monitor/eagle_monitor.py --serial $SER` (펀널 TRIGGER→COLA→MATCH→COKE/PEPSI + CLIP/SLAM/영상)
- 착용 없이 영상만 확인: `adb -s $SER shell "echo coca-cola > /sdcard/Android/data/$PKG/files/eyad_debug.txt"` (→펩시영상) / `echo pepsi`(→코크영상)

## 꼭 알 것 (함정)
- **Unity 2022.3.62f3 전용** — Unity 6 빌드 = 검은화면(RayNeo ARDK 비호환)
- **테스트 사이 리부트** — 공유 Hexagon CDSP 세션 leak으로 재실행 시 기기 꺼질 수 있음(근본수정 진행 중)
- **OCR는 제거됨** — 온디바이스에서 콜라 로고를 못 읽어 색상 판별로 전환(`skipOcr=true`)
- **공유 ADB** — install/reboot는 팀과 조율, 무선 ADB 끊지 말 것

## 지금 가장 중요한 다음 작업
**한 물체에 광고가 여러 번 뜨는 중복 spawn** (반복 트리거 → 재spawn). 수정방향: `maxAds=1` / brand dedup / 게이트 연장 중 택1. → `spatial_anchor_test/B25_DEMO_HANDOFF.md` §4-1, §6.

> 전체 맥락·결정 근거·파일 맵: **`spatial_anchor_test/B25_DEMO_HANDOFF.md`** (레포 안에 있음)
