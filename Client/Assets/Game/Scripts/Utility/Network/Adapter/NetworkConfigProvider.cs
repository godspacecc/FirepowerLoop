using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Network
{
    public interface INetworkConfigProvider : IUtility
    {
        Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 提供玩家可编辑的 GameGate 覆盖地址；只负责格式校验与持久化，不负责切换现有网络会话。
    /// </summary>
    public interface IEditableNetworkConfigProvider : INetworkConfigProvider
    {
        bool TrySaveEndpoint(string value, out NetworkEndpoint endpoint, out string diagnostic);
    }

    public interface IResettableNetworkConfigProvider : IEditableNetworkConfigProvider
    {
        bool TryResetEndpoint(out string diagnostic);
    }

    public enum NetworkConfigError
    {
        None,
        Invalid
    }

    public readonly struct NetworkConfigResult
    {
        public NetworkConfigResult(bool success, NetworkEndpoint endpoint, NetworkConfigError error, string diagnostic)
        {
            Success = success;
            Endpoint = endpoint;
            Error = error;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public NetworkEndpoint Endpoint { get; }
        public NetworkConfigError Error { get; }
        public string Diagnostic { get; }

        public static NetworkConfigResult Ok(NetworkEndpoint endpoint)
        {
            return new NetworkConfigResult(true, endpoint, NetworkConfigError.None, string.Empty);
        }

        public static NetworkConfigResult Invalid(string diagnostic)
        {
            return new NetworkConfigResult(false, default, NetworkConfigError.Invalid, diagnostic);
        }
    }

    public sealed class StreamingAssetsNetworkConfigProvider : IResettableNetworkConfigProvider
    {
        public const string FileName = "networkConfig.json";
        public const string OverrideFileName = "network-endpoint.json";
        public static readonly NetworkEndpoint DefaultEndpoint = new NetworkEndpoint("127.0.0.1", 31011);
        private readonly Func<CancellationToken, Task<string>> readText;
        private readonly string overrideFilePath;

        public StreamingAssetsNetworkConfigProvider()
            : this(ReadStreamingAssetsConfig, Application.persistentDataPath)
        {
        }

        internal StreamingAssetsNetworkConfigProvider(Func<CancellationToken, Task<string>> readText)
            : this(readText, null)
        {
        }

        internal StreamingAssetsNetworkConfigProvider(Func<CancellationToken, Task<string>> readText, string persistentRoot)
        {
            this.readText = readText ?? throw new ArgumentNullException(nameof(readText));
            overrideFilePath = string.IsNullOrWhiteSpace(persistentRoot)
                ? null
                : Path.Combine(persistentRoot, "Online", OverrideFileName);
        }

        public async Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken)
        {
            NetworkConfigResult? saved = LoadSavedEndpoint();
            if (saved.HasValue)
            {
                return saved.Value;
            }

            string json;
            try
            {
                json = await readText(cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return NetworkConfigResult.Ok(DefaultEndpoint);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return NetworkConfigResult.Invalid(e.GetType().Name);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return NetworkConfigResult.Invalid("empty");
            }

            try
            {
                var root = JObject.Parse(json);
                string value = root.Value<string>("gameGateEndpoint");
                return NetworkEndpoint.TryParse(value, out var endpoint)
                    ? NetworkConfigResult.Ok(endpoint)
                    : NetworkConfigResult.Invalid("endpoint");
            }
            catch (Exception e)
            {
                return NetworkConfigResult.Invalid(e.GetType().Name);
            }
        }

        /// <summary>
        /// 原子保存规范化端点。失败时保留旧文件，调用者可继续使用上一次有效地址。
        /// </summary>
        public bool TrySaveEndpoint(string value, out NetworkEndpoint endpoint, out string diagnostic)
        {
            endpoint = default;
            diagnostic = null;
            if (!NetworkEndpoint.TryParse(value, out endpoint))
            {
                diagnostic = "endpoint";
                return false;
            }
            if (string.IsNullOrWhiteSpace(overrideFilePath))
            {
                diagnostic = "persistence-unavailable";
                return false;
            }

            string directory = Path.GetDirectoryName(overrideFilePath);
            string tempPath = overrideFilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                var root = new JObject
                {
                    ["version"] = 1,
                    ["gameGateEndpoint"] = endpoint.ToString()
                };
                File.WriteAllText(tempPath, root.ToString());
                if (File.Exists(overrideFilePath))
                {
                    File.Replace(tempPath, overrideFilePath, null, true);
                }
                else
                {
                    File.Move(tempPath, overrideFilePath);
                }
                Boot.DiagnosticReportSender.UseGameGateEndpoint(endpoint.ToString());
                return true;
            }
            catch (Exception e)
            {
                diagnostic = e.GetType().Name;
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupException)
                {
                    Debug.LogWarning($"[NetworkConfigProvider] 删除端点临时文件失败：{cleanupException.GetType().Name}");
                }
                return false;
            }
        }

        /// <summary>删除玩家覆盖文件；下一次读取重新使用安装包地址或内置默认端点。</summary>
        public bool TryResetEndpoint(out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(overrideFilePath))
            {
                diagnostic = "persistence-unavailable";
                return false;
            }

            try
            {
                if (File.Exists(overrideFilePath))
                {
                    File.Delete(overrideFilePath);
                }
                Boot.DiagnosticReportSender.RefreshGameGateEndpoint();
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.GetType().Name;
                return false;
            }
        }

        private NetworkConfigResult? LoadSavedEndpoint()
        {
            if (string.IsNullOrWhiteSpace(overrideFilePath) || !File.Exists(overrideFilePath))
            {
                return null;
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(overrideFilePath));
                if (root.Value<int?>("version") != 1)
                {
                    return NetworkConfigResult.Invalid("override-version");
                }
                return NetworkEndpoint.TryParse(root.Value<string>("gameGateEndpoint"), out NetworkEndpoint endpoint)
                    ? NetworkConfigResult.Ok(endpoint)
                    : NetworkConfigResult.Invalid("override-endpoint");
            }
            catch (Exception e)
            {
                return NetworkConfigResult.Invalid(e.GetType().Name);
            }
        }

        private static async Task<string> ReadStreamingAssetsConfig(CancellationToken cancellationToken)
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (path.Contains("://"))
            {
                using (UnityWebRequest request = UnityWebRequest.Get(path))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        throw new IOException(request.error);
                    }

                    return request.downloadHandler.text;
                }
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(FileName);
            }

            using (var reader = new StreamReader(path))
            {
                return await reader.ReadToEndAsync();
            }
        }
    }
}
