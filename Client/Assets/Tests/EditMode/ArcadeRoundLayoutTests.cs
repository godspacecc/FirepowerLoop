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
    }
}
