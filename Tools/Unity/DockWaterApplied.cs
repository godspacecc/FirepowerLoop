using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>把已采用预览的真实平台岸距烘焙到正式纹理；不改码头网格或场景。</summary>
public static class DockWaterApplied
{
    private const string Art = "Assets/Game/Art/Arcade/Docks/";
    private const string TexturePath = Art + "DockShoreDistance.png";

    /// <summary>与已采用的DockWaterPreview源文件一同动态编译，保证沿用原岸线算法。</summary>
    public static string Bake()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("只允许本工程空闲编辑态");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab");
        var water = prefab.GetComponentsInChildren<Renderer>(true).Single(r => r.sharedMaterials.Any(m => m.shader.name == "FirepowerLoop/DockWater"));
        // 合并网格与码头根坐标一致；正式Shader使用局部坐标以随地图一起移动。
        if (water.transform.localToWorldMatrix != Matrix4x4.identity)
            throw new InvalidOperationException("岸距烘焙要求水面合并网格使用码头根坐标");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Art + "DockWater.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("正式水面Shader未通过编译");
        var method = typeof(DockWaterPreview).GetMethod("BuildShore", BindingFlags.NonPublic | BindingFlags.Static);
        Texture2D source = null, packed = null;
        try
        {
            source = (Texture2D)method.Invoke(null, new object[] { prefab, water });
            var distances = source.GetPixelData<float>(0);
            var colors = new Color32[distances.Length];
            float maxError = 0;
            for (int i = 0; i < distances.Length; i++)
            {
                // RG两通道保存0..32米的16位线性数；RGBA32不依赖手机浮点纹理过滤支持。
                float meters = Mathf.Clamp(distances[i], 0, 32);
                int value = Mathf.RoundToInt(meters / 32 * 65535);
                colors[i] = new Color32((byte)(value >> 8), (byte)(value & 255), 0, 255);
                maxError = Mathf.Max(maxError, Mathf.Abs(value / 65535f * 32 - meters));
            }
            packed = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            packed.SetPixels32(colors); packed.Apply();
            File.WriteAllBytes(TexturePath, packed.EncodeToPNG());
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.alphaSource = TextureImporterAlphaSource.None;
            // 数据纹理不能被已有平台压缩或缩小覆盖破坏编码。
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" }) importer.ClearPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null) throw new InvalidOperationException("岸距纹理尚未导入完成，必须完成导入后重新执行，不能保存空材质引用");
            var material = water.sharedMaterials.Single(m => m.shader == shader);
            material.SetTexture("_ShoreDistance", texture);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return "正式岸距=" + TexturePath + ";512x512;量化最大误差米=" + maxError.ToString("R") + ";材质=" + AssetDatabase.GetAssetPath(material);
        }
        finally
        {
            if (source != null) Object.DestroyImmediate(source);
            if (packed != null) Object.DestroyImmediate(packed);
        }
    }
}
