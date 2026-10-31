using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>幸存者独立敌池；只负责活动时间解锁与抽取，不创建场景对象或修改闯关配置。</summary>
    public sealed class SurvivorEnemyRoster
    {
        /// <summary>保留表顺序以便同随机输入获得稳定选择。</summary>
        public IReadOnlyList<SurvivorEnemyDefinition> Entries { get; }
        private SurvivorEnemyRoster(SurvivorEnemyDefinition[] entries) => Entries=Array.AsReadOnly(entries);

        /// <summary>开局前一次性校验敌人引用及所有有限参数；失败拒绝准备敌池。</summary>
        public static SurvivorEnemyRoster FromTables(global::Tables tables)
        {
            if(tables==null)throw new ArgumentNullException(nameof(tables));
            var rows=tables.TbSurvivorEnemy.DataList;
            if(rows.Count==0)throw new ArgumentException("幸存者敌池不能为空");
            var entries=rows.Select(r=>new SurvivorEnemyDefinition(r.Id,r.EnemyId,r.UnlockSeconds,r.PhaseSeconds,r.PhaseWeights,
                r.HealthMultiplier,r.DamageMultiplier,r.SpeedMultiplier,new Vector3(r.ScaleX,r.ScaleY,r.ScaleZ),r.AttackCooldownSeconds)).ToArray();
            if(entries.Select(e=>e.Id).Distinct().Count()!=entries.Length ||
                entries.Any(e=>tables.TbEnemy.GetOrDefault(e.EnemyId)==null) ||
                !entries.Any(e=>e.UnlockSeconds==0))
                throw new ArgumentException("幸存者敌池需要有效敌人引用、唯一身份和开局普通敌人");
            var first=entries[0];
            if(entries.Any(e=>e.PhaseSeconds!=first.PhaseSeconds||e.PhaseWeights.Count!=first.PhaseWeights.Count))
                throw new ArgumentException("幸存者敌池分档间隔及档数必须一致");
            for(int phase=0;phase<first.PhaseWeights.Count;phase++)
            {
                double age=(double)phase*first.PhaseSeconds;
                if(entries.Sum(e=>(long)e.PhaseWeights[phase])!=100||entries.Any(e=>e.UnlockSeconds>age&&e.PhaseWeights[phase]>0))
                    throw new ArgumentException("幸存者每档比例必须合计100，未解锁敌人比例必须为0");
            }
            return new SurvivorEnemyRoster(entries);
        }

        /// <summary>只影响本次新出生抽取；按活动时间选择档位，不修改任何已存活敌人。</summary>
        public SurvivorEnemyDefinition Select(double elapsedSeconds,float random01)
        {
            if(double.IsNaN(elapsedSeconds)||double.IsInfinity(elapsedSeconds)||elapsedSeconds<0 ||
                float.IsNaN(random01)||random01<0||random01>=1)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds),"敌池时间或随机数无效");
            var eligible=Entries.Where(e=>e.WeightAt(elapsedSeconds)>0).ToArray();
            double roll=random01*eligible.Sum(e=>(double)e.WeightAt(elapsedSeconds));
            foreach(var entry in eligible){int weight=entry.WeightAt(elapsedSeconds);if(roll<weight)return entry;roll-=weight;}
            return eligible[eligible.Length-1];
        }
    }

    /// <summary>本模式只读敌人倍率与出生限制；原模型、武器和动作继续引用闯关敌人身份。</summary>
    public sealed class SurvivorEnemyDefinition
    {
        /// <summary>敌池行身份与共用敌人配置身份。</summary>
        public string Id { get; }
        public string EnemyId { get; }
        /// <summary>有效生存秒数达到此值才加入候选。</summary>
        public float UnlockSeconds { get; }
        /// <summary>每档活动秒数及各档百分比；复制生成数组，避免外部修改只读敌池。</summary>
        public float PhaseSeconds { get; }
        public IReadOnlyList<int> PhaseWeights { get; }
        /// <summary>只在本模式生成时乘入共用原值，时间推进不改变这些属性。</summary>
        public float HealthMultiplier { get; }
        public float DamageMultiplier { get; }
        public float SpeedMultiplier { get; }
        /// <summary>已采用的本模式三轴体型；占地及受击使用水平最大比例。</summary>
        public Vector3 VisualScale { get; }
        public float BodyRadiusMultiplier => Mathf.Max(VisualScale.x,VisualScale.z);
        /// <summary>幸存者独立命中后冷却，来自本模式权威表。</summary>
        public float AttackCooldownSeconds { get; }
        public SurvivorEnemyDefinition(string id,string enemyId,float unlock,float phaseSeconds,int[] phaseWeights,float health,float damage,float speed,Vector3 scale,float cooldown)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(enemyId)||phaseWeights==null||phaseWeights.Length==0||phaseWeights.Any(w=>w<0||w>100)||
                new[]{unlock,phaseSeconds,health,damage,speed,scale.x,scale.y,scale.z,cooldown}.Any(x=>float.IsNaN(x)||float.IsInfinity(x)) ||
                unlock<0||phaseSeconds<=0||health<=0||damage<=0||speed<=0||scale.x<=0||scale.y<=0||scale.z<=0||cooldown<=0)
                throw new ArgumentException("幸存者敌池参数无效");
            Id=id;EnemyId=enemyId;UnlockSeconds=unlock;PhaseSeconds=phaseSeconds;PhaseWeights=Array.AsReadOnly((int[])phaseWeights.Clone());HealthMultiplier=health;
            DamageMultiplier=damage;SpeedMultiplier=speed;VisualScale=scale;AttackCooldownSeconds=cooldown;
        }
        /// <summary>向下取整定位时间档，超出配置后保持末档；解锁前和零占比均不参与抽取。</summary>
        internal int WeightAt(double seconds)
            =>seconds<UnlockSeconds?0:PhaseWeights[(int)Math.Min(PhaseWeights.Count-1,seconds/PhaseSeconds)];
    }
}
