using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Unity batch-mode 빌드 진입점.
// 호출:
//   Unity -batchmode -quit -projectPath <path> \
//         -executeMethod BuildSpatialAnchorTest.PerformBuild \
//         -buildTarget Android -logFile <log>
public static class BuildSpatialAnchorTest
{
    const string PACKAGE_NAME = "com.eagleeye.spatialanchor.v2";
    const string PRODUCT_NAME = "SpatialAnchor v2";
    const string COMPANY_NAME = "Eagle Eye";
    const string OUTPUT_APK   = "Build/EagleEye-SpatialAnchor-v2.apk";
    const string SCENE_PATH   = "Assets/Scenes/SpatialAnchorScene.unity";

    [MenuItem("Build/SpatialAnchor APK")]
    public static void PerformBuild()
    {
        Debug.Log("[BuildSpatialAnchorTest] === starting ===");
        Debug.Log($"[BuildSpatialAnchorTest] active build target: {EditorUserBuildSettings.activeBuildTarget}");

        // -buildTarget Android CLI 인자가 이미 Active target 셋팅함.
        // 여기서 SwitchActiveBuildTarget 을 다시 호출하면 domain reload 가 트리거되어
        // PerformBuild 의 나머지 코드가 끊김. 호출하지 않는다.

        ConfigureExternalTools();   // ★ JDK/SDK/NDK/Gradle 경로 강제 set (batch 가 GUI Preferences 못 봄)
        ConfigurePlayerSettings();
        ConfigureAndroidSettings();

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

    static void ConfigureExternalTools()
    {
        // JDK 만 명시 set (Cycle 4 의 fail 원인). SDK/NDK/Gradle 의 setter 는
        // Unity 의 OnUsbDevicesChanged callback chain 을 trigger 해서 silent exit 유발
        // (AndroidSDKTools.ctor:62 fail). Unity 의 default 자체 install 사용.
        string editorRoot = EditorApplication.applicationContentsPath;
        string androidPlayerRoot = Path.Combine(editorRoot, "PlaybackEngines", "AndroidPlayer");
        AndroidExternalToolsSettings.jdkRootPath = Path.Combine(androidPlayerRoot, "OpenJDK");
        Debug.Log($"[BuildSpatialAnchorTest] JDK = {AndroidExternalToolsSettings.jdkRootPath}");
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = COMPANY_NAME;
        PlayerSettings.productName = PRODUCT_NAME;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PACKAGE_NAME);

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
