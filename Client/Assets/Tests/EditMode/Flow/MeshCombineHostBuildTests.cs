using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>只验证宿主网格合并保留规则，不启动任何打包流程。</summary>
    public sealed class MeshCombineHostBuildTests
    {
        [Test] public void ProjectPreservationPassesMeshCombineGuard()
        { Assert.DoesNotThrow(()=>Validate(File.ReadAllText("Assets/Boot/TerrainPreservation/link.xml"))); }

        [Test] public void GuardRejectsMissingCombineInstance()
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Validate("<linker><assembly fullname='UnityEngine.CoreModule'><type fullname='UnityEngine.Mesh' preserve='all'/></assembly></linker>"));
            Assert.That(error.InnerException.Message,Does.Contain("CombineInstance"));
        }

        [Test] public void GuardRejectsMissingMeshApi()
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Validate("<linker><assembly fullname='UnityEngine.CoreModule'><type fullname='UnityEngine.CombineInstance' preserve='all'/></assembly></linker>"));
            Assert.That(error.InnerException.Message,Does.Contain("UnityEngine.Mesh"));
        }

        [Test] public void GuardAcceptsCompleteMeshTypes()
        { Assert.DoesNotThrow(()=>Validate("<linker><assembly fullname='UnityEngine.CoreModule'><type fullname='UnityEngine.CombineInstance' preserve='all'/><type fullname='UnityEngine.Mesh' preserve='all'/></assembly></linker>")); }

        [Test] public void GuardRejectsMalformedXml()
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Validate("<linker>"));
            Assert.That(error.InnerException.Message,Does.Contain("无效"));
        }

        private static void Validate(string xml)
        {
            var type=Type.GetType("Game.Editor.MeshCombineHostBuildGuard, Assembly-CSharp-Editor");
            Assert.That(type,Is.Not.Null,"宿主尚未检查河谷网格合并保留规则");
            type.GetMethod("ValidatePreservation",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{xml});
        }
    }
}
