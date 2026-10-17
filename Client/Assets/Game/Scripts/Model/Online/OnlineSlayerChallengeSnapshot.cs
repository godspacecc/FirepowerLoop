using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    /// <summary>独立挑战只读投影；界面不能通过票数列表改写服务器工作态。</summary>
    public sealed class OnlineSlayerChallengeSnapshot
    {
        public int Phase {get;}
        public int Downs {get;}
        public int DownLimit {get;}
        public long CashCollected {get;}
        public int Outcome {get;}
        public IReadOnlyList<long> ExitVotes {get;}
        public IReadOnlyList<long> EnterReady {get;}
        public IReadOnlyList<long> SkipReady {get;}
        public OnlineSlayerChallengeSnapshot(int phase,int downs,int limit,long cash,int outcome,IEnumerable<long> exits,IEnumerable<long> enter,IEnumerable<long> skip)
        {
            Phase=phase;Downs=downs;DownLimit=limit;CashCollected=cash;Outcome=outcome;
            ExitVotes=Array.AsReadOnly(exits?.ToArray()??Array.Empty<long>());EnterReady=Array.AsReadOnly(enter?.ToArray()??Array.Empty<long>());SkipReady=Array.AsReadOnly(skip?.ToArray()??Array.Empty<long>());
        }
    }
}
