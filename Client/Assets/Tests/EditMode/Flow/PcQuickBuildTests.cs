#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
namespace Game.Tests
{
    public class PcQuickBuildTests
    {
        [TestCase("dll-a", "bridge-a", true)]
        [TestCase("dll-b", "bridge-a", false)]
        [TestCase("dll-a", "bridge-b", false)]
        public void PreparationRejectsDllOrBridgeChangedDuringResourceBuild(string dllAfter, string bridgeAfter, bool accepted)
        {
            var method = Type.GetType("Game.Editor.PcQuickBuildUtility, Assembly-CSharp-Editor", true)
                .GetMethod("RequireUnchangedPreparation", BindingFlags.Static | BindingFlags.NonPublic);
            TestDelegate validate = () => method.Invoke(null, new object[] { "dll-a", dllAfter, "bridge-a", bridgeAfter });
            if (accepted) Assert.DoesNotThrow(validate);
            else Assert.Throws<TargetInvocationException>(validate);
        }
        [Test] public void FingerprintDetectsSameLengthChange()
        {
            string path = Path.GetTempFileName();
            try {
                var type = Type.GetType("Game.Editor.PcQuickBuildUtility, Assembly-CSharp-Editor", true);
                var method = type.GetMethod("Fingerprint", BindingFlags.Static | BindingFlags.NonPublic);
                File.WriteAllText(path, "abc");
                var before = method.Invoke(null, new object[] { new[] { path } });
                File.WriteAllText(path, "abd");
                Assert.AreNotEqual(before, method.Invoke(null, new object[] { new[] { path } }));
            } finally { File.Delete(path); }
        }
        // 直接验证记录边界，不调用Unity构建或工程扫描。
        [TestCase("Full", "StandaloneWindows64", "v1", true)]
        [TestCase("SmallHostBase", "StandaloneWindows64", "v1", true)]
        [TestCase("QuickAot", "StandaloneWindows64", "v1", false)]
        [TestCase("Full", "Android", "v1", false)]
        [TestCase("Full", "StandaloneWindows64", "../v1", false)]
        [TestCase("Full", "StandaloneWindows64", "", false)]
        public void IdentityRejectsWrongPlatformKindAndVersion(string kind, string platform, string version, bool valid)
        {
            var type = Type.GetType("Game.Editor.PcQuickBuildUtility, Assembly-CSharp-Editor", true);
            var record = Activator.CreateInstance(type.GetNestedType("Prepared", BindingFlags.NonPublic));
            string cache = Path.Combine(Path.GetTempPath(), "PcQuickCache");
            string packages = Path.Combine(Path.GetTempPath(), "PcQuickPackages");
            foreach (var pair in new[] { new[] { "Kind", kind }, new[] { "Platform", platform }, new[] { "Version", version },
                new[] { "Directory", Path.Combine(cache, Guid.NewGuid().ToString("N")) }, new[] { "Package", Path.Combine(packages, version) } })
                record.GetType().GetField(pair[0]).SetValue(record, pair[1]);
            var method = type.GetMethod("ValidateIdentity", BindingFlags.NonPublic | BindingFlags.Static);
            if (valid) Assert.DoesNotThrow(() => method.Invoke(null, new[] { record, cache, packages }));
            else Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new[] { record, cache, packages }));
        }

        [TestCase("Directory")]
        [TestCase("Package")]
        public void IdentityRejectsOutsideDirectory(string field)
        {
            var type = Type.GetType("Game.Editor.PcQuickBuildUtility, Assembly-CSharp-Editor", true);
            var record = Activator.CreateInstance(type.GetNestedType("Prepared", BindingFlags.NonPublic));
            string cache = Path.Combine(Path.GetTempPath(), "PcQuickCache");
            string packages = Path.Combine(Path.GetTempPath(), "PcQuickPackages");
            foreach (var pair in new[] { new[] { "Kind", "Full" }, new[] { "Version", "v1" },
                new[] { "Directory", Path.Combine(cache, Guid.NewGuid().ToString("N")) }, new[] { "Package", Path.Combine(packages, "v1") } })
                record.GetType().GetField(pair[0]).SetValue(record, pair[1]);
            record.GetType().GetField(field).SetValue(record, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            Assert.Throws<TargetInvocationException>(() => type.GetMethod("ValidateIdentity", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new[] { record, cache, packages }));
        }

        [TestCase("generated")]
        [TestCase("archive")]
        public void TreeFingerprintDetectsAddDeleteAndRename(string folder)
        {
            string root = Path.Combine(Path.GetTempPath(), "PcQuick_" + Guid.NewGuid().ToString("N"), folder);
            Directory.CreateDirectory(root);
            try
            {
                var method = Type.GetType("Game.Editor.PcQuickBuildUtility, Assembly-CSharp-Editor", true)
                    .GetMethod("Tree", BindingFlags.Static | BindingFlags.NonPublic);
                Func<object> capture = () => method.Invoke(null, new object[] { root });
                var empty = capture();
                string file = Path.Combine(root, "one"); File.WriteAllText(file, "bytes");
                var added = capture(); Assert.AreNotEqual(empty, added);
                File.Move(file, Path.Combine(root, "two"));
                Assert.AreNotEqual(added, capture());
                File.Delete(Path.Combine(root, "two")); Assert.AreEqual(empty, capture());
            }
            finally { Directory.Delete(Path.GetDirectoryName(root), true); }
        }
        [Test] public void ObsoleteHostEntryRejectsOldMeaning()
        {
            var type = Type.GetType("Game.Editor.BuildUtility, Assembly-CSharp-Editor", true);
            var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("BuildRecentHostPlayerBatch").Invoke(null, null));
            Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
        }
    }
}
#endif
