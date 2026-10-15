using NUnit.Framework;
using System.IO;
using System.Linq;
using Luban;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>锁定街机循环、成长、边界与散射的纯规则，避免调配置时破坏循环竞技场语义。</summary>
    public sealed class ArcadeCombatSystemTests
    {
        [TestCase(3f, 140f, 2.8f, 0f, true)]
        [TestCase(1.5f, 70f, 2f, 0f, false)]
        [TestCase(3f, 140f, 1f, 1.5f, true)]
        [TestCase(2.6f, 60f, 1f, 1.5f, false)]
        public void ConfiguredMeleeStrike_UsesWeaponRangeAndArc(float range, float arc,
            float x, float y, bool expected)
        {
            Assert.That(ArcadeCombatSystem.IsWithinMeleeStrike(Vector2.zero, Vector2.right,
                new Vector2(x, y), range, arc), Is.EqualTo(expected));
        }

        [Test]
        public void RetiredMeleeDefinitions_DoNotHaveGameplaySources()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            // 定义仅用于旧存档识别，不能再次进入掉落池或商品池。
            foreach (var weapon in catalog.Weapons.Where(value => value.Behavior == ArcadeWeaponBehavior.Melee))
            {
                Assert.That(catalog.Drops.Any(value => value.ProductId == weapon.Id), Is.False);
                Assert.That(catalog.ShopItems.Any(value => value.ProductId == weapon.Id), Is.False);
            }
        }

        [Test]
        public void SharedMeleeStrike_OnlyHitsTargetsInFrontAndWithinRange()
        {
            Assert.That(ArcadeCombatSystem.IsWithinMeleeStrike(Vector2.zero, Vector2.right,
                new Vector2(1.5f, 0.3f), 2.2f, 90f), Is.True);
            Assert.That(ArcadeCombatSystem.IsWithinMeleeStrike(Vector2.zero, Vector2.right,
                new Vector2(-1f, 0f), 2.2f, 90f), Is.False);
            Assert.That(ArcadeCombatSystem.IsWithinMeleeStrike(Vector2.zero, Vector2.right,
                new Vector2(3f, 0f), 2.2f, 90f), Is.False);
            Assert.That(ArcadeCombatSystem.IsWithinMeleeStrike(Vector2.zero, Vector2.right,
                new Vector2(1f, 1.5f), 2.2f, 90f), Is.False);
        }

        [Test]
        public void ArenaIndex_LoopsConfiguredArenasWithoutCreatingNewSceneData()
        {
            Assert.That(ArcadeCombatSystem.ResolveConfiguredArenaIndex(1, 20), Is.Zero);
            Assert.That(ArcadeCombatSystem.ResolveConfiguredArenaIndex(20, 20), Is.EqualTo(19));
            Assert.That(ArcadeCombatSystem.ResolveConfiguredArenaIndex(21, 20), Is.Zero);
            Assert.That(ArcadeCombatSystem.ResolveConfiguredArenaIndex(41, 20), Is.Zero);
        }

        [Test]
        public void PlayerHealthUpgrade_UsesPercentAndNeverFallsBelowOne()
        {
            Assert.That(ArcadeCombatSystem.ScalePlayerHealth(100, 0.30f), Is.EqualTo(130));
            Assert.That(ArcadeCombatSystem.ScalePlayerHealth(0, 0f), Is.EqualTo(1));
        }

        [Test]
        public void PlayerHealthFeedback_OnlyReportsVisibleLossAfterBaseline()
        {
            Assert.That(ArcadeCombatViewController.CalculateHealthLoss(false, 100, 75), Is.Zero);
            Assert.That(ArcadeCombatViewController.CalculateHealthLoss(true, 100, 75), Is.EqualTo(25));
            Assert.That(ArcadeCombatViewController.CalculateHealthLoss(true, 75, 100), Is.Zero);
            Assert.That(ArcadeCombatViewController.CalculateHealthLoss(true, 100, 75, false), Is.Zero);
        }

        [Test]
        public void GoldGrowth_UsesActualArenaNumberAcrossLoops()
        {
            Assert.That(ArcadeCombatSystem.ScaleGold(10, 1, 0.08f), Is.EqualTo(10));
            Assert.That(ArcadeCombatSystem.ScaleGold(10, 6, 0.08f), Is.EqualTo(14));
            Assert.That(ArcadeCombatSystem.ScaleGold(10, 21, 0.08f), Is.EqualTo(26));
        }

        [Test]
        public void ClampPosition_RespectsIndependentArenaAxes()
        {
            Assert.That(ArcadeCombatSystem.ClampPosition(new Vector2(20f, -20f), 12f, 8f),
                Is.EqualTo(new Vector2(12f, -8f)));
        }

        [Test]
        public void ObstacleResolution_PushesCircleToNearestFreeEdge()
        {
            var obstacles = new[] { new ArcadeObstacleDefinition(4f, 0f, 1f, 2f) };

            var resolved = ArcadeCombatSystem.ResolvePosition(new Vector2(4.2f, 0f), 0.5f, 12f, 8f, obstacles);

            Assert.That(resolved, Is.EqualTo(new Vector2(5.5f, 0f)));
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(resolved, 0.5f, obstacles), Is.False);
            Assert.That(ArcadeCombatSystem.IsInsideObstacle(new Vector2(4f, 0f), 0f, obstacles), Is.True);
        }

        [Test]
        public void EnemyNavigation_UsesDirectDirectionWhenRouteIsClear()
        {
            var direction = ArcadeCombatSystem.ResolveNavigationDirection(Vector2.zero, new Vector2(3f, 4f),
                0.5f, System.Array.Empty<ArcadeObstacleDefinition>());

            Assert.That(Vector2.Distance(direction, new Vector2(0.6f, 0.8f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void EnemyNavigation_SelectsVisibleCornerWhenObstacleBlocksTarget()
        {
            var obstacles = new[] { new ArcadeObstacleDefinition(0f, 0f, 1f, 2f) };

            var direction = ArcadeCombatSystem.ResolveNavigationDirection(new Vector2(-4f, 0f),
                new Vector2(4f, 0f), 0.5f, obstacles);

            Assert.That(direction.x, Is.GreaterThan(0f));
            Assert.That(Mathf.Abs(direction.y), Is.GreaterThan(0.1f));
        }

        [Test]
        public void EnemyNavigation_RecomputedStepsReachTargetWithoutEnteringObstacle()
        {
            var obstacles = new[] { new ArcadeObstacleDefinition(0f, 0f, 1f, 2f) };
            var position = new Vector2(-4f, 0f);
            var target = new Vector2(4f, 0f);
            for (var step = 0; step < 160 && Vector2.Distance(position, target) > 0.2f; step++)
            {
                var direction = ArcadeCombatSystem.ResolveNavigationDirection(position, target, 0.5f, obstacles);
                position = ArcadeCombatSystem.ResolvePosition(position + direction * 0.1f, 0.5f, 12f, 8f,
                    obstacles);
                Assert.That(ArcadeCombatSystem.IsInsideObstacle(position, 0.5f, obstacles), Is.False);
            }

            Assert.That(Vector2.Distance(position, target), Is.LessThanOrEqualTo(0.2f));
        }

        [Test]
        public void EnemyCrowding_SeparatesOverlappingPositionsInsideArena()
        {
            var positions = new System.Collections.Generic.List<Vector2>
            {
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.1f, 0f)
            };

            ArcadeCombatSystem.ResolveCrowding(positions, 0.5f, 12f, 8f,
                System.Array.Empty<ArcadeObstacleDefinition>());

            Assert.That(Vector2.Distance(positions[0], positions[1]), Is.GreaterThan(0.4f));
            Assert.That(positions, Has.All.Matches<Vector2>(value => Mathf.Abs(value.x) <= 11.5f && Mathf.Abs(value.y) <= 7.5f));
        }

        [Test]
        public void RewardTrigger_UsesStartAndInterval()
        {
            Assert.That(ArcadeCombatSystem.IsRewardTriggered(4, 4, 4), Is.True);
            Assert.That(ArcadeCombatSystem.IsRewardTriggered(8, 4, 4), Is.True);
            Assert.That(ArcadeCombatSystem.IsRewardTriggered(6, 4, 4), Is.False);
            Assert.That(ArcadeCombatSystem.IsRewardTriggered(3, 4, 4), Is.False);
        }

        [Test]
        public void FirstBoss_BindsOnlyToConfiguredFinalWaveGroup()
        {
            var group = new ArcadeEnemyGroupDefinition("arcade.group.boss.brute",
                new[] { new ArcadeEnemyMemberDefinition("enemy.graybox.basic", 1) },
                0.8f, 1, 8f, 1.7f, 0.92f, 35);
            var arena = new ArcadeArenaDefinition(4, new[] { "a", "b", "c", group.Id },
                System.Array.Empty<ArcadeObstacleDefinition>(), true, "arcade.boss.brute-charge", 32, 401);
            var bosses = new[]
            {
                new ArcadeBossDefinition("arcade.boss.brute-charge", group.Id,
                    4.5f, 0.9f, 0.75f, 3.2f, 1.15f, 1.8f)
            };

            Assert.That(ArcadeCombatSystem.TryResolveBoss(arena, group, bosses, out var boss), Is.True);
            Assert.That(boss.TelegraphSeconds, Is.EqualTo(0.9f));
            var otherGroup = new ArcadeEnemyGroupDefinition("arcade.group.rush.1",
                new[] { new ArcadeEnemyMemberDefinition("enemy.graybox.basic", 1) },
                1f, 1, 1f, 1f, 1f, 0);
            Assert.That(ArcadeCombatSystem.TryResolveBoss(arena, otherGroup, bosses, out _), Is.False);
        }

        [Test]
        public void BossChargeHit_UsesSweptSegmentInsteadOfOnlyEndPosition()
        {
            var player = new Vector2(2f, 0.4f);

            var distanceSquared = ArcadeCombatSystem.DistanceToSegmentSquared(player, Vector2.zero,
                new Vector2(4f, 0f));

            Assert.That(distanceSquared, Is.EqualTo(0.16f).Within(0.0001f));
            Assert.That(distanceSquared, Is.LessThan(1f));
        }

        [Test]
        public void SpreadAngles_AreSymmetricAndSingleShotFacesForward()
        {
            Assert.That(ArcadeCombatSystem.BuildSpreadAngles(1, 30f), Is.EqualTo(new[] { 0f }));
            Assert.That(ArcadeCombatSystem.BuildSpreadAngles(5, 28f),
                Is.EqualTo(new[] { -14f, -7f, 0f, 7f, 14f }));
        }

        [Test]
        public void PiercingProjectile_RegistersEachEnemyOnceAndConsumesHitBudget()
        {
            var projectile = new ArcadeProjectileRuntime
            {
                Behavior = ArcadeWeaponBehavior.Piercing,
                RemainingHits = 2
            };

            Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(projectile, 11), Is.True);
            Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(projectile, 11), Is.False);
            Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(projectile, 12), Is.True);
            Assert.That(projectile.RemainingHits, Is.Zero);
            Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(projectile, 13), Is.False);
        }

        [Test]
        public void ExplosionRadius_UsesConfiguredCircularBoundary()
        {
            Assert.That(ArcadeCombatSystem.IsWithinExplosion(Vector2.zero, new Vector2(3f, 4f), 5f), Is.True);
            Assert.That(ArcadeCombatSystem.IsWithinExplosion(Vector2.zero, new Vector2(3.01f, 4f), 5f), Is.False);
            Assert.That(ArcadeCombatSystem.IsWithinExplosion(Vector2.zero, Vector2.zero, 0f), Is.False);
        }

        [Test]
        public void TimedExplosive_DetonatesOnFuseWhileImpactExplosiveDoesNot()
        {
            Assert.That(ArcadeCombatSystem.ShouldExplodeOnRemoval(ArcadeWeaponBehavior.TimedExplosive, true, false),
                Is.True);
            Assert.That(ArcadeCombatSystem.ShouldExplodeOnRemoval(ArcadeWeaponBehavior.Explosive, true, false),
                Is.False);
            Assert.That(ArcadeCombatSystem.ShouldExplodeOnRemoval(ArcadeWeaponBehavior.Explosive, false, true),
                Is.True);
            Assert.That(ArcadeCombatSystem.ShouldExplodeOnRemoval(ArcadeWeaponBehavior.Direct, true, true),
                Is.False);
        }

        [Test]
        public void CompletionRetry_RequiresExplicitConsumptionAndCannotOverlapSubmission()
        {
            var runtime = new ArcadeCombatRuntime { CompletionAwaitingRetry = true };
            Assert.That(ArcadeCombatSystem.TryConsumeCompletionRetry(runtime), Is.True);
            Assert.That(runtime.CompletionAwaitingRetry, Is.False);
            Assert.That(ArcadeCombatSystem.TryConsumeCompletionRetry(runtime), Is.False);

            runtime.CompletionAwaitingRetry = true;
            runtime.CompletionSubmitted = true;
            Assert.That(ArcadeCombatSystem.TryConsumeCompletionRetry(runtime), Is.False);
        }

        [Test]
        public void PowerupMovementAndMagnet_CombineWithPermanentUpgradesWithoutShrinkingValues()
        {
            Assert.That(ArcadeCombatSystem.CalculatePlayerSpeed(6f, 0.2f, false, 0.35f),
                Is.EqualTo(9.72f).Within(0.0001f));
            Assert.That(ArcadeCombatSystem.CalculatePlayerSpeed(6f, 0.2f, true, 0.35f),
                Is.EqualTo(23.328f).Within(0.0001f));
            Assert.That(ArcadeCombatSystem.CalculatePickupRadius(1.1f, 0.5f, 5f), Is.EqualTo(5f));
            Assert.That(ArcadeCombatSystem.CalculatePickupRadius(1.1f, 0.5f, 1f),
                Is.EqualTo(1.65f).Within(0.0001f));
        }

        [Test]
        public void Shield_ConsumesConfiguredChargesAndRemovesExpiredState()
        {
            var runtime = new ArcadeCombatRuntime();
            runtime.Powerups.Add(ArcadePowerupBehavior.Shield, new ArcadePowerupRuntime
            {
                RemainingSeconds = 10f,
                Charges = 2,
            });

            Assert.That(ArcadeCombatSystem.TryConsumeShield(runtime), Is.True);
            Assert.That(runtime.Powerups[ArcadePowerupBehavior.Shield].Charges, Is.EqualTo(1));
            Assert.That(ArcadeCombatSystem.TryConsumeShield(runtime), Is.True);
            Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.Shield), Is.False);
            Assert.That(ArcadeCombatSystem.TryConsumeShield(runtime), Is.False);
        }

        [Test]
        public void RuntimePool_ReusesResetObjectsAndHonorsConfiguredCapacity()
        {
            var pool = new ArcadeCombatRuntimePool();
            pool.Configure(1, 1, 1);
            Assert.That(pool.EnemyCount, Is.EqualTo(1));
            Assert.That(pool.ProjectileCount, Is.EqualTo(1));
            Assert.That(pool.DropCount, Is.EqualTo(1));
            var enemy = pool.RentEnemy();
            var projectile = pool.RentProjectile();
            var drop = pool.RentDrop();
            enemy.Id = 7;
            enemy.ConfigId = "enemy_old";
            enemy.Health = 99;
            enemy.BossId = "stale-boss";
            enemy.BossPhase = ArcadeBossPhase.Charging;
            projectile.Id = 8;
            projectile.Damage = 12;
            projectile.RemainingSeconds = 3f;
            projectile.Behavior = ArcadeWeaponBehavior.Piercing;
            projectile.RemainingHits = 3;
            projectile.ExplosionRadius = 2f;
            projectile.HitEnemyIds.Add(7);
            drop.Id = 9;
            drop.Position = Vector2.one;

            pool.Return(enemy);
            pool.Return(projectile);
            pool.Return(drop);
            pool.Return(new ArcadeEnemyRuntime());
            pool.Return(new ArcadeProjectileRuntime());
            pool.Return(new ArcadeDropRuntime());

            Assert.That(pool.EnemyCount, Is.EqualTo(1));
            Assert.That(pool.ProjectileCount, Is.EqualTo(1));
            Assert.That(pool.DropCount, Is.EqualTo(1));
            var reusedEnemy = pool.RentEnemy();
            var reusedProjectile = pool.RentProjectile();
            var reusedDrop = pool.RentDrop();
            Assert.That(reusedEnemy, Is.SameAs(enemy));
            Assert.That(reusedEnemy.Id, Is.Zero);
            Assert.That(reusedEnemy.ConfigId, Is.Null);
            Assert.That(reusedEnemy.Health, Is.Zero);
            Assert.That(reusedEnemy.BossId, Is.Null);
            Assert.That(reusedEnemy.BossPhase, Is.EqualTo(ArcadeBossPhase.None));
            Assert.That(reusedProjectile, Is.SameAs(projectile));
            Assert.That(reusedProjectile.Id, Is.Zero);
            Assert.That(reusedProjectile.Damage, Is.Zero);
            Assert.That(reusedProjectile.RemainingSeconds, Is.Zero);
            Assert.That(reusedProjectile.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Direct));
            Assert.That(reusedProjectile.RemainingHits, Is.Zero);
            Assert.That(reusedProjectile.ExplosionRadius, Is.Zero);
            Assert.That(reusedProjectile.HitEnemyIds, Is.Empty);
            Assert.That(reusedDrop, Is.SameAs(drop));
            Assert.That(reusedDrop.Id, Is.Zero);
            Assert.That(reusedDrop.Position, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ArcadeMapContext_PreparesOnlyTheFormalPlayer()
        {
            var root = new GameObject("ArcadeMapContext");
            try
            {
                var mapContext = root.AddComponent<MapContext>();
                var context = MapPreparationContext.CreateArcade(MapIds.Arcade, 7, mapContext,
                    System.Array.Empty<MapSpawnDefinition>());
                Assert.That(context.Mode, Is.EqualTo(MapPreparationMode.ArcadePlayerOnly));
                Assert.That(context.MapId, Is.EqualTo(MapIds.Arcade));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

    }
}
