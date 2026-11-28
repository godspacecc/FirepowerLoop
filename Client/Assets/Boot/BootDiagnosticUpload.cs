using UnityEngine.Networking;

namespace SharedBoot
{
    /// <summary>为配置中已验证的地址创建诊断上报请求。</summary>
    internal static class BootDiagnosticUpload
    {
        internal static UnityWebRequest Create(string url, byte[] body)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                redirectLimit = 0,
                timeout = 5
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            return request;
        }
    }
}
