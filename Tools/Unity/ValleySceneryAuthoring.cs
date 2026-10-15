using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>将验收样片实际所需的资源导出为正式布景，绑定随机河谷Kit；不修改备用包、样片或地图存档。</summary>
public static class ValleySceneryAuthoring
{
    private const string Base="Assets/Game/Art/Environment/CivilisationExpedition/";
    private const string Art=Base+"Scenery";
    private static void Folder(string p){if(AssetDatabase.IsValidFolder(p))return;int i=p.LastIndexOf('/');Folder(p.Substring(0,i));AssetDatabase.CreateFolder(p.Substring(0,i),p.Substring(i+1));}
    private static T Copy<T>(string path,string name)where T:Object
    {
        var target=Art+"/"+name;var old=AssetDatabase.LoadAssetAtPath<T>(target);if(old!=null)return old;
        if(!AssetDatabase.CopyAsset(path,target))throw new InvalidOperationException("资源复制失败："+path);return AssetDatabase.LoadAssetAtPath<T>(target);
    }
    private static Material Material(string name)=>Copy<Material>(Base+"ArtSample/"+name+".mat",name+".mat");
    private static GameObject MeshRoot(string name,Mesh mesh,Material[] materials)
    {
        var root=new GameObject(name);root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterials=materials;return root;
    }
    private static GameObject Save(GameObject root,string name)
    {try{return PrefabUtility.SaveAsPrefabAsset(root,Art+"/"+name+".prefab");}finally{Object.DestroyImmediate(root);}}
    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("仅允许编辑模式");Folder(Art);
        var originalRock=AssetDatabase.LoadAssetAtPath<GameObject>(Base+"Sources/SimpleNaturePack/Prefabs/Rock_01.prefab").GetComponentInChildren<MeshFilter>().sharedMesh;
        var rockMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/RockVisual.asset");if(rockMesh==null){rockMesh=Object.Instantiate(originalRock);rockMesh.name="RockVisual";AssetDatabase.CreateAsset(rockMesh,Art+"/RockVisual.asset");}
        // 原FBX关闭读写，导出原生Mesh仍会继承此标记；只对正式合批副本开启CPU顶点读取。
        var rockData=new SerializedObject(rockMesh);rockData.FindProperty("m_IsReadable").boolValue=true;rockData.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(rockMesh);
        var rock=Save(MeshRoot("RiverRock",rockMesh,new[]{Material("LandSlate")}),"RiverRock");
        var pine=Save(MeshRoot("PineVisual",Copy<Mesh>(Base+"ArtSample/PineVisual.asset","PineVisual.asset"),new[]{AssetDatabase.LoadAssetAtPath<Material>(Base+"Sources/RPGPP_LT/Materials/rpgpp_lt_mat_a.mat")}),"PineVisual");
        var vertices=new List<Vector3>();var triangles=new[]{new List<int>(),new List<int>(),new List<int>()};var random=new System.Random(61011);
        float R(float a,float b)=>a+(b-a)*(float)random.NextDouble();
        void Tri(int group,Vector3 a,Vector3 b,Vector3 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles[group].AddRange(new[]{n,n+1,n+2});}
        for(int leaf=0;leaf<30;leaf++)
        {
            float angle=R(0,Mathf.PI*2),radius=R(0,.65f),h=R(.24f,.6f);var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var p=direction*radius;var side=Vector3.Cross(Vector3.up,direction)*R(.055f,.12f);var knee=p+Vector3.up*h*.55f+direction*h*.2f;var tip=p+Vector3.up*h+direction*h*.55f;
            Tri(leaf%3,p-side,p+side,knee+side*.5f);Tri(leaf%3,p-side,knee+side*.5f,knee-side*.5f);Tri(leaf%3,knee-side*.5f,knee+side*.5f,tip);
        }
        var grassMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/GrassClump.asset");
        if(grassMesh==null){grassMesh=new Mesh{name="GrassClump"};AssetDatabase.CreateAsset(grassMesh,Art+"/GrassClump.asset");}
        grassMesh.Clear();grassMesh.SetVertices(vertices);grassMesh.subMeshCount=3;for(int i=0;i<3;i++)grassMesh.SetTriangles(triangles[i],i);grassMesh.RecalculateNormals();grassMesh.normals=grassMesh.normals.Select(n=>(Vector3.up*.85f+n*.15f).normalized).ToArray();grassMesh.RecalculateBounds();EditorUtility.SetDirty(grassMesh);
        var grass=Save(MeshRoot("GrassClump",grassMesh,new[]{Material("GrassDeep"),Material("GrassFresh"),Material("GrassWarm")}),"GrassClump");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Base+"ArtSample/BranchAndHideShelter.prefab");var shelter=new GameObject("CampHideShelter");var visual=Object.Instantiate(source,shelter.transform);visual.transform.localPosition=new Vector3(19,-1.35f,0);
        foreach(var filter in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            var path=AssetDatabase.GetAssetPath(filter.sharedMesh);if(path.Contains("/ArtSample/"))filter.sharedMesh=Copy<Mesh>(path,filter.sharedMesh.name+".asset");
        }
        foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Material(m.name)).ToArray();
        var camp=Save(shelter,"CampHideShelter");
        var ridge=Save(MeshRoot("DistantRidge",Copy<Mesh>(Base+"ArtSample/Ridge_4.asset","DistantRidge.asset"),new[]{Material("DistantSlate"),Material("MountainPale")}),"DistantRidge");
        foreach(var item in new[]{rock,grass,camp,pine,ridge})
        {
            var dependencies=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(item),true);if(dependencies.Any(p=>p.StartsWith("Assets/Art/")||p.Contains("/ArtSample/")))throw new InvalidOperationException("布景存在样片或备用库依赖："+item.name);
        }
        var root=PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab");
        try
        {
            var data=new SerializedObject(root.GetComponent<ValleyGenerationKit>());
            void Set(string key,Object value){data.FindProperty(key).objectReferenceValue=value;}
            Set("sceneryRockPrefab",rock);Set("sceneryGrassPrefab",grass);Set("sceneryShelterPrefab",camp);Set("coniferVisualPrefab",pine);Set("ridgeVisualPrefab",ridge);
            Set("terrainMaterial",Copy<Material>(Base+"ArtSample/Meadow.mat","Meadow.mat"));
            Set("waterMaterial",Copy<Material>(Base+"ArtSample/SampleWater.mat","ValleyWater.mat"));
            data.ApplyModifiedPropertiesWithoutUndo();if(!root.GetComponent<ValleyGenerationKit>().Validate(out var error))throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAsset(root,"Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        // 只保存本次创建的资源，避免提交其他会话的编辑器脏资产。
        foreach(var guid in AssetDatabase.FindAssets("",new[]{Art}))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        return "正式布景已导出、Kit已绑定，无备用库及样片依赖";
    }
}
