using Game;
using Newtonsoft.Json;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class MobileFrameRateSettingsTests
    {
        [TestCase(30, 30)]
        [TestCase(60, 60)]
        [TestCase(0, 60)]
        [TestCase(120, 60)]
        public void SavedFrameRateSurvivesCloneAndNormalizesInvalidValues(int input, int expected)
        {
            var data = JsonConvert.DeserializeObject<DisplaySettingsData>("{\"mobileFrameRate\":" + input + "}").Clone().Sanitize();
            var json = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(data));
            Assert.That((int?)json["mobileFrameRate"], Is.EqualTo(expected));
        }

        [Test]
        public void DisplayModelRestoresSavedFrameRateAndResetsToDefault()
        {
            var model = new DisplaySettingsModel();
            model.Apply(JsonConvert.DeserializeObject<DisplaySettingsData>("{\"mobileFrameRate\":30}"));
            Assert.That((int?)Newtonsoft.Json.Linq.JObject.FromObject(model.ToData())["mobileFrameRate"], Is.EqualTo(30));
            model.ResetToDefault();
            Assert.That((int?)Newtonsoft.Json.Linq.JObject.FromObject(model.ToData())["mobileFrameRate"], Is.EqualTo(60));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void GeneralRowsStartAtPageTopForAnySmallCount(int count)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Page/SettingGeneral.prefab");
            var root = Object.Instantiate(prefab);
            try
            {
                var ui = root.GetComponent<SettingGeneralPageUI>();
                Assert.NotNull(root.GetComponent<ReferenceCollector>());
                Assert.NotNull(ui.SettingList);
                Assert.NotNull(ui.ResetButton);
                ui.SettingList.totalCount = count;
                ui.SettingList.RefreshCells();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)ui.SettingList.transform);
                var row = (RectTransform)ui.SettingList.transform.GetChild(0);
                var corners = new Vector3[4];
                row.GetWorldCorners(corners);
                var top = root.transform.InverseTransformPoint(corners[1]).y;
                Assert.That(top, Is.EqualTo(((RectTransform)root.transform).rect.yMax - 26f).Within(1f));
                Assert.That(ui.SettingList.transform.GetChild(count - 1).gameObject.activeSelf, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
