using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class TerrainHostBuildTests
    {
        [Test]
        public void HostKeepsTerrainRenderingAndCollisionWithoutGeneratedLinkFile()
        {
            var xml = new System.Xml.XmlDocument();
            var path = "Assets/Boot/TerrainPreservation/link.xml";
            Assert.That(File.Exists(path), Is.True, "需要不受 HybridCLR 生成覆盖的地形保留配置");
            xml.Load(path);
            foreach (var type in new[] { "Terrain", "TerrainData", "TerrainLayer" })
                Assert.That(xml.SelectSingleNode($"/linker/assembly[@fullname='UnityEngine.TerrainModule']/type[@fullname='UnityEngine.{type}' and @preserve='all']"), Is.Not.Null);
            Assert.That(xml.SelectSingleNode("/linker/assembly[@fullname='UnityEngine.TerrainPhysicsModule']/type[@fullname='UnityEngine.TerrainCollider' and @preserve='all']"), Is.Not.Null);
        }

        [Test]
        public void GuardRejectsRenderingOnlyPreservation()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => Validate(
                "<linker><assembly fullname='UnityEngine.TerrainModule' preserve='all'/></linker>"));
            Assert.That(exception.InnerException.Message, Does.Contain("TerrainCollider"));
        }

        [Test]
        public void GuardAcceptsCompleteModulePreservation()
        {
            Assert.DoesNotThrow(() => Validate(
                "<linker><assembly fullname='UnityEngine.TerrainModule' preserve='all'/>" +
                "<assembly fullname='UnityEngine.TerrainPhysicsModule' preserve='all'/></linker>"));
        }

        private static void Validate(string xml)
        {
            var type = Type.GetType("Game.Editor.TerrainHostBuildGuard, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            var method = type.GetMethod("ValidatePreservation", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { xml });
        }
    }
}
