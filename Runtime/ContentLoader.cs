using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Exceptions;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace AddressablesWork.Loader
{
    /// <summary>
    /// 공용 로더 진입점. 규칙 8장.
    /// 순서: <see cref="LoadCatalogAsync"/> → (선택) <see cref="GetDownloadSizeAsync"/> / <see cref="DownloadAsync"/>
    /// → <see cref="InstantiateAsync"/> / <see cref="LoadAssetAsync{T}"/> / <see cref="LoadSceneAsync"/> → Release 계열.
    /// 소비자는 주소 문자열만 다루고 (R8-2), 핸들은 이 클래스가 추적한다.
    /// </summary>
    public static class ContentLoader
    {
        // R8-4: 같은 카탈로그는 한 번만. 로케이터와 .hash 값을 들고 있어야 갱신 확인·재로드가 된다 (R7-3)
        sealed class CatalogState
        {
            public IResourceLocator Locator;
            public string Hash;
        }
        static readonly Dictionary<string, CatalogState> s_LoadedCatalogs = new Dictionary<string, CatalogState>();
        static readonly Dictionary<UnityEngine.Object, AsyncOperationHandle> s_AssetHandles = new();
        static readonly Dictionary<Scene, AsyncOperationHandle<SceneInstance>> s_SceneHandles = new();
        static bool s_Initialized;

        /// <summary>Network 실패 재시도 횟수 (R8-5). 0 이면 재시도 없음.</summary>
        public static int NetworkRetryCount = 2;
        /// <summary>재시도 간격(초).</summary>
        public static float NetworkRetryDelaySeconds = 1f;

        // ---------------------------------------------------------------- 카탈로그

        /// <summary>
        /// 원격 카탈로그를 로드한다. 같은 URL 은 한 번만 로드한다.
        /// catalogUrl 예: http://localhost:8080/6000.6/StandaloneWindows64/catalog_content.json
        /// </summary>
        public static async Task<LoadResult> LoadCatalogAsync(string catalogUrl)
        {
            if (string.IsNullOrEmpty(catalogUrl))
                return new LoadResult(LoadFailure.Unknown, "catalogUrl is empty");
            if (s_LoadedCatalogs.ContainsKey(catalogUrl))
                return LoadResult.Ok;

            await EnsureInitializedAsync();

            for (int attempt = 0; ; attempt++)
            {
                // autoReleaseHandle=false: 완료 직후 Status 를 읽어야 하므로 직접 해제한다
                var handle = Addressables.LoadContentCatalogAsync(catalogUrl, false);
                await handle.Task;
                bool ok = handle.Status == AsyncOperationStatus.Succeeded;
                var failure = ok ? LoadResult.Ok : Classify(handle.OperationException, "catalog " + catalogUrl);
                var locator = ok ? handle.Result : null;
                Addressables.Release(handle);
                if (ok)
                {
                    // Addressables 가 방금 비교한 원격 .hash 를 다시 읽어 둔다. 실패해도 로드는 성공으로 본다 (갱신 확인만 못 함)
                    string hash = await FetchTextAsync(HashUrl(catalogUrl));
                    s_LoadedCatalogs[catalogUrl] = new CatalogState { Locator = locator, Hash = hash };
                    return LoadResult.Ok;
                }

                if (failure.Failure != LoadFailure.Network || attempt >= NetworkRetryCount)
                    return failure;
                await Task.Delay(TimeSpan.FromSeconds(NetworkRetryDelaySeconds));
            }
        }

        /// <summary>
        /// 서버의 카탈로그 .hash 가 로드 때와 다른지 확인한다 (R7-3 기본안: 재시작 없이 .hash 로 갱신 여부 확인).
        /// true 면 <see cref="ReloadCatalogAsync"/>로 새 카탈로그를 받을 수 있다. 네트워크 실패면 Failure.Network.
        /// </summary>
        public static async Task<LoadResult<bool>> CheckForCatalogUpdateAsync(string catalogUrl)
        {
            if (!s_LoadedCatalogs.TryGetValue(catalogUrl, out var state))
                return new LoadResult<bool>(new LoadResult(LoadFailure.Unknown, "catalog was not loaded through ContentLoader: " + catalogUrl));
            string remote = await FetchTextAsync(HashUrl(catalogUrl));
            if (remote == null)
                return new LoadResult<bool>(new LoadResult(LoadFailure.Network, "cannot read " + HashUrl(catalogUrl)));
            return new LoadResult<bool>(!string.Equals(remote, state.Hash, StringComparison.Ordinal));
        }

        /// <summary>
        /// 이미 로드한 카탈로그를 서버의 현재 버전으로 바꾼다. 옛 로케이터를 빼고 다시 로드하므로 새 주소가 보이고,
        /// 이미 생성된 인스턴스·로드된 애셋은 그대로 남는다 (번들은 해시 이름이라 옛 것과 새 것이 공존).
        /// </summary>
        public static async Task<LoadResult> ReloadCatalogAsync(string catalogUrl)
        {
            if (s_LoadedCatalogs.TryGetValue(catalogUrl, out var state))
            {
                s_LoadedCatalogs.Remove(catalogUrl);
                if (state.Locator != null)
                    Addressables.RemoveResourceLocator(state.Locator);
            }
            return await LoadCatalogAsync(catalogUrl);
        }

        static string HashUrl(string catalogUrl)
        {
            int dot = catalogUrl.LastIndexOf('.');
            int slash = catalogUrl.LastIndexOf('/');
            return dot > slash ? catalogUrl.Substring(0, dot) + ".hash" : catalogUrl + ".hash";
        }

        // 작은 텍스트 파일(.hash)을 받는다. 실패하면 null. 캐시를 피하려 쿼리를 붙인다.
        static async Task<string> FetchTextAsync(string url)
        {
            using var req = UnityEngine.Networking.UnityWebRequest.Get(url + "?t=" + DateTime.UtcNow.Ticks);
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                return null;
            return req.downloadHandler.text.Trim();
        }

        // 소비자(B)에 Addressables 설정이 없으면 초기화가 실패 로그를 남기지만, 이후 원격 카탈로그 로드는 가능하다. [검증 필요]
        static async Task EnsureInitializedAsync()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            var init = Addressables.InitializeAsync(false);
            await init.Task;
            if (init.Status != AsyncOperationStatus.Succeeded)
                Debug.Log("[ContentLoader] local Addressables runtime data not found (expected for a consumer without its own build). Remote catalogs still load.");
            Addressables.Release(init);
        }

        // ---------------------------------------------------------------- 다운로드

        /// <summary>주소 또는 라벨의 미캐시 다운로드 크기(바이트). 0 이면 이미 캐시됨.</summary>
        public static async Task<LoadResult<long>> GetDownloadSizeAsync(string key)
        {
            var handle = Addressables.GetDownloadSizeAsync(key);
            await handle.Task;
            var result = handle.Status == AsyncOperationStatus.Succeeded
                ? new LoadResult<long>(handle.Result)
                : new LoadResult<long>(Classify(handle.OperationException, key));
            Addressables.Release(handle);
            return result;
        }

        /// <summary>주소 또는 라벨의 번들을 미리 내려받아 캐시한다.</summary>
        public static async Task<LoadResult> DownloadAsync(string key, IProgress<float> progress = null)
        {
            var handle = Addressables.DownloadDependenciesAsync(key, false);
            while (!handle.IsDone)
            {
                progress?.Report(handle.GetDownloadStatus().Percent);
                await Task.Yield();
            }
            progress?.Report(1f);
            var result = handle.Status == AsyncOperationStatus.Succeeded
                ? LoadResult.Ok
                : Classify(handle.OperationException, key);
            Addressables.Release(handle);
            return result;
        }

        // ---------------------------------------------------------------- 단독 애셋

        /// <summary>모델 프리팹을 생성한다. 반환된 인스턴스는 <see cref="ReleaseInstance"/>로 해제한다 (R8-3).</summary>
        public static async Task<LoadResult<GameObject>> InstantiateAsync(string address, InstantiateOptions options)
        {
            var handle = Addressables.InstantiateAsync(address, options.Position, options.Rotation, options.Parent, true);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var failure = Classify(handle.OperationException, address);
                Addressables.Release(handle);
                return new LoadResult<GameObject>(failure);
            }

            var instance = handle.Result;
            if (options.Layer >= 0)
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = options.Layer;                                           // R4-3
            return new LoadResult<GameObject>(instance);
        }

        public static Task<LoadResult<GameObject>> InstantiateAsync(string address)
            => InstantiateAsync(address, InstantiateOptions.Default);

        /// <summary><see cref="InstantiateAsync"/>로 만든 인스턴스를 파괴하고 번들 참조를 놓는다.</summary>
        public static void ReleaseInstance(GameObject instance)
        {
            if (instance == null) return;
            if (!Addressables.ReleaseInstance(instance))
                UnityEngine.Object.Destroy(instance);   // 로더 밖에서 만든 오브젝트였을 때
        }

        /// <summary>텍스처, 오디오 클립 등 단독 애셋. <see cref="Release{T}"/>로 해제한다 (R8-3).</summary>
        public static async Task<LoadResult<T>> LoadAssetAsync<T>(string address) where T : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetAsync<T>(address);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var failure = Classify(handle.OperationException, address);
                Addressables.Release(handle);
                return new LoadResult<T>(failure);
            }
            s_AssetHandles[handle.Result] = handle;
            return new LoadResult<T>(handle.Result);
        }

        public static void Release<T>(T asset) where T : UnityEngine.Object
        {
            if (asset == null) return;
            if (s_AssetHandles.TryGetValue(asset, out var handle))
            {
                s_AssetHandles.Remove(asset);
                Addressables.Release(handle);
            }
        }

        // ---------------------------------------------------------------- 씬 콘텐츠 (R11)

        /// <summary>
        /// 씬을 Additive 로 로드한다. setActive 가 true 면 활성 씬으로 지정하고 라이트 프로브를 합친다 (R11-1). [검증 필요]
        /// </summary>
        public static async Task<LoadResult<SceneInstance>> LoadSceneAsync(string address, bool setActive = true)
        {
            var handle = Addressables.LoadSceneAsync(address, LoadSceneMode.Additive);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var failure = Classify(handle.OperationException, address);
                Addressables.Release(handle);
                return new LoadResult<SceneInstance>(failure);
            }

            var scene = handle.Result;
            s_SceneHandles[scene.Scene] = handle;
            if (setActive)
            {
                SceneManager.SetActiveScene(scene.Scene);
                LightProbes.Tetrahedralize();
            }
            return new LoadResult<SceneInstance>(scene);
        }

        public static async Task<LoadResult> UnloadSceneAsync(SceneInstance scene)
        {
            if (!s_SceneHandles.TryGetValue(scene.Scene, out var handle))
                return new LoadResult(LoadFailure.Unknown, "scene was not loaded through ContentLoader");
            s_SceneHandles.Remove(scene.Scene);

            // autoReleaseHandle=false: 완료 직후 Status 를 읽어야 하므로 직접 해제한다 (카탈로그 로드와 같은 이유)
            var unload = Addressables.UnloadSceneAsync(handle, false);
            await unload.Task;
            var result = unload.Status == AsyncOperationStatus.Succeeded
                ? LoadResult.Ok
                : Classify(unload.OperationException, scene.Scene.name);
            Addressables.Release(unload);
            return result;
        }

        // ---------------------------------------------------------------- 목록

        /// <summary>라벨(`model`, `props` 등)에 해당하는 주소 목록. 카탈로그를 먼저 로드해야 한다.</summary>
        public static async Task<LoadResult<IList<string>>> GetAddressesByLabelAsync(string label)
        {
            var handle = Addressables.LoadResourceLocationsAsync(label);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                var failure = Classify(handle.OperationException, label);
                Addressables.Release(handle);
                return new LoadResult<IList<string>>(failure);
            }

            var addresses = new List<string>(handle.Result.Count);
            foreach (IResourceLocation loc in handle.Result)
                if (!addresses.Contains(loc.PrimaryKey))
                    addresses.Add(loc.PrimaryKey);
            Addressables.Release(handle);
            return new LoadResult<IList<string>>(addresses);
        }

        // ---------------------------------------------------------------- 내부

        static LoadResult Classify(Exception e, string context)
        {
            if (e == null)
                return new LoadResult(LoadFailure.Unknown, "failed: " + context);

            for (var cur = e; cur != null; cur = cur.InnerException)
            {
                if (cur is InvalidKeyException)
                    return new LoadResult(LoadFailure.KeyNotFound, context + " — " + cur.Message, e);
                if (cur is RemoteProviderException)
                    return new LoadResult(LoadFailure.Network, context + " — " + cur.Message, e);
                if (cur is ProviderException)
                    return new LoadResult(LoadFailure.Download, context + " — " + cur.Message, e);
            }

            string msg = e.Message ?? string.Empty;
            if (msg.Contains("Unable to load") || msg.Contains("Connection") || msg.Contains("HTTP") || msg.Contains("Insecure"))
                return new LoadResult(LoadFailure.Network, context + " — " + msg, e);
            return new LoadResult(LoadFailure.Unknown, context + " — " + msg, e);
        }
    }
}
