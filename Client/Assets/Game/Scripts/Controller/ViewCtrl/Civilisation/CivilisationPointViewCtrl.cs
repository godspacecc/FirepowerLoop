using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;

namespace Game
{
    /// <summary>绑定场景交互物与当前地图身份，只转发交互意图；不持有搜索、金币或永久科技状态。</summary>
    public sealed class CivilisationPointViewCtrl : IController
    {
        private readonly CivilisationPointView view;
        public CivilisationPointViewCtrl(CivilisationPointView view) { this.view = view; }
        public IArchitecture GetArchitecture() => Game.Interface;
        private bool TryContext(out long instance)
        {
            instance = 0;
            if(this.GetSystem<PlayerMoveSystem>()?.IsSwimming==true)return false;
            if (view == null || !view.isActiveAndEnabled || UIHelper.HasUIOpen || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing) return false;
            if (this.GetModel<TemporaryBuildingModel>().Mode != TemporaryBuildingMode.Normal) return false;
            var map = this.GetModel<MapSceneModel>();
            if (map.Status.Value != MapLoadStatus.Loaded || map.CurrentMode != MapPreparationMode.OfflineFull ||
                map.CurrentHandle?.Context == null || view.gameObject.scene != map.CurrentHandle.Context.gameObject.scene ||
                !view.transform.IsChildOf(map.CurrentHandle.Context.MapContentRoot.transform) ||
                !this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) || !player.IsActive ||
                player.MapInstanceId != this.GetSystem<MapSceneSystem>().CurrentMapInstanceId ||
                (player.Position - view.transform.position).sqrMagnitude > 2.25f * 2.25f) return false;
            instance = player.MapInstanceId;
            return map.CurrentMapId == (view.Kind == CivilisationPointKind.Workshop ? MapIds.Base : MapIds.CivilisationValley);
        }
        public bool CanInteract
        {
            get
            {
                if (!TryContext(out _)) return false;
                if (view.Kind == CivilisationPointKind.Search)
                {
                    // 名称与交互可用性投影配置规则，旧访问记录不能耗尽可重复水源；提交仍由搜索系统裁决。
                    if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _)) return false;
                    CivilisationMapPointResolver.TryGetPoint(this.GetModel<MapSceneModel>().CurrentHandle,catalog,view.PointId,out var point);
                    return point != null && (point.Repeatable || !this.GetModel<CivilisationExpeditionModel>().HasVisited(view.PointId));
                }
                if (view.Kind == CivilisationPointKind.Preparation) return !this.GetModel<CivilisationExpeditionModel>().PreparationClaimed;
                return true;
            }
        }
        /// <summary>每次点击重新验证场景与地图实例，取消旧对象回调对新地图的影响。</summary>
        public void Interact()
        {
            if (!CanInteract || !TryContext(out var instance)) return;
            string error = null;
            switch (view.Kind)
            {
                case CivilisationPointKind.Search:
                    if (!this.GetSystem<CivilisationSearchSystem>().TryBeginSearch(instance, view.PointId, out error)) Notify.ShowFloatTips(Loc.T(error));
                    break;
                case CivilisationPointKind.Clue:
                    if(this.GetSystem<CivilisationExplorationSystem>().TryReadClue(instance,view.PointId,out var key,out error))
                    {
                        var map=this.GetModel<MapSceneModel>();var layout=(map.CurrentHandle as GeneratedValleyContentHandle)?.Layout;
                        var clue=layout?.Clues.FirstOrDefault(c=>c.Id==view.PointId);
                        // 世界中的方位来自本局布局；完整观察记录可在日志中反复阅读。
                        Notify.ShowFloatTips(Loc.T("civilisation.clue.recorded",clue==null?string.Empty:Loc.T(CivilisationExplorationSystem.DirectionKey(clue.Target.ToVector()-clue.Pose.ToVector()))));
                    }
                    else Notify.ShowFloatTips(Loc.T(error));
                    break;
                case CivilisationPointKind.Shortcut:
                    DiscoverShortcutAsync(instance).Forget();
                    break;
                case CivilisationPointKind.Preparation:
                    var success = this.GetSystem<CivilisationSearchSystem>().TryClaimBasicPreparation(out error);
                    Notify.ShowFloatTips(success ? Loc.T("civilisation.prepare.done") : Loc.T(error));
                    break;
                case CivilisationPointKind.Workshop:
                    UIHelper.ShowUI<CivilisationWorkshopUICtrl>();
                    break;
                case CivilisationPointKind.Return:
                    Notify.ShowMessage(Loc.T("civilisation.return.title"), Loc.T("civilisation.return.warning"), new List<MessageButtonConfig>
                    {
                        new MessageButtonConfig { ButtonText = Loc.T("civilisation.point.return"), Role = MessageButtonRole.Confirm, OnClick = () => ReturnAsync(instance).Forget() },
                        new MessageButtonConfig { ButtonText = Loc.T("civilisation.cancel"), Role = MessageButtonRole.Cancel }
                    });
                    break;
            }
        }
        private async UniTask ReturnAsync(long expectedInstance)
        {
            // 对话框关闭后才能返程；重验原实例，已销毁或已换图的按钮不会再次发起结算。
            if (view == null || !view.isActiveAndEnabled || this.GetSystem<MapSceneSystem>().CurrentMapInstanceId != expectedInstance) return;
            var result = await this.GetSystem<ExpeditionFlowSystem>().ReturnToBaseAsync();
            if (!result.Success) Notify.ShowFloatTips(result.Error);
        }
        private async UniTask DiscoverShortcutAsync(long expectedInstance)
        {
            var result=await this.GetSystem<CivilisationExplorationSystem>().DiscoverReturnShortcutAsync(expectedInstance,view.PointId);
            Notify.ShowFloatTips(result.Success?Loc.T("civilisation.shortcut.opened"):Loc.T(result.Error));
        }
    }
}
