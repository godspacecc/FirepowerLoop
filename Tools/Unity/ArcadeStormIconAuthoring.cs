using System;
using System.IO;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>离线绘制透明旋风轮廓并绑定正式手机风暴按钮，避免与冲刺闪电混淆。</summary>
public static class ArcadeStormIconAuthoring
{
    private const string IconPath = "Assets/Game/Art/UI/UtilityButtons/StormVortex.png";
    private const string HudPath = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";

    public static string Run()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅在 FirepowerLoop 编辑模式制作图标");
        DrawIcon();
        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        var root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            var ui = root.GetComponent<ArcadeHudUI>();
            var icon = ui.MobileFieldUpgradeFace.Find("StormIcon").GetComponent<Image>();
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            icon.color = new Color(.2f, .8f, 1);
            icon.preserveAspect = true; icon.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath));
        return Bind();
    }

    /// <summary>生成器执行后回填现有 View，并核对风暴、冲刺没有共用同一图标。</summary>
    public static string Bind()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { HudPath, "Game.ArcadeHudUI" });
        var ui = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath).GetComponent<ArcadeHudUI>();
        var storm = ui.MobileFieldUpgradeFace.Find("StormIcon").GetComponent<Image>();
        var dash = ui.BtnDash.transform.Find("AbilityIcon").GetComponent<Image>();
        if (storm.sprite == null || storm.sprite == dash.sprite)
            throw new InvalidOperationException("风暴必须使用独立旋风 Sprite");
        return "风暴已使用独立透明旋风图标，冲刺保留原闪电";
    }

    /// <summary>用三条向中心盘旋的渐粗曲线生成静态抗锯齿图标，白色轮廓由 UI 染成青色。</summary>
    private static void DrawIcon()
    {
        const int size = 256, steps = 96;
        var points = new Vector2[3, steps + 1];
        for (var arm = 0; arm < 3; arm++)
        for (var step = 0; step <= steps; step++)
        {
            float t = (float)step / steps;
            float angle = arm * Mathf.PI * 2 / 3 + t * 3.5f;
            float radius = Mathf.Lerp(100, 20, t);
            points[arm, step] = new Vector2(128 + Mathf.Cos(angle) * radius, 128 + Mathf.Sin(angle) * radius);
        }
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var point = new Vector2(x + .5f, y + .5f);
            float distance = float.PositiveInfinity;
            for (var arm = 0; arm < 3; arm++)
            for (var step = 0; step < steps; step++)
            {
                var start = points[arm, step]; var segment = points[arm, step + 1] - start;
                float along = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
                float t = (step + along) / steps;
                float halfWidth = Mathf.Lerp(.3f, 12f, Mathf.Sin(t * Mathf.PI * .5f));
                distance = Mathf.Min(distance, Vector2.Distance(point, start + along * segment) - halfWidth);
            }
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(.75f - distance) * 255);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try { texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(IconPath, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
    }
}
