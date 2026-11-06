using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ChallengeCombatTests
    {
        [Test]
        public void KillingEnemyRemovesAuthorityExactlyOnce()
        {
            var combat = new ChallengeCombatState(100);
            combat.Enemies.Add(1, new ChallengeCombatState.Enemy { Health = 100 });
            Assert.That(combat.DamageEnemy(1, 34), Is.False);
            Assert.That(combat.DamageEnemy(1, 34), Is.False);
            Assert.That(combat.DamageEnemy(1, 34), Is.True);
            Assert.That(combat.DamageEnemy(1, 34), Is.False);
            Assert.That(combat.Kills, Is.EqualTo(1));
            Assert.That(combat.Enemies, Is.Empty);
        }

        [Test]
        public void FinishedBattleAndInvalidDamageCannotChangeHealth()
        {
            var combat = new ChallengeCombatState(100);
            combat.Enemies.Add(1, new ChallengeCombatState.Enemy { Health = 100 });
            Assert.That(combat.DamageEnemy(1, -1), Is.False);
            combat.Finished = true;
            Assert.That(combat.DamageEnemy(1, 200), Is.False);
            Assert.That(combat.Enemies[1].Health, Is.EqualTo(100));
        }
    }
}
