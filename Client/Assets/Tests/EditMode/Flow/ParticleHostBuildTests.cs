using System;
using System.IO;
using System.Reflection;
using System.Xml;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证热更天气所需的宿主粒子保留规则；不执行打包，也不代表旧客户端已修复。</summary>
    public sealed class ParticleHostBuildTests
    {
        [Test]
        public void ProjectPreservesParticleModuleIncludingNestedEmitParams()
        {
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(File.ReadAllText("Assets/Boot/TerrainPreservation/link.xml"));
            Assert.That(document.SelectSingleNode(
                "/linker/assembly[@fullname='UnityEngine.ParticleSystemModule' and @preserve='all']"),
                Is.Not.Null, "热更天气需要保留粒子模块，包括 EmitParams 及各粒子模块结构体的属性。");
        }

        [Test]
        public void ProjectPreservationPassesParticleGuard()
        {
            Assert.DoesNotThrow(() => Validate(File.ReadAllText("Assets/Boot/TerrainPreservation/link.xml")));
        }

        [TestCase("<linker/>")]
        [TestCase("<linker><assembly fullname='UnityEngine.CoreModule' preserve='all'/></linker>")]
        [TestCase("<linker><assembly fullname='UnityEngine.ParticleSystemModule' preserve='nothing'/></linker>")]
        [TestCase("<linker><assembly fullname='UnityEngine.ParticleSystemModule'><type fullname='UnityEngine.ParticleSystem' preserve='all'/></assembly></linker>")]
        public void GuardRejectsIncompleteParticlePreservation(string xml)
        {
            var error = Assert.Throws<TargetInvocationException>(() => Validate(xml));
            Assert.That(error.InnerException.GetType().Name, Is.EqualTo("BuildFailedException"));
            Assert.That(error.InnerException.Message, Does.Contain("UnityEngine.ParticleSystemModule"));
        }

        [Test]
        public void GuardAcceptsCompleteParticleModule()
        {
            Assert.DoesNotThrow(() => Validate(
                "<linker><assembly fullname='UnityEngine.ParticleSystemModule' preserve='all'/></linker>"));
        }

        [Test]
        public void GuardRejectsMalformedXml()
        {
            var error = Assert.Throws<TargetInvocationException>(() => Validate("<linker>"));
            Assert.That(error.InnerException.GetType().Name, Is.EqualTo("BuildFailedException"));
            Assert.That(error.InnerException.Message, Does.Contain("无效"));
        }

        private static void Validate(string xml)
        {
            var type = Type.GetType("Game.Editor.ParticleHostBuildGuard, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "粒子宿主保留校验器必须注册到 Editor 程序集");
            var method = type.GetMethod("ValidatePreservation", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { xml });
        }
    }
}
