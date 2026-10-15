using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class DynamicResourceAddressTests
    {
        [SetUp]
        public void SetUp()
        {
            UIHelper.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
        }

        [TestCase(ResourceAddressKind.CharacterPrefab, "GrayboxPlayer", "Prefabs/Characters/GrayboxPlayer")]
        [TestCase(ResourceAddressKind.WeaponAppearancePrefab, "GrayboxBow", "Prefabs/Characters/Weapons/GrayboxBow")]
        [TestCase(ResourceAddressKind.SceneObjectPrefab, "TaskExit", "Prefabs/SceneObjects/TaskExit")]
        [TestCase(ResourceAddressKind.ProjectilePrefab, "GrayboxArrow", "Prefabs/Combat/GrayboxArrow")]
        [TestCase(ResourceAddressKind.TemporaryBuildingPrefab, "GrayboxCampfire", "Prefabs/Building/GrayboxCampfire")]
        [TestCase(ResourceAddressKind.ItemIcon, "IconMedkit", "Art/UI/Items/IconMedkit")]
        [TestCase(ResourceAddressKind.TemporaryBuildingIcon, "IconTower", "Art/UI/TemporaryBuilding/IconTower")]
        [TestCase(ResourceAddressKind.Music, "HomeTheme", "Audio/Music/HomeTheme")]
        [TestCase(ResourceAddressKind.Sound, "FoundationClick", "Audio/Sound/FoundationClick")]
        public void FileNameKindsProduceTheirFixedAddress(ResourceAddressKind kind, string value, string expected)
        {
            Assert.That(ResourceAddress.TryFromFileName(kind, value, false, out var address, out var error), Is.True, error);
            Assert.That(address, Is.EqualTo(expected));
        }

        [Test]
        public void OptionalFileNameAcceptsOnlyAnEmptyValue()
        {
            Assert.That(ResourceAddress.TryFromFileName(ResourceAddressKind.ItemIcon, "   ", true, out var address, out var error), Is.True, error);
            Assert.That(address, Is.Empty);
            Assert.That(ResourceAddress.TryFromFileName(ResourceAddressKind.ItemIcon, "Art/UI/Items/Icon", true, out _, out error), Is.False);
            Assert.That(error, Does.Contain("路径分隔符"));
        }

        [TestCase("Folder/Prefab")]
        [TestCase("Folder\\Prefab")]
        [TestCase("Prefab.prefab")]
        [TestCase("Icon.png")]
        [TestCase("Track.ogg")]
        [TestCase("Scene.unity")]
        public void FileNameRejectsDirectoriesAndResourceExtensions(string value)
        {
            Assert.That(ResourceAddress.TryFromFileName(ResourceAddressKind.CharacterPrefab, value, false, out var address, out var error), Is.False);
            Assert.That(address, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void UiAndMapRelativePathsProduceTheirFixedAddress()
        {
            Assert.That(ResourceAddress.TryFromRelativePath(ResourceAddressKind.UiPrefab, "Inventory/Bag", out var uiAddress, out var uiError), Is.True, uiError);
            Assert.That(uiAddress, Is.EqualTo("Prefabs/UI/Inventory/Bag"));
            Assert.That(ResourceAddress.TryFromRelativePath(ResourceAddressKind.MapScene, "Mission/TaskSampleMap", out var mapAddress, out var mapError), Is.True, mapError);
            Assert.That(mapAddress, Is.EqualTo("Scenes/Mission/TaskSampleMap"));
        }

        [TestCase("Prefabs/UI/Home/Start")]
        [TestCase("/Home/Start")]
        [TestCase("Home/Start/")]
        [TestCase("Home//Start")]
        [TestCase("Home/../Start")]
        [TestCase("Home\\Start")]
        [TestCase("Home/Start.prefab")]
        public void RelativePathsRejectRootsTraversalSeparatorsAndExtensions(string value)
        {
            Assert.That(ResourceAddress.TryFromRelativePath(ResourceAddressKind.UiPrefab, value, out var address, out var error), Is.False);
            Assert.That(address, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void AddressKindsRejectTheWrongInputMethod()
        {
            Assert.That(ResourceAddress.TryFromFileName(ResourceAddressKind.UiPrefab, "Home/Start", false, out _, out _), Is.False);
            Assert.That(ResourceAddress.TryFromRelativePath(ResourceAddressKind.Sound, "FoundationClick", out _, out _), Is.False);
        }

        [Test]
        public void GenericLoaderAndGridDoNotAcceptResourceKindsOrStringPaths()
        {
            Assert.That(typeof(IResourceLoader).GetMethods().Any(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ResourceAddressKind))), Is.False);
            Assert.That(typeof(CustomGridLayoutGroup).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == nameof(CustomGridLayoutGroup.LoadAndFillGrid))
                .SelectMany(method => method.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(string)), Is.False);
        }

        [Test]
        public void BaseUiShowLoadsTheFullAddressForALegalRelativePath()
        {
            CreateUiRoot();
            var requestedAddresses = new List<string>();
            var prefab = CreateProbePrefab();
            UIHelper.SetPrefabLoaderForTests(address =>
            {
                requestedAddresses.Add(address);
                return prefab;
            });

            try
            {
                var ctrl = new AddressProbeUiCtrl("Inventory/Bag");
                ctrl.Show();

                Assert.That(requestedAddresses, Is.EqualTo(new[] { "Prefabs/UI/Inventory/Bag" }));
                Assert.That(GetBaseUi(ctrl), Is.Not.Null);
                Assert.That(ctrl.IsShow(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void BaseUiShowRejectsInvalidPathWithoutLoadingOrChangingExistingUi()
        {
            CreateUiRoot();
            var requestedAddresses = new List<string>();
            var prefab = CreateProbePrefab();
            UIHelper.SetPrefabLoaderForTests(address =>
            {
                requestedAddresses.Add(address);
                return prefab;
            });

            try
            {
                var existingCtrl = new AddressProbeUiCtrl("Home/Start");
                existingCtrl.Show();
                var existingUi = GetBaseUi(existingCtrl);
                Assert.That(existingUi, Is.Not.Null);

                var invalidCtrl = new AddressProbeUiCtrl("../Escape");
                LogAssert.Expect(LogType.Error, new Regex("\\[BaseUICtrl\\] UI 相对路径无效：../Escape；相对路径不能包含 \\. 或 \\.\\."));
                invalidCtrl.Show();

                Assert.That(requestedAddresses, Is.EqualTo(new[] { "Prefabs/UI/Home/Start" }));
                Assert.That(GetBaseUi(invalidCtrl), Is.Null);
                Assert.That(GetBaseUi(existingCtrl), Is.SameAs(existingUi));
                Assert.That(existingCtrl.IsShow(), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void AllUiControllersExposeRelativePaths()
        {
            var expectedPaths = new Dictionary<BaseUICtrl, string>
            {
                [new LoadingUICtrl()] = "Common/Loading",
                [new RuntimeLogUICtrl()] = "Diagnostics/RuntimeLog",
                [new TestCmdButtonUICtrl()] = "Diagnostics/TestCmdButton",
                [new TestCmdUICtrl()] = "Diagnostics/TestCmd",
                [new SettingUICtrl()] = "Home/Setting",
                [new StartUICtrl()] = "Home/Start",
                [new FloatUICtrl()] = "Notify/Float",
                [new MessageUICtrl()] = "Notify/Message",
                [new OnlineLoginUICtrl()] = "Online/OnlineLogin",
                [new OnlineRoomUICtrl()] = "Online/OnlineRoom",
            };

            Assert.That(expectedPaths, Has.Count.EqualTo(19));
            var pathMethod = typeof(BaseUICtrl).GetMethod("Path", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var pair in expectedPaths)
            {
                var path = (string)pathMethod.Invoke(pair.Key, null);
                Assert.That(path, Is.EqualTo(pair.Value), pair.Key.GetType().Name);
                Assert.That(ResourceAddress.TryFromRelativePath(ResourceAddressKind.UiPrefab, path, out var address, out var error), Is.True, error);
                Assert.That(address, Is.EqualTo("Prefabs/UI/" + pair.Value));
            }
        }

        private static void CreateUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null, "Unity Input System UI module type must be available.");
            eventSystem.AddComponent(inputModuleType);
            root.AddComponent<UIRoot>();
            UIRoot.EnsureInstance();
        }

        private static GameObject CreateProbePrefab()
        {
            var prefab = new GameObject("AddressProbePrefab", typeof(RectTransform), typeof(BaseUI));
            prefab.SetActive(false);
            return prefab;
        }

        private static BaseUI GetBaseUi(BaseUICtrl ctrl)
        {
            return (BaseUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ctrl);
        }

        private sealed class AddressProbeUiCtrl : BaseUICtrl
        {
            private readonly string path;

            public AddressProbeUiCtrl(string path)
            {
                this.path = path;
            }

            protected override string Path() => path;
            protected override void OnInit() { }
            protected override void OnShow() { }
            protected override void OnHide() { }
            public override UILayer Layer() => UILayer.Dialog;
        }
    }
}
