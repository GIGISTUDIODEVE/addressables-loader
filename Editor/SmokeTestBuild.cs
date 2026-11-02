using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AddressablesWork.Loader.Editor
{
    /// <summary>
    /// 소비자 프로젝트를 명령줄에서 검증용 Development 플레이어로 빌드한다 (개발 계획 단계 1-6).
    /// 인자: -smokeOutput &lt;exe 경로&gt; (기본 Builds/SmokeTest/SmokeTest.exe), -smokeCatalog &lt;카탈로그 URL&gt; (선택)
    /// </summary>
    public static class SmokeTestBuild
    {
        public static void BuildFromCommandLine()
        {
            try
            {
                string output = Arg("-smokeOutput") ?? "Builds/SmokeTest/SmokeTest.exe";
                string catalog = Arg("-smokeCatalog");
                // 단계 7-3: -smokeIl2cpp 로 IL2CPP + 엔진 코드 스트리핑, -smokeLinkXml <경로|-> 로 A 가 배포한 link.xml 을 Assets/link.xml 에 두거나(-) 지운다
                bool il2cpp = Array.IndexOf(Environment.GetCommandLineArgs(), "-smokeIl2cpp") >= 0;
                string linkXml = Arg("-smokeLinkXml");

                var standalone = UnityEditor.Build.NamedBuildTarget.Standalone;
                PlayerSettings.SetScriptingBackend(standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                if (il2cpp)
                {
                    PlayerSettings.stripEngineCode = true;                                   // 번들 안 엔진 타입이 벗겨지는 조건
                    PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Low);
                }
                const string projectLink = "Assets/link.xml";
                if (linkXml == "-") { if (File.Exists(projectLink)) AssetDatabase.DeleteAsset(projectLink); }
                else if (!string.IsNullOrEmpty(linkXml))
                {
                    File.Copy(linkXml, projectLink, true);
                    AssetDatabase.ImportAsset(projectLink);
                }
                Debug.Log($"[SmokeTestBuild] backend={(il2cpp ? "IL2CPP" : "Mono")} link.xml={(File.Exists(projectLink) ? "yes" : "no")}");

                SmokeTestSceneCreator.Create();
                if (!string.IsNullOrEmpty(catalog))
                {
                    var tester = UnityEngine.Object.FindObjectsByType<LoaderSmokeTest>(FindObjectsSortMode.None)[0];
                    tester.CatalogUrl = catalog;
                    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SmokeTestSceneCreator.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development      // HTTP 허용은 Development 빌드에서만 (규칙 12장)
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception("player build " + report.summary.result + ", errors: " + report.summary.totalErrors);

                Debug.Log("[SmokeTestBuild] done: " + Path.GetFullPath(output));
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[SmokeTestBuild] failed: " + e);
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
