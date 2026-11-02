using System;
namespace Game
{
    /// <summary>本局任务阶段，禁止跨阶段结算。</summary>
    public enum MissionPhase { Entry, Search, Activate, Defend, Breakthrough, Extract, Completed }
    /// <summary>有限任务规则；生成成功才扣额度，所有战斗须清空后推进。</summary>
    public sealed class MissionRunState
    {
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private readonly bool[] collected;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private readonly int[] searchCounts;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private readonly int defendCount, waveCount, waveSize;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private readonly float activationSeconds, defendSeconds;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private int scheduled, phaseSpawned, alive, waves;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private bool rewardPending, rewardTaken, breakthroughStarted;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int SupplyCount=>collected.Length;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int AmbushCount {get;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int Collected {get;private set;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int Spawned {get;private set;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public MissionPhase Phase {get;private set;}=MissionPhase.Entry;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public bool Completed=>Phase==MissionPhase.Completed;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public float SecondsRemaining {get;private set;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public int EncounterSpawned=>phaseSpawned;
        /// <summary>本轮搜索位置，决定遭遇敌人组合。</summary>
        public int SearchIndex {get;private set;}=-1;
        /// <summary>最终突破当前波次，从一开始。</summary>
        public int BreakthroughWave=>waves;
        /// <summary>尚未成功生成的额度。</summary>
        public int PendingSpawns=>scheduled-phaseSpawned;
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public bool CanSpawn=>!Completed&&PendingSpawns>0;
        /// <summary>简短规则构造入口；正式运行使用完整权威参数。</summary>
        public MissionRunState(int supplyCount,int ambushCount):this(ambushCount,CreateCounts(supplyCount,ambushCount),ambushCount,1,ambushCount,1,1){}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private static int[] CreateCounts(int n,int count){if(n<=0)throw new ArgumentOutOfRangeException();var result=new int[n];for(int i=0;i<n;i++)result[i]=count;return result;}
        /// <summary>复制配置避免运行中被外部改写。</summary>
        public MissionRunState(int entry,int[] searches,int defense,int waveTotal,int waveEnemies,float activation,float duration)
        {
            if(entry<=0||searches==null||searches.Length==0||Array.Exists(searches,n=>n<=0)||defense<=0||waveTotal<=0||waveEnemies<=0||activation<=0||duration<=0||float.IsNaN(activation)||float.IsInfinity(activation)||float.IsNaN(duration)||float.IsInfinity(duration))throw new ArgumentOutOfRangeException();
            collected=new bool[searches.Length];searchCounts=(int[])searches.Clone();defendCount=defense;waveCount=waveTotal;waveSize=waveEnemies;activationSeconds=activation;defendSeconds=duration;
            checked{AmbushCount=entry+defense+waveTotal*waveEnemies;foreach(int count in searches)AmbushCount+=count;}Schedule(entry);
        }
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        private void Schedule(int count){scheduled=count;phaseSpawned=0;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public bool IsCollected(int index)=>index>=0&&index<SupplyCount&&collected[index];
        /// <summary>搜刮顺序自由，但必须完成当前地点遭遇。</summary>
        public bool TryCollect(int index){if(Phase!=MissionPhase.Search||PendingSpawns!=0||alive!=0||index<0||index>=SupplyCount||collected[index])return false;collected[index]=true;Collected++;SearchIndex=index;Schedule(searchCounts[index]);return true;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public bool TryRecordSpawn(){if(!CanSpawn)return false;phaseSpawned++;Spawned++;return true;}
        /// <summary>仅由未暂停战斗时钟调用；离开设施停止读秒。</summary>
        public void Advance(float dt,int aliveCount,bool inFacility,bool inBreakthrough)
        {
            if(Completed||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt)||aliveCount<0)return;
            alive=aliveCount;bool clear=PendingSpawns==0&&alive==0;
            switch(Phase)
            {
                case MissionPhase.Entry:if(clear){Phase=MissionPhase.Search;Schedule(0);}break;
                case MissionPhase.Search:if(clear&&Collected==SupplyCount){Phase=MissionPhase.Activate;SecondsRemaining=activationSeconds;}break;
                case MissionPhase.Activate:
                    if(inFacility)SecondsRemaining=Math.Max(0,SecondsRemaining-dt);
                    if(SecondsRemaining==0){Phase=MissionPhase.Defend;SecondsRemaining=defendSeconds;Schedule(defendCount);}break;
                case MissionPhase.Defend:
                    if(inFacility)SecondsRemaining=Math.Max(0,SecondsRemaining-dt);
                    if(SecondsRemaining==0&&clear){rewardPending=true;Phase=MissionPhase.Breakthrough;Schedule(0);}break;
                case MissionPhase.Breakthrough:
                    if(!breakthroughStarted){if(inBreakthrough){breakthroughStarted=true;waves=1;Schedule(waveSize);}}
                    else if(clear){if(waves<waveCount){waves++;Schedule(waveSize);}else Phase=MissionPhase.Extract;}break;
            }
        }
        /// <summary>设施奖励只消费一次，沿用本局强化入口。</summary>
        public bool TryTakeFacilityReward(){if(!rewardPending||rewardTaken)return false;rewardTaken=true;return true;}
        /// <summary>当前任务的权威阶段参数与状态，界面只读使用。</summary>
        public bool TryExtract(int aliveCount){if(Phase!=MissionPhase.Extract||PendingSpawns!=0||aliveCount!=0)return false;Phase=MissionPhase.Completed;return true;}
    }
}
