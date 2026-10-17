using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        private ArcadeSlayerChallengeConfiguration slayerChallengeConfig;

        /// <summary>界面只提交准备选择，先保存进入尝试再公开收币；跳过留待完整返回事务保存。</summary>
        public bool TrySelectSlayerEntry(bool enter,out string error)
        {
            error="Slayer当前不在准备阶段";var runtime=this.GetModel<ArcadeCombatModel>().Runtime;
            if(runtime?.SlayerChallenge?.Phase!=ArcadeSlayerChallengePhase.Preparing)return false;
            var next=ArcadeSlayerChallengeRules.SetEntryChoice(runtime.SlayerChallenge,slayerChallengeConfig,1,enter);
            if(enter&&!this.GetSystem<ArcadeRunSystem>().TryCommitSlayerEntry(runtime.SlayerChallenge,out error))return false;
            runtime.SlayerChallenge=next;runtime.ChallengeSeconds=(float)next.RemainingSeconds;
            if(enter)BeginSlayerCollectionWave(runtime);PublishHud(runtime);error=null;return true;
        }

        /// <summary>独立挑战只借正式墓园几何，保留原主线返回身份，不继承主题机关。</summary>
        private bool InitializeSlayerChallenge(ArcadeCombatRuntime runtime,ArcadeRunSaveData run)
        {
            if(!runtime.IsChallenge)return false;var provider=this.GetUtility<IRuntimeConfigProvider>();
            if(!provider.TryGetSlayerChallenge(out var c,out var error))
            {if(runtime.ChallengeId=="arcade.challenge.slayer-guardian")throw new InvalidOperationException(error);return false;}
            if(runtime.ChallengeId!=c.ChallengeId)return false;
            if(run.SlayerMartRulesVersion!=1||!run.IsCasualRoute)throw new InvalidOperationException("Slayer需要有效规则1休闲局");
            if(!provider.TryGetSlayerMart(out slayerMartConfig,out error))throw new InvalidOperationException(error);
            slayerChallengeConfig=c;arena=catalog.Arenas.Single(p=>p.ArenaNumber==c.ArenaConfigNumber);
            challenge=catalog.Challenges.Single(p=>p.Id==c.ChallengeId);
            runtime.SlayerChallenge=ArcadeSlayerChallengeRules.Create(c,run.RunId,new long[]{1},run.NextArenaNumber,run.ArenaConfigNumber,1);
            runtime.WaveIndex=0;runtime.ChallengeSeconds=c.PreparationSeconds;runtime.SpawnQueue.Clear();
            var layout=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==c.ArenaConfigNumber&&p.RoundNumber==c.LayoutRound);
            runtime.PlayerPosition=new Vector2(layout.SpawnX,layout.SpawnY);runtime.MoveDirection=Vector2.zero;runtime.Firing=false;
            view.Begin(arena.ArenaNumber,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,
                layout.Obstacles.Select(p=>new ArcadeObstacleDefinition(p.X,p.Y,p.HalfWidth,p.HalfHeight)).ToArray());
            view.SetIslandRound(0);view.SetRoundLayout(layout);return true;
        }

        /// <summary>每批只用配置规模，不依主线清空规则发奖励或提前成功。</summary>
        private void BeginSlayerCollectionWave(ArcadeCombatRuntime runtime)
        {
            group=catalog.EnemyGroups.Single(p=>p.Id==challenge.EnemyGroupId);runtime.SpawnQueue.Clear();
            int weight=group.Members.Sum(p=>p.Count),previous=0;
            foreach(var member in group.Members)
            {
                int count=ArcadeOpeningRules.AllocateMember(slayerChallengeConfig.CollectionWaveTotal,previous,member.Count,weight);
                for(int i=0;i<count;i++)runtime.SpawnQueue.Enqueue(member.EnemyId);previous+=member.Count;
            }
            runtime.SpawnCooldown=runtime.WaveTransitionRemaining=0;runtime.WaveClearAwarded=false;
        }

        /// <summary>返回是否可继续战斗；准备、资源等待和返回阶段冻结其余战斗与能力时钟。</summary>
        private bool TickSlayerChallenge(ArcadeCombatRuntime runtime,float seconds)
        {
            if(runtime.SlayerChallenge==null)return true;
            if(!view.IsRoundLayoutReady)return false;
            var previous=runtime.SlayerChallenge;
            runtime.SlayerChallenge=ArcadeSlayerChallengeRules.Advance(previous,slayerChallengeConfig,seconds,true);
            runtime.ChallengeSeconds=(float)runtime.SlayerChallenge.RemainingSeconds;
            if(previous.Phase!=runtime.SlayerChallenge.Phase&&runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Boss)
            {
                // 结束收币只清旧敌群与敌方攻击，不伪造击杀收益，也不删除尚未拾取的金币。
                foreach(var enemy in runtime.Enemies.Values){view.RemoveEnemy(enemy.Id);runtimePool.Return(enemy);}
                runtime.Enemies.Clear();runtime.SpawnQueue.Clear();
                foreach(int id in runtime.Projectiles.Where(p=>p.Value.Hostile).Select(p=>p.Key).ToArray())RemoveProjectile(runtime,id);
                runtime.ContentAreas.Clear();runtime.ContentAreaSnapshots.Clear();
            }
            bool combat=runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting||runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Boss;
            if(!combat){runtime.MoveDirection=Vector2.zero;runtime.Firing=false;}return combat;
        }

        /// <summary>独立容错不调用主线失命事务，死亡清理仍沿既有支援合同。</summary>
        private void DownSlayerPlayer(ArcadeCombatRuntime runtime)
        {
            ArcadeSlayerMartAbilityRules.OnDeath(SlayerMartAbility(runtime));ClearSlayerMartOrdinarySupports(runtime);
            runtime.PlayerHealth=0;runtime.MoveDirection=Vector2.zero;runtime.Firing=false;
            runtime.SlayerChallenge=ArcadeSlayerChallengeRules.RecordDown(runtime.SlayerChallenge,slayerChallengeConfig);
            runtime.RespawnRemaining=runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Returning?0:slayerChallengeConfig.RespawnSeconds;
        }
    }
}
