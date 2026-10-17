using System;
using UnityEditor;
using UnityEngine;

/// <summary>首轮只统一正式海岛材质配色；不移动场景、修改碰撞、主角、灯光或玩法数据。</summary>
public static class ArcadeBrightIslandPaletteAuthoring
{
    public static string Run()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅本工程编辑态允许制作");
        Set("Sand", new Color(.78f, .68f, .46f));
        Set("Water", new Color(.08f, .36f, .50f));
        Set("ShallowWater", new Color(.18f, .66f, .69f));
        Set("Shoreline", new Color(.79f, .91f, .85f));
        Set("Rock", new Color(.31f, .40f, .43f));
        Set("Trunk", new Color(.35f, .24f, .16f));
        Set("PalmLeaves", new Color(.18f, .49f, .28f));
        AssetDatabase.SaveAssets();
        return "海岛七个正式材质更新：浅暖沙地、青蓝水岸、冷灰障碍和绿棕植被；无几何或光照变更";
    }

    private static void Set(string name, Color color)
    {
        string path = "Assets/Game/Art/Arcade/Island/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) throw new InvalidOperationException(path);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", .12f);
        EditorUtility.SetDirty(material);
    }
}
