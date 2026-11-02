using System.Collections.Generic;
namespace FirepowerLoop.Preview
{
    /// <summary>只供独立编辑器原型使用的任务阶段。</summary>
    public enum MissionPreviewPhase { Briefing, Searching, Ambush, Extracting, Success, Failed }
    /// <summary>预览的任务规则，不写正式存档；数值为可丢弃的试玩占位。</summary>
    public sealed class MissionPreviewState
    {
        /// <summary>已搜寻的物资身份。</summary>
        private readonly HashSet<int> supplies=new HashSet<int>();
        /// <summary>已击败的追兵身份。</summary>
        private readonly HashSet<int> defeated=new HashSet<int>();
        /// <summary>当前任务阶段。</summary>
        public MissionPreviewPhase Phase {get;private set;}
        /// <summary>当前生命。</summary>
        public int Health {get;private set;}=100;
        /// <summary>已收集的任务物资数。</summary>
        public int Collected=>supplies.Count;
        /// <summary>当前剩余追兵。</summary>
        public int Remaining=>Phase==MissionPreviewPhase.Ambush?10-defeated.Count:0;
        /// <summary>只有出发后的未结束阶段接受输入。</summary>
        public bool Active=>Phase==MissionPreviewPhase.Searching||Phase==MissionPreviewPhase.Ambush||Phase==MissionPreviewPhase.Extracting;
        /// <summary>从简报开始一次任务。</summary>
        public void Start(){if(Phase==MissionPreviewPhase.Briefing)Phase=MissionPreviewPhase.Searching;}
        /// <summary>近距离搜寻唯一物资，集齐后触发尸潮。</summary>
        public bool Collect(int id,bool inRange){if(Phase!=MissionPreviewPhase.Searching||!inRange||id<0||id>=3||!supplies.Add(id))return false;if(supplies.Count==3)Phase=MissionPreviewPhase.Ambush;return true;}
        /// <summary>击败追兵仅记一次，清除所有追兵后允许撤离。</summary>
        public bool Defeat(int id){if(Phase!=MissionPreviewPhase.Ambush||id<0||id>=10||!defeated.Add(id))return false;if(defeated.Count==10)Phase=MissionPreviewPhase.Extracting;return true;}
        /// <summary>只在撤离阶段且接近出口时成功。</summary>
        public bool Extract(bool inRange){if(Phase!=MissionPreviewPhase.Extracting||!inRange)return false;Phase=MissionPreviewPhase.Success;return true;}
        /// <summary>活动任务内承受正伤害，失败后停止推进。</summary>
        public void Damage(int amount){if(!Active||amount<=0)return;Health=System.Math.Max(0,Health-amount);if(Health==0)Phase=MissionPreviewPhase.Failed;}
        /// <summary>重试清空原型内所有进度。</summary>
        public void Reset(){Phase=MissionPreviewPhase.Briefing;Health=100;supplies.Clear();defeated.Clear();}
    }
}
