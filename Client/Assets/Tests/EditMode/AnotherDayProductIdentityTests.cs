using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class AnotherDayProductIdentityTests
    {
        [Test]
        public void ProductSettingsUseAnotherDayPrototypeIdentity()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var source = File.ReadAllText(Path.Combine(projectRoot, "ProjectSettings/ProjectSettings.asset"));

            Assert.That(source, Does.Contain("companyName: AnotherDay"));
            Assert.That(source, Does.Contain("productName: Another Day"));
            Assert.That(source, Does.Contain("Android: com.anotherday.game"));
            Assert.That(source, Does.Contain("iPhone: com.anotherday.game"));
        }

        [Test]
        public void NewSaveDefaultsToAnotherDayBase()
        {
            Assert.That(MapIds.Base, Is.Not.Empty);
        }
    }
}
