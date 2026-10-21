using System;
using System.Collections.Generic;
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
    /// <summary>以正式配置验证奖励房开关、清场推进和普通财宝计分，不启动整局。</summary>
    public sealed class ArcadeRewardRoomGateTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private GateView view;

        [SetUp]
        public void Setup()
        {
            architecture = new Game();
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var data = new ArcadeRunSaveData { RunId = Guid.NewGuid().ToString("N"), IsCasualRoute = true,
                ArenaConfigNumber = 1, RemainingLives = 3, Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 1 } };
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId;
            var run = architecture.GetModel<ArcadeRunModel>();
            run.CommitSafeNode(data);
            run.EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 1, WaveIndex = 2,
                PlayerHealth = 100, PlayerMaxHealth = 100, PlayerPosition = new Vector2(-12, -12) };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            view = new GateView();
            Set("catalog", catalog);
            Set("arena", catalog.Arenas.Single(a => a.ArenaNumber == 1));
            Set("view", view);
        }

        [TearDown]
        public void Cleanup() => architecture.GetModel<ArcadeCombatModel>().Clear();

        private void Set(string name, object value) => typeof(ArcadeCombatSystem).GetField(name, Flags).SetValue(system, value);
        private object Call(string name, params object[] args) => typeof(ArcadeCombatSystem).GetMethod(name, Flags).Invoke(system, args);

        [Test]
        public void FirstArenaDoesNotCreateOrRevealRewardRoom()
        {
            Call("TickTreasureRoom", runtime, 0f);
            Call("SyncTreasureView", runtime);
            Assert.That(runtime.TreasureRoom, Is.Null);
            Assert.That(runtime.TreasureDiscovered || runtime.TreasureCleared || runtime.InTreasureRoom, Is.False);
            Assert.That(view.EntranceShown, Is.False);
        }

        [Test]
        public void FirstArenaThirdRoundClearOpensOrdinaryExit()
        {
            Call("TickTreasureRoom", runtime, 0f);
            Call("TickWaveTransition", runtime, 0f);
            Assert.That(runtime.WaveClearAwarded, Is.True);
            Assert.That(runtime.IslandRoundAwaitingExit, Is.True);
            Assert.That(runtime.WaveIndex, Is.EqualTo(2));
            Assert.That(runtime.InTreasureRoom, Is.False);
        }

        [TestCase(2)]
        [TestCase(3)]
        public void SecondArenaArmoryRemainsAvailable(int map)
        {
            var run = architecture.GetModel<ArcadeRunModel>();
            var data = run.Current;
            data.ArenaConfigNumber = map;
            data.NextArenaNumber = 2;
            run.CommitSafeNode(data);
            run.EnterArena();
            runtime.ArenaNumber = 2;
            runtime.PlayerPosition = new Vector2(15, 15);
            Set("arena", catalog.Arenas.Single(a => a.ArenaNumber == map));
            Call("TickTreasureRoom", runtime, 0f);
            Assert.That(runtime.TreasureRoom, Is.Not.Null);
            Assert.That(runtime.TreasureRoom.Parameters.IsArmory, Is.True);
            Assert.That(runtime.TreasureRoom.RemainingRewards.Length, Is.EqualTo(6));
            Assert.That(runtime.TreasureDiscovered, Is.True);
            Assert.That(runtime.InTreasureRoom, Is.False);
        }

        [Test]
        public void OrdinaryTreasureStillAwardsScoreAndExtraLife()
        {
            var run = architecture.GetModel<ArcadeRunModel>();
            var data = run.Current;
            var rules = catalog.TreasureRooms.First();
            data.Score = rules.LifeThreshold - 100;
            run.CommitSafeNode(data);
            run.EnterArena();
            Assert.That(Call("AwardOrdinaryTreasure", 100), Is.True);
            Assert.That(run.Current.Score, Is.EqualTo(rules.LifeThreshold));
            Assert.That(run.Current.RemainingLives, Is.EqualTo(4));
        }

        /// <summary>记录奖励房表现输入，确保关闭后不发布入口或方向引导。</summary>
        private sealed class GateView : IArcadeCombatViewPort
        {
            public bool EntranceShown;
            public bool IsActive => true;
            public void SetTreasureRoom(ArcadeTreasureRoomParameters p, bool inside, bool discovered, bool open, IReadOnlyList<int> ids)
            { EntranceShown = p != null && !inside && discovered; }
            public void Begin(int number, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable) { }
            public void SetPlayerHealth(int current, int maximum) { }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible, bool invulnerable, int current, int maximum) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}
