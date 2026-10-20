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
                Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 1 } });
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

        /// <summary>满额拒收不延长地面寿命；等待出口仍在30秒移除且不补发任何奖励。</summary>
        [Test]
        public void GroundSupply_FadesDuringLastThreeSecondsThenExpiresWithoutReward()
        {
            BeginExitWait();
            var supply = Supply(true);
            SetHeld(true, supply.MaxOwned);
            Add(1, supply);
            for (int i = 0; i < 108; i++) Call("Tick", runtime, .25f);
            Assert.That(runtime.Drops.Count, Is.EqualTo(1));
            Assert.That(view.Opacity.ContainsKey(1), Is.True, "地面表现须同步权威透明度");
            Assert.That(view.Opacity[1], Is.EqualTo(1f).Within(.001f));
            for (int i = 0; i < 6; i++) Call("Tick", runtime, .25f);
            Assert.That(view.Opacity[1], Is.EqualTo(.5f).Within(.001f));
            for (int i = 0; i < 6; i++) Call("Tick", runtime, .25f);
            Assert.That(runtime.Drops, Is.Empty);
            Assert.That(view.Removed, Is.EqualTo(new[] { 1 }));
            Assert.That(Held(true), Is.EqualTo(supply.MaxOwned));
            Assert.That(view.Added, Is.Empty);
        }

        [Test]
        public void GroundSupply_FadingPickupStillAppliesExactlyOnce()
        {
            BeginExitWait();
            Add(1, Supply(true));
            runtime.PlayerPosition = new Vector2(10, 10);
            Call("TickTimers", runtime, 28.5f);
            runtime.PlayerPosition = Vector2.zero;
            Call("TickDrops", runtime);
            Call("TickDrops", runtime);
            Assert.That(runtime.Bombs, Is.EqualTo(Supply(true).Amount));
            Assert.That(runtime.Drops, Is.Empty);
            Assert.That(view.Added.Count, Is.EqualTo(1));
        }

        [Test]
        public void GroundSupply_PauseFreezesLifetimeAndExpiryReleasesBudget()
        {
            BeginExitWait();
            var supply = Supply(true);
            SetHeld(true, supply.MaxOwned);
            Add(1, supply);
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Paused;
            for (int i = 0; i < 160; i++) Call("Tick", runtime, .25f);
            Assert.That(runtime.Drops.Count, Is.EqualTo(1));
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Call("Tick", runtime, 30f);
            Assert.That(runtime.Drops, Is.Empty);
            Add(2, supply);
            Call("Tick", runtime, 27f);
            Assert.That(runtime.Drops.ContainsKey(2), Is.True, "新实体不可继承已过期实体的计时");
        }

        /// <summary>从完整帧入口验证出口等待不会绕过拾取，并保留满额与一次结算规则。</summary>
        [TestCase(true)] [TestCase(false)]
        public void AwaitingExit_FullSupplyRemainsThenAvailableSupplyIsCollectedOnce(bool bomb)
        {
            BeginExitWait();
            var supply = Supply(bomb);
            SetHeld(bomb, supply.MaxOwned);
            Add(1, supply);
            Call("Tick", runtime, .02f);
            Assert.That(runtime.Drops.ContainsKey(1), Is.True);
            Assert.That(Held(bomb), Is.EqualTo(supply.MaxOwned));
            Assert.That(view.Added, Is.Empty);

            SetHeld(bomb, supply.MaxOwned - 1);
            Call("Tick", runtime, .02f);
            Call("Tick", runtime, .02f);
            Assert.That(runtime.AwaitingExit, Is.True);
            Assert.That(runtime.CompletionSubmitted, Is.False);
            Assert.That(runtime.Drops, Is.Empty, "等待出口时仍须领取范围内补给");
            Assert.That(Held(bomb), Is.EqualTo(supply.MaxOwned));
            Assert.That(view.Removed, Is.EqualTo(new[] { 1 }));
            Assert.That(view.Added, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void AwaitingExit_RejectedSupplyDoesNotBlockGoldOrWeapon()
        {
            BeginExitWait();
            var bomb = Supply(true);
            var gold = catalog.Drops.First(d => d.Kind == ArcadeDropKind.RunGold);
            var weapon = catalog.Drops.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
            runtime.Bombs = bomb.MaxOwned;
            Add(1, bomb); Add(2, gold); Add(3, weapon);
            Call("Tick", runtime, .02f);
            Call("Tick", runtime, .02f);
            Assert.That(runtime.Drops.Keys, Is.EqualTo(new[] { 1 }));
            Assert.That(runtime.ArenaGold, Is.EqualTo(gold.Amount));
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo(weapon.ProductId));
            Assert.That(view.Removed, Is.EquivalentTo(new[] { 2, 3 }));
        }

        /// <summary>搭建已清场但尚未踏入出口的现场，使用正式 Tick 而非直接调用拾取函数。</summary>
        private void BeginExitWait()
        {
            architecture.RegisterModel(new AppStateModel());
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            runtime.AwaitingExit = true;
            runtime.WaveClearAwarded = true;
            runtime.TreasureInitialized = true;
            runtime.WaveTransitionRemaining = 300f;
        }

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

        /// <summary>倍率后的真实积分应单独反馈，不能把面值或积分当作金币。</summary>
        [Test]
        public void CasualTreasure_ReportsActualMultipliedScoreWithoutGold()
        {
            var run = architecture.GetModel<ArcadeRunModel>();
            var data = run.Current; data.IsCasualRoute = true; data.ScoreMultiplier = 3;
            run.CommitSafeNode(data); run.EnterArena();
            Add(1, catalog.Drops.First(d => d.Kind == ArcadeDropKind.RunGold));
            var pickupPosition = new Vector2(.7f, -.4f);
            runtime.Drops[1].Position = pickupPosition;
            Call("TickDrops", runtime);
            Assert.That(run.Current.Score, Is.EqualTo(300));
            Assert.That(runtime.ArenaGold, Is.Zero);
            Assert.That(view.Treasure, Is.EqualTo(new[] { (300L, 0L) }));
            Assert.That(view.TreasurePositions, Is.EqualTo(new[] { pickupPosition }), "归还运行对象池前须保留财宝坐标，不能用玩家脚下代替");
        }

        [Test]
        public void GoldAndProducts_OnlyReportSuccessfulApplications()
        {
            Add(1, catalog.Drops.First(d => d.Kind == ArcadeDropKind.RunGold));
            var weapon = catalog.Drops.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
            var powerup = catalog.Drops.First(d => d.Kind == ArcadeDropKind.Powerup);
            Add(2, weapon); Add(3, powerup);
            Call("TickDrops", runtime); Call("TickDrops", runtime);
            Assert.That(view.Treasure, Is.EqualTo(new[] { (0L, 100L) }));
            Assert.That(view.Products, Is.EqualTo(new[] { weapon.ProductId, powerup.ProductId }));
        }

        [Test]
        public void InvalidProductAndOverflowReward_RemainWithoutSuccessFeedback()
        {
            var weapon = catalog.Drops.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
            Add(1, new ArcadeDropDefinition(weapon.Id, 1, 0, 1, weapon.Kind, "missing-product", weapon.VisualPrefabAddress,
                1, 10, 1));
            runtime.ArenaGold = long.MaxValue;
            Add(2, catalog.Drops.First(d => d.Kind == ArcadeDropKind.RunGold));
            Call("TickDrops", runtime);
            Assert.That(runtime.Drops.Count, Is.EqualTo(2));
            Assert.That(view.Removed, Is.Empty);
            Assert.That(view.Products, Is.Empty);
            Assert.That(view.Treasure, Is.Empty);
        }

        [Test]
        public void OnlineReceipt_ReportsScoreAndGoldSeparatelyAndProductsOnce()
        {
            var presentation = new OnlineArcadePresentationSystem();
            ((ICanSetArchitecture)presentation).SetArchitecture(architecture);
            typeof(OnlineArcadePresentationSystem).GetField("view", Private).SetValue(presentation, view);
            typeof(OnlineArcadePresentationSystem).GetField("catalog", Private).SetValue(presentation, catalog);
            var weapon = catalog.Drops.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
            var receipts = new[] {
                new OnlineArcadeDropSnapshot(10, "", 7, -4, 1, 0, 600, 2),
                new OnlineArcadeDropSnapshot(11, weapon.Id, 0, 0, 1),
                new OnlineArcadeDropSnapshot(12, weapon.Id, 0, 0, 2) };
            var method = typeof(OnlineArcadePresentationSystem).GetMethod("SyncSupplyPickups", Private);
            method.Invoke(presentation, new object[] { receipts, 1L, false });
            method.Invoke(presentation, new object[] { receipts, 1L, false });
            Assert.That(view.Treasure.Where(x => x != (0L, 0L)), Is.EqualTo(new[] { (600L, 2L) }));
            Assert.That(view.TreasurePositions, Is.EqualTo(new[] { new Vector2(7, -4) }), "联机回执必须保留远处财宝位置且只反馈一次");
            Assert.That(view.Products, Is.EqualTo(new[] { weapon.ProductId }));
        }

        private sealed class TestView : IArcadeCombatViewPort
        {
            public readonly Dictionary<int, float> Opacity = new();
            public void SetDropOpacity(int id, float opacity) => Opacity[id] = opacity;
            public readonly List<int> Removed = new();
            public readonly List<int> Added = new();
            public readonly List<(long, long)> Treasure = new();
            public readonly List<Vector2> TreasurePositions = new();
            public readonly List<string> Products = new();
            public void PlayTreasurePickup(long score, long gold) { if (score > 0 || gold > 0) Treasure.Add((score, gold)); }
            public void PlayTreasurePickup(long score, long gold, Vector2 position)
            { if (score > 0 || gold > 0) TreasurePositions.Add(position); PlayTreasurePickup(score, gold); }
            public void PlayProductPickup(string productId) => Products.Add(productId);
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
