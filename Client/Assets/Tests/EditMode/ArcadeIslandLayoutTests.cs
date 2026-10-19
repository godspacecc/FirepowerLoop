using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>四图几何与固定阻挡的局部合同；不运行场景或改变存档。</summary>
    public sealed class ArcadeIslandLayoutTests
    {
        [Test]
        public void EverySpawnAndObstacleKeepsInsideMapWithDrySafeSpawn()
        {
            for (int round = 1; round <= 4; round++)
            {
                var region = new ArcadeIslandRegion(19.5f,19.5f,round);
                var spawn = ArcadeIslandLayout.GetSpawn(round,19.5f,19.5f);
                Assert.That(region.Contains(spawn.x,spawn.y),Is.True,"第"+round+"图出生点越界");
                Assert.That(region.IsShallow(spawn.x,spawn.y),Is.False,"第"+round+"图出生点位于浅水");
                foreach (var obstacle in ArcadeIslandLayout.GetObstacles(round,19.5f,19.5f))
                {
                    Assert.That(ArcadeCombatSystem.IsInsideObstacle(spawn,.55f,new[]{obstacle}),Is.False,
                        "第"+round+"图出生点侵入膨胀障碍："+obstacle.Center);
                    for(int corner=0;corner<4;corner++)
                        Assert.That(region.Contains(obstacle.Center.x+((corner&1)==0?-1:1)*obstacle.HalfExtents.x,
                            obstacle.Center.y+((corner&2)==0?-1:1)*obstacle.HalfExtents.y),Is.True,
                            "第"+round+"图障碍角点越界："+obstacle.Center+" corner="+corner);
                }
            }
        }

        [Test]
        public void EveryRoundHasFourDryWalkableExitsInDifferentDirections()
        {
            foreach (int round in new[] { 1, 2, 3, 4 })
            foreach (int count in new[] { 4 })
            for (int index = 0; index < count; index++)
            {
                var region = new ArcadeIslandRegion(19.5f, 19.5f, round);
                var exit = ArcadeIslandLayout.GetExitPosition(round, index, count, 19.5f, 19.5f, .55f);
                var direction = new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left }[index];
                Assert.That(Vector2.Dot(exit.normalized, direction), Is.GreaterThan(.95f), "出口必须分布在上下左右四边");
                Assert.That(region.Contains(exit.x, exit.y), Is.True);
                Assert.That(region.IsShallow(exit.x, exit.y), Is.False);
                Assert.That(ArcadeCombatSystem.IsInsideObstacle(exit, .55f,
                    ArcadeIslandLayout.GetObstacles(round, 19.5f, 19.5f)), Is.False);
                var constrained = region.Constrain(exit.x, exit.y, .55f + 1.5f);
                Assert.That(Vector2.Distance(exit,new Vector2(constrained.X,constrained.Y)), Is.LessThan(.01f));
            }
        }

        [Test]
        public void WreckFootprintMatchesThirdMapObstacle()
        {
            var obstacle=ArcadeIslandLayout.GetObstacles(3,19.5f,19.5f)[0];
            var mesh=ArcadeIslandMesh.Wreck(obstacle.HalfExtents.x*2,obstacle.HalfExtents.y*2);
            try
            {
                Assert.That(mesh.bounds.extents.x,Is.EqualTo(obstacle.HalfExtents.x).Within(.001f));
                Assert.That(mesh.bounds.extents.z,Is.EqualTo(obstacle.HalfExtents.y).Within(.001f));
                Assert.That(mesh.bounds.size.y,Is.GreaterThan(2));
                Assert.That(new ArcadeIslandRegion(19.5f,19.5f,3).IsShallow(11,0),Is.True);
                Assert.That(new ArcadeIslandRegion(19.5f,19.5f,3).IsShallow(-11,0),Is.False);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
