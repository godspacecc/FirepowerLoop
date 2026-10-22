using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>只接入用户已采用purple-weapons-v1的短尾，不修改原弹体模型和碰撞。</summary>
public static class ApplyPurpleProjectileVisuals
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态正式接入");
        const string materialPath="Assets/Game/Materials/Arcade/PurpleWeaponTail.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit");if(shader==null)throw new InvalidOperationException("缺少显式短尾Shader");
            material=new Material(shader){name="PurpleWeaponTail",color=new Color(.8f,.12f,1)};AssetDatabase.CreateAsset(material,materialPath);
        }
        foreach(string name in new[]{"GrayboxArrow","GrayboxRocket"})
        {
            string path="Assets/Game/Prefabs/Combat/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var child=root.transform.Find("PurpleWeaponTail");if(child==null){child=new GameObject("PurpleWeaponTail").transform;child.SetParent(root.transform,false);}
                var line=child.GetComponent<LineRenderer>();if(line==null)line=child.gameObject.AddComponent<LineRenderer>();
                line.sharedMaterial=material;line.useWorldSpace=true;line.positionCount=2;line.startWidth=.07f;line.endWidth=.01f;
                line.startColor=line.endColor=Color.white;line.numCapVertices=2;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.enabled=false;
                var serialized=new SerializedObject(root.GetComponent<Game.ProjectileView>());var field=serialized.FindProperty("purpleWeaponTail");
                if(field==null)throw new InvalidOperationException("ProjectileView短尾字段尚未编译");
                field.objectReferenceValue=line;serialized.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssetIfDirty(material);return "正式Arrow/Rocket已接确认短尾；未改碰撞、模型或伤害";
    }
}
