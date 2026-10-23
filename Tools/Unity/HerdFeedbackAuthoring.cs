using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
/// <summary>将已采用候选的同源软边贴图和材质持久化，只给正式牛模板增加明确的材质引用。</summary>
public static class HerdFeedbackAuthoring
{
    public static string Run()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("仅本项目空闲编辑态可保存已采用反馈素材");
        const string folder = "Assets/Game/Art/Arcade/Farm/HerdFeedback";
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/HerdDustSoft.asset");
        if (texture == null)
        {
            texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "HerdDustSoft", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)), .8f)));
            }
            texture.Apply(); AssetDatabase.CreateAsset(texture, folder + "/HerdDustSoft.asset");
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/HerdDust.mat");
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "HerdDust" };
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            AssetDatabase.CreateAsset(material, folder + "/HerdDust.mat");
        }
        const string prefabPath = "Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var serialized = new SerializedObject(prefab.GetComponent<ArcadeHerdVisual>());
            serialized.FindProperty("dustMaterial").objectReferenceValue = material; serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        AssetDatabase.SaveAssets();
        return "已采用材质持久化，牛模板引用完成；未改模型、尺寸、碰撞或动作参数";
    }
}
