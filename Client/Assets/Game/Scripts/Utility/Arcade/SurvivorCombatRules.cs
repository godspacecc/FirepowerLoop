using System;

namespace Game
{
    /// <summary>幸存者自动瞄准与刷怪预算纯规则；参数由权威配置与当前战斗快照提供。</summary>
    public static class SurvivorCombatRules
    {
        /// <summary>在有效射程内选择最近可攻击目标，同距按稳定ID排序，零表示本帧不射击。</summary>
        public static long SelectTarget(float playerX,float playerZ,float range,long[] ids,float[] x,float[] z,bool[] attackable)
        {
            if(!Finite(playerX)||!Finite(playerZ)||!Finite(range)||range<=0 || ids==null || x==null || z==null || attackable==null ||
                ids.Length!=x.Length || ids.Length!=z.Length || ids.Length!=attackable.Length) return 0;
            long selected=0; double distance=(double)range*range;
            for(int i=0;i<ids.Length;i++)
            {
                if(ids[i]<=0 || !attackable[i] || !Finite(x[i]) || !Finite(z[i])) continue;
                double dx=(double)x[i]-playerX,dz=(double)z[i]-playerZ, next=dx*dx+dz*dz;
                if(next<distance || next==distance&&(selected==0 || ids[i]<selected))
                {selected=ids[i];distance=next;}
            }
            return selected;
        }

        /// <summary>根据有效生存分钟加快生成；配置下限限制每帧生成压力。</summary>
        public static float SpawnInterval(double elapsedSeconds,float initial,float minimum,float accelerationPerMinute)
        {
            if(!Finite(initial)||!Finite(minimum)||!Finite(accelerationPerMinute)||initial<=0||minimum<=0||minimum>initial||accelerationPerMinute<0)
                throw new ArgumentOutOfRangeException(nameof(initial),"刷怪间隔配置无效");
            if(double.IsNaN(elapsedSeconds)||elapsedSeconds<0) elapsedSeconds=0;
            return (float)Math.Max(minimum,initial/(1+elapsedSeconds/60*accelerationPerMinute));
        }

        /// <summary>本帧最多生成量，达到上限不累积可无限补发的欠账。</summary>
        public static int SpawnBudget(int alive,int maximum,int requested)
            =>alive<0 || maximum<=0 || requested<=0 || alive>=maximum?0:Math.Min(maximum-alive,requested);

        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
