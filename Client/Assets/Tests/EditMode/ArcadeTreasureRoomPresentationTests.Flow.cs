using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed partial class ArcadeTreasureRoomPresentationTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game architecture;
        private OnlineArcadePresentationSystem system;
        private ArcadeConfigCatalogDefinition catalog;
        private TestView view;
        [SetUp] public void Setup()
        {
            architecture = new Game(); var account = new OnlineAccountModel(); architecture.RegisterModel(account);
            account.Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated, OnlineOperationState.Idle, OnlineAccountResultCode.Success, "one", 1, 1, 1, false, "one"));
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            system = new OnlineArcadePresentationSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture); view = new TestView();
            Set("view", view); Set("catalog", catalog); Set("activeArena", catalog.Arenas.First(a => a.ArenaNumber == 1)); Set("active", true);
        }
        private void Set(string name, object value) => typeof(OnlineArcadePresentationSystem).GetField(name, Flags).SetValue(system, value);
        private T Get<T>(string name) => (T)typeof(OnlineArcadePresentationSystem).GetField(name, Flags).GetValue(system);
        private OnlineArcadeBattleSnapshot Battle(long revision, bool inside, bool teammateInside = false, params int[] rewards)
        {
            var players = new[] { new OnlineArcadeBattlePlayerSnapshot(1, inside ? 0 : -12, inside ? -4 : -15, 0, 1, 100, 100, true, false, 0, inTreasureRoom: inside), new OnlineArcadeBattlePlayerSnapshot(2, 2, 2, 0, 1, 100, 100, true, false, 0, inTreasureRoom: teammateInside) };
            return new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 2, revision, 1000, players,
                new[] { new OnlineArcadeEnemySnapshot(10, "enemy.arcade.basic", 2, 3, 10, 10, 2, 4) }, new[] { new OnlineArcadeProjectileSnapshot(11, 0, 1, 1, 1, 1) },
                new[] { new OnlineArcadeDropSnapshot(12, catalog.Drops[0].Id, 1, 1, remainingSeconds: 1.5f) }, arenaConfigNumber: 1,
                treasureRoomId: "101:1:1", treasureDiscovered: true, treasureOpen: true, treasureSeconds: 40, treasureRewardIds: rewards,
                treasureRewardSeconds: 2f);
        }
        private void Apply(OnlineArcadeBattleSnapshot battle)
        {
            Set("latest", new OnlineArcadeRunClientSnapshot(true, 101, 202, 1, OnlineArcadeRunPhase.Arena, 0, 1, "test",
                new[] { new OnlineArcadePlayerSnapshot(1, "one", 3, 0, 2, 2, "arcade.weapon.basic", false, null), new OnlineArcadePlayerSnapshot(2, "two", 3, 0, 2, 2, "arcade.weapon.basic", false, null) }, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle));
            typeof(OnlineArcadePresentationSystem).GetMethod("ApplyAuthoritativeSnapshot", Flags).Invoke(system, new object[] { battle });
        }
        [TestCase(1, 0, true, false, true)]
        [TestCase(1, 1, true, false, true)]
        [TestCase(1, 2, true, false, true)]
        [TestCase(1, 3, true, false, true)]
        [TestCase(4, 0, true, false, true)]
        [TestCase(4, 1, true, false, true)]
        [TestCase(4, 2, true, false, true)]
        [TestCase(4, 3, true, false, true)]
        [TestCase(1, 0, false, false, true)]
        [TestCase(1, 0, true, true, true)]
        [TestCase(1, 0, true, false, false)]
        public void OnlineExitContactSubmitsCurrentRouteWithoutConfirmation(int round, int exitIndex,
            bool awaitingExit, bool inside, bool touching)
        {
            var runs = new OnlineArcadeRunSystem();
            architecture.RegisterSystem(runs);
            try
            {
                var arena = catalog.Arenas.First(a => a.ArenaNumber == 1);
                var layout = catalog.RoundLayouts.Single(p => p.ArenaConfigNumber == 1 && p.RoundNumber == round);
                var exit = layout.GetExitPosition(exitIndex, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius);
                var self = new OnlineArcadeBattlePlayerSnapshot(1,
                    exit.X + (touching ? 0 : 30), exit.Y, 0, 1, 100, 100, true, false, 0, inTreasureRoom: inside);
                var battle = new OnlineArcadeBattleSnapshot(1, round, 4, 1, 1, 2, 1, 0,
                    new[] { self }, null, arenaConfigNumber: 1, awaitingExit: awaitingExit, roundGeneration: 7);
                var snapshot = new OnlineArcadeRunClientSnapshot(true, 101, 202, 1, OnlineArcadeRunPhase.Arena,
                    0, 1, "test", null, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle);
                Set("latest", snapshot); Set("activeRoundLayout", layout);
                typeof(OnlineArcadeRunSystem).GetField("lastSnapshot", Flags).SetValue(runs, snapshot);
                typeof(OnlineArcadePresentationSystem).GetMethod("TickRoundExitContact", Flags).Invoke(system, new object[] { self });
                int generation = (int)typeof(OnlineArcadeRunSystem).GetField("confirmedExitGeneration", Flags).GetValue(runs);
                bool expected = awaitingExit && !inside && touching;
                Assert.That(generation, Is.EqualTo(expected ? 7 : 0));
                if (expected)
                    Assert.That(typeof(OnlineArcadeRunSystem).GetField("confirmedExitTarget", Flags).GetValue(runs),
                        Is.EqualTo(round < 4 ? 1 : arena.NextArenaNumbers[0]));
            }
            finally { runs.Deinit(); }
        }
        [Test] public void SecondArenaForestSnapshotUsesArmoryParametersAndAuthorityFade()
        {
            Set("activeArena", catalog.Arenas.Single(a => a.ArenaNumber == 3));
            Apply(new OnlineArcadeBattleSnapshot(2, 3, 4, 0, 0, 2, 1, 0, Battle(1, true).Players, null,
                arenaConfigNumber: 3, treasureRoomId: "101:1:arcade.armory.second", treasureDiscovered: true,
                treasureOpen: true, treasureSeconds: 40, treasureRewardIds: new[] {1,2,3,4,5,6}, treasureRewardSeconds: 1.5f));
            Assert.That(view.RoomId, Is.EqualTo("arcade.armory.second"));
            Assert.That(view.RoomRewards.Length, Is.EqualTo(6));
            Assert.That(view.RewardOpacity, Is.EqualTo(.5f));
        }
        [Test] public void InsideHidesArenaEntitiesAndOutsideTeammate()
        { Apply(Battle(1, true, false, 1, 2)); Assert.That(view.Enemies, Is.Empty); Assert.That(view.Projectiles, Is.Empty); Assert.That(view.Drops, Is.Empty); Assert.That(view.RemotePlayers, Is.Empty); Assert.That(view.RoomRewards, Is.EqualTo(new[] { 1, 2 })); }
        [Test] public void GroundOpacityUsesAuthorityAndRepeatedSnapshotDoesNotResetIt()
        {
            Apply(Battle(1, false));
            Assert.That(view.DropOpacities.Values.Single(), Is.EqualTo(.5f).Within(.001f));
            Apply(Battle(1, false));
            Assert.That(view.DropOpacities.Values.Single(), Is.EqualTo(.5f).Within(.001f));
            Apply(Battle(2, true, false, 1));
            Assert.That(view.RewardOpacity, Is.EqualTo(2f / 3f).Within(.001f));
        }
        [Test] public void ArenaHidesRoomTeammate()
        { Apply(Battle(1, false, true, 1, 2)); Assert.That(view.Enemies.Count, Is.EqualTo(1)); Assert.That(view.RemotePlayers, Is.Empty); Assert.That(view.InRoom, Is.False); }
        [Test] public void TeammateVisibleThenReturnRebuildsArena()
        { Apply(Battle(1, true, true, 1)); Assert.That(view.RemotePlayers, Does.Contain(2L)); Apply(Battle(2, false, true)); Assert.That(view.RemotePlayers, Is.Empty); Assert.That(view.Enemies.Count, Is.EqualTo(1)); Assert.That(view.Width, Is.EqualTo(catalog.Rule.ArenaHalfWidth)); }
        [Test] public void EnterChangesBoundsAndResetsPrediction()
        { Apply(Battle(1, false)); Set("predictedLocalPosition", new Vector2(8, 6)); Apply(Battle(2, true, false, 1)); Assert.That(view.Width, Is.EqualTo(9)); Assert.That(view.PlayerPosition, Is.EqualTo(new Vector2(0, -4))); }
        [Test] public void StaleFrameCannotRestoreRewardOrRegion()
        { Apply(Battle(5, true, false, 2)); int begins = view.Begins; Apply(Battle(4, false, false, 1, 2)); Assert.That(view.InRoom, Is.True); Assert.That(view.RoomRewards, Is.EqualTo(new[] { 2 })); Assert.That(view.Begins, Is.EqualTo(begins)); }
        [Test] public void RewardSnapshotCopiesSource()
        { var ids = new List<int> { 1, 2 }; var b = new OnlineArcadeBattleSnapshot(1, 3, 4, 0, 0, 2, 1, 0, null, null, treasureRewardIds: ids); ids.Clear(); Assert.That(b.TreasureRewardIds.Count, Is.EqualTo(2)); }
        [Test] public void EndResetsRevisionAndRegion()
        { Apply(Battle(5, true)); typeof(OnlineArcadePresentationSystem).GetMethod("EndPresentation", Flags).Invoke(system, null); Assert.That(Get<long>("lastAppliedBattleRevision"), Is.EqualTo(-1)); Assert.That(Get<bool>("presentedInTreasureRoom"), Is.False); }
        [Test] public void StageNoticesUseAuthorityAndIgnoreRepeatedOrStaleSnapshots()
        {
            var players = Battle(1, false).Players;
            Apply(new OnlineArcadeBattleSnapshot(1, 1, 4, 0, 1, 2, 1, 0, players, null, arenaConfigNumber: 1));
            Assert.That(system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out var notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveStarted));
            var cleared = new OnlineArcadeBattleSnapshot(1, 1, 4, 1, 1, 2, 2, 0, players, null, arenaConfigNumber: 1);
            Apply(cleared);
            Assert.That(system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveCompleted));
            Apply(cleared);
            Apply(new OnlineArcadeBattleSnapshot(1, 1, 4, 0, 1, 2, 1, 0, players, null, arenaConfigNumber: 1));
            Assert.That(system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out _), Is.False);
            Apply(new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 2, 3, 0, players, null, arenaConfigNumber: 1, awaitingExit: true));
            Assert.That(system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.ArenaCompleted));
        }
        [Test] public void ThirdWaveClearIsAnnouncedWhilePlayerEntersTreasureRoom()
        {
            Apply(new OnlineArcadeBattleSnapshot(1, 3, 4, 0, 2, 2, 1, 0, Battle(1, false).Players, null, arenaConfigNumber: 1));
            system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out _);
            Apply(new OnlineArcadeBattleSnapshot(1, 3, 4, 2, 2, 2, 2, 0, Battle(1, true).Players, null,
                arenaConfigNumber: 1, treasureRoomId: "101:1:1", treasureDiscovered: true, treasureOpen: true, treasureSeconds: 60));
            Assert.That(system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out var notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveCompleted));
            Assert.That(notice.WaveNumber, Is.EqualTo(3));
        }
        [Test] public void ThirdWaveWithoutTreasureInstanceUsesNormalClearSnapshot()
        {
            Apply(new OnlineArcadeBattleSnapshot(1, 3, 4, 0, 1, 2, 1, 0, Battle(1, false).Players, null, arenaConfigNumber: 1));
            system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out _);
            Apply(new OnlineArcadeBattleSnapshot(1, 3, 4, 1, 1, 2, 2, 0, Battle(1, false).Players, null, arenaConfigNumber: 1));
            Assert.That(system.StageNotices.TryConsume(Time.unscaledTimeAsDouble, out var notice), Is.True);
            Assert.That(notice.Kind, Is.EqualTo(ArcadeStageNoticeKind.WaveCompleted));
        }
        [Test] public void HudKeysInBothLanguages()
        { foreach (var lang in new[] { "zh-CN", "en-US" }) { var text = File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", lang + ".json")); Assert.That(text, Does.Contain("ui.arcade.hud.score")); Assert.That(text, Does.Contain("ui.arcade.hud.treasure")); } }
        [Test] public void OnlineClearedRoundsUseOneDestinationForAllFourDirections()
        {
            for (int round = 1; round <= 3; round++)
            {
                Apply(new OnlineArcadeBattleSnapshot(1, round, 4, 1, 1, 2, round, 0,
                    Battle(1, false).Players, null, arenaConfigNumber: 1, awaitingExit: true));
                Assert.That(view.ExitTargets, Is.EqualTo(new[] { -(round + 1) }), "普通轮出口显示下一轮而非当前主题");
            }
            Apply(new OnlineArcadeBattleSnapshot(1, 4, 4, 1, 1, 2, 4, 0,
                Battle(1, false).Players, null, arenaConfigNumber: 1, awaitingExit: true));
            Assert.That(view.ExitTargets, Is.EqualTo(new[] { 2 }));
        }
        /// <summary>外观随弹丸携带发射武器身份；不从玩家当前临时武器反推，敌弹与旧快照不借用火焰。</summary>
        [Test]
        public void OnlineProjectilePresentationUsesShotWeaponIdentity()
        {
            var sync = typeof(OnlineArcadePresentationSystem).GetMethod("SyncProjectiles", Flags);
            sync.Invoke(system, new object[] { new[] {
                new OnlineArcadeProjectileSnapshot(100, 1, 0, 0, 0, 8, "arcade.weapon.flamethrower"),
                new OnlineArcadeProjectileSnapshot(101, 1, 0, 0, 0, 16, "arcade.weapon.basic"),
                new OnlineArcadeProjectileSnapshot(102, 0, 0, 0, 0, 8, "arcade.weapon.flamethrower"),
                new OnlineArcadeProjectileSnapshot(103, 1, 0, 0, 0, 8)
            }});
            Assert.That(view.ProjectileVisuals.Values.Count(v => v == "ArcadeFlameBurst"), Is.EqualTo(1));
            Assert.That(view.ProjectileVisuals.Values.Count(v => v == "ArcadeBulletBasic"), Is.EqualTo(1));
            Assert.That(view.ProjectileVisuals.Values.Count(string.IsNullOrEmpty), Is.EqualTo(2));
            sync.Invoke(system, new object[] { new[] {
                new OnlineArcadeProjectileSnapshot(100, 1, 0, 1, 0, 8, "arcade.weapon.flamethrower")
            }});
            Assert.That(view.Projectiles.Count, Is.EqualTo(1));
            Assert.That(view.ProjectileVisuals[view.Projectiles.Single()], Is.EqualTo("ArcadeFlameBurst"));
        }

        [Test]
        public void OnlineEnemyFlamesUseAuthorityAndDoNotReplayDeaths()
        {
            var sync=typeof(OnlineArcadePresentationSystem).GetMethod("SyncEnemies",Flags);
            var enemies=new[]{new OnlineArcadeEnemySnapshot(10,"enemy.arcade.basic",0,0,10,20,1,1,flameHitRemainingSeconds:.65f),
                new OnlineArcadeEnemySnapshot(11,"enemy.arcade.basic",1,0,10,20,1,1,flameHitRemainingSeconds:.5f),
                new OnlineArcadeEnemySnapshot(12,"enemy.arcade.basic",2,0,10,20,1,1)};
            sync.Invoke(system,new object[]{enemies,false,Array.Empty<OnlineArcadeEnemyDeathSnapshot>(),3,true});
            Assert.That(view.EnemyFlames.Values,Does.Contain(.65f));
            var deaths=new[]{new OnlineArcadeEnemyDeathSnapshot(10,0,1,false,3,true),
                new OnlineArcadeEnemyDeathSnapshot(11,0,1,false,3,false),new OnlineArcadeEnemyDeathSnapshot(12,0,1,false,2,true)};
            sync.Invoke(system,new object[]{Array.Empty<OnlineArcadeEnemySnapshot>(),false,deaths,3,true});
            Assert.That(view.EnemyDeathKinds.Count,Is.EqualTo(2),"旧轮死亡不得补播");
            Assert.That(view.EnemyDeathKinds.Count(v=>v),Is.EqualTo(1),"残留燃烧不能冒充火焰最终击杀");
            sync.Invoke(system,new object[]{Array.Empty<OnlineArcadeEnemySnapshot>(),false,deaths,3,true});
            Assert.That(view.EnemyDeathKinds.Count,Is.EqualTo(2),"重复快照不能重播死亡火焰");
        }

        private sealed class TestView : IArcadeCombatViewPort
        {
            public readonly Dictionary<int, float> DropOpacities = new();
            public float RewardOpacity;
            public int[] ExitTargets;
            public void SetExits(IReadOnlyList<int> destinations, float halfHeight, bool victory) => ExitTargets = destinations.ToArray();
            public void SetDropOpacity(int id, float opacity) => DropOpacities[id] = opacity;
            public void SetTreasureRewardOpacity(float opacity) => RewardOpacity = opacity;
            public readonly HashSet<int> Enemies = new(), Projectiles = new(), Drops = new(); public readonly HashSet<long> RemotePlayers = new();
            public readonly Dictionary<int, string> ProjectileVisuals = new();
            public readonly Dictionary<int,float> EnemyFlames=new();
            public readonly List<bool> EnemyDeathKinds=new();
            public int[] RoomRewards; public bool InRoom; public float Width; public int Begins; public Vector2 PlayerPosition;
            public bool IsActive => true;
            public void Begin(int number, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { Begins++; Width = width; Enemies.Clear(); Projectiles.Clear(); Drops.Clear(); RemotePlayers.Clear(); }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable) { PlayerPosition = position; }
            public void SetPlayerHealth(int current, int maximum) { }
            public string RoomId;
            public void SetTreasureRoom(FirepowerLoop.Arcade.ArcadeTreasureRoomParameters p, bool inRoom, bool discovered, bool open, IReadOnlyList<int> ids) { InRoom = inRoom; RoomId = p?.Id; RoomRewards = ids.ToArray(); }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { Enemies.Add(id); }
            public void RemoveEnemy(int id, bool playDeath = false) { Enemies.Remove(id); }
            public void SetEnemyBurning(int id,float seconds){EnemyFlames[id]=seconds;}
            public void RemoveEnemy(int id,bool playDeath,Vector2 direction,bool explosive,bool flameKill)
            {Enemies.Remove(id);if(playDeath)EnemyDeathKinds.Add(flameKill);}
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { Projectiles.Add(id); }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction, string visualResourceName)
            { Projectiles.Add(id); ProjectileVisuals[id] = hostile ? null : visualResourceName; }
            public void RemoveProjectile(int id) { Projectiles.Remove(id); }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { Drops.Add(id); }
            public void RemoveDrop(int id) { Drops.Remove(id); }
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible, bool invulnerable, int current, int maximum) { RemotePlayers.Add(id); }
            public void RemoveRemotePlayer(long id) { RemotePlayers.Remove(id); }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}
