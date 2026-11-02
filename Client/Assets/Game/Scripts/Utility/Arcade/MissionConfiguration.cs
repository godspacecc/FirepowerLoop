using System;
using System.Linq;
using UnityEngine;
namespace Game
{
    /// <summary>来自任务权威表的不可变参数；坐标为相对开局点偏移。</summary>
    public sealed class MissionConfiguration
    {
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int SupplyCount {get;}
        /// <summary>各遭遇按成功出生序号循环选择已有敌池行。</summary>
        private readonly string[][] rosters;
        /// <summary>只在有待生成额度时查询，失败重试不改变敌人组合。</summary>
        public string RosterId(MissionRunState run)
        {
            int index=run.Phase==MissionPhase.Entry?0:run.Phase==MissionPhase.Search?1+run.SearchIndex:run.Phase==MissionPhase.Defend?4:4+run.BreakthroughWave;
            var sequence=rosters[index];return sequence[run.EncounterSpawned%sequence.Length];
        }
        /// <summary>各阶段有限生成额度、设施时长与空间目标来自权威表。</summary>
        public int EntryCount {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int[] SearchCounts => (int[])searchCounts.Clone();
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private readonly int[] searchCounts;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int DefendCount {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int BreakthroughWaves {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int BreakthroughCount {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int AliveCap {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public float SpawnInterval {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public float ActivationSeconds {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public float DefendSeconds {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public float FacilityRadius {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public Vector2 Facility {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public Vector2 Breakthrough {get;}

        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int AmbushCount {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public float InteractionRadius {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private readonly Vector2[] supplies;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public Vector2 Exit {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public Vector2 Supply(int index)=>supplies[index];
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public MissionConfiguration(global::Tables tables)
        {
            var row=tables.TbMissionRule.DataList.Single();
            rosters=new[]{row.EntryRoster,row.Search1Roster,row.Search2Roster,row.Search3Roster,row.DefendRoster,row.Breakthrough1Roster,row.Breakthrough2Roster,row.Breakthrough3Roster}.Select(v=>(v??string.Empty).Split(';')).ToArray();
            if(row.BreakthroughWaves!=3||rosters.Any(sequence=>sequence.Any(id=>string.IsNullOrWhiteSpace(id)||tables.TbSurvivorEnemy.GetOrDefault(id)==null)))throw new InvalidOperationException("任务敌人组合无效");
            if(row.SupplyCount!=3||row.AmbushCount<=0||row.InteractionRadius<=0||float.IsNaN(row.InteractionRadius)||float.IsInfinity(row.InteractionRadius))throw new InvalidOperationException("任务配置无效");
            SupplyCount=row.SupplyCount;AmbushCount=row.AmbushCount;InteractionRadius=row.InteractionRadius;
            EntryCount=row.EntryCount;searchCounts=new[]{row.Search1Count,row.Search2Count,row.Search3Count};DefendCount=row.DefendCount;BreakthroughWaves=row.BreakthroughWaves;BreakthroughCount=row.BreakthroughCount;AliveCap=row.AliveCap;
            SpawnInterval=row.SpawnInterval;ActivationSeconds=row.ActivationSeconds;DefendSeconds=row.DefendSeconds;FacilityRadius=row.FacilityRadius;
            Facility=new Vector2(row.FacilityX,row.FacilityY);Breakthrough=new Vector2(row.BreakthroughX,row.BreakthroughY);
            if(EntryCount<=0||searchCounts.Any(n=>n<=0)||DefendCount<=0||BreakthroughWaves<=0||BreakthroughCount<=0||AliveCap<=0||new[]{SpawnInterval,ActivationSeconds,DefendSeconds,FacilityRadius}.Any(v=>v<=0||float.IsNaN(v)||float.IsInfinity(v)))throw new InvalidOperationException("任务阶段配置无效");
            supplies=new[]{new Vector2(row.Supply1X,row.Supply1Y),new Vector2(row.Supply2X,row.Supply2Y),new Vector2(row.Supply3X,row.Supply3Y)};Exit=new Vector2(row.ExitX,row.ExitY);
            foreach(var point in supplies.Concat(new[]{Exit,Facility,Breakthrough}))if(float.IsNaN(point.x)||float.IsInfinity(point.x)||float.IsNaN(point.y)||float.IsInfinity(point.y))throw new InvalidOperationException("任务坐标无效");
        }
    }
}
