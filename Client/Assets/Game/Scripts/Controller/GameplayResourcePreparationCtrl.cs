using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>领域进入前的本地资源检查端口；资源下载统一由 Boot 完成。</summary>
    public interface IGameplayResourcePreparation : IUtility
    {
        UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default);
    }

    /// <summary>只检查启动资源是否就绪并展示失败提示，不发起独立下载、不创建游戏局。</summary>
    public sealed class GameplayResourcePreparationCtrl : IGameplayResourcePreparation, IController
    {
        /// <summary>沿用入口所在架构，查询与地图事务相同的资源状态。</summary>
        private readonly IArchitecture architecture;
        public GameplayResourcePreparationCtrl(IArchitecture architecture) => this.architecture = architecture;
        public IArchitecture GetArchitecture() => architecture;

        /// <summary>保留现有异步端口以兼容入口生命周期；本地检查直接完成，不等待服务器或下载确认。</summary>
        public UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent())
                return UniTask.FromResult(false);
            try
            {
                var resources = this.GetSystem<OptionalGameplayResourceSystem>();
                if (resources == null) throw new InvalidOperationException("启动资源检查系统未注册");
                var query = resources.QueryMap(mapId);
                if (cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent())
                    return UniTask.FromResult(false);
                if (query.State == OptionalGameplayResourceState.Ready)
                    return UniTask.FromResult(true);

                Log.Warning("[GameplayResourcePreparationCtrl] 启动资源不完整：" + query.Error);
                Notify.ShowFloatTips(Loc.T("ui.optional.content.unavailable") + "\n" + Loc.T("ui.start.update.restart_now"));
                return UniTask.FromResult(false);
            }
            catch (Exception exception)
            {
                Log.Warning("[GameplayResourcePreparationCtrl] 本地资源检查失败：" + exception.Message);
                Notify.ShowFloatTips(Loc.T("ui.optional.content.unavailable") + "\n" + Loc.T("ui.start.update.restart_now"));
                return UniTask.FromResult(false);
            }
        }
    }
}
