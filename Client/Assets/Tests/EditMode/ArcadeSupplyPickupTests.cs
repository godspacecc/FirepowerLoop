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
    /// <summary>经过真实单人拾取循环验证满额拒绝与一次结算；只使用内存现场，不写存档。</summary>
    public sealed class ArcadeSupplyPickupTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private TestView view;

        [SetUp]
        public void Setup()
        {
            architecture = new Game();
            var run = new ArcadeRunModel();
            architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = "supply-test", RemainingLives = 3,
                Shop = new ArcadeShopSaveData { ArenaNumber = 1 } });
            run.EnterArena();
            system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime { ArenaNumber = 1, PlayerHealth = 100, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            view = new TestView();
            typeof(ArcadeCombatSystem).GetField("catalog", Private).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("arena", Private).SetValue(system, catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("view", Private).SetValue(system, view);
        }

        private void Call(string method, params object[] args) =>
            typeof(ArcadeCombatSystem).GetMethod(method, Private).Invoke(system, args);
        private ArcadeDropDefinition Supply(bool bomb) => catalog.Drops.First(d =>
            d.Kind == (bomb ? ArcadeDropKind.Bomb : ArcadeDropKind.Dash));
        private int Held(bool bomb) => bomb ? runtime.Bombs : runtime.Dashes;
        private void SetHeld(bool bomb, int amount) { if (bomb) runtime.Bombs = amount; else runtime.Dashes = amount; }
        private void Add(int id, ArcadeDropDefinition definition) => runtime.Drops.Add(id,
            new ArcadeDropRuntime { Id = id, Definition = definition, Position = Vector2.zero });

        [TestCase(true)] [TestCase(false)]
        public void FullSupply_RemainsAcrossRepeatedRangeEntries_ThenConsumedOnceAfterUse(bool bomb)
        {
            var supply = Supply(bomb);
            SetHeld(bomb, supply.MaxOwned);
            Add(1, supply);
            var original = runtime.Drops[1];
            for (int i = 0; i < 3; i++)
            {
                runtime.PlayerPosition = Vector2.zero; Call("TickDrops", runtime);
                runtime.PlayerPosition = new Vector2(20, 20); Call("TickDrops", runtime);
            }
            Assert.That(runtime.Drops.ContainsKey(1), Is.True, "满额经过不得移除补给");
            Assert.That(runtime.Drops[1], Is.SameAs(original));
            Assert.That(Held(bomb), Is.EqualTo(supply.MaxOwned));
            Assert.That(view.Removed, Is.Empty);
            Assert.That(view.Added, Is.Empty);
            Call(bomb ? "UseBomb" : "UseDash", runtime);
            Assert.That(Held(bomb), Is.EqualTo(supply.MaxOwned - 1));
            runtime.PlayerPosition = Vector2.zero;
            // 成功反馈的同步重复通知也不能重新结算刚移除的实例。
            view.OnPickup = () => Call("TickDrops", runtime);
            Call("TickDrops", runtime); Call("TickDrops", runtime);
            Assert.That(Held(bomb), Is.EqualTo(supply.MaxOwned));
            Assert.That(runtime.Drops, Is.Empty);
            Assert.That(view.Removed, Is.EqualTo(new[] { 1 }));
            Assert.That(view.Added, Is.EqualTo(new[] { 1 }));
        }

        [TestCase(true)] [TestCase(false)]
        public void PartialSupply_FeedbackUsesActualIncrease(bool bomb)
        {
            var source = Supply(bomb);
            var supply = new ArcadeDropDefinition(source.Id, source.MinArena, source.MaxArena, source.Weight,
                source.Kind, source.ProductId, source.VisualPrefabAddress, 3, source.DurationSeconds, source.MaxOwned);
            SetHeld(bomb, supply.MaxOwned - 1); Add(1, supply);
            Call("TickDrops", runtime);
            Assert.That(Held(bomb), Is.EqualTo(supply.MaxOwned));
            Assert.That(view.Added, Is.EqualTo(new[] { 1 }));
            Assert.That(view.Removed, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void RejectedSupply_DoesNotBlockGoldWeaponOrOtherSupply()
        {
            var bomb = Supply(true); runtime.Bombs = bomb.MaxOwned; runtime.Dashes = 0;
            var gold = catalog.Drops.First(d => d.Kind == ArcadeDropKind.RunGold);
            var weapon = catalog.Drops.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
            Add(1, bomb); Add(2, gold); Add(3, weapon); Add(4, Supply(false));
            Call("TickDrops", runtime);
            Assert.That(runtime.Drops.Keys, Is.EqualTo(new[] { 1 }));
            Assert.That(runtime.ArenaGold, Is.EqualTo(gold.Amount));
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo(weapon.ProductId));
            Assert.That(runtime.Dashes, Is.EqualTo(Supply(false).Amount));
            Assert.That(view.Removed, Is.EquivalentTo(new[] { 2, 3, 4 }));
        }

        /// <summary>协议先拷贝成只读快照；历史基线、队友拾取和重复通知不能冒充本机新奖励。</summary>
        [Test]
        public void OnlineReceipt_UsesActualAmountAndDoesNotReplayHistoryOrDuplicateNotifications()
        {
            var presentation = new OnlineArcadePresentationSystem();
            ((ICanSetArchitecture)presentation).SetArchitecture(architecture);
            typeof(OnlineArcadePresentationSystem).GetField("view", Private).SetValue(presentation, view);
            typeof(OnlineArcadePresentationSystem).GetField("catalog", Private).SetValue(presentation, catalog);
            var method = typeof(OnlineArcadePresentationSystem).GetMethod("SyncSupplyPickups", Private);
            Assert.That(method, Is.Not.Null, "联机反馈必须消费权威拾取记录");
            var history = ReceiptSnapshot(1, 1, 2);
            method.Invoke(presentation, new object[] { history, 1L, true });
            Assert.That(view.Added, Is.Empty, "首次进入只建立历史基线");
            var own = ReceiptSnapshot(2, 1, 1);
            method.Invoke(presentation, new object[] { own, 1L, false });
            method.Invoke(presentation, new object[] { own, 1L, false });
            method.Invoke(presentation, new object[] { ReceiptSnapshot(3, 2, 3), 1L, false });
            method.Invoke(presentation, new object[] { history, 1L, false });
            Assert.That(view.Added, Is.EqualTo(new[] { 1 }));
        }

        private object ReceiptSnapshot(long id, long player, int added)
        {
            using var source = ET.OnlineArcadeBattleInfo.Create();
            var receipt = ET.OnlineArcadeDropInfo.Create();
            receipt.RuntimeId = id; receipt.DropConfigId = Supply(true).Id;
            receipt.CollectedByPlayerId = player; receipt.AddedAmount = added;
            source.SupplyPickups.Add(receipt);
            var snapshot = typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { source });
            var property = snapshot.GetType().GetProperty("SupplyPickups");
            Assert.That(property, Is.Not.Null);
            return property.GetValue(snapshot);
        }

        private sealed class TestView : IArcadeCombatViewPort
        {
            public readonly List<int> Removed = new();
            public readonly List<int> Added = new();
            public Action OnPickup;
            public bool IsActive => true;
            public void PlaySupplyPickup(ArcadeDropDefinition definition, int added)
            { Added.Add(added); OnPickup?.Invoke(); }
            public void Begin(int number, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable) { }
            public void SetPlayerHealth(int current, int maximum) { }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum,
                bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { }
            public void RemoveDrop(int id) => Removed.Add(id);
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible,
                bool invulnerable, int current, int maximum) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}
