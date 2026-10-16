using System;
using System.IO;
using System.Reflection;
using Boot;
using Boot.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Tests.Flow
{
    /// <summary>读取真实 Boot 场景验证地址入口；持久化只写临时目录，不覆盖玩家设置。</summary>
    public sealed class BootResourceServerEditorTests
    {
        private Scene _scene;
        private string _root;
        private Component _editor;
        private Launcher _launcher;
        private Type _type;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "FirepowerLoopBootServerTests", Guid.NewGuid().ToString("N"));
            _type = typeof(Launcher).Assembly.GetType("Boot.UI.BootResourceServerView");
            Assert.That(_type, Is.Not.Null, "Boot 需要无需热更即可打开的地址编辑界面");
            _type.GetProperty("TestPersistenceRoot").SetValue(null, _root);
            _type.GetProperty("TestDefaultAddress").SetValue(null,
                (Func<UniTask<string>>)(() => UniTask.FromResult("http://192.168.2.100:8000")));
            Launcher.ResetTestPorts();
            Launcher.TestBootErrorLogWriter = _ => true;
            ResourceBoot.TestRawInitializationOutcome = () => UniTask.FromResult("resource-download-failed");
            _scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity", OpenSceneMode.Additive);
            foreach (GameObject obj in _scene.GetRootGameObjects())
            {
                if (!_launcher) _launcher = obj.GetComponentInChildren<Launcher>(true);
                if (!_editor) _editor = obj.GetComponentInChildren(_type, true);
            }
            Assert.That(_editor, Is.Not.Null, "Boot 场景必须序列化入口及编辑区域");
            _launcher.InitializeForTests();
            Call("InitializeForTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid()) EditorSceneManager.CloseScene(_scene, true);
            if (_type != null) _type.GetMethod("ResetTestPorts").Invoke(null, null);
            Launcher.ResetTestPorts();
            ResourceBoot.ResetTestHooks();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void EntrySurvivesBusyAndFailureAndOpensBoundInput()
        {
            var view = _editor.transform.parent.GetComponentInChildren<HotUpdateView>(true);
            view.RenderBusy("启动中");
            Assert.That(Get<Button>("_entryButton").gameObject.activeInHierarchy, Is.True);
            view.RenderFailure(BootFailure.Create(BootFailureCode.PackageVersionRequestFailed,
                BootFailureStage.ResourceVersion));
            Assert.That(Get<Button>("_entryButton").interactable, Is.True);
            Open();
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.True);
            Assert.That(Get<InputField>("_addressInput").text, Is.EqualTo("http://192.168.2.100:8000"));
        }

        [Test]
        public void SavePersistsNormalizedAddressAndStartsRetry()
        {
            Open();
            Get<InputField>("_addressInput").text = " http://192.168.2.111:8000/ ";
            Get<Button>("_saveButton").onClick.Invoke();
            Assert.That(ResourceServerPreference.TryLoad(_root, out string address, out _), Is.True);
            Assert.That(address, Is.EqualTo("http://192.168.2.111:8000"));
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.False);
            Assert.That(_launcher.GetComponent<ResourceBoot>().LastError, Is.EqualTo("ResourceDownloadFailed"));
        }

        [Test]
        public void InvalidSaveKeepsPreviousAddressAndEditorOpen()
        {
            ResourceServerPreference.TrySave("http://192.168.2.112:8000", _root, out _, out _);
            Open();
            Get<InputField>("_addressInput").text = "192.168.2.111";
            Get<Button>("_saveButton").onClick.Invoke();
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.True);
            Assert.That(ResourceServerPreference.TryLoad(_root, out string address, out _), Is.True);
            Assert.That(address, Is.EqualTo("http://192.168.2.112:8000"));
            Assert.That(Get<Text>("_feedbackText").text, Does.Contain("http://"));
        }

        [Test]
        public void ResetRemovesOverrideAndRestartsWithDefault()
        {
            ResourceServerPreference.TrySave("http://192.168.2.112:8000", _root, out _, out _);
            Open();
            ((UniTask)Call("RestoreDefaultAsync")).GetAwaiter().GetResult();
            Assert.That(ResourceServerPreference.TryLoad(_root, out _, out _), Is.False);
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.False);
            Assert.That(_launcher.GetComponent<ResourceBoot>().LastError, Is.EqualTo("ResourceDownloadFailed"));
        }

        [Test]
        public void CancelDiscardsDraftWithoutSaving()
        {
            Open();
            Get<InputField>("_addressInput").text = "http://192.168.2.111:8000";
            Get<Button>("_cancelButton").onClick.Invoke();
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.False);
            Assert.That(ResourceServerPreference.TryLoad(_root, out _, out _), Is.False);
        }

        [Test]
        public void RepeatedInitializationDoesNotDuplicateSaveListeners()
        {
            Call("InitializeForTests");
            int calls = 0;
            ResourceBoot.TestRawInitializationOutcome = () => { calls++; return UniTask.FromResult("resource-download-failed"); };
            Open();
            Get<InputField>("_addressInput").text = "http://192.168.2.111:8000";
            Get<Button>("_saveButton").onClick.Invoke();
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void CancelWhileDefaultLoadsIgnoresLateResult()
        {
            var gate = new UniTaskCompletionSource<string>();
            _type.GetProperty("TestDefaultAddress").SetValue(null,
                (Func<UniTask<string>>)(() => gate.Task));
            var open = (UniTask)Call("OpenEditorAsync");
            Get<Button>("_cancelButton").onClick.Invoke();
            gate.TrySetResult("http://192.168.2.120:8000");
            open.GetAwaiter().GetResult();
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.False);
            Assert.That(Get<InputField>("_addressInput").text, Is.Empty);
            Assert.That(ResourceServerPreference.TryLoad(_root, out _, out _), Is.False);
        }

        [Test]
        public void DefaultReadFailurePreservesSavedOverride()
        {
            ResourceServerPreference.TrySave("http://192.168.2.112:8000", _root, out _, out _);
            Open();
            _type.GetProperty("TestDefaultAddress").SetValue(null,
                (Func<UniTask<string>>)(() => UniTask.FromException<string>(new IOException("unreadable"))));
            ((UniTask)Call("RestoreDefaultAsync")).GetAwaiter().GetResult();
            Assert.That(Get<GameObject>("_editorPanel").activeSelf, Is.True);
            Assert.That(ResourceServerPreference.TryLoad(_root, out string address, out _), Is.True);
            Assert.That(address, Is.EqualTo("http://192.168.2.112:8000"));
        }

        private void Open() => ((UniTask)Call("OpenEditorAsync")).GetAwaiter().GetResult();
        private object Call(string name) => _type.GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_editor, null);
        private T Get<T>(string name) => (T)_type.GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_editor);
    }
}
