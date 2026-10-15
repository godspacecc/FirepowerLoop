using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class TasksJournalManagementTests
    {
        private GameObject root;
        private TasksManagementPageUI tasks;
        private JournalManagementPageUI journal;
        private TasksManagementPageCtrl taskPage;
        private JournalManagementPageCtrl journalPage;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture(); LocalizationLanguages.ResetForTests();
            ResourceLoader.SetLoaderForTests(new LocalizationLoader());
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new MapSceneModel()); architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new ExpeditionTaskModel()); architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new SaveModel()); architecture.RegisterModel(new CivilisationTechnologyModel());
            architecture.RegisterModel(new CivilisationExpeditionModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")))));
            var localization = new LocalizationUtility(loadConfiguredTables: false); architecture.RegisterUtility(localization);
            foreach (var code in new[] { "zh-CN", "en-US" }) localization.LoadFromJson(code, File.ReadAllText("Assets/Game/Config/Localization/" + code + ".json"));
            architecture.GetModel<LocalizationModel>().SetLanguageCode("zh-CN");
            root = new GameObject("TasksJournalTestCanvas", typeof(RectTransform), typeof(Canvas));
            tasks = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/TasksManagement.prefab"), root.transform).GetComponent<TasksManagementPageUI>();
            journal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/JournalManagement.prefab"), root.transform).GetComponent<JournalManagementPageUI>();
            taskPage = new TasksManagementPageCtrl(); taskPage.SetUI(tasks);
            journalPage = new JournalManagementPageCtrl(); journalPage.SetUI(journal);
        }
        [TearDown]
        public void TearDown()
        {
            taskPage?.SetUI(null); journalPage?.SetUI(null);
            if (root != null) Object.DestroyImmediate(root);
            TestReset.ResetArchitecture(); ResourceLoader.ResetForTests(); LocalizationLanguages.ResetForTests();
        }
        [Test]
        public void ManagementHostContainsTasksAndJournalPages()
        {
            var host = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");
            var ui = host.GetComponent<BagUI>(); var collector = host.GetComponent<ReferenceCollector>();
            Assert.That(collector.Get<BaseUI>("TasksPage"), Is.SameAs(ui.TasksPage)); Assert.That(ui.TasksPage, Is.Not.Null);
            Assert.That(collector.Get<BaseUI>("JournalPage"), Is.SameAs(ui.JournalPage)); Assert.That(ui.JournalPage, Is.Not.Null);
            Assert.That(ui.TasksPage.transform.parent, Is.EqualTo(ui.ManagementRoot.transform));
            Assert.That(ui.JournalPage.transform.parent, Is.EqualTo(ui.ManagementRoot.transform));
        }
        [Test]
        public void GeneratedContractsAndTaskListSourceResolveAfterDeserialization()
        {
            foreach (var view in new BaseUI[] { tasks, journal })
            {
                foreach (var reference in view.GetComponent<ReferenceCollector>().data) Assert.That(reference.gameObject, Is.Not.Null, reference.key);
                foreach (var field in view.GetType().GetFields()) if (typeof(Object).IsAssignableFrom(field.FieldType)) Assert.That(field.GetValue(view), Is.Not.Null, field.Name);
            }
            Assert.That(tasks.TaskList.itemPrefab, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Box/ManagementTask.prefab")));
            var box = tasks.TaskList.itemPrefab.GetComponent<ManagementTaskBoxUI>(); Assert.That(box, Is.Not.Null);
            foreach (var field in typeof(ManagementTaskBoxUI).GetFields()) if (typeof(Object).IsAssignableFrom(field.FieldType)) Assert.That(field.GetValue(box), Is.Not.Null, field.Name);
        }
        [Test]
        public void EmptyTaskFiltersNeverFabricateObjectives()
        {
            taskPage.Show(); Assert.That(tasks.TaskList.totalCount, Is.Zero); Assert.That(tasks.EmptyRoot.activeSelf, Is.True);
            tasks.CompletedButton.onClick.Invoke(); Assert.That(tasks.TaskList.totalCount, Is.Zero);
            Assert.That(tasks.EmptyTitleText.text, Is.EqualTo(Loc.T("ui.tasks.empty_completed")));
            Assert.That(tasks.DetailBodyText.text, Is.Empty);
        }
        [Test]
        public void RealObjectiveProgressAndCompletionAreReadOnlyAndFiltered()
        {
            SetTask(false, 1); var before = Game.Interface.GetModel<ExpeditionTaskModel>().Capture(); taskPage.Show();
            Assert.That(tasks.TaskList.totalCount, Is.EqualTo(1)); Assert.That(tasks.DetailTitleText.text, Is.EqualTo(Loc.T("task.expedition.mission_sample.kill.name")));
            Assert.That(tasks.DetailBodyText.text, Does.Contain("1 / 2"));
            tasks.TaskList.GetDraggableItems().Single().GetComponent<ManagementTaskBoxUI>().SelectButton.onClick.Invoke();
            Assert.That(JToken.DeepEquals(before, Game.Interface.GetModel<ExpeditionTaskModel>().Capture()), Is.True);
            SetTask(false, 2); Game.Interface.SendEvent(new ExpeditionChangedEvent()); Assert.That(tasks.TaskList.totalCount, Is.Zero);
            tasks.CompletedButton.onClick.Invoke(); Assert.That(tasks.TaskList.totalCount, Is.EqualTo(1));
            Assert.That(tasks.DetailStatusText.text, Is.EqualTo(Loc.T("ui.tasks.completed_state")));
            Assert.That(tasks.DetailBodyText.text, Does.Contain(Loc.T("ui.tasks.completed_description")));
        }
        [Test]
        public void SurviveGoalUsesDurationAndWrongExpeditionOrOnlineDataIsHidden()
        {
            SetTask(true, 60); taskPage.Show();
            Assert.That(tasks.DetailBodyText.text, Does.Contain(ExpeditionClockRules.FormatDuration(60)));
            Assert.That(tasks.DetailBodyText.text, Does.Contain(ExpeditionClockRules.FormatDuration(1440)));
            Game.Interface.GetModel<ExpeditionModel>().SetExpeditionId("other"); Game.Interface.SendEvent(new ExpeditionChangedEvent());
            Assert.That(tasks.TaskList.totalCount, Is.Zero);
            SetTask(true, 60); Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null, MapPreparationMode.OnlinePlayersOnly);
            Game.Interface.SendEvent(new ExpeditionChangedEvent()); Assert.That(tasks.TaskList.totalCount, Is.Zero);
        }
        [Test]
        public void HiddenTaskPageUnsubscribesAndReopenReadsLatestProgress()
        {
            SetTask(false, 0); taskPage.Show(); taskPage.Show(); taskPage.Hide(); taskPage.Hide();
            var hiddenText = tasks.DetailBodyText.text; SetTask(false, 1); Game.Interface.SendEvent(new ExpeditionChangedEvent());
            Assert.That(tasks.DetailBodyText.text, Is.EqualTo(hiddenText));
            taskPage.Show(); Assert.That(tasks.TaskList.GetDraggableItems(), Has.Count.EqualTo(1));
            Assert.That(tasks.DetailBodyText.text, Does.Contain("1 / 2"));
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US"); Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(tasks.DetailTitleText.text, Is.EqualTo("Eliminate Threats"));
        }
        [Test]
        public void EmptyJournalCategoriesAndLanguageRefreshRemainCorrect()
        {
            journalPage.Show(); Assert.That(journal.DailySelected.activeSelf, Is.True);
            journal.NotesButton.onClick.Invoke(); Assert.That(journal.NotesSelected.activeSelf, Is.True); Assert.That(journal.DailySelected.activeSelf, Is.False);
            Assert.That(journal.DetailBodyText.text, Is.EqualTo(Loc.T("ui.journal.notes_hint")));
            journal.CollectionsButton.onClick.Invoke(); Assert.That(journal.CollectionsSelected.activeSelf, Is.True);
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US"); Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(journal.DetailBodyText.text, Is.EqualTo(Loc.T("ui.journal.collections_hint")));
            journalPage.Hide(); journalPage.Show(); Assert.That(journal.DailySelected.activeSelf, Is.True);
        }
        [Test]
        public void FixedLabelsUseReadablePreviewsAndEveryFormalLanguageHasKeys()
        {
            var chinese = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            var english = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            foreach (var name in new[] { "TasksManagement", "JournalManagement" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/" + name + ".prefab");
                foreach (var text in prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    Assert.That(text.text.StartsWith("ui."), Is.False, text.name);
                    var localized = text.GetComponent<LocalizedText>();
                    if (localized == null) continue;
                    Assert.That(chinese[localized.Key], Is.Not.Null); Assert.That(english[localized.Key], Is.Not.Null);
                    Assert.That(text.text, Is.EqualTo(chinese[localized.Key].Value<string>()));
                    Assert.That(prefab.GetComponent<ReferenceCollector>().data.Any(r => r.gameObject == text), Is.False,
                        "固定文案不应作为动态字段生成");
                }
            }
        }

        private static void SetTask(bool survival, float current)
        {
            var mapId = survival ? MapIds.SurvivalSample : MapIds.MissionSample;
            var taskId = survival ? "expedition.survival-sample.survive" : "expedition.mission-sample.kill";
            var target = survival ? 1440f : 2f; var status = current >= target ? "Completed" : "InProgress";
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Game.Interface.GetModel<ExpeditionModel>().BeginConfiguredExpedition(mapId, "test", true, survival ? 1440 : 0, survival ? 0 : 2);
            Game.Interface.GetModel<ExpeditionTaskModel>().Restore(new JObject { ["schemaVersion"] = 1, ["mapId"] = mapId, ["expeditionId"] = "test", ["tasks"] = new JArray(new JObject {
                ["instanceId"] = "test:" + taskId, ["taskId"] = taskId, ["mapId"] = mapId, ["expeditionId"] = "test", ["objectiveType"] = survival ? "Survive" : "Kill", ["targetAmount"] = target, ["currentAmount"] = current, ["order"] = 100, ["status"] = status }) });
        }
        private sealed class LocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object => typeof(T) == typeof(TextAsset)
                ? new TextAsset(File.ReadAllText("Assets/Game/Config/Localization/" + Path.GetFileName(path))) as T : null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object { callback?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
            public void Release(ResourceLoadHandle handle) { }
        }
    }
}
