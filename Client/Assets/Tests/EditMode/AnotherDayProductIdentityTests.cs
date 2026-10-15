using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class AnotherDayProductIdentityTests
    {
        [Test]
        public void ProductSettingsUseFirepowerLoopIdentity()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var source = File.ReadAllText(Path.Combine(projectRoot, "ProjectSettings/ProjectSettings.asset"));

            Assert.That(source, Does.Contain("companyName: AnotherDay"));
            Assert.That(source, Does.Contain("productName: FirepowerLoop"));
            Assert.That(source, Does.Contain("Android: com.anotherday.firepowerloop"));
            Assert.That(source, Does.Contain("iPhone: com.anotherday.firepowerloop"));
        }

        [Test]
        public void ArcadeMapKeepsStableProjectIdentity()
        {
            Assert.That(MapIds.Arcade, Is.EqualTo("map.another-day.arcade"));
        }
    }
}
