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
        public void IndependentUiPrefabs_HaveRootCollectorGeneratedViewAndStableBindings()
        {
            AssertPrefab<LoadingUI>("Assets/Game/Prefabs/UI/Common/Loading.prefab", LoadingFields);
            SettingsPrefabs_HaveGeneratedBindings();
        }

        [Test]
        public void SettingsPrefabs_HaveGeneratedBindings()
        {
            AssertPrefab<SettingUI>("Assets/Game/Prefabs/UI/Home/Setting.prefab", SettingFields);
            AssertPrefab<SettingGeneralPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingGeneral.prefab", SettingGeneralFields);
            AssertPrefab<SettingAudioPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingAudio.prefab", SettingAudioFields);
            AssertPrefab<SettingControlsPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingControls.prefab", SettingControlsFields);
            AssertPrefab<SettingNetworkPageUI>("Assets/Game/Prefabs/UI/Home/Page/SettingNetwork.prefab", SettingNetworkFields);
        }

        [Test]
        public void IndependentUiControllers_KeepPrefabPaths()
        {
            Assert.That(InvokePath(new LoadingUICtrl()), Is.EqualTo("Common/Loading"));
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

            foreach (var expected in expectedFields)
            {
                var field = fieldsByName[expected.Name];
                var viewValue = field.GetValue(ui) as Object;
                var collected = collector.data.Single(item => item.key == expected.Name);
                var collectedValue = ResolveReferenceObject(collected.gameObject, field.FieldType);

                Assert.That(field.FieldType, Is.EqualTo(expected.DeclaredType), expected.Name);
                Assert.That(collected.TypeName, Is.EqualTo(expected.CollectedType.FullName), expected.Name);
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

        private static FieldExpectation F(string name, Type declaredType, Type actualType, string path, string objectName, Type collectedType = null)
        {
            return new FieldExpectation(name, declaredType, actualType, path, objectName, collectedType ?? actualType);
        }

        private readonly struct FieldExpectation
        {
            public FieldExpectation(string name, Type declaredType, Type actualType, string path, string objectName, Type collectedType)
            {
                Name = name;
                DeclaredType = declaredType;
                ActualType = actualType;
                CollectedType = collectedType;
                Path = path;
                ObjectName = objectName;
            }

            public string Name { get; }
            public Type DeclaredType { get; }
            public Type ActualType { get; }
            /// <summary>收集器可保存基类类型；与实际组件类型分别校验，避免把合法多态引用误判为错绑。</summary>
            public Type CollectedType { get; }
            public string Path { get; }
            public string ObjectName { get; }
        }

        private static readonly FieldExpectation[] LoadingFields =
        {
            F("BtnBack", typeof(Button), typeof(Button), "Loading/MapRoot/ErrorRoot/BackButton", "BackButton"),
            F("ErrorRoot", typeof(GameObject), typeof(GameObject), "Loading/MapRoot/ErrorRoot", "ErrorRoot"),
            F("ErrorText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/MapRoot/ErrorRoot/ErrorText", "ErrorText"),
            F("MessageText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/MapRoot/Message", "Message"),
            F("PercentText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/MapRoot/Percent", "Percent"),
            F("ProgressSlider", typeof(Slider), typeof(Slider), "Loading/MapRoot/Progress", "Progress"),
            F("BaseArtwork", typeof(GameObject), typeof(GameObject), "Loading/MapRoot/BaseArtwork", "BaseArtwork"),
            F("MapArtwork", typeof(GameObject), typeof(GameObject), "Loading/MapRoot/BackgroundArtwork", "BackgroundArtwork"),
            F("BaseTitle", typeof(GameObject), typeof(GameObject), "Loading/MapRoot/BaseTitle", "BaseTitle"),
            F("MapTitle", typeof(GameObject), typeof(GameObject), "Loading/MapRoot/Title", "Title"),
            F("MapRoot", typeof(GameObject), typeof(GameObject), "Loading/MapRoot", "MapRoot"),
            F("DownloadRoot", typeof(GameObject), typeof(GameObject), "Loading/DownloadRoot", "DownloadRoot"),
            F("DownloadPercentText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/DownloadRoot/Panel/DownloadPercent", "DownloadPercent"),
            F("DownloadMessageText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/DownloadRoot/Panel/DownloadMessage", "DownloadMessage"),
            F("DownloadSlider", typeof(Slider), typeof(Slider), "Loading/DownloadRoot/Panel/DownloadProgress", "DownloadProgress"),
            F("BtnDownloadClose", typeof(Button), typeof(Button), "Loading/DownloadRoot/Panel/DownloadClose", "DownloadClose"),
            F("BtnCopyLog", typeof(Button), typeof(Button), "Loading/MapRoot/ErrorRoot/CopyLogButton", "CopyLogButton"),
            F("CopyStatusText", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "Loading/MapRoot/ErrorRoot/CopyStatusText", "CopyStatusText"),
            F("BtnRetry", typeof(Button), typeof(Button), "Loading/MapRoot/ErrorRoot/RetryButton", "RetryButton"),
            F("LoadingArtwork", typeof(ArcadeLoadingArtwork), typeof(ArcadeLoadingArtwork), "Loading/MapRoot/BackgroundArtwork", "BackgroundArtwork"),
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
            F("SettingNetworkRoot", typeof(GameObject), typeof(GameObject), "Setting/Panel/Content/SettingNetwork", "SettingNetwork"),
            F("SettingMobileRoot", typeof(GameObject), typeof(GameObject), "Setting/Panel/Content/SettingMobile", "SettingMobile")
        };

        private static readonly FieldExpectation[] SettingGeneralFields =
        {
            F("ResetButton", typeof(Button), typeof(Button), "SettingGeneral/ResetButton", "ResetButton"),
            F("SettingList", typeof(CustomListView), typeof(CustomListView), "SettingGeneral/SettingList", "SettingList")
        };

        private static readonly FieldExpectation[] SettingAudioFields =
        {
            F("MasterVolumeValue", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingAudio/MasterVolume/VolumeValue", "VolumeValue"),
            F("MusicVolumeValue", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingAudio/MusicVolume/VolumeValue", "VolumeValue"),
            F("SoundVolumeValue", typeof(TextMeshProUGUI), typeof(TextMeshProUGUI), "SettingAudio/SoundVolume/VolumeValue", "VolumeValue"),
            F("MuteThumb", typeof(RectTransform), typeof(RectTransform), "SettingAudio/Mute/Toggle/SwitchTrack/Thumb", "Thumb"),
            F("MuteTrack", typeof(Image), typeof(Image), "SettingAudio/Mute/Toggle/SwitchTrack", "SwitchTrack"),
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

    }
}
