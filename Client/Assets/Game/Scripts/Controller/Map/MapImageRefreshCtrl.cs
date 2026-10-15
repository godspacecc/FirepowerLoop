using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>管理当前离线地图的局部图片记忆与限频拍摄；不决定探索、建筑、采集或存档规则。</summary>
    public sealed class MapImageRefreshCtrl : IController, IUtility, IDisposable
    {
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();
        private MapImageMemory memory;
        private MapDisplayDefinition display;
        /// <summary>纹理只属于一个地图实例；隐藏地图分页保留，地图卸载或实例更换立即释放。</summary>
        private long mapInstanceId;
        private float nextRefresh;
        private bool initialized;
        private bool failed;
        public IArchitecture GetArchitecture() => Game.Interface;

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            LifeCycle.RegisterLateUpdate(Refresh);
            subscriptions.Add(this.RegisterEvent<MapCurrentReleasedEvent>(e => { if (e.MapInstanceId == mapInstanceId) Clear(); }));
        }

        /// <summary>仅当前离线实例可生成记忆，其他地图仍查看资料图；UI 拖动位置不参与拍摄。</summary>
        public Texture2D ResolveTexture(string mapId, Texture2D source)
        {
            if (source == null || !TryGetPlayer(mapId, out var player, out var context)) return source;
            if (mapInstanceId != 0 && mapInstanceId != player.MapInstanceId) Clear();
            if (memory == null && !failed && this.GetUtility<IRuntimeConfigProvider>().TryGetMapDisplay(mapId, out display, out _))
            {
                try
                {
                    mapInstanceId = player.MapInstanceId;
                    memory = MapNearbyCapture.CreateMemory(display, source);
                    nextRefresh = 0;
                }
                catch (Exception e) { failed = true; Debug.LogWarning("[MapImageRefresh] 地图缓存创建失败，保留资料图：" + e.Message); }
            }
            return memory?.Texture ?? source;
        }

        /// <summary>地图曾打开后每两秒最多拍摄一次玩家附近；关闭分页不遗忘，远处不拍摄。</summary>
        private void Refresh()
        {
            if (memory == null) return;
            if (!TryGetPlayer(display.MapId, out var player, out var context) || player.MapInstanceId != mapInstanceId)
            { Clear(); return; }
            // 暂停菜单暂时隐藏地图内容不等于卸载，不应丢失已经观察到的图片。
            if (failed || !context.MapContentRoot.activeInHierarchy || Time.timeScale == 0 || Time.realtimeSinceStartup < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartup + 2f;
            var radius = Mathf.Clamp(Mathf.Min(display.Width, display.Height) * .06f, 3f, 24f);
            var region = MapNearbyCapture.GetRegion(display, player.Position, radius, memory.Texture.width);
            if (region.width <= 0 || region.height <= 0) return;
            try
            {
                var patch = MapNearbyCapture.Capture(context, display, region, memory.Texture.width, player.Position.y);
                memory.ApplyNearbyPatch(region, patch, player.Position, radius);
            }
            catch (Exception e)
            {
                // 本次实例停止重试，避免每两秒输出异常；已提交的历史画面保持可用。
                failed = true; Debug.LogWarning("[MapImageRefresh] 局部拍摄失败，保留旧图：" + e.Message);
            }
        }

        private bool TryGetPlayer(string mapId, out UnitSnapshot player, out MapContext context)
        {
            player = default; context = null;
            var map = this.GetModel<MapSceneModel>();
            if (map.Status.Value != MapLoadStatus.Loaded || map.CurrentMode != MapPreparationMode.OfflineFull ||
                map.CurrentMapId != mapId || map.CurrentHandle == null || !map.CurrentHandle.IsValid) return false;
            context = map.CurrentHandle.Context;
            return context != null && context.MapContentRoot != null &&
                this.GetModel<UnitModel>().TryGet(PlayerModel.LocalPlayerUnitId, out player) && player.IsActive &&
                player.UnitType == UnitType.Player && player.MapInstanceId > 0 &&
                player.MapInstanceId == this.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
        }

        private void Clear()
        { memory?.Dispose(); memory = null; mapInstanceId = 0; nextRefresh = 0; failed = false; }

        public void Dispose()
        {
            if (initialized) LifeCycle.UnregisterLateUpdate(Refresh);
            initialized = false;
            foreach (var subscription in subscriptions) subscription.UnRegister();
            subscriptions.Clear(); Clear();
        }
    }
}
