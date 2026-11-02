using System.Linq;
using QFramework;
using UnityEngine;
namespace Game
{
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
    public sealed partial class SurvivorSystem
    {
        /// <summary>任务状态只属于当前战斗，停止后清除。</summary>
        private bool missionMode;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private MissionConfiguration missionConfig;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private MissionRunState mission;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private Vector2[] missionSupplies;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private Vector2 missionExit;
        /// <summary>设施与最终突破坐标仅属于本局地图。</summary>
        private Vector2 missionFacility,missionBreakthrough;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public MissionPhase MissionPhase=>mission?.Phase??global::Game.MissionPhase.Entry;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public float MissionFacilityRadius=>missionConfig?.FacilityRadius??0;
        /// <summary>当前搜索与最终波次供任务提示显示。</summary>
        public int MissionCurrentSearchIndex=>mission?.SearchIndex??-1;
        public int MissionWaveIndex=>mission?.BreakthroughWave??0;
        public bool MissionCanCollect=>mission!=null&&mission.Phase==global::Game.MissionPhase.Search&&MissionRemainingEnemies==0;
        /// <summary>当前设施活动倒计时。</summary>
        public float MissionPhaseSecondsRemaining=>mission?.SecondsRemaining??0;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public Vector2 MissionFacilityPosition=>missionFacility;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public Vector2 MissionBreakthroughPosition=>missionBreakthrough;

        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool IsMission=>missionMode;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int MissionCollected=>mission?.Collected??0;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int MissionSupplyCount=>missionConfig?.SupplyCount??0;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int MissionRemainingEnemies=>mission==null?0:mission.PendingSpawns+enemies.Count;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool MissionReadyToExtract=>mission!=null&&mission.Phase==global::Game.MissionPhase.Extract&&MissionRemainingEnemies==0;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public Vector2 MissionTargetOffset=>MissionObjectivePosition-player;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public Vector2 MissionObjectivePosition {get{
            if(mission==null)return player;
            if(mission.Phase==global::Game.MissionPhase.Activate||mission.Phase==global::Game.MissionPhase.Defend)return missionFacility;
            if(mission.Phase==global::Game.MissionPhase.Breakthrough)return missionBreakthrough;
            if(mission.Phase==global::Game.MissionPhase.Search&&missionSupplies!=null){int nearest=-1;float distance=float.MaxValue;for(int i=0;i<mission.SupplyCount;i++)if(!mission.IsCollected(i)){float d=(missionSupplies[i]-player).sqrMagnitude;if(d<distance){distance=d;nearest=i;}}if(nearest>=0)return missionSupplies[nearest];}
            return mission.Phase==global::Game.MissionPhase.Entry?player:missionExit;
        }}
        /// <summary>普通模式入口清除失败的任务准备状态。</summary>
        public void ResetMissionMode(){if(!Model.Active){missionMode=false;mission=null;}}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool TryPrepareMission(out string error){if(Model.Active){error="当前战斗尚未退出";return false;}missionMode=true;if(!this.GetUtility<IRuntimeConfigProvider>().TryGetMission(out missionConfig,out error)){missionMode=false;return false;}return TryPrepare(out error);}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool TryStartMission(out string error){return TryPrepareMission(out error)&&TryStart(out error);}
        /// <summary>沿用真实场地坐标与碰撞，将任务目标放在可站立的位置。</summary>
        private void BeginMission()
        {
            mission=new MissionRunState(missionConfig.EntryCount,missionConfig.SearchCounts,missionConfig.DefendCount,missionConfig.BreakthroughWaves,missionConfig.BreakthroughCount,missionConfig.ActivationSeconds,missionConfig.DefendSeconds);
            missionSupplies=Enumerable.Range(0,mission.SupplyCount).Select(i=>Resolve(player+missionConfig.Supply(i),catalog.Rule.PlayerRadius)).ToArray();
            missionExit=Resolve(player+missionConfig.Exit,catalog.Rule.PlayerRadius);
            missionFacility=Resolve(player+missionConfig.Facility,catalog.Rule.PlayerRadius);missionBreakthrough=Resolve(player+missionConfig.Breakthrough,catalog.Rule.PlayerRadius);

        }
        /// <summary>开场和每次推进都确认箱体已加载，失败交回既有场景错误流程。</summary>
        public bool TryPrepareMissionPresentation(out bool ready,out string error)
        {
            ready=false;error=null;if(!missionMode||mission==null){error="任务尚未开始";return false;}
            view.SetMissionExit(MissionReadyToExtract,missionExit);
            view.SetMissionFacility(missionFacility);
            bool allReady=view.IsMissionExitReady&&view.IsMissionFacilityReady;
            for(int i=0;i<mission.SupplyCount;i++)if(!mission.IsCollected(i)){view.SetMissionSupply(i,true,missionSupplies[i]);allReady&=view.IsMissionSupplyReady(i);}
            error=view.MissionSupplyLoadError;if(!string.IsNullOrEmpty(error))return false;
            ready=allReady;return true;
        }
        /// <summary>靠近自动收集，按实际生成成功数限量伏击；手柄与键盘共享移动入口。</summary>
        private void TickMission(float dt)
        {
            if(mission==null||mission.Completed||Model.Paused)return;
            if(!TryPrepareMissionPresentation(out bool ready,out var error)){LastError=error;Model.Paused=true;input.Disable();Publish();return;}
            if(!ready)return;
            mission.Advance(dt,enemies.Count,Vector2.Distance(player,missionFacility)<=missionConfig.FacilityRadius,Vector2.Distance(player,missionBreakthrough)<=missionConfig.InteractionRadius);
            if(mission.TryTakeFacilityReward()){Model.State.AddExperience(Model.State.RequiredExperience);Offer();if(Model.Paused)return;}
            for(int i=0;i<mission.SupplyCount;i++)if(!mission.IsCollected(i)){
                // 表现就绪后才接受靠近收集。
                if(Vector2.Distance(player,missionSupplies[i])<=missionConfig.InteractionRadius&&mission.TryCollect(i))view.SetMissionSupply(i,false,missionSupplies[i]);
            }
            if(mission.CanSpawn&&enemies.Count<missionConfig.AliveCap&&spawnClock<=0){int before=enemies.Count;SpawnEnemy(enemyRoster.Entries.Single(e=>e.Id==missionConfig.RosterId(mission)));if(enemies.Count>before)mission.TryRecordSpawn();spawnClock=missionConfig.SpawnInterval;}
            if(MissionReadyToExtract){view.SetMissionExit(true,missionExit);if(Model.Health>0&&Vector2.Distance(player,missionExit)<=missionConfig.InteractionRadius&&mission.TryExtract(enemies.Count)){view.SetMissionExit(false,missionExit);TryFinish(out _);}}
        }
    }
}
