using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>活动权威帧处理房间时钟及空间接触；只为休闲起始场构建一次实例，不新增存档点。</summary>
        private void TickTreasureRoom(ArcadeCombatRuntime runtime, float seconds)
        {
            if (!runtime.TreasureInitialized)
            {
                runtime.TreasureInitialized = true;
                var run = this.GetModel<ArcadeRunModel>().Current;
                var p = !runtime.IsChallenge && run?.IsCasualRoute == true && run.NextArenaNumber == 1
                    ? catalog.TreasureRooms.FirstOrDefault(r => r.ArenaConfigNumber == arena.ArenaNumber) : null;
                if (p != null) runtime.TreasureRoom = new ArcadeTreasureRoomState(
                    run.RunId + ":" + runtime.ArenaNumber + ":" + p.WaveNumber + ":" + p.Id, p);
            }
            var room = runtime.TreasureRoom;
            if (room == null) return;
            var rules = room.Parameters;
            if (room.Advance(seconds).Contains(1L)) ReturnFromTreasure(runtime);
            if (room.Expired || runtime.TreasureCompleted || runtime.RespawnRemaining > 0) return;
            if (runtime.InTreasureRoom)
            {
                if ((runtime.PlayerPosition - new Vector2(rules.ExitX, rules.ExitY)).sqrMagnitude <=
                    rules.ExitRadius * rules.ExitRadius)
                {
                    if (room.TryLeave(room.InstanceId, 1, out _)) ReturnFromTreasure(runtime);
                    return;
                }
                foreach (var reward in room.RemainingRewards)
                {
                    if ((runtime.PlayerPosition - new Vector2(reward.X, reward.Y)).sqrMagnitude >
                        rules.PickupRadius * rules.PickupRadius) continue;
                    long scoreBefore = this.GetModel<ArcadeRunModel>().Current?.Score ?? 0;
                    int bombsBefore = runtime.Bombs, dashesBefore = runtime.Dashes;
                    if (!room.TryCollect(room.InstanceId, 1, reward.Id, r => ApplyTreasureReward(runtime, r), out _)) continue;
                    // 领取账本已移除物品后才通知表现，反馈失败不能让同一宝藏重复发奖。
                    if (runtime.Bombs > bombsBefore || runtime.Dashes > dashesBefore)
                    {
                        bool bomb = runtime.Bombs > bombsBefore;
                        var drop = catalog.Drops.First(d => d.Kind == (bomb ? ArcadeDropKind.Bomb : ArcadeDropKind.Dash));
                        view.PlaySupplyPickup(drop, bomb ? runtime.Bombs - bombsBefore : runtime.Dashes - dashesBefore);
                    }
                    view.PlayTreasurePickup((this.GetModel<ArcadeRunModel>().Current?.Score ?? scoreBefore) - scoreBefore, 0);
                }
                return;
            }
            var entry = new Vector2(rules.EntryX, rules.EntryY);
            if (UsesIslandRounds(runtime)) entry = ArcadeIslandLayout.GetLandmarkPosition(IslandFor(runtime),
                entry, rules.EntryRadius + catalog.Rule.PlayerRadius);
            bool near = (runtime.PlayerPosition - entry).sqrMagnitude <=
                rules.EntryRadius * rules.EntryRadius;
            if (runtime.WaveIndex + 1 == rules.WaveNumber)
            {
                if (near) runtime.TreasureDiscovered = true;
                if (runtime.Enemies.Count == 0 && runtime.SpawnQueue.Count == 0)
                { runtime.TreasureCleared = true; runtime.TreasureDiscovered = true; }
            }
            if ((near || runtime.TreasureCleared) && room.TryEnter(room.InstanceId, 1, runtime.TreasureDiscovered, runtime.TreasureCleared, out _))
            {
                // 场外弹丸不能随领取者进入新区域继续命中，逐个归还现有运行对象池。
                foreach (var projectile in runtime.Projectiles.Values.Where(p => !p.Hostile).ToArray())
                    RemoveProjectile(runtime, projectile.Id);
                if (UsesIslandRounds(runtime)) SuspendIslandFirstPerson(runtime); else EndFirstPerson(runtime);
                runtime.InTreasureRoom = true;
                runtime.IslandRoomTransitionPending = UsesIslandRounds(runtime);
                runtime.PlayerPosition = new Vector2(rules.SpawnX, rules.SpawnY);
            }
        }

        private void ReturnFromTreasure(ArcadeCombatRuntime runtime)
        {
            runtime.InTreasureRoom = false;
            runtime.TreasureCompleted = true;
            if (UsesIslandRounds(runtime) && runtime.WaveIndex == 2)
            { StartIslandRound(runtime, 3); return; }
            runtime.PlayerPosition = IslandPosition(runtime,
                new Vector2(runtime.TreasureRoom.Parameters.ReturnX, runtime.TreasureRoom.Parameters.ReturnY), catalog.Rule.PlayerRadius);
            if (runtime.WaveIndex == 2) BeginWave(runtime, 3);
        }

        /// <summary>先算完整候选，领取者资源可接收才提交；满额补给保留，不转换现金或预存。</summary>
        private bool ApplyTreasureReward(ArcadeCombatRuntime runtime, ArcadeTreasureRewardParameters reward)
        {
            var model = this.GetModel<ArcadeRunModel>();
            var run = model.Current;
            if (run == null || model.Phase != ArcadeRunPhase.Arena) return false;
            if (reward.Kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Bomb ||
                reward.Kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Dash)
            {
                bool bomb = reward.Kind == FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Bomb;
                var drop = catalog.Drops.FirstOrDefault(r => r.Kind == (bomb ? ArcadeDropKind.Bomb : ArcadeDropKind.Dash));
                int held = bomb ? runtime.Bombs : runtime.Dashes;
                if (drop.MaxOwned <= 0 || held >= drop.MaxOwned) return false;
                int added = Math.Min(reward.Amount, drop.MaxOwned - held);
                if (added <= 0) return false;
                if (bomb) runtime.Bombs += added; else runtime.Dashes += added;
                return true;
            }
            var before = new ArcadeTreasureScoreState(run.Score, run.ScoreMultiplier, run.MultiplierProgress, run.RemainingLives);
            if (!ArcadeTreasureScoreRules.TryAward(before, reward, runtime.TreasureRoom.Parameters, out var next)) return false;
            model.SetTransientTreasureScore(next);
            return true;
        }

        private void ResetTreasureMultiplierAfterDeath()
        {
            var model = this.GetModel<ArcadeRunModel>();
            var run = model.Current;
            if (run != null) model.SetTransientTreasureScore(
                new ArcadeTreasureScoreState(run.Score, run.ScoreMultiplier, run.MultiplierProgress, run.RemainingLives).ResetAfterDeath());
        }

        /// <summary>区域变化重建本机空间表现，规则现场保持原样；进入不卸载整队地图。</summary>
        private void SyncTreasureView(ArcadeCombatRuntime runtime)
        {
            try { SyncTreasureViewCore(runtime); }
            catch (Exception exception)
            {
                if (!UsesIslandRounds(runtime)) throw;
                runtime.IslandRoomTransitionPending = true;
                runtime.PresentedRegion = 0;
                FailIslandTransition(runtime, exception.Message);
            }
        }

        private void SyncTreasureViewCore(ArcadeCombatRuntime runtime)
        {
            int region = runtime.InTreasureRoom ? 1 : 0;
            if (runtime.PresentedRegion != region)
            {
                var p = runtime.TreasureRoom.Parameters;
                view.Begin(arena.ArenaNumber, region == 1 ? p.HalfWidth : catalog.Rule.ArenaHalfWidth,
                    region == 1 ? p.HalfHeight : catalog.Rule.ArenaHalfHeight,
                    region == 1 ? Array.Empty<ArcadeObstacleDefinition>() : ObstaclesFor(runtime));
                view.SetIslandRound(UsesIslandRounds(runtime) && region == 0 ? runtime.WaveIndex + 1 : 0);
                runtime.PresentedRegion = region;
            }
            var room = runtime.TreasureRoom;
            bool completedIslandTreasure = UsesIslandRounds(runtime) && runtime.TreasureCompleted;
            view.SetTreasureRoom(room?.Parameters, runtime.InTreasureRoom, runtime.TreasureDiscovered && !completedIslandTreasure,
                runtime.TreasureCleared && !completedIslandTreasure && room?.Expired == false,
                room?.RemainingRewards.Select(r => r.Id).ToArray() ?? Array.Empty<int>());
            view.SetTreasureRewardOpacity(GroundDropOpacity((float)(room?.RewardRemainingSeconds ?? 30)));
        }
    }
}
