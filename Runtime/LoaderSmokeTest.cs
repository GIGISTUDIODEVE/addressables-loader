using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace AddressablesWork.Loader
{
    /// <summary>
    /// 소비자 확인용 컴포넌트 (개발 계획 단계 1-5, 1-6, 2-2, 2-3). 빈 씬에 붙이면 Start 에서
    /// 카탈로그 로드 → 모델 생성 → 텍스처·사운드 로드 를 하고, 화면 왼쪽 위에 상태를 보여준다. 제품 코드가 아니다.
    /// </summary>
    public class LoaderSmokeTest : MonoBehaviour
    {
        [Tooltip("예: http://localhost:8080/6000.6/StandaloneWindows64/catalog_content.bin")]
        public string CatalogUrl = "http://localhost:8080/6000.6/StandaloneWindows64/catalog_content.bin";

        [Tooltip("생성할 모델 주소. 예: A/Models/Props/TestCube")]
        public string Address = "A/Models/Props/TestCube";

        [Tooltip("로드해 화면 오른쪽에 그릴 텍스처 주소. 비우면 건너뜀")]
        public string TextureAddress = "A/Textures/Test/TestPattern";

        [Tooltip("로드해 재생할 사운드 주소. 비우면 건너뜀")]
        public string AudioAddress = "A/Audio/SFX/Tone";

        [Tooltip("Additive 로 로드할 환경 씬 주소. 비우면 건너뜀")]
        public string SceneAddress = "A/Scenes/TestRoom";

        public Vector3 SpawnPosition = Vector3.zero;
        public bool LoadOnStart = true;

        readonly List<string> m_Log = new List<string>();
        GameObject m_Instance;
        UnityEngine.ResourceManagement.ResourceProviders.SceneInstance m_Scene;
        bool m_SceneLoaded;
        readonly List<Light> m_DisabledLights = new List<Light>();
        Texture2D m_Texture;
        AudioClip m_Clip;
        AudioSource m_Source;
        bool m_Busy;
        string m_UpdateAddress;
        float m_UpdateTimeout = 180f;
        bool m_UpdateRequested, m_UpdateOk;
        GameObject m_UpdateInstance;

        async void Start()
        {
            // 명령줄 재정의 (자동 검증용): -smokeCatalog <url> -smokeAddress <addr> -smokeTexture <addr> -smokeAudio <addr> -smokeAuto <결과 폴더>
            string v;
            if (!string.IsNullOrEmpty(v = Arg("-smokeCatalog"))) CatalogUrl = v;
            if (!string.IsNullOrEmpty(v = Arg("-smokeAddress"))) Address = v;
            if (!string.IsNullOrEmpty(v = Arg("-smokeTexture"))) TextureAddress = v;
            if (!string.IsNullOrEmpty(v = Arg("-smokeAudio"))) AudioAddress = v;
            if (!string.IsNullOrEmpty(v = Arg("-smokeScene"))) SceneAddress = v == "-" ? "" : v;
            // 단계 7-4: -smokeUpdate <새 주소> [-smokeUpdateTimeout <초>] → 서버 카탈로그가 바뀔 때까지 기다렸다가 재로드하고 그 주소를 생성한다
            if (!string.IsNullOrEmpty(v = Arg("-smokeUpdate"))) m_UpdateAddress = v;
            if (!string.IsNullOrEmpty(v = Arg("-smokeUpdateTimeout")) && float.TryParse(v, out float t)) m_UpdateTimeout = t;

            if (LoadOnStart)
                await RunAsync();

            if (!string.IsNullOrEmpty(m_UpdateAddress))
                await WaitForCatalogUpdateAsync();

            string autoDir = Arg("-smokeAuto");
            if (!string.IsNullOrEmpty(autoDir))
                await AutoFinishAsync(autoDir);
        }

        // 결과 폴더에 스크린샷(smoke.png)과 결과(smoke.txt: OK 또는 FAIL, 이어서 로그)를 쓰고 종료한다.
        // 스크린샷 뒤에 전부 해제해 애셋이 실제로 파괴되는지(메모리 복귀)도 기록한다.
        async System.Threading.Tasks.Task AutoFinishAsync(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            for (int i = 0; i < 3; i++) await System.Threading.Tasks.Task.Yield();   // 생성된 모델이 한 프레임 이상 그려지도록
            await System.Threading.Tasks.Task.Delay(500);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "smoke.png"));
            await System.Threading.Tasks.Task.Delay(1500);

            bool ok = m_Instance != null
                   && (!m_UpdateRequested || m_UpdateOk)
                   && (string.IsNullOrEmpty(SceneAddress) || m_SceneLoaded)
                   && (string.IsNullOrEmpty(TextureAddress) || m_Texture != null)
                   && (string.IsNullOrEmpty(AudioAddress) || (m_Clip != null && m_Source != null && m_Source.isPlaying));
            var verdictLog = new List<string>(m_Log);

            string sceneName = m_SceneLoaded ? m_Scene.Scene.name : "";
            try
            {
                await ReleaseAllAsync();
                await System.Threading.Tasks.Task.Yield();
                await System.Threading.Tasks.Task.Yield();
                verdictLog.Add($"after release: instance destroyed={m_Instance == null}, texture destroyed={m_Texture == null}, clip destroyed={m_Clip == null}" +
                    (sceneName.Length > 0 ? $", scene '{sceneName}' still loaded={UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName).isLoaded}, scene count={UnityEngine.SceneManagement.SceneManager.sceneCount}" : ""));
            }
            catch (System.Exception e)
            {
                ok = false;
                verdictLog.Add("RELEASE EXCEPTION: " + e.GetType().Name + " " + e.Message);
                Debug.LogException(e);
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "smoke.txt"),
                (ok ? "OK" : "FAIL") + "\n" + string.Join("\n", verdictLog));
            Log("auto mode: " + (ok ? "OK" : "FAIL") + ", quitting");
            Application.Quit();
        }

        // 단계 7-4 (R7-3): 2초마다 .hash 를 비교해 서버 카탈로그가 바뀌면 재로드하고, 새 주소가 생성되는지 본다. 재시작 없음.
        async System.Threading.Tasks.Task WaitForCatalogUpdateAsync()
        {
            m_UpdateRequested = true;
            Log($"update: waiting for catalog change (new address {m_UpdateAddress}, timeout {m_UpdateTimeout:F0}s)");
            var before = await ContentLoader.InstantiateAsync(m_UpdateAddress);
            Log($"  before update: {m_UpdateAddress} -> {before}");
            if (before.Succeeded) { ContentLoader.ReleaseInstance(before.Value); Log("  (address already exists; test is not meaningful)"); }

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < m_UpdateTimeout)
            {
                var changed = await ContentLoader.CheckForCatalogUpdateAsync(CatalogUrl);
                if (changed.Succeeded && changed.Value)
                {
                    Log($"  catalog changed after {Time.realtimeSinceStartup - start:F0}s, reloading");
                    var reload = await ContentLoader.ReloadCatalogAsync(CatalogUrl);
                    Log($"  reload -> {reload}");
                    if (!reload.Succeeded) return;
                    var models = await ContentLoader.GetAddressesByLabelAsync("model");
                    if (models.Succeeded) Log("  label 'model' now: " + string.Join(", ", models.Value));
                    var inst = await ContentLoader.InstantiateAsync(m_UpdateAddress, InstantiateOptions.At(SpawnPosition + new Vector3(1.5f, 0f, 0f)));
                    Log($"  after update: {m_UpdateAddress} -> {inst}");
                    if (inst.Succeeded) { m_UpdateInstance = inst.Value; m_UpdateOk = !before.Succeeded; }
                    // 기존 인스턴스는 그대로 살아 있어야 한다
                    Log($"  existing instance alive={m_Instance != null}");
                    return;
                }
                if (!changed.Succeeded) Log("  check -> " + changed);
                await System.Threading.Tasks.Task.Delay(2000);
            }
            Log("  update: TIMEOUT, catalog did not change");
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public async System.Threading.Tasks.Task RunAsync()
        {
            if (m_Busy) return;
            m_Busy = true;
            try
            {
                Log("catalog: " + CatalogUrl);
                var cat = await ContentLoader.LoadCatalogAsync(CatalogUrl);
                Log("  -> " + cat);
                if (!cat.Succeeded) return;

                var size = await ContentLoader.GetDownloadSizeAsync(Address);
                Log($"download size for {Address}: {(size.Succeeded ? size.Value + " B" : size.Status.ToString())}");

                var models = await ContentLoader.GetAddressesByLabelAsync("model");
                if (models.Succeeded) Log("label 'model': " + string.Join(", ", models.Value));

                // 환경 씬을 먼저 (R11-1: Additive + 활성 씬 지정은 로더가 한다)
                if (!string.IsNullOrEmpty(SceneAddress) && !m_SceneLoaded)
                {
                    var sceneSize = await ContentLoader.GetDownloadSizeAsync(SceneAddress);
                    Log($"download size for {SceneAddress}: {(sceneSize.Succeeded ? sceneSize.Value + " B" : sceneSize.Status.ToString())}");
                    var sc = await ContentLoader.LoadSceneAsync(SceneAddress, true);
                    Log($"scene {SceneAddress} -> {sc}");
                    if (sc.Succeeded)
                    {
                        m_Scene = sc.Value;
                        m_SceneLoaded = true;
                        var s = m_Scene.Scene;
                        Log($"  '{s.name}' loaded={s.isLoaded} roots={s.rootCount} active={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == s.name} " +
                            $"scenes={UnityEngine.SceneManagement.SceneManager.sceneCount}");
                        // 단계 4: 활성 씬의 라이트맵·프로브가 붙었는지 (R11-1). 소비자 자체 조명은 끄고 씬의 베이크된 조명만 본다 (R11-2)
                        var probes = LightmapSettings.lightProbes;
                        Log($"  lightmaps={LightmapSettings.lightmaps.Length} mode={LightmapSettings.lightmapsMode} " +
                            $"probes={(probes != null ? probes.count : 0)} reflection={(RenderSettings.defaultReflectionMode)}");
                        int disabled = 0;
                        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                            if (l.gameObject.scene != s && l.enabled) { l.enabled = false; m_DisabledLights.Add(l); disabled++; }
                        if (disabled > 0) Log($"  consumer lights disabled: {disabled}");

                        await CheckOcclusionAsync(s);
                    }
                }

                Log("instantiate: " + Address);
                var inst = await ContentLoader.InstantiateAsync(Address, InstantiateOptions.At(SpawnPosition));
                Log("  -> " + inst);
                if (inst.Succeeded) m_Instance = inst.Value;

                if (!string.IsNullOrEmpty(TextureAddress) && m_Texture == null)
                {
                    var tex = await ContentLoader.LoadAssetAsync<Texture2D>(TextureAddress);
                    Log($"texture {TextureAddress} -> {tex}" + (tex.Succeeded ? $" {tex.Value.width}x{tex.Value.height} {tex.Value.format}" : ""));
                    if (tex.Succeeded) m_Texture = tex.Value;
                }

                if (!string.IsNullOrEmpty(AudioAddress) && m_Clip == null)
                {
                    var clip = await ContentLoader.LoadAssetAsync<AudioClip>(AudioAddress);
                    Log($"audio {AudioAddress} -> {clip}");
                    if (clip.Succeeded)
                    {
                        m_Clip = clip.Value;
                        if (m_Source == null) m_Source = gameObject.AddComponent<AudioSource>();
                        m_Source.clip = m_Clip;
                        m_Source.loop = true;
                        m_Source.Play();
                        // 계획 2-3: Streaming 이면 클립 메모리가 전체 PCM 크기보다 훨씬 작아야 한다
                        long pcmBytes = (long)(m_Clip.samples * m_Clip.channels * 2);
                        Log($"  length={m_Clip.length:F1}s loadType={m_Clip.loadType} state={m_Clip.loadState} playing={m_Source.isPlaying} " +
                            $"runtimeMem={Profiler.GetRuntimeMemorySizeLong(m_Clip)}B (pcm would be {pcmBytes}B)");
                    }
                }
            }
            catch (System.Exception e)
            {
                Log("EXCEPTION: " + e.GetType().Name + " " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                m_Busy = false;
            }
        }

        // 단계 5-3, 5-4: 카메라의 오클루전 컬링을 껐다 켜며 씬 안 Renderer 의 isVisible 수를 센다.
        // 오클루전 데이터가 붙었으면 켰을 때 Hidden_* 가 보이지 않아야 한다. 활성 씬을 소비자 씬으로 바꿔도 같은지 본다 (R11-1).
        async System.Threading.Tasks.Task CheckOcclusionAsync(UnityEngine.SceneManagement.Scene s)
        {
            var cam = Camera.main;
            if (cam == null) { Log("  occlusion: no main camera"); return; }
            var renderers = new List<Renderer>();
            foreach (var root in s.GetRootGameObjects())
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));

            var consumerScene = gameObject.scene;
            foreach (bool activeIsContent in new[] { true, false })
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(activeIsContent ? s : consumerScene);
                string with = await CountVisibleAsync(cam, renderers, true);
                string without = await CountVisibleAsync(cam, renderers, false);
                Log($"  occlusion (active={(activeIsContent ? s.name : consumerScene.name)}): OC on {with} | OC off {without}");
            }
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(s);
            cam.useOcclusionCulling = true;
        }

        static async System.Threading.Tasks.Task<string> CountVisibleAsync(Camera cam, List<Renderer> renderers, bool occlusion)
        {
            cam.useOcclusionCulling = occlusion;
            for (int i = 0; i < 3; i++) await System.Threading.Tasks.Task.Yield();
            int visible = 0, hiddenVisible = 0, hiddenTotal = 0;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (r.isVisible) visible++;
                if (r.name.StartsWith("Hidden_")) { hiddenTotal++; if (r.isVisible) hiddenVisible++; }
            }
            return $"visible={visible}/{renderers.Count} hidden-behind-wall visible={hiddenVisible}/{hiddenTotal}";
        }

        public void ReleaseAll() => _ = ReleaseAllAsync();

        public async System.Threading.Tasks.Task ReleaseAllAsync()
        {
            if (m_SceneLoaded)
            {
                m_SceneLoaded = false;
                var r = await ContentLoader.UnloadSceneAsync(m_Scene);
                Log("unload scene -> " + r);
                foreach (var l in m_DisabledLights) if (l != null) l.enabled = true;
                m_DisabledLights.Clear();
            }
            if (m_Instance != null)
            {
                ContentLoader.ReleaseInstance(m_Instance);
                m_Instance = null;
            }
            if (m_UpdateInstance != null)
            {
                ContentLoader.ReleaseInstance(m_UpdateInstance);
                m_UpdateInstance = null;
            }
            if (m_Source != null) m_Source.Stop();
            if (m_Clip != null)
            {
                if (m_Source != null) m_Source.clip = null;
                ContentLoader.Release(m_Clip);
                m_Clip = null;
            }
            if (m_Texture != null)
            {
                ContentLoader.Release(m_Texture);
                m_Texture = null;
            }
            Log("released");
        }

        void Log(string line)
        {
            Debug.Log("[LoaderSmokeTest] " + line);
            m_Log.Add(line);
            if (m_Log.Count > 16) m_Log.RemoveAt(0);
        }

        void OnGUI()
        {
            const int w = 640;
            GUILayout.BeginArea(new Rect(10, 10, w, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("AddressablesWork Loader smoke test");
            foreach (var line in m_Log)
                GUILayout.Label(line);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUI.enabled = !m_Busy;
            if (GUILayout.Button("Load")) _ = RunAsync();
            GUI.enabled = m_Instance != null || m_Texture != null || m_Clip != null || m_SceneLoaded;
            if (GUILayout.Button("Release")) ReleaseAll();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (m_Texture != null)
            {
                const int size = 200;
                GUI.DrawTexture(new Rect(Screen.width - size - 10, 10, size, size), m_Texture, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(Screen.width - size - 10, size + 12, size, 20), TextureAddress);
            }
        }
    }
}
