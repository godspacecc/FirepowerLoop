using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>真实死亡结算到表现端口及联机快照的定向检查；不把爆炸视觉误当伤害事实。</summary>
    public sealed partial class EnemyDeathPresentationTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Recorder recorder = new();
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        [SetUp] public void ResetRecorder() { recorder.Deaths.Clear(); recorder.Impacts.Clear(); }

        private void PrepareSolo(int arenaNumber = 1)
        {
            var architecture = new global::Game.Game();
            var run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = "death-test", RemainingLives = 3, IsCasualRoute = true,
                ArenaConfigNumber = arenaNumber, NextArenaNumber = arenaNumber, Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = arenaNumber } });
            run.EnterArena(); system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime { ArenaNumber = arenaNumber, PlayerHealth = 100, PlayerMaxHealth = 100, Bombs = 1 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            foreach (var field in new[] { ("catalog", (object)catalog), ("arena", catalog.Arenas.Single(arena => arena.ArenaNumber == arenaNumber)),
                ("view", recorder), ("random", new System.Random(37)) })
                typeof(ArcadeCombatSystem).GetField(field.Item1, Flags).SetValue(system, field.Item2);
        }

        private object CallSolo(string method, params object[] args) => typeof(ArcadeCombatSystem).GetMethod(method, Flags).Invoke(system, args);

        [Test]
        public void SoloExplosionCarriesRadialDirectionAndRepeatedKillDoesNotReplay()
        {
            PrepareSolo(); var enemy = new ArcadeEnemyRuntime { Id = 1, Health = 1, Position = new Vector2(3, 2) };
            runtime.Enemies.Add(1, enemy);
            CallSolo("DamageEnemiesInExplosionRadius", runtime, new Vector2(1, 2), 4f, 20);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(1));
            Assert.That(recorder.Deaths[0].explosive, Is.True);
            Assert.That(recorder.Deaths[0].direction.normalized, Is.EqualTo(Vector2.right));
            CallSolo("KillEnemy", runtime, enemy, false);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void SoloBombCarriesExplosiveDeathWithoutChangingKillBudget()
        {
            PrepareSolo(); runtime.Enemies.Add(1, new ArcadeEnemyRuntime { Id = 1, Health = 20, Position = new Vector2(3, 0) });
            CallSolo("UseBomb", runtime);
            Assert.That(runtime.Bombs, Is.Zero); Assert.That(runtime.Enemies, Is.Empty);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(1));
            Assert.That(recorder.Deaths[0].explosive, Is.True);
        }

        [Test]
        public void GenericCircularDamageDoesNotTurnElectricOrbIntoExplosion()
        {
            PrepareSolo(); runtime.Enemies.Add(1, new ArcadeEnemyRuntime { Id = 1, Health = 1, Position = new Vector2(3, 2) });
            CallSolo("DamageEnemiesInRadius", runtime, new Vector2(1, 2), 4f, 20);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(1));
            Assert.That(recorder.Deaths[0].explosive, Is.False);
        }

        [Test]
        public void GroundBarrelDeathCarriesExplosionAndDoesNotReplayAfterRemoval()
        {
            PrepareSolo(3); runtime.PlayerPosition = new Vector2(-15, -15);
            runtime.GroundBarrels.Add(0, Vector2.zero);
            runtime.Enemies.Add(1, new ArcadeEnemyRuntime { Id = 1, Health = 1, MaxHealth = 20, Position = Vector2.right });
            CallSolo("DetonateGroundBarrel", runtime, 0);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(1));
            Assert.That(recorder.Deaths[0].explosive, Is.True);
            Assert.That(recorder.Deaths[0].direction.normalized, Is.EqualTo(Vector2.right));
            CallSolo("DetonateGroundBarrel", runtime, 0);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExplosiveCarrierTransfersRadialImpulseToNearbyVictim()
        {
            PrepareSolo(3); runtime.PlayerPosition = new Vector2(-15, -15);
            var carrier = new ArcadeEnemyRuntime { Id = 1, Health = 1, MaxHealth = 20, ContentSpecialKind = 1 };
            runtime.Enemies.Add(1, carrier);
            runtime.Enemies.Add(2, new ArcadeEnemyRuntime { Id = 2, Health = 1, MaxHealth = 20, Position = Vector2.right });
            CallSolo("KillEnemy", runtime, carrier, false);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(2));
            Assert.That(recorder.Deaths.Count(death => death.explosive), Is.EqualTo(1));
            Assert.That(recorder.Deaths.Single(death => death.explosive).direction.normalized, Is.EqualTo(Vector2.right));
        }

        private static Type DeathType => typeof(OnlineArcadePresentationSystem).Assembly.GetType("Game.OnlineArcadeEnemyDeathSnapshot");
        private Array Deaths(int generation)
        {
            Assert.That(DeathType, Is.Not.Null, "缺少权威死亡事实的不可变快照");
            var array = Array.CreateInstance(DeathType, 1);
            array.SetValue(new OnlineArcadeEnemyDeathSnapshot(41L, 1f, 0f, true, generation, false), 0); return array;
        }

        [TestCase(7, true)]
        [TestCase(6, false)]
        public void OnlineConsumesOnlyVisibleRemovedEnemyFromSameRound(int deathRound, bool shouldPlay)
        {
            var deaths = Deaths(deathRound);
            var online = new OnlineArcadePresentationSystem();
            typeof(OnlineArcadePresentationSystem).GetField("view", Flags).SetValue(online, recorder);
            var method = typeof(OnlineArcadePresentationSystem).GetMethod("SyncEnemies", Flags);
            var alive = new[] { new OnlineArcadeEnemySnapshot(41, "enemy.arcade.basic", 3, 0, 20, 20, 1, 1) };
            // 同步入口现已需要玩家列表供近战朝向使用，本例仅测死亡，显式传入空列表。
            var players = Array.Empty<OnlineArcadeBattlePlayerSnapshot>();
            method.Invoke(online, new object[] { alive, false, null, 7, true, players });
            method.Invoke(online, new object[] { Array.Empty<OnlineArcadeEnemySnapshot>(), false, deaths, 7, true, players });
            method.Invoke(online, new object[] { Array.Empty<OnlineArcadeEnemySnapshot>(), false, deaths, 7, true, players });
            Assert.That(recorder.Deaths.Count, Is.EqualTo(shouldPlay ? 1 : 0));
            if (shouldPlay) Assert.That(recorder.Deaths[0].explosive, Is.True);
        }

        [Test]
        public void NetworkProjectionCopiesDeathValuesIndependentOfMessageMutation()
        {
            var source = new ET.OnlineArcadeBattleInfo();
            var field = source.GetType().GetProperty("EnemyDeaths");
            Assert.That(field, Is.Not.Null, "正式协议未携带死亡事实");
            var list = (System.Collections.IList)field.GetValue(source);
            var item = Activator.CreateInstance(field.PropertyType.GetGenericArguments()[0]);
            foreach (var value in new[] { ("RuntimeId", (object)41L), ("DirectionX", 1f), ("DirectionZ", 0f), ("Explosive", true), ("RoundGeneration", 7) })
                item.GetType().GetProperty(value.Item1).SetValue(item, value.Item2);
            list.Add(item);
            var copy = (OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { source });
            item.GetType().GetProperty("DirectionX").SetValue(item, 99f);
            var copied = (System.Collections.IList)copy.GetType().GetProperty("EnemyDeaths").GetValue(copy);
            Assert.That(copied.Count, Is.EqualTo(1));
            Assert.That(copied[0].GetType().GetProperty("DirectionX").GetValue(copied[0]), Is.EqualTo(1f));
        }

        private sealed class Recorder : IArcadeCombatViewPort
        {
            public readonly List<(Vector2 direction, bool explosive)> Deaths = new();
            public readonly List<int> Impacts = new();
            public bool IsActive => true;
            public void RemoveEnemy(int id, bool death = false) { if (death) Deaths.Add((Vector2.zero, false)); }
            public void RemoveEnemy(int id, bool death, Vector2 direction, bool explosive) { if (death) { Deaths.Add((direction, explosive)); Impacts.Add(0); } }
            public void RemoveEnemy(int id, bool death, Vector2 direction, bool explosive, bool flameKill, int impactKind)
            { if (death) { Deaths.Add((direction, explosive)); Impacts.Add(impactKind); } }
            public void Begin(int n, float w, float h, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 p, Vector2 f, bool v, bool i) { }
            public void SetPlayerHealth(int c, int m) { }
            public void SetEnemy(int id, string cfg, Vector2 p, int c, int m, bool t = false, int s = 0, bool h = false) { }
            public void SetProjectile(int id, bool h, Vector2 p, Vector2 d) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition d, Vector2 p) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 p, Vector2 f, bool v, bool i, int c, int m) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float dt) { }
        }
    }
}
