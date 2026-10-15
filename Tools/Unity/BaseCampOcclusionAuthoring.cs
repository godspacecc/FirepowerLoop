using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using Game;

/// <summary>只编辑正式基地副本与 Prefab；屋顶按原网格的连通结构提取，不修改来源 FBX 或实体碰撞。</summary>
public static class BaseCampOcclusionAuthoring
{
    private const string Root = "Assets/Game/Art/Environment/BaseCamp";
    public static string Apply()
    {
        Folder(Root + "/Meshes");
        var templatePath = Root + "/Materials/OcclusionTransparent.mat";
        var template = AssetDatabase.LoadAssetAtPath<Material>(templatePath);
        if (template == null)
        {
            template = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            template.SetFloat("_Surface", 1); template.SetFloat("_Blend", 0);
            template.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            template.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            template.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            template.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            template.SetFloat("_ZWrite", 0); template.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            template.renderQueue = (int)RenderQueue.Transparent;
            template.SetOverrideTag("RenderType", "Transparent");
            template.SetShaderPassEnabled("ShadowCaster", false);
            AssetDatabase.CreateAsset(template, templatePath);
        }
        var count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Game/Prefabs/Building"}))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!System.IO.Path.GetFileName(path).StartsWith("Camp_")) continue;
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var model = prefab.transform.Find("Model"); if(model==null)continue;
                var view = prefab.GetComponent<CameraOccluderView>() ?? prefab.AddComponent<CameraOccluderView>();
                view.TransparentMaterial = template;
                var kind = System.IO.Path.GetFileNameWithoutExtension(path).Substring(5);
                if (kind == "shed_wood_01" || kind == "awning_standing_01a")
                {
                    SplitRoof(model, kind);
                    view.InteriorCollider = prefab.GetComponent<TemporaryBuildingView>().FootprintCollider as BoxCollider;
                    view.RoofRenderers = new[]{prefab.transform.Find("Roof").GetComponent<Renderer>()};
                }
                view.BodyRenderers = model.GetComponentsInChildren<Renderer>(true);
                PrefabUtility.SaveAsPrefabAsset(prefab, path); count++;
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity", OpenSceneMode.Additive);
        var natural = 0;
        try
        {
            var decoration = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
                .First(t=>t.name=="BaseCampDecoration");
            foreach (var collider in decoration.GetComponentsInChildren<Collider>(true))
            {
                // 基地装饰碰撞仅来自自然物；初始隐藏根使 Collider.bounds 为空，不能用世界 Bounds 筛选。
                var renderers = collider.GetComponentsInChildren<Renderer>(true);
                if(renderers.Length==0 || renderers.Any(r=>r.sharedMaterials.Any(m=>m!=null && m.shader!=template.shader))) continue;
                var view = collider.GetComponent<CameraOccluderView>() ?? collider.gameObject.AddComponent<CameraOccluderView>();
                view.TransparentMaterial=template; view.BodyRenderers=renderers; natural++;
            }
            EditorSceneManager.SaveScene(scene);
        }
        finally { EditorSceneManager.CloseScene(scene,true); }
        AssetDatabase.SaveAssets();
        return "Camp prefab markers="+count+"; natural blockers="+natural;
    }
    private static void SplitRoof(Transform model, string kind)
    {
        var sourcePath=Root+"/Sources/RPGPP_LT/Models/Exterior/rpgpp_lt_"+kind+".fbx";
        var mesh=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Mesh>().First();
        var vertices=mesh.vertices; var parent=new int[vertices.Length]; var positions=new Dictionary<Vector3,int>();
        for(var i=0;i<vertices.Length;i++) {parent[i]=i;if(positions.TryGetValue(vertices[i],out var old))parent[i]=old;else positions[vertices[i]]=i;}
        var triangles=mesh.triangles;
        for(var i=0;i<triangles.Length;i+=3) {Union(parent,triangles[i],triangles[i+1]);Union(parent,triangles[i],triangles[i+2]);}
        var minimum=new Dictionary<int,float>();
        for(var i=0;i<vertices.Length;i++) {var id=Find(parent,i);var y=model.TransformPoint(vertices[i]).y;minimum[id]=minimum.TryGetValue(id,out var previous)?Mathf.Min(previous,y):y;}
        var body=new List<int>();var roof=new List<int>();
        // 以连通件最底高度区分棚顶与支柱/斜撑，保留原三角形、UV、法线和材质。
        var height=kind=="shed_wood_01"?1.9f:2f;
        for(var i=0;i<triangles.Length;i+=3) {var list=minimum[Find(parent,triangles[i])]>=height?roof:body;list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
        if(roof.Count==0 || body.Count==0)throw new Exception("Roof split failed: "+kind);
        var bodyMesh=Store(mesh,body.ToArray(),Root+"/Meshes/"+kind+"_Body.asset");
        var roofMesh=Store(mesh,roof.ToArray(),Root+"/Meshes/"+kind+"_Roof.asset");
        model.GetComponent<MeshFilter>().sharedMesh=bodyMesh;
        var roofTransform=model.parent.Find("Roof");
        if(roofTransform==null) {roofTransform=new GameObject("Roof").transform;roofTransform.SetParent(model.parent,false);roofTransform.gameObject.AddComponent<MeshFilter>();roofTransform.gameObject.AddComponent<MeshRenderer>();}
        roofTransform.localPosition=model.localPosition;roofTransform.localRotation=model.localRotation;roofTransform.localScale=model.localScale;
        roofTransform.GetComponent<MeshFilter>().sharedMesh=roofMesh;
        roofTransform.GetComponent<MeshRenderer>().sharedMaterials=model.GetComponent<MeshRenderer>().sharedMaterials;
    }
    private static Mesh Store(Mesh original,int[] triangles,string path)
    {
        var result=UnityEngine.Object.Instantiate(original);result.name=System.IO.Path.GetFileNameWithoutExtension(path);result.triangles=triangles;
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(result,path);return result;}
        EditorUtility.CopySerialized(result,existing);UnityEngine.Object.DestroyImmediate(result);EditorUtility.SetDirty(existing);return existing;
    }
    private static int Find(int[] parent,int id){while(parent[id]!=id){parent[id]=parent[parent[id]];id=parent[id];}return id;}
    private static void Union(int[] parent,int a,int b)=>parent[Find(parent,a)]=Find(parent,b);
    private static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));}
}