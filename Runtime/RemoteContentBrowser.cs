using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace AddressablesWork.Loader
{
    /// <summary>
    /// 소비자가 A 가 올린 콘텐츠를 받아 보는 컴포넌트. Tools → AddressablesWork → Setup 의 "테스트 씬 만들기"가 씬에 넣는다.
    /// 1) 카탈로그 로드 → 2) 라벨로 주소 목록·다운로드 크기 → 3) 미리 다운로드 → 4) 주소별 로드·해제 → 5) 카탈로그 갱신 확인.
    /// 빈 씬(카메라 + AudioListener)에 붙이면 화면 왼쪽에 버튼이 나온다. 코드는 <see cref="ContentLoader"/> 만 쓴다 (R8-2).
    /// </summary>
    [AddComponentMenu("AddressablesWork/Remote Content Browser")]
    public class RemoteContentBrowser : MonoBehaviour
    {
        /// <summary>클라우드 호스트 (규칙 12장). A 의 AddressablesSetup.GitHubPagesRoot + "/A" 와 같아야 한다.</summary>
        public const string DefaultServerRoot = "https://gigistudiodeve.github.io/addressables-cdn/A";

        [Tooltip("번들 호스트 루트. 카탈로그 URL = <루트>/<Unity 마이너 버전>/<빌드 타깃>/catalog_content.bin (R7-2, 규칙 12장). " +
                 "클라우드(기본): https://gigistudiodeve.github.io/addressables-cdn/A / 로컬 서버: http://localhost:8080")]
        public string ServerRoot = DefaultServerRoot;

        [Tooltip("목록에 보여줄 라벨. A 의 등록 라벨 (개발 계획 2장)")]
        public string[] Labels = { "model", "texture", "audio", "scene" };

        [Tooltip("모델을 생성할 첫 위치. 다음 모델은 SpawnSpacing 만큼 옆에")]
        public Vector3 SpawnOrigin = Vector3.zero;
        public Vector3 SpawnSpacing = new Vector3(1.5f, 0f, 0f);

        [Tooltip("환경 씬을 올린 동안 이 씬의 Light 를 끈다. 켜 두면 베이크된 씬이 이중으로 밝아진다 (R11-2)")]
        public bool DisableOwnLightsWhileSceneLoaded = true;

        [Tooltip("서버에 이 Unity 마이너 버전의 빌드가 없으면 같은 메이저의 가장 가까운 하위 버전 빌드를 받는다 (R1-2). 경고를 남기고, 동작은 보장되지 않는다")]
        public bool AllowLowerVersion = true;

        // 목록 한 줄. 주소 하나가 여러 라벨에 있으면 첫 라벨에만 넣는다
        sealed class Entry
        {
            public string Label, Address;
            public long DownloadSize = -1;
            public GameObject Instance;
            public Object Asset;
            public SceneInstance Scene;
            public bool SceneLoaded, Busy;
            public bool IsLoaded => Instance != null || Asset != null || SceneLoaded;
        }

        readonly List<Entry> m_Entries = new List<Entry>();
        readonly List<string> m_Log = new List<string>();
        readonly List<Light> m_DisabledLights = new List<Light>();
        AudioSource m_Source;
        bool m_CatalogLoaded, m_Busy;
        float m_Progress = -1f;
        Vector2 m_Scroll;
        string m_UnityMinor;
        string m_ServerMinor;       // 실제로 받는 버전. 하위 버전으로 대체하면 Minor 와 다르다

        /// <summary>이 플레이어의 Unity 마이너 버전(또는 대체한 하위 버전)·플랫폼에 맞는 카탈로그 (R1-2: 버전마다 따로 빌드한 번들을 받는다).</summary>
        public string CatalogUrl => $"{ServerRoot.TrimEnd('/')}/{m_ServerMinor ?? Minor}/{PlatformFolder()}/catalog_content.bin";

        string Minor => string.IsNullOrEmpty(m_UnityMinor) ? UnityMinor() : m_UnityMinor;

        // ---------------------------------------------------------------- 1. 카탈로그

        public async Task LoadCatalogAsync()
        {
            // 클라우드 업로드 스크립트가 <루트>/versions.json 에 올린 목록으로 받을 버전을 고른다. 로컬 서버에는 없으므로 이 버전을 그대로 쓴다
            string[] versions = await ServerVersions.FetchAsync(ServerRoot);
            m_ServerMinor = null;
            if (versions != null)
            {
                string pick = ServerVersions.Pick(versions, Minor, AllowLowerVersion, out bool exact);
                if (pick == null)
                {
                    Log($"no build for Unity {Minor} on this server" + (AllowLowerVersion ? " (and no lower version)" : "") +
                        $". Available: {string.Join(", ", versions)}");
                    m_CatalogLoaded = false;
                    return;
                }
                m_ServerMinor = pick;
                if (!exact)
                {
                    Log($"no build for Unity {Minor}; using lower version {pick} (not guaranteed, R1-2)");
                    Debug.LogWarning($"[RemoteContentBrowser] 서버에 Unity {Minor} 빌드가 없어 {pick} 빌드를 받는다. 동작은 보장되지 않는다 (R1-2)");
                }
            }

            Log("catalog: " + CatalogUrl);
            var cat = await ContentLoader.LoadCatalogAsync(CatalogUrl);
            Log("  -> " + cat);
            m_CatalogLoaded = cat.Succeeded;
            if (m_CatalogLoaded)
                await RefreshListAsync();
            else if (versions != null)
                Log($"  server lists Unity {m_ServerMinor}; check the network or platform ({PlatformFolder()})");
        }

        // ---------------------------------------------------------------- 2. 목록과 다운로드 크기

        async Task RefreshListAsync()
        {
            var loaded = new Dictionary<string, Entry>();
            foreach (var e in m_Entries)
                if (e.IsLoaded) loaded[e.Address] = e;          // 로드된 것은 상태를 유지한다
            m_Entries.Clear();

            var seen = new HashSet<string>();
            foreach (string label in Labels)
            {
                var addresses = await ContentLoader.GetAddressesByLabelAsync(label);
                if (!addresses.Succeeded) { Log($"label '{label}' -> {addresses}"); continue; }
                foreach (string address in addresses.Value)
                {
                    if (!seen.Add(address)) continue;
                    var e = loaded.TryGetValue(address, out var old) ? old : new Entry { Address = address };
                    e.Label = label;
                    m_Entries.Add(e);
                }
            }
            await RefreshSizesAsync();
            Log($"list: {m_Entries.Count} addresses in {Labels.Length} labels");
        }

        async Task RefreshSizesAsync()
        {
            foreach (var e in m_Entries)
            {
                var size = await ContentLoader.GetDownloadSizeAsync(e.Address);   // 의존 번들 포함, 이미 캐시된 것은 0
                e.DownloadSize = size.Succeeded ? size.Value : -1;
            }
        }

        // ---------------------------------------------------------------- 3. 미리 다운로드

        /// <summary>라벨별로 번들을 받아 캐시에 넣는다. 받은 뒤에는 로드가 네트워크 없이 된다.</summary>
        public async Task DownloadAllAsync()
        {
            for (int i = 0; i < Labels.Length; i++)
            {
                int index = i;
                var progress = new System.Progress<float>(p => m_Progress = (index + p) / Labels.Length);
                var result = await ContentLoader.DownloadAsync(Labels[i], progress);
                Log($"download '{Labels[i]}' -> {result}");
            }
            m_Progress = -1f;
            await RefreshSizesAsync();
        }

        // ---------------------------------------------------------------- 4. 로드와 해제

        async Task LoadAsync(Entry e)
        {
            switch (e.Label)
            {
                case "model":
                {
                    int slot = m_Entries.FindAll(x => x.Instance != null).Count;
                    var r = await ContentLoader.InstantiateAsync(e.Address, InstantiateOptions.At(SpawnOrigin + SpawnSpacing * slot));
                    Log($"instantiate {e.Address} -> {r}");
                    if (r.Succeeded) e.Instance = r.Value;
                    break;
                }
                case "texture":
                {
                    var r = await ContentLoader.LoadAssetAsync<Texture2D>(e.Address);
                    Log($"texture {e.Address} -> {r}" + (r.Succeeded ? $" {r.Value.width}x{r.Value.height}" : ""));
                    if (r.Succeeded) e.Asset = r.Value;
                    break;
                }
                case "audio":
                {
                    var r = await ContentLoader.LoadAssetAsync<AudioClip>(e.Address);
                    Log($"audio {e.Address} -> {r}" + (r.Succeeded ? $" {r.Value.length:F1}s {r.Value.loadType}" : ""));
                    if (r.Succeeded)
                    {
                        e.Asset = r.Value;
                        if (m_Source == null) m_Source = gameObject.AddComponent<AudioSource>();
                        m_Source.clip = r.Value;
                        m_Source.Play();
                    }
                    break;
                }
                case "scene":
                {
                    var r = await ContentLoader.LoadSceneAsync(e.Address);           // Additive + 활성 씬 (R11-1)
                    Log($"scene {e.Address} -> {r}");
                    if (r.Succeeded)
                    {
                        e.Scene = r.Value;
                        e.SceneLoaded = true;
                        if (DisableOwnLightsWhileSceneLoaded)
                            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                                if (l.gameObject.scene == gameObject.scene && l.enabled) { l.enabled = false; m_DisabledLights.Add(l); }
                    }
                    break;
                }
            }
        }

        async Task ReleaseAsync(Entry e)
        {
            if (e.Instance != null)
            {
                ContentLoader.ReleaseInstance(e.Instance);                         // R8-3
                e.Instance = null;
            }
            if (e.Asset != null)
            {
                if (m_Source != null && m_Source.clip == e.Asset) { m_Source.Stop(); m_Source.clip = null; }
                ContentLoader.Release(e.Asset);
                e.Asset = null;
            }
            if (e.SceneLoaded)
            {
                e.SceneLoaded = false;
                Log($"unload {e.Address} -> {await ContentLoader.UnloadSceneAsync(e.Scene)}");
                foreach (var l in m_DisabledLights) if (l != null) l.enabled = true;
                m_DisabledLights.Clear();
            }
            Log("released " + e.Address);
        }

        public async Task LoadAllAsync()
        {
            // 씬을 먼저 올려야 모델이 그 씬의 조명·프로브를 받는다
            foreach (var e in m_Entries) if (e.Label == "scene" && !e.IsLoaded) await LoadAsync(e);
            foreach (var e in m_Entries) if (e.Label != "scene" && !e.IsLoaded) await LoadAsync(e);
        }

        public async Task ReleaseAllAsync()
        {
            foreach (var e in m_Entries) if (e.IsLoaded) await ReleaseAsync(e);
        }

        // ---------------------------------------------------------------- 5. 카탈로그 갱신 (R7-3)

        public async Task CheckForUpdateAsync()
        {
            var changed = await ContentLoader.CheckForCatalogUpdateAsync(CatalogUrl);
            Log("update check -> " + (changed.Succeeded ? (changed.Value ? "changed" : "up to date") : changed.ToString()));
            if (!changed.Succeeded || !changed.Value) return;
            Log("reload -> " + await ContentLoader.ReloadCatalogAsync(CatalogUrl));   // 이미 생성·로드한 것은 그대로 남는다
            await RefreshListAsync();
        }

        /// <summary>
        /// 내려받은 번들 캐시를 지운다. 다운로드 크기가 다시 0 보다 커지는 것을 보여주기 위한 것.
        /// 로드된 번들이 있으면 지워지지 않으므로 전부 해제한 뒤에만 부른다.
        /// </summary>
        public async Task ClearCacheAsync()
        {
            Log("clear cache -> " + (Caching.ClearCache() ? "OK" : "FAILED (bundles in use)"));
            if (m_CatalogLoaded) await RefreshSizesAsync();
        }

        // ---------------------------------------------------------------- 실행

        async void Start()
        {
            string v;
            if (!string.IsNullOrEmpty(v = Arg("-sampleServer"))) ServerRoot = v;
            if (!string.IsNullOrEmpty(v = Arg("-sampleUnityVersion"))) m_UnityMinor = v;   // 다른 버전인 척 (버전 없음·하위 버전 대체 확인용)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-sampleNoFallback") >= 0) AllowLowerVersion = false;

            // 자동 확인: -sampleAuto <결과 폴더> → 캐시 비우기부터 해제까지 한 번 돌고 sample.txt, sample.png 를 쓰고 종료
            string autoDir = Arg("-sampleAuto");
            if (!string.IsNullOrEmpty(autoDir))
                await AutoRunAsync(autoDir);
        }

        async Task AutoRunAsync(string dir)
        {
            bool ok = true;
            await Run(ClearCacheAsync);
            await Run(LoadCatalogAsync);
            ok &= m_CatalogLoaded && m_Entries.Count > 0;
            foreach (string label in Labels)
                ok &= m_Entries.Exists(e => e.Label == label);
            long before = TotalSize();
            await Run(DownloadAllAsync);
            long after = TotalSize();
            Log($"auto: download size before={before} B after={after} B");
            ok &= before > 0 && after == 0;

            await Run(LoadAllAsync);
            ok &= m_Entries.TrueForAll(e => e.IsLoaded);
            for (int i = 0; i < 3; i++) await Task.Yield();
            await Task.Delay(1000);
            System.IO.Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "sample.png"));
            await Task.Delay(1500);

            var kept = new List<Object>();
            foreach (var e in m_Entries) if (e.Asset != null) kept.Add(e.Asset);
            await Run(ReleaseAllAsync);
            await Task.Yield();
            await Task.Yield();
            bool destroyed = kept.TrueForAll(a => a == null) && m_Entries.TrueForAll(e => !e.IsLoaded);
            Log($"auto: after release all destroyed={destroyed}, scene count={UnityEngine.SceneManagement.SceneManager.sceneCount}");
            ok &= destroyed;

            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "sample.txt"),
                (ok ? "OK" : "FAIL") + "\n" + string.Join("\n", m_AllLog));
            Application.Quit();
        }

        long TotalSize()
        {
            long sum = 0;
            foreach (var e in m_Entries) if (e.DownloadSize > 0) sum += e.DownloadSize;
            return sum;
        }

        // 버튼 하나가 끝날 때까지 다른 버튼을 막는다
        async Task Run(System.Func<Task> action)
        {
            if (m_Busy) return;
            m_Busy = true;
            try { await action(); }
            catch (System.Exception ex) { Log("EXCEPTION: " + ex.GetType().Name + " " + ex.Message); Debug.LogException(ex); }
            finally { m_Busy = false; }
        }

        async Task Run(Entry e)
        {
            if (e.Busy) return;
            e.Busy = true;
            try { await (e.IsLoaded ? ReleaseAsync(e) : LoadAsync(e)); }
            catch (System.Exception ex) { Log("EXCEPTION: " + ex.GetType().Name + " " + ex.Message); Debug.LogException(ex); }
            finally { e.Busy = false; }
        }

        // ---------------------------------------------------------------- 화면

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 560, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("AddressablesWork - Remote Content");
            GUILayout.Label(CatalogUrl);

            GUI.enabled = !m_Busy;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1. Load catalog")) _ = Run(LoadCatalogAsync);
            GUI.enabled = !m_Busy && m_CatalogLoaded;
            if (GUILayout.Button($"2. Download all ({Kb(TotalSize())})")) _ = Run(DownloadAllAsync);
            if (GUILayout.Button("Check update")) _ = Run(CheckForUpdateAsync);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load all")) _ = Run(LoadAllAsync);
            if (GUILayout.Button("Release all")) _ = Run(ReleaseAllAsync);
            GUI.enabled = !m_Busy && !m_Entries.Exists(e => e.IsLoaded);
            if (GUILayout.Button("Clear cache")) _ = Run(ClearCacheAsync);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (m_Progress >= 0f)
                GUILayout.HorizontalSlider(m_Progress, 0f, 1f);

            GUILayout.Space(6);
            m_Scroll = GUILayout.BeginScrollView(m_Scroll, GUILayout.Height(200));
            foreach (var e in m_Entries)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(e.Label, GUILayout.Width(60));
                GUILayout.Label(e.Address, GUILayout.Width(250));
                GUILayout.Label(e.DownloadSize < 0 ? "?" : e.DownloadSize == 0 ? "cached" : Kb(e.DownloadSize), GUILayout.Width(80));
                GUI.enabled = !e.Busy && !m_Busy;
                if (GUILayout.Button(e.IsLoaded ? "Release" : "Load")) _ = Run(e);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            foreach (var line in m_Log) GUILayout.Label(line);
            GUILayout.EndArea();

            // 로드한 텍스처를 오른쪽 위에 그린다
            float y = 10;
            foreach (var e in m_Entries)
            {
                if (!(e.Asset is Texture2D tex)) continue;
                GUI.DrawTexture(new Rect(Screen.width - 170, y, 160, 160), tex, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(Screen.width - 170, y + 162, 160, 20), e.Address);
                y += 190;
            }
        }

        // ---------------------------------------------------------------- 내부

        readonly List<string> m_AllLog = new List<string>();

        void Log(string line)
        {
            Debug.Log("[RemoteContentBrowser] " + line);
            m_AllLog.Add(line);
            m_Log.Add(line);
            if (m_Log.Count > 10) m_Log.RemoveAt(0);
        }

        static string Kb(long bytes) => bytes <= 0 ? "0 KB" : $"{bytes / 1024f:F1} KB";

        public static string UnityMinor()
        {
            string[] parts = Application.unityVersion.Split('.');
            return parts.Length >= 2 ? parts[0] + "." + parts[1] : Application.unityVersion;
        }

        // ServerData 의 폴더 이름은 BuildTarget 이름이다. 첫 단계 대상은 Windows 만 (규칙 12장)
        public static string PlatformFolder()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor: return "StandaloneWindows64";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor: return "StandaloneOSX";
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                default: return Application.platform.ToString();
            }
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
