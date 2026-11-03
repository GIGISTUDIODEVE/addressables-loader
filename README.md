# AddressablesWork Loader

A 프로젝트(AddressablesWork)가 공급한 Addressables 콘텐츠를 받아 쓰는 공용 로더. 규칙 문서 8장을 코드로 옮긴 것.

## 새 프로젝트에서 바로 쓰기

Unity **6000.0 / 6000.2 / 6000.6**의 **URP 프로젝트**(Universal 3D 템플릿), Windows 기준.

1. **패키지 설치** — Package Manager → **+ → Install package from git URL…** (또는 `Packages/manifest.json`의 `dependencies`에 한 줄):
   ```
   https://github.com/GIGISTUDIODEVE/addressables-loader.git#v0.11.0
   ```
   Addressables(2.11.2)는 의존성으로 알아서 설치된다. 이미 설치돼 있으면 그대로 쓴다. git이 PATH에 있어야 한다.
2. **Setup 창** — 설치가 끝나면 **Tools → AddressablesWork → Setup**이 한 번 자동으로 열린다(안 열리면 메뉴에서). 환경 점검이 빨간색 없이 나오는지 본다:
   서버에 이 Unity 버전의 빌드가 있는지 / URP / Linear(버튼으로 바꿈) / Addressables 버전 / 빌드 타깃.
3. **테스트 씬 만들고 Play** 버튼 → 화면 왼쪽 **1. Load catalog → 2. Download all → Load all**. 서버는 기본값(GitHub Pages)이라 설정할 것이 없다.

- 샘플 Import는 필요 없다. 테스트 씬은 `Assets/AddressablesWork/RemoteContentTest.unity`, 컴포넌트는 `RemoteContentBrowser`(Add Component → AddressablesWork → Remote Content Browser로 다른 씬에도 붙일 수 있음).
- 처음 로드할 때 콘솔에 빨간 오류 2개(`Invalid path in TextDataProvider ... settings.json`, `Unable to load runtime data`)가 찍힌다. 이 프로젝트에 Addressables 빌드가 없어서 나는 것이고 **동작에는 영향이 없다.**
- 서버에 내 Unity 버전의 빌드가 없으면 **같은 메이저의 가장 가까운 하위 버전** 빌드를 받는다(예: 6000.3 → 6000.2). Setup 창은 노란색으로, 브라우저는 `using lower version 6000.2 (not guaranteed, R1-2)`로 알린다. 동작은 보장되지 않으므로 정확한 버전 빌드를 A에 요청한다. 상위 버전 빌드는 옛 Unity가 읽지 못해서 고르지 않는다. 끄려면 `RemoteContentBrowser.AllowLowerVersion`을 끈다(플레이어 인자 `-sampleNoFallback`).
- 하위 버전도 없으면 `Available: 6000.0, 6000.2, 6000.6`처럼 있는 버전을 알려준다.
- 직접 코드로 받을 때: `ServerVersions.FetchAsync(루트)` → `ServerVersions.Pick(목록, RemoteContentBrowser.UnityMinor(), true, out bool exact)`로 받을 버전 폴더를 고른다.
- 명령줄 확인: `-executeMethod AddressablesWork.Loader.Editor.RemoteSetupWindow.BuildTestPlayerFromCommandLine [-setupOutput <exe>] [-setupServer <루트>]` → 점검 결과를 로그에 쓰고 테스트 씬으로 플레이어를 빌드한다.
- HTTPS라 Player Settings의 HTTP 허용은 필요 없다. IL2CPP로 빌드하면 서버의 `link.xml`(카탈로그 옆)을 `Assets/`에 둔다.
- 배포 저장소는 [GIGISTUDIODEVE/addressables-loader](https://github.com/GIGISTUDIODEVE/addressables-loader) (원본에서 `Tools/publish-loader.cmd`로 복사한 사본. 손으로 고치지 않는다). 콘텐츠 서버는 `https://gigistudiodeve.github.io/addressables-cdn/A/<Unity 마이너>/StandaloneWindows64/`.

## 설치 (A·B 개발용)

A와 B는 원본을 로컬 경로로 쓴다:

```json
"com.addressableswork.loader": "file:../../Packages/com.addressableswork.loader"
```

Addressables 2.11.2가 함께 설치된다.

## 사용

```csharp
using AddressablesWork.Loader;

var cat = await ContentLoader.LoadCatalogAsync("http://localhost:8080/6000.6/StandaloneWindows64/catalog_content.bin");
if (!cat.Succeeded) { Debug.LogError(cat); return; }

var cube = await ContentLoader.InstantiateAsync("A/Models/Props/TestCube", InstantiateOptions.At(Vector3.zero));
if (cube.Succeeded) ContentLoader.ReleaseInstance(cube.Value);   // 다 쓰면 반드시 해제

var tex = await ContentLoader.LoadAssetAsync<Texture2D>("A/Textures/UI/Icon");
ContentLoader.Release(tex.Value);

var scene = await ContentLoader.LoadSceneAsync("A/Scenes/Lobby");   // Additive, 활성 씬으로 지정
await ContentLoader.UnloadSceneAsync(scene.Value);
```

실패는 `LoadResult.Failure`로 구분한다: `Network`(서버·HTTP 차단, 자동 재시도), `KeyNotFound`(주소 없음), `Download`, `Unknown`.

### 카탈로그 갱신 (R7-3)

A가 새 빌드를 올리면 `catalog_content.bin`과 `.hash`가 바뀐다. 번들은 해시 이름이라 옛 것과 새 것이 서버에 공존하고, 이미 생성된 인스턴스는 영향받지 않는다. 재시작은 필요 없다.

```csharp
var changed = await ContentLoader.CheckForCatalogUpdateAsync(catalogUrl);   // 서버 .hash 와 로드 때 값을 비교
if (changed.Succeeded && changed.Value)
    await ContentLoader.ReloadCatalogAsync(catalogUrl);                     // 옛 로케이터를 빼고 다시 로드 → 새 주소가 보임
```

`LoadCatalogAsync`는 로드 직후 `.hash`를 한 번 더 읽어 둔다(읽기 실패해도 로드는 성공으로 본다).

## RemoteContentBrowser (원격 콘텐츠 받아 보기)

버튼으로 카탈로그 로드 → 라벨(`model`, `texture`, `audio`, `scene`)별 주소 목록·다운로드 크기 → 미리 다운로드 → 주소마다 Load/Release → 갱신 확인을 해 본다. 카탈로그 URL은 실행 중인 Unity 마이너 버전으로 자동으로 정한다. 기본 서버는 GitHub Pages이고, 로컬 서버로 보려면 `ServerRoot`(또는 Setup 창의 "서버")를 `http://localhost:8080`으로 바꾼다. 플레이어 자동 확인: `-sampleAuto <폴더> [-sampleServer <루트>] [-sampleUnityVersion <버전>]`.

`Samples~/BasicUsage`(v0.8 ~ v0.9)는 이 컴포넌트를 상속한 호환용 껍데기만 남았다.

## 확인용 씬

**Tools → Addressables Loader → Create Smoke Test Scene** — `Assets/Scenes/LoaderTest.unity`를 만들고 빌드 설정의 첫 씬으로 넣는다. `LoaderSmokeTest` 컴포넌트가 Start에서 카탈로그를 받고 `A/Models/Props/TestCube`를 생성한 뒤, `A/Textures/Test/TestPattern`을 받아 화면 오른쪽 위에 그리고 `A/Audio/SFX/Tone`을 받아 재생한다(주소를 비우면 건너뜀). 화면에 로그와 Load / Release 버튼이 나온다. Release는 세 애셋을 모두 해제한다.

- `LoadAssetAsync<T>`로 받은 애셋은 `Release(asset)`로 해제한다. 해제하면 번들 참조가 0이 될 때 애셋이 파괴되므로(`asset == null`) 해제 뒤에 쓰지 않는다.
- 사운드는 임포트 설정의 `Streaming` 로드 타입이 번들에서 그대로 동작한다 (2026-11-02 B 플레이어에서 확인).
- 씬은 `LoadSceneAsync`가 Additive로 올리고 활성 씬으로 지정한다. 다 쓰면 `UnloadSceneAsync(sceneInstance)`. 스모크 테스트는 `A/Scenes/TestRoom`을 먼저 올리고 그 안에 모델을 생성한다 (`-smokeScene <주소>`, `-`면 건너뜀). 올린 뒤 라이트맵·프로브 수를 기록하고, 베이크된 조명만 보이도록 다른 씬의 Light를 껐다가 언로드 때 되돌린다. 카메라의 오클루전 컬링을 껐다 켜며 씬 Renderer의 `isVisible` 수(`Hidden_*` 포함)를 활성 씬 두 경우로 기록한다.

## 소비자 조건

- Unity 버전에 맞는 `ServerData/<버전>/` 카탈로그 URL을 쓴다.
- 첫 단계 호스트는 HTTP이므로 **Player Settings → Allow downloads over HTTP = Development builds only**, 플레이어는 **Development Build**.
- 소비자에 Addressables 설정이 없어도 된다 (2026-11-01 B 플레이어에서 확인). 초기화 때 `Unable to load runtime data` 에러 2개가 콘솔에 찍히지만 원격 카탈로그·생성은 정상이다.

## 자동 검증

`SmokeTestBuild.BuildFromCommandLine`(배치 모드, `-smokeOutput`, `-smokeCatalog`)로 Development 플레이어를 만들고, 플레이어를 `-smokeAuto <폴더>`로 실행하면 `smoke.txt`(OK/FAIL + 로그 + 해제 뒤 파괴 여부)와 `smoke.png`를 쓰고 종료한다. `-smokeCatalog`, `-smokeAddress`, `-smokeTexture`, `-smokeAudio`, `-smokeScene`으로 대상을 바꾼다. 절차는 AddressablesWork/DEVELOPMENT_GUIDE.md 8.1.

- 빌드 옵션: `-smokeIl2cpp`(IL2CPP + Strip Engine Code, 규칙 9장 `link.xml` 확인용), `-smokeLinkXml <A가 배포한 link.xml 경로>`(`Assets/link.xml`로 복사, `-`면 지움).
- 실행 옵션: `-smokeUpdate <새 주소> [-smokeUpdateTimeout <초, 기본 180>]` — 서버 카탈로그의 `.hash`가 바뀔 때까지 2초마다 확인하고, 바뀌면 `ReloadCatalogAsync` 뒤 그 주소를 생성한다 (R7-3 검증). 그 주소가 처음부터 있으면 검증이 아니라서 FAIL.
- 플레이어는 `-batchmode` 없이 돌린다. 렌더링이 없으면 오클루전 수치가 0이 된다.
