using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证两端共用的版本、失败诊断和重载门禁；不切平台，不调用构建委托。</summary>
    public sealed class BuildWorkflowPolicyTests
    {
        private static Type RequiredType(string name)
        {
            var type = Type.GetType("Game.Editor." + name + ", Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "缺少构建策略类型：" + name);
            return type;
        }

        [Test]
        public void ResourceVersionsDistinguishSecondsWithinSameMinute()
        {
            var method = RequiredType("BuildWorkflowUtility").GetMethod("CreateResourceVersion");
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { new DateTime(2026, 10, 9, 14, 5, 1) }),
                Is.EqualTo("20261009140501"));
            Assert.That(method.Invoke(null, new object[] { new DateTime(2026, 10, 9, 14, 5, 2) }),
                Is.EqualTo("20261009140502"));
        }

        [TestCase(false, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, true, true, false)]
        [TestCase(true, true, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, true, false)]
        [TestCase(true, false, false, true)]
        public void ResumeRequiresRealReloadAndIdleEditor(bool reloaded, bool compiling, bool updating, bool expected)
        {
            var method = RequiredType("BuildWorkflowUtility").GetMethod("CanResume");
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { reloaded, compiling, updating }), Is.EqualTo(expected));
        }

        [Test]
        public void FailureIncludesWorkflowFailedStageTimingAndRetainedArtifact()
        {
            var timingType = RequiredType("BuildStageTiming");
            var timing = Activator.CreateInstance(timingType, "Android 首包完整打包");
            var run = timingType.GetMethod("Run", new[] { typeof(string), typeof(Action) });
            Action fail = () => throw new InvalidOperationException("sample-failure");
            Assert.Throws<TargetInvocationException>(() => run.Invoke(timing, new object[] { "PublishAndroidApk", fail }));
            var format = RequiredType("BuildWorkflowUtility").GetMethod("FormatFailure");
            Assert.That(format, Is.Not.Null);
            string text = (string)format.Invoke(null, new[] { timing, "sample-failure", "E:/Release/AnotherDay.apk" });
            Assert.That(text, Does.Contain("入口: Android 首包完整打包"));
            Assert.That(text, Does.Contain("失败阶段: PublishAndroidApk"));
            Assert.That(text, Does.Contain("sample-failure"));
            Assert.That(text, Does.Contain("E:/Release/AnotherDay.apk"));
            Assert.That(text, Does.Contain("总耗时："));
            Assert.That(text, Does.Contain("PublishAndroidApk:"));
        }

        [TestCase("Android 最新代码资源完整打包", "BuildLatestCodeAndResourcesBatch")]
        [TestCase("Android Host 基础包完整打包", "BuildHostBaseCodeAndResourcesBatch")]
        [TestCase("Android 首包完整打包", "BuildSmallHostBaseCodeAndResourcesBatch")]
        [TestCase("Android Host 代码+资源更新", "BuildHostCodeAndResourcesBatch")]
        public void AndroidPendingWorkflowResolvesOnlyKnownEntryWithoutExecutingIt(string name, string expectedMethod)
        {
            var method = RequiredType("AndroidBuildUtility").GetMethod("ResolveWorkflow", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            var action = (Action)method.Invoke(null, new object[] { name });
            Assert.That(action, Is.Not.Null);
            Assert.That(action.Method.Name, Is.EqualTo(expectedMethod));
        }

        [Test]
        public void AndroidUnknownPendingWorkflowIsRejected()
        {
            var method = RequiredType("AndroidBuildUtility").GetMethod("ResolveWorkflow", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { "unknown-workflow" }), Is.Null);
        }
    }
}
