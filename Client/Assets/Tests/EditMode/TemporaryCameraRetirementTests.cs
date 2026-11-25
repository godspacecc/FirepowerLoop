using System;
using System.Reflection;
using System.IO;
using System.Linq;
using Luban;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>退役临时相机效果；旧局额外字段可忽略，普通射击释放闸门必须保留。</summary>
    public sealed class TemporaryCameraRetirementTests
    {
        [Test]
        public void FormalCatalogAndDropEnumExcludeTemporaryCamera()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(
                File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            Assert.That(catalog.Drops.Any(drop => drop.ProductId == "arcade.camera.first_person" || (int)drop.Kind == 7), Is.False);
            Assert.That(Enum.IsDefined(typeof(global::ArcadeDropKind), 7), Is.False);
            Assert.That(typeof(PlayerCameraSystem).GetMethod("TryBeginArcadeFirstPerson"), Is.Null);
            Assert.That(Enum.IsDefined(typeof(PlayerCameraMode), PlayerCameraMode.FirstPerson), Is.True);
        }

        [Test]
        public void OldCheckpointIgnoresRetiredCameraAndKeepsWeapon()
        {
            var value = JsonConvert.DeserializeObject<ArcadeIslandRoundCheckpoint>("{\"FirstPersonRemaining\":20,\"TemporaryWeaponRemaining\":12}");
            Assert.That(value.TemporaryWeaponRemaining, Is.EqualTo(12));
            Assert.That(JsonConvert.SerializeObject(value), Does.Not.Contain("FirstPersonRemaining"));
        }

    }
}
