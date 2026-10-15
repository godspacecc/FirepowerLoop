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
                new[] { new OnlineArcadeDropSnapshot(12, catalog.Drops[0].Id, 1, 1) }, arenaConfigNumber: 1,
                treasureRoomId: "101:1:1", treasureDiscovered: true, treasureOpen: true, treasureSeconds: 40, treasureRewardIds: rewards);
        }
        private void Apply(OnlineArcadeBattleSnapshot battle)
        {
            Set("latest", new OnlineArcadeRunClientSnapshot(true, 101, 202, 1, OnlineArcadeRunPhase.Arena, 0, 1, "test",
                new[] { new OnlineArcadePlayerSnapshot(1, "one", 3, 0, 2, 2, "arcade.weapon.basic", false, null), new OnlineArcadePlayerSnapshot(2, "two", 3, 0, 2, 2, "arcade.weapon.basic", false, null) }, OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success, battle));
            typeof(OnlineArcadePresentationSystem).GetMethod("ApplyAuthoritativeSnapshot", Flags).Invoke(system, new object[] { battle });
        }
        [Test] public void InsideHidesArenaEntitiesAndOutsideTeammate()
        { Apply(Battle(1, true, false, 1, 2)); Assert.That(view.Enemies, Is.Empty); Assert.That(view.Projectiles, Is.Empty); Assert.That(view.Drops, Is.Empty); Assert.That(view.RemotePlayers, Is.Empty); Assert.That(view.RoomRewards, Is.EqualTo(new[] { 1, 2 })); }
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
        [Test] public void HudKeysInBothLanguages()
        { foreach (var lang in new[] { "zh-CN", "en-US" }) { var text = File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", lang + ".json")); Assert.That(text, Does.Contain("ui.arcade.hud.score")); Assert.That(text, Does.Contain("ui.arcade.hud.treasure")); } }
        private sealed class TestView : IArcadeCombatViewPort
        {
            public readonly HashSet<int> Enemies = new(), Projectiles = new(), Drops = new(); public readonly HashSet<long> RemotePlayers = new();
            public int[] RoomRewards; public bool InRoom; public float Width; public int Begins; public Vector2 PlayerPosition;
            public bool IsActive => true;
            public void Begin(int number, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { Begins++; Width = width; Enemies.Clear(); Projectiles.Clear(); Drops.Clear(); RemotePlayers.Clear(); }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable) { PlayerPosition = position; }
            public void SetPlayerHealth(int current, int maximum) { }
            public void SetTreasureRoom(AnotherDay.Arcade.ArcadeTreasureRoomParameters p, bool inRoom, bool discovered, bool open, IReadOnlyList<int> ids) { InRoom = inRoom; RoomRewards = ids.ToArray(); }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { Enemies.Add(id); }
            public void RemoveEnemy(int id, bool playDeath = false) { Enemies.Remove(id); }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { Projectiles.Add(id); }
            public void RemoveProjectile(int id) { Projectiles.Remove(id); }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { Drops.Add(id); }
            public void RemoveDrop(int id) { Drops.Remove(id); }
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible, bool invulnerable, int current, int maximum) { RemotePlayers.Add(id); }
            public void RemoveRemotePlayer(long id) { RemotePlayers.Remove(id); }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}
