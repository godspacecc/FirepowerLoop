using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;
using QFramework;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        private bool pendingRegionPosition;
        private string treasureVisualIdentity;
        private readonly List<GameObject> treasureVisuals = new();
        /// <summary>物品表现专用的高位正 ID；与本场递增实体 ID 分离，不进入规则账本。</summary>
        private readonly List<int> treasureDropVisualIds = new();
        /// <summary>仅地面金币和宝石拥有渐隐材质；门与环境不参与物资寿命。</summary>
        private readonly List<ArcadeDropOpacity> treasureRewardMaterials = new();
        private float treasureRewardOpacity = 1f;

        /// <summary>只显示权威房间入口、出口及尚未领取物品；碰撞与奖励全部由领域处理。</summary>
        public void SetTreasureRoom(ArcadeTreasureRoomParameters p, bool inRoom, bool discovered, bool open, IReadOnlyList<int> remainingIds)
        {
            contentTreasure=p;contentInRoom=inRoom;contentDiscovered=discovered;contentOpen=open;
            contentRemaining=remainingIds?.ToArray()??Array.Empty<int>();
            if (root == null) return;
            SyncContentEnvironment();
            if(contentTemplates==null)return;
            string identity = p == null ? string.Empty : p.Id + ":" + inRoom + ":" + discovered + ":" + open + ":" + string.Join(",", remainingIds) + ":" + Loc.T("ui.arcade.treasure.return");
            if (identity == treasureVisualIdentity) return;
            ClearTreasureVisuals(); treasureVisualIdentity = identity;
            if (p == null) return;
            if (!inRoom)
            {
                if (discovered)
                {
                    var entryPosition = new Vector2(p.EntryX, p.EntryY);
                    if (islandRound > 0 && Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var islandCatalog, out _))
                        entryPosition = ArcadeIslandLayout.GetLandmarkPosition(new ArcadeIslandRegion(islandCatalog.Rule.ArenaHalfWidth,
                            islandCatalog.Rule.ArenaHalfHeight, islandRound), entryPosition, p.EntryRadius + islandCatalog.Rule.PlayerRadius);
                    var entrance=CreateContent("TreasureEntrance",new Vector3(entryPosition.x,0,entryPosition.y));
                    SetContentLabel(entrance,open?"ui.arcade.treasure.open":"ui.arcade.treasure.locked");
                    entrance.transform.Find("Chest").localRotation=Quaternion.Euler(0,open?35:0,0);
                    treasureVisuals.Add(entrance);
                    if(open){var gate=CreateContent("RouteGate",new Vector3(entryPosition.x,0,entryPosition.y));SetContentLabel(gate,"ui.arcade.treasure.open");treasureVisuals.Add(gate);}
                }
                return;
            }
            var exit=CreateContent("RouteGate",new Vector3(p.ExitX,0,p.ExitY));
            exit.name="TreasureReturn";SetContentLabel(exit,"ui.arcade.treasure.return");treasureVisuals.Add(exit);
            ArcadeConfigCatalogDefinition catalog = null;
            if (lootPresentation != null && !Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out catalog, out var configError))
                throw new InvalidOperationException(configError);
            foreach (var reward in p.Rewards)
            {
                if (!remainingIds.Contains(reward.Id)) continue;
                var kind = reward.Kind;
                var gold = kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.GoldBar;
                var gem = kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem;
                if (!gold && !gem && catalog != null)
                {
                    var definition = catalog.Drops.First(r => r.Kind == (kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Bomb ? global::ArcadeDropKind.Bomb : global::ArcadeDropKind.Dash));
                    int id = int.MaxValue - reward.Id;
                    if (!lootPresentation.TrySetDrop(id, mapRoot.TransformPoint(World(new Vector2(reward.X, reward.Y), .3f)),
                        ResolveDropDisplayName(definition), reward.Amount, definition.VisualPrefabAddress, out var error))
                        throw new InvalidOperationException(error);
                    treasureDropVisualIds.Add(id);
                    continue;
                }
                if(!gold&&!gem)throw new InvalidOperationException("宝藏道具正式掉落端口不可用");
                var item=CreateContent(gold?"GoldBar":"Gem",new Vector3(reward.X,.14f,reward.Y));
                item.name="TreasureReward_"+reward.Id;treasureVisuals.Add(item);
                treasureRewardMaterials.Add(new ArcadeDropOpacity(item));
            }
            SetTreasureRewardOpacity(treasureRewardOpacity);
        }

        /// <summary>模型与正式拾取端口同用权威透明度；异步模板就绪后的重绘保留已推进的淡出进度。</summary>
        public void SetTreasureRewardOpacity(float opacity)
        {
            treasureRewardOpacity = Mathf.Clamp01(opacity);
            foreach (var material in treasureRewardMaterials) material.SetOpacity(treasureRewardOpacity);
            foreach (int id in treasureDropVisualIds) lootPresentation?.SetDropOpacity(id, treasureRewardOpacity);
        }

        /// <summary>区域重建及地图退出配对释放本 Controller 创建的对象和材质。</summary>
        private void ClearTreasureVisuals()
        {
            foreach (var material in treasureRewardMaterials) material.Dispose();
            treasureRewardMaterials.Clear();
            foreach (int id in treasureDropVisualIds) lootPresentation?.RemoveDrop(id);
            treasureDropVisualIds.Clear();
            foreach (var marker in treasureVisuals) { if (marker != null) marker.SetActive(false); DestroyObject(marker); }
            treasureVisuals.Clear(); treasureVisualIdentity = null;
        }
    }
}
