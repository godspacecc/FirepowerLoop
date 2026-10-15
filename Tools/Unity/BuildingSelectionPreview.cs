using System;
using System.IO;
using System.Linq;
using Game;
using Luban;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>在临时场景渲染正式右侧列表；不加载玩家存档、不保存预览对象。</summary>
public static class BuildingSelectionPreview
{
    public static string Render()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var canvasObject = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(1920, 1080);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab"), canvasObject.transform);
        foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true)) localized.enabled = false;
        var ui = root.GetComponent<TemporaryBuildingSelectUI>();
        var table = new global::TbTempBuilding(new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/tbtempbuilding.bytes").bytes));
        ui.BuildingList.AddItemRefreshListener((item, index) => {
            var box = item.GetComponent<TemporaryBuildingOptionBoxUI>(); var data = table.DataList[index];
            foreach (var localized in box.GetComponentsInChildren<LocalizedText>(true)) localized.enabled = false;
            box.TextName.text = data.Name;
            box.TextMaterials.text = string.Join("  ", data.Materials.Select(material =>
                (material.ItemId == 9 ? "木材" : material.ItemId == 2 ? "布料" : "金属零件") + " 20/" + material.Amount));
            box.Icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/UI/TemporaryBuilding/" + data.IconName + ".png");
        });
        ui.BuildingList.totalCount = table.DataList.Count; ui.BuildingList.RefreshCells();
        var label = new GameObject("SceneLabel", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)label.transform).anchoredPosition = new Vector2(-260, 0); ((RectTransform)label.transform).sizeDelta = new Vector2(900, 90);
        var text = label.GetComponent<TextMeshProUGUI>(); text.font = ui.BtnDestroy.GetComponentInChildren<TextMeshProUGUI>().font;
        text.text = "基地场景区域"; text.fontSize = 40; text.color = new Color(.42f, .51f, .56f); text.alignment = TextAlignmentOptions.Center;
        var cameraObject = new GameObject("PreviewCamera", typeof(Camera)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene; camera.orthographic = true; camera.orthographicSize = 540;
        camera.transform.position = new Vector3(0, 0, -100); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .11f, .14f);
        canvas.worldCamera = camera;
        var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGBA32, false); var previous = RenderTexture.active;
        try
        {
            canvasObject.SetActive(true); Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(ui.BuildingScroll.content);
            ui.BuildingScroll.verticalNormalizedPosition = 1; Canvas.ForceUpdateCanvases();
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            const string path = "Assets/Tests/Results~/BaseDecorationResearch/building-selection-right.png";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, texture.EncodeToPNG()); return Path.GetFullPath(path);
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
