using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>独立挑战弹药事务；数值仅为手算测试夹具，不代表原作枪械参数。</summary>
    public sealed class ChallengeAmmoTests
    {
        private static object NewWeapon(int magazine = 3, int reserve = 5)
        {
            var type = typeof(SurvivorModel).Assembly.GetType("Game.ChallengeWeaponState");
            Assert.That(type, Is.Not.Null, "独立挑战弹药规则尚未实现");
            return Activator.CreateInstance(type, "test", magazine, reserve, 2f, .1f);
        }
        private static object Call(object value, string method, params object[] args) =>
            value.GetType().GetMethod(method).Invoke(value, args);
        private static T Read<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
        private static void Shot(object value) { Assert.That(Call(value,"TryFire"), Is.True); Call(value,"Advance",.1f); }

        [Test] public void ReloadDebitsReserveOnlyWhenCompleted()
        {
            var gun=NewWeapon(); Shot(gun); Shot(gun);
            Assert.That(Call(gun,"TryReload"),Is.True);
            Call(gun,"Advance",1f);
            Assert.That(Read<int>(gun,"Magazine"),Is.EqualTo(1));
            Assert.That(Read<int>(gun,"Reserve"),Is.EqualTo(5));
            Assert.That(Call(gun,"TryFire"),Is.False);
            Assert.That(Call(gun,"TryReload"),Is.False);
            Call(gun,"Advance",1f);
            Assert.That(Read<int>(gun,"Magazine"),Is.EqualTo(3));
            Assert.That(Read<int>(gun,"Reserve"),Is.EqualTo(3));
        }

        [Test] public void InsufficientReserveCannotCreateBullets()
        {
            var gun=NewWeapon(3,1); Shot(gun); Shot(gun); Shot(gun);
            Assert.That(Call(gun,"TryFire"),Is.False);
            Assert.That(Read<float>(gun,"ReloadRemaining"),Is.EqualTo(2));
            Call(gun,"Advance",2f);
            Assert.That(Read<int>(gun,"Magazine"),Is.EqualTo(1));
            Assert.That(Read<int>(gun,"Reserve"),Is.Zero);
            Assert.That(Call(gun,"TryReload"),Is.False);
        }

        [Test] public void CancelReloadPreservesBothAmmoPools()
        {
            var gun=NewWeapon(); Shot(gun); Call(gun,"TryReload"); Call(gun,"Advance",1f);
            Call(gun,"CancelReload"); Call(gun,"Advance",5f);
            Assert.That(Read<int>(gun,"Magazine"),Is.EqualTo(2));
            Assert.That(Read<int>(gun,"Reserve"),Is.EqualTo(5));
            Assert.That(Read<float>(gun,"ReloadRemaining"),Is.Zero);
        }

        [Test] public void SupplyIsCappedWithoutFillingMagazine()
        {
            var gun=NewWeapon(); Shot(gun); Call(gun,"TryReload"); Call(gun,"Advance",2f); Shot(gun);
            Assert.That(Call(gun,"Supply",int.MaxValue),Is.EqualTo(1));
            Assert.That(Read<int>(gun,"Reserve"),Is.EqualTo(5));
            Assert.That(Read<int>(gun,"Magazine"),Is.EqualTo(2));
            Assert.That(Call(gun,"Supply",-1),Is.EqualTo(0));
        }

        [Test] public void FireCooldownAndInvalidElapsedCannotSpendExtraAmmo()
        {
            var gun=NewWeapon(); Assert.That(Call(gun,"TryFire"),Is.True);
            Assert.That(Call(gun,"TryFire"),Is.False);
            Call(gun,"Advance",float.NaN); Call(gun,"Advance",float.PositiveInfinity); Call(gun,"Advance",-1f);
            Assert.That(Call(gun,"TryFire"),Is.False);
            Assert.That(Read<int>(gun,"Magazine"),Is.EqualTo(2));
        }

        [Test] public void InvalidCapacityIsRejected()
        {
            var type=typeof(SurvivorModel).Assembly.GetType("Game.ChallengeWeaponState");
            Assert.That(type,Is.Not.Null);
            var error=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(type,"test",0,5,2f,.1f));
            Assert.That(error.InnerException,Is.TypeOf<ArgumentException>());
        }
    }
}
