using System;
namespace Game
{
    /// <summary>单局任务规则；去重收集、有限生成和撤离顺序由战斗系统拥有。</summary>
    public sealed class MissionRunState
    {
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        private readonly bool[] collected;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int SupplyCount => collected.Length;
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int AmbushCount {get;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int Collected {get;private set;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public int Spawned {get;private set;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool Completed {get;private set;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public MissionRunState(int supplyCount,int ambushCount){if(supplyCount<=0||ambushCount<=0)throw new ArgumentOutOfRangeException();collected=new bool[supplyCount];AmbushCount=ambushCount;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool IsCollected(int index)=>index>=0&&index<SupplyCount&&collected[index];
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool TryCollect(int index){if(Completed||index<0||index>=SupplyCount||collected[index])return false;collected[index]=true;Collected++;return true;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool TryRecordSpawn(){if(Completed||Spawned>=AmbushCount)return false;Spawned++;return true;}
        /// <summary>本局任务参数与状态由战斗权威持有，界面只读查询。</summary>
        public bool TryExtract(int aliveCount){if(Completed||Collected!=SupplyCount||Spawned!=AmbushCount||aliveCount!=0)return false;Completed=true;return true;}
    }
}
