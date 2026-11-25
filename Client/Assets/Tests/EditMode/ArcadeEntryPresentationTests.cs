using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证幸存者入场与正式加载页的进度所有权。</summary>
    public sealed class ArcadeEntryPresentationTests
    {
        private static readonly FieldInfo ArchitectureField =
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
        private object previousArchitecture;

        [SetUp]
        public void SetUp()
        {
            previousArchitecture = ArchitectureField.GetValue(null);
            var architecture = new Game();
            ArchitectureField.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new LocalizationModel());
            ResourceLoader.SetLoaderForTests(new EntryLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            architecture.RegisterUtility(new LocalizationUtility());
        }

        [TearDown]
        public void TearDown()
        {
            ArchitectureField.SetValue(null, previousArchitecture);
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [TestCase("entryPreparing")]
        [TestCase("mapLoadingActive")]
        public void LoadingCompletionWaitsForEveryOwner(string owner)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var ctrl = new LoadingUICtrl();
                var ui = instance.GetComponent<LoadingUI>();
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
                typeof(LoadingUICtrl).GetField(owner, flags).SetValue(ctrl, true);
                typeof(LoadingUICtrl).GetMethod("SetMapProgress", flags).Invoke(ctrl, new object[] { 1f });
                Assert.That(ui.PercentText.text, Is.Not.EqualTo("100%"), "准备所有者未释放时不能显示完成");
                Assert.That(ui.ProgressSlider.gameObject.activeSelf, Is.True, "后续准备仍纳入同一进度条");
                typeof(LoadingUICtrl).GetField(owner, flags).SetValue(ctrl, false);
                typeof(LoadingUICtrl).GetMethod("SetMapProgress", flags).Invoke(ctrl, new object[] { 1f });
                Assert.That(ui.PercentText.text, Is.EqualTo("100%"));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LoadingEntryStagesIgnoreOldRequestsAndOnlyCompleteOnSuccess(bool succeeded)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Common/Loading.prefab"));
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var ctrl = new LoadingUICtrl();
                var ui = instance.GetComponent<LoadingUI>();
                typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
                typeof(LoadingUICtrl).GetField("entryPreparing", flags).SetValue(ctrl, true);
                typeof(LoadingUICtrl).GetField("entryRequestId", flags).SetValue(ctrl, 2);
                typeof(LoadingUICtrl).GetField("mapProgress", flags).SetValue(ctrl, 1f);
                ctrl.ReportEntryPreparation(2, .85f, "ui.arcade.island.loading");
                Assert.That(ui.PercentText.text, Is.EqualTo("85%"));
                ctrl.ReportEntryPreparation(1, .99f, "ui.loading.stage.Scene");
                ctrl.EndEntryPreparation(1);
                Assert.That(ui.PercentText.text, Is.EqualTo("85%"));
                ctrl.ReportEntryPreparation(2, .95f, "ui.loading.stage.Activate");
                Assert.That(ui.PercentText.text, Is.EqualTo("95%"));
                Assert.That(ui.MessageText.text, Is.EqualTo(Loc.T("ui.loading.stage.Activate")));
                ctrl.EndEntryPreparation(2, succeeded);
                Assert.That(ui.PercentText.text == "100%", Is.EqualTo(succeeded));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        /// <summary>复用正式语言表验证界面文本。</summary>
        private sealed class EntryLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object =>
                AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path);
            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
