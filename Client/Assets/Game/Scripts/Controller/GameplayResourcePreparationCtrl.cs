using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>领域进入前的资源准备端口；失败或取消不代表地图、存档或账号已变化。</summary>
    public interface IGameplayResourcePreparation : IUtility
    {
        UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 编排玩法资源确认与进度，只展示资源 System 的结果；不更新清单、不解锁地图、不创建游戏局。
    /// 从查询到确认、下载终态持有单一请求；等待后复核调用者，防止过期请求进入玩法。
    /// </summary>
    public sealed class GameplayResourcePreparationCtrl : IGameplayResourcePreparation, IController
    {
        private readonly IArchitecture architecture;
        private int generation;
        private bool busy;
        public GameplayResourcePreparationCtrl(IArchitecture architecture) => this.architecture = architecture;
        public IArchitecture GetArchitecture() => architecture;

        public async UniTask<bool> PrepareAsync(string mapId, Func<bool> isCurrent = null, CancellationToken cancellationToken = default)
        {
            if (busy || cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent()) return false;
            busy = true;
            int request = ++generation;
            LoadingUICtrl loading = null;
            MessageUICtrl message = null;
            List<MessageButtonConfig> confirmationButtons = null;
            bool downloading = false;
            try
            {
                var resources = this.GetSystem<OptionalGameplayResourceSystem>();
                var query = await resources.CheckMapAsync(mapId);
                if (cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent()) return false;
                if (query.State == OptionalGameplayResourceState.Ready) return true;
                if (query.State != OptionalGameplayResourceState.NeedsDownload)
                {
                    Log.Warning("[GameplayResourcePreparationCtrl] 资源检查失败：" + query.Error);
                    Notify.ShowFloatTips(Loc.T("ui.optional.content.unavailable"));
                    return false;
                }

                string name = mapId == MapIds.Arcade ? Loc.T("ui.optional.content.arcade") :
                    MapCatalog.TryGet(mapId, out var map) ? Loc.T(map.DisplayNameKey) : mapId;
                var confirmation = new UniTaskCompletionSource<bool>();
                confirmationButtons = new List<MessageButtonConfig>
                    {
                        new MessageButtonConfig { ButtonText = Loc.T("ui.common.confirm"), Role = MessageButtonRole.Confirm,
                            OnClick = () => confirmation.TrySetResult(true) },
                        new MessageButtonConfig { ButtonText = Loc.T("ui.common.cancel"), Role = MessageButtonRole.Cancel,
                            OnClick = () => confirmation.TrySetResult(false) }
                    };
                Notify.ShowMessage(Loc.T("ui.optional.content.title"),
                    Loc.T("ui.optional.content.confirm", name, ResourceDownloadSize.Format(query.TotalBytes), query.FileCount),
                    confirmationButtons);
                message = UIHelper.GetUICtrl<MessageUICtrl>();
                // 确认期间也检测调用者失效；晚到按钮只完成本地令牌，不发起新事务。
                while (confirmation.Task.Status == UniTaskStatus.Pending)
                {
                    if (resources.IsReleased || cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent() ||
                        message?.IsShowingButtons(confirmationButtons) != true) return false;
                    await UniTask.Yield();
                }
                if (!await confirmation.Task || cancellationToken.IsCancellationRequested || isCurrent != null && !isCurrent()) return false;
                loading = UIHelper.ShowUI<LoadingUICtrl>();
                if (loading == null || !loading.IsShow()) return false;
                downloading = true;
                loading.BeginResourceDownload(request, new OptionalGameplayDownloadProgress(0, query.TotalBytes, 0, query.FileCount));
                var result = await resources.DownloadMapAsync(mapId, query.Version,
                    progress => loading?.SetResourceDownloadProgress(request, progress), cancellationToken);
                loading.EndResourceDownload(request, result.Success);
                downloading = false;
                return result.Success && !cancellationToken.IsCancellationRequested && (isCurrent == null || isCurrent());
            }
            catch (Exception exception)
            {
                Log.Warning("[GameplayResourcePreparationCtrl] 资源准备失败：" + exception.Message);
                Notify.ShowFloatTips(Loc.T("ui.optional.content.failed"));
                return false;
            }
            finally
            {
                // 仅清理本请求仍拥有的确认框，不能隐藏其他业务覆盖的新弹窗。
                if (message?.IsShowingButtons(confirmationButtons) == true) message.Hide();
                if (downloading) loading?.EndResourceDownload(request, false);
                busy = false;
            }
        }
    }
}
