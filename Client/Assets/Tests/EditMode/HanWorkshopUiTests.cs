using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>用正式工坊 Prefab 验证配方切换及锁定状态，输出独立 UI 预览供人工检查。</summary>
    public sealed class HanWorkshopUiTests
    {
        [Test] public void RecipeNavigationUpdatesProductAndLockState()
        {
            var support = new CivilisationProductionTests(); support.SetUp();
            var root = new GameObject("HanWorkshopPreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var cameraObject = new GameObject("HanWorkshopPreviewCamera", typeof(Camera));
            RenderTexture target = null; Texture2D pixels = null;
            try
            {
                root.SetActive(false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Civilisation/CivilisationWorkshop.prefab");
                var view = Object.Instantiate(prefab, root.transform).GetComponent<CivilisationWorkshopUI>();
                var controller = new CivilisationWorkshopUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, view);
                typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CivilisationWorkshopUICtrl).GetMethod("OnInit", flags).Invoke(controller, null);
                typeof(CivilisationWorkshopUICtrl).GetMethod("OnShow", flags).Invoke(controller, null);
                Assert.That(view.BodyText.text, Does.Contain("干粮"));
                Assert.That(view.BtnProduce.interactable, Is.False);
                view.BtnNext.onClick.Invoke();
                Assert.That(view.BodyText.text, Does.Contain("精锻斧"));
                Assert.That(view.BtnProduce.interactable, Is.False);
                var state = CivilisationTechnologyModel.Empty(); state["unlocked"] = new JArray("technology.iron-tools");
                Game.Interface.GetModel<CivilisationTechnologyModel>().Restore(state);
                Game.Interface.GetModel<CurrencyModel>().Restore(new JObject { ["schemaVersion"] = 1, ["gold"] = 100 });
                view.BtnNext.onClick.Invoke();
                Assert.That(view.BodyText.text, Does.Contain("精锻镐"));
                Assert.That(view.BtnProduce.interactable, Is.True);
                view.BtnPrevious.onClick.Invoke();
                Assert.That(view.BodyText.text, Does.Contain("精锻斧"));

                var canvas = root.GetComponent<Canvas>(); var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f,.09f,.11f);
                camera.orthographic = true; camera.orthographicSize = 450;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 900);
                target = new RenderTexture(1280,900,24); camera.targetTexture = target;
                root.SetActive(true); view.gameObject.SetActive(true); Canvas.ForceUpdateCanvases();
                foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
                camera.Render(); var oldTarget = RenderTexture.active;
                try
                {
                    RenderTexture.active = target; pixels = new Texture2D(1280,900,TextureFormat.RGB24,false);
                    pixels.ReadPixels(new Rect(0,0,1280,900),0,0); pixels.Apply();
                    Directory.CreateDirectory("Assets/Tests/Results~/HanChapter");
                    File.WriteAllBytes("Assets/Tests/Results~/HanChapter/workshop.png",pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = oldTarget; }
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject);
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null) Object.DestroyImmediate(target);
                support.TearDown();
            }
        }
    }
}
