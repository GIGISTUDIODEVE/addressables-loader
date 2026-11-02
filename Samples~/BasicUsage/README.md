# Basic Usage 샘플

A 프로젝트가 서버에 올린 콘텐츠를 소비자(B)가 받아 쓰는 기본 흐름. 코드는 `ContentLoader`만 쓴다 (규칙 R8-2).

| 순서 | 버튼 | 로더 API |
|---|---|---|
| 1 | **1. Load catalog** | `LoadCatalogAsync(<서버>/<Unity 마이너>/<타깃>/catalog_content.bin)` → `GetAddressesByLabelAsync(label)` → `GetDownloadSizeAsync(address)` |
| 2 | **2. Download all** | `DownloadAsync(label)` — 번들을 캐시에 받아 둔다. 끝나면 크기가 `cached`로 바뀐다 |
| 3 | 줄마다 **Load / Release**, **Load all / Release all** | 라벨에 따라 `InstantiateAsync`(model), `LoadAssetAsync<Texture2D>`(texture), `LoadAssetAsync<AudioClip>`(audio), `LoadSceneAsync`(scene) / `ReleaseInstance`, `Release`, `UnloadSceneAsync` |
| 4 | **Check update** | `CheckForCatalogUpdateAsync` → 바뀌었으면 `ReloadCatalogAsync` 후 목록 갱신 (R7-3) |
| — | **Clear cache** | `Caching.ClearCache()` — 다운로드를 다시 보여주기 위한 것. 모두 해제한 뒤에만 된다 |

## 쓰는 법

1. 서버: 기본값은 클라우드(GitHub Pages)라 할 일이 없다. 로컬 서버로 보려면 A에서 Default 프로필로 빌드하고 `Tools/serve-bundles.cmd`를 띄운 뒤 `ServerRoot`를 `http://localhost:8080`으로 바꾼다 (가이드 7.1·7.1a).
2. 소비자 프로젝트에서 **Window → Package Manager → AddressablesWork Loader → Samples → Basic Usage → Import**.
3. **Tools → Addressables Loader → Create Basic Usage Scene** → `Assets/Scenes/BasicUsage.unity`가 생기고 빌드 설정에 추가된다.
4. Play, 또는 **Development Build**로 플레이어를 만들어 실행한다 (HTTP는 Development 빌드에서만, 규칙 12장). 최종 확인은 플레이어다 (R10-1).

- 카탈로그 URL은 실행 중인 Unity 버전으로 정한다. 6000.6 플레이어는 `.../6000.6/StandaloneWindows64/`를 받는다. 서버 주소는 `BasicUsageSample.ServerRoot`.
- **기본 서버는 클라우드(GitHub Pages)** `https://gigistudiodeve.github.io/addressables-cdn/A` (A가 `GitHubPages` 프로필로 빌드해 올린 것, 가이드 7.5). HTTPS라 HTTP 허용 설정이 필요 없고 다른 PC에서도 된다. 로컬 서버(`localhost:8080`)용 빌드는 번들 주소가 localhost로 박혀 있어 같은 PC에서만 된다.
- 카탈로그를 못 받으면 서버의 `versions.json`을 읽어 `no build for Unity <버전> on this server. Available: ...`처럼 알려준다 (클라우드 서버만).
- 환경 씬을 올린 동안 이 씬의 Light를 끈다(`DisableOwnLightsWhileSceneLoaded`). 켜 두면 베이크된 씬이 이중으로 밝아진다 (R11-2).
- `Download all`의 크기는 주소별 크기의 합이라 공유 번들이 여러 번 더해진다. 실제로 받는 양은 그보다 작다.

## 자동 확인

```
BasicUsage.exe -sampleAuto <결과 폴더> [-sampleServer http://localhost:8080] [-sampleUnityVersion 6000.3]
```

캐시 비우기 → 카탈로그 → 다운로드(크기가 0보다 컸다가 0이 되는지) → 전부 로드 → 스크린샷 `sample.png` → 전부 해제(애셋이 파괴되는지) 순서로 돌고, `sample.txt`(첫 줄 OK/FAIL + 로그)를 쓰고 종료한다. 렌더링이 필요하므로 `-batchmode` 없이 실행한다.

명령줄 빌드: `Unity.exe -batchmode -projectPath <B> -buildTarget Win64 -executeMethod AddressablesWork.Loader.Samples.Editor.BasicUsageSceneCreator.BuildFromCommandLine -sampleOutput <exe>`
