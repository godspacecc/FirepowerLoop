using System;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>新海岛的实际通行与入口合同；不启动对局，不写玩家存档。</summary>
    public sealed class IslandPassageRevisionTests
    {
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void LoadCatalog()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
        }

        [Test] public void FourMapsKeepLargeDryUnobstructedCentralMovementArea()
        {
            foreach (var layout in catalog.RoundLayouts.Where(p => p.ArenaConfigNumber == 1))
            {
                var region = new ArcadeIslandRegion(19.5f, 19.5f, layout.RoundNumber);
                for (int x = -15; x <= 15; x += 3)
                for (int y = -15; y <= 15; y += 3)
                {
                    Assert.That(layout.IsWalkable(x, y, .55f), Is.True, layout.Id + " 中央通路被地形侵占 " + x + "," + y);
                    Assert.That(region.IsShallow(x, y), Is.False, layout.Id + " 中央不能按浅水减速");
                }
            }
        }

        [Test] public void EveryMapHasThreeConnectedEntriesAndMatchingPassageExits()
        {
            foreach (var layout in catalog.RoundLayouts.Where(p => p.ArenaConfigNumber == 1))
            {
                Assert.That(layout.Entries.Count, Is.EqualTo(3), layout.Id);
                Assert.That(layout.FitsArena(19.5f, 19.5f, .55f), Is.True, layout.Id + " 通道必须连通");
                for (int i = 0; i < 3; i++)
                {
                    var exit = layout.GetExitPosition(i, 19.5f, 19.5f, .55f);
                    var entry = layout.Entries[i];
                    Assert.That(Vector2.Distance(new Vector2(exit.X, exit.Y), new Vector2(entry.X, entry.Y)), Is.LessThan(.05f),
                        "敌人通道与清场离开通道应同位置：" + layout.Id);
                }
            }
        }

        [TestCase(1,18f,0f)][TestCase(2,18.7f,8f)]
        [TestCase(3,-18f,0f)][TestCase(4,0f,-18f)]
        public void EachMapPlacesItsSeaOnTheApprovedSide(int round, float x, float y)
        {
            var region = new ArcadeIslandRegion(19.5f, 19.5f, round);
            Assert.That(region.IsShallow(x, y), Is.True, "四图海岸应符合已确认的方向");
            Assert.That(region.IsShallow(-x * .8f, -y * .8f), Is.False, "内陆不能仍然是环岛水带");
        }
    }
}
