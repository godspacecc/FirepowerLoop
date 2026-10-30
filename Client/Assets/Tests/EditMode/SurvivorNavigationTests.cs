using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>真实农场导航三项限定测试，每项覆盖全部配置入口。</summary>
    public sealed class SurvivorNavigationTests
    {
        private SurvivorNavigation navigation;
        private ArcadeRoundLayoutParameters layout;
        private ArcadeObstacleDefinition[] obstacles;
        private float width,height,radius;
        [SetUp] public void Setup(){var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);layout=catalog.RoundLayouts.Single(x=>x.Id=="arena.22.round.1");var stage=catalog.EarlyStages.Single(x=>x.ArenaConfigNumber==22);width=stage.HalfWidth;height=stage.HalfHeight;radius=catalog.Rule.EnemyContactRadius;obstacles=layout.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight,o.BlocksProjectiles)).ToArray();navigation=new SurvivorNavigation(width,height,radius,obstacles);}
        private Vector2 Resolve(Vector2 p)=>ArcadeCombatSystem.ResolvePosition(p,radius,width,height,obstacles);
        private void Chase(Vector2 target,bool retarget)
        {
            foreach(var entry in layout.Entries){Vector2 position=Resolve(new Vector2(entry.X,entry.Y));navigation.SetTarget(target);for(int step=0;step<6000&&Vector2.Distance(position,target)>.7f;step++){if(retarget&&step==100){target=Resolve(new Vector2(layout.SpawnX+4,layout.SpawnY+2));navigation.SetTarget(target);}Vector2 next=navigation.Move(position,target,.12f);Assert.That(navigation.CanTravel(position,next),Is.True,"不能穿障碍或切围栏角");Assert.That(Vector2.Distance(position,next),Is.LessThanOrEqualTo(.121f));position=next;}Assert.That(Vector2.Distance(position,target),Is.LessThanOrEqualTo(.7f),"入口 "+entry.X+","+entry.Y+" 未抵达目标，终点 "+position);}
        }
        [Test] public void EveryFarmEntranceReachesPlayerSpawn(){Chase(Resolve(new Vector2(layout.SpawnX,layout.SpawnY)),false);}
        [Test] public void MovingTargetDoesNotRescanStaticObstacles()
        {
            var counted=new CountingObstacles(obstacles);
            var cached=new SurvivorNavigation(width,height,radius,counted);
            counted.Scans=0;
            for(int i=0;i<20;i++)cached.SetTarget(Resolve(new Vector2(layout.SpawnX+(i%2)*.8f,layout.SpawnY)));
            Assert.That(counted.Scans,Is.Zero,"玩家换格时应复用静态通路，不能重新扫描全图障碍");
        }
        /// <summary>统计规则读取静态障碍的次数，避免依赖机器速度的耗时断言。</summary>
        private sealed class CountingObstacles:System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition>
        {
            private readonly ArcadeObstacleDefinition[] values;
            public int Scans;
            public CountingObstacles(ArcadeObstacleDefinition[] source){values=source;}
            public int Count=>values.Length;
            public ArcadeObstacleDefinition this[int index]=>values[index];
            public System.Collections.Generic.IEnumerator<ArcadeObstacleDefinition> GetEnumerator(){Scans++;return ((System.Collections.Generic.IEnumerable<ArcadeObstacleDefinition>)values).GetEnumerator();}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [Test] public void EveryFarmEntranceReplansForMovingPlayer(){Chase(Resolve(new Vector2(layout.SpawnX,layout.SpawnY)),true);}
        [Test] public void EveryFarmEntranceReachesOppositeSideWithoutCrossingFence(){Chase(Resolve(new Vector2(13,-16)),false);}
    }
}
