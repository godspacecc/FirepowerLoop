using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ApplicationRestartTests
    {
        [Test]
        public void PathProbeExceptionReturnsFailureWithoutQuitting()
        {
            var quitter = new Quitter();
            var utility = new ApplicationRestartUtility(new Launcher(), quitter, () => false,
                () => throw new NotSupportedException("path unavailable"));
            Assert.That(utility.TryRestart(out var error), Is.False);
            Assert.That(error, Does.Contain("path unavailable"));
            Assert.That(quitter.Called, Is.False);
        }

        [Test]
        public void AndroidPackageDeclaresPrivateSeparateProcessActivity()
        {
            var path = Path.Combine(Application.dataPath,
                "Plugins/Android/AnotherDayRestart.androidlib/AndroidManifest.xml");
            Assert.That(File.Exists(path), Is.True);
            var document = System.Xml.Linq.XDocument.Load(path);
            System.Xml.Linq.XNamespace android = "http://schemas.android.com/apk/res/android";
            var activity = document.Root.Element("application").Element("activity");
            Assert.That((string)activity.Attribute(android + "process"), Is.EqualTo(":resource_restart"));
            Assert.That((string)activity.Attribute(android + "exported"), Is.EqualTo("false"));
        }

        private sealed class Launcher : IProcessLauncher
        {
            public bool TryLaunch(string fileName, string workingDirectory, out string error)
            { error = "must not launch"; return false; }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void AndroidUsesNativeHandoffWithoutProbingDesktopPath(bool success)
        {
            var quitter = new Quitter();
            var native = new AndroidLauncher { Result = success };
            var utility = new ApplicationRestartUtility(new Launcher(), quitter, () => false,
                () => throw new Exception("desktop path must not be read"), () => true, native);
            Assert.That(utility.TryRestart(out var error), Is.EqualTo(success));
            Assert.That(native.Calls, Is.EqualTo(1));
            Assert.That(quitter.Called, Is.False, "退出由原生中转进程负责");
            if (success)
            {
                Assert.That(utility.TryRestart(out error), Is.True);
                Assert.That(native.Calls, Is.EqualTo(1), "重复点击不能创建第二个中转进程");
            }
            else Assert.That(error, Is.EqualTo("native failed"));
        }

        private sealed class AndroidLauncher : IAndroidRestartLauncher
        {
            public bool Result;
            public int Calls;
            public bool TryRestart(out string error) { Calls++; error = Result ? "" : "native failed"; return Result; }
        }
        private sealed class Quitter : IApplicationQuitter
        {
            public bool Called;
            public void Quit() => Called = true;
        }
    }
}
