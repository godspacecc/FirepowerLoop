using System;
using System.Threading;
using System.Threading.Tasks;
using Boot;
using QFramework;

namespace Game
{
    public readonly struct ResourceServerConfigResult
    {
        public ResourceServerConfigResult(bool success, string resourceServer, string diagnostic)
        {
            Success = success;
            ResourceServer = resourceServer ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public string ResourceServer { get; }
        public string Diagnostic { get; }
    }

    /// <summary>
    /// 向设置界面暴露资源服务器地址的读取、覆盖与恢复默认能力；资源连接仍由 Boot 和更新系统负责。
    /// </summary>
    public interface IResourceServerConfigProvider : IUtility
    {
        Task<ResourceServerConfigResult> LoadAsync(CancellationToken cancellationToken);
        bool TrySave(string value, out string resourceServer, out string diagnostic);
        bool TryReset(out string diagnostic);
    }

    public sealed class ResourceServerConfigProvider : IResourceServerConfigProvider
    {
        public async Task<ResourceServerConfigResult> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                BootstrapConfig config = await BootstrapConfig.LoadAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return new ResourceServerConfigResult(true, config.ResourceServer, string.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new ResourceServerConfigResult(false, string.Empty, exception.GetType().Name);
            }
        }

        public bool TrySave(string value, out string resourceServer, out string diagnostic)
        {
            return ResourceServerPreference.TrySave(value, out resourceServer, out diagnostic);
        }

        public bool TryReset(out string diagnostic)
        {
            return ResourceServerPreference.TryReset(out diagnostic);
        }
    }
}
