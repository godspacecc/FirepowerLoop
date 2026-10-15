using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Game;

/// <summary>将已验证的同骨架衣裤绑定到正式玩家；保留动画与武器挂点，不创建运行时库存。</summary>
public static class ClothingRuntimeAuthoring
{
    const string Folder="Assets/Game/Art/Player/OutfitPrototype";
    static void SaveMesh(Mesh mesh,string path)
    {
        var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old!=null){old.Clear(false);EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);}
        else AssetDatabase.CreateAsset(mesh,path);
    }
    /// <summary>提取手和袖子的实际三角形，保留对应原骨架权重；不把头和躯干显示在第一人称。</summary>
    static Mesh Arms(Mesh body,Mesh top,string name)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var weights=new List<BoneWeight>();var triangles=new List<int>();
        foreach(var mesh in new[]{body,top})
        {
            var v=mesh.vertices;var n=mesh.normals;var u=mesh.uv;var b=mesh.boneWeights;var tr=mesh.triangles;var map=new Dictionary<int,int>();
            for(int i=0;i<tr.Length;i+=3)
            {
                var center=(v[tr[i]]+v[tr[i+1]]+v[tr[i+2]])/3;
                if(Mathf.Abs(center.x)<.24f||center.y<1.1f)continue;
                for(int j=0;j<3;j++)
                {int k=tr[i+j];if(!map.TryGetValue(k,out int ix)){ix=vertices.Count;map.Add(k,ix);vertices.Add(v[k]);normals.Add(n[k]);uv.Add(u[k]);weights.Add(b[k]);}triangles.Add(ix);}
            }
        }
        var result=new Mesh{name=name};result.SetVertices(vertices);result.SetNormals(normals);result.SetUVs(0,uv);result.boneWeights=weights.ToArray();result.bindposes=body.bindposes;result.SetTriangles(triangles,0);result.RecalculateBounds();return result;
    }
    public static string Run()
    {
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/PlayerModularOutfit.fbx");
        var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/Humanoid/PlayerCityCharacters.fbx");
        var template=original.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name.Contains("Hoodie"));
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var src=renderer.sharedMesh;var mesh=new Mesh{name=renderer.name};
            mesh.vertices=src.vertices;mesh.normals=src.normals;mesh.uv=src.uv;mesh.boneWeights=src.boneWeights;mesh.triangles=src.triangles;
            mesh.bindposes=renderer.bones.Select(b=>template.sharedMesh.bindposes[Array.FindIndex(template.bones,t=>t.name==b.name.Replace(".001"," 1"))]).ToArray();mesh.RecalculateBounds();SaveMesh(mesh,Folder+"/"+renderer.name+".asset");
        }
        var body=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Base_Hands.asset");
        foreach(var top in new[]{"Base_Torso","Top_RedHoodie","Top_OliveUtility"})
        {
            SaveMesh(Arms(body,AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+top+".asset"),"Sleeves_"+top),Folder+"/Sleeves_"+top+".asset");
            SaveMesh(Arms(AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Hands_WorkGloves.asset"),AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+top+".asset"),"GloveSleeves_"+top),Folder+"/GloveSleeves_"+top+".asset");
        }
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Player/Humanoid/PlayerCity.mat");
        var arms=UnityEngine.Object.Instantiate(original);
        try
        {
            var names=arms.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
            foreach(var r in arms.GetComponentsInChildren<SkinnedMeshRenderer>(true))UnityEngine.Object.DestroyImmediate(r.gameObject);
            var obj=new GameObject("Arms");obj.transform.SetParent(arms.transform,false);var skin=obj.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Sleeves_Base_Torso.asset");skin.sharedMaterial=material;
            skin.bones=template.bones.Select(t=>names[t.name]).ToArray();skin.rootBone=names["Root"];skin.updateWhenOffscreen=true;
            PrefabUtility.SaveAsPrefabAsset(arms,Folder+"/ClothingFirstPersonArms.prefab");
        }
        finally{UnityEngine.Object.DestroyImmediate(arms);}
        const string playerPath="Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab";
        var root=PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            var view=root.GetComponent<PlayerView>();var animator=view.Animator;var names=animator.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
            foreach(var r in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.StartsWith("Character_")))r.gameObject.SetActive(false);
            var previous=animator.transform.Find("ClothingParts");if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var holder=new GameObject("ClothingParts");holder.transform.SetParent(animator.transform,false);
            var parts=new Dictionary<string,GameObject>();
            foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var obj=new GameObject(renderer.name);obj.transform.SetParent(holder.transform,false);var skin=obj.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+renderer.name+".asset");skin.sharedMaterial=material;
                skin.bones=renderer.bones.Select(b=>names[b.name.Replace(".001"," 1")]).ToArray();skin.rootBone=names["Root"];skin.updateWhenOffscreen=true;
                parts.Add(obj.name,obj);
            }
            var appearance=root.GetComponent<ClothingAppearanceView>()??root.AddComponent<ClothingAppearanceView>();var so=new SerializedObject(appearance);
            so.FindProperty("baseTorso").objectReferenceValue=parts["Base_Torso"];so.FindProperty("baseLegs").objectReferenceValue=parts["Base_Legs"];
            so.FindProperty("baseHands").objectReferenceValue=parts["Base_Hands"];so.FindProperty("baseFeet").objectReferenceValue=parts["Base_Feet"];
            so.FindProperty("baseHeadCover").objectReferenceValue=parts["Base_HeadCover"];
            so.FindProperty("baseSleeves").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Sleeves_Base_Torso.asset");
            foreach(var pair in new[]{("tops",new[]{"Top_RedHoodie","Top_OliveUtility"}),("pants",new[]{"Pants_BlueDenim","Pants_TanCargo"})})
            {
                var array=so.FindProperty(pair.Item1);array.arraySize=2;
                for(int i=0;i<2;i++){var e=array.GetArrayElementAtIndex(i);e.FindPropertyRelative("AppearanceId").stringValue=pair.Item2[i];e.FindPropertyRelative("Root").objectReferenceValue=parts[pair.Item2[i]];e.FindPropertyRelative("SleeveMesh").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Sleeves_"+pair.Item2[i]+".asset");}
            }
            foreach(var pair in new[]{("heads","Head_OliveCap"),("hands","Hands_WorkGloves"),("feet","Feet_BrownBoots"),("backs","Back_CanvasPack"),("armors","Armor_UtilityVest")})
            {
                var array=so.FindProperty(pair.Item1);array.arraySize=1;var e=array.GetArrayElementAtIndex(0);
                e.FindPropertyRelative("AppearanceId").stringValue=pair.Item2;e.FindPropertyRelative("Root").objectReferenceValue=parts[pair.Item2];
            }
            var variants=so.FindProperty("gloveSleeves");variants.arraySize=3;
            var topNames=new[]{"Base_Torso","Top_RedHoodie","Top_OliveUtility"};
            for(int i=0;i<topNames.Length;i++)
            {
                var e=variants.GetArrayElementAtIndex(i);e.FindPropertyRelative("TorsoId").stringValue=i==0?"":topNames[i];
                e.FindPropertyRelative("HandsId").stringValue="Hands_WorkGloves";
                e.FindPropertyRelative("Mesh").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/GloveSleeves_"+topNames[i]+".asset");
            }
            so.ApplyModifiedPropertiesWithoutUndo();appearance.Apply("","");
            var player=new SerializedObject(view);player.FindProperty("clothingAppearance").objectReferenceValue=appearance;
            player.FindProperty("firstPersonArmsPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/ClothingFirstPersonArms.prefab");player.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,playerPath);
            var display=UnityEngine.Object.Instantiate(root);
            try
            {
                display.name="ClothingCharacterPreview";
                var a=display.GetComponent<PlayerView>().Animator;
                foreach(var child in display.transform.Cast<Transform>().ToArray())
                    if(!a.transform.IsChildOf(child))UnityEngine.Object.DestroyImmediate(child.gameObject);
                foreach(var component in display.GetComponentsInChildren<MonoBehaviour>(true))
                    if(!(component is ClothingAppearanceView))UnityEngine.Object.DestroyImmediate(component);
                foreach(var c in display.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(c);
                foreach(var r in display.GetComponentsInChildren<Renderer>(true))
                    if(!r.transform.IsChildOf(a.transform.Find("ClothingParts")))UnityEngine.Object.DestroyImmediate(r);
                PrefabUtility.SaveAsPrefabAsset(display,Folder+"/ClothingCharacterPreview.prefab");
            }
            finally{UnityEngine.Object.DestroyImmediate(display);}
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();return "已绑定七部位正式玩家与第一人称手套袖口";
    }
}
