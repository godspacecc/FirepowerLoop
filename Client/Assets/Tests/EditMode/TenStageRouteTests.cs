using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>只检查已确认十关路线、实际普通轮及三种旧分支恢复；不加载正式场景。</summary>
    public sealed class TenStageRouteTests
    {
        private static readonly int[] Route = { 1, 21, 22, 23, 24, 25, 5, 7, 10, 20 };
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private string directory;
        private Game app;
        private ArcadeRunModel model;
        private ArcadeRunSystem runs;
        private ArcadeConfigCatalogDefinition catalog;

        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "FirepowerTenStage-" + Guid.NewGuid().ToString("N"));
            app = new Game(); model = new ArcadeRunModel(); app.RegisterModel(model);
            app.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes")))));
            app.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            runs = new ArcadeRunSystem(); app.RegisterSystem(runs); runs.ConfigureForTests(directory);
        }

        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        /// <summary>遗漏版本切换会让正式新局仍进入十三场；直接检查开局落盘后的身份。</summary>
        [Test] public void ConfiguredNewRunPersistsTenStageVersion()
        {
            Assert.That(runs.TryStartConfiguredRun(out var data, out var error), Is.True, error);
            var saved = new ArcadeRunFileStore(directory).Inspect();
            Assert.That(saved.IsValid, Is.True, saved.Error);
            Assert.That(data.CasualRouteVersion, Is.EqualTo(2));
            Assert.That(saved.Data.CasualRouteVersion, Is.EqualTo(2));
            Assert.That(saved.Data.ArenaConfigNumber, Is.EqualTo(1));
        }

        /// <summary>配置仍连接火山或旧分支时，正式目录实际可达路径会偏离用户名单。</summary>
        [Test] public void GeneratedCatalogOnlyOpensConfirmedTenStages()
        {
            for (int i = 0; i < Route.Length; i++)
            {
                var arena = catalog.Arenas.Single(a => a.ArenaNumber == Route[i]);
                Assert.That(arena.NextArenaNumbers, Is.EqualTo(i == 9 ? Array.Empty<int>() : new[] { Route[i + 1] }), "map " + Route[i]);
                Assert.That(catalog.RoundLayouts.Count(r => r.ArenaConfigNumber == Route[i]), Is.EqualTo(4));
            }
            Assert.That(catalog.Arenas.Select(a => a.ArenaNumber), Does.Contain(6), "暂停场地仍须保留供旧局恢复");
        }

        /// <summary>实际结算事务必须在第四十普通轮之后保存胜利、分数和本局结果。</summary>
        [Test] public void NewRunCompletesTenStagesAndKeepsSavedResult()
        {
            Assert.That(runs.TryStartConfiguredRun(out var data, out var error), Is.True, error);
            CompleteRoute(data, Route, 2);
        }

        /// <summary>分别覆盖三条旧路线分支，旧局恢复不得被新的唯一名单截断。</summary>
        [TestCase(7, 9)] [TestCase(8, 10)] [TestCase(13, 16)]
        public void OldRunResumesAndCompletesOriginalBranches(int branch, int next)
        {
            foreach (int version in new[] { 0, 1 })
            {
                int[] prefix = version == 0 ? new[] { 1, 21, 22, 5, 6 } : new[] { 1, 21, 22, 23, 24, 25, 5, 6 };
                int[] suffix = branch == 13 ? new[] { branch, next, 18, 19, 20 } : new[] { branch, next, 11, 12, 20 };
                int[] route = prefix.Concat(suffix).ToArray();
                var data = Node(version, route[3], 4);
                Assert.That(new ArcadeRunFileStore(directory).Save(data, out var error), Is.True, error);
                Assert.That(runs.TryResume(out data, out error), Is.True, error);
                CompleteRoute(data, route, version, 3);
            }
        }

        /// <summary>出口投影必须只作用于本局，不能因新配置覆盖旧边或污染原始目录。</summary>
        [Test] public void LegacyExitProjectionPreservesFarmVolcanoAndBothCourtyards()
        {
            foreach (int version in new[] { 0, 1 })
            {
                foreach (var item in new[] { (5, new[] { 6 }), (7, new[] { 9, 10 }), (10, new[] { 11 }) })
                {
                    var row = catalog.Arenas.Single(a => a.ArenaNumber == item.Item1);
                    Assert.That(row.WithRouteVersion(version).NextArenaNumbers, Is.EqualTo(item.Item2));
                }
                Assert.That(catalog.Arenas.Single(a => a.ArenaNumber == 22).WithRouteVersion(version).NextArenaNumbers,
                    Is.EqualTo(new[] { version == 0 ? 5 : 23 }));
            }
            Assert.That(catalog.Arenas.Single(a => a.ArenaNumber == 5).NextArenaNumbers, Is.EqualTo(new[] { 7 }));
        }

        /// <summary>新旧轮前恢复仍携带原地图、轮数与玩家状态，不能重解释成另一条路线。</summary>
        [Test] public void RoundCheckpointsResumeAllRouteVersionsWithoutChangingIdentity()
        {
            foreach (var identity in new[] { (0, 6, 5), (1, 12, 12), (2, 10, 9) })
            {
                var data = Node(identity.Item1, identity.Item2, identity.Item3);
                string mapId = $"arena.{identity.Item2}.round.3";
                data.RoundCheckpoint = new ArcadeRoundCheckpoint { ArenaNumber = identity.Item3, ArenaConfigNumber = identity.Item2,
                    RoundNumber = 3, LayoutVersion = 1, MapId = mapId,
                    PlayerState = new ArcadeIslandRoundCheckpoint { MapId = mapId, RoundNumber = 3, PlayerHealth = 73, PlayerMaxHealth = 100, Bombs = 1, Dashes = 2 } };
                Assert.That(new ArcadeRunFileStore(directory).Save(data, out var error), Is.True, error);
                Assert.That(runs.TryResume(out var resumed, out error), Is.True, error);
                Assert.That(resumed.CasualRouteVersion, Is.EqualTo(identity.Item1));
                Assert.That(resumed.RoundCheckpoint.MapId, Is.EqualTo(mapId));
                Assert.That(resumed.RoundCheckpoint.PlayerState.PlayerHealth, Is.EqualTo(73));
            }
        }

        /// <summary>按实际战斗生成四轮，不把十关仅实现成结算数字。</summary>
        [Test] public void ConfirmedStagesPrepareFortyRealOrdinaryRounds()
        {
            var combat = new ArcadeCombatSystem(); ((ICanSetArchitecture)combat).SetArchitecture(app);
            typeof(ArcadeCombatSystem).GetField("catalog", Flags).SetValue(combat, catalog);
            typeof(ArcadeCombatSystem).GetField("random", Flags).SetValue(combat, new System.Random(10));
            for (int stage = 0; stage < Route.Length; stage++)
            {
                var data = Node(2, Route[stage], stage + 1); model.CommitSafeNode(data); model.EnterArena();
                typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(combat, catalog.Arenas.Single(a => a.ArenaNumber == Route[stage]).WithRouteVersion(2));
                var runtime = new ArcadeCombatRuntime { ArenaNumber = stage + 1, PlayerHealth = 100, PlayerMaxHealth = 100 };
                app.GetModel<ArcadeCombatModel>().Begin(runtime);
                for (int round = 0; round < 4; round++)
                {
                    typeof(ArcadeCombatSystem).GetMethod("BeginWave", Flags).Invoke(combat, new object[] { runtime, round });
                    Assert.That(runtime.SpawnQueue.Count, Is.EqualTo(Math.Min(110 + (stage * 4 + round) * 10, 400)), $"stage {stage + 1} round {round + 1}");
                    Assert.That(runtime.WeaponSpawns, Is.Not.Null);
                }
            }
        }

        /// <summary>旧十三场局的后段挑战仍须接受已保存的第十一至十三场返回身份。</summary>
        [Test] public void LegacyLateChallengeReturnsRemainValid()
        {
            var tables = new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes"))));
            var config = ArcadeSlayerChallengeConfiguration.FromTables(tables);
            foreach (var item in new[] { (11, 11), (12, 12), (13, 20) })
                Assert.That(ArcadeSlayerChallengeRules.Create(config, "legacy-return", new long[] { 1 }, item.Item1, item.Item2, 1).ReturnArena, Is.EqualTo(item.Item1));
        }

        /// <summary>未知版本仍明确拒绝恢复，避免未来存档被误解释成十关。</summary>
        [Test] public void UnknownRouteVersionIsRejectedBeforeSaving()
        {
            var data = Node(3, 1, 1);
            Assert.That(new ArcadeRunFileStore(directory).Save(data, out _), Is.False);
            Assert.That(File.Exists(new ArcadeRunFileStore(directory).RunPath), Is.False);
        }

        private static ArcadeRunSaveData Node(int version, int map, int order)
        {
            var data = new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                CasualRouteVersion = version, ArenaConfigNumber = map, NextArenaNumber = order, CompletedArenaCount = order - 1,
                UpdatedAtUtc = DateTime.UtcNow.ToString("O"), Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = order } };
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId; return data;
        }

        private void CompleteRoute(ArcadeRunSaveData data, int[] route, int version, int start = 0)
        {
            for (int index = start; index < route.Length; index++)
            {
                Assert.That(data.ArenaConfigNumber, Is.EqualTo(route[index])); model.EnterArena();
                bool final = index == route.Length - 1;
                Assert.That(runs.TryCompleteArena(new ArcadeArenaCompletion { ArenaNumber = index + 1, NextArenaConfigNumber = final ? 0 : route[index + 1],
                    Victory = final, Score = (index + 1) * 100, ScoreMultiplier = 1, EarnedGold = 1,
                    NextCheckpoint = new ArcadeCheckpointSaveData { ArenaNumber = index + 2 }, EquipmentBySlot = data.EquipmentBySlot, EmergencyResources = data.EmergencyResources }, out data, out var error), Is.True, error);
                Assert.That(new ArcadeRunFileStore(directory).Inspect().IsValid, Is.True);
            }
            var result = new ArcadeRunFileStore(directory).Inspect().Data;
            Assert.That(result.CasualRouteVersion, Is.EqualTo(version)); Assert.That(result.Victory, Is.True);
            Assert.That(result.CompletedArenaCount, Is.EqualTo(route.Length)); Assert.That(result.Score, Is.EqualTo(route.Length * 100));
            Assert.That(result.Outcome, Is.EqualTo("victory")); Assert.That(result.PendingRewards, Is.Empty);
        }
    }
}
