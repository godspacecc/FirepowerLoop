using System;
using QFramework;

namespace Game
{
    public static class InventoryOpenRules
    {
        public static bool CanOpenPlayerBag(out string error)
        {
            return CanOpenPlayerBag(Game.Interface, out error);
        }

        public static bool CanOpenPlayerBag(IArchitecture architecture, out string error)
        {
            if (architecture == null || architecture.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                error = "库存需要 游戏中状态";
                return false;
            }

            OnlineWorldSnapshot onlineWorld = architecture.GetSystem<OnlineWorldSystem>()?.Snapshot;
            if(architecture.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly)
            {
                if(onlineWorld?.HasWorld==true&&onlineWorld.SelfUnitId!=0&&!onlineWorld.ConnectionLost)
                {error=null;return true;}
                error="联机世界尚未恢复";return false;
            }

            if (!architecture.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld())
            {
                error = "库存需要有效活动世界";
                return false;
            }

            var mapId = architecture.GetModel<MapSceneModel>().CurrentMapId;
            var risk = architecture.GetModel<PlayerBagModel>().RiskState;
            if (string.Equals(mapId, MapIds.Base, StringComparison.Ordinal) && risk == PlayerBagRiskState.BaseSafe)
            {
                error = null;
                return true;
            }

            var expedition = architecture.GetModel<ExpeditionModel>();
            var validPhase = expedition.Status == ExpeditionStatus.Playing ||
                             expedition.Status == ExpeditionStatus.ReturnQualified;
            if (risk == PlayerBagRiskState.InExpedition && validPhase &&
                string.Equals(mapId, expedition.CurrentMapId, StringComparison.Ordinal) &&
                architecture.GetModel<CombatModel>().PlayerHP.Value > 0)
            {
                error = null;
                return true;
            }

            error = "当前状态下库存不可用";
            return false;
        }

        public static bool CanOpenWarehouse(out string error)
        {
            var architecture = Game.Interface;
            if (!CanOpenPlayerBag(architecture, out error))
            {
                return false;
            }

            if (!string.Equals(architecture.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal) ||
                architecture.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.BaseSafe)
            {
                error = "仓库需要 基地安全状态";
                return false;
            }

            error = null;
            return true;
        }

        public static bool CanOpenSearchContainer(long containerId, out string error)
        {
            if (!CanOpenPlayerBag(out error))
            {
                return false;
            }

            IItemTransferPort transfer=Game.Interface.GetUtility<IItemTransferPort>();
            if(transfer.IsOnlineMode)
            {
                if(containerId>0&&Game.Interface.GetSystem<OnlineWorldInventorySystem>().TryGetOpenCorpse(out OnlineWorldCorpseContentsSnapshot corpse)&&corpse.Corpse.CorpseId==containerId){error=null;return true;}
                error="联机尸体会话未激活";return false;
            }
            var search = Game.Interface.GetSystem<SearchContainerSystem>();
            if (containerId <= 0 || containerId>int.MaxValue || !search.IsCurrentSession((int)containerId) || !search.TryGetSnapshot((int)containerId, out _))
            {
                error = "搜索容器会话未激活";
                return false;
            }

            error = null;
            return true;
        }
    }
}
