using System;
using System.Linq;
#if DOTNET
using StageConfig=ET.ArcadeEarlyStageConfig;
#else
using StageConfig=global::ArcadeEarlyStageConfig;
#endif

namespace FirepowerLoop.Arcade
{
    /// <summary>已确认码头、农场、墓园、神庙和集市的不可变场地参数，不改变旧主题或全局场地尺寸。</summary>
#if DOTNET
    [ET.EnableClass]
#endif
    public sealed class ArcadeEarlyStageParameters
    {
        public readonly int ArenaConfigNumber,HerdCount;
        public readonly float HalfWidth,HalfHeight,PortalX,PortalY,HerdSpacing,HerdFirstDelay,HerdPeriod,HerdPlayerDamage,HerdEnemyDamage;
        public readonly string EnvironmentAddress,HerdAddress;
        public readonly ArcadeHerdTuning Herd;
        private ArcadeEarlyStageParameters(StageConfig row)
        {
            ArenaConfigNumber=row.ArenaConfigNumber;HalfWidth=row.HalfWidth;HalfHeight=row.HalfHeight;PortalX=row.PortalX;PortalY=row.PortalY;
            EnvironmentAddress="Prefabs/SceneObjects/"+row.EnvironmentPrefabName;HerdAddress=row.HerdCount>0?"Prefabs/SceneObjects/"+row.HerdPrefabName:null;
            HerdCount=row.HerdCount;HerdSpacing=row.HerdSpacing;HerdFirstDelay=row.HerdFirstDelay;HerdPeriod=row.HerdPeriod;
            HerdPlayerDamage=row.HerdPlayerDamage;HerdEnemyDamage=row.HerdEnemyDamage;
            Herd=new ArcadeHerdTuning(row.HerdWindup,row.HerdSpeed,row.HerdDistance,row.HerdRadius);
        }
        /// <summary>新配置只影响21—25，拒绝路径注入、无效边界和多排/重叠牛群。</summary>
        public static ArcadeEarlyStageParameters FromConfig(StageConfig row)
        {
            if(row==null)throw new ArgumentNullException(nameof(row));
            bool Name(string name)=>!string.IsNullOrWhiteSpace(name)&&name.All(c=>char.IsLetterOrDigit(c)||c=='_');
            bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
            var values=new[]{row.HalfWidth,row.HalfHeight,row.PortalX,row.PortalY,row.HerdSpacing,row.HerdWindup,row.HerdSpeed,row.HerdDistance,row.HerdRadius,row.HerdFirstDelay,row.HerdPeriod,row.HerdPlayerDamage,row.HerdEnemyDamage};
            if(values.Any(v=>!Finite(v))||(row.ArenaConfigNumber<21||row.ArenaConfigNumber>25)||!Name(row.EnvironmentPrefabName)||
                row.HalfWidth<=5||row.HalfWidth>64||row.HalfHeight<=5||row.HalfHeight>64||Math.Abs(row.PortalX)+2>=row.HalfWidth||Math.Abs(row.PortalY)+2>=row.HalfHeight||
                row.HerdCount<0||row.HerdCount>7||row.ArenaConfigNumber!=22&&row.HerdCount!=0||row.ArenaConfigNumber==22&&row.HerdCount==0||
                row.HerdCount>0&&(!Name(row.HerdPrefabName)||row.HerdWindup<=0||row.HerdSpeed<=0||row.HerdDistance<=0||row.HerdRadius<=0||row.HerdSpacing<row.HerdRadius*2||
                row.HerdFirstDelay<=0||row.HerdPeriod<=row.HerdWindup+row.HerdDistance/row.HerdSpeed||row.HerdPlayerDamage<=0||row.HerdPlayerDamage>1||row.HerdEnemyDamage<=0||row.HerdEnemyDamage>1))
                throw new ArgumentException("早期固定场地参数无效："+row.ArenaConfigNumber);
            return new ArcadeEarlyStageParameters(row);
        }
    }
}
