using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using Game;
using FirepowerLoop.Arcade;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>三场已采用环境原样合并并持久化；十二轮全部通过后才写入资源，保留既有meta与历史文件。</summary>
public static class TenStageEnvironmentBake
{
    private const string Art="Assets/Game/Art/Arcade/TenStageEnvironments";
    private const string AcceptedHash="DF1DF570926D6DF510A3CD602D930A2441B34630E4C9A08F6776DAD266E51A44";
    private static string Evidence=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/TenStageEnvironmentsApplied-20261026"));
    private static string Prefab(int arena)=>"Assets/Game/Prefabs/SceneObjects/"+(arena==7?"ArcadeFortVisual":arena==10?"ArcadeKoiVisual":"ArcadeFinalNestVisual")+".prefab";

    /// <summary>采用源哈希、真实通路、障碍足迹与逐面几何均一致时，批量保存十二轮静态资源。</summary>
    public static string Run()
    {
        Guard();Directory.CreateDirectory(Evidence);
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var owned=new List<Object>();var reports=new List<string>{"source-sha256="+AcceptedHash};
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots=new Dictionary<int,GameObject>();var materials=new Dictionary<string,Material>();
            foreach(var layout in Layouts())
            {
                if(!roots.TryGetValue(layout.ArenaConfigNumber,out var root))
                {root=new GameObject(Path.GetFileNameWithoutExtension(Prefab(layout.ArenaConfigNumber)));roots.Add(layout.ArenaConfigNumber,root);}
                var variant=TenStageEnvironmentCandidates.Build(root.transform,layout,owned);variant.name="Round_"+layout.RoundNumber;
                VerifyFootprints(layout,variant,reports);
                var beforeRows=GeometryRows(variant.transform);string before=RowsHash(beforeRows);
                foreach(Transform semantic in variant.transform.Cast<Transform>().ToArray())Combine(semantic,layout.ArenaConfigNumber*10+layout.RoundNumber,materials,owned);
                foreach(var t in variant.GetComponentsInChildren<Transform>(true)){t.hideFlags=HideFlags.None;t.gameObject.hideFlags=HideFlags.None;t.gameObject.layer=0;}
                var afterRows=GeometryRows(variant.transform);string after=RowsHash(afterRows);
                if(before!=after)
                {
                    File.WriteAllLines(Path.Combine(Evidence,layout.Id+"-source-triangles.txt"),beforeRows);
                    File.WriteAllLines(Path.Combine(Evidence,layout.Id+"-merged-triangles.txt"),afterRows);
                    throw new InvalidOperationException("合并改变采用几何/法线/材质："+layout.Id);
                }
                VerifyFootprints(layout,variant,reports);
                variant.SetActive(layout.RoundNumber==1);
                reports.Add(layout.Id+"|equivalent-triangles="+before+"|renderers="+variant.GetComponentsInChildren<Renderer>(true).Length);
            }
            // 所有十二轮先完成纯内存验证，避免失败重烘焙先覆盖可用的持久资源。
            EnsureFolder(Art);
            AssetDatabase.StartAssetEditing();
            try { foreach(var root in roots.Values)Persist(root.transform,owned); }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach(var pair in roots)
            {
                var saved=PrefabUtility.SaveAsPrefabAsset(pair.Value,Prefab(pair.Key));
                if(saved==null)throw new InvalidOperationException("正式环境Prefab保存失败："+pair.Key);
                reports.Add("prefab="+Prefab(pair.Key));
            }
            File.WriteAllLines(Path.Combine(Evidence,"bake.txt"),reports);return string.Join("\n",reports.Where(p=>p.Contains("equivalent-triangles")||p.StartsWith("prefab=")));
        }
        finally
        {
            if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            foreach(var item in owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);
        }
    }
    /// <summary>按语义父节点、材质和阴影配置合并，保留每个独立可核验足迹。</summary>
    private static void Combine(Transform semantic,int round,Dictionary<string,Material> savedMaterials,List<Object> owned)
    {
        var groups=new SortedDictionary<string,List<CombineInstance>>(StringComparer.Ordinal);var settings=new Dictionary<string,MeshRenderer>();
        foreach(var filter in semantic.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null||filter.sharedMesh==null)throw new InvalidOperationException("采用网格缺失");
            for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
            {
                if(filter.sharedMesh.GetIndexCount(sub)==0)continue;
                var material=renderer.sharedMaterials[sub];string key=material.name+"_"+renderer.shadowCastingMode+"_"+renderer.receiveShadows;
                if(!groups.TryGetValue(key,out var list)){groups.Add(key,list=new List<CombineInstance>());settings.Add(key,renderer);}
                list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=filter.transform.localToWorldMatrix});
            }
        }
        // 候选根为恒等变换；直接保存原世界顶点及逆转置法线，避免CombineMeshes二次归一化。
        semantic.SetPositionAndRotation(Vector3.zero,Quaternion.identity);semantic.localScale=Vector3.one;
        var output=new List<GameObject>();
        foreach(var group in groups)
        {
            var source=settings[group.Key];string name="Round_"+round+"_"+semantic.name+"_"+group.Key;
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};owned.Add(mesh);MergeExact(mesh,group.Value);
            var material=source.sharedMaterials.First(m=>group.Key.StartsWith(m.name+"_",StringComparison.Ordinal));
            if(!savedMaterials.TryGetValue(material.name,out var savedMaterial)){savedMaterial=material;savedMaterials.Add(material.name,savedMaterial);}
            var go=new GameObject(group.Key,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(semantic,false);output.Add(go);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=savedMaterial;
            renderer.shadowCastingMode=source.shadowCastingMode;renderer.receiveShadows=source.receiveShadows;
        }
        foreach(var child in semantic.Cast<Transform>().Where(t=>!output.Contains(t.gameObject)).ToArray())Object.DestroyImmediate(child.gameObject);
        var oldRenderer=semantic.GetComponent<MeshRenderer>();if(oldRenderer!=null)Object.DestroyImmediate(oldRenderer);
        var oldFilter=semantic.GetComponent<MeshFilter>();if(oldFilter!=null)Object.DestroyImmediate(oldFilter);
    }

    /// <summary>保留源世界变换运算结果；法线不额外归一化，几何摘要沿原路径统一归一化比较。</summary>
    private static void MergeExact(Mesh output,List<CombineInstance> inputs)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var triangles=new List<int>();
        foreach(var input in inputs)
        {
            var source=input.mesh;var positions=source.vertices;var directions=source.normals;
            var normalMatrix=input.transform.inverse.transpose;int offset=vertices.Count;
            foreach(var p in positions)vertices.Add(input.transform.MultiplyPoint3x4(p));
            foreach(var n in directions)normals.Add(normalMatrix.MultiplyVector(n));
            foreach(var index in source.GetTriangles(input.subMeshIndex))triangles.Add(offset+index);
        }
        output.SetVertices(vertices);output.SetNormals(normals);output.SetTriangles(triangles,0);output.RecalculateBounds();
    }
    /// <summary>只在十二轮临时几何验证均通过后保存本项网格、共享材质，再由调用方保存Prefab。</summary>
    private static void Persist(Transform root,List<Object> owned)
    {
        var materials=new Dictionary<Material,Material>();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var source=filter.sharedMesh;var copy=Object.Instantiate(source);copy.name=source.name;owned.Add(copy);
            filter.sharedMesh=Save(copy,Art+"/"+source.name+".asset");
        }
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
            {
                if(!materials.TryGetValue(source,out var saved))
                {
                    var copy=new Material(source){name=source.name,hideFlags=HideFlags.None};owned.Add(copy);
                    saved=Save(copy,Art+"/"+source.name+".mat");materials.Add(source,saved);
                }
                return saved;
            }).ToArray();
        }
    }

    /// <summary>比较世界空间三角形、法线、面材质和阴影设置；排序消除静态合并的索引顺序差异。</summary>
    private static string GeometryHash(Transform root)=>RowsHash(GeometryRows(root));
    /// <summary>排序的原始面记录供失败时定点诊断，不改变采用几何或比较精度。</summary>
    private static List<string> GeometryRows(Transform root)
    {
        var rows=new List<string>();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();var vertices=mesh.vertices;var normals=mesh.normals;
            var matrix=filter.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var material=renderer.sharedMaterials[sub];string surface=material.name+"|"+material.color+"|"+renderer.shadowCastingMode+"|"+renderer.receiveShadows;
                var triangles=mesh.GetTriangles(sub);
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var corners=new string[3];for(int k=0;k<3;k++){int v=triangles[i+k];corners[k]=Point(matrix.MultiplyPoint3x4(vertices[v]))+"/"+Point(normalMatrix.MultiplyVector(normals[v]).normalized);}
                    // 循环起点可变，绕序不能翻转。
                    int first=string.CompareOrdinal(corners[0],corners[1])<0?0:1;if(string.CompareOrdinal(corners[2],corners[first])<0)first=2;
                    rows.Add(surface+"|"+corners[first]+"|"+corners[(first+1)%3]+"|"+corners[(first+2)%3]);
                }
            }
        }
        rows.Sort(StringComparer.Ordinal);return rows;
    }
    private static string RowsHash(List<string> rows)
    {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",rows)))).Replace("-","");}
    private static string Point(Vector3 p)=>Mathf.RoundToInt(p.x*10000)+","+Mathf.RoundToInt(p.y*10000)+","+Mathf.RoundToInt(p.z*10000);

    /// <summary>每轮逐顶点验证真实矩形足迹，外景不侵入战场；失败时拒绝继续出图。</summary>
    private static void VerifyFootprints(ArcadeRoundLayoutParameters layout, GameObject candidate, List<string> rows)
    {
        if (candidate.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("候选不能创建碰撞器");
        for (int i = 0; i < layout.Obstacles.Count; i++)
        {
            var o = layout.Obstacles[i]; var t = candidate.transform.Find("ConfiguredObstacle_" + i);
            if (t == null) throw new InvalidOperationException("候选缺少障碍" + i);
            var points = t.GetComponentsInChildren<MeshFilter>(true).SelectMany(f => f.sharedMesh.vertices.Select(v => candidate.transform.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
            float xmin = points.Min(p => p.x), xmax = points.Max(p => p.x), zmin = points.Min(p => p.z), zmax = points.Max(p => p.z);
            if (Mathf.Abs(xmin - (o.X - o.HalfWidth)) > .001f || Mathf.Abs(xmax - (o.X + o.HalfWidth)) > .001f ||
                Mathf.Abs(zmin - (o.Y - o.HalfHeight)) > .001f || Mathf.Abs(zmax - (o.Y + o.HalfHeight)) > .001f)
                throw new InvalidOperationException("障碍投影越界/缩边：" + layout.Id + "/" + i + " actual=" + xmin + "," + xmax + "," + zmin + "," + zmax);
            rows.Add(FormattableString.Invariant($"{layout.Id}|obstacle{i} exact footprint|{xmin:F3},{xmax:F3},{zmin:F3},{zmax:F3}"));
        }
        var outside = candidate.transform.Find("OutsidePlayableArea_Landmarks");
        foreach (var f in outside.GetComponentsInChildren<MeshFilter>(true)) foreach (var v in f.sharedMesh.vertices)
        {
            var p = candidate.transform.InverseTransformPoint(f.transform.TransformPoint(v));
            if (Mathf.Abs(p.x) < 19.5f && Mathf.Abs(p.z) < 19.5f) throw new InvalidOperationException("外景侵入39米场地：" + f.name);
        }
        rows.Add(layout.Id + "|outsideLandmarks|all vertices outside playable rectangle"); rows.Add(layout.Id + "|candidateColliders|0");
    }


    private static ArcadeRoundLayoutParameters[] Layouts()
    {
        var tables=new global::Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
        var layouts=tables.TbArcadeRoundLayout.DataList.Where(p=>p.ArenaConfigNumber==7||p.ArenaConfigNumber==10||p.ArenaConfigNumber==20).Select(ArcadeRoundLayoutParameters.FromConfig).OrderBy(p=>p.ArenaConfigNumber).ThenBy(p=>p.RoundNumber).ToArray();
        if(layouts.Length!=12||layouts.Any(p=>!p.FitsArena(19.5f,19.5f,.55f))||layouts.GroupBy(p=>p.ArenaConfigNumber).Any(g=>g.Count()!=4||!g.Select(p=>p.RoundNumber).SequenceEqual(new[]{1,2,3,4})))throw new InvalidOperationException("三场真实配置不是采用的十二轮");return layouts;
    }
    private static T Save<T>(T source,string path) where T:Object
    {
        source.hideFlags=HideFlags.None;var saved=AssetDatabase.LoadAssetAtPath<T>(path);
        if(saved==null){AssetDatabase.CreateAsset(source,path);return source;}
        if(saved is Mesh mesh)mesh.Clear();EditorUtility.CopySerialized(source,saved);if(saved is Mesh upload)upload.UploadMeshData(false);
        EditorUtility.SetDirty(saved);AssetDatabase.SaveAssetIfDirty(saved);return saved;
    }
    private static void EnsureFolder(string folder)
    {if(AssetDatabase.IsValidFolder(folder))return;var parent=Path.GetDirectoryName(folder).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));}
    private static void Guard()
    {
        const string expected="F:/GitHub/FirepowerLoop/Client/Assets";
        if(Application.dataPath.Replace('\\','/')!=expected||!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath,"../../Config/gen.bat")))||EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("需要本工程空闲编辑态");
        var file=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Unity/TenStageEnvironmentCandidates.cs"));
        using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","")!=AcceptedHash)throw new InvalidOperationException("采用源已变化，禁止静默重设计");
    }
}
