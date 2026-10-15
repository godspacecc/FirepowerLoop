using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class PlayerInteractionTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
        }

        [Test]
        public void NoFocusDoesNotExecute()
        {
            var fake = new FakeInteractable("生存远征");
            var system = CreateInteractionArchitectureForTests().GetSystem<PlayerInteractionSystem>();

            system.RequestInteract();

            Assert.That(fake.InteractionCount, Is.Zero);
            Assert.That(fake.SearchCount, Is.EqualTo(0));
        }

        [Test]
        public void PureInteractExecutesInteract()
        {
            var fake = new FakeInteractable("仓库终端") { CanSearchValue = false };
            var system = CreateInteractionArchitectureForTests().GetSystem<PlayerInteractionSystem>();

            system.Focus(fake);
            system.RequestInteract();

            Assert.That(fake.InteractionCount, Is.EqualTo(1));
            Assert.That(fake.SearchCount, Is.Zero);
        }

        [Test]
        public void SearchableAndInteractablePrefersSearch()
        {
            var fake = new FakeInteractable("尸体");
            var system = CreateInteractionArchitectureForTests().GetSystem<PlayerInteractionSystem>();

            system.Focus(fake);
            system.RequestInteract();

            Assert.That(fake.InteractionCount, Is.Zero);
            Assert.That(fake.SearchCount, Is.EqualTo(1));
        }

        [Test]
        public void CanSearchFalseFallsBackToInteract()
        {
            var fake = new FakeInteractable("已搜空尸体") { CanSearchValue = false };
            var system = CreateInteractionArchitectureForTests().GetSystem<PlayerInteractionSystem>();

            system.Focus(fake);
            system.RequestInteract();

            Assert.That(fake.InteractionCount, Is.EqualTo(1));
            Assert.That(fake.SearchCount, Is.Zero);
        }

        [Test]
        public void NeitherSearchableNorInteractableDoesNotExecute()
        {
            var fake = new FakeInteractable("失效目标")
            {
                CanInteractValue = false,
                CanSearchValue = false
            };
            var system = CreateInteractionArchitectureForTests().GetSystem<PlayerInteractionSystem>();

            system.Focus(fake);
            system.RequestInteract();

            Assert.That(fake.InteractionCount, Is.Zero);
            Assert.That(fake.SearchCount, Is.Zero);
        }

        [Test]
        public void FocusedNpcAvailabilityDoesNotExposeOtherInteractableKinds()
        {
            var root = new GameObject("Npc");
            root.SetActive(false);
            try
            {
                root.AddComponent<BoxCollider>();
                var view = root.AddComponent<NpcView>();
                var system = CreateInteractionArchitectureForTests().GetSystem<PlayerInteractionSystem>();
                root.SetActive(true);

                system.Focus(view);
                Assert.That(system.CanInteractWithFocusedNpc, Is.True);

                system.Focus(new FakeInteractable("仓库终端"));
                Assert.That(system.CanInteractWithFocusedNpc, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NpcWorldPromptShowsNameInMobileModeWhileFocusRemains()
        {
            var root = new GameObject("Npc");
            var prompt = new GameObject("Prompt");
            NpcView view = null;
            var lifecycleStarted = false;
            root.SetActive(false);
            try
            {
                prompt.transform.SetParent(root.transform);
                root.AddComponent<BoxCollider>();
                view = root.AddComponent<NpcView>();
                view.ConfigureForTests(prompt, "仓库管理员");
                var text = prompt.AddComponent<TMPro.TextMeshPro>();
                typeof(NpcView).GetField("promptText", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, text);
                view.SetPromptText("按 [E] 与仓库管理员交互", "仓库管理员");
                var architecture = CreateInteractionArchitectureForTests();
                var interaction = architecture.GetSystem<PlayerInteractionSystem>();
                var mobileInput = architecture.GetSystem<MobileInputSystem>();
                root.SetActive(true);
                InvokeNpcLifecycle(view, "OnEnable");
                lifecycleStarted = true;

                interaction.Focus(view);
                Assert.That(prompt.activeSelf, Is.True);

                Assert.That(mobileInput.TrySetEditorSimulation(true), Is.True);
                Assert.That(prompt.activeSelf, Is.True);
                Assert.That(text.text, Is.EqualTo("仓库管理员"));
                Assert.That(interaction.CanInteractWithFocusedNpc, Is.True);

                view.SetPromptText("Press [E] to interact with Merchant", "Merchant");
                Assert.That(text.text, Is.EqualTo("Merchant"), "切换语言后手机名字应刷新");

                Assert.That(mobileInput.TrySetEditorSimulation(false), Is.True);
                Assert.That(prompt.activeSelf, Is.True);
                Assert.That(text.text, Is.EqualTo("Press [E] to interact with Merchant"));

                interaction.Blur(view);
                Assert.That(prompt.activeSelf, Is.False);
            }
            finally
            {
                if (lifecycleStarted && view != null)
                {
                    InvokeNpcLifecycle(view, "OnDisable");
                }

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void InteractableNameIsHiddenUntilPlayerEntersRange()
        {
            var root = new GameObject("Terminal");
            var label = new GameObject("NameLabel");
            try
            {
                label.transform.SetParent(root.transform);
                label.SetActive(false);

                var view = root.AddComponent<TestInteractableView>();
                view.ConfigureForTests(label, "任务远征");
                view.SetFocusedForTests(true);
                Assert.That(label.activeSelf, Is.True);
                view.SetFocusedForTests(false);
                Assert.That(label.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(90f, 0f)]
        [TestCase(25f, 120f)]
        [TestCase(-45f, 270f)]
        public void FocusedObjectNameStaysParallelToScreen(float pitch, float yaw)
        {
            var root = new GameObject("Terminal");
            root.SetActive(false);
            try
            {
                var label = new GameObject("NameLabel");
                label.transform.SetParent(root.transform);
                root.transform.position = new Vector3(-5f, 1f, 8f);
                var view = root.AddComponent<TestInteractableView>();
                view.ConfigureForTests(label, "任务远征");
                view.SetFocused(true);
                var cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
                view.FaceName(cameraRotation * Vector3.forward, cameraRotation * Vector3.up);
                Assert.That(Quaternion.Angle(label.transform.rotation, cameraRotation), Is.LessThan(0.01f));

                view.SetFocused(false);
                view.FaceName(Vector3.forward, Vector3.up);
                Assert.That(label.activeSelf, Is.False);
                Assert.That(Quaternion.Angle(label.transform.rotation, cameraRotation), Is.LessThan(0.01f),
                    "隐藏名称不应继续旋转");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DisabledInteractableClearsFocusThroughRealLifecycle()
        {
            var root = new GameObject("Terminal");
            root.SetActive(false);
            try
            {
                var architecture = CreateInteractionArchitectureForTests();
                var view = root.AddComponent<TestInteractableView>();
                root.SetActive(true);

                var system = architecture.GetSystem<PlayerInteractionSystem>();
                system.Focus(view);

                root.SetActive(false);
                system.RequestInteract();

                Assert.That(view.InteractionCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DisablingInteractableAfterArchitectureDeinitDoesNotRecreateArchitecture()
        {
            var root = new GameObject("Terminal");
            root.SetActive(false);
            try
            {
                var architecture = CreateInteractionArchitectureForTests();
                root.AddComponent<TestInteractableView>();
                root.SetActive(true);

                var system = architecture.GetSystem<PlayerInteractionSystem>();
                Assert.That(system, Is.Not.Null);

                architecture.Deinit();
                Assert.That(CurrentArchitecture, Is.Null);

                root.SetActive(false);

                Assert.That(CurrentArchitecture, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Game CurrentArchitecture =>
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Game;

        private static Game CreateInteractionArchitectureForTests()
        {
            var architecture = new Game();
            architecture.RegisterSystem(new PlayerInteractionSystem());
            architecture.RegisterSystem(new MobileInputSystem());
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            return architecture;
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void InvokeNpcLifecycle(NpcView view, string methodName)
        {
            typeof(NpcView)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(view, null);
        }

        private sealed class FakeInteractable : IPlayerInteractable, IPlayerSearchable
        {
            public FakeInteractable(string displayName)
            {
                DisplayName = displayName;
            }

            public string DisplayName { get; }
            public bool CanInteract => CanInteractValue;
            public bool CanInteractValue { get; set; } = true;
            public int InteractionCount { get; private set; }
            public int SearchCount { get; private set; }
            public bool CanSearchValue { get; set; } = true;
            public bool CanSearch => CanSearchValue;

            public void SetFocused(bool focused)
            {
            }

            public void Interact()
            {
                InteractionCount++;
            }

            public void Search()
            {
                SearchCount++;
            }
        }

        private sealed class TestInteractableView : PlayerInteractableView
        {
            public int InteractionCount { get; private set; }

            public override void Interact()
            {
                InteractionCount++;
            }
        }
    }
}
