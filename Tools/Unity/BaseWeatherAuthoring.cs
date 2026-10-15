using System;
using System.IO;
using System.Linq;
using Game;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>只装配正式基地天气样板与必要资源副本；不改备用包、Main 场景或游戏存档。</summary>
public static class BaseWeatherAuthoring
{
    private const string Output="Assets/Game/Art/Environment/BaseWeather";
    public static string Run()
    {
        Directory.CreateDirectory(Output);AssetDatabase.Refresh();
        Copy("Assets/Art/ARPG Effects/Textures/rain.png","Rain.png");
        Copy("Assets/Art/ARPG Effects/Textures/rain_impact.png","RainImpact.png");
        Copy("Assets/Art/ARPG Effects/Sound/rain_loop.wav","RainLoop.wav");
        var rain=Material("Rain.mat","Rain.png");var splash=Material("RainSplash.mat","RainImpact.png");
        const string path="Assets/Game/Scenes/BaseMap.unity";
        var old=SceneManager.GetActiveScene();var scene=SceneManager.GetSceneByPath(path);
        var opened=!scene.IsValid()||!scene.isLoaded;
        if(!opened && scene.isDirty)throw new InvalidOperationException("基地场景存在未保存修改，拒绝覆盖");
        if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            var context=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MapContext>(true)).First();
            var root=context.MapContentRoot;var view=root.GetComponent<BaseWeatherView>()??root.AddComponent<BaseWeatherView>();
            var surfaces=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.sharedMaterials.Any(m=>m && (m.name=="CampGround"||m.name=="CampDirt"||m.name=="CampGravel"))).ToArray();
            if(surfaces.Length!=6)throw new InvalidOperationException("基地地面清单变化，请重新核对："+surfaces.Length);
            var serialized=new SerializedObject(view);
            SetArray(serialized,"wetSurfaces",surfaces);SetArray(serialized,"rainBlockers",root.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger).ToArray());
            SetArray(serialized,"rainRoofs",root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Roof").ToArray());
            serialized.FindProperty("rainMaterial").objectReferenceValue=rain;
            serialized.FindProperty("splashMaterial").objectReferenceValue=splash;
            serialized.FindProperty("rainClip").objectReferenceValue=AssetDatabase.LoadAssetAtPath<AudioClip>(Output+"/RainLoop.wav");
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();return "基地天气已装配：6个地面、"+serialized.FindProperty("rainBlockers").arraySize+"个非触发碰撞遮挡";
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(old.IsValid()&&old.isLoaded)SceneManager.SetActiveScene(old);}
    }
    private static void Copy(string source,string destination)
    {
        if(!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Output+"/"+destination) && !AssetDatabase.CopyAsset(source,Output+"/"+destination))throw new Exception("资源复制失败："+source);
    }
    private static Material Material(string name,string texture)
    {
        var path=Output+"/"+name;var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));AssetDatabase.CreateAsset(material,path);}
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Output+"/"+texture));material.SetColor("_BaseColor",Color.white);
        material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_SrcBlend",5);material.SetFloat("_DstBlend",10);material.SetFloat("_ZWrite",0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;EditorUtility.SetDirty(material);return material;
    }
    private static void SetArray(SerializedObject serialized,string field,UnityEngine.Object[] values)
    {
        var array=serialized.FindProperty(field);array.arraySize=values.Length;
        for(var i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
    }
}
