using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct PlayerFlashlightChangedEvent
    {
        public PlayerFlashlightChangedEvent(bool isOn)
        {
            IsOn = isOn;
        }

        public bool IsOn { get; }
    }

    /// <summary>
    /// 维护当前本地玩家手电筒开关，并把鼠标或视口瞄准方向提交给玩家表现端口。
    /// 状态只属于当前玩家地图实例，不持久化也不参与联机同步。
    /// </summary>
    public sealed class PlayerFlashlightSystem : AbstractSystem
    {
        private IPlayerFlashlightPort port;
        private int playerUnitId;
        private long playerMapInstanceId;

        public bool IsOn { get; private set; }

        protected override void OnInit()
        {
            this.RegisterEvent<PlayerReadyEvent>(OnPlayerReady);
            this.RegisterEvent<UnitChangedEvent>(OnUnitChanged);
            this.RegisterEvent<UnitRemovedEvent>(OnUnitRemoved);
            this.RegisterEvent<MapLoadingStartedEvent>(_ => ClearPlayer());
            this.RegisterEvent<OnlineWorldChangedEvent>(OnOnlineWorldChanged);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            ClearPlayer();
        }

        public bool CanToggle(out string error)
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                this.GetModel<MapSceneModel>().Status.Value != MapLoadStatus.Loaded)
            {
                error = "手电筒只能在已加载的游戏地图中使用";
                return false;
            }

            if (port == null || !port.IsValid || playerUnitId <= 0 || playerMapInstanceId <= 0 ||
                !this.GetSystem<UnitSystem>().TryGet(playerUnitId, out var player) ||
                player.MapInstanceId != playerMapInstanceId || !player.IsActive)
            {
                error = "当前玩家手电筒不可用";
                return false;
            }

            if (!IsLocalPlayerAlive())
            {
                error = "玩家无法使用手电筒";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryToggle(out string error)
        {
            if (!CanToggle(out error))
            {
                return false;
            }

            IsOn = !IsOn;
            port.SetActive(IsOn);
            this.SendEvent(new PlayerFlashlightChangedEvent(IsOn));
            return true;
        }

        private void OnPlayerReady(PlayerReadyEvent evt)
        {
            if (evt.UnitId == playerUnitId && evt.MapInstanceId == playerMapInstanceId && port?.IsValid == true)
            {
                port.SetActive(IsOn);
                return;
            }

            ClearPlayer();
            if (!this.GetUtility<IPlayerRuntimePortProvider>()
                    .TryGetFlashlightPort(evt.UnitId, evt.MapInstanceId, out var nextPort))
            {
                return;
            }

            playerUnitId = evt.UnitId;
            playerMapInstanceId = evt.MapInstanceId;
            port = nextPort;
            port.SetActive(false);
        }

        private void OnUnitChanged(UnitChangedEvent evt)
        {
            if (evt.Current.UnitId == playerUnitId &&
                evt.Current.MapInstanceId == playerMapInstanceId &&
                (evt.ChangeMask & UnitChangeMask.Active) != 0 &&
                !evt.Current.IsActive)
            {
                ClearPlayer();
            }
        }

        private void OnUnitRemoved(UnitRemovedEvent evt)
        {
            if (evt.Snapshot.UnitId == playerUnitId && evt.Snapshot.MapInstanceId == playerMapInstanceId)
            {
                ClearPlayer();
            }
        }

        private void OnOnlineWorldChanged(OnlineWorldChangedEvent evt)
        {
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly ||
                evt.Snapshot == null || !evt.Snapshot.HasWorld)
            {
                return;
            }

            var self = evt.Snapshot.Players.FirstOrDefault(item => item.UnitId == evt.Snapshot.SelfUnitId);
            if (self.UnitId == 0 || self.IsDead)
            {
                TurnOff();
            }
        }

        private void OnUpdate()
        {
            if (!IsOn || port?.IsValid != true)
            {
                return;
            }

            if (this.GetSystem<PlayerInputSystem>().TryGetCurrentFacingDirection(out var direction))
            {
                port.SetWorldDirection(direction);
            }
        }

        private bool IsLocalPlayerAlive()
        {
            if (this.GetModel<MapSceneModel>().CurrentMode != MapPreparationMode.OnlinePlayersOnly)
            {
                return this.GetModel<CombatModel>().PlayerHP.Value > 0;
            }

            var world = this.GetModel<OnlineWorldModel>().Snapshot.Value;
            if (world == null || !world.HasWorld || world.ConnectionLost)
            {
                return false;
            }

            var self = world.Players.FirstOrDefault(item => item.UnitId == world.SelfUnitId);
            return self.UnitId != 0 && !self.IsDead;
        }

        private void TurnOff()
        {
            var changed = IsOn;
            IsOn = false;
            if (port?.IsValid == true)
            {
                port.SetActive(false);
            }

            if (changed)
            {
                this.SendEvent(new PlayerFlashlightChangedEvent(false));
            }
        }

        private void ClearPlayer()
        {
            TurnOff();
            port = null;
            playerUnitId = 0;
            playerMapInstanceId = 0;
        }
    }
}
