# Basic Usage 샘플

v0.10.0부터 **샘플을 Import하지 않아도 된다.** 원격 콘텐츠를 받아 보는 컴포넌트는 로더 본체의 `RemoteContentBrowser`이고, 씬은 **Tools → AddressablesWork → Setup → 테스트 씬 만들기**로 만든다 (로더 README "새 프로젝트에서 바로 쓰기").

이 샘플에는 v0.8 ~ v0.9에서 만든 씬이 깨지지 않도록 `BasicUsageSample`(= `RemoteContentBrowser`를 상속한 빈 클래스)만 남아 있다. 흐름을 코드로 보려면 패키지의 `Runtime/RemoteContentBrowser.cs`를 연다.

| 순서 | 버튼 | 로더 API |
|---|---|---|
| 1 | **1. Load catalog** | `LoadCatalogAsync(<서버>/<Unity 마이너>/<타깃>/catalog_content.bin)` → `GetAddressesByLabelAsync(label)` → `GetDownloadSizeAsync(address)` |
| 2 | **2. Download all** | `DownloadAsync(label)` |
| 3 | 줄마다 **Load / Release**, **Load all / Release all** | `InstantiateAsync`·`LoadAssetAsync<Texture2D>`·`LoadAssetAsync<AudioClip>`·`LoadSceneAsync` / `ReleaseInstance`·`Release`·`UnloadSceneAsync` |
| 4 | **Check update** | `CheckForCatalogUpdateAsync` → `ReloadCatalogAsync` (R7-3) |
| — | **Clear cache** | `Caching.ClearCache()` (모두 해제한 뒤) |

자동 확인(플레이어): `-sampleAuto <결과 폴더> [-sampleServer <루트>] [-sampleUnityVersion <버전>]` → `sample.txt`(OK/FAIL), `sample.png`.
