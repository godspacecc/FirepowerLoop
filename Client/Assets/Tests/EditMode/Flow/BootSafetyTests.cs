using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Boot;
using Boot.UI;

namespace Game.Tests
{
    [TestFixture]
    public sealed class BootSafetyTests
    {
        private const string BootScenePath = "Assets/Game/Scenes/Boot.unity";
        private const string HostConfigPath = "Assets/StreamingAssets/hostConfig.json";
        private const string OldHostConfigPath = "Assets/StreamingAssets/3dbase-client-config.json";

        private static readonly string[] HotUpdateViewFields =
        {
            "_statusText",
            "_versionText",
            "_titleText",
            "_guidanceText",
            "_detailsText",
            "_progressArea",
            "_downloadStatsArea",
            "_buttonArea",
            "_progressSlider",
            "_progressText",
            "_sizeText",
            "_fileCountText",
            "_speedText",
            "_retryButton",
            "_copyDetailsButton",
            "_quitButton"
        };

        [Test]
        public void HostConfigUsesReadableFileNameAndPreservesContract()
        {
            Assert.That(BootstrapConfig.FileName, Is.EqualTo("hostConfig.json"));
            Assert.That(File.Exists(HostConfigPath), Is.True);
            Assert.That(File.Exists(OldHostConfigPath), Is.False);

            string json = File.ReadAllText(HostConfigPath);
            Assert.That(json, Does.Contain("\"resourceServer\""));
            var config = JsonUtility.FromJson<HostConfigContract>(json);
            Assert.That(config, Is.Not.Null);
            Assert.That(Uri.TryCreate(config.resourceServer, UriKind.Absolute, out Uri resourceServer), Is.True);
            Assert.That(
                resourceServer.Scheme,
                Is.EqualTo(Uri.UriSchemeHttp).Or.EqualTo(Uri.UriSchemeHttps));
        }

        [Serializable]
        private sealed class HostConfigContract
        {
            public string resourceServer;
        }

        [Test]
        public void AndroidOfflineCandidateCanSelectBuiltinResourcesWithoutChangingHostDefault()
        {
            string androidBuild = File.ReadAllText("Assets/Editor/Build/AndroidBuildUtility.cs");
            string resourceBoot = File.ReadAllText("Assets/Boot/Res/ResourceBoot.cs");

            Assert.That(androidBuild, Does.Contain("FIREPOWERLOOP_ANDROID_BUILTIN_RESOURCES"));
            Assert.That(androidBuild, Does.Contain("extraScriptingDefines"));
            Assert.That(resourceBoot, Does.Contain("#elif FIREPOWERLOOP_ANDROID_BUILTIN_RESOURCES"));
            Assert.That(resourceBoot, Does.Contain("_actualPlayMode = ResourcePlayMode.OfflinePlayMode;"));
            Assert.That(resourceBoot, Does.Contain("_actualPlayMode = ResourcePlayMode.HostPlayMode;"));
        }

        [Test]
        public void NonDevelopmentPlayerAllowsHttpAndBuildValidationRequiresIt()
        {
            Assert.That(PlayerSettings.insecureHttpOption, Is.EqualTo(InsecureHttpOption.AlwaysAllowed));

            Type type = Type.GetType("Game.Editor.BuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod(
                "ValidateInsecureHttpOption",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.DoesNotThrow(() =>
                method.Invoke(null, new object[] { InsecureHttpOption.AlwaysAllowed }));

            var exception = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { InsecureHttpOption.NotAllowed }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException.Message, Does.Contain("AlwaysAllowed"));
            Assert.That(exception.InnerException.Message, Does.Contain("NotAllowed"));
        }

        [Test]
        public void BootSceneBindsLauncherAndAllHotUpdateFields()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                Launcher launcher = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Launcher>(true))
                    .Single();
                var launcherObject = new SerializedObject(launcher);
                var updateViewProperty = launcherObject.FindProperty("_updateView");
                Assert.That(updateViewProperty, Is.Not.Null);
                Assert.That(updateViewProperty.objectReferenceValue, Is.Not.Null);

                var view = (HotUpdateView)updateViewProperty.objectReferenceValue;
                var viewObject = new SerializedObject(view);
                foreach (string field in HotUpdateViewFields)
                {
                    SerializedProperty property = viewObject.FindProperty(field);
                    Assert.That(property, Is.Not.Null, $"缺少序列化字段：{field}");
                    Assert.That(property.objectReferenceValue, Is.Not.Null,
                        $"Boot 场景未绑定：HotUpdateView.{field}");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void BootPanelBindsNormalFailureAndBusyRegions()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                var canvas = scene.GetRootGameObjects().Single(root => root.name == "HotUpdateCanvas");
                var scaler = canvas.GetComponent<CanvasScaler>();
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
                Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f));

                var panel = canvas.transform.Find("Panel").GetComponent<RectTransform>();
                Assert.That(panel.anchorMin, Is.EqualTo(new Vector2(0.25f, 0f)));
                Assert.That(panel.anchorMax, Is.EqualTo(new Vector2(0.75f, 0f)));
                Assert.That(panel.sizeDelta.x, Is.LessThan(0f));
                Assert.That(panel.anchoredPosition.y, Is.GreaterThan(0f));

                var view = panel.GetComponent<HotUpdateView>();
                var viewObject = new SerializedObject(view);
                Assert.That(viewObject.FindProperty("_progressArea").objectReferenceValue, Is.Not.Null);
                Assert.That(viewObject.FindProperty("_downloadStatsArea").objectReferenceValue, Is.Not.Null);
                Assert.That(viewObject.FindProperty("_buttonArea").objectReferenceValue, Is.Not.Null);
                Assert.That(viewObject.FindProperty("_copyDetailsButton").objectReferenceValue, Is.Not.Null);
                Assert.That(viewObject.FindProperty("_quitButton").objectReferenceValue, Is.Not.Null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void BootDownloadProgressHasVisibleFillAndIndependentTextRegions()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                var view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<HotUpdateView>(true))
                    .Single();
                var serialized = new SerializedObject(view);
                var slider = (Slider)serialized.FindProperty("_progressSlider").objectReferenceValue;
                var progressArea = (GameObject)serialized.FindProperty("_progressArea").objectReferenceValue;
                var statsArea = (GameObject)serialized.FindProperty("_downloadStatsArea").objectReferenceValue;
                var title = serialized.FindProperty("_titleText").objectReferenceValue;
                var progress = serialized.FindProperty("_progressText").objectReferenceValue;
                var guidance = serialized.FindProperty("_guidanceText").objectReferenceValue;
                var size = (Component)serialized.FindProperty("_sizeText").objectReferenceValue;
                var details = serialized.FindProperty("_detailsText").objectReferenceValue;
                var speed = serialized.FindProperty("_speedText").objectReferenceValue;

                Assert.That(slider.fillRect, Is.Not.Null);
                Assert.That(slider.targetGraphic, Is.Not.Null);
                Assert.That(slider.fillRect.GetComponent<Image>(), Is.Not.Null);
                Assert.That(slider.transform.IsChildOf(progressArea.transform), Is.True);
                Assert.That(size.transform.IsChildOf(statsArea.transform), Is.True);
                Assert.That(title, Is.Not.SameAs(progress));
                Assert.That(guidance, Is.Not.SameAs(size));
                Assert.That(details, Is.Not.SameAs(speed));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void BootOnlyShowsDownloadMetricsDuringActualDownload()
        {
            Scene scene = EditModeSceneIsolation.OpenBootSceneCopy(BootScenePath, out _, out var tempScenePath);
            try
            {
                var view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<HotUpdateView>(true))
                    .Single();
                var serialized = new SerializedObject(view);
                var title = (Text)serialized.FindProperty("_titleText").objectReferenceValue;
                var status = (Text)serialized.FindProperty("_statusText").objectReferenceValue;
                var progressArea = (GameObject)serialized.FindProperty("_progressArea").objectReferenceValue;
                var statsArea = (GameObject)serialized.FindProperty("_downloadStatsArea").objectReferenceValue;
                var fileCount = (Text)serialized.FindProperty("_fileCountText").objectReferenceValue;

                view.InitializeForTests();
                Assert.That(title.gameObject.activeSelf, Is.False);
                Assert.That(status.gameObject.activeSelf, Is.True);
                Assert.That(progressArea.activeSelf, Is.False);
                Assert.That(statsArea.activeSelf, Is.False);

                view.RenderDownloading("正在下载资源...");
                Assert.That(progressArea.activeSelf, Is.True);
                Assert.That(statsArea.activeSelf, Is.True);
                Assert.That(fileCount.gameObject.activeSelf, Is.False);
            }
            finally
            {
                EditModeSceneIsolation.CloseAndDeleteSceneCopy(scene, tempScenePath);
            }
        }

        [Test]
        public void DownloadMetricsShowSpeedAndEstimatedRemainingTime()
        {
            Scene scene = EditModeSceneIsolation.OpenBootSceneCopy(BootScenePath, out _, out var tempScenePath);
            try
            {
                var view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<HotUpdateView>(true))
                    .Single();
                view.InitializeForTests();
                var serialized = new SerializedObject(view);
                var speed = (Text)serialized.FindProperty("_speedText").objectReferenceValue;

                view.SetDownloadEstimate(2UL * 1024UL * 1024UL, 65d);
                Assert.That(speed.text, Does.Contain("2.00 MB/s"));
                Assert.That(speed.text, Does.Contain("预计剩余: 约 1分 5秒"));

                view.SetDownloadEstimate(0, null);
                Assert.That(speed.text, Does.Contain("预计剩余: 计算中"));
            }
            finally
            {
                EditModeSceneIsolation.CloseAndDeleteSceneCopy(scene, tempScenePath);
            }
        }

        [Test]
        public void BootFailureDetailsPreserveLongAotSummary()
        {
            Scene scene = EditModeSceneIsolation.OpenBootSceneCopy(BootScenePath, out _, out var tempScenePath);
            try
            {
                var view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<HotUpdateView>(true))
                    .Single();
                view.InitializeForTests();
                var serialized = new SerializedObject(view);
                var title = (Text)serialized.FindProperty("_titleText").objectReferenceValue;
                var guidance = (Text)serialized.FindProperty("_guidanceText").objectReferenceValue;
                var details = (Text)serialized.FindProperty("_detailsText").objectReferenceValue;
                var copy = (Button)serialized.FindProperty("_copyDetailsButton").objectReferenceValue;
                var quit = (Button)serialized.FindProperty("_quitButton").objectReferenceValue;
                var panel = view.GetComponent<RectTransform>();
                var canvas = view.GetComponentInParent<Canvas>().GetComponent<RectTransform>();

                var failure = BootFailure.Create(
                    BootFailureCode.AotMetadataRejected,
                    BootFailureStage.AotMetadata,
                    BootRetryScope.None,
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["dllName"] = "VeryLongAotDependencyName.Core.Runtime.Support.Library.dll",
                        ["metadataResult"] = "BadImageFormat_2148734208"
                    });
                view.RenderFailure(failure);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

                Assert.That(title.text, Does.Contain("AOT"));
                Assert.That(guidance.text, Does.Contain("完整安装包"));
                Assert.That(details.text, Does.Contain("Code: AotMetadataRejected"));
                Assert.That(details.text, Does.Contain("Stage: AotMetadata"));
                Assert.That(details.text, Does.Contain("RetryScope: None"));
                Assert.That(details.text, Does.Contain("VeryLongAotDependencyName"));
                Assert.That(details.text, Does.Contain("BadImageFormat_2148734208"));
                Assert.That(details.rectTransform.sizeDelta.y, Is.GreaterThanOrEqualTo(120f));
                Assert.That(details.verticalOverflow, Is.EqualTo(VerticalWrapMode.Truncate));
                Assert.That(details.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Wrap));
                Assert.That(Overlaps(title.rectTransform, guidance.rectTransform), Is.False);
                Assert.That(Overlaps(guidance.rectTransform, details.rectTransform), Is.False);
                Assert.That(Overlaps(details.rectTransform, copy.GetComponent<RectTransform>()), Is.False);
                Assert.That(IsInside(canvas, copy.GetComponent<RectTransform>()), Is.True);
                Assert.That(IsInside(canvas, quit.GetComponent<RectTransform>()), Is.True);
            }
            finally
            {
                EditModeSceneIsolation.CloseAndDeleteSceneCopy(scene, tempScenePath);
            }
        }

        [Test]
        public void BootSceneHasOneInputSystemEventSystem()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            try
            {
                var eventSystems = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EventSystem>(true))
                    .ToArray();
                Assert.That(eventSystems, Has.Length.EqualTo(1));
                Assert.That(eventSystems[0].GetComponent(GetInputSystemUiInputModuleType()), Is.Not.Null);
                Assert.That(eventSystems[0].GetComponent<StandaloneInputModule>(), Is.Null);
                Assert.That(eventSystems[0].transform.parent, Is.Null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        [Test]
        public void LauncherMissingUpdateViewFailsWithContext()
        {
            var root = new GameObject("BootSafetyLauncher");
            try
            {
                root.SetActive(false);
                root.AddComponent<ResourceBoot>();
                var launcher = root.AddComponent<Launcher>();

                Exception error = InvokeValidation(launcher);
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
                Assert.That(error.Message, Does.Contain("BootSafetyLauncher"));
                Assert.That(error.Message, Does.Contain("Launcher"));
                Assert.That(error.Message, Does.Contain("_updateView"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase("_statusText")]
        [TestCase("_versionText")]
        [TestCase("_progressSlider")]
        [TestCase("_progressText")]
        [TestCase("_sizeText")]
        [TestCase("_fileCountText")]
        [TestCase("_speedText")]
        [TestCase("_retryButton")]
        [TestCase("_copyDetailsButton")]
        [TestCase("_quitButton")]
        public void HotUpdateViewMissingRequiredFieldFailsWithContext(string missingField)
        {
            var root = new GameObject("BootSafetyView");
            try
            {
                root.SetActive(false);
                var view = root.AddComponent<HotUpdateView>();
                var serialized = new SerializedObject(view);

                foreach (string field in HotUpdateViewFields)
                {
                    GameObject child = new GameObject(field);
                    child.transform.SetParent(root.transform);
                    UnityEngine.Object component = field == "_progressSlider"
                        ? (UnityEngine.Object)child.AddComponent<Slider>()
                        : field.EndsWith("Button", StringComparison.Ordinal)
                            ? child.AddComponent<Button>()
                            : field.EndsWith("Area", StringComparison.Ordinal)
                                ? child
                                : child.AddComponent<Text>();
                    serialized.FindProperty(field).objectReferenceValue = component;
                }

                serialized.FindProperty(missingField).objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Exception error = InvokeValidation(view);
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
                Assert.That(error.Message, Does.Contain("BootSafetyView"));
                Assert.That(error.Message, Does.Contain("HotUpdateView"));
                Assert.That(error.Message, Does.Contain(missingField));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RequiredBootUiDoesNotUseSilentNullConditionalAccess()
        {
            string launcher = File.ReadAllText("Assets/Boot/Launcher.cs");
            string resourceBoot = File.ReadAllText("Assets/Boot/Res/ResourceBoot.cs");
            string view = File.ReadAllText("Assets/Boot/UI/HotUpdate/HotUpdateView.cs");

            Assert.That(launcher, Does.Not.Contain("_updateView?."));
            Assert.That(launcher, Does.Not.Contain("_updateView != null"));
            Assert.That(resourceBoot, Does.Not.Contain("_updateView?."));

            foreach (string field in HotUpdateViewFields)
            {
                Assert.That(view, Does.Not.Contain($"{field} != null"));
                Assert.That(view, Does.Not.Contain($"{field} == null"));
                Assert.That(view, Does.Not.Contain($"{field}?."));
            }

            Assert.That(view, Does.Contain("OnRetryClicked?.Invoke()"),
                "可选事件无订阅者时仍应安全不触发");
        }

        [Test]
        public void LauncherSetupIsRemoved()
        {
            Assert.That(File.Exists("Assets/Editor/LauncherSetup.cs"), Is.False);
            Assert.That(File.Exists("Assets/Editor/LauncherSetup.cs.meta"), Is.False);
            Assert.That(Type.GetType("LauncherSetup, Assembly-CSharp-Editor"), Is.Null);
        }

        private static Exception InvokeValidation(Component component)
        {
            MethodInfo method = component.GetType().GetMethod(
                "ValidateRequiredReferences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null,
                $"{component.GetType().FullName}.ValidateRequiredReferences 缺失");

            var exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(component, null));
            return exception.InnerException;
        }

        private static Type GetInputSystemUiInputModuleType()
        {
            Type type = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(type, Is.Not.Null);
            return type;
        }

        private static bool Overlaps(RectTransform first, RectTransform second)
        {
            Rect firstRect = GetWorldRect(first);
            Rect secondRect = GetWorldRect(second);
            return firstRect.Overlaps(secondRect);
        }

        private static bool IsInside(RectTransform outer, RectTransform inner)
        {
            Rect outerRect = GetWorldRect(outer);
            Rect innerRect = GetWorldRect(inner);
            return outerRect.xMin <= innerRect.xMin &&
                outerRect.xMax >= innerRect.xMax &&
                outerRect.yMin <= innerRect.yMin &&
                outerRect.yMax >= innerRect.yMax;
        }

        private static Rect GetWorldRect(RectTransform transform)
        {
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
