using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

// Unity batch-mode 빌드 진입점.
// 호출:
//   Unity -batchmode -quit -projectPath <path> \
//         -executeMethod BuildSpatialAnchorTest.PerformBuild \
//         -buildTarget Android -logFile <log>
public static class BuildSpatialAnchorTest
{
    // b22 OCR+SLAM 통합: 원본 real-pipeline package(spatialanchor.bisection)는 옆 세션 점유 → helloar override.
    const string PACKAGE_NAME = "com.eagleeye.helloar";
    const string PRODUCT_NAME = "EagleEye OCR+SLAM";
    const string COMPANY_NAME = "Eagle Eye";
    // ★ 새 빌드/시도마다 이 BUILD_TAG 한 줄만 bump 한다 (예: "b27-<한줄설명>").
    //   APK 파일명(OUTPUT_APK)·기기 dumpsys 식별이 여기서 파생됨. (CLAUDE.md "새 빌드/시도" 참조)
    //   빌드 식별용 버전 스탬프 — 기기에서 dumpsys 로 어느 빌드가 설치됐는지 검증.
    const string BUILD_TAG    = "b26-dedup-checkout";
    const string OUTPUT_APK   = "Build/EagleEye-" + BUILD_TAG + ".apk";  // BUILD_TAG 에서 파생 — 따로 안 고침
    const string SCENE_PATH   = "Assets/Scenes/SpatialAnchorScene.unity";

    [MenuItem("Build/SpatialAnchor APK")]
    public static void PerformBuild()
    {
        Debug.Log("[BuildSpatialAnchorTest] === starting ===");
        Debug.Log($"[BuildSpatialAnchorTest] active build target: {EditorUserBuildSettings.activeBuildTarget}");

        // -buildTarget Android CLI 인자가 이미 Active target 셋팅함.
        // 여기서 SwitchActiveBuildTarget 을 다시 호출하면 domain reload 가 트리거되어
        // PerformBuild 의 나머지 코드가 끊김. 호출하지 않는다.

        EnsureOpenXRLoader();        // ★ OpenXR 로더가 제대로 된 IXRLoaderPreInit 타입인지 보장 (build hook 전체 실행 → lib+pre-init 자동 포함)
        ConfigureExternalTools();   // ★ JDK/SDK/NDK/Gradle 경로 강제 set (batch 가 GUI Preferences 못 봄)
        ConfigurePlayerSettings();
        ConfigureAndroidSettings();
        EnsureRayNeoSettingsPreloaded();  // ★ RayNeoXRGeneralSettings 를 PreloadedAssets 에 주입 (안 하면 런타임 SLAM pose 죽음)

        // PlayerSettings 변경을 ProjectSettings.asset 에 즉시 flush.
        // 이게 빠지면 OpenXR OnPreprocessBuild 가 이전 캐시 값을 봐서 minSdk validation 으로 빌드 실패.
        AssetDatabase.SaveAssets();

        EnsureScene();

        Directory.CreateDirectory(Path.GetDirectoryName(OUTPUT_APK));

        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = new[] { SCENE_PATH },
            locationPathName = OUTPUT_APK,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary s = report.summary;

        if (s.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildSpatialAnchorTest] === SUCCEEDED ===  apk={OUTPUT_APK}  size={s.totalSize / 1024 / 1024}MB  t={s.totalTime}");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildSpatialAnchorTest] === FAILED ===  result={s.result}  errors={s.totalErrors}");
            EditorApplication.Exit(1);
        }
    }

    static void EnsureScene()
    {
        if (File.Exists(SCENE_PATH))
        {
            Debug.Log($"[BuildSpatialAnchorTest] scene exists: {SCENE_PATH}");
            return;
        }
        Debug.Log($"[BuildSpatialAnchorTest] creating scene: {SCENE_PATH}");
        Directory.CreateDirectory("Assets/Scenes");

        // 빈 scene 부터 시작 (DefaultGameObjects 의 기본 Camera 는 ARDK rig 와 충돌)
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ARDK 의 표준 XR rig 를 instantiate.
        // SDK/Runtime/Resources/Prefab/XR Plugin.prefab → Resources.Load 경로는 "Prefab/XR Plugin"
        // 이 prefab 안에 CameraOffset/Head (MainCamera) + HeadTrackedPoseDriver + EventSystem 다 들어있음.
        // OpenXR XR loader 가 활성화되어 있으면 Head 의 단일 Camera 가 양쪽 눈에 자동 stereo rendering.
        GameObject xrPrefab = Resources.Load<GameObject>("Prefab/XR Plugin");
        if (xrPrefab == null)
        {
            Debug.LogError("[BuildSpatialAnchorTest] Resources/Prefab/XR Plugin.prefab 못 찾음. " +
                           "RayNeo OpenXR ARDK 패키지가 import 됐는지 확인.");
            // 폴백: 그냥 기본 카메라 — stereo 안 됨
            new GameObject("DirectionalLight").AddComponent<Light>().type = LightType.Directional;
            GameObject cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            cam.AddComponent<Camera>();
        }
        else
        {
            PrefabUtility.InstantiatePrefab(xrPrefab);
            Debug.Log("[BuildSpatialAnchorTest] XR Plugin prefab instantiated.");

            // 조명 — XR Plugin 에는 light 가 없을 수도. 안전하게 directional light 추가.
            var lt = new GameObject("Directional Light").AddComponent<Light>();
            lt.type = LightType.Directional;
            lt.transform.rotation = Quaternion.Euler(50, -30, 0);
            lt.intensity = 1.0f;
        }

        // 호스트 GameObject — SpatialAnchorTest 스크립트 부착
        GameObject host = new GameObject("SpatialAnchorHost");
        host.AddComponent(System.Type.GetType("SpatialAnchorTest, Assembly-CSharp"));

        EditorSceneManager.SaveScene(scene, SCENE_PATH);
    }

    // RayNeo SDK 의 RayNeoXRGeneralSettings 에셋을 PlayerSettings PreloadedAssets 에 보장.
    // 없으면 런타임 [RuntimeInitializeOnLoadMethod] XRSDK.Initialize() 가 RayNeoXRGeneralSettings.Instance(null)
    // 접근 → NullReferenceException → RayNeo pose 파이프라인 미초기화 → HeadTrackedPoseDriver 가 SLAM pose 못 받음
    // → 카메라 transform 정지(camPos=0,0,0) → 렌더 head-locked, 6DoF 죽음.
    // 정상 빌드는 Project Settings > RayNeo GUI 의 ConfigPreloadInfo() 가 주입하지만 batchmode 는 GUI 미실행이라 직접 주입.
    const string RAYNEO_SETTINGS = "Assets/XR/RayNeoGeneralSettings.asset";
    static void EnsureRayNeoSettingsPreloaded()
    {
        UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(RAYNEO_SETTINGS);
        if (asset == null)
        {
            Debug.LogError($"[BuildSpatialAnchorTest] {RAYNEO_SETTINGS} 못 찾음 — RayNeo SDK 설정 누락, SLAM pose 안 뜸");
            return;
        }
        var preloaded = PlayerSettings.GetPreloadedAssets().Where(a => a != null).ToList();
        if (!preloaded.Contains(asset))
        {
            preloaded.Add(asset);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            Debug.Log("[BuildSpatialAnchorTest] RayNeoXRGeneralSettings → PreloadedAssets 주입 완료");
        }
        else
        {
            Debug.Log("[BuildSpatialAnchorTest] RayNeoXRGeneralSettings 이미 PreloadedAssets 에 있음");
        }
    }

    // OpenXR 로더가 IXRLoaderPreInit 구현체(UnityEngine.XR.OpenXR.OpenXRLoader)로 Android 에 등록됐는지 보장.
    // 현재 로더 타입을 로그로 출력(NoPreInit/dangling 진단). 잘못됐으면 제거 후 OpenXRLoader 재할당 →
    // OpenXR build hook 이 libUnityOpenXR.so + xrsdk-pre-init-library 를 자동 포함하게 됨.
    static void EnsureOpenXRLoader()
    {
        try
        {
            XRGeneralSettingsPerBuildTarget bts;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out bts) || bts == null)
            {
                Debug.LogError("[EnsureOpenXRLoader] XRGeneralSettingsPerBuildTarget 없음");
                return;
            }
            XRGeneralSettings settings = bts.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings == null || settings.Manager == null)
            {
                Debug.LogError("[EnsureOpenXRLoader] Android XRGeneralSettings/Manager 없음");
                return;
            }
            XRManagerSettings mgr = settings.Manager;
            foreach (var l in mgr.activeLoaders)
                Debug.Log("[EnsureOpenXRLoader] BEFORE loader: " + (l == null ? "NULL(dangling)" : l.GetType().FullName));

            const string OPENXR_LOADER = "UnityEngine.XR.OpenXR.OpenXRLoader";
            bool hasPreInit = mgr.activeLoaders.Any(l => l != null && l.GetType().FullName == OPENXR_LOADER);
            if (!hasPreInit)
            {
                // 기존(잘못된/dangling) 로더 모두 제거 후 OpenXRLoader 재할당
                var existing = mgr.activeLoaders.Where(l => l != null).Select(l => l.GetType().FullName).ToList();
                foreach (var tn in existing)
                {
                    bool rm = XRPackageMetadataStore.RemoveLoader(mgr, tn, BuildTargetGroup.Android);
                    Debug.Log($"[EnsureOpenXRLoader] RemoveLoader {tn} => {rm}");
                }
                bool ok = XRPackageMetadataStore.AssignLoader(mgr, OPENXR_LOADER, BuildTargetGroup.Android);
                Debug.Log("[EnsureOpenXRLoader] AssignLoader OpenXRLoader => " + ok);
                EditorUtility.SetDirty(mgr);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            else Debug.Log("[EnsureOpenXRLoader] OpenXRLoader 이미 등록됨");

            foreach (var l in mgr.activeLoaders)
                Debug.Log("[EnsureOpenXRLoader] AFTER loader: " + (l == null ? "NULL" : l.GetType().FullName));
        }
        catch (System.Exception e) { Debug.LogError("[EnsureOpenXRLoader] " + e.Message + "\n" + e.StackTrace); }
    }

    // 외부 도구(JDK/SDK/NDK/Gradle) 설정. 에디터에 번들(embedded)이 있으면 그걸, 없으면 머신에 설치된
    // Android Studio SDK 등을 자동 탐색해 EditorPrefs 로 지정한다. 하드코딩 경로 없음(팀/머신 중립).
    // ※ Unity 6 의 AndroidExternalToolsSettings API 는 2022.3 에 없어(CS0103) EditorPrefs 로만 처리 — 전 버전 호환.
    //   (RayNeo 호환 위해 Unity 6 → 2022.3.62f3 다운그레이드한 잔재 코드였음.) 셋업: docs/dev-environment.md §1~2.
    static void ConfigureExternalTools()
    {
        string ap = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");

        // Gradle: 항상 에디터 번들(AndroidPlayer\Tools\gradle) 사용
        EditorPrefs.SetBool("GradleUseEmbedded", true);

        // JDK
        if (Directory.Exists(Path.Combine(ap, "OpenJDK")))
        {
            EditorPrefs.SetBool("JdkUseEmbedded", true);
            Debug.Log("[BuildSpatialAnchorTest] JDK = embedded");
        }
        else
        {
            string jdk = FindJdk();
            EditorPrefs.SetBool("JdkUseEmbedded", false);
            if (jdk != null) { EditorPrefs.SetString("JdkPath", jdk); Debug.Log("[BuildSpatialAnchorTest] JDK = " + jdk); }
            else Debug.LogWarning("[BuildSpatialAnchorTest] JDK 못 찾음 — OpenJDK 모듈 설치 또는 JAVA_HOME 설정 필요");
        }

        // SDK
        if (Directory.Exists(Path.Combine(ap, "SDK")))
        {
            EditorPrefs.SetBool("SdkUseEmbedded", true);
            Debug.Log("[BuildSpatialAnchorTest] SDK = embedded");
        }
        else
        {
            string sdk = FindAndroidSdk();
            EditorPrefs.SetBool("SdkUseEmbedded", false);
            if (sdk != null) { EditorPrefs.SetString("AndroidSdkRoot", sdk); Debug.Log("[BuildSpatialAnchorTest] SDK = " + sdk); }
            else Debug.LogError("[BuildSpatialAnchorTest] Android SDK 못 찾음 — ANDROID_SDK_ROOT 또는 %LOCALAPPDATA%\\Android\\Sdk");
        }

        // NDK (Unity 2022.3 = r23b / 23.1.7779620). EditorPrefs 키가 버전 접미사로 바뀌어서 변형을 모두 set.
        if (Directory.Exists(Path.Combine(ap, "NDK")))
        {
            EditorPrefs.SetBool("NdkUseEmbedded", true);
            Debug.Log("[BuildSpatialAnchorTest] NDK = embedded");
        }
        else
        {
            string ndk = FindNdk();
            EditorPrefs.SetBool("NdkUseEmbedded", false);
            if (ndk != null)
            {
                EditorPrefs.SetString("AndroidNdkRoot", ndk);
                EditorPrefs.SetString("AndroidNdkRootR23B", ndk);
                EditorPrefs.SetString("AndroidNdkRootR23b", ndk);
                Debug.Log("[BuildSpatialAnchorTest] NDK = " + ndk);
            }
            else Debug.LogError("[BuildSpatialAnchorTest] NDK r23b(23.1.7779620) 못 찾음 — sdkmanager \"ndk;23.1.7779620\"");
        }
    }

    static string FindJdk()
    {
        string jh = System.Environment.GetEnvironmentVariable("JAVA_HOME");
        var cands = new[] { jh, @"C:\Program Files\Android\Android Studio\jbr" };
        foreach (var c in cands)
            if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "bin", "javac.exe"))) return c;
        foreach (var root in new[] { @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Java" })
            if (Directory.Exists(root))
                foreach (var d in Directory.GetDirectories(root, "jdk-17*"))
                    if (File.Exists(Path.Combine(d, "bin", "javac.exe"))) return d;
        return null;
    }

    static string FindAndroidSdk()
    {
        var cands = new[]
        {
            System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            System.Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
        };
        foreach (var c in cands)
            if (!string.IsNullOrEmpty(c) && Directory.Exists(Path.Combine(c, "platform-tools"))) return c;
        return null;
    }

    static string FindNdk()
    {
        string sdk = FindAndroidSdk();
        if (sdk == null) return null;
        string exact = Path.Combine(sdk, "ndk", "23.1.7779620");   // Unity 2022.3 기대 버전
        if (Directory.Exists(exact)) return exact;
        string ndkRoot = Path.Combine(sdk, "ndk");
        if (Directory.Exists(ndkRoot))
        {
            var dirs = Directory.GetDirectories(ndkRoot);
            if (dirs.Length > 0) { System.Array.Sort(dirs); return dirs[0]; }   // r23b 없으면 가장 낮은 것(버전 경고 가능)
        }
        return null;
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = COMPANY_NAME;
        PlayerSettings.productName = PRODUCT_NAME;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PACKAGE_NAME);

        // 버전 스탬프 (기기에서 어느 빌드 설치됐는지 dumpsys 검증용) + versionCode bump
        PlayerSettings.bundleVersion = BUILD_TAG;
        PlayerSettings.Android.bundleVersionCode = PlayerSettings.Android.bundleVersionCode + 1;

        PlayerSettings.colorSpace = ColorSpace.Linear;

        // RayNeoSupportFeature 의 validation 이 graphics[0] == OpenGLES3 강제.
        // Vulkan first 면 RayNeo runtime 이 surface 못 잡아서 runtime fail.
        // (Discord 보고와 일치 — RayNeo 가 OpenGL ES 만 사용)
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

        // RayNeo validation 이 LandscapeLeft 강제
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
    }

    static void ConfigureAndroidSettings()
    {
        // RayNeo X3 Pro = Android 12 (API 32). OpenXR plugin 은 minSdk 30 이상 강제.
        // targetSdk 는 34 — androidx.activity:1.7.1 등 의존성이 compileSdk 33+ 요구.
        // (compileSdk 는 Unity 가 targetSdk 에 맞춰 자동 설정. RayNeo X3 Pro 의 OS 가 API 32 라도
        //  targetSdk 34 앱은 정상 동작 — OS 가 targetSdk 에 맞춰 behavior 적용.)
        PlayerSettings.Android.minSdkVersion    = AndroidSdkVersions.AndroidApiLevel30;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
    }
}

// gradle 프로젝트 생성 직후(APK 패키징 전) boot.config 에 누락된 XR pre-init 키 주입.
// 원인: 우리 빌드는 xrsdk-pre-init-library 를 안 내보내서(loader gate) splash 시점에 OpenXR 가
// 제대로 init 안 됨 → view/head 트래킹 미수립 → centerEye=0 → head-locked. vendor APK 의 boot.config 와 일치시킴.
public class RayNeoBootConfigPatcher : UnityEditor.Android.IPostGenerateGradleAndroidProject
{
    public int callbackOrder { get { return 999; } }
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        try
        {
            string bc = Path.Combine(path, "src/main/assets/bin/Data/boot.config");
            if (!File.Exists(bc))
            {
                string[] hits = Directory.GetFiles(path, "boot.config", SearchOption.AllDirectories);
                if (hits.Length > 0) bc = hits[0];
            }
            if (!File.Exists(bc))
            {
                Debug.LogError("[RayNeoBootConfigPatcher] boot.config 못 찾음 under " + path);
                return;
            }
            string txt = File.ReadAllText(bc);
            bool changed = false;
            if (!txt.Contains("xrsdk-pre-init-library")) { txt += "\nxrsdk-pre-init-library=UnityOpenXR\n"; changed = true; }
            if (!txt.Contains("gfx-disable-mt-rendering")) { txt += "gfx-disable-mt-rendering=1\n"; changed = true; }
            if (changed)
            {
                File.WriteAllText(bc, txt);
                Debug.Log("[RayNeoBootConfigPatcher] boot.config 에 xrsdk-pre-init-library 주입: " + bc);
            }
            else Debug.Log("[RayNeoBootConfigPatcher] boot.config 이미 pre-init 키 있음");

            // ★ libUnityOpenXR.so 강제 포함. OpenXR build hook 이 우리 로더를 비활성으로 판단해
            // 이 native 플러그인을 누락시킴(vendor 작동 APK 엔 있고 우리엔 없던 결정적 차이).
            // pre-init 키(xrsdk-pre-init-library=UnityOpenXR)가 이 lib 를 로드하므로 반드시 있어야 함.
            string projRoot = Path.GetDirectoryName(Application.dataPath);
            string pkgCache = Path.Combine(projRoot, "Library", "PackageCache");
            string libSrc = null;
            if (Directory.Exists(pkgCache))
            {
                foreach (string d in Directory.GetDirectories(pkgCache, "com.unity.xr.openxr@*"))
                {
                    string cand = Path.Combine(d, "Runtime", "android", "arm64", "libUnityOpenXR.so");
                    if (File.Exists(cand)) { libSrc = cand; break; }
                }
            }
            if (libSrc != null)
            {
                string dstDir = Path.Combine(path, "src", "main", "jniLibs", "arm64-v8a");
                Directory.CreateDirectory(dstDir);
                string libDst = Path.Combine(dstDir, "libUnityOpenXR.so");
                File.Copy(libSrc, libDst, true);
                Debug.Log("[RayNeoBootConfigPatcher] libUnityOpenXR.so 복사 완료: " + libDst);
            }
            else Debug.LogError("[RayNeoBootConfigPatcher] libUnityOpenXR.so 소스 못 찾음 under " + pkgCache);
        }
        catch (System.Exception e) { Debug.LogError("[RayNeoBootConfigPatcher] " + e.Message); }
    }
}
