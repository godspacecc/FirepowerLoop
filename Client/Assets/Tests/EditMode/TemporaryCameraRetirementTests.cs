using System;
using System.Reflection;
using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>退役临时相机效果，保留正式相机模式。</summary>
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

    }
}
