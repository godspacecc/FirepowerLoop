using System;
using System.Linq;
using AnotherDay.Arcade;
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
                    room.TryCollect(room.InstanceId, 1, reward.Id, r => ApplyTreasureReward(runtime, r), out _);
                }
                return;
            }
            bool near = (runtime.PlayerPosition - new Vector2(rules.EntryX, rules.EntryY)).sqrMagnitude <=
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
                EndFirstPerson(runtime);
                runtime.InTreasureRoom = true;
                runtime.PlayerPosition = new Vector2(rules.SpawnX, rules.SpawnY);
            }
        }

        private void ReturnFromTreasure(ArcadeCombatRuntime runtime)
        {
            runtime.InTreasureRoom = false;
            runtime.TreasureCompleted = true;
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
            if (reward.Kind == AnotherDay.Arcade.ArcadeTreasureRewardKind.Bomb ||
                reward.Kind == AnotherDay.Arcade.ArcadeTreasureRewardKind.Dash)
            {
                bool bomb = reward.Kind == AnotherDay.Arcade.ArcadeTreasureRewardKind.Bomb;
                var drop = catalog.Drops.FirstOrDefault(r => r.Kind == (bomb ? ArcadeDropKind.Bomb : ArcadeDropKind.Dash));
                int held = bomb ? runtime.Bombs : runtime.Dashes;
                if (drop.MaxOwned <= 0 || held >= drop.MaxOwned) return false;
                int added = Math.Min(reward.Amount, drop.MaxOwned - held);
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
            int region = runtime.InTreasureRoom ? 1 : 0;
            if (runtime.PresentedRegion != region)
            {
                var p = runtime.TreasureRoom.Parameters;
                view.Begin(arena.ArenaNumber, region == 1 ? p.HalfWidth : catalog.Rule.ArenaHalfWidth,
                    region == 1 ? p.HalfHeight : catalog.Rule.ArenaHalfHeight,
                    region == 1 ? Array.Empty<ArcadeObstacleDefinition>() : arena.Obstacles);
                runtime.PresentedRegion = region;
            }
            var room = runtime.TreasureRoom;
            view.SetTreasureRoom(room?.Parameters, runtime.InTreasureRoom, runtime.TreasureDiscovered,
                runtime.TreasureCleared && room?.Expired == false, room?.RemainingRewards.Select(r => r.Id).ToArray() ?? Array.Empty<int>());
        }
    }
}
