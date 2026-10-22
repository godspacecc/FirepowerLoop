using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>正式配置必须给出新图、真实阻挡与连通通路；旧分支继续保留。</summary>
    public sealed class ArcadeEarlyStageConfigTests
    {
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
        }
        [Test] public void NewStagesHaveEightFixedRoundsWithoutReplacingLegacyBranchIdentities()
        {
            Assert.That(catalog.Arenas.Any(a=>a.ArenaNumber==21),Is.True,"缺码头配置21");Assert.That(catalog.Arenas.Any(a=>a.ArenaNumber==22),Is.True,"缺农场配置22");
            Assert.That(catalog.Arenas.First(a=>a.ArenaNumber==21).NextArenaNumbers,Is.EqualTo(new[]{22}));Assert.That(catalog.Arenas.First(a=>a.ArenaNumber==22).NextArenaNumbers,Is.EqualTo(new[]{5}));
            Assert.That(catalog.Arenas.First(a=>a.ArenaNumber==2).NextArenaNumbers,Is.EqualTo(new[]{4,15}));Assert.That(catalog.Arenas.First(a=>a.ArenaNumber==3).NextArenaNumbers,Is.EqualTo(new[]{4,15}));
            foreach(int id in new[]{4,15,20})Assert.That(catalog.Arenas.Any(a=>a.ArenaNumber==id),Is.True);
            Assert.That(catalog.RoundLayouts.Count(p=>p.ArenaConfigNumber==21||p.ArenaConfigNumber==22),Is.EqualTo(8));
        }
        [Test] public void FarmFourYardsConnectThroughGatesAndBreachesButWallsAndShedsBlockMovement()
        {
            var rounds=catalog.RoundLayouts.Where(p=>p.ArenaConfigNumber==22).ToArray();Assert.That(rounds.Length,Is.EqualTo(4));
            foreach(var p in rounds)
            {
                Assert.That(p.FitsArena(30.5f,30.5f,.55f),Is.True,p.Id);
                foreach(var at in new[]{new Vector2(-15,-15),new Vector2(15,-15),new Vector2(-15,15),new Vector2(15,15),new Vector2(0,-14),new Vector2(0,14),new Vector2(-14,0),new Vector2(14,0)})Assert.That(p.IsWalkable(at.x,at.y,.55f,30.5f,30.5f),Is.True,at.ToString());
                foreach(var at in new[]{new Vector2(0,0),new Vector2(0,7),new Vector2(8,0),new Vector2(23,23),new Vector2(-23,23)})Assert.That(p.IsWalkable(at.x,at.y,.55f,30.5f,30.5f),Is.False,at.ToString());
            }
        }
        [Test] public void DockPlatformAndPassagesConnectWhileVisibleWaterAndCratesBlockMovement()
        {
            var rounds=catalog.RoundLayouts.Where(p=>p.ArenaConfigNumber==21).ToArray();Assert.That(rounds.Length,Is.EqualTo(4));
            foreach(var p in rounds)
            {
                Assert.That(p.FitsArena(20.5f,19.5f,.55f),Is.True,p.Id);
                foreach(var at in new[]{new Vector2(0,0),new Vector2(-17,4),new Vector2(3,16),new Vector2(18,8),new Vector2(14,-4)})Assert.That(p.IsWalkable(at.x,at.y,.55f,20.5f,19.5f),Is.True,at.ToString());
                foreach(var at in new[]{new Vector2(14,7),new Vector2(-18,-8),new Vector2(-6.75f,9.7f)})Assert.That(p.IsWalkable(at.x,at.y,.55f,20.5f,19.5f),Is.False,at.ToString());
            }
        }
    }
}
