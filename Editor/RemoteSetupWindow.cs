using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace AddressablesWork.Loader.Editor
{
    /// <summary>
    /// 새 프로젝트에서 A 의 원격 콘텐츠를 바로 받아 보게 하는 설정 창 (가이드 7.6).
    /// 환경 점검(서버에 이 Unity 버전 빌드가 있는지, URP, Linear, Addressables 버전, 빌드 타깃) → 테스트 씬 만들기 → Play.
    /// Addressables 설정이 없는 프로젝트(소비자)에서는 패키지를 설치한 뒤 한 번 자동으로 열린다.
    /// </summary>
    public class RemoteSetupWindow : EditorWindow
    {
        public const string ScenePath = "Assets/AddressablesWork/RemoteContentTest.unity";
        const string ServerPrefKey = "AddressablesWork.Loader.ServerRoot";
        const string RecommendedAddressables = "2.11.2";      // A 가 빌드하는 버전 (규칙 R1)

        public enum State { Ok, Warn, Fail }

        public struct Check
        {
            public string Label, Detail;
            public State State;
            public Check(State state, string label, string detail) { State = state; Label = label; Detail = detail; }
        }

        string m_ServerRoot;
        string[] m_ServerVersions;
        string m_ServerError;
        UnityWebRequestAsyncOperation m_Fetch;
        Vector2 m_Scroll;

        [MenuItem("Tools/AddressablesWork/Setup")]
        public static void Open() => GetWindow<RemoteSetupWindow>("AddressablesWork Setup").minSize = new Vector2(460, 420);

        void OnEnable()
        {
            m_ServerRoot = EditorPrefs.GetString(ServerPrefKey, RemoteContentBrowser.DefaultServerRoot);
            FetchVersions();
        }

        void OnDisable() => EditorApplication.update -= PollFetch;

        // ---------------------------------------------------------------- 서버 버전 목록 (<루트>/versions.json)

        void FetchVersions()
        {
            m_ServerVersions = null;
            m_ServerError = null;
            var req = UnityWebRequest.Get(m_ServerRoot.TrimEnd('/') + "/versions.json?t=" + DateTime.UtcNow.Ticks);
            m_Fetch = req.SendWebRequest();
            EditorApplication.update -= PollFetch;
            EditorApplication.update += PollFetch;
        }

        void PollFetch()
        {
            if (m_Fetch == null || !m_Fetch.isDone) return;
            EditorApplication.update -= PollFetch;
            using (var req = m_Fetch.webRequest)
                m_ServerVersions = ParseVersions(req, out m_ServerError);
            m_Fetch = null;
            Repaint();
        }

        [Serializable] sealed class VersionList { public string[] versions; }

        static string[] ParseVersions(UnityWebRequest req, out string error)
        {
            error = null;
            if (req.result != UnityWebRequest.Result.Success) { error = $"{req.error} ({req.url})"; return null; }
            var list = JsonUtility.FromJson<VersionList>(req.downloadHandler.text);
            if (list?.versions == null) { error = "versions.json 형식이 다르다"; return null; }
            return list.versions;
        }

        // ---------------------------------------------------------------- 점검 (창과 명령줄 공용)

        public static List<Check> RunChecks(string[] serverVersions, string serverError)
        {
            var checks = new List<Check>();
            string minor = RemoteContentBrowser.UnityMinor();

            if (serverVersions == null)
                checks.Add(new Check(serverError == null ? State.Warn : State.Fail, $"서버의 Unity {minor} 빌드",
                    serverError == null ? "확인 중…" : "서버 목록을 읽지 못함: " + serverError));
            else if (serverVersions.Contains(minor))
                checks.Add(new Check(State.Ok, $"서버의 Unity {minor} 빌드", "있음"));
            else
                checks.Add(new Check(State.Fail, $"서버의 Unity {minor} 빌드",
                    $"없음. 서버에 있는 버전: {string.Join(", ", serverVersions)} — 그 버전 에디터로 연 프로젝트에서 쓰거나, A 에 이 버전 빌드를 요청한다"));

            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.GetType().Name.Contains("Universal"))
                checks.Add(new Check(State.Ok, "렌더 파이프라인", "URP (" + rp.name + ")"));
            else
                checks.Add(new Check(State.Fail, "렌더 파이프라인",
                    (rp == null ? "Built-in" : rp.GetType().Name) + " — 콘텐츠가 분홍색으로 보인다. URP 템플릿(Universal 3D)으로 만든 프로젝트를 쓴다"));

            checks.Add(PlayerSettings.colorSpace == ColorSpace.Linear
                ? new Check(State.Ok, "색 공간", "Linear")
                : new Check(State.Warn, "색 공간", "Gamma — A 는 Linear 로 만든다. 색이 다르게 보인다 (아래 버튼으로 바꿈)"));

            var addr = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.addressables");
            if (addr == null)
                checks.Add(new Check(State.Fail, "Addressables", "설치되지 않음 — 로더 패키지가 의존성으로 설치한다. Package Manager 를 확인"));
            else if (!addr.version.StartsWith("2."))
                checks.Add(new Check(State.Fail, "Addressables", addr.version + " — 서버 카탈로그(.bin)는 2.x 가 필요하다"));
            else if (addr.version != RecommendedAddressables)
                checks.Add(new Check(State.Warn, "Addressables", $"{addr.version} — A 는 {RecommendedAddressables} 로 빌드한다. 같은 버전을 권장"));
            else
                checks.Add(new Check(State.Ok, "Addressables", addr.version));

            checks.Add(EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64
                ? new Check(State.Ok, "빌드 타깃", "Windows 64-bit")
                : new Check(State.Warn, "빌드 타깃", EditorUserBuildSettings.activeBuildTarget +
                    " — 에디터 Play 는 되지만 서버에는 Windows 번들만 있어 플레이어 빌드는 Windows 로 한다"));

            checks.Add(File.Exists(ScenePath)
                ? new Check(State.Ok, "테스트 씬", ScenePath)
                : new Check(State.Warn, "테스트 씬", "없음 — 아래 '테스트 씬 만들기'"));
            return checks;
        }

        // ---------------------------------------------------------------- 테스트 씬

        /// <summary>카메라(Culling Mask 에 Default, 규칙 9장) + AudioListener + Directional Light + RemoteContentBrowser 씬을 만들고 연다.</summary>
        public static bool CreateTestScene(string serverRoot)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

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

            var browser = new GameObject("Remote Content Browser", typeof(RemoteContentBrowser));
            browser.GetComponent<RemoteContentBrowser>().ServerRoot = serverRoot;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
            Debug.Log($"[RemoteSetup] created {ScenePath} (server {serverRoot}). Play 를 누르고 1. Load catalog → 2. Download all → Load all");
            return true;
        }

        // ---------------------------------------------------------------- 화면

        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.LabelField("AddressablesWork 원격 콘텐츠 받기", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("1) 아래 점검이 빨간색이 없는지 본다  2) '테스트 씬 만들고 Play'  3) 화면 왼쪽 버튼: 1. Load catalog → 2. Download all → Load all", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            m_ServerRoot = EditorGUILayout.TextField("서버", m_ServerRoot);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(ServerPrefKey, m_ServerRoot);
            if (GUILayout.Button("기본값", GUILayout.Width(56)))
            {
                m_ServerRoot = RemoteContentBrowser.DefaultServerRoot;
                EditorPrefs.SetString(ServerPrefKey, m_ServerRoot);
                FetchVersions();
            }
            if (GUILayout.Button("다시 점검", GUILayout.Width(72))) FetchVersions();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("카탈로그", $"{m_ServerRoot.TrimEnd('/')}/{RemoteContentBrowser.UnityMinor()}/{RemoteContentBrowser.PlatformFolder()}/catalog_content.bin", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("환경 점검", EditorStyles.boldLabel);
            var checks = RunChecks(m_ServerVersions, m_Fetch != null ? null : m_ServerError);
            foreach (var c in checks)
            {
                string icon = c.State == State.Ok ? "TestPassed" : c.State == State.Warn ? "console.warnicon.sml" : "console.erroricon.sml";
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(EditorGUIUtility.IconContent(icon), GUILayout.Width(20), GUILayout.Height(18));
                GUILayout.Label(c.Label, EditorStyles.boldLabel, GUILayout.Width(150));
                GUILayout.Label(c.Detail, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            if (PlayerSettings.colorSpace != ColorSpace.Linear && GUILayout.Button("색 공간을 Linear 로"))
                if (EditorUtility.DisplayDialog("색 공간", "Linear 로 바꾸면 텍스처를 다시 임포트한다. 계속할까?", "바꾸기", "취소"))
                    PlayerSettings.colorSpace = ColorSpace.Linear;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 && GUILayout.Button("빌드 타깃을 Windows 로"))
                if (EditorUtility.DisplayDialog("빌드 타깃", "Windows 64-bit 로 바꾸면 애셋을 다시 임포트한다. 계속할까?", "바꾸기", "취소"))
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("테스트 씬 만들기", GUILayout.Height(28)))
                CreateTestScene(m_ServerRoot);
            if (GUILayout.Button("테스트 씬 만들고 Play", GUILayout.Height(28)) && CreateTestScene(m_ServerRoot))
                EditorApplication.isPlaying = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("처음 로드할 때 콘솔에 빨간 오류 2개(Invalid path in TextDataProvider … settings.json, Unable to load runtime data)가 찍힌다. 이 프로젝트에 Addressables 빌드가 없어서 나는 것이고 동작에는 영향이 없다.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- 처음 한 번 자동으로 열기

        [InitializeOnLoadMethod]
        static void OpenOnceForConsumer()
        {
            if (Application.isBatchMode) return;
            // 공급자(A)처럼 Addressables 설정이 있는 프로젝트는 건너뛴다
            if (Directory.Exists("Assets/AddressableAssetsData")) return;
            string key = "AddressablesWork.Loader.SetupShown." + Application.dataPath.GetHashCode();
            if (EditorPrefs.GetBool(key)) return;
            EditorPrefs.SetBool(key, true);
            EditorApplication.delayCall += Open;
        }

        // ---------------------------------------------------------------- 명령줄 (확인용)

        /// <summary>
        /// -batchmode -executeMethod AddressablesWork.Loader.Editor.RemoteSetupWindow.BuildTestPlayerFromCommandLine
        /// [-setupServer &lt;루트&gt;] [-setupOutput &lt;exe&gt;] — 점검 결과를 로그에 쓰고, 테스트 씬을 만들어 플레이어를 빌드한다.
        /// </summary>
        public static void BuildTestPlayerFromCommandLine()
        {
            try
            {
                string server = Arg("-setupServer") ?? RemoteContentBrowser.DefaultServerRoot;
                string output = Arg("-setupOutput") ?? "Builds/RemoteContentTest/RemoteContentTest.exe";

                using (var req = UnityWebRequest.Get(server.TrimEnd('/') + "/versions.json?t=" + DateTime.UtcNow.Ticks))
                {
                    var op = req.SendWebRequest();
                    var started = DateTime.UtcNow;
                    while (!op.isDone && (DateTime.UtcNow - started).TotalSeconds < 20) System.Threading.Thread.Sleep(50);
                    string error = "timeout";
                    string[] versions = op.isDone ? ParseVersions(req, out error) : null;
                    foreach (var c in RunChecks(versions, error))
                        Debug.Log($"[RemoteSetup] check {c.State}: {c.Label} — {c.Detail}");
                }

                CreateTestScene(server);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None          // 클라우드 호스트는 HTTPS 라 Development 빌드가 필요 없다
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception("player build " + report.summary.result + ", errors: " + report.summary.totalErrors);
                Debug.Log("[RemoteSetup] done: " + Path.GetFullPath(output));
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[RemoteSetup] failed: " + e);
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
