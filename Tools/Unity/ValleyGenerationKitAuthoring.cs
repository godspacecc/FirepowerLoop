using System;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>从现有正式河谷导出独立生成模板，不修改原场景或第三方资源。</summary>
public static class ValleyGenerationKitAuthoring
{
    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("仅允许编辑模式制作模板");
        var scene=SceneManager.GetSceneByPath("Assets/Game/Scenes/CivilisationValleyMap.unity");
        if(!scene.IsValid()||!scene.isLoaded||scene.isDirty)throw new InvalidOperationException("需要已保存的河谷场景");
        var context=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MapContext>(true)).Single();
        var views=context.GetComponentsInChildren<CivilisationPointView>(true);
        var root=new GameObject("CivilisationValleyGenerationKit");root.SetActive(false);
        try
        {
            var kit=root.AddComponent<ValleyGenerationKit>();
            var data=new SerializedObject(kit);
            data.FindProperty("terrainMaterial").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Environment/CivilisationExpedition/River/RiverBank.mat");
            data.FindProperty("waterMaterial").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Environment/CivilisationExpedition/River/RiverWater.mat");
            data.FindProperty("campfirePrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/Camp_RPGFXCampfire.prefab");
            data.FindProperty("awningPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/Camp_awning_standing_01a.prefab");
            CivilisationPointView Copy(CivilisationPointView original)
            {
                var clone=Object.Instantiate(original.gameObject,root.transform);clone.name=original.PointId.Length>0?original.PointId:original.Kind.ToString();clone.SetActive(false);clone.transform.localPosition=Vector3.zero;
                var view=clone.GetComponent<CivilisationPointView>();var source=new SerializedObject(original);var target=new SerializedObject(view);
                var visual=(GameObject)source.FindProperty("harvestedVisualRoot").objectReferenceValue;
                if(visual==null && original.PointId=="camp.fire")visual=context.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="OldHearth").gameObject;
                if(visual==null && original.PointId=="camp.food")visual=context.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="FoodBasket").gameObject;
                if(visual==null && original.PointId=="camp.wood")visual=context.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="WoodPile").gameObject;
                if(visual==null && (original.Kind==CivilisationPointKind.Preparation||original.Kind==CivilisationPointKind.Return))
                    visual=context.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="PreparationSack").gameObject;
                if(visual!=null&&!visual.transform.IsChildOf(original.transform))
                {
                    var copied=Object.Instantiate(visual,clone.transform);copied.transform.localPosition=Vector3.zero;copied.transform.localRotation=Quaternion.identity;
                    target.FindProperty("harvestedVisualRoot").objectReferenceValue=copied;target.ApplyModifiedPropertiesWithoutUndo();
                }
                return view;
            }
            var ids=new[]{"camp.fire","camp.food","camp.wood","camp.axe","camp.pickaxe","camp.cloth","tree.valley.5","rock.valley.1","water.valley.ford"};
            var templates=data.FindProperty("searchTemplates");templates.arraySize=ids.Length;
            for(int i=0;i<ids.Length;i++)templates.GetArrayElementAtIndex(i).objectReferenceValue=Copy(views.Single(v=>v.PointId==ids[i]));
            data.FindProperty("preparationTemplate").objectReferenceValue=Copy(views.Single(v=>v.Kind==CivilisationPointKind.Preparation));
            data.FindProperty("returnTemplate").objectReferenceValue=Copy(views.Single(v=>v.Kind==CivilisationPointKind.Return));
            data.ApplyModifiedPropertiesWithoutUndo();
            if(!kit.Validate(out var error))throw new InvalidOperationException(error);
            const string folder="Assets/Game/Prefabs/Environment";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Game/Prefabs","Environment");
            var saved=PrefabUtility.SaveAsPrefabAsset(root,folder+"/CivilisationValleyGenerationKit.prefab");
            if(saved==null)throw new InvalidOperationException("模板保存失败");AssetDatabase.SaveAssets();return "已制作9种交互模板、准备与返程入口及河谷正式环境引用，原场景未修改";
        }
        finally{Object.DestroyImmediate(root);}
    }
}
