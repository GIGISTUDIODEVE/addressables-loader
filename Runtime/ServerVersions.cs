using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AddressablesWork.Loader
{
    /// <summary>
    /// 서버에 올라가 있는 Unity 버전 목록(&lt;루트&gt;/versions.json)과, 이 플레이어가 받을 버전 고르기 (R1-2).
    /// 정확한 마이너 빌드가 없으면 같은 메이저의 가장 가까운 **하위** 마이너로 대체한다. 상위 버전 빌드는 옛 플레이어가 읽지 못해서 고르지 않는다.
    /// </summary>
    public static class ServerVersions
    {
        [Serializable] sealed class VersionList { public string[] versions; }

        /// <summary>versions.json 내용을 읽는다. 형식이 다르면 null.</summary>
        public static string[] Parse(string json)
        {
            try { return JsonUtility.FromJson<VersionList>(json)?.versions; }
            catch (ArgumentException) { return null; }
        }

        /// <summary>
        /// &lt;루트&gt;/versions.json 을 받는다. 없거나(로컬 서버) 실패하면 null — 그때는 이 플레이어의 버전을 그대로 쓴다.
        /// </summary>
        public static async Task<string[]> FetchAsync(string serverRoot, int timeoutSeconds = 20)
        {
            using var req = UnityWebRequest.Get(serverRoot.TrimEnd('/') + "/versions.json?t=" + DateTime.UtcNow.Ticks);
            req.timeout = timeoutSeconds;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            return req.result == UnityWebRequest.Result.Success ? Parse(req.downloadHandler.text) : null;
        }

        /// <summary>
        /// <paramref name="versions"/> 에서 <paramref name="minor"/>("6000.2") 에 쓸 버전을 고른다.
        /// 있으면 그대로(exact = true). 없고 <paramref name="allowLower"/> 이면 같은 메이저에서 그보다 낮은 것 중 가장 높은 것(exact = false). 없으면 null.
        /// </summary>
        public static string Pick(string[] versions, string minor, bool allowLower, out bool exact)
        {
            exact = false;
            if (versions == null) return null;
            if (Array.IndexOf(versions, minor) >= 0) { exact = true; return minor; }
            if (!allowLower || !TryParse(minor, out int major, out int min)) return null;

            string best = null;
            int bestMinor = -1;
            foreach (string v in versions)
                if (TryParse(v, out int vMajor, out int vMinor) && vMajor == major && vMinor < min && vMinor > bestMinor)
                {
                    best = v;
                    bestMinor = vMinor;
                }
            return best;
        }

        static bool TryParse(string version, out int major, out int minor)
        {
            major = minor = 0;
            string[] parts = version?.Split('.');
            return parts != null && parts.Length >= 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor);
        }
    }
}
