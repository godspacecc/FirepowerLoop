using System;
using System.Linq;
using AnotherDay.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>权威帧推进共享时钟及个人接触，现场不持久化；领取只修改当前工作记录。</summary>
        private static bool TickTreasureRoom(this OnlineArcadeRunActor actor, OnlineArcadeBattleRuntime battle, float seconds)
        {
            bool changed = false;
            if (!battle.TreasureInitialized)
            {
                battle.TreasureInitialized = true;
                var config = !battle.IsChallenge && battle.IsCasualRoute && battle.ArenaNumber == 1
                    ? ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.FirstOrDefault(r => r.ArenaConfigNumber == battle.ArenaConfigNumber) : null;
                if (config != null)
                {
                    var rules = ArcadeTreasureRoomParameters.FromConfig(config);
                    battle.TreasureRoom = new ArcadeTreasureRoomState(actor.Record.RunId + ":" + actor.BattleGeneration + ":" + rules.Id, rules);
                    changed = true;
                }
            }
            var room = battle.TreasureRoom;
            if (room == null) return changed;
            var p = room.Parameters;
            double before = room.RemainingSeconds;
            double rewardBefore = room.RewardRemainingSeconds;
            int rewardCountBefore = room.RemainingRewards.Length;
            foreach (long id in room.Advance(seconds))
                if (battle.Players.TryGetValue(id, out var member)) ReturnFromTreasure(member, p);
            changed |= before != room.RemainingSeconds;
            changed |= rewardBefore != room.RewardRemainingSeconds || rewardCountBefore != room.RemainingRewards.Length;
            if (battle.TreasureCompleted) return changed;
            if (room.Expired)
            {
                battle.TreasureCompleted = true;
                if (battle.WaveIndex == 2) PrepareWave(battle, ResolveBattleArena(battle), 3);
                return true;
            }
            if (battle.WaveIndex + 1 == p.WaveNumber && battle.SpawnQueueIndex >= battle.SpawnQueue.Count && battle.Enemies.Count == 0 && !battle.TreasureCleared)
            { battle.TreasureCleared = true; battle.TreasureDiscovered = true; changed = true; }
            foreach (var player in battle.Players.Values.OrderBy(r => r.PlayerId))
            {
                if (!player.Alive) continue;
                if (player.InTreasureRoom)
                {
                    if (DistanceSquared(player.PositionX, player.PositionZ, p.ExitX, p.ExitY) <= p.ExitRadius * p.ExitRadius)
                    {
                        if (room.TryLeave(room.InstanceId, player.PlayerId, out _))
                        { ReturnFromTreasure(player, p); changed = true; }
                        continue;
                    }
                    foreach (var reward in room.RemainingRewards)
                        if (DistanceSquared(player.PositionX, player.PositionZ, reward.X, reward.Y) <= p.PickupRadius * p.PickupRadius)
                            changed |= room.TryCollect(room.InstanceId, player.PlayerId, reward.Id,
                                r => ApplyTreasureReward(actor, player.PlayerId, r, p), out _);
                    continue;
                }
                bool near = DistanceSquared(player.PositionX, player.PositionZ, p.EntryX, p.EntryY) <= p.EntryRadius * p.EntryRadius;
                if (near && battle.WaveIndex + 1 == p.WaveNumber && !battle.TreasureDiscovered)
                { battle.TreasureDiscovered = true; changed = true; }
                if (!player.TreasureVisited && (near || battle.TreasureCleared) &&
                    room.TryEnter(room.InstanceId, player.PlayerId, battle.TreasureDiscovered, battle.TreasureCleared, out _))
                {
                    // 弹丸没有独立区域身份，进入时清除本人场外弹丸，禁止房内继续造成场外伤害。
                    foreach (long id in battle.Projectiles.Where(pair => pair.Value.OwnerPlayerId == player.PlayerId).Select(pair => pair.Key).ToArray())
                        battle.Projectiles.Remove(id);
                    player.TreasureVisited = true; player.InTreasureRoom = true; player.PositionX = p.SpawnX; player.PositionZ = p.SpawnY;
                    player.MoveX = player.MoveZ = 0; player.Firing = false; changed = true;
                }
            }
            if (room.Started && room.Members.Length == 0 && battle.Players.Values.Where(p => p.Alive).All(p => p.TreasureVisited))
            {
                battle.TreasureCompleted = true;
                if (battle.WaveIndex == 2) PrepareWave(battle, ResolveBattleArena(battle), 3);
                changed = true;
            }
            return changed;
        }

        /// <summary>个人返回竞技场当前轮次，重置当帧移动输入以避免跨区域携带位移。</summary>
        private static void ReturnFromTreasure(OnlineArcadeBattlePlayerRuntime player, ArcadeTreasureRoomParameters p)
        {
            player.InTreasureRoom = false; player.PositionX = p.ReturnX; player.PositionZ = p.ReturnY;
            player.HazardPreviousX = p.ReturnX; player.HazardPreviousZ = p.ReturnY;
            player.MoveX = player.MoveZ = 0; player.Firing = false;
        }

        /// <summary>完整候选成功才写入个人工作态；溢出或满额返回失败，规则账本保留物品。</summary>
        private static bool ApplyTreasureReward(OnlineArcadeRunActor actor, long playerId, ArcadeTreasureRewardParameters reward,
            ArcadeTreasureRoomParameters rules, bool recordFeedback = true)
        {
            var record = actor.Record.Players.First(r => r.PlayerId == playerId);
            if (reward.Kind == AnotherDay.Arcade.ArcadeTreasureRewardKind.Bomb || reward.Kind == AnotherDay.Arcade.ArcadeTreasureRewardKind.Dash)
            {
                bool bomb = reward.Kind == AnotherDay.Arcade.ArcadeTreasureRewardKind.Bomb;
                var drop = ServerConfig.Instance.Tables.TbArcadeDrop.DataList.First(r => r.Kind == (bomb ? ArcadeDropKind.Bomb : ArcadeDropKind.Dash));
                int held = bomb ? record.Bombs : record.Dashes;
                if (drop.MaxOwned <= 0 || held >= drop.MaxOwned) return false;
                int added = Math.Min(reward.Amount, drop.MaxOwned - held);
                if (added <= 0) return false;
                if (bomb) record.Bombs += added; else record.Dashes += added;
                if (recordFeedback) RecordTreasurePickup(actor, playerId, drop.Id, added, 0);
                return true;
            }
            if (!ArcadeTreasureScoreRules.TryAward(new ArcadeTreasureScoreState(record.Score, record.ScoreMultiplier, record.MultiplierProgress, record.Lives), reward, rules, out var next)) return false;
            long scoreAdded = next.Score - record.Score;
            record.Score = next.Score; record.ScoreMultiplier = next.Multiplier; record.MultiplierProgress = next.Progress; record.Lives = next.Lives;
            if (recordFeedback && scoreAdded > 0) RecordTreasurePickup(actor, playerId, string.Empty, 0, scoreAdded);
            return true;
        }

        /// <summary>宝藏房与普通掉落使用同一地图内递增身份，重连基线不会重放旧奖励。</summary>
        private static void RecordTreasurePickup(OnlineArcadeRunActor actor, long playerId, string dropId, int added, long score)
        {
            var battle = actor.Battle;
            if (battle == null) return;
            battle.SupplyPickups.Add(new OnlineArcadeDropRuntime
            {
                RuntimeId = battle.NextDropRuntimeId++, DropConfigId = dropId,
                CollectedByPlayerId = playerId, AddedAmount = added, ScoreAdded = score,
            });
        }
    }
}
