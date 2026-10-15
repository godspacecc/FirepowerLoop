using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class ArcadeRunSystemTests
    {
        private string runtimeRoot;
        private Game architecture;
        private SaveSystem save;
        private ArcadeRunSystem arcade;

        [SetUp]
        public void SetUp()
        {
            runtimeRoot = Path.Combine(Path.GetTempPath(), "AnotherDayArcade", Guid.NewGuid().ToString("N"));
            TestReset.ResetArchitecture();
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new ArcadeRewardWalletModel());
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new ClothingEquipmentModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(LoadTables));
            architecture.RegisterSystem(new SaveSystem());
            // 基地恢复现在校验唯一手持来源；街机夹具也须注册正式保存依赖。
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            architecture.RegisterSystem(new ClothingEquipmentSystem());
            architecture.RegisterSystem(new ArcadeRunSystem());
            save = architecture.GetSystem<SaveSystem>();
            arcade = architecture.GetSystem<ArcadeRunSystem>();
            save.ConfigureForTests(runtimeRoot);
            arcade.ConfigureForTests(runtimeRoot);
            Assert.That(save.RegisterSection(architecture.GetModel<PlayerBagModel>(), out var bagError), Is.True, bagError);
            Assert.That(save.CreateBase(out var baseData, out var error), Is.True, error);
            Assert.That(save.CaptureBaseSections(baseData, out error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
            if (Directory.Exists(runtimeRoot)) Directory.Delete(runtimeRoot, true);
        }

        [Test]
        public void TransitCompletionSaveFailureKeepsRouteAndRetryPreservesExactResourcesWithoutShop()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var initial,out var error),Is.True,error);
            Assert.That(arcade.TryContinue(out _,out error),Is.True,error);
            var model=architecture.GetModel<ArcadeRunModel>();
            model.SetTransientLives(2);model.SetTransientTreasureScore(new AnotherDay.Arcade.ArcadeTreasureScoreState(201000,2,4,2));
            var completion=new ArcadeArenaCompletion{ArenaNumber=1,NextArenaConfigNumber=2,Score=201000,ScoreMultiplier=2,MultiplierProgress=4,
                NextShop=new ArcadeShopSaveData{ArenaNumber=2},EquipmentBySlot=initial.EquipmentBySlot,
                EmergencyResources=new Dictionary<string,int>{{ArcadeRunDefaults.BombResourceId,3},{ArcadeRunDefaults.DashResourceId,4}}};
            var store=new ArcadeRunFileStore(runtimeRoot);
            using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                Assert.That(arcade.TryCompleteArena(completion,out _,out _),Is.False);
                Assert.That(model.Phase,Is.EqualTo(ArcadeRunPhase.Arena));Assert.That(model.Current.ArenaConfigNumber,Is.EqualTo(1));
            }
            Assert.That(arcade.TryCompleteArena(completion,out var saved,out error),Is.True,error);
            Assert.That(saved.ArenaConfigNumber,Is.EqualTo(2));Assert.That(saved.Shop.Offers,Is.Empty);
            Assert.That(saved.RemainingLives,Is.EqualTo(2));Assert.That(saved.Score,Is.EqualTo(201000));
            Assert.That(arcade.TryResume(out var restored,out error),Is.True,error);
            Assert.That(restored.ArenaConfigNumber,Is.EqualTo(2));Assert.That(restored.EmergencyResources[ArcadeRunDefaults.BombResourceId],Is.EqualTo(3));
            Assert.That(restored.EmergencyResources[ArcadeRunDefaults.DashResourceId],Is.EqualTo(4));
            Assert.That(restored.ScoreMultiplier,Is.EqualTo(2));Assert.That(restored.MultiplierProgress,Is.EqualTo(4));
        }

        [Test]
        public void NewRunUsesFixedIsolatedLoadoutAndWritesSafeNode()
        {
            Assert.That(arcade.TryStartNewRun(Shop(1), out var run, out var error), Is.True, error);

            Assert.That(run.RemainingLives, Is.EqualTo(3));
            Assert.That(run.RunGold, Is.Zero);
            Assert.That(run.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId], Is.EqualTo(ArcadeRunDefaults.BasicWeaponId));
            Assert.That(run.NextArenaNumber, Is.EqualTo(1));
            Assert.That(arcade.InspectPending().IsValid, Is.True);
        }

        [Test]
        public void ConfiguredRunUsesOpeningLoadoutAndTransitNodeWithoutShopOffers()
        {
            Assert.That(arcade.TryStartConfiguredRun(out var run, out var error), Is.True, error);

            Assert.That(run.RemainingLives, Is.EqualTo(3));
            Assert.That(run.EmergencyResources[ArcadeRunDefaults.BombResourceId], Is.EqualTo(1));
            Assert.That(run.EmergencyResources[ArcadeRunDefaults.DashResourceId], Is.EqualTo(2));
            Assert.That(run.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId], Is.EqualTo("arcade.weapon.rapid"));
            Assert.That(run.Shop.Offers, Is.Empty);
            Assert.That(run.Shop.Offers.All(value => !value.Purchased), Is.True);
        }

        [Test]
        public void GeneratedCatalogHasTwentyFourWaveArenasAndMobileBudgets()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);

            Assert.That(catalog.Arenas, Has.Count.EqualTo(20));
            Assert.That(catalog.Arenas, Has.All.Property("WaveGroupIds").Count.EqualTo(4));
            Assert.That(catalog.Arenas.Where(value => value.IsBoss).Select(value => value.ArenaNumber),
                Is.EqualTo(new[] { 4, 8, 12, 16, 20 }));
            Assert.That(catalog.Arenas[0].Obstacles, Is.Empty);
            Assert.That(catalog.Arenas[3].Obstacles, Is.Empty);
            Assert.That(catalog.Arenas[3].BossId, Is.EqualTo("arcade.boss.brute-charge"));
            Assert.That(catalog.Bosses, Has.Count.EqualTo(2));
            Assert.That(catalog.Bosses[0].EnemyGroupId, Is.EqualTo("arcade.group.boss.brute"));
            Assert.That(catalog.Bosses[0].ChargeSpeedMultiplier, Is.EqualTo(3.2f));
            Assert.That(catalog.Challenges, Has.Count.EqualTo(1));
            Assert.That(catalog.Challenges[0].EnemyGroupId, Is.EqualTo("arcade.group.rush.1"));
            Assert.That(catalog.Challenges[0].TimeLimitSeconds, Is.EqualTo(45f));
            Assert.That(catalog.Rule.ConfigFingerprint, Is.EqualTo("arcade-20261012-v22-babylon-content"));
            Assert.That(catalog.Rule.MaxAliveEnemies, Is.EqualTo(24));
            Assert.That(catalog.Rule.MaxProjectiles, Is.EqualTo(80));
            Assert.That(catalog.Rule.MaxDrops, Is.EqualTo(18));
            Assert.That(catalog.Rule.PlayerMaxHealth, Is.EqualTo(100));
            Assert.That(catalog.Rule.ArenaHalfWidth, Is.EqualTo(19.5f));
            Assert.That(catalog.Rule.ArenaHalfHeight, Is.EqualTo(19.5f));
            Assert.That(catalog.Weapons.Where(value => value.Id.StartsWith("arcade.weapon.")).Select(value => value.Id), Is.EquivalentTo(new[]
            {
                "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.spread",
                "arcade.weapon.piercing", "arcade.weapon.rocket", "arcade.weapon.grenade",
                "arcade.weapon.raygun"
            }));
            var piercing = catalog.Weapons.Single(value => value.Id == "arcade.weapon.piercing");
            Assert.That(piercing.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Piercing));
            Assert.That(piercing.MaxHits, Is.EqualTo(5));
            var rocket = catalog.Weapons.Single(value => value.Id == "arcade.weapon.rocket");
            Assert.That(rocket.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Explosive));
            Assert.That(rocket.ExplosionRadius, Is.EqualTo(2.8f));
            Assert.That(rocket.HeldVisualResourceName, Is.EqualTo("ArcadePanzerschreck"));
            Assert.That(rocket.ProjectileVisualResourceName, Is.EqualTo("ArcadePanzerschreckRocket"));
            var grenade = catalog.Weapons.Single(value => value.Id == "arcade.weapon.grenade");
            Assert.That(grenade.Behavior, Is.EqualTo(ArcadeWeaponBehavior.TimedExplosive));
            Assert.That(grenade.ExplosionRadius, Is.EqualTo(3.2f));
            var raygun = catalog.Weapons.Single(value => value.Id == "arcade.weapon.raygun");
            Assert.That(raygun.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Explosive));
            Assert.That(raygun.ExplosionRadius, Is.EqualTo(1.35f));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.piercing"));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.rocket"));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.grenade"));
            Assert.That(catalog.Drops.Select(value => value.ProductId), Does.Contain("arcade.weapon.raygun"));
        }

        [Test]
        public void GeneratedArcadeMap_ReusesTaskSceneWithOnePlayerSpawn()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetMap(MapIds.Arcade, out var map, out var error), Is.True, error);
            Assert.That(map.SceneAddresses, Is.EqualTo(new[] { "Scenes/TaskSampleMap" }));
            Assert.That(provider.TryGetMapSpawns(MapIds.Arcade, out var spawns, out error), Is.True, error);
            Assert.That(spawns, Has.Count.EqualTo(1));
            Assert.That(spawns[0].ObjectType, Is.EqualTo(MapSpawnObjectType.Player));
        }

        [Test]
        public void ConfiguredRunPersistsInitialSafeNodeBeforeSceneEntry()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);

            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeShop));
            Assert.That(arcade.InspectPending().IsValid, Is.True);
            Assert.That(arcade.InspectPending().Data.CompletedArenaCount, Is.Zero);
            Assert.That(arcade.InspectPending().Data.NextArenaNumber, Is.EqualTo(1));
        }

        [Test]
        public void ConfiguredEntryFailureRollbackDeletesFalseResumeState()
        {
            Assert.That(arcade.TryStartConfiguredRun(out _, out var error), Is.True, error);

            ArcadeResumeUICtrl.RollbackNewRun(arcade, "模拟地图加载失败");
            Assert.That(arcade.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().HasRun, Is.False);
        }

        [Test]
        public void ArenaExitRestoresPreviousSafeNodeWithoutLifeCost()
        {
            Assert.That(arcade.TryStartNewRun(Shop(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryHandleDeath(out var afterDeath, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
            Assert.That(afterDeath.RemainingLives, Is.EqualTo(2));

            Assert.That(arcade.TryReturnToSafeNode(out var restored, out error), Is.True, error);

            Assert.That(restored.RemainingLives, Is.EqualTo(3));
            Assert.That(restored.RunGold, Is.Zero);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeShop));
        }

        [Test]
        public void CompletedArenaCommitsOnlyStableResultsAndNextShop()
        {
            Assert.That(arcade.TryStartNewRun(Shop(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            var completion = Completion(1, 25, 2, "reward.scrap", 4);

            Assert.That(arcade.TryCompleteArena(completion, out var safe, out error), Is.True, error);

            Assert.That(safe.CompletedArenaCount, Is.EqualTo(1));
            Assert.That(safe.NextArenaNumber, Is.EqualTo(2));
            Assert.That(safe.RunGold, Is.EqualTo(25));
            Assert.That(safe.PendingRewards[0].RewardId, Is.EqualTo("reward.scrap"));
            Assert.That(safe.Shop.ArenaNumber, Is.EqualTo(2));
        }

        [Test]
        public void PurchasePersistsImmediatelyAndResumeKeepsIdenticalOfferState()
        {
            var shop = Shop(1);
            shop.Offers.Add(new ArcadeShopOfferSaveData
            {
                OfferId = "offer.life",
                ProductId = "life",
                Kind = ArcadePurchaseKind.ExtraLife,
                Price = 10,
                Amount = 1
            });
            Assert.That(arcade.TryStartNewRun(shop, out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryCompleteArena(Completion(1, 10, 2, nextOffers: shop.Offers), out _, out error), Is.True, error);

            Assert.That(arcade.TryPurchase("offer.life", out var purchased, out error), Is.True, error);
            Assert.That(purchased.RemainingLives, Is.EqualTo(4));
            Assert.That(purchased.RunGold, Is.Zero);
            Assert.That(purchased.Shop.Offers[0].Purchased, Is.True);
            Assert.That(arcade.TryResume(out var restored, out error), Is.True, error);
            Assert.That(restored.Shop.Offers[0].Purchased, Is.True);
            Assert.That(restored.RemainingLives, Is.EqualTo(4));
        }

        [Test]
        public void LastLifeDeletesRunAndClearsAllSessionValue()
        {
            Assert.That(arcade.TryStartNewRun(Shop(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
            Assert.That(arcade.TryHandleDeath(out _, out error), Is.EqualTo(ArcadeDeathResult.Respawn), error);
            Assert.That(arcade.TryHandleDeath(out var snapshot, out error), Is.EqualTo(ArcadeDeathResult.RunFailed), error);

            Assert.That(snapshot, Is.Null);
            Assert.That(arcade.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().HasRun, Is.False);
        }

        [Test]
        public void SettlementDeleteFailureRetriesWithoutDuplicatingBaseReward()
        {
            Assert.That(arcade.TryStartNewRun(Shop(1), out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryCompleteArena(Completion(1, 0, 2, "reward.scrap", 5), out _, out error), Is.True, error);
            arcade.FailNextDeleteForTests();

            Assert.That(arcade.TrySettle(out var firstAlreadySettled, out error), Is.False);
            Assert.That(firstAlreadySettled, Is.False);
            Assert.That(Game.Interface.GetModel<ArcadeRewardWalletModel>().GetBalance("reward.scrap"), Is.EqualTo(5));
            Assert.That(arcade.InspectPending().IsValid, Is.True);

            Assert.That(arcade.TrySettle(out var retryAlreadySettled, out error), Is.True, error);
            Assert.That(retryAlreadySettled, Is.True);
            Assert.That(Game.Interface.GetModel<ArcadeRewardWalletModel>().GetBalance("reward.scrap"), Is.EqualTo(5));
            Assert.That(arcade.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        [Test]
        public void FailedPurchaseSaveDoesNotCommitGoldOrOffer()
        {
            var shop = Shop(1);
            shop.Offers.Add(new ArcadeShopOfferSaveData
            {
                OfferId = "offer.life",
                ProductId = "life",
                Kind = ArcadePurchaseKind.ExtraLife,
                Price = 10,
                Amount = 1
            });
            Assert.That(arcade.TryStartNewRun(shop, out _, out var error), Is.True, error);
            Assert.That(arcade.TryContinue(out _, out error), Is.True, error);
            Assert.That(arcade.TryCompleteArena(Completion(1, 10, 2, nextOffers: shop.Offers), out _, out error), Is.True, error);
            var backupPath = new ArcadeRunFileStore(runtimeRoot).BackupPath;
            if (File.Exists(backupPath)) File.Delete(backupPath);
            Directory.CreateDirectory(backupPath);

            Assert.That(arcade.TryPurchase("offer.life", out var unchanged, out error), Is.False);

            Assert.That(unchanged.RunGold, Is.EqualTo(10));
            Assert.That(unchanged.RemainingLives, Is.EqualTo(3));
            Assert.That(unchanged.Shop.Offers[0].Purchased, Is.False);
        }

        [Test]
        public void ChallengeAttemptPersistsAndCompletionDoesNotAdvanceArena()
        {
            ArcadeShopSaveData shop = Shop(2);
            shop.ChallengeId = "arcade.challenge.timed-clear";
            Assert.That(arcade.TryStartNewRun(shop, out _, out var error), Is.True, error);

            Assert.That(arcade.TryStartChallenge(out var entered, out error), Is.True, error);
            Assert.That(entered.Shop.ChallengeAttempted, Is.True);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.Challenge));

            var rewards = new[] { new ArcadeRewardSaveData { RewardId = "reward.arcade.token", Amount = 1 } };
            Assert.That(arcade.TryCompleteChallenge(true, rewards, out var completed, out error), Is.True, error);
            Assert.That(completed.NextArenaNumber, Is.EqualTo(1));
            Assert.That(completed.CompletedArenaCount, Is.Zero);
            Assert.That(completed.Shop.ChallengeAttempted, Is.True);
            Assert.That(completed.PendingRewards.Single().RewardId, Is.EqualTo("reward.arcade.token"));
            Assert.That(arcade.TryStartChallenge(out _, out _), Is.False);
        }

        [Test]
        public void FailedChallengeReturnsPersistedShopWithoutLifeCost()
        {
            ArcadeShopSaveData shop = Shop(2);
            shop.ChallengeId = "arcade.challenge.timed-clear";
            Assert.That(arcade.TryStartNewRun(shop, out _, out var error), Is.True, error);
            Assert.That(arcade.TryStartChallenge(out _, out error), Is.True, error);

            Assert.That(arcade.TryCompleteChallenge(false, Array.Empty<ArcadeRewardSaveData>(),
                out var restored, out error), Is.True, error);

            Assert.That(restored.RemainingLives, Is.EqualTo(3));
            Assert.That(restored.Shop.ChallengeAttempted, Is.True);
            Assert.That(restored.PendingRewards, Is.Empty);
            Assert.That(Game.Interface.GetModel<ArcadeRunModel>().Phase, Is.EqualTo(ArcadeRunPhase.SafeShop));
        }

        private static ArcadeShopSaveData Shop(int arenaNumber) => new ArcadeShopSaveData
        {
            ArenaNumber = arenaNumber,
            GenerationSeed = 12345 + arenaNumber,
            RefreshCount = 0
        };

        private static ArcadeArenaCompletion Completion(
            int arenaNumber,
            long gold,
            int nextArena,
            string rewardId = null,
            long rewardAmount = 0,
            IReadOnlyList<ArcadeShopOfferSaveData> nextOffers = null)
        {
            var shop = Shop(nextArena);
            if (nextOffers != null)
            {
                foreach (var offer in nextOffers) shop.Offers.Add(offer.Clone());
            }
            var rewards = new List<ArcadeRewardSaveData>();
            if (!string.IsNullOrWhiteSpace(rewardId)) rewards.Add(new ArcadeRewardSaveData { RewardId = rewardId, Amount = rewardAmount });
            return new ArcadeArenaCompletion
            {
                ArenaNumber = arenaNumber,
                EarnedGold = gold,
                EmergencyResources = new Dictionary<string, int>(),
                EquipmentBySlot = new Dictionary<string, string>
                {
                    [ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId
                },
                EarnedRewards = rewards,
                NextShop = shop
            };
        }

        private static global::Tables LoadTables()
        {
            return new global::Tables(name =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>($"Assets/Game/Config/{name}.bytes");
                Assert.That(asset, Is.Not.Null, name);
                return new ByteBuf(asset.bytes);
            });
        }

        /// <summary>模拟 Player 构建中战斗表现初始化失败，并将运行阶段同步退回初始安全节点。</summary>
        private sealed class RejectArcadeEntrySystem : AbstractSystem
        {
            protected override void OnInit()
            {
                this.RegisterEvent<ArcadeRunChangedEvent>(evt =>
                {
                    if (evt.Phase != ArcadeRunPhase.Arena) return;
                    this.GetSystem<ArcadeRunSystem>().TryReturnToSafeNode(out _, out _);
                });
            }
        }
    }
}
