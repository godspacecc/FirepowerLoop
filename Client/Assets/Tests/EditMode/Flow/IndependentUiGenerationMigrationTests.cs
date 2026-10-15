using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class IndependentUiGenerationMigrationTests
    {
        [Test]
        public void SettingNetworkPagePrefab_HasGeneratedDiagnosticBindings()
        {
            AssertPrefab<SettingNetworkPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingNetwork.prefab", SettingNetworkFields);
        }

        [Test]
        public void GameplayGuideHudPrefab_HasGeneratedPauseButtonAndStableBindings()
        {
            AssertPrefab<GameplayGuideHudUI>("Assets/Game/Prefabs/UI/Player/GameplayGuideHud.prefab", GameplayGuideHudFields);
        }

        [Test]
        public void IndependentUiPrefabs_HaveRootCollectorGeneratedViewAndStableBindings()
        {
            AssertPrefab<ExpeditionHudUI>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab", ExpeditionHudFields);
            AssertPrefab<BagUI>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab", BagFields);
            AssertPrefab<WarehouseUI>("Assets/Game/Prefabs/UI/Inventory/Warehouse.prefab", WarehouseFields);
            AssertPrefab<SearchContainerUI>("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab", SearchContainerFields);
            AssertPrefab<LoadingUI>("Assets/Game/Prefabs/UI/Common/Loading.prefab", LoadingFields);
            AssertPrefab<PlayerConditionHudUI>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab", PlayerConditionHudFields);
            AssertPrefab<GameplayGuideHudUI>("Assets/Game/Prefabs/UI/Player/GameplayGuideHud.prefab", GameplayGuideHudFields);
            AssertPrefab<SettingUI>("Assets/Game/Prefabs/UI/Home/Setting.prefab", SettingFields);
            AssertPrefab<SettingGeneralPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingGeneral.prefab", SettingGeneralFields);
            AssertPrefab<SettingAudioPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingAudio.prefab", SettingAudioFields);
            AssertPrefab<SettingControlsPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingControls.prefab", SettingControlsFields);
            AssertPrefab<SettingNetworkPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingNetwork.prefab", SettingNetworkFields);
        }

        [Test]
        public void SearchContainerPrefab_ViewportUsesRectMask2D()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab");
            var ui = prefab.GetComponent<SearchContainerUI>();

            Assert.That(prefab, Is.Not.Null);
            Assert.That(ui.SearchList.viewport, Is.Not.Null);
            Assert.That(ui.SearchList.viewport, Is.SameAs(ui.SearchViewport));
            Assert.That(ui.SearchScrollRect.viewport, Is.SameAs(ui.SearchViewport));
            Assert.That(ui.SearchViewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(ui.SearchViewport.GetComponent<Mask>(), Is.Null);
        }

        [Test]
        public void IndependentUiControllers_KeepPrefabPaths()
        {
            Assert.That(InvokePath(new ExpeditionHudUICtrl()), Is.EqualTo("Expedition/ExpeditionHud"));
            Assert.That(InvokePath(new BagUICtrl()), Is.EqualTo("Inventory/Bag"));
            Assert.That(InvokePath(new WarehouseUICtrl()), Is.EqualTo("Inventory/Warehouse"));
            Assert.That(InvokePath(new SearchContainerUICtrl()), Is.EqualTo("Inventory/SearchContainer"));
            Assert.That(InvokePath(new LoadingUICtrl()), Is.EqualTo("Common/Loading"));
            Assert.That(InvokePath(new PlayerConditionHudUICtrl()), Is.EqualTo("Player/PlayerConditionHud"));
            Assert.That(InvokePath(new GameplayGuideHudUICtrl()), Is.EqualTo("Player/GameplayGuideHud"));
            Assert.That(InvokePath(new SettingUICtrl()), Is.EqualTo("Home/Setting"));
        }

        private static void AssertPrefab<T>(string path, FieldExpectation[] expectedFields) where T : BaseUI
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var collectors = prefab.GetComponents<ReferenceCollector>();
            Assert.That(collectors.Length, Is.EqualTo(1), path);
            var collector = collectors[0];
            var ui = prefab.GetComponent<T>();
            Assert.That(ui, Is.Not.Null, path);

            var fields = typeof(T).GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance);
            var fieldsByName = fields.ToDictionary(field => field.Name);
            Assert.That(fieldsByName.Keys, Is.EquivalentTo(expectedFields.Select(item => item.Name)), path);
            Assert.That(collector.data.Select(item => item.key), Is.EquivalentTo(expectedFields.Select(item => item.Name)), path);
            Assert.That(collector.data.Select(item => item.key).Distinct().Count(), Is.EqualTo(collector.data.Count), path);
            if (typeof(T) == typeof(PlayerConditionHudUI))
            {
                Assert.That(typeof(T).GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty, path);
                Assert.That(typeof(T).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty, path);
            }

            foreach (var expected in expectedFields)
            {
                var field = fieldsByName[expected.Name];
                var viewValue = field.GetValue(ui) as Object;
                var collected = collector.data.Single(item => item.key == expected.Name);
                var collectedValue = ResolveReferenceObject(collected.gameObject, field.FieldType);

                Assert.That(field.FieldType, Is.EqualTo(expected.DeclaredType), expected.Name);
                Assert.That(collected.TypeName, Is.EqualTo(expected.ActualType.FullName), expected.Name);
                Assert.That(viewValue, Is.Not.Null, expected.Name);
                Assert.That(collected.gameObject, Is.Not.Null, expected.Name);
                Assert.That(collectedValue, Is.Not.Null, expected.Name);
                Assert.That(field.FieldType.IsInstanceOfType(viewValue), Is.True, expected.Name);
                Assert.That(PathOf(prefab.transform, viewValue), Is.EqualTo(expected.Path), expected.Name);
                Assert.That(ObjectName(viewValue), Is.EqualTo(expected.ObjectName), expected.Name);
                Assert.That(viewValue.GetType(), Is.EqualTo(expected.ActualType), expected.Name);
                Assert.That(ReferenceEquals(viewValue, collectedValue), Is.True, expected.Name);
            }
        }

        private static Object ResolveReferenceObject(Object source, Type expectedType)
        {
            if (source == null || expectedType == null)
            {
                return null;
            }

            if (expectedType.IsInstanceOfType(source))
            {
                return source;
            }

            if (source is GameObject gameObject)
            {
                return expectedType == typeof(GameObject)
                    ? gameObject
                    : gameObject.GetComponent(expectedType);
            }

            if (source is Component component)
            {
                return expectedType == typeof(GameObject)
                    ? component.gameObject
                    : component.GetComponent(expectedType);
            }

            return null;
        }

        private static string PathOf(Transform root, Object value)
        {
            var transform = value switch
            {
                GameObject gameObject => gameObject.transform,
                Component component => component.transform,
                _ => null
            };
            Assert.That(transform, Is.Not.Null);
            var parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                if (transform == root)
                {
                    break;
                }

                transform = transform.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string ObjectName(Object value)
        {
            return value is Component component ? component.name : value.name;
        }

        private static string InvokePath(BaseUICtrl ctrl)
        {
            return (string)ctrl.GetType()
                .GetMethod("Path", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(ctrl, null);
        }

        private static FieldExpectation F(string name, Type declaredType, Type actualType, string path, string objectName)
        {
            return new FieldExpectation(name, declaredType, actualType, path, objectName);
        }

        private readonly struct FieldExpectation
        {
            public FieldExpectation(string name, Type declaredType, Type actualType, string path, string objectName)
            {
                Name = name;
                DeclaredType = declaredType;
                ActualType = actualType;
                Path = path;
                ObjectName = objectName;
            }

            public string Name { get; }
            public Type DeclaredType { get; }
            public Type ActualType { get; }
            public string Path { get; }
            public string ObjectName { get; }
        }

        private static readonly FieldExpectation[] ExpeditionHudFields =
        {
            F("TimerText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "ExpeditionHud/HudPanel/TimerText", "TimerText"),
            F("StatusText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "ExpeditionHud/HudPanel/StatusText", "StatusText"),
            F("ResourceText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "ExpeditionHud/HudPanel/ResourceText", "ResourceText"),
            F("BtnReturn", typeof(Button), typeof(Button), "ExpeditionHud/HudPanel/BtnReturn", "BtnReturn"),
            F("ReturnButtonText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "ExpeditionHud/HudPanel/BtnReturn/Text", "Text")
        };

        private static readonly FieldExpectation[] LoadingFields =
        {
            F("BaseArtwork", typeof(GameObject), typeof(GameObject), "Loading/BaseArtwork", "BaseArtwork"),
            F("MapArtwork", typeof(GameObject), typeof(GameObject), "Loading/BackgroundArtwork", "BackgroundArtwork"),
            F("BaseTitle", typeof(GameObject), typeof(GameObject), "Loading/BaseTitle", "BaseTitle"),
            F("MapTitle", typeof(GameObject), typeof(GameObject), "Loading/Title", "Title"),
            F("MessageText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/Message", "Message"),
            F("ProgressSlider", typeof(Slider), typeof(Slider), "Loading/Progress", "Progress"),
            F("PercentText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/Percent", "Percent"),
            F("ErrorRoot", typeof(GameObject), typeof(GameObject), "Loading/ErrorRoot", "ErrorRoot"),
            F("ErrorText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/ErrorRoot/ErrorText", "ErrorText"),
            F("BtnBack", typeof(Button), typeof(Button), "Loading/ErrorRoot/BackButton", "BackButton")
        };

        private static readonly FieldExpectation[] PlayerConditionHudFields =
        {
            F("ContentRoot", typeof(GameObject), typeof(GameObject), "PlayerConditionHud/ContentRoot", "ContentRoot"),
            F("PowerRoot", typeof(GameObject), typeof(GameObject), "PlayerConditionHud/ContentRoot/PowerRoot", "PowerRoot"),
            F("PowerFill", typeof(Image), typeof(Image), "PlayerConditionHud/ContentRoot/PowerRoot/PowerFill", "PowerFill"),
            F("PowerIconText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/PowerRoot/PowerIconText", "PowerIconText"),
            F("PowerValueText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/PowerRoot/PowerValueText", "PowerValueText"),
            F("WaterRoot", typeof(GameObject), typeof(GameObject), "PlayerConditionHud/ContentRoot/WaterRoot", "WaterRoot"),
            F("WaterFill", typeof(Image), typeof(Image), "PlayerConditionHud/ContentRoot/WaterRoot/WaterFill", "WaterFill"),
            F("WaterIconText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/WaterRoot/WaterIconText", "WaterIconText"),
            F("WaterValueText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/WaterRoot/WaterValueText", "WaterValueText"),
            F("TemperatureRoot", typeof(GameObject), typeof(GameObject), "PlayerConditionHud/ContentRoot/TemperatureRoot", "TemperatureRoot"),
            F("TemperatureFill", typeof(Image), typeof(Image), "PlayerConditionHud/ContentRoot/TemperatureRoot/TemperatureFill", "TemperatureFill"),
            F("TemperatureIconText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/TemperatureRoot/TemperatureIconText", "TemperatureIconText"),
            F("TemperatureValueText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/TemperatureRoot/TemperatureValueText", "TemperatureValueText"),
            F("TiliRoot", typeof(GameObject), typeof(GameObject), "PlayerConditionHud/ContentRoot/TiliRoot", "TiliRoot"),
            F("TiliFill", typeof(Image), typeof(Image), "PlayerConditionHud/ContentRoot/TiliRoot/TiliFill", "TiliFill"),
            F("TiliIconText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/TiliRoot/TiliIconText", "TiliIconText"),
            F("TiliValueText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/TiliRoot/TiliValueText", "TiliValueText"),
            F("HealthRoot", typeof(GameObject), typeof(GameObject), "PlayerConditionHud/ContentRoot/HealthRoot", "HealthRoot"),
            F("HealthFill", typeof(Image), typeof(Image), "PlayerConditionHud/ContentRoot/HealthRoot/HealthFill", "HealthFill"),
            F("HealthIconText", typeof(TMP_Text), typeof(TextMeshProUGUI), "PlayerConditionHud/ContentRoot/HealthRoot/HealthIconText", "HealthIconText")
        };

        private static readonly FieldExpectation[] GameplayGuideHudFields =
        {
            F("GuideRoot", typeof(GameObject), typeof(GameObject), "GameplayGuideHud/GuideRoot", "GuideRoot"),
            F("GuideText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "GameplayGuideHud/GuideRoot/GuideText", "GuideText"),
            F("VersionText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "GameplayGuideHud/VersionText", "VersionText"),
            F("BtnPause", typeof(Button), typeof(Button), "GameplayGuideHud/BtnPause", "BtnPause")
        };

        private static readonly FieldExpectation[] SettingFields =
        {
            F("AudioTabButton", typeof(Button), typeof(Button), "Setting/Panel/Sidebar/AudioTabButton", "AudioTabButton"),
            F("BtnExit", typeof(Button), typeof(Button), "Setting/Panel/BtnExit", "BtnExit"),
            F("ControlsTabButton", typeof(Button), typeof(Button), "Setting/Panel/Sidebar/ControlsTabButton", "ControlsTabButton"),
            F("GeneralTabButton", typeof(Button), typeof(Button), "Setting/Panel/Sidebar/GeneralTabButton", "GeneralTabButton"),
            F("NetworkTabButton", typeof(Button), typeof(Button), "Setting/Panel/Sidebar/NetworkTabButton", "NetworkTabButton"),
            F("SettingAudioRoot", typeof(GameObject), typeof(GameObject), "Setting/Panel/Content/SettingAudio", "SettingAudio"),
            F("SettingControlsRoot", typeof(GameObject), typeof(GameObject), "Setting/Panel/Content/SettingControls", "SettingControls"),
            F("SettingGeneralRoot", typeof(GameObject), typeof(GameObject), "Setting/Panel/Content/SettingGeneral", "SettingGeneral"),
            F("SettingNetworkRoot", typeof(GameObject), typeof(GameObject), "Setting/Panel/Content/SettingNetwork", "SettingNetwork")
        };

        private static readonly FieldExpectation[] SettingGeneralFields =
        {
            F("ResetButton", typeof(Button), typeof(Button), "SettingGeneral/ResetButton", "ResetButton"),
            F("SettingList", typeof(CustomListView), typeof(CustomListView), "SettingGeneral/SettingList", "SettingList")
        };

        private static readonly FieldExpectation[] SettingAudioFields =
        {
            F("MasterVolumeSlider", typeof(Slider), typeof(Slider), "SettingAudio/MasterVolume/Slider", "Slider"),
            F("MusicVolumeSlider", typeof(Slider), typeof(Slider), "SettingAudio/MusicVolume/Slider", "Slider"),
            F("MuteToggle", typeof(Toggle), typeof(Toggle), "SettingAudio/Mute/Toggle", "Toggle"),
            F("ResetButton", typeof(Button), typeof(Button), "SettingAudio/ResetButton", "ResetButton"),
            F("SoundVolumeSlider", typeof(Slider), typeof(Slider), "SettingAudio/SoundVolume/Slider", "Slider"),
            F("TestSoundButton", typeof(Button), typeof(Button), "SettingAudio/BtnTestSound", "BtnTestSound")
        };

        private static readonly FieldExpectation[] SettingControlsFields =
        {
            F("HotKeyList", typeof(CustomListView), typeof(CustomListView), "SettingControls/Viewport/HotKeyList", "HotKeyList"),
            F("ResetButton", typeof(Button), typeof(Button), "SettingControls/ResetButton", "ResetButton")
        };

        private static readonly FieldExpectation[] SettingNetworkFields =
        {
            F("DiagnosticNotice", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingNetwork/DiagnosticNotice", "DiagnosticNotice"),
            F("DiagnosticPending", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingNetwork/DiagnosticPending", "DiagnosticPending"),
            F("DiagnosticClearButton", typeof(Button), typeof(Button), "SettingNetwork/DiagnosticClearButton", "DiagnosticClearButton"),
            F("DiagnosticUploadButton", typeof(Button), typeof(Button), "SettingNetwork/DiagnosticUploadButton", "DiagnosticUploadButton"),
            F("DiagnosticStatus", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingNetwork/DiagnosticStatus", "DiagnosticStatus"),
            F("GameServerApplyButton", typeof(Button), typeof(Button), "SettingNetwork/GameServerApplyButton", "GameServerApplyButton"),
            F("GameServerFeedback", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingNetwork/GameServerFeedback", "GameServerFeedback"),
            F("GameServerInput", typeof(TMP_InputField), typeof(TMP_InputField), "SettingNetwork/GameServerInput", "GameServerInput"),
            F("GameServerResetButton", typeof(Button), typeof(Button), "SettingNetwork/GameServerResetButton", "GameServerResetButton"),
            F("ResourceServerApplyButton", typeof(Button), typeof(Button), "SettingNetwork/ResourceServerApplyButton", "ResourceServerApplyButton"),
            F("ResourceServerFeedback", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingNetwork/ResourceServerFeedback", "ResourceServerFeedback"),
            F("ResourceServerInput", typeof(TMP_InputField), typeof(TMP_InputField), "SettingNetwork/ResourceServerInput", "ResourceServerInput"),
            F("ResourceServerResetButton", typeof(Button), typeof(Button), "SettingNetwork/ResourceServerResetButton", "ResourceServerResetButton")
        };

        private static readonly FieldExpectation[] BagFields =
        {
            F("ActionRoot", typeof(GameObject), typeof(GameObject), "Bag/BagOnlyRoot/BagOnlyActionRoot", "BagOnlyActionRoot"),
            F("BackgroundImage", typeof(Image), typeof(Image), "Bag", "Bag"),
            F("BtnDrop", typeof(Button), typeof(Button), "Bag/BagOnlyRoot/BagOnlyActionRoot/BagOnlyBtnDrop", "BagOnlyBtnDrop"),
            F("BtnSplit", typeof(Button), typeof(Button), "Bag/BagOnlyRoot/BagOnlyActionRoot/BagOnlyBtnSplit", "BagOnlyBtnSplit"),
            F("BtnUse", typeof(Button), typeof(Button), "Bag/BagOnlyRoot/BagOnlyActionRoot/BagOnlyBtnUse", "BagOnlyBtnUse"),
            F("CapacityText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Bag/BagOnlyRoot/BagOnlyCapacityText", "BagOnlyCapacityText"),
            F("DetailAmountText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Bag/BagOnlyRoot/BagOnlyDetailRoot/BagOnlyDetailAmountText", "BagOnlyDetailAmountText"),
            F("DetailDescText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Bag/BagOnlyRoot/BagOnlyDetailRoot/BagOnlyDetailDescText", "BagOnlyDetailDescText"),
            F("DetailNameText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Bag/BagOnlyRoot/BagOnlyDetailRoot/BagOnlyDetailNameText", "BagOnlyDetailNameText"),
            F("DetailRoot", typeof(GameObject), typeof(GameObject), "Bag/BagOnlyRoot/BagOnlyDetailRoot", "BagOnlyDetailRoot"),
            F("MeleeWeaponSlotBox", typeof(EquipmentSlotBoxUI), typeof(EquipmentSlotBoxUI), "Bag/BagOnlyRoot/BagOnlyEquipmentRow/BagOnlyMeleeEquipmentGroup/BagOnlyMeleeWeaponSlotBox", "BagOnlyMeleeWeaponSlotBox"),
            F("PlayerBagList", typeof(CustomListView), typeof(CustomListView), "Bag/BagOnlyRoot/BagOnlyPlayerBagSlotRoot", "BagOnlyPlayerBagSlotRoot"),
            F("RangedWeaponSlotBox", typeof(EquipmentSlotBoxUI), typeof(EquipmentSlotBoxUI), "Bag/BagOnlyRoot/BagOnlyEquipmentRow/BagOnlyRangedEquipmentGroup/BagOnlyRangedWeaponSlotBox", "BagOnlyRangedWeaponSlotBox")
        };

        private static readonly FieldExpectation[] WarehouseFields =
        {
            F("BackgroundImage", typeof(Image), typeof(Image), "Warehouse", "Warehouse"),
            F("BtnClose", typeof(Button), typeof(Button), "Warehouse/WarehousePanel/BtnClose", "BtnClose"),
            F("WarehouseList", typeof(CustomListView), typeof(CustomListView), "Warehouse/WarehousePanel/WarehouseScrollRect/Viewport/WarehouseSlotRoot", "WarehouseSlotRoot"),
            F("WarehouseScrollRect", typeof(ScrollRect), typeof(ScrollRect), "Warehouse/WarehousePanel/WarehouseScrollRect", "WarehouseScrollRect"),
        };

        private static readonly FieldExpectation[] SearchContainerFields =
        {
            F("BackgroundImage", typeof(Image), typeof(Image), "SearchContainer", "SearchContainer"),
            F("SearchRootRect", typeof(RectTransform), typeof(RectTransform), "SearchContainer/SearchRoot", "SearchRoot"),
            F("SearchScrollRect", typeof(ScrollRect), typeof(ScrollRect), "SearchContainer/SearchRoot", "SearchRoot"),
            F("SearchList", typeof(LoopGridLayoutGroup), typeof(LoopGridLayoutGroup), "SearchContainer/SearchRoot/Viewport/SlotRoot", "SlotRoot"),
            F("SearchViewport", typeof(RectTransform), typeof(RectTransform), "SearchContainer/SearchRoot/Viewport", "Viewport")
        };
    }
}
