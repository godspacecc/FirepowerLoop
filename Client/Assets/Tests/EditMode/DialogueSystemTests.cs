using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过正式架构事件验证对话与玩家相机的释放边界，不加载或改写正式地图。</summary>
    public sealed class DialogueSystemTests
    {
        private Ports ports;
        private DialogueDefinition graph;
        private LocaleLoader localeLoader;
        private GameObject uiRoot;
        private const int NpcId = 21;
        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests(); TestReset.ResetArchitecture();
            localeLoader = new LocaleLoader();
            ResourceLoader.SetLoaderForTests(localeLoader);
            ports = new Ports();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(ports);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider());
            };
            var architecture = Game.Interface;
            // 集成测试绕过启动资源加载，显式加载正式本地化，避免把未初始化资源当作缺失台词。
            var localization = architecture.GetUtility<LocalizationUtility>();
            localization.InitializeConfiguredTables();
            localization.LoadFromJson("zh-CN", System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            architecture.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", "", 100, 10, 1, 1, 1, 1, 5), false);
            architecture.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            architecture.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            var units = architecture.GetSystem<UnitSystem>();
            Assert.That(units.TryRegister(new UnitSnapshot(1, 9, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
            Assert.That(units.TryRegister(new UnitSnapshot(NpcId, 9, UnitType.Npc, UnitSource.MapPlaced, "npc.test", Vector3.forward, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out error), Is.True, error);
            architecture.SendEvent(new PlayerReadyEvent(1, 9));
            Assert.That(architecture.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out error), Is.True, error);
            graph = new DialogueDefinition("test", "npc.test", "start", new[]
            {
                new DialogueNodeDefinition("start", "dialogue.test.intro", "", new[]
                { new DialogueOptionDefinition("function", 1, "dialogue.test.function", global::DialogueOptionAction.OpenNpcFunction, "") })
            });
        }
        [TearDown]
        public void TearDown()
        {
            var system = Game.Interface.GetSystem<DialogueSystem>();
            if (system.Current != null) system.Close(system.Current.SessionId);
            UIHelper.ResetForTests();
            if (uiRoot != null) UnityEngine.Object.DestroyImmediate(uiRoot);
            localeLoader.Dispose();
            Architecture<Game>.OnRegisterPatch = _ => { }; TestReset.ResetArchitecture();
        }
        private DialogueSnapshot Begin()
        {
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().TryBegin(graph, NpcId, 9, "npc.role.map_guide", out var error), Is.True, error);
            Assert.That(ports.DialogueHidden, Is.True);
            return Game.Interface.GetSystem<DialogueSystem>().Current;
        }
        [TestCase(PlayerCameraMode.Shoulder)]
        [TestCase(PlayerCameraMode.TopDown)]
        [TestCase(PlayerCameraMode.FirstPerson)]
        public void Conversation_HidesLocalPresentationAndRejectsStaleRelease(PlayerCameraMode mode)
        {
            Assert.That(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(mode, out var error), Is.True, error);
            var previous = Begin();
            Assert.That(ports.DialogueHidden, Is.True);
            Game.Interface.GetSystem<DialogueSystem>().Close(previous.SessionId);
            Assert.That(ports.DialogueHidden, Is.False);
            var current = Begin();
            Game.Interface.GetSystem<PlayerCameraSystem>().EndDialogueCamera(previous.SessionId);
            Assert.That(ports.DialogueHidden, Is.True, "旧会话不能恢复新会话隐藏的主角");
            Game.Interface.GetSystem<DialogueSystem>().Close(current.SessionId);
            Assert.That(ports.DialogueHidden, Is.False);
        }
        [Test]
        public void NpcRemoval_ClosesAndReleasesCamera()
        {
            Begin();Assert.That(ports.Focus.HasValue, Is.True);
            Game.Interface.GetSystem<UnitSystem>().Remove(NpcId, UnitRemovedReason.Explicit);
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(ports.Focus.HasValue, Is.False);
            Assert.That(ports.DialogueHidden, Is.False);
        }
        [Test]
        public void PlayerDeath_ClosesImmediately()
        {
            Begin();Game.Interface.SendEvent(new CombatantDiedEvent(1, 0));
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(ports.Focus.HasValue, Is.False);
            Assert.That(ports.DialogueHidden, Is.False);
        }
        [Test]
        public void MapLoading_ClosesWithoutRestoringOldBinding()
        {
            Begin();Game.Interface.SendEvent(new MapLoadingStartedEvent("next"));
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(ports.Focus.HasValue, Is.False);
            Assert.That(ports.DialogueHidden, Is.False);
        }
        [Test]
        public void ModeChange_EndsDialogueAndPreservesRequestedMode()
        {
            Begin();Assert.That(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.FirstPerson, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(Game.Interface.GetSystem<PlayerCameraSystem>().Mode, Is.EqualTo(PlayerCameraMode.FirstPerson));
            Assert.That(ports.Focus.HasValue, Is.False);
            Assert.That(ports.DialogueHidden, Is.False);
        }
        [Test]
        public void FunctionRequest_IsSentOnceAfterCameraRelease()
        {
            var current = Begin();var requests = 0;
            var listener = Game.Interface.RegisterEvent<DialogueNpcFunctionRequestedEvent>(e =>
            {
                requests++;Assert.That(e.NpcUnitId, Is.EqualTo(NpcId));Assert.That(e.MapInstanceId, Is.EqualTo(9));
                Assert.That(ports.Focus.HasValue, Is.False);
                Assert.That(ports.DialogueHidden, Is.False);
                Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            });
            try
            {
                var system = Game.Interface.GetSystem<DialogueSystem>();
                Assert.That(system.Choose(current.SessionId, current.Revision, "function"), Is.True);
                Assert.That(system.Choose(current.SessionId, current.Revision, "function"), Is.False);
                Assert.That(requests, Is.EqualTo(1));
            }
            finally { listener.UnRegister(); }
        }
        [Test]
        public void CameraFailure_RollsBackAndAllowsRetry()
        {
            ports.AllowFocus = false;
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().TryBegin(graph, NpcId, 9, "npc.role.map_guide", out _), Is.False);
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(ports.DialogueHidden, Is.False);
            ports.AllowFocus = true;Begin();
        }
        private DialogueUI ShowDialogue()
        {
            uiRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            uiRoot.SetActive(false);
            foreach (var name in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            { var layer = new GameObject(name, typeof(RectTransform));layer.transform.SetParent(uiRoot.transform, false); }
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            eventSystem.transform.SetParent(uiRoot.transform, false);
            uiRoot.AddComponent<UIRoot>();uiRoot.SetActive(true);
            UIHelper.SetPrefabLoaderForTests(path => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            Assert.That(DialogueUICtrl.TryShow(out var error), Is.True, error);
            return uiRoot.GetComponentInChildren<DialogueUI>(true);
        }
        [Test]
        public void Dialogue_HidesUnavailableHudControlsAndRestoresWithoutReplacingVirtualDevice()
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            Assert.That(mobile.TrySetEditorSimulation(true), Is.True);
            Begin();
            var dialogue = ShowDialogue();
            var guideCtrl = UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var joystickCtrl = UIHelper.ShowUI<MobileJoystickUICtrl>();
            var guide = uiRoot.GetComponentInChildren<GameplayGuideHudUI>(true);
            var joystick = uiRoot.GetComponentInChildren<MobileJoystickUI>(true);
            var device = UnityEngine.InputSystem.Gamepad.current;
            Assert.That(guide.MobileBtnPause.gameObject.activeSelf, Is.True);
            Assert.That(joystick.InventoryButton.gameObject.activeSelf, Is.False);
            Assert.That(joystick.BuildButton.gameObject.activeSelf, Is.False);
            Assert.That(joystick.ContentVisibility.alpha, Is.Zero);
            Assert.That(joystick.ContentVisibility.blocksRaycasts, Is.False);
            Assert.That(guideCtrl.IsShow() && joystickCtrl.IsShow(), Is.True);
            dialogue.BtnClose.onClick.Invoke();
            Assert.That(guide.MobileBtnPause.gameObject.activeSelf, Is.True);
            Assert.That(joystick.ContentVisibility.alpha, Is.EqualTo(1f));
            Assert.That(joystick.SprintButton.gameObject.activeSelf, Is.True);
            Assert.That(UnityEngine.InputSystem.Gamepad.current, Is.SameAs(device));
            Begin();
            Assert.That(DialogueUICtrl.TryShow(out var error), Is.True, error);
            Assert.That(guide.MobileBtnPause.gameObject.activeSelf, Is.True);
            Assert.That(joystick.ContentVisibility.alpha, Is.Zero);
            Game.Interface.GetSystem<UnitSystem>().Remove(NpcId, UnitRemovedReason.Explicit);
            Assert.That(guide.MobileBtnPause.gameObject.activeSelf, Is.True);
            Assert.That(joystick.ContentVisibility.alpha, Is.EqualTo(1f));
        }

        [Test]
        public void PauseDuringDialogue_PointerAndEscapePreserveSnapshotAndResumeSameOptions()
        {
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            var snapshot = Begin();
            var ui = ShowDialogue();
            var context = new GameObject("PauseTestMap", typeof(MapContext)).GetComponent<MapContext>();
            context.transform.SetParent(uiRoot.transform, false);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new PauseTestMapHandle(context));
            UIHelper.ShowUI<GameplayGuideHudUICtrl>();
            var guide = uiRoot.GetComponentInChildren<GameplayGuideHudUI>(true);
            var choice = ui.ChoiceList.GetComponentInChildren<DialogueChoiceBoxUI>().BtnChoice;
            var body = ui.BodyText.text;
            Assert.That(guide.MobileBtnPause.gameObject.activeInHierarchy, Is.True);
            Canvas.ForceUpdateCanvases();
            var point = RectTransformUtility.WorldToScreenPoint(null, guide.MobileBtnPause.transform.position);
            var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { position = point, button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
            // EditMode 不自动登记 GraphicRaycaster；直接派发给真实背景 Relay，验证其区域转交逻辑。
            var background = ui.transform.parent.Find(ui.name + "_Background").gameObject;
            Assert.That(background.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            var outside = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { position = new Vector2(-1000, -1000) };
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(background, outside,
                UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(background, pointer,
                UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            LifeCycle.LateUpdate();
            Assert.That(UIHelper.GetUICtrl<DialogueUICtrl>().IsShow(), Is.True);
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.SameAs(snapshot));
            choice = ui.ChoiceList.GetComponentInChildren<DialogueChoiceBoxUI>().BtnChoice;
            Assert.That(choice.interactable, Is.False);
            choice.onClick.Invoke();
            ui.BtnClose.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.SameAs(snapshot));
            Assert.That(ports.DialogueHidden && ports.Focus.HasValue, Is.True);
            UIHelper.ShowUI<SettingUICtrl>();
            LifeCycle.LateUpdate();
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.SameAs(snapshot));
            var keys = Game.Interface.GetSystem<HotKeySystem>();
            Assert.That(keys.TryExecuteAction(HotKeyActionIds.TogglePause), Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(keys.TryExecuteAction(HotKeyActionIds.TogglePause), Is.True);
            LifeCycle.LateUpdate();
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.SameAs(snapshot));
            Assert.That(ui.BodyText.text, Is.EqualTo(body));
            Assert.That(ui.ChoiceList.GetComponentInChildren<DialogueChoiceBoxUI>().BtnChoice.interactable, Is.True);
            Assert.That(keys.TryExecuteAction(HotKeyActionIds.TogglePause), Is.True);
            LifeCycle.LateUpdate();
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.SameAs(snapshot));
            uiRoot.GetComponentInChildren<PauseUI>(true).BtnContinue.onClick.Invoke();
            LifeCycle.LateUpdate();
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.SameAs(snapshot));
            Assert.That(keys.TryExecuteAction(HotKeyActionIds.TogglePause), Is.True);
            Game.Interface.SendEvent(new MapLoadingStartedEvent("next"));
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(ports.DialogueHidden, Is.False);
        }

        [Test]
        public void UiClose_ReleasesInputAndCameraWithoutPausingWorld()
        {
            var state = Game.Interface.GetModel<AppStateModel>().CurrentState.Value;var timeScale=Time.timeScale;
            Begin();var ui=ShowDialogue();
            Assert.That(UIHelper.HasUIOpen, Is.True);
            Assert.That(ui.BodyText.text, Does.Contain("对话流程测试"));
            Assert.That(ui.ChoiceList.GetDraggableItems().Count, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(state));
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            ui.BtnClose.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(UIHelper.HasUIOpen, Is.False);Assert.That(ports.Focus.HasValue, Is.False);
            Assert.That(ports.DialogueHidden, Is.False);
        }
        [Test]
        public void UiFunctionClick_ClosesBeforeIntentAndRejectsRepeat()
        {
            Begin();var ui=ShowDialogue();var requests=0;
            var listener=Game.Interface.RegisterEvent<DialogueNpcFunctionRequestedEvent>(_ =>
            {requests++;Assert.That(UIHelper.HasUIOpen, Is.False);});
            try
            {
                var button=ui.ChoiceList.GetComponentInChildren<DialogueChoiceBoxUI>().BtnChoice;
                button.onClick.Invoke();button.onClick.Invoke();
                Assert.That(requests, Is.EqualTo(1));Assert.That(ports.Focus.HasValue, Is.False);
            }
            finally {listener.UnRegister();}
        }
        [Test]
        public void UiDestroyed_EndsConversationAndAllowsReopen()
        {
            Begin();var ui=ShowDialogue();
            // EditMode 的新实例不保证执行 MonoBehaviour.OnDestroy；按 UI 基础测试显式派发销毁生命周期。
            ui.OnTriggerDestroyEvent.Trigger();
            UnityEngine.Object.DestroyImmediate(ui.gameObject);
            Assert.That(Game.Interface.GetSystem<DialogueSystem>().Current, Is.Null);
            Assert.That(UIHelper.HasUIOpen, Is.False);Assert.That(ports.Focus.HasValue, Is.False);
            Assert.That(ports.DialogueHidden, Is.False);
            Begin();Assert.That(DialogueUICtrl.TryShow(out var error), Is.True, error);
        }
        private sealed class LocaleLoader : IResourceLoader
        {
            private readonly System.Collections.Generic.List<TextAsset> assets = new System.Collections.Generic.List<TextAsset>();
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                var file="Assets/Game/Config/Localization/"+path;
                if (!System.IO.File.Exists(file)) return null;
                var asset=new TextAsset(System.IO.File.ReadAllText(file));assets.Add(asset);return asset as T;
            }
            public ResourceLoadHandle LoadAssetAsync<T>(string path,System.Action<T> callback) where T : UnityEngine.Object
            { callback?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
            public void Dispose() {foreach(var asset in assets)UnityEngine.Object.DestroyImmediate(asset);assets.Clear();}
        }
        private sealed class PauseTestMapHandle : IMapContentHandle
        {
            public PauseTestMapHandle(MapContext context) => Context = context;
            public string MapId => MapIds.Base;
            public MapContext Context { get; }
            public bool IsValid => Context != null;
        }

        private sealed class Ports : IPlayerRuntimePortProvider, IPlayerCameraPort, ICharacterMotorPort
        {
            public Vector3? Focus;
            public bool AllowFocus = true;
            public bool DialogueHidden;
            public void SetDialoguePresentationHidden(bool hidden) => DialogueHidden = hidden;
            public int UnitId => 1; public long MapInstanceId => 9;
            public bool IsValid => true;public bool IsEnabled => true;public bool IsGrounded => true;
            public Vector3 Position => Vector3.zero;public Quaternion Rotation => Quaternion.identity;public float MovementYawDegrees => 0;
            public bool TryGetMotorPort(int id, long map, out ICharacterMotorPort port) { port = this; return id == 1 && map == 9; }
            public bool TryGetCameraPort(int id, long map, out IPlayerCameraPort port) { port = this; return id == 1 && map == 9; }
            public bool IsPlayerCollider(int id, long map, Collider collider) => false;
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = new PlayerMapLocalPoseSnapshot(1, 9, Vector3.zero, 0f, true);
                error = null;
                return true;
            }
            public bool TrySetDialogueFocus(Vector3? position) { if (position.HasValue && !AllowFocus) return false;Focus=position;return true; }
            public void ApplyProfile() { }public void LateUpdate() { }
            public bool SupportsMode(PlayerCameraMode mode, out string error) { error=null;return true; }
            public void ApplyMode(PlayerCameraMode mode, float yaw, float pitch) { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up) { position=Vector3.back;up=Vector3.up;return true; }
            public void Move(Vector3 motion) { }public void RotateTowards(Quaternion rotation,float delta) { }public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float value,float max,bool sprint,bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction,out Vector3 origin) { origin=Vector3.zero;return true; }
            public void FaceWorldHud(Vector3 position,Vector3 up) { }public void PlayAttackFeedback() { }public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }public void TickCombatFeedback(float dt) { }
        }
    }
}
