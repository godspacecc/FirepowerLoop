using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class RedPointTests
    {
        [SetUp]
        public void SetUp() => ResetArchitecture();

        [TearDown]
        public void TearDown() => ResetArchitecture();

        [Test]
        public void DefaultStartIdsAreStableAndOnlyRootModulesCanBeUnregistered()
        {
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();
            Assert.That(RedPointIds.Start, Is.EqualTo("start"));
            Assert.That(RedPointIds.StartResourceUpdate, Is.EqualTo("start.resource-update"));
            Assert.IsFalse(redPoints.Unregister(RedPointIds.StartResourceUpdate));
            Assert.That(redPoints.GetState(RedPointIds.Start).Id, Is.EqualTo(RedPointIds.Start));
        }

        [Test]
        public void RegisterRefreshesCompleteTreeAndAggregatesLeafAtFrameEnd()
        {
            var leaf = new TestLeafMod("test.leaf", true, 2);
            var root = new TestGroupMod("test.root", leaf);
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();

            Assert.IsTrue(redPoints.Register(root));
            Assert.IsFalse(redPoints.GetState(root.Id).Active);
            LifeCycle.LateUpdate();

            AssertState(redPoints.GetState(leaf.Id), leaf.Id, true, 2);
            AssertState(redPoints.GetState(root.Id), root.Id, true, 2);
        }

        [Test]
        public void MultipleChangesInOneFrameUseLatestProjectionAndDoNotRepeatEvents()
        {
            var leaf = new TestLeafMod("batch.leaf", false, 0);
            var root = new TestGroupMod("batch.root", leaf);
            var events = new List<RedPointChangedEvent>();
            Game.Interface.RegisterEvent<RedPointChangedEvent>(events.Add);
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();
            Assert.IsTrue(redPoints.Register(root));
            LifeCycle.LateUpdate();
            events.Clear();

            leaf.SetAndNotify(true, 1);
            leaf.SetAndNotify(true, 4);
            leaf.SetAndNotify(true, 3);
            LifeCycle.LateUpdate();
            LifeCycle.LateUpdate();

            Assert.That(redPoints.GetState(leaf.Id).Count, Is.EqualTo(3));
            Assert.That(redPoints.GetState(root.Id).Count, Is.EqualTo(3));
            Assert.That(events.FindAll(e => e.Id == leaf.Id), Has.Count.EqualTo(1));
            Assert.That(events.FindAll(e => e.Id == root.Id), Has.Count.EqualTo(1));
        }

        [Test]
        public void RejectingChildUnregisterKeepsTreeRegisteredAndRefreshable()
        {
            var leaf = new TestLeafMod("child.reject.leaf", false, 0);
            var root = new TestGroupMod("child.reject.root", leaf);
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();
            Assert.IsTrue(redPoints.Register(root));
            LifeCycle.LateUpdate();

            Assert.IsFalse(redPoints.Unregister(leaf.Id));
            leaf.Prepare(true, 1);
            Game.Interface.SendEvent(new TestDomainChangedEvent());
            LifeCycle.LateUpdate();

            AssertState(redPoints.GetState(leaf.Id), leaf.Id, true, 1);
            AssertState(redPoints.GetState(root.Id), root.Id, true, 1);
            Assert.That(leaf.DomainEventCount, Is.EqualTo(1));
            Assert.That(leaf.DetachCount, Is.EqualTo(0));
        }

        [Test]
        public void UnregisterInactiveTreeDoesNotEmitRedundantEventsAndReleasesTrackedSubscription()
        {
            var leaf = new TestLeafMod("inactive.leaf", false, 0);
            var root = new TestGroupMod("inactive.root", leaf);
            var events = new List<RedPointChangedEvent>();
            Game.Interface.RegisterEvent<RedPointChangedEvent>(events.Add);
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();
            Assert.IsTrue(redPoints.Register(root));
            LifeCycle.LateUpdate();
            events.Clear();

            Assert.IsTrue(redPoints.Unregister(root.Id));
            leaf.Prepare(true, 1);
            Game.Interface.SendEvent(new TestDomainChangedEvent());
            LifeCycle.LateUpdate();

            Assert.That(events, Is.Empty);
            Assert.That(leaf.DomainEventCount, Is.EqualTo(0));
            Assert.That(leaf.DetachCount, Is.EqualTo(1));
            AssertState(redPoints.GetState(leaf.Id), leaf.Id, false, 0);
            AssertState(redPoints.GetState(root.Id), root.Id, false, 0);
        }

        [Test]
        public void UnregisterActiveTreeDetachesOnceAndEmitsRequiredOffEvents()
        {
            var leaf = new TestLeafMod("detach.leaf", true, 1);
            var root = new TestGroupMod("detach.root", leaf);
            var events = new List<RedPointChangedEvent>();
            Game.Interface.RegisterEvent<RedPointChangedEvent>(events.Add);
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();
            Assert.IsTrue(redPoints.Register(root));
            LifeCycle.LateUpdate();
            events.Clear();

            Assert.IsTrue(redPoints.Unregister(root.Id));
            Assert.That(leaf.DetachCount, Is.EqualTo(1));
            Assert.That(root.DetachCount, Is.EqualTo(1));
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].Id, Is.EqualTo(leaf.Id));
            Assert.That(events[1].Id, Is.EqualTo(root.Id));
            Assert.That(events.TrueForAll(e => !e.Active && e.Count == 0), Is.True);
        }

        [Test]
        public void FailedAttachRollsBackAllModulesEvenWhenDetachThrows()
        {
            var leaf = new TestLeafMod("rollback.leaf", false, 0)
            {
                ThrowOnAttach = true,
                ThrowOnDetach = true
            };
            var root = new TestGroupMod("rollback.root", leaf);
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();

            var error = Assert.Throws<InvalidOperationException>(() => redPoints.Register(root));
            Assert.That(error.Message, Is.EqualTo("测试附加异常"));
            Game.Interface.SendEvent(new TestDomainChangedEvent());
            LifeCycle.LateUpdate();

            Assert.That(root.DetachCount, Is.EqualTo(1));
            Assert.That(leaf.DetachCount, Is.EqualTo(1));
            Assert.That(leaf.DomainEventCount, Is.EqualTo(0));
            AssertState(redPoints.GetState(root.Id), root.Id, false, 0);
            AssertState(redPoints.GetState(leaf.Id), leaf.Id, false, 0);
        }

        [Test]
        public void SystemDeinitIsolatesDetachErrorsAndLateFrameCallbackIsSafe()
        {
            var throwingLeaf = new TestLeafMod("deinit.throw", false, 0) { ThrowOnDetach = true };
            var normalLeaf = new TestLeafMod("deinit.normal", false, 0);
            var root = new TestGroupMod("deinit.root", throwingLeaf, normalLeaf);
            var architecture = Game.Interface;
            var redPoints = Game.Interface.GetSystem<RedPointSystem>();
            Assert.IsTrue(redPoints.Register(root));
            throwingLeaf.SetAndNotify(true, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"\[RedPointSystem\] 红点模块释放失败，模块 ID：deinit\.throw"));

            Assert.DoesNotThrow(() => architecture.Deinit());
            LifeCycle.LateUpdate();

            Assert.That(throwingLeaf.DetachCount, Is.EqualTo(1));
            Assert.That(normalLeaf.DetachCount, Is.EqualTo(1));
            Assert.That(root.DetachCount, Is.EqualTo(1));
            AssertState(redPoints.GetState(throwingLeaf.Id), throwingLeaf.Id, false, 0);
            AssertState(redPoints.GetState(normalLeaf.Id), normalLeaf.Id, false, 0);
            AssertState(redPoints.GetState(root.Id), root.Id, false, 0);
        }

        private static void AssertState(RedPointState state, string id, bool active, int count)
        {
            Assert.That(state.Id, Is.EqualTo(id));
            Assert.That(state.Active, Is.EqualTo(active));
            Assert.That(state.Count, Is.EqualTo(count));
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private readonly struct TestDomainChangedEvent
        {
        }

        private sealed class TestLeafMod : RedPointMod
        {
            private bool active;
            private int count;

            public TestLeafMod(string id, bool active, int count) : base(id)
            {
                this.active = active;
                this.count = count;
            }

            public bool ThrowOnAttach { get; set; }
            public bool ThrowOnDetach { get; set; }
            public int DetachCount { get; private set; }
            public int DomainEventCount { get; private set; }

            public void Prepare(bool nextActive, int nextCount)
            {
                active = nextActive;
                count = nextCount;
            }

            public void SetAndNotify(bool nextActive, int nextCount)
            {
                Prepare(nextActive, nextCount);
                NotifyChange();
            }

            protected override void OnAttach()
            {
                Track(this.RegisterEvent<TestDomainChangedEvent>(_ =>
                {
                    DomainEventCount++;
                    NotifyChange();
                }));
                if (ThrowOnAttach)
                {
                    throw new InvalidOperationException("测试附加异常");
                }
            }

            protected override void OnDetach()
            {
                DetachCount++;
                if (ThrowOnDetach)
                {
                    throw new InvalidOperationException("测试释放异常");
                }
            }

            protected override bool OnCheckActive() => active;
            protected override int OnGetCount() => count;
        }

        private sealed class TestGroupMod : RedPointMod
        {
            private readonly IReadOnlyList<RedPointMod> children;

            public TestGroupMod(string id, params RedPointMod[] children) : base(id)
            {
                this.children = Array.AsReadOnly(children);
            }

            public int DetachCount { get; private set; }
            public override IReadOnlyList<RedPointMod> ChildModules => children;
            protected override void OnDetach() => DetachCount++;
        }
    }
}
