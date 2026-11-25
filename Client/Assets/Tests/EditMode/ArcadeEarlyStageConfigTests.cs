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
        [Test] public void FarmIsTheOnlyConfiguredArenaAndHasNoCampaignExit()
        {
            var farm = catalog.Arenas.Single();
            Assert.That(farm.ArenaNumber, Is.EqualTo(22));
            Assert.That(farm.NextArenaNumbers, Is.Empty);
            Assert.That(catalog.RoundLayouts.Select(layout => layout.ArenaConfigNumber).Distinct(),
                Is.EquivalentTo(new[] { farm.ArenaNumber }));
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
    }
}
