using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式内容落点应保持在角色所在的合法墙边，不因浮点接触误差回跳出生点。</summary>
    public sealed class ArcadeContentPlacementTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private FullRouteFlowTests h;
        private ArcadeCombatSystem combat;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            h=new FullRouteFlowTests();h.SetUp();
            combat=(ArcadeCombatSystem)typeof(FullRouteFlowTests).GetField("combat",Private).GetValue(h);
            runtime=(ArcadeCombatRuntime)typeof(FullRouteFlowTests).GetField("runtime",Private).GetValue(h);
            catalog=(ArcadeConfigCatalogDefinition)typeof(FullRouteFlowTests).GetField("catalog",Private).GetValue(h);
        }
        [TearDown] public void Cleanup(){h?.TearDown();}
        private Vector2 Place(Vector2 point)
        {
            var result=(Vector2)typeof(ArcadeCombatSystem).GetMethod("ContentPosition",Private).Invoke(combat,new object[]{runtime,point});
            var layout=catalog.RoundLayouts.Single(x=>x.ArenaConfigNumber==3&&x.RoundNumber==1);
            Assert.That(layout.IsWalkable(result.x,result.y,catalog.Rule.PlayerRadius,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight),Is.True,"落点必须满足正式布局半径");
            return result;
        }
        [Test] public void ObservedWallContactDoesNotTeleportToSpawn()
        {var point=new Vector2(11.480113f,-3.45f);Assert.That(Vector2.Distance(Place(point),point),Is.LessThan(.01f));}
        [Test] public void InsideObstacleProjectsToNearbyLegalEdge()
        {var point=new Vector2(10,-6);Assert.That(Vector2.Distance(Place(point),point),Is.LessThan(4));}
        [Test] public void ClearGroundKeepsExactPosition()
        {Assert.That(Place(new Vector2(0,-8)),Is.EqualTo(new Vector2(0,-8)));}
        [Test] public void FarCornerProjectsNearActualMapCorner()
        {var expected=new Vector2(catalog.Rule.ArenaHalfWidth-catalog.Rule.PlayerRadius,catalog.Rule.ArenaHalfHeight-catalog.Rule.PlayerRadius);Assert.That(Vector2.Distance(Place(new Vector2(100,100)),expected),Is.LessThan(.01f));}
        [Test] public void OppositeWallContactRemainsLocal()
        {var point=new Vector2(-11,5.45f);Assert.That(Vector2.Distance(Place(point),point),Is.LessThan(.01f));}
        [Test] public void ConfiguredSpawnRemainsUnchanged()
        {Assert.That(Place(new Vector2(0,-13)),Is.EqualTo(new Vector2(0,-13)));}
    }
}
