using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class FirepowerLoopProductIdentityTests
    {
        [Test]
        public void ProductSettingsUseFirepowerLoopIdentity()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var source = File.ReadAllText(Path.Combine(projectRoot, "ProjectSettings/ProjectSettings.asset"));

            Assert.That(source, Does.Contain("companyName: FirepowerLoop"));
            Assert.That(source, Does.Contain("productName: FirepowerLoop"));
            Assert.That(source, Does.Contain("Android: com.firepowerloop.game"));
            Assert.That(source, Does.Contain("iPhone: com.firepowerloop.game"));
        }

        [Test]
        public void ArcadeMapKeepsStableProjectIdentity()
        {
            Assert.That(MapIds.Arcade, Is.EqualTo("map.firepowerloop.arcade"));
        }
    }
}
