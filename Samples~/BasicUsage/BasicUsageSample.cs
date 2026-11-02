using AddressablesWork.Loader;
using UnityEngine;

namespace AddressablesWork.Loader.Samples
{
    /// <summary>
    /// v0.10.0 부터 내용은 로더 본체의 <see cref="RemoteContentBrowser"/> 로 옮겼다 (샘플을 Import 하지 않아도 쓰도록).
    /// 이 클래스는 v0.8 ~ v0.9 에 만든 씬이 깨지지 않게 남겨 둔 껍데기다. 새 씬은 Tools → AddressablesWork → Setup 으로 만든다.
    /// 흐름을 코드로 보려면 RemoteContentBrowser.cs 를 연다.
    /// </summary>
    [AddComponentMenu("")]
    public class BasicUsageSample : RemoteContentBrowser
    {
    }
}
