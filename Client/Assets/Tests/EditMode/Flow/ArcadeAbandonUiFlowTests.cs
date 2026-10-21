using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>通过正式按钮、弹窗、领域事务及隔离存档验证单人放弃流程；不启动战斗或访问正式玩家数据。</summary>
    public sealed class ArcadeAbandonUiFlowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game architecture;
        private string root;
        private GameObject poolRoot;
        private ArcadeRunSystem runs;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            UIHelper.ResetForTests();
            LocalizationLanguages.ResetForTests();
            LifeCycle.ClearForTests();
            architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", PrivateInstance).SetValue(architecture, true);
            root = Path.Combine(Path.GetTempPath(), "FirepowerLoopAbandonTests", Guid.NewGuid().ToString("N"));
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new ResourceUpdateModel());
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new OnlineArcadeRunModel());
            ResourceLoader.SetLoaderForTests(new LocalizationLoader());
            architecture.RegisterUtility(new LocalizationUtility());
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new global::Tables(
                name => new ByteBuf(File.ReadAllBytes(Path.Combine(project, "Client/Assets/Game/Config", name + ".bytes"))))));
            architecture.RegisterSystem(new AppStateSystem());
            architecture.RegisterSystem(new ResourceUpdateSystem());
            architecture.RegisterSystem(new OnlineArcadeRunSystem());
            architecture.RegisterSystem(new RedPointSystem());
            runs = new ArcadeRunSystem();
            runs.ConfigureForTests(root);
            architecture.RegisterSystem(runs);
            architecture.RegisterSystem(new MapSceneSystem(new ProjectileCtrl(architecture), new PlayerCtrl(architecture),
                new EnemyCtrl(architecture), new LootCtrl(architecture)));
            architecture.RegisterSystem(new ArcadeSceneFlowSystem());
            poolRoot = new GameObject(SceneRoot.PoolRootName);
            var uiRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var name in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
                new GameObject(name, typeof(RectTransform)).transform.SetParent(uiRoot.transform, false);
            var inputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            new GameObject("EventSystem", typeof(EventSystem), inputModule).transform.SetParent(uiRoot.transform, false);
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            Assert.That(runs.TryStartConfiguredRun(out _, out var error), Is.True, error);
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            architecture.Deinit();
            Object.DestroyImmediate(poolRoot);
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            LocalizationLanguages.ResetForTests();
            LifeCycle.ClearForTests();
            Time.timeScale = previousTimeScale;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void AbandonClickOpensConfirmationAndCancelPreservesProgress()
        {
            var ctrl = UIHelper.ShowUI<ArcadeResumeUICtrl>();
            var before = File.ReadAllText(new ArcadeRunFileStore(root).RunPath);
            View<ArcadeResumeUI>(ctrl).BtnAbandon.onClick.Invoke();
            var message = UIHelper.GetUICtrl<MessageUICtrl>();
            Assert.That(message?.IsShow(), Is.True, "第一次点击必须显示确认框");
            Assert.That(View<MessageUI>(message).ContentText.text, Does.Contain("本局进度将被清除"));
            ClickMessage(MessageButtonRole.Cancel);
            Assert.That(message.IsShow(), Is.False);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(File.ReadAllText(new ArcadeRunFileStore(root).RunPath), Is.EqualTo(before));
        }

        [Test]
        public void ConfirmAbandonClearsRunAndReturnsToMenuWithoutResultDialog()
        {
            var ctrl = OpenConfirmation();
            ClickMessage(MessageButtonRole.Confirm);
            Assert.That(runs.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
            Assert.That(architecture.GetModel<ArcadeRunModel>().HasRun, Is.False);
            Assert.That(Directory.GetFiles(root, "*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(ctrl.IsShow(), Is.False, "成功放弃后不能重新打开结果界面");
            var menu = UIHelper.GetUICtrl<StartUICtrl>();
            Assert.That(menu?.IsShow(), Is.True);
            Assert.That(View<StartUI>(menu).BtnNewGame.gameObject.activeSelf, Is.True);
            Assert.That(View<StartUI>(menu).BtnContinue.gameObject.activeSelf, Is.False);
            Assert.That(UIHelper.GetUICtrl<FloatUICtrl>()?.IsShow(), Is.True);
        }

        [Test]
        public void DeleteFailurePreservesProgressAndAllowsAbandonRetry()
        {
            var ctrl = OpenConfirmation();
            var runId = architecture.GetModel<ArcadeRunModel>().Current.RunId;
            runs.FailNextDeleteForTests("删除失败测试");
            ClickMessage(MessageButtonRole.Confirm);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(View<ArcadeResumeUI>(ctrl).TextMessage.text, Is.EqualTo("删除失败测试"));
            Assert.That(View<ArcadeResumeUI>(ctrl).BtnAbandon.gameObject.activeSelf, Is.True);
            Assert.That(runs.InspectPending().Data.RunId, Is.EqualTo(runId));
            Assert.That(runs.InspectPending().Data.HasResult, Is.False);
            View<ArcadeResumeUI>(ctrl).BtnAbandon.onClick.Invoke();
            ClickMessage(MessageButtonRole.Confirm);
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(runs.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        [Test]
        public void SceneFailurePreservesProgressAndAllowsAbandonRetry()
        {
            var ctrl = OpenConfirmation();
            var flow = architecture.GetSystem<ArcadeSceneFlowSystem>();
            typeof(ArcadeSceneFlowSystem).GetField("busy", PrivateInstance).SetValue(flow, true);
            ClickMessage(MessageButtonRole.Confirm);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(View<ArcadeResumeUI>(ctrl).TextMessage.text, Is.Not.Empty);
            Assert.That(runs.InspectPending().Data.HasResult, Is.False);
            typeof(ArcadeSceneFlowSystem).GetField("busy", PrivateInstance).SetValue(flow, false);
            View<ArcadeResumeUI>(ctrl).BtnAbandon.onClick.Invoke();
            ClickMessage(MessageButtonRole.Confirm);
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(runs.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        [Test]
        public void OldConfirmationCannotAbandonAfterDialogReopen()
        {
            var ctrl = OpenConfirmation();
            var callback = Config().Buttons.Single(button => button.Role == MessageButtonRole.Confirm).OnClick;
            UIHelper.HideUI<MessageUICtrl>();
            ctrl.Hide();
            ctrl.Show();
            callback.Invoke();
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(runs.InspectPending().Data.HasResult, Is.False);
        }

        [Test]
        public void OldConfirmationCannotDeleteAReplacementRun()
        {
            OpenConfirmation();
            var callback = Config().Buttons.Single(button => button.Role == MessageButtonRole.Confirm).OnClick;
            Assert.That(runs.TryAbandon(out var error), Is.True, error);
            Assert.That(runs.TrySettle(out _, out error), Is.True, error);
            Assert.That(runs.TryStartConfiguredRun(out var replacement, out error), Is.True, error);
            callback.Invoke();
            Assert.That(runs.InspectPending().Data.RunId, Is.EqualTo(replacement.RunId));
            Assert.That(runs.InspectPending().Data.HasResult, Is.False);
        }

        [Test]
        public void ExistingResultStillRequiresNormalAcknowledgement()
        {
            Assert.That(runs.TryAbandon(out var error), Is.True, error);
            var ctrl = UIHelper.ShowUI<ArcadeResumeUICtrl>();
            Assert.That(View<ArcadeResumeUI>(ctrl).BtnSettle.gameObject.activeSelf, Is.True);
            Assert.That(View<ArcadeResumeUI>(ctrl).BtnAbandon.gameObject.activeSelf, Is.False);
            Assert.That(runs.InspectPending().Status, Is.EqualTo(SaveFileStatus.Valid));
            View<ArcadeResumeUI>(ctrl).BtnSettle.onClick.Invoke();
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(runs.InspectPending().Status, Is.EqualTo(SaveFileStatus.Missing));
        }

        private static ArcadeResumeUICtrl OpenConfirmation()
        {
            var ctrl = UIHelper.ShowUI<ArcadeResumeUICtrl>();
            View<ArcadeResumeUI>(ctrl).BtnAbandon.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow(), Is.True);
            return ctrl;
        }

        private static MessageConfig Config() => (MessageConfig)typeof(MessageUICtrl)
            .GetField("currentConfig", PrivateInstance).GetValue(UIHelper.GetUICtrl<MessageUICtrl>());

        private static void ClickMessage(MessageButtonRole role)
        {
            var config = Config();
            typeof(MessageUICtrl).GetMethod("OnMessageButtonClick", PrivateInstance).Invoke(UIHelper.GetUICtrl<MessageUICtrl>(),
                new object[] { new object[] { config.Buttons.Single(button => button.Role == role) } });
        }

        private static T View<T>(BaseUICtrl ctrl) where T : BaseUI =>
            (T)typeof(BaseUICtrl).GetField("BaseUI", PrivateInstance).GetValue(ctrl);

        /// <summary>只读取正式本地化资源，不访问资源服务器；Prefab 同样由 AssetDatabase 提供。</summary>
        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + Path.GetFileName(path));
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
