using NUnit.Framework;
using QFramework;
using System.Linq;
using UnityEngine;

namespace Game.Tests
{
    public sealed class UnitSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
        }

        [Test]
        public void RegisterUpdateAndRemovePublishesExpectedEvents()
        {
            var added = 0;
            var changed = 0;
            var removed = 0;
            Game.Interface.RegisterEvent<UnitAddedEvent>(_ => added++);
            Game.Interface.RegisterEvent<UnitChangedEvent>(_ => changed++);
            Game.Interface.RegisterEvent<UnitRemovedEvent>(_ => removed++);

            var system = Game.Interface.GetSystem<UnitSystem>();
            var snapshot = new UnitSnapshot(1, 7, UnitType.Player, UnitSource.Player, "player", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true);

            Assert.IsTrue(system.TryRegister(snapshot, out var error), error);
            Assert.IsFalse(system.TryRegister(snapshot, out error));
            Assert.That(error, Does.Contain("已存在"));
            Assert.IsTrue(system.TryUpdateTransform(1, Vector3.forward, Quaternion.Euler(0f, 90f, 0f)));
            Assert.IsFalse(system.TryUpdateTransform(1, Vector3.forward, Quaternion.Euler(0f, 90f, 0f)));
            Assert.IsTrue(system.TrySetActive(1, false));
            Assert.IsTrue(system.Remove(1, UnitRemovedReason.Explicit));

            Assert.That(added, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(2));
            Assert.That(removed, Is.EqualTo(1));
        }

        [Test]
        public void AllocateDynamicIdSkipsRegisteredIdsAndPlayerId()
        {
            var system = Game.Interface.GetSystem<UnitSystem>();
            Assert.That(system.AllocateUnitId(), Is.EqualTo(1000));
            Assert.IsTrue(system.TryRegister(new UnitSnapshot(1001, 1, UnitType.Loot, UnitSource.RuntimeSpawned, "loot", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));
            Assert.That(system.AllocateUnitId(), Is.EqualTo(1002));
        }

        [Test]
        public void RejectsInvalidIdsAndRemoveMapInstanceOnlyRemovesMatchingSession()
        {
            var system = Game.Interface.GetSystem<UnitSystem>();

            Assert.IsFalse(system.TryRegister(new UnitSnapshot(0, 1, UnitType.Player, UnitSource.Player, "bad", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error));
            Assert.That(error, Does.Contain("UnitId"));
            Assert.IsFalse(system.TryRegister(new UnitSnapshot(2, 0, UnitType.Player, UnitSource.Player, "bad", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error));
            Assert.That(error, Does.Contain("MapInstanceId"));

            Assert.IsTrue(system.TryRegister(new UnitSnapshot(3, 10, UnitType.Loot, UnitSource.RuntimeSpawned, "a", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), error);
            Assert.IsTrue(system.TryRegister(new UnitSnapshot(4, 11, UnitType.Loot, UnitSource.RuntimeSpawned, "b", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), error);

            Assert.That(system.RemoveMapInstance(10, UnitRemovedReason.MapInstanceReleased), Is.EqualTo(1));
            Assert.IsFalse(system.TryGet(3, out _));
            Assert.IsTrue(system.TryGet(4, out _));
            Assert.IsFalse(system.Remove(3, UnitRemovedReason.Explicit));
        }

        [Test]
        public void TransformAndActiveChangesPublishExactMasks()
        {
            var masks = new System.Collections.Generic.List<UnitChangeMask>();
            Game.Interface.RegisterEvent<UnitChangedEvent>(evt => masks.Add(evt.ChangeMask));
            var system = Game.Interface.GetSystem<UnitSystem>();
            Assert.IsTrue(system.TryRegister(new UnitSnapshot(5, 12, UnitType.Enemy, UnitSource.MapPlaced, "enemy", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));

            Assert.IsTrue(system.TryUpdateTransform(5, Vector3.right, Quaternion.identity));
            Assert.IsTrue(system.TryUpdateTransform(5, Vector3.right, Quaternion.Euler(0f, 45f, 0f)));
            Assert.IsTrue(system.TryUpdateTransform(5, Vector3.forward, Quaternion.Euler(0f, 90f, 0f)));
            Assert.IsTrue(system.TrySetActive(5, false));

            Assert.That(masks[0], Is.EqualTo(UnitChangeMask.Position));
            Assert.That(masks[1], Is.EqualTo(UnitChangeMask.Rotation));
            Assert.That(masks[2], Is.EqualTo(UnitChangeMask.Position | UnitChangeMask.Rotation));
            Assert.That(masks[3], Is.EqualTo(UnitChangeMask.Active));
        }

        [Test]
        public void UnitSnapshotOnlyContainsPlannedPublicFields()
        {
            var fields = typeof(UnitSnapshot)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(fields, Is.EqualTo(new[]
            {
                "AuthorityMode",
                "ConfigId",
                "IsActive",
                "OwnerUnitId",
                "Position",
                "Rotation",
                "MapInstanceId",
                "UnitId",
                "UnitSource",
                "UnitType"
            }));
        }
    }
}
