using System;

namespace Game
{
    /// <summary>基地日历与负重的纯计算；不修改金币、库存、属性或存档。</summary>
    public static class PreparationMath
    {
        public const long MillisecondsPerDay=86400000;
        public static long Day(long milliseconds){if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));return milliseconds/MillisecondsPerDay+1;}
        /// <summary>资金只计算尚未记账的日，不按现实日期或远征开始时刻发放。</summary>
        public static long DailyCredit(long milliseconds,long lastGrantedDay,long dailyGold)=>checked(Math.Max(0,Day(milliseconds)-lastGrantedDay)*dailyGold);
        public static long NextRestTime(long milliseconds,int minute)=>checked(Day(milliseconds)*MillisecondsPerDay+minute*60000L);
        public static double MoveMultiplier(long grams,long normal,double perKg,double minimum)=>Math.Max(minimum,1-Math.Max(0,grams-normal)/1000.0*perKg);
        public static double SprintMultiplier(long grams,long normal,double perKg,double maximum)=>Math.Min(maximum,1+Math.Max(0,grams-normal)/1000.0*perKg);
    }
}
