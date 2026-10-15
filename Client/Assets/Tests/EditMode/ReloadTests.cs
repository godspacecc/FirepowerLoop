using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>指定验收入口；复用正式武器与敌人夹具执行行为回归。</summary>
    public sealed class ReloadTests
    {
        [Test]
        public void PlayerBowReload_IsManualPersistentAndAtomic()
        {
            Run(new WeaponInputAndMeleeTests(), fixture => fixture.ReloadInputPublishesInGameplayButBlockedUiDoesNotLeak());
            Run(new BowProjectileTests(), fixture => fixture.ReloadRejectsInvalidStatesAndCanceledCompletionNeverConsumesArrow());
            Run(new WeaponEquipmentTests(), fixture => fixture.RangedLoadStaysOnInventoryInstanceAndRiskLossDestroysIt());
            Run(new BowProjectileTests(), fixture => fixture.ArrowConsumptionIsAtomicAndCooldownStartsOnlyAfterSpawn());
        }

        [Test]
        public void ArcherReload_IsAutomaticVisibleAndPreservesTotalAmmoDrop()
        {
            Run(new EnemyArsenalAndDropTests(), fixture => fixture.ArcherReloadsOneRoundAutomaticallyAndCleansStateAtIdle());
            Run(new EnemyArsenalAndDropTests(), fixture => fixture.FailedProjectileDoesNotConsumeAmmoAndUsesCooldown());
            Run(new EnemyArsenalAndDropTests(), fixture => fixture.ArcherConsumesTenSuccessfulShotsThenFallsBackToMelee());
            Run(new EnemyArsenalAndDropTests(), fixture => fixture.ArcherDropsBowRemainingAmmoAndExistingExtraLoot());
            Run(new EnemyArsenalAndDropTests(), fixture => fixture.ZeroAmmoOmitsArrowLoot());
            Run(new EnemyArsenalAndDropTests(), fixture => fixture.ArcherPrefabProvidesReloadProgressPresentation());
        }

        [Test]
        public void RifleReload_FillsAcrossStacks()
        {
            Run(new BowProjectileTests(), fixture => fixture.RifleReloadFillsMagazineAcrossStacksAndPreservesRemainder());
        }

        [Test]
        public void RifleReload_ShortageAndCancellationAreAtomic()
        {
            Run(new BowProjectileTests(), fixture => fixture.RifleReloadUsesAvailableAmmoAndCancellationConsumesNothing());
        }

        [Test]
        public void RifleReload_OnlyConsumesMagazineGap()
        {
            Run(new BowProjectileTests(), fixture => fixture.RifleReloadOnlyConsumesMagazineGap());
        }

        private static void Run<TFixture>(TFixture fixture, System.Action<TFixture> verify)
            where TFixture : class
        {
            var type = fixture.GetType();
            type.GetMethod("SetUp")?.Invoke(fixture, null);
            try
            {
                verify(fixture);
            }
            finally
            {
                type.GetMethod("TearDown")?.Invoke(fixture, null);
            }
        }
    }
}
