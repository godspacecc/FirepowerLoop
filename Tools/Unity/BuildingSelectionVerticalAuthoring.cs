using System;
using System.IO;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>离线制作右侧建造列表及旧建筑图标；不改变建筑资格、消费或拆除规则。</summary>
public static class BuildingSelectionVerticalAuthoring
{
    public static string Apply()
    {
        Row(); Panel();
        foreach (var name in new[] { "GrayboxArrowTower", "GrayboxRoadblock", "GrayboxCampfire" }) Icon(name);
        AssetDatabase.SaveAssets();
        return "Saved right-side vertical building panel, scrollbar and three prefab-rendered icons.";
    }

    static void Row()
    {
        const string path = "Assets/Game/Prefabs/UI/TemporaryBuilding/Box/TemporaryBuildingOptionBox.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var ui = root.GetComponent<TemporaryBuildingOptionBoxUI>();
            var old = ui.BtnSelect;
            if (old.transform != root.transform) Object.DestroyImmediate(old.gameObject);
            var image = root.GetComponent<Image>() ?? root.AddComponent<Image>();
            image.color = new Color(.10f, .15f, .19f, .98f);
            ui.BtnSelect = root.GetComponent<Button>() ?? root.AddComponent<Button>();
            ui.BtnSelect.targetGraphic = image;
            root.GetComponent<ReferenceCollector>().Add("BtnSelect", ui.BtnSelect);
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(490, 112);
            var layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 112; layout.flexibleHeight = 0;
            Rect((RectTransform)ui.Icon.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(55, 0), new Vector2(84, 84));
            ui.Icon.raycastTarget = false; ui.Icon.preserveAspect = true;
            Rect((RectTransform)ui.TextName.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(48, -30), new Vector2(-126, 36));
            Rect((RectTransform)ui.TextMaterials.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(48, 32), new Vector2(-126, 44));
            ui.TextName.fontSize = 27; ui.TextName.alignment = TextAlignmentOptions.MidlineLeft;
            ui.TextMaterials.fontSize = 19; ui.TextMaterials.alignment = TextAlignmentOptions.MidlineLeft;
            ui.TextName.raycastTarget = ui.TextMaterials.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }

    static void Panel()
    {
        const string path = "Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingSelect.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var ui = root.GetComponent<TemporaryBuildingSelectUI>();
            Rect((RectTransform)root.transform, new Vector2(1, 0), Vector2.one, new Vector2(-304, 0), new Vector2(560, -140));
            root.GetComponent<Image>().color = new Color(.055f, .085f, .11f, .98f);
            Rect((RectTransform)ui.BuildingScroll.transform, Vector2.zero, Vector2.one, new Vector2(0, -5), new Vector2(-40, -190));
            ui.BuildingScroll.horizontal = false; ui.BuildingScroll.vertical = true;
            ui.BuildingScroll.horizontalScrollbar = null;
            var content = ui.BuildingScroll.content;
            var old = content.GetComponent<HorizontalLayoutGroup>(); if (old != null) Object.DestroyImmediate(old);
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10; layout.padding = new RectOffset(0, 24, 0, 0);
            layout.childAlignment = TextAnchor.UpperLeft; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Rect(content, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero); content.pivot = new Vector2(.5f, 1);
            var barTransform = ui.BuildingScroll.transform.Find("VerticalScrollbar");
            var barGo = barTransform != null ? barTransform.gameObject : new GameObject("VerticalScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barGo.transform.SetParent(ui.BuildingScroll.transform, false);
            Rect((RectTransform)barGo.transform, new Vector2(1, 0), Vector2.one, new Vector2(-8, 0), new Vector2(24, 0));
            barGo.GetComponent<Image>().color = new Color(.15f, .20f, .24f, 1);
            var handleTransform = barGo.transform.Find("Handle");
            var handle = handleTransform != null ? handleTransform.gameObject : new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(barGo.transform, false);
            Rect((RectTransform)handle.transform, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-8, 0));
            handle.GetComponent<Image>().color = new Color(.79f, .62f, .32f, 1);
            var bar = barGo.GetComponent<Scrollbar>(); bar.handleRect = (RectTransform)handle.transform;
            bar.targetGraphic = handle.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop;
            ui.BuildingScroll.verticalScrollbar = bar;
            ui.BuildingScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            Rect((RectTransform)ui.BtnClose.transform, Vector2.one, Vector2.one, new Vector2(-34, -34), new Vector2(44, 44));
            Rect((RectTransform)ui.BtnDestroy.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(500, 58));
            Rect((RectTransform)root.transform.Find("Text"), new Vector2(0, 1), Vector2.one, new Vector2(-20, -38), new Vector2(-90, 42));
            root.transform.Find("Text").GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }

    static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    { rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }

    static void Icon(string name)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/" + name + ".prefab"));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
        var cameraObject = new GameObject("IconCamera", typeof(Camera));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.GetComponent<Camera>(); var target = new RenderTexture(256, 256, 24);
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false); var previous = RenderTexture.active;
        try
        {
            camera.scene = scene; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .12f, .16f, 1);
            camera.orthographic = true; camera.orthographicSize = Mathf.Max(.5f, bounds.size.magnitude * .58f);
            camera.transform.position = bounds.center + new Vector3(1.3f, 1, -1.5f).normalized * (bounds.size.magnitude * 3 + 5);
            camera.transform.LookAt(bounds.center); camera.targetTexture = target;
            var light = new GameObject("Light", typeof(Light)); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light, scene);
            light.GetComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 2; light.transform.rotation = Quaternion.Euler(40, -30, 0);
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new UnityEngine.Rect(0, 0, 256, 256), 0, 0); texture.Apply();
            var path = "Assets/Game/Art/UI/TemporaryBuilding/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single; importer.mipmapEnabled = false; importer.SaveAndReimport();
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
