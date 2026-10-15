using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class UnifiedInteractionTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input = new FakeInput());
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            ResetArchitecture();
        }

        private FakeInput input;

        [Test]
        public void SingleInteractInputPublishesOneInteractEvent()
        {
            var architecture = CreateArchitecture();
            var count = 0;
            var unregister = architecture.RegisterEvent<PlayerInteractRequestedEvent>(_ => count++);
            try
            {
                var system = architecture.GetSystem<PlayerInputSystem>();
                input = new FakeInput();
                typeof(PlayerInputSystem)
                    .GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(system, input);
                InvokeUpdate(system);
                input.InteractPressedThisFrame = true;
                InvokeUpdate(system);
                Assert.That(count, Is.EqualTo(1));
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void NoFInputSurfacePublishesNoInteractEvent()
        {
            var architecture = CreateArchitecture();
            var count = 0;
            var unregister = architecture.RegisterEvent<PlayerInteractRequestedEvent>(_ => count++);
            try
            {
                var system = architecture.GetSystem<PlayerInputSystem>();
                ReplaceInput(system);

                Assert.That(typeof(PlayerInputSystem.IPlayerInputReader).GetProperty("FPressedThisFrame"), Is.Null);
                InvokeUpdate(system);

                Assert.That(count, Is.Zero);
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void SearchInventoryOpenInteractClosesOnlyAndDoesNotPierce()
        {
            var architecture = CreateArchitecture();
            var target = new FakeInteractable("尸体");
            var count = 0;
            var unregister = architecture.RegisterEvent<PlayerInteractRequestedEvent>(_ => count++);
            try
            {
                var inventory = InsertShownSearchContainer();
                var system = architecture.GetSystem<PlayerInputSystem>();
                architecture.GetSystem<PlayerInteractionSystem>().Focus(target);
                ReplaceInput(system);

                input.InteractPressedThisFrame = true;
                InvokeUpdate(system);

                Assert.That(inventory.IsShow(), Is.False);
                Assert.That(count, Is.Zero);
                Assert.That(target.InteractionCount, Is.Zero);
                Assert.That(target.SearchCount, Is.Zero);
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void BlockingUiOpenInteractDoesNotPublish()
        {
            var architecture = CreateArchitecture();
            var count = 0;
            var unregister = architecture.RegisterEvent<PlayerInteractRequestedEvent>(_ => count++);
            try
            {
                InsertShownBlockingUi();
                var system = architecture.GetSystem<PlayerInputSystem>();
                ReplaceInput(system);

                input.InteractPressedThisFrame = true;
                InvokeUpdate(system);

                Assert.That(UIHelper.HasUIOpen, Is.True);
                Assert.That(count, Is.Zero);
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void SearchingInProgressInteractDoesNotPublish()
        {
            var architecture = CreateArchitecture();
            var count = 0;
            var unregister = architecture.RegisterEvent<PlayerInteractRequestedEvent>(_ => count++);
            try
            {
                var search = architecture.GetSystem<SearchContainerSystem>();
                typeof(SearchContainerSystem)
                    .GetField("pendingContainerId", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(search, 7001);
                var system = architecture.GetSystem<PlayerInputSystem>();
                ReplaceInput(system);

                input.InteractPressedThisFrame = true;
                InvokeUpdate(system);

                Assert.That(search.IsSearching, Is.True);
                Assert.That(count, Is.Zero);
            }
            finally
            {
                unregister.UnRegister();
            }
        }

        [Test]
        public void NoUiInteractRequestSearchesFocusedCorpseThroughInteractionSystem()
        {
            var architecture = CreateArchitecture();
            var corpse = new FakeInteractable("尸体") { CanSearchValue = true };
            var system = architecture.GetSystem<PlayerInputSystem>();
            architecture.GetSystem<PlayerInteractionSystem>().Focus(corpse);
            ReplaceInput(system);

            input.InteractPressedThisFrame = true;
            InvokeUpdate(system);

            Assert.That(corpse.SearchCount, Is.EqualTo(1));
            Assert.That(corpse.InteractionCount, Is.Zero);
        }

        [Test]
        public void NoUiInteractRequestInteractsFocusedNpcThroughInteractionSystem()
        {
            var architecture = CreateArchitecture();
            var npc = new FakeInteractable("仓库管理员") { CanSearchValue = false };
            var system = architecture.GetSystem<PlayerInputSystem>();
            architecture.GetSystem<PlayerInteractionSystem>().Focus(npc);
            ReplaceInput(system);

            input.InteractPressedThisFrame = true;
            InvokeUpdate(system);

            Assert.That(npc.InteractionCount, Is.EqualTo(1));
            Assert.That(npc.SearchCount, Is.Zero);
        }

        private static Game CreateArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new SearchContainerModel());
            architecture.RegisterModel(new TemporaryBuildingModel());
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new PlayerModel());
            architecture.RegisterModel(new CombatModel());
            architecture.RegisterUtility<IPlayerRuntimePortProvider>(new NullPlayerPortProvider());
            architecture.RegisterSystem(new PlayerActionSystem());
            architecture.RegisterSystem(new SearchContainerSystem());
            architecture.RegisterSystem(new TemporaryBuildingSystem());
            var interactionSystem = new PlayerInteractionSystem();
            architecture.RegisterSystem(interactionSystem);
            ((ICanInit)interactionSystem).Init();
            interactionSystem.Initialized = true;
            architecture.RegisterSystem(new PlayerInputSystem());
            architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            return architecture;
        }

        private void ReplaceInput(PlayerInputSystem system)
        {
            input = new FakeInput();
            typeof(PlayerInputSystem)
                .GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(system, input);
        }

        private static void InvokeUpdate(PlayerInputSystem system)
        {
            typeof(PlayerInputSystem)
                .GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(system, null);
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static SearchContainerUICtrl InsertShownSearchContainer()
        {
            var instance = new GameObject("SearchContainerTestUI", typeof(RectTransform), typeof(SearchContainerUI));
            var ui = instance.GetComponent<SearchContainerUI>();
            var ctrl = new SearchContainerUICtrl();
            SetBaseUiState(ctrl, ui, true);
            typeof(SearchContainerUICtrl)
                .GetField("currentContainerId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, 7001);
            RegisterUiCtrl(ctrl);
            return ctrl;
        }

        private static void InsertShownBlockingUi()
        {
            var instance = new GameObject("BlockingTestUI", typeof(RectTransform), typeof(BaseUI));
            var ctrl = new BlockingTestCtrl();
            SetBaseUiState(ctrl, instance.GetComponent<BaseUI>(), true);
            RegisterUiCtrl(ctrl);
        }

        private static void SetBaseUiState(BaseUICtrl ctrl, BaseUI ui, bool active)
        {
            typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, ui);
            typeof(BaseUICtrl)
                .GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(ctrl, active);
            ui.gameObject.SetActive(active);
        }

        private static void RegisterUiCtrl(BaseUICtrl ctrl)
        {
            var dict = typeof(UIHelper)
                .GetField("UICtrlDict", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Dictionary<string, BaseUICtrl>;
            Assert.That(dict, Is.Not.Null);
            dict[ctrl.GetType().Name] = ctrl;
        }

        private sealed class FakeInput : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput { get; set; }
            public bool IsEnabled { get; private set; }
            public bool AttackPressedThisFrame { get; set; }
            public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame { get; set; }
            public bool RangedSlotPressedThisFrame { get; set; }
            public bool InteractPressedThisFrame { get; set; }
            public bool SprintHeld { get; set; }
            public bool BuildPressedThisFrame { get; set; }
            public bool DestroyPressedThisFrame { get; set; }
            public bool CancelPressedThisFrame { get; set; }
            public bool ReloadPressedThisFrame { get; set; }
            public void Enable() => IsEnabled = true;
            public void Disable() => IsEnabled = false;
            public void Dispose() { }
        }

        private sealed class NullPlayerPortProvider : IPlayerRuntimePortProvider
        {
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                port = null;
                return false;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                port = null;
                return false;
            }

            public bool IsPlayerCollider(int playerUnitId, long mapInstanceId, Collider collider)
            {
                return false;
            }
        }

        private sealed class FakeInteractable : IPlayerInteractable, IPlayerSearchable
        {
            public FakeInteractable(string displayName)
            {
                DisplayName = displayName;
            }

            public string DisplayName { get; }
            public bool CanInteract => true;
            public bool CanSearchValue { get; set; }
            public bool CanSearch => CanSearchValue;
            public int InteractionCount { get; private set; }
            public int SearchCount { get; private set; }
            public void SetFocused(bool focused) { }
            public void Interact() => InteractionCount++;
            public void Search() => SearchCount++;
        }

        private sealed class BlockingTestCtrl : BaseUICtrl
        {
            protected override string Path() => "Blocking";
            protected override void OnInit() { }
            protected override void OnShow() { }
            protected override void OnHide() { }
            public override UILayer Layer() => UILayer.Dialog;
        }
    }
}
