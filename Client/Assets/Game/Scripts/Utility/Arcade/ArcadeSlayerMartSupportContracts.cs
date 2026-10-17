using System;
using System.Collections.Generic;
using System.Linq;

namespace FirepowerLoop.Arcade
{
    /// <summary>商店支援独立于原普通掉落槽。</summary>
    public enum SlayerMartSupportKind { Chicken=1, Orb, Saw, Barrel }
    /// <summary>一个有稳定身份的支援效果；双锯的两个接触体共享本效果身份与命中账本。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartEffectInstance
    {
        /// <summary>实例、组合、来源事件和接力队列身份，普通跨图不重建规则身份。</summary>
        public string InstanceId { get; set; }
        public string GroupId { get; set; }
        public string SourceEventId { get; set; }
        public string QueueId { get; set; }
        public string ProductId { get; set; }
        public long OwnerId { get; set; }
        public SlayerMartSupportKind Kind { get; set; }
        /// <summary>来源商品决定时限合同，例如组合爆桶的300秒硬上限。</summary>
        public SlayerMartEffect SourceEffect { get; set; }
        /// <summary>活动剩余、周期冷却、轨道相位和真实剩余次数。</summary>
        public double RemainingSeconds { get; set; }
        public double CooldownSeconds { get; set; }
        public double Phase { get; set; }
        public int Charges { get; set; }
        /// <summary>固定设施的逻辑锚点；移动支援由拥有者实时位置投影。</summary>
        public float AnchorX { get; set; }
        public float AnchorY { get; set; }
        public ArcadeSlayerMartEffectInstance Clone()=>(ArcadeSlayerMartEffectInstance)MemberwiseClone();
    }
    /// <summary>当前份在个人Supports内，Waiting只保存尚未启动的完整份。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartEffectQueue
    {
        public string QueueId { get; set; }
        public List<ArcadeSlayerMartEffectInstance> Waiting { get; set; }=new List<ArcadeSlayerMartEffectInstance>();
        public ArcadeSlayerMartEffectQueue Clone()=>new ArcadeSlayerMartEffectQueue{QueueId=QueueId,Waiting=Waiting.Select(p=>p.Clone()).ToList()};
    }
    /// <summary>同组合对每个目标共享冷却；TargetKey由宿主携带地图代次，避免跨图ID复用。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartSupportHit
    {
        public string GroupId { get; set; }
        public string TargetKey { get; set; }
        public double RemainingSeconds { get; set; }
        public ArcadeSlayerMartSupportHit Clone()=>(ArcadeSlayerMartSupportHit)MemberwiseClone();
    }
    /// <summary>复用普通支援配置的不可变基础值；不把精通乘进原始定义。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartSupportDefinition
    {
        public SlayerMartSupportKind Kind { get; }
        public string ProductId { get; }
        public float Duration { get; }
        public int Damage { get; }
        public float Interval { get; }
        public float Radius { get; }
        public float ProjectileSpeed { get; }
        public int Charges { get; }
        public ArcadeSlayerMartSupportDefinition(SlayerMartSupportKind kind,string product,float duration,int damage,float interval,float radius,float projectileSpeed,int charges)
        {Kind=kind;ProductId=product;Duration=duration;Damage=damage;Interval=interval;Radius=radius;ProjectileSpeed=projectileSpeed;Charges=charges;
            if(!Enum.IsDefined(typeof(SlayerMartSupportKind),kind)||string.IsNullOrWhiteSpace(product)||duration<=0||damage<=0||interval<=0||radius<0||projectileSpeed<0||charges<0||
                new[]{duration,interval,radius,projectileSpeed}.Any(p=>float.IsNaN(p)||float.IsInfinity(p)))throw new ArgumentException("支援基础配置无效");}
    }
    /// <summary>纯规则共用的四类支援目录。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeSlayerMartSupportCatalog
    {
        private readonly IReadOnlyDictionary<SlayerMartSupportKind,ArcadeSlayerMartSupportDefinition> definitions;
        public ArcadeSlayerMartSupportDefinition Get(SlayerMartSupportKind kind)=>definitions[kind];
        public ArcadeSlayerMartSupportCatalog(IEnumerable<ArcadeSlayerMartSupportDefinition> values)
        {var rows=values.ToArray();if(rows.Length!=4||rows.Select(p=>p.Kind).Distinct().Count()!=4)throw new ArgumentException("四类支援配置不完整");definitions=rows.ToDictionary(p=>p.Kind);}
    }
}
