using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class OnlineLoginUiGenerationTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab";

        private static readonly FieldExpectation[] ExpectedFields =
        {
            new FieldExpectation("RootPanel", typeof(RectTransform), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-1832084145794790776-0"),
            new FieldExpectation("AnonymousRoot", typeof(GameObject), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-7023431494390080244-0"),
            new FieldExpectation("AuthenticatedRoot", typeof(GameObject), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-2544842655209300070-0"),
            new FieldExpectation("UserNameInput", typeof(TMP_InputField), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-5737539804132797578-0"),
            new FieldExpectation("PasswordInput", typeof(TMP_InputField), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-1234704575787636661-0"),
            new FieldExpectation("ServerAddressInput", typeof(TMP_InputField), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-1857192322086178747-0"),
            new FieldExpectation("SaveServerButton", typeof(Button), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-2208656212690976453-0"),
            new FieldExpectation("LoginButton", typeof(Button), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-8072789183465558659-0"),
            new FieldExpectation("LogoutButton", typeof(Button), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-3006928175437741375-0"),
            new FieldExpectation("RoomButton", typeof(Button), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-6344381357683178872-0"),
            new FieldExpectation("CloseButton", typeof(Button), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-6395093348667173369-0"),
            new FieldExpectation("ResultText", typeof(TextMeshProUGUI), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-1978306714761719370-0"),
            new FieldExpectation("AuthenticatedUserText", typeof(TextMeshProUGUI), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-6748054833823751336-0"),
            new FieldExpectation("AuthenticatedPlayerText", typeof(TextMeshProUGUI), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-2405985785552694994-0"),
            new FieldExpectation("WorldButton", typeof(Button), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-1762954111018666909-0"),
            new FieldExpectation("WorldButtonText", typeof(TextMeshProUGUI), "GlobalObjectId_V1-1-1ecdc9909fa36f4469dc531af5890e85-7003122708719417137-0"),
        };

        private OnlineHarness harness;
        private GameObject poolRoot;
        private GameObject testCamera;
        private string saveRoot;

        [SetUp]
        public void SetUp()
        {
            ResetGameArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            poolRoot = new GameObject(SceneRoot.PoolRootName);
            testCamera = new GameObject("OnlineLoginWorldTestCamera", typeof(Camera), typeof(AudioListener));
            testCamera.tag = "MainCamera";
            CreateUiRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
            harness = CreateHarness();
            saveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayWorldUi", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(saveRoot);
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            EditModeSceneIsolation.DestroyIfInActiveTestScene(poolRoot);
            EditModeSceneIsolation.DestroyIfInActiveTestScene(testCamera);

            ResetGameArchitecture();
            if (!string.IsNullOrWhiteSpace(saveRoot) && Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
            Game.OnRegisterPatch = _ => { };
            harness = null;
            poolRoot = null;
            testCamera = null;
        }

        [Test]
        public void OnlineLoginPrefab_HasCollectorGeneratedViewAndStableFieldBindings()
        {
            var prefab = LoadPrefab();
            var collector = prefab.GetComponent<ReferenceCollector>();
            var ui = prefab.GetComponent<OnlineLoginUI>();

            Assert.That(collector, Is.Not.Null);
            Assert.That(ui, Is.Not.Null);

            var fields = typeof(OnlineLoginUI)
                .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(field => field.Name);
            Assert.That(fields.Keys.OrderBy(name => name), Is.EqualTo(ExpectedFields.Select(item => item.Name).OrderBy(name => name)));
            Assert.That(collector.data.Select(item => item.key).OrderBy(key => key), Is.EqualTo(ExpectedFields.Select(item => item.Name).OrderBy(name => name)));
            Assert.That(collector.data.Select(item => item.key).Distinct().Count(), Is.EqualTo(collector.data.Count));

            foreach (var expected in ExpectedFields)
            {
                var field = fields[expected.Name];
                Assert.That(field.FieldType, Is.EqualTo(expected.Type), expected.Name);
                var value = field.GetValue(ui) as Object;
                Assert.That(value, Is.Not.Null, expected.Name);
                Assert.That(GlobalObjectId.GetGlobalObjectIdSlow(value).ToString(), Is.EqualTo(expected.GlobalObjectId), expected.Name);

                var collected = collector.data.Single(item => item.key == expected.Name);
                Assert.That(collected.TypeName, Is.EqualTo(expected.Type.FullName), expected.Name);
            }
        }

        [Test]
        public void OnlineLoginController_RendersAnonymousAndAuthenticatedStates()
        {
            var ui = ShowLoginUi();

            AssertAnonymous(ui);
            Assert.That(ui.RoomButton.transform.parent, Is.SameAs(ui.AuthenticatedRoot.transform));
            Assert.That(ui.LogoutButton.transform.parent, Is.SameAs(ui.AuthenticatedRoot.transform));

            Game.Interface.SendEvent(new OnlineAccountChangedEvent(AuthenticatedSnapshot()));

            Assert.That(ui.AnonymousRoot.activeSelf, Is.False);
            Assert.That(ui.AuthenticatedRoot.activeSelf, Is.True);
            Assert.That(ui.RoomButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(ui.LogoutButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(WorldRect(ui.RoomButton.GetComponent<RectTransform>()).Overlaps(WorldRect(ui.LogoutButton.GetComponent<RectTransform>())), Is.False);
        }

        [UnityTest]
        public IEnumerator ServerAddressEditingAvailableInLoginAndSettingsNetworkPage()
        {
            OnlineLoginUI ui = ShowLoginUi();
            yield return null;
            Assert.That(ui.ServerAddressInput.gameObject.activeSelf, Is.True);
            Assert.That(ui.SaveServerButton.gameObject.activeSelf, Is.True);

            var setting = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Home/Page/SettingNetwork.prefab");
            var network = setting.GetComponent<SettingNetworkPageUI>();
            Assert.That(network.GameServerInput, Is.Not.Null);
            Assert.That(network.GameServerApplyButton, Is.Not.Null);
            Assert.That(network.ResourceServerInput, Is.Not.Null);
            Assert.That(network.ResourceServerApplyButton, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator LogoutButton_UsesRealOperationAndHidesAuthenticatedActions()
        {
            var ui = ShowLoginUi();
            var login = harness.Account.LoginAsync("review_user", "Pass_123");
            yield return WaitUntil("login pending request", () => harness.Network.PendingCallCount == 1);
            harness.Network.CompleteNextLoginSuccess();
            yield return WaitUntil("login completion", () => login.IsCompleted);
            Assert.That(harness.Account.Snapshot.IsAuthenticated, Is.True);
            Assert.That(harness.Account.Snapshot.OperationState, Is.EqualTo(OnlineOperationState.Idle));
            Assert.That(ui.AuthenticatedRoot.activeSelf, Is.True);
            Assert.That(ui.LogoutButton.interactable, Is.True);
            Assert.That(harness.Network.PendingLogoutCount, Is.EqualTo(0), PendingDiagnostics("before click", ui));
            int listenersBeforeClick = RuntimeListenerCount(ui.LogoutButton.onClick);
            Assert.That(listenersBeforeClick, Is.GreaterThan(0));
            Assert.That(RuntimeListenerDescription(ui.LogoutButton.onClick), Does.Contain(nameof(OnlineLoginUICtrl)));

            ui.LogoutButton.onClick.Invoke();
            yield return null;
            yield return WaitUntil("logout pending request", () =>
                harness.Network.PendingLogoutCount > 0 || !harness.Account.Snapshot.IsAuthenticated);
            Assert.That(
                harness.Network.PendingLogoutCount,
                Is.EqualTo(1),
                PendingDiagnostics("after one LogoutButton.onClick.Invoke()", ui, listenersBeforeClick));

            harness.Network.CompleteNextLogoutSuccess();
            yield return WaitUntil("logout completion", () => !harness.Account.Snapshot.IsAuthenticated);

            Assert.That(ui.AuthenticatedRoot.activeSelf, Is.False);
            Assert.That(ui.RoomButton.gameObject.activeInHierarchy, Is.False);
            Assert.That(ui.LogoutButton.gameObject.activeInHierarchy, Is.False);
        }

        [Test]
        public void RoomButton_ClickOpensOnlineRoomController()
        {
            var ui = ShowLoginUi();
            Game.Interface.SendEvent(new OnlineAccountChangedEvent(AuthenticatedSnapshot()));

            ui.RoomButton.onClick.Invoke();

            var roomCtrl = UIHelper.GetUICtrl<OnlineRoomUICtrl>();
            Assert.That(roomCtrl, Is.Not.Null);
            Assert.That(roomCtrl.IsShow(), Is.True);
            Assert.That(EditModeSceneIsolation.FindObjectsInActiveTestScene<OnlineRoomUI>(), Is.Not.Empty);
        }

        [UnityTest]
        public IEnumerator OnlineWorldButtonFromLoadedBaseWithoutRoomEntersAndLeaveRestoresBase()
        {
            var ui = ShowLoginUi();
            var login = harness.Account.LoginAsync("world_ui_user", "Pass_123");
            yield return WaitUntil("login request", () => harness.Network.PendingCount<C2G_Login>() == 1);
            harness.Network.CompleteNextLoginSuccess();
            yield return WaitUntil("login completion", () => login.IsCompleted);
            yield return WaitUntil("automatic world restore", () => harness.Network.PendingCount<C2G_GetCurrentWorld>() == 1);
            harness.Network.CompleteNextCurrentWorldEmpty();
            yield return WaitUntil("world button ready after restore", () => ui.WorldButton.interactable);
            // 使用正式创建入口建立增量存档清单，保证随后 Flush 有可追加的磁盘基准。
            Assert.That(Game.Interface.GetSystem<SaveSystem>().CreateBase(out var baseData, out var createError), Is.True, createError);
            Game.Interface.GetModel<SaveModel>().SetBase(baseData, true);
            Game.Interface.GetSystem<SaveSystem>().MarkDirty("world UI base entry test");
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(
                MapIds.Base,
                TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 700),
                MapPreparationMode.OfflineFull);

            Assert.That(ui.WorldButton.interactable, Is.True);
            Assert.That(RuntimeListenerDescription(ui.WorldButton.onClick), Does.Contain(nameof(OnlineLoginUICtrl)));
            ui.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero, "opening selector must not enter");
            ClickOnlineMapRow();
            yield return WaitUntil("enter world request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);
            Assert.That(harness.Network.PendingEnterMapId, Is.EqualTo(MapIds.SurvivalSample));

            OnlineWorldSystem system = Game.Interface.GetSystem<OnlineWorldSystem>();
            ET.OnlineWorldSnapshot world = CreateWorldSnapshot(77);
            harness.Network.CompleteNextEnterWorld(world);
            yield return WaitUntil("final world convergence", () => harness.Network.PendingCount<C2G_GetCurrentWorld>() == 1);
            harness.Network.CompleteNextCurrentWorld(world);
            yield return WaitUntil("world established", () => system.Snapshot.HasWorld && system.Snapshot.OperationState == OnlineWorldOperationState.Idle);
            Assert.That(system.Snapshot.SelfUnitId, Is.EqualTo(77));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Not.Null);
            Assert.That(UIHelper.GetUICtrl<OnlineLoginUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);

            OnlineLoginUI reopened = ShowLoginUi();
            yield return WaitUntil("world leave button ready", () => reopened.WorldButton.interactable);
            reopened.WorldButton.onClick.Invoke();
            yield return WaitUntil("leave world request", () => harness.Network.PendingCount<C2G_LeaveWorld>() == 1);
            harness.Network.CompleteNextLeaveWorld(0);
            yield return WaitUntil("world leave completion", () => !system.Snapshot.HasWorld);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.Not.Null);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.Base));
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMode, Is.EqualTo(MapPreparationMode.OfflineFull));
            Assert.That(reopened.WorldButton.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator OnlineRoomWorldButtonUsesRealEntryFlowAndClosesMenusOnSuccess()
        {
            OnlineLoginUI loginUi = ShowLoginUi();
            Task<OnlineAccountOperationResult> login = harness.Account.LoginAsync("room_world_user", "Pass_123");
            yield return WaitUntil("room world login request", () => harness.Network.PendingCount<C2G_Login>() == 1);
            harness.Network.CompleteNextLoginSuccess();
            yield return WaitUntil("room world login completion", () => login.IsCompleted);
            yield return WaitUntil("room world automatic restore", () => harness.Network.PendingCount<C2G_GetCurrentWorld>() == 1);
            harness.Network.CompleteNextCurrentWorldEmpty();
            yield return WaitUntil("room world restore completion", () => Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.OperationState == OnlineWorldOperationState.Idle);

            loginUi.RoomButton.onClick.Invoke();
            OnlineRoomUI roomUi = ShowRoomUi();
            Game.Interface.GetModel<OnlineRoomModel>().Publish(JoinedRoomSnapshot());
            Assert.That(roomUi.WorldButton.interactable, Is.True);
            Assert.That(RuntimeListenerDescription(roomUi.WorldButton.onClick), Does.Contain(nameof(OnlineRoomUICtrl)));

            roomUi.WorldButton.onClick.Invoke();
            roomUi.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero, "room entry only opens selector");
            ClickOnlineMapRow();
            ClickOnlineMapRow(expectIdle: false);
            yield return WaitUntil("room enter request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.EqualTo(1));
            ET.OnlineWorldSnapshot world = CreateWorldSnapshot(177);
            harness.Network.CompleteNextEnterWorld(world);
            yield return WaitUntil("room final convergence", () => harness.Network.PendingCount<C2G_GetCurrentWorld>() == 1);
            harness.Network.CompleteNextCurrentWorld(world);
            yield return WaitUntil("room world established", () => Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.HasWorld);

            Assert.That(UIHelper.GetUICtrl<OnlineRoomUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<OnlineLoginUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<StartUICtrl>()?.IsShow() ?? false, Is.False);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [UnityTest]
        public IEnumerator OnlineWorldButtonBaseSaveFailureKeepsBaseAndShowsSpecificPrompt()
        {
            yield return LoginAndRestoreEmpty("base_save_fail");
            OnlineLoginUI loginUi = ShowLoginUi();
            IMapContentHandle baseHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, 702);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, baseHandle, MapPreparationMode.OfflineFull);
            SaveSystem save = Game.Interface.GetSystem<SaveSystem>();
            save.MarkDirty("world UI save failure");
            save.FailNextOfflineStorageCleanupForTests();

            loginUi.WorldButton.onClick.Invoke();
            ClickOnlineMapRow();
            yield return WaitUntil("base save failure prompt", () =>
                VisibleTipTexts().Contains(OnlineWorldText.Result(OnlineWorldResultCode.BaseSaveFailed)));

            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentHandle, Is.SameAs(baseHandle));
            Assert.That(baseHandle.IsValid, Is.True);
            Assert.That(UIHelper.GetUICtrl<OnlineLoginUICtrl>().IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [UnityTest]
        public IEnumerator OnlineRoomWorldButtonRejectsDuplicateAndKeepsRoomOnFailure()
        {
            yield return LoginAndRestoreEmpty("room_fail");
            OnlineRoomUI roomUi = OpenJoinedRoom();

            roomUi.WorldButton.onClick.Invoke();
            roomUi.WorldButton.onClick.Invoke();
            ClickOnlineMapRow();
            ClickOnlineMapRow(expectIdle: false);
            yield return WaitUntil("single room world request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);
            Assert.That(harness.Network.TotalEnterRequests, Is.EqualTo(1));
            harness.Network.CompleteNextEnterWorldError(200123);
            yield return WaitUntil("room failure prompt", () =>
                VisibleTipTexts().Contains(OnlineWorldText.Result(OnlineWorldResultCode.ServiceUnavailable)));

            Assert.That(UIHelper.GetUICtrl<OnlineRoomUICtrl>().IsShow(), Is.True);
            Assert.That(roomUi.WorldButton.interactable, Is.True);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
            Assert.That(harness.Network.TotalEnterRequests, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OnlineRoomWorldButtonStateMatrixBlocksUnavailableRoomStates()
        {
            yield return LoginAndRestoreEmpty("room_matrix");
            OnlineRoomUI roomUi = OpenJoinedRoom();
            Game.Interface.GetModel<OnlineRoomModel>().Publish(JoinedRoomSnapshot(connectionLost: true));
            Assert.That(roomUi.WorldButton.interactable, Is.False);
            roomUi.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero);

            Game.Interface.GetModel<OnlineRoomModel>().Publish(JoinedRoomSnapshot(operation: OnlineRoomOperationState.Joining));
            Assert.That(roomUi.WorldButton.interactable, Is.False);
            roomUi.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero);

            Game.Interface.GetModel<OnlineRoomModel>().Publish(JoinedRoomSnapshot());
            Game.Interface.GetModel<OnlineWorldModel>().Publish(
                OnlineWorldSnapshot.Empty().WithOperation(OnlineWorldOperationState.Entering, OnlineWorldResultCode.None));
            Assert.That(roomUi.WorldButton.interactable, Is.False);
            roomUi.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero);

            Game.Interface.GetModel<OnlineWorldModel>().Publish(new OnlineWorldSnapshot(
                true, 1, "map.another-day.survival-sample", 77, 1,
                Array.Empty<OnlineWorldPlayerSnapshot>(), false,
                OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success));
            Assert.That(roomUi.WorldButton.interactable, Is.False);
            roomUi.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.PendingCount<C2G_EnterWorld>(), Is.Zero);

            Game.Interface.GetModel<OnlineWorldModel>().Publish(OnlineWorldSnapshot.Empty());
            Assert.That(roomUi.WorldButton.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator OnlineRoomWorldButtonLeavePushSwitchesToEmptyWithoutDuplicate()
        {
            yield return LoginAndRestoreEmpty("room_leave");
            OnlineRoomUI roomUi = OpenJoinedRoom();
            roomUi.WorldButton.onClick.Invoke();
            ClickOnlineMapRow();
            yield return WaitUntil("room leave push pending world request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);

            Game.Interface.GetModel<OnlineRoomModel>().Publish(OnlineRoomSnapshot.Empty());
            Assert.That(roomUi.EmptyRoot.activeSelf, Is.True);
            Assert.That(roomUi.JoinedRoot.activeSelf, Is.False);
            Assert.That(roomUi.WorldButton.interactable, Is.False);
            roomUi.WorldButton.onClick.Invoke();
            Assert.That(harness.Network.TotalEnterRequests, Is.EqualTo(1));

            harness.Network.CompleteNextEnterWorldError(200123);
            yield return WaitUntil("leave push request completed", () => harness.Network.PendingCount<C2G_EnterWorld>() == 0);
            Assert.That(roomUi.EmptyRoot.activeSelf, Is.True);
            Assert.That(harness.Network.TotalEnterRequests, Is.EqualTo(1));
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [UnityTest]
        public IEnumerator OnlineRoomHideKeepsVisibleSelectorAsFailureOwner()
        {
            yield return LoginAndRestoreEmpty("room_hide");
            OnlineRoomUI roomUi = OpenJoinedRoom();
            roomUi.WorldButton.onClick.Invoke();
            ClickOnlineMapRow();
            yield return WaitUntil("hidden room pending world request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);
            UIHelper.HideUI<OnlineRoomUICtrl>();
            string[] tipsBefore = VisibleTipTexts();

            harness.Network.CompleteNextEnterWorldError(200123);
            yield return WaitUntil("hidden room request completed", () => harness.Network.PendingCount<C2G_EnterWorld>() == 0);
            yield return null;

            Assert.That(UIHelper.GetUICtrl<OnlineRoomUICtrl>().IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>().IsShow(), Is.True);
            Assert.That(VisibleTipTexts(), Does.Contain(OnlineWorldText.Result(OnlineWorldResultCode.ServiceUnavailable)));
            Assert.That(VisibleTipTexts().Length, Is.EqualTo(tipsBefore.Length + 1));
        }

        [UnityTest]
        public IEnumerator DestroyedRoomReplacementStaysUntouchedWhileSelectorOwnsException()
        {
            yield return LoginAndRestoreEmpty("room_reload");
            OnlineRoomUI oldUi = OpenJoinedRoom();
            oldUi.WorldButton.onClick.Invoke();
            ClickOnlineMapRow();
            yield return WaitUntil("destroyed room pending world request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);
            Object.DestroyImmediate(oldUi.gameObject);

            OnlineRoomUI replacement = ShowRoomUi();
            Assert.That(replacement, Is.Not.SameAs(oldUi));
            OnlineRoomSnapshot replacementSnapshot = JoinedRoomSnapshot(connectionLost: true);
            Game.Interface.GetModel<OnlineRoomModel>().Publish(replacementSnapshot);
            Assert.That(replacement.gameObject.activeInHierarchy, Is.True);
            Assert.That(replacement.DisconnectHintRoot.activeSelf, Is.True);
            Assert.That(replacement.WorldButton.interactable, Is.False);
            string[] tipsBefore = VisibleTipTexts();

            harness.Network.FailNext<C2G_EnterWorld>(new InvalidOperationException("injected replacement exception"));
            yield return WaitUntil("destroyed room request completed", () => harness.Network.PendingCount<C2G_EnterWorld>() == 0);
            // RPC 出队不代表 await 后的界面刷新已完成，等待实际可见结果再检查替换界面。
            yield return WaitUntil("destroyed room exception prompt", () =>
                VisibleTipTexts().Contains(OnlineWorldText.Result(OnlineWorldResultCode.UnknownFailure)));

            Assert.That(UIHelper.GetUICtrl<OnlineRoomUICtrl>().IsShow(), Is.True);
            Assert.That(replacement.gameObject.activeInHierarchy, Is.True);
            Assert.That(replacement.DisconnectHintRoot.activeSelf, Is.True);
            Assert.That(replacement.WorldButton.interactable, Is.False);
            Assert.That(VisibleTipTexts(), Does.Contain(OnlineWorldText.Result(OnlineWorldResultCode.UnknownFailure)));
            Assert.That(VisibleTipTexts().Length, Is.EqualTo(tipsBefore.Length + 1));
            Assert.That(harness.Network.TotalEnterRequests, Is.EqualTo(1));
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [UnityTest]
        public IEnumerator OnlineRoomWorldButtonVisibleNetworkExceptionKeepsRoomAndClosesLoading()
        {
            yield return LoginAndRestoreEmpty("room_except");
            OnlineRoomUI roomUi = OpenJoinedRoom();
            roomUi.WorldButton.onClick.Invoke();
            ClickOnlineMapRow();
            yield return WaitUntil("visible room pending world request", () => harness.Network.PendingCount<C2G_EnterWorld>() == 1);

            harness.Network.FailNext<C2G_EnterWorld>(new InvalidOperationException("injected visible exception"));
            yield return WaitUntil("visible exception prompt", () =>
                VisibleTipTexts().Contains(OnlineWorldText.Result(OnlineWorldResultCode.UnknownFailure)));

            Assert.That(UIHelper.GetUICtrl<OnlineRoomUICtrl>().IsShow(), Is.True);
            Assert.That(roomUi.WorldButton.interactable, Is.True);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
        }

        private static ET.OnlineWorldSnapshot CreateWorldSnapshot(long selfUnitId)
        {
            var player = WorldPlayerInfo.Create();
            player.PlayerId = 23;
            player.UnitId = selfUnitId;
            player.DisplayName = "world_ui_user";
            player.WorldId = 1;
            player.MapId = "map.another-day.survival-sample";
            player.PlayerConfigId = "player.graybox.default";
            player.PositionY = 0.1f;
            player.PositionZ = -14f;
            player.ConnectionState = 1;
            player.StateRevision = 1;
            var snapshot = ET.OnlineWorldSnapshot.Create();
            snapshot.WorldId = 1;
            snapshot.MapId = player.MapId;
            snapshot.SelfUnitId = selfUnitId;
            snapshot.WorldRevision = 1;
            snapshot.Players.Add(player);
            return snapshot;
        }

        [Test]
        public void CloseButton_UsesRootPanelTopRightAndDoesNotOverlapTitleOrAuthenticatedActions()
        {
            var prefab = LoadPrefab();
            var ui = prefab.GetComponent<OnlineLoginUI>();
            var close = ui.CloseButton.GetComponent<RectTransform>();
            var title = prefab.transform.Find("LoginContent/Title").GetComponent<RectTransform>();

            Assert.That(close.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(close.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(close.pivot, Is.EqualTo(Vector2.one));
            Assert.That(close.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(close.sizeDelta, Is.EqualTo(new Vector2(220f, 128f)));
            Assert.That(WorldRect(close).Overlaps(WorldRect(title)), Is.False);
            Assert.That(WorldRect(close).Overlaps(WorldRect(ui.RoomButton.GetComponent<RectTransform>())), Is.False);
            Assert.That(WorldRect(close).Overlaps(WorldRect(ui.LogoutButton.GetComponent<RectTransform>())), Is.False);
        }

        [Test]
        public void AccountSessionClosedEvent_RefreshesBackToAnonymousState()
        {
            var ui = ShowLoginUi();
            Game.Interface.SendEvent(new OnlineAccountChangedEvent(AuthenticatedSnapshot()));
            Assert.That(ui.AuthenticatedRoot.activeSelf, Is.True);

            Game.Interface.SendEvent(new OnlineAccountChangedEvent(OnlineAccountSnapshot.Empty("review_user")));

            AssertAnonymous(ui);
        }

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            return prefab;
        }

        private static OnlineLoginUI ShowLoginUi()
        {
            var ctrl = UIHelper.ShowUI<OnlineLoginUICtrl>();
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.IsShow(), Is.True);
            var ui = typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl) as OnlineLoginUI;
            Assert.That(ui, Is.Not.Null);
            return ui;
        }

        private static OnlineRoomUI ShowRoomUi()
        {
            var ctrl = UIHelper.GetUICtrl<OnlineRoomUICtrl>();
            if (ctrl == null || !ctrl.IsShow())
            {
                ctrl = UIHelper.ShowUI<OnlineRoomUICtrl>();
            }
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.IsShow(), Is.True);
            var ui = typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl) as OnlineRoomUI;
            Assert.That(ui, Is.Not.Null);
            return ui;
        }

        private static void ClickOnlineMapRow(int index = 0, bool expectIdle = true)
        {
            MapSelectionUICtrl ctrl = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            Assert.That(ctrl, Is.Not.Null, "online map selector was not created");
            Assert.That(ctrl.IsShow(), Is.True, "online map selector was not shown");
            MapSelectionUI ui = EditModeSceneIsolation.FindObjectsInActiveTestScene<MapSelectionUI>().Single();
            var rows = ui.MapList.GetDraggableItems();
            // 地图目录会随内容扩充，联机入口应展示完整目录，不固定旧版三张地图。
            Assert.That(rows, Has.Count.EqualTo(MapCatalog.GetExpeditionMaps().Count));
            MapSelectionOptionBoxUI row = rows[index].GetComponent<MapSelectionOptionBoxUI>();
            Assert.That(row.TextState.text, Is.Empty, "online rows must not display offline unlock cost/state");
            Assert.That(ui.ActionText.text, Is.EqualTo(Loc.T(expectIdle
                ? "ui.map_selection.action.explore"
                : "ui.map_selection.action.processing")));
            row.BtnSelect.onClick.Invoke();
            ui.BtnAction.onClick.Invoke();
        }

        private IEnumerator LoginAndRestoreEmpty(string userName)
        {
            Task<OnlineAccountOperationResult> login = harness.Account.LoginAsync(userName, "Pass_123");
            yield return WaitUntil("login request", () => harness.Network.PendingCount<C2G_Login>() == 1);
            harness.Network.CompleteNextLoginSuccess();
            yield return WaitUntil("login completion", () => login.IsCompleted);
            yield return WaitUntil("automatic empty world restore", () => harness.Network.PendingCount<C2G_GetCurrentWorld>() == 1);
            harness.Network.CompleteNextCurrentWorldEmpty();
            yield return WaitUntil("empty world restore completion", () =>
                Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.OperationState == OnlineWorldOperationState.Idle);
        }

        private static OnlineRoomUI OpenJoinedRoom()
        {
            ShowLoginUi().RoomButton.onClick.Invoke();
            OnlineRoomUI roomUi = ShowRoomUi();
            Game.Interface.GetModel<OnlineRoomModel>().Publish(JoinedRoomSnapshot());
            Assert.That(roomUi.WorldButton.interactable, Is.True);
            return roomUi;
        }

        private static string[] VisibleTipTexts()
        {
            return EditModeSceneIsolation.FindObjectsInActiveTestScene<FloatTextBoxUI>()
                .Where(box => box != null && box.gameObject.activeInHierarchy && box.TextTips != null &&
                              !string.IsNullOrWhiteSpace(box.TextTips.text))
                .Select(box => box.TextTips.text)
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToArray();
        }

        private static OnlineRoomSnapshot JoinedRoomSnapshot(
            bool connectionLost = false,
            OnlineRoomOperationState operation = OnlineRoomOperationState.Idle)
        {
            return new OnlineRoomSnapshot(
                true, 500, "ABC234", 23, 1,
                new[] { new OnlineRoomMemberSnapshot(23, "room_world_user", true, OnlineRoomMemberConnectionState.Online, 1) },
                connectionLost, operation, OnlineRoomResultCode.None);
        }

        private static void AssertAnonymous(OnlineLoginUI ui)
        {
            Assert.That(ui.AnonymousRoot.activeSelf, Is.True);
            Assert.That(ui.AuthenticatedRoot.activeSelf, Is.False);
            Assert.That(ui.RoomButton.gameObject.activeInHierarchy, Is.False);
            Assert.That(ui.LogoutButton.gameObject.activeInHierarchy, Is.False);
        }

        private static OnlineAccountSnapshot AuthenticatedSnapshot()
        {
            return new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,
                OnlineOperationState.Idle,
                OnlineAccountResultCode.Success,
                "review_user",
                17,
                23,
                29,
                true,
                "review_user");
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static int RuntimeListenerCount(Button.ButtonClickedEvent buttonEvent)
        {
            var calls = typeof(UnityEngine.Events.UnityEventBase)
                .GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(buttonEvent);
            var runtimeCalls = calls?.GetType()
                .GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(calls) as ICollection;
            return runtimeCalls?.Count ?? 0;
        }

        private static string RuntimeListenerDescription(Button.ButtonClickedEvent buttonEvent)
        {
            var calls = typeof(UnityEngine.Events.UnityEventBase)
                .GetField("m_Calls", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(buttonEvent);
            var runtimeCalls = calls?.GetType()
                .GetField("m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(calls) as IEnumerable;
            if (runtimeCalls == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var call in runtimeCalls)
            {
                var target = call.GetType().GetField("Delegate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(call) as Delegate;
                parts.Add(target?.Target?.GetType().FullName ?? call.GetType().FullName);
            }

            return string.Join("|", parts);
        }

        private string PendingDiagnostics(string stage, OnlineLoginUI ui, int? listenersBeforeClick = null)
        {
            string before = listenersBeforeClick.HasValue ? $" listenersBefore={listenersBeforeClick.Value}" : string.Empty;
            return $"{stage}:{before} listenersNow={RuntimeListenerCount(ui.LogoutButton.onClick)} " +
                   $"pendingTotal={harness.Network.PendingCallCount} pendingLogout={harness.Network.PendingLogoutCount} " +
                   $"pendingTypes=[{harness.Network.PendingRequestSummary}]";
        }

        private static IEnumerator WaitUntil(string label, Func<bool> condition)
        {
            var timeout = Time.realtimeSinceStartup + 5f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup >= timeout)
                {
                    var world = Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot;
                    var map = Game.Interface.GetModel<MapSceneModel>();
                    Assert.Fail($"{label}: world={world.OperationState}/{world.LastResultCode}; map={map.CurrentMapId}/{map.Status.Value}/{map.CurrentMode}; tips={string.Join(";", VisibleTipTexts())}");
                }
                yield return null;
            }
        }

        private static OnlineHarness CreateHarness()
        {
            var network = new FakeOnlineNetwork();
            var mapLoader = new UiWorldMapLoader();
            var config = new FakeNetworkConfigProvider();
            var preferences = new FakeOnlineAccountPreferenceStore();
            Game.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<INetworkRuntime>(network);
                architecture.RegisterUtility<INetworkConfigProvider>(config);
                architecture.RegisterUtility<IOnlineAccountPreferenceStore>(preferences);
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
                architecture.RegisterSystem<OptionalGameplayResourceSystem>(new ReadyGameplayResources());
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider(includeMapPlayerSpawn: true));
            };

            var account = Game.Interface.GetSystem<OnlineAccountSystem>();
            return new OnlineHarness(account, network, mapLoader, config);
        }

        private static void ResetGameArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }
            else
            {
                field?.SetValue(null, null);
            }
        }

        private static void CreateUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null)
            {
                eventSystem.AddComponent(inputModuleType);
            }
            eventSystem.transform.SetParent(root.transform, false);
        }

        private readonly struct FieldExpectation
        {
            public FieldExpectation(string name, Type type, string globalObjectId)
            {
                Name = name;
                Type = type;
                GlobalObjectId = globalObjectId;
            }

            public string Name { get; }
            public Type Type { get; }
            public string GlobalObjectId { get; }
        }

        private sealed class OnlineHarness
        {
            public OnlineHarness(OnlineAccountSystem account, FakeOnlineNetwork network, UiWorldMapLoader mapLoader, FakeNetworkConfigProvider config)
            {
                Account = account;
                Network = network;
                MapLoader = mapLoader;
                Config = config;
            }

            public OnlineAccountSystem Account { get; }
            public FakeOnlineNetwork Network { get; }
            public UiWorldMapLoader MapLoader { get; }
            public FakeNetworkConfigProvider Config { get; }
        }

        /// <summary>测试地图加载器提供现成资源；入口与地图事务查询同一就绪结果，隔离 YooAsset 清单及下载。</summary>
        private sealed class ReadyGameplayResources : OptionalGameplayResourceSystem
        {
            public override OptionalGameplayResourceQuery QueryMap(string mapId)
                => new OptionalGameplayResourceQuery(OptionalGameplayResourceState.Ready, "test", 0, 0, string.Empty);
        }

        private sealed class UiWorldMapLoader : IMapContentLoader
        {
            public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default)
            {
                onProgress?.Invoke(1f);
                return UniTask.FromResult(MapContentLoadResult.Ok(TemporaryBuildingBehaviorTestSupport.CreateMapHandle(mapId, 701)));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
            {
                if (handle is FakeMapHandle fake && fake.Root != null) Object.DestroyImmediate(fake.Root);
                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }

        private sealed class FakeNetworkConfigProvider : IEditableNetworkConfigProvider
        {
            public string SavedEndpoint { get; private set; } = "127.0.0.1:31001";

            public Task<NetworkConfigResult> LoadAsync(CancellationToken cancellationToken)
            {
                NetworkEndpoint.TryParse(SavedEndpoint, out NetworkEndpoint endpoint);
                return Task.FromResult(NetworkConfigResult.Ok(endpoint));
            }

            public bool TrySaveEndpoint(string value, out NetworkEndpoint endpoint, out string diagnostic)
            {
                if (!NetworkEndpoint.TryParse(value, out endpoint))
                {
                    diagnostic = "endpoint";
                    return false;
                }
                SavedEndpoint = endpoint.ToString();
                diagnostic = null;
                return true;
            }
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
                    default:
                        if (path.StartsWith("Prefabs/", StringComparison.Ordinal))
                        {
                            return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab") as T;
                        }
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class FakeOnlineAccountPreferenceStore : IOnlineAccountPreferenceStore
        {
            public string LoadLastUserName()
            {
                return string.Empty;
            }

            public bool SaveLastUserName(string userName, out string error)
            {
                error = null;
                return true;
            }
        }

        private sealed class FakeOnlineNetwork : INetworkRuntime, INetworkResponseObserver
        {
            private readonly List<PendingCall> pendingCalls = new List<PendingCall>();
            private int totalEnterRequests;

            public bool IsInitialized { get; private set; } = true;
            public NetworkConnectionState ConnectionState { get; private set; } = NetworkConnectionState.Disconnected;
            public int ConnectionGeneration { get; private set; }
            public int PendingCallCount => pendingCalls.Count;
            public int PendingLogoutCount => pendingCalls.Count(call => call.Request is C2G_Logout);
            public int PendingCount<TRequest>() where TRequest : class, IRequest => pendingCalls.Count(call => call.Request is TRequest);
            public int TotalEnterRequests => totalEnterRequests;
            public string PendingEnterMapId => pendingCalls.Select(call => call.Request).OfType<C2G_EnterWorld>().Single().MapId;
            public string PendingRequestSummary => string.Join(
                ",",
                pendingCalls
                    .GroupBy(call => call.Request.GetType().Name)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}:{group.Count()}"));

            public void Initialize()
            {
                IsInitialized = true;
            }

            public void Update()
            {
            }

            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectionGeneration++;
                ConnectionState = NetworkConnectionState.Connected;
                return Task.CompletedTask;
            }

            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                var call = new PendingCall(request);
                pendingCalls.Add(call);
                if (request is C2G_EnterWorld) totalEnterRequests++;
                return call.Task.ContinueWith(task => (TResponse)task.GetAwaiter().GetResult(), CancellationToken.None);
            }

            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler)
                where TMessage : class, IMessage
            {
                return new DelegateSubscription(() => { });
            }

            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler)
            {
                return new DelegateSubscription(() => { });
            }

            public IDisposable RegisterResponseHandler<TResponse>(Action<TResponse> handler) where TResponse : class, IResponse
            {
                return new DelegateSubscription(() => { });
            }

            public void Disconnect(NetworkDisconnectReason reason)
            {
                ConnectionState = NetworkConnectionState.Disconnected;
            }

            public void Dispose()
            {
                IsInitialized = false;
                ConnectionState = NetworkConnectionState.Disposed;
            }

            public void CompleteNextLoginSuccess()
            {
                var call = TakeNext<C2G_Login>();
                var response = G2C_Login.Create();
                response.Error = 0;
                response.AccountId = 17;
                response.PlayerId = 23;
                response.SessionEpoch = 29;
                response.IsPlayerActorReused = true;
                call.TrySetResult(response);
            }

            public void CompleteNextLogoutSuccess()
            {
                var call = TakeNext<C2G_Logout>();
                var response = G2C_Logout.Create();
                response.Error = 0;
                call.TrySetResult(response);
            }

            public void CompleteNextCurrentWorldEmpty()
            {
                var call = TakeNext<C2G_GetCurrentWorld>();
                var response = G2C_GetCurrentWorld.Create();
                response.Error = 0;
                response.HasWorld = false;
                call.TrySetResult(response);
            }

            public void CompleteNextEnterWorld(ET.OnlineWorldSnapshot snapshot)
            {
                var call = TakeNext<C2G_EnterWorld>();
                var response = G2C_EnterWorld.Create();
                response.Error = 0;
                response.HasWorld = true;
                response.World = snapshot;
                call.TrySetResult(response);
            }

            public void CompleteNextEnterWorldError(int error)
            {
                var call = TakeNext<C2G_EnterWorld>();
                var response = G2C_EnterWorld.Create();
                response.Error = error;
                response.HasWorld = false;
                call.TrySetResult(response);
            }

            public void FailNext<TRequest>(Exception exception)
                where TRequest : class, IRequest
            {
                TakeNext<TRequest>().TrySetException(exception);
            }

            public void CompleteNextCurrentWorld(ET.OnlineWorldSnapshot snapshot)
            {
                var call = TakeNext<C2G_GetCurrentWorld>();
                var response = G2C_GetCurrentWorld.Create();
                response.Error = 0;
                response.HasWorld = true;
                response.World = snapshot;
                call.TrySetResult(response);
            }

            public void CompleteNextLeaveWorld(int error)
            {
                var call = TakeNext<C2G_LeaveWorld>();
                var response = G2C_LeaveWorld.Create();
                response.Error = error;
                call.TrySetResult(response);
            }

            private PendingCall TakeNext<TRequest>()
                where TRequest : class, IRequest
            {
                int index = pendingCalls.FindIndex(call => call.Request is TRequest);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"No pending {typeof(TRequest).Name} call.");
                var call = pendingCalls[index];
                pendingCalls.RemoveAt(index);
                return call;
            }

            private sealed class PendingCall
            {
                private readonly TaskCompletionSource<IResponse> completion =
                    new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

                public PendingCall(IRequest request)
                {
                    Request = request;
                }

                public IRequest Request { get; }
                public Task<IResponse> Task => completion.Task;

                public void TrySetResult(IResponse response)
                {
                    completion.TrySetResult(response);
                }

                public void TrySetException(Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
        }

        private sealed class DelegateSubscription : IDisposable
        {
            private readonly Action dispose;

            public DelegateSubscription(Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                dispose?.Invoke();
            }
        }
    }
}
