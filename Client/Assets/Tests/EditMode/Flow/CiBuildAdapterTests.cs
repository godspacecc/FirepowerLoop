using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>CI接线和产物发布事务的纯文件验收，不调用实际构建入口或启动服务器。</summary>
    public sealed class CiBuildAdapterTests
    {
        private string root, package, marker;
        [SetUp] public void Setup()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerAdapter-" + Guid.NewGuid().ToString("N"));
            package = Path.Combine(root, "packages/v1"); Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "DefaultPackage.version"), "v1"); File.WriteAllText(Path.Combine(package, "bundle"), "data");
            marker = Path.Combine(root, "artifact.json");
        }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static object Call(string typeName, string method, params object[] args)
        {
            var type = Type.GetType("Game.Editor." + typeName + ", Assembly-CSharp-Editor"); Assert.That(type, Is.Not.Null, typeName);
            try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        private void Seal(string version = "v1") => Call("CiBuildArtifacts", "Seal", marker, new JObject
        {
            ["SchemaVersion"] = 1, ["ProjectId"] = "FirepowerLoop", ["Platform"] = "PC", ["BuildType"] = "HostUpdate",
            ["Version"] = version, ["PackageDirectory"] = package
        }.ToString());
        [Test] public void SealedHostArtifactSurvivesValidation() { Seal(); Call("CiBuildArtifacts", "Validate", marker); Assert.That(File.ReadAllText(marker), Does.Contain("Sha256")); }
        [Test] public void SameLengthPackageChangeInvalidatesArtifact() { Seal(); File.WriteAllText(Path.Combine(package, "bundle"), "evil"); Assert.Throws<InvalidOperationException>(() => Call("CiBuildArtifacts", "Validate", marker)); }
        [Test] public void ArtifactCannotClaimAnotherResourceVersion() { Assert.Throws<InvalidOperationException>(() => Seal("v2")); Assert.That(File.Exists(marker), Is.False); }
        [Test] public void AddedResourceCannotBypassInventory() { Seal(); File.WriteAllText(Path.Combine(package, "extra"), "extra"); Assert.Throws<InvalidOperationException>(() => Call("CiBuildArtifacts", "Validate", marker)); }
        [Test] public void DownloadedArtifactValidatesItsRelocatedCopy()
        {
            Seal(); string relocated = root + "-downloaded"; Directory.Move(root, relocated); root = relocated;
            marker = Path.Combine(root, "artifact.json");
            Assert.DoesNotThrow(() => Call("CiBuildArtifacts", "Validate", marker));
            File.WriteAllText(Path.Combine(root, "packages/v1/bundle"), "evil");
            Assert.Throws<InvalidOperationException>(() => Call("CiBuildArtifacts", "Validate", marker));
        }
        [Test] public void ArtifactCannotReferencePathsOutsideItsDownloadedRoot()
        {
            Seal(); var json = JObject.Parse(File.ReadAllText(marker)); json["PackageDirectory"] = "../outside"; File.WriteAllText(marker, json.ToString());
            Assert.Throws<InvalidOperationException>(() => Call("CiBuildArtifacts", "Validate", marker));
        }
        [Test] public void PublisherUsesSealedPackageInsteadOfNewerSibling()
        {
            File.WriteAllText(Path.Combine(package, "DefaultPackage_v1.bytes"), "manifest");
            File.WriteAllText(Path.Combine(package, "DefaultPackage_v1.hash"), "hash");
            File.WriteAllText(Path.Combine(package, "0123456789abcdef0123456789abcdef"), "bundle-v1");
            string newer = Path.Combine(root, "packages/v9"); Directory.CreateDirectory(newer);
            File.WriteAllText(Path.Combine(newer, "DefaultPackage.version"), "v9");
            Seal(); string destination = Path.Combine(root, "published");
            Call("CiBuildPublisher", "Publish", marker, destination);
            Assert.That(File.ReadAllText(Path.Combine(destination, "ResourceFiles/DefaultPackage.version")), Is.EqualTo("v1"));
            Assert.That(File.Exists(Path.Combine(destination, "resource_server.ps1")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(destination, "r")), Is.False);
        }
        [Test] public void FirstPackageCannotBeSealedWithoutPlayerOrBaseline()
        {
            Assert.Throws<InvalidOperationException>(() => Call("CiBuildArtifacts", "Seal", marker, new JObject
            {
                ["SchemaVersion"] = 1, ["ProjectId"] = "FirepowerLoop", ["Platform"] = "Android", ["BuildType"] = "FirstPackage",
                ["Version"] = "v1", ["PackageDirectory"] = package
            }.ToString()));
            Assert.That(File.Exists(marker), Is.False);
        }
        [Test] public void PublishingFailureDoesNotExposePartialDestination()
        {
            string destination = Path.Combine(root, "published");
            Assert.Throws<IOException>(() => Call("CiBuildPublisher", "PublishTransaction", destination, (Action<string>)(staging => { File.WriteAllText(Path.Combine(staging, "partial"), "data"); throw new IOException("failed"); })));
            Assert.That(Directory.Exists(destination), Is.False); Assert.That(Directory.GetDirectories(root, ".ci-*").Length, Is.Zero);
        }
        [Test] public void PublishingSuccessMovesOnlyCompletedBundle()
        {
            string destination = Path.Combine(root, "published");
            Call("CiBuildPublisher", "PublishTransaction", destination, (Action<string>)(staging => File.WriteAllText(Path.Combine(staging, "complete"), "data")));
            Assert.That(File.ReadAllText(Path.Combine(destination, "complete")), Is.EqualTo("data"));
        }
        [Test] public void PublishingCannotOverwriteExistingBundle()
        {
            string destination = Path.Combine(root, "published"); Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "keep"), "keep");
            Assert.Throws<IOException>(() => Call("CiBuildPublisher", "PublishTransaction", destination, (Action<string>)(_ => Assert.Fail("禁止准备新内容"))));
            Assert.That(File.ReadAllText(Path.Combine(destination, "keep")), Is.EqualTo("keep"));
        }
        [TestCase(false, "StandaloneWindows64", "PC", false)]
        [TestCase(true, "Android", "PC", false)]
        [TestCase(true, "StandaloneWindows64", "Android", false)]
        [TestCase(true, "StandaloneWindows64", "PC", true)]
        public void InvalidEditorStateFailsBeforeBuild(bool batch, string target, string platform, bool busy) => Assert.Throws<InvalidOperationException>(() => Call("CiBuildEntry", "ValidateEnvironment", batch, target, platform, busy));
        [Test] public void AllFourRoutesResolveToConcreteMenuEntrypoints()
        {
            foreach (string route in new[] { "PcFirst", "PcHost", "AndroidFirst", "AndroidHost" })
            {
                var method = (MethodInfo)Call("CiBuildEntry", "ResolveRoute", route);
                Assert.That(method, Is.Not.Null); Assert.That(method.GetParameters(), Is.Empty);
                Assert.That(method.DeclaringType.Name, Is.EqualTo(route.StartsWith("Pc") ? "BuildUtility" : "AndroidBuildUtility"));
            }
        }
    }
}
