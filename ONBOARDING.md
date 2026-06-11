# Eagle Eye conquest AR — 이어받기 온보딩

> **다른 개발자에게**: 아래를 그대로 **당신의 Claude Code(또는 Codex) 세션에 붙여넣으세요.** 이 레포를 열고 이 문장과 함께 시작하면 AI가 현재 구조·결정·다음 작업을 파악하고 바로 이어서 개발합니다.

---

## 🤖 Claude/Codex 에게 붙여넣을 문장 (복사)

```
이 레포(snucapstone-ar-project)의 Eagle Eye conquest AR 데모를 이어받아 개발할 거야.
먼저 이 순서로 읽고 현황을 파악해줘:
1) spatial_anchor_test/B25_DEMO_HANDOFF.md  ← 현재 구조·결정·근거·알려진 이슈·빌드 런북 (제일 먼저)
2) docs/findings-2026-06-11-crash-slam-openxr.md  ← 크래시/SLAM 8Hz/OpenXR 표면 근본 진단
3) AGENTS.md (루트, 자동 로드됨) + spatial_anchor_test/B22_TEST_RESULTS.md (OCR 실패 측정)
파악 끝나면 "알려진 이슈 §4" 중 1번(한 물체에 광고 여러 번 뜨는 중복 spawn)부터 고치자.
⚠️ 빌드는 반드시 Unity 2022.3.62f3 (Unity 6 쓰면 안경에서 검은화면). 브랜치 feature/b24-integrated.
```

---

## 한 줄 현황
콜라/펩시 페트병 응시 → CLIP 이 "콜라병" 인식 → **색상**(빨강=코크/파랑=펩시)으로 브랜드 확정 → **경쟁사 영상**을 정면에 6DoF world-anchored 로 재생. RayNeo X3 Pro 에서 동작 확인됨(2026-06-11). 빌드 `b25-color-video`, 브랜치 `feature/b24-integrated`.

## 받기 + 빌드 (3분)
```bash
git clone https://github.com/hoseung1102/snucapstone-ar-project.git
cd snucapstone-ar-project && git checkout feature/b24-integrated
# Unity 2022.3.62f3 로 spatial_anchor_test 열기 (Unity 6 절대 금지 — 검은화면).
# 빌드: 메뉴 Build > SpatialAnchor APK  또는 batchmode:
"<UnityHub>/2022.3.62f3/Editor/Unity.exe" -batchmode -quit -nographics -silent-crashes \
  -projectPath "$(pwd)/spatial_anchor_test" -buildTarget Android \
  -executeMethod BuildSpatialAnchorTest.PerformBuild \
  -logFile "$(pwd)/spatial_anchor_test/Build/build.log"
# → spatial_anchor_test/Build/EagleEye-b25-color-video.apk
```
모델·영상은 브랜치에 포함됨(따로 받을 것 없음).

## 설치 + 실행 (RayNeo X3 Pro adb)
```bash
SER=<adb devices 시리얼> ; PKG=com.eagleeye.helloar
adb -s $SER install -r spatial_anchor_test/Build/EagleEye-b25-color-video.apk
adb -s $SER shell pm grant $PKG android.permission.CAMERA
adb -s $SER reboot   # ★ 테스트 전 리부트 = 클린 CDSP (안 하면 재실행 시 기기 꺼질 수 있음)
adb -s $SER shell am start -n $PKG/com.rayneo.openxradapter.UnityOpenXrActivity
```
- 착용 → **머리 잠깐 움직여 SLAM 수렴**(정지면 SEEKING) → 콜라/펩시병을 시야 중앙 → 머리 1초 정지 = 트리거.
- 실시간 모니터: `python spatial_anchor_test/tools/monitor/eagle_monitor.py --serial $SER` (펀널 TRIGGER→COLA→MATCH→COKE/PEPSI + CLIP/SLAM/영상).
- 착용 없이 영상만 확인: `adb -s $SER shell "echo coca-cola > /sdcard/Android/data/$PKG/files/eyad_debug.txt"` (→펩시영상) / `echo pepsi`(→코크영상).

## 꼭 알 것 (함정)
- **Unity 2022.3.62f3 전용.** Unity 6 빌드 = 검은화면(RayNeo ARDK 비호환).
- **테스트 사이 리부트** 권장 — 공유 Hexagon CDSP 세션 leak 으로 재실행 시 기기 꺼질 수 있음(근본수정 진행 중).
- **OCR 는 뺐다** — 온디바이스에서 콜라 로고를 못 읽어서(측정 완료) 색상 판별로 전환. `skipOcr=true`.
- **공유 ADB 환경** — install/reboot 등 writing 은 팀과 사전 조율, 무선 ADB 끊지 말 것.

## 지금 가장 중요한 다음 작업
`spatial_anchor_test/B25_DEMO_HANDOFF.md` §4(알려진 이슈) — 특히 **#1 한 물체에 광고 중복 spawn**(트리거 반복 → 재spawn). §6 에 우선순위.

전체 맥락·결정 근거·파일 맵은 **`spatial_anchor_test/B25_DEMO_HANDOFF.md`** 에 다 있습니다.
