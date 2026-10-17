using System;
using System.Collections.Generic;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>独立挑战阶段，不复用主线清轮或限时清怪的成功标记。</summary>
    public enum ArcadeSlayerChallengePhase {Preparing=1,Collecting=2,Boss=3,Shopping=4,Returning=5,Finished=6}
    public enum ArcadeSlayerChallengeOutcome {None=0,Won=1,Failed=2,Abandoned=3,Skipped=4}

    /// <summary>纯逻辑挑战候选；保存身份、投票和有限现金账本，不保存任何世界对象。</summary>
    [Serializable]
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerChallengeState
    {
        public string RunId {get;set;}
        public int Generation {get;set;}
        public int ReturnArena {get;set;}
        public int ReturnMap {get;set;}
        public ArcadeSlayerChallengePhase Phase {get;set;}
        public ArcadeSlayerChallengeOutcome Outcome {get;set;}
        public double RemainingSeconds {get;set;}
        public int TeamDowns {get;set;}
        public long CashCollected {get;set;}
        public bool CashGranted {get;set;}
        public List<long> Members {get;set;}=new List<long>();
        /// <summary>0未选择、1挑战、2主线；只接受原始成员。</summary>
        public Dictionary<long,int> EntryChoices {get;set;}=new Dictionary<long,int>();
        public List<long> ExitVotes {get;set;}=new List<long>();
        /// <summary>正额领取才记录，数量由正式现金上限约束，满额后的拾取不增长账本。</summary>
        public List<long> CollectedCoinIds {get;set;}=new List<long>();
        public ArcadeSlayerChallengeState Clone()=>new ArcadeSlayerChallengeState {RunId=RunId,Generation=Generation,ReturnArena=ReturnArena,ReturnMap=ReturnMap,
            Phase=Phase,Outcome=Outcome,RemainingSeconds=RemainingSeconds,TeamDowns=TeamDowns,CashCollected=CashCollected,CashGranted=CashGranted,
            Members=Members.ToList(),EntryChoices=new Dictionary<long,int>(EntryChoices),ExitVotes=ExitVotes.ToList(),CollectedCoinIds=CollectedCoinIds.ToList()};
    }
}
