using System;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>全目录真实生成数据与纯几何定向验证；不启动场景或写入玩家存档。</summary>
    public sealed class ArcadeRoundLayoutTests
    {
        private Tables Load() => new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes"))));
        [Test] public void CatalogContainsEveryThemeAndFourDistinctRoundLayouts()
        {
            var provider=new LubanRuntimeConfigProvider(Load);
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            Assert.That(catalog.RoundLayouts.Count,Is.EqualTo(80));
            foreach(var arena in catalog.Arenas)
            {
                var group=catalog.RoundLayouts.Where(p=>p.ArenaConfigNumber==arena.ArenaNumber).ToArray();
                Assert.That(group.Select(p=>p.RoundNumber),Is.EquivalentTo(new[]{1,2,3,4}));
                Assert.That(group.Select(p=>string.Join(";",p.Obstacles.Select(o=>$"{o.X}|{o.Y}|{o.HalfWidth}|{o.HalfHeight}"))).Distinct().Count(),Is.EqualTo(4));
            }
        }
        [Test] public void AllGeneratedEntrancesSpawnAndExitsHaveMultiplayerRoutes()
        {
            foreach(var row in Load().TbArcadeRoundLayout.DataList)
            {
                var p=ArcadeRoundLayoutParameters.FromConfig(row);
                Assert.That(p.FitsArena(19.5f,19.5f,.55f),Is.True,p.Id);
            }
        }
        [Test] public void MissingConfigIsRejectedBeforeCreatingLayout()
        {
            Assert.Throws<ArgumentNullException>(()=>ArcadeRoundLayoutParameters.FromConfig(null));
        }

        /// <summary>所有正式地图均有六个可达、不同位置的武器候选点，每轮生成两把。</summary>
        [Test] public void AllMapsProvideSixReachableWeaponPointsAndTwoPickups()
        {
            foreach(var row in Load().TbArcadeRoundLayout.DataList)
            {
                var p=ArcadeRoundLayoutParameters.FromConfig(row);
                var points=p.WeaponSpawnPoints;
                Assert.That(points.Count,Is.EqualTo(6),p.Id);
                Assert.That(points.Select(v=>(v.X,v.Y)).Distinct().Count(),Is.EqualTo(6),p.Id);
                Assert.That(p.WeaponSpawnCount,Is.EqualTo(2),p.Id);
                Assert.That(p.WeaponSpawnFirstDelayMin,Is.EqualTo(3f),p.Id);
                Assert.That(p.WeaponSpawnFirstDelayMax,Is.EqualTo(8f),p.Id);
                Assert.That(p.WeaponSpawnDelayMin,Is.EqualTo(20f),p.Id);
                Assert.That(p.WeaponSpawnDelayMax,Is.EqualTo(30f),p.Id);
                Assert.That(p.FitsArena(19.5f,19.5f,.55f),Is.True,p.Id);
                foreach(var point in points)
                {
                    Assert.That(p.IsWalkable(point.X,point.Y,1.1f),Is.True,p.Id);
                    Assert.That(Vector2.Distance(new Vector2(point.X,point.Y),new Vector2(p.SpawnX,p.SpawnY)),Is.GreaterThan(3),p.Id);
                }
            }
        }

        /// <summary>拒绝非有限坐标、重复和不可达点位；已复制的运行时点位不随源数组变化。</summary>
        [Test] public void WeaponPointsRejectInvalidGeometryAndKeepImmutableCopies()
        {
            var row=Load().TbArcadeRoundLayout.DataList.First();
            var original=row.WeaponSpawnPoints[0];
            var retained=ArcadeRoundLayoutParameters.FromConfig(row);
            ArcadeLayoutEntryConfig Point(float x,float y)
            {var bytes=new ByteBuf();bytes.WriteFloat(x);bytes.WriteFloat(y);return new ArcadeLayoutEntryConfig(new ByteBuf(bytes.CopyData()));}
            row.WeaponSpawnPoints[0]=Point(float.NaN,0);
            Assert.Throws<ArgumentException>(()=>ArcadeRoundLayoutParameters.FromConfig(row));
            Assert.That(retained.WeaponSpawnPoints[0].X,Is.EqualTo(original.X));
            row.WeaponSpawnPoints[0]=row.WeaponSpawnPoints[1];
            Assert.Throws<ArgumentException>(()=>ArcadeRoundLayoutParameters.FromConfig(row));
            row.WeaponSpawnPoints[0]=Point(row.SpawnX,row.SpawnY);
            Assert.Throws<ArgumentException>(()=>ArcadeRoundLayoutParameters.FromConfig(row));
            row.WeaponSpawnPoints[0]=Point(25,0);
            Assert.That(ArcadeRoundLayoutParameters.FromConfig(row).FitsArena(19.5f,19.5f,.55f),Is.False);
        }
    }
}
