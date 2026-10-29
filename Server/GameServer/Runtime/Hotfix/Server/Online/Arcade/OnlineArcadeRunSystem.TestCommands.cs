using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    public static partial class OnlineArcadeRunManagerSystem
    {
        /// <summary>恢复正常内容时仅提升内存发布版本，避免客户端拒绝旧节点；不修改磁盘正常节点。</summary>
        public static bool RestoreNormalAfterTestGoto(OnlineArcadeRunActor actor)
        {
            if (actor.NormalRecordBeforeTestGoto == null) return false;
            var normal = Clone(actor.NormalRecordBeforeTestGoto);
            normal.Revision = checked(Math.Max(normal.Revision, actor.Record.Revision) + 1);
            actor.NormalRecordBeforeTestGoto = null;
            actor.BattleGeneration++; actor.Battle = null;
            CommitActorRecord(actor, normal);
            return true;
        }
        /// <summary>先准备完整候选现场，再替换权威记录；临时局保留原已提交记录供恢复和其他局写盘。</summary>
        private static string GotoForTests(this OnlineArcadeRunManagerComponent self, OnlineArcadeRunActor actor, int ordinal, int round)
        {
            var prepared = PrepareGotoForTests(actor, ordinal, round, out var candidate);
            actor.NormalRecordBeforeTestGoto ??= Clone(actor.CommittedRecord);
            actor.BattleGeneration++; actor.Battle = null;
            CommitActorRecord(actor, candidate);
            actor.StartBattle(prepared);
            self.StartBattleTick(actor, actor.BattleGeneration);
            return $"已进入临时测试局：第{ordinal}关第{round}轮；正常节点保留，结束测试局后恢复";
        }
        /// <summary>仅准备候选，不改actor或文件；供权威入口在全部校验成功后提交。</summary>
        public static OnlineArcadeBattleRuntime PrepareGotoForTests(OnlineArcadeRunActor actor, int ordinal, int round, out OnlineArcadeRunRecord candidate)
        {
            candidate = null;
            var source = actor.Record;
            if (!source.IsCasualRoute || actor.Battle.IsChallenge || ordinal < 1 || ordinal > FirepowerLoop.Arcade.ArcadeRouteRules.ArenaCount(source.CasualRouteVersion))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "目标不在本局正常路线内");
            int target = source.ArenaConfigNumber;
            if (ordinal != source.NextArena)
            {
                var reachable = new HashSet<int> { 1 };
                for (int step = 1; step < ordinal; step++)
                    reachable = reachable.SelectMany(id => FirepowerLoop.Arcade.ArcadeRouteRules.NextArenas(source.CasualRouteVersion, id,
                        ServerConfig.Instance.Tables.TbArcadeArena.Get(id).NextArenaNumbers)).ToHashSet();
                if (reachable.Count != 1) throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "旧路线分支无法唯一确定目标");
                target = reachable.Single();
            }
            var arena = ServerConfig.Instance.Tables.TbArcadeArena.GetOrDefault(target);
            if (arena == null || round < 1 || round > 4 || round > arena.WaveGroupIds.Length)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "目标场地或轮次不存在");
            candidate = Clone(source);
            candidate.NextArena = ordinal; candidate.CompletedArena = ordinal - 1; candidate.ArenaConfigNumber = target;
            candidate.RoundNumber = round; candidate.RoundLayoutId = string.Empty; candidate.RoundLayoutVersion = 0;
            candidate.RoundBossEncounter = false; candidate.DestroyedCrateIds.Clear(); candidate.CrateHealth.Clear(); candidate.DestroyedBarrelIds.Clear();
            candidate.ChallengeId = string.Empty; candidate.ChallengeAttempted = false;
            candidate.Phase = OnlineArcadeRunPhase.SafeCheckpoint; candidate.Revision++;
            foreach (var player in candidate.Players) { player.ScoreShop = null; player.Ready = true; player.ChallengeReady = false; }
            var prepared = OnlineArcadeRunActorSystem.PrepareBattle(candidate, false, false);
            // 客户端按现场代次决定重载；不能从旧安全节点重用正在显示的地图身份。
            prepared.RoundGeneration = checked(Math.Max(prepared.RoundGeneration, actor.Battle.RoundGeneration) + 1);
            candidate.RoundGeneration = prepared.RoundGeneration;
            return prepared;
        }
    }

    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>只对发起房主的当前现场生效，新建现场自然清除。</summary>
        public static string SetInvincibleForTests(this OnlineArcadeRunActor self, OnlineArcadeBattlePlayerRuntime player, bool enabled)
        { player.TestInvincible = enabled; return enabled ? "房主当前现场无敌已开启" : "房主当前现场无敌已关闭"; }

        /// <summary>指定敌人沿用正式出生表现，不消费原轮队列与出生预算。</summary>
        public static string SpawnEnemiesForTests(this OnlineArcadeRunActor self, string enemyId, int count)
        {
            var battle = self.Battle;
            if (count < 1 || count > 20 || battle.IsChallenge || battle.IsFinalBoss || battle.RoundBossEncounter || battle.RoundLayout?.SpecialKind == FirepowerLoop.Arcade.ArcadeNextStageEnemyRules.GraveKind)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "当前现场不支持该刷怪请求");
            if (ServerConfig.Instance.Tables.TbEnemy.GetOrDefault(enemyId) == null)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "敌人配置不存在");
            if (battle.Enemies.Count + count > battle.MaxAlive)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "超过当前敌人存活上限");
            int spawned = battle.SpawnedInWave; long nextSpawn = battle.NextSpawnAt;
            try
            {
                for (int index = 0; index < count; index++)
                    if (!SpawnQueuedEnemy(battle, TimeInfo.Instance.ServerNow(), -1, enemyId))
                        throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "当前出生条件不支持指定敌人");
            }
            finally { battle.SpawnedInWave = spawned; battle.NextSpawnAt = nextSpawn; }
            return $"已生成{count}个{enemyId}";
        }
    }
}
