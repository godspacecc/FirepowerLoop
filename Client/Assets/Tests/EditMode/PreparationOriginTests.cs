using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class PreparationOriginTests
    {
        private const string Journey="0123456789abcdef0123456789abcdef";
        private static void Mark(ItemStack stack,int count)
        {
            var method=typeof(ItemStack).GetMethod("MarkExploration",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null,"缺少可守恒的探索来源数量");method.Invoke(stack,new object[]{Journey,count});
        }
        private static int Acquired(ItemStack stack)=>(int)typeof(ItemStack).GetProperty("AcquiredAmount").GetValue(stack);
        [Test] public void ConsumingBaseUnitsKeepsOnlyRemainingExploredUnitsEligible()
        {
            var container=new ItemContainer(2);container.TryAdd(4,8,10,out _,out _);container.TryGet(0,out var stack);Mark(stack,3);
            container.TryRemove(0,6,out var removed,out _);
            Assert.That(Acquired(removed),Is.EqualTo(1));container.TryGet(0,out stack);Assert.That(Acquired(stack),Is.EqualTo(2));
        }
        [Test] public void SplittingAndMergingCannotCreateExploredQuantity()
        {
            var container=new ItemContainer(2);container.TryAdd(4,8,10,out _,out _);container.TryGet(0,out var stack);Mark(stack,3);
            container.TrySplitHalf(0,1,out _);container.TryGet(1,out var split);Assert.That(Acquired(split),Is.EqualTo(0));
            container.TryMoveWithin(1,0,10,out _);container.TryGet(0,out stack);Assert.That(stack.Amount,Is.EqualTo(8));Assert.That(Acquired(stack),Is.EqualTo(3));
        }
        [Test] public void TransactionClonesPreserveProvenanceAndFreshDepartureClearsIt()
        {
            var stack=new ItemStack(5,1);Mark(stack,1);var clone=stack.Clone();Assert.That(Acquired(clone),Is.EqualTo(1));
            typeof(ItemStack).GetMethod("ClearExplorationOrigin",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(clone,null);
            Assert.That(Acquired(clone),Is.Zero);Assert.That(Acquired(stack),Is.EqualTo(1));
        }
    }
}
