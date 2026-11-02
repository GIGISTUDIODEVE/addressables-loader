using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AddressablesWork.Loader.Samples.Editor
{
    /// <summary>
    /// Basic Usage 샘플 씬을 만들고, 명령줄에서 Development 플레이어로 빌드한다.
    /// 씬: 카메라(Culling Mask 에 Default 포함, 규칙 9장) + AudioListener + Directional Light + BasicUsageSample.
    /// </summary>
    public static class BasicUsageSceneCreator
    {
        public const string ScenePath = "Assets/Scenes/BasicUsage.unity";

        [MenuItem("Tools/Addressables Loader/Create Basic Usage Scene")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(1.5f, 1.8f, -4.5f);
            cam.transform.LookAt(new Vector3(1.5f, 0.5f, 0f));
            var camera = cam.GetComponent<Camera>();
            camera.cullingMask = -1;
            camera.clearFlags = CameraClearFlags.Skybox;

            var light = new GameObject("Directional Light", typeof(Light));
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var l = light.GetComponent<Light>();
            l.type = LightType.Directional;
            l.shadows = LightShadows.Soft;
            l.intensity = 1.5f;

            new GameObject("BasicUsageSample", typeof(BasicUsageSample));
            RenderSettings.ambientMode = AmbientMode.Trilight;

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[BasicUsageSceneCreator] created " + ScenePath + " (added to Build Settings)");
        }

        /// <summary>
        /// 배치 모드: -executeMethod AddressablesWork.Loader.Samples.Editor.BasicUsageSceneCreator.BuildFromCommandLine
        /// -sampleOutput &lt;exe 경로&gt; (기본 Builds/BasicUsage/BasicUsage.exe). 이 씬 하나만 넣은 플레이어를 만든다.
        /// </summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                string output = Arg("-sampleOutput") ?? "Builds/BasicUsage/BasicUsage.exe";
                Create();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development      // HTTP 허용은 Development 빌드에서만 (규칙 12장)
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception("player build " + report.summary.result + ", errors: " + report.summary.totalErrors);
                Debug.Log("[BasicUsageSceneCreator] done: " + Path.GetFullPath(output));
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[BasicUsageSceneCreator] failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
