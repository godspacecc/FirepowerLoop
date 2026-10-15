using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>离线准备的数值快照；来自正式配置，不持有生成表或玩家状态。</summary>
    public sealed class BasePreparationRule
    {
        public double MinutesPerSecond { get; }
        public int StartMinute { get; }
        public long DailyGold { get; }
        public int RestMinute { get; }
        public long DepartureGrams { get; }
        public long NormalGrams { get; }
        public double SpeedPenalty { get; }
        public double SprintIncrease { get; }
        public double MinimumSpeed { get; }
        public double MaximumSprint { get; }
        public BasePreparationRule(double minutesPerSecond,int startMinute,long dailyGold,int restMinute,long departureGrams,long normalGrams,double speedPenalty,double sprintIncrease,double minimumSpeed,double maximumSprint)
        { MinutesPerSecond=minutesPerSecond;StartMinute=startMinute;DailyGold=dailyGold;RestMinute=restMinute;DepartureGrams=departureGrams;NormalGrams=normalGrams;SpeedPenalty=speedPenalty;SprintIncrease=sprintIncrease;MinimumSpeed=minimumSpeed;MaximumSprint=maximumSprint; }
    }
    public readonly struct ItemPreparationDefinition
    {
        public ItemPreparationDefinition(int itemId,int grams,string category){ItemId=itemId;Grams=grams;Category=category;}
        public int ItemId { get; }
        public int Grams { get; }
        public string Category { get; }
    }
    public sealed class MapPreparationRule
    {
        public MapPreparationRule(bool restricted,IReadOnlyCollection<string> allowed){Restricted=restricted;Allowed=allowed;}
        public bool Restricted { get; }
        public IReadOnlyCollection<string> Allowed { get; }
    }
}
