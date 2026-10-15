using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>探索网格变更通知；只传地图身份，界面重新读取模型。</summary>
    public readonly struct MapExplorationChangedEvent
    {
        public MapExplorationChangedEvent(string mapId) { MapId = mapId; }
        public string MapId { get; }
    }

    /// <summary>根据当前地图本地玩家位置累积探索并标记存档；不依赖地图界面是否打开，不接受界面坐标。</summary>
    public sealed class MapExplorationSystem : AbstractSystem
    {
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();
        protected override void OnInit()
        {
            if (!this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<MapExplorationModel>(), out var error))
                throw new InvalidOperationException(error);
            subscriptions.Add(this.RegisterEvent<UnitAddedEvent>(e => Observe(e.Snapshot)));
            subscriptions.Add(this.RegisterEvent<UnitChangedEvent>(e => Observe(e.Current)));
            subscriptions.Add(this.RegisterEvent<MapLoadingCompletedEvent>(_ =>
            {
                if (this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId, out var unit)) Observe(unit);
            }));
        }
        protected override void OnDeinit()
        { foreach (var subscription in subscriptions) subscription.UnRegister(); subscriptions.Clear(); }

        /// <summary>只记录活动的本地玩家及当前地图实例；加载、远程单位、旧实例与非离线域均不产生探索。</summary>
        private void Observe(UnitSnapshot unit)
        {
            var map = this.GetModel<MapSceneModel>();
            var instanceId = this.GetSystem<MapSceneSystem>()?.CurrentMapInstanceId ?? 0;
            if (unit.UnitId != PlayerModel.LocalPlayerUnitId || unit.UnitType != UnitType.Player || !unit.IsActive ||
                instanceId <= 0 || unit.MapInstanceId != instanceId || map.Status.Value != MapLoadStatus.Loaded ||
                map.CurrentMode != MapPreparationMode.OfflineFull || this.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.None ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetMapDisplay(map.CurrentMapId, out var display, out _)) return;
            // 小地图保留逐步揭开的尺度，大地图半径上限为 24 米；不按帧重复写入同一格。
            var radius = Mathf.Clamp(Mathf.Min(display.Width, display.Height) * .06f, 3f, 24f);
            // 使用已提交场景的布局身份，不能依赖尚未安装或已经清理的远征模型。
            var layout = (map.CurrentHandle as GeneratedValleyContentHandle)?.Layout;
            var identity = layout == null ? string.Empty :
                $"{layout.GenerationVersion}:{layout.Seed}:{layout.AttemptIndex}";
            var exploration = this.GetModel<MapExplorationModel>();
            var layoutChanged = exploration.SynchronizeLayoutIdentity(display, identity);
            if (!exploration.Reveal(display, unit.Position, radius) && !layoutChanged) return;
            this.GetSystem<SaveSystem>().MarkDirty("map-exploration");
            this.SendEvent(new MapExplorationChangedEvent(map.CurrentMapId));
        }
    }
}
