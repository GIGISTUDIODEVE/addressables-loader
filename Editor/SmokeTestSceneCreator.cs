using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AddressablesWork.Loader.Editor
{
    /// <summary>
    /// 소비자 프로젝트에 확인용 씬을 만든다 (개발 계획 단계 1-5).
    /// 카메라(Culling Mask 에 Default 포함), Directional Light, LoaderSmokeTest 하나. 빌드 설정의 첫 씬으로 넣는다.
    /// </summary>
    public static class SmokeTestSceneCreator
    {
        public const string ScenePath = "Assets/Scenes/LoaderTest.unity";

        [MenuItem("Tools/Addressables Loader/Create Smoke Test Scene")]
        public static void Create()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 1.5f, -4f);
            cam.transform.LookAt(new Vector3(0f, 0.5f, 0f));
            var camera = cam.GetComponent<Camera>();
            camera.cullingMask = -1;                       // Default 포함 (규칙 9장)
            camera.clearFlags = CameraClearFlags.Skybox;

            var light = new GameObject("Directional Light", typeof(Light));
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var l = light.GetComponent<Light>();
            l.type = LightType.Directional;
            l.shadows = LightShadows.Soft;
            l.intensity = 1.5f;

            var tester = new GameObject("LoaderSmokeTest", typeof(LoaderSmokeTest));
            tester.GetComponent<LoaderSmokeTest>().CatalogUrl = DefaultCatalogUrl();

            RenderSettings.ambientMode = AmbientMode.Trilight;

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[SmokeTestSceneCreator] created " + ScenePath + " (first in Build Settings). " +
                      "LoaderSmokeTest.CatalogUrl = " + tester.GetComponent<LoaderSmokeTest>().CatalogUrl);
        }

        // 첫 단계 호스트(규칙 12장) + 이 에디터의 마이너 버전 + 현재 빌드 타깃.
        static string DefaultCatalogUrl()
        {
            string[] parts = Application.unityVersion.Split('.');
            string minor = parts.Length >= 2 ? parts[0] + "." + parts[1] : Application.unityVersion;
            return $"http://localhost:8080/{minor}/{EditorUserBuildSettings.activeBuildTarget}/catalog_content.bin";
        }
    }
}
