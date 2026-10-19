using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证资源包布娃娃的宿主保留门禁；不执行打包，不替代真机验收。</summary>
    public sealed class RagdollHostBuildTests
    {
        [Test] public void ProjectPreservationPassesRagdollGuard()
        { Assert.DoesNotThrow(()=>Validate(File.ReadAllText("Assets/Boot/TerrainPreservation/link.xml"))); }

        [TestCase("<linker/>")]
        [TestCase("<linker><assembly fullname='UnityEngine.PhysicsModule'><type fullname='UnityEngine.Rigidbody' preserve='all'/></assembly></linker>")]
        [TestCase("<linker><assembly fullname='UnityEngine.PhysicsModule' preserve='nothing'/></linker>")]
        public void GuardRejectsIncompletePhysicsPreservation(string xml)
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Validate(xml));
            Assert.That(error.InnerException.GetType().Name,Is.EqualTo("BuildFailedException"));
            Assert.That(error.InnerException.Message,Does.Contain("UnityEngine.PhysicsModule"));
        }

        [Test] public void GuardAcceptsCompletePhysicsModule()
        { Assert.DoesNotThrow(()=>Validate("<linker><assembly fullname='UnityEngine.PhysicsModule' preserve='all'/></linker>")); }

        [Test] public void GuardRejectsMalformedXml()
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Validate("<linker>"));
            Assert.That(error.InnerException.GetType().Name,Is.EqualTo("BuildFailedException"));
            Assert.That(error.InnerException.Message,Does.Contain("无效"));
        }

        private static void Validate(string xml)
        {
            var type=Type.GetType("Game.Editor.RagdollHostBuildGuard, Assembly-CSharp-Editor");
            Assert.That(type,Is.Not.Null,"宿主必须检查资源包布娃娃的原生物理组件保留规则");
            type.GetMethod("ValidatePreservation",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{xml});
        }
    }
}
