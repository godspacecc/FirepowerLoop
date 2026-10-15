using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证导航宿主保留校验，防止地图热更资源依赖的导航类型和方法被裁剪。</summary>
    public sealed class NavigationHostBuildTests
    {
        [Test]
        public void ProjectPreservationPassesNavigationGuard()
        {
            Assert.DoesNotThrow(() => Validate(File.ReadAllText("Assets/Boot/TerrainPreservation/link.xml")));
        }

        [Test]
        public void GuardRejectsTerrainOnlyPreservation()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => Validate(
                "<linker><assembly fullname='UnityEngine.TerrainModule' preserve='all'/></linker>"));
            Assert.That(exception.InnerException.GetType().Name, Is.EqualTo("BuildFailedException"));
            Assert.That(exception.InnerException.Message, Does.Contain("UnityEngine.AIModule"));
        }

        [Test]
        public void GuardRejectsNavMeshDataOnlyPreservation()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => Validate(
                "<linker><assembly fullname='UnityEngine.AIModule'><type fullname='UnityEngine.AI.NavMeshData' preserve='all'/></assembly></linker>"));
            Assert.That(exception.InnerException.GetType().Name, Is.EqualTo("BuildFailedException"));
        }

        [Test]
        public void GuardAcceptsCompleteNavigationModule()
        {
            Assert.DoesNotThrow(() => Validate("<linker><assembly fullname='UnityEngine.AIModule' preserve='all'/></linker>"));
        }

        [Test]
        public void GuardRejectsMalformedXml()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => Validate("<linker>"));
            Assert.That(exception.InnerException.GetType().Name, Is.EqualTo("BuildFailedException"));
            Assert.That(exception.InnerException.Message, Does.Contain("无效"));
        }

        private static void Validate(string xml)
        {
            var type = Type.GetType("Game.Editor.NavigationHostBuildGuard, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "导航宿主保留校验器必须注册到 Editor 程序集");
            var method = type.GetMethod("ValidatePreservation", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { xml });
        }
    }
}
