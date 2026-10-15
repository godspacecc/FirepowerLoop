using System.IO;
using Luban;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>锁定独立街机可进入的地图与奖励边界，防止旧玩法重新进入正式目录。</summary>
    public sealed class FirepowerLoopBoundaryTests
    {
        [Test]
        public void MapCatalogOnlyAllowsTheArcadeDestination()
        {
            Assert.That(MapCatalog.TryGet("map.another-day.arcade", out _), Is.True);
            foreach (var id in new[] { "map.another-day.base", "map.another-day.civilisation-valley",
                         "map.another-day.survival-sample", "map.another-day.mission-sample", "map.hejia-village" })
                Assert.That(MapCatalog.TryGet(id, out _), Is.False, id);
        }

        [Test]
        public void ArcadeCatalogDoesNotIssueOutOfRunCurrencies()
        {
            var root = System.Environment.GetEnvironmentVariable("FIREPOWER_LOOP_TEST_ROOT") ??
                       Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../.."));
            var provider = new LubanRuntimeConfigProvider(() => new global::Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(root, "Client/Assets/Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Rewards, Is.Empty, "本局金币与宝藏奖励不通过局外货币目录发放");
        }
    }
}
