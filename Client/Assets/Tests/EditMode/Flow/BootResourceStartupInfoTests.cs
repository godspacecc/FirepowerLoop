using System.Reflection;
using Boot;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class BootResourceStartupInfoTests
    {
        [SetUp]
        public void SetUp()
        {
            Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Reset();
        }

        [Test]
        public void ResetClearsStartupSnapshot()
        {
            Publish(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "1",
                "1"));

            Reset();

            Assert.That(BootResourceStartupState.TryGet(out _), Is.False);
        }

        [Test]
        public void HostLatestSavesServerActiveAndHostVersion()
        {
            Publish(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://127.0.0.1:8000",
                "host-1",
                "host-2"));

            Assert.That(BootResourceStartupState.TryGet(out var info), Is.True);
            Assert.That(info.Source, Is.EqualTo(BootResourceSource.HostLatest));
            Assert.That(info.ResourceServer, Is.EqualTo("http://127.0.0.1:8000"));
            Assert.That(info.ActiveVersion, Is.EqualTo("host-1"));
            Assert.That(info.HostVersion, Is.EqualTo("host-2"));
        }

        [Test]
        public void HostCachedSavesCachedSourceAndVersion()
        {
            Publish(new BootResourceStartupInfo(
                BootResourceSource.HostCached,
                "http://127.0.0.1:8000",
                "cached-1",
                "host-2"));

            Assert.That(BootResourceStartupState.TryGet(out var info), Is.True);
            Assert.That(info.Source, Is.EqualTo(BootResourceSource.HostCached));
            Assert.That(info.ActiveVersion, Is.EqualTo("cached-1"));
            Assert.That(info.HostVersion, Is.EqualTo("host-2"));
        }

        [Test]
        public void BuiltinFallbackSavesBuiltinVersionAndResourceServer()
        {
            Publish(new BootResourceStartupInfo(
                BootResourceSource.BuiltinFallback,
                "http://127.0.0.1:8000",
                "builtin-1",
                string.Empty));

            Assert.That(BootResourceStartupState.TryGet(out var info), Is.True);
            Assert.That(info.Source, Is.EqualTo(BootResourceSource.BuiltinFallback));
            Assert.That(info.ResourceServer, Is.EqualTo("http://127.0.0.1:8000"));
            Assert.That(info.ActiveVersion, Is.EqualTo("builtin-1"));
        }

        [Test]
        public void EditorSimulateSavesEditorSource()
        {
            Publish(new BootResourceStartupInfo(
                BootResourceSource.EditorSimulate,
                "http://127.0.0.1:8000",
                "editor-1",
                string.Empty));

            Assert.That(BootResourceStartupState.TryGet(out var info), Is.True);
            Assert.That(info.Source, Is.EqualTo(BootResourceSource.EditorSimulate));
            Assert.That(info.ActiveVersion, Is.EqualTo("editor-1"));
        }

        [Test]
        public void LaterPublishOverridesPreviousSnapshot()
        {
            Publish(new BootResourceStartupInfo(
                BootResourceSource.HostLatest,
                "http://old",
                "old",
                "old"));

            Publish(new BootResourceStartupInfo(
                BootResourceSource.BuiltinFallback,
                "http://new",
                "new",
                string.Empty));

            Assert.That(BootResourceStartupState.TryGet(out var info), Is.True);
            Assert.That(info.Source, Is.EqualTo(BootResourceSource.BuiltinFallback));
            Assert.That(info.ResourceServer, Is.EqualTo("http://new"));
            Assert.That(info.ActiveVersion, Is.EqualTo("new"));
        }

        private static void Reset()
        {
            typeof(BootResourceStartupState)
                .GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, null);
        }

        private static void Publish(BootResourceStartupInfo info)
        {
            typeof(BootResourceStartupState)
                .GetMethod("Publish", BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { info });
        }
    }
}
