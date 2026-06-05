using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Unity batch-mode 빌드 진입점.
// CLI 호출:
//   Unity -batchmode -quit -projectPath <path> \
//         -executeMethod BuildHelloAR.PerformBuild \
//         -buildTarget Android \
//         -logFile <log_path>
public static class BuildHelloAR
{
    const string PACKAGE_NAME = "com.eagleeye.helloar";
    const string PRODUCT_NAME = "Eagle Eye Hello AR";
    const string COMPANY_NAME = "Eagle Eye";
    const string OUTPUT_APK = "Build/EagleEye-HelloAR.apk";
    const string SCENE_PATH = "Assets/Scenes/HelloARScene.unity";

    [MenuItem("Build/Hello AR APK")]
    public static void PerformBuild()
    {
        Debug.Log("[BuildHelloAR] === Starting build ===");

        EnsureScene();
        ConfigurePlayerSettings();
        ConfigureAndroidSettings();

        EditorUserBuildSettings.SwitchActiveBuildTarget(
            BuildTargetGroup.Android, BuildTarget.Android);

        Directory.CreateDirectory(Path.GetDirectoryName(OUTPUT_APK));

        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = new[] { SCENE_PATH },
            locationPathName = OUTPUT_APK,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildHelloAR] === BUILD SUCCEEDED ===");
            Debug.Log($"[BuildHelloAR] APK: {OUTPUT_APK}");
            Debug.Log($"[BuildHelloAR] Size: {summary.totalSize / 1024 / 1024} MB");
            Debug.Log($"[BuildHelloAR] Time: {summary.totalTime}");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildHelloAR] === BUILD FAILED ===");
            Debug.LogError($"Result: {summary.result}");
            Debug.LogError($"Errors: {summary.totalErrors}");
            EditorApplication.Exit(1);
        }
    }

    static void EnsureScene()
    {
        if (File.Exists(SCENE_PATH))
        {
            Debug.Log($"[BuildHelloAR] Scene exists: {SCENE_PATH}");
            return;
        }
        Debug.Log($"[BuildHelloAR] Creating scene: {SCENE_PATH}");
        Directory.CreateDirectory("Assets/Scenes");

        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject host = new GameObject("HelloARHost");
        host.AddComponent(System.Type.GetType("HelloAR, Assembly-CSharp"));

        EditorSceneManager.SaveScene(scene, SCENE_PATH);
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = COMPANY_NAME;
        PlayerSettings.productName = PRODUCT_NAME;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PACKAGE_NAME);

        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                    UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
    }

    static void ConfigureAndroidSettings()
    {
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel31;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        // NET API 호환성 레벨은 기본값 유지 (Unity 6에서 enum 이름이 NET_Standard로 변경됨)

        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
    }
}

