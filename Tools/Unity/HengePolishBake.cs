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

/// <summary>把用户采用的HengePolishPreview几何原样持久化；必须与采用源同次动态编译。</summary>
public static class HengePolishBake
{
    private const string Art="Assets/Game/Art/Arcade/Henge";
    private const string Prefab="Assets/Game/Prefabs/SceneObjects/ArcadeHengeVisual.prefab";
    private const string AcceptedHash="3668B137F1D0B39085146BE0A7083A8975447DC228F75F3F05D03300D2C0F2F5";
    private static string Evidence=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/HengePolishApplied-20261025"));
    private static readonly BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;

    /// <summary>只保存本项Mesh/Material/Prefab；临时additive场景与所有临时资源在finally释放。</summary>
    public static string Run()
    {
        Guard();Directory.CreateDirectory(Evidence);EnsureFolder(Art);
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var owned=new List<Object>();var reports=new List<string>{"source-sha256="+AcceptedHash};
        try
        {
            SceneManager.SetActiveScene(scene);var prefabRoot=new GameObject("ArcadeHengeVisual");
            var materials=new Dictionary<string,Material>();
            foreach(var layout in Layouts())
            {
                var variant=HengeCandidateGeometry.Build(prefabRoot.transform,layout,owned);variant.name="Round_"+layout.RoundNumber;
                var beforeRows=GeometryRows(variant.transform);string before=RowsHash(beforeRows);
                foreach(Transform semantic in variant.transform.Cast<Transform>().ToArray())Combine(semantic,layout.RoundNumber,materials,owned);
                foreach(var t in variant.GetComponentsInChildren<Transform>(true)){t.hideFlags=HideFlags.None;t.gameObject.hideFlags=HideFlags.None;t.gameObject.layer=0;}
                var afterRows=GeometryRows(variant.transform);string after=RowsHash(afterRows);
                if(before!=after)
                {
                    // 先保留真实差异证据，不能用放宽容差掩盖顶点、面色或法线变化。
                    File.WriteAllLines(Path.Combine(Evidence,layout.Id+"-source-triangles.txt"),beforeRows);
                    File.WriteAllLines(Path.Combine(Evidence,layout.Id+"-merged-triangles.txt"),afterRows);
                    var missing=beforeRows.Except(afterRows).ToArray();var added=afterRows.Except(beforeRows).ToArray();
                    var detail=new[]{"source-count="+beforeRows.Count,"merged-count="+afterRows.Count,"missing="+missing.Length,"added="+added.Length}
                        .Concat(missing.Take(20).Select(p=>"SOURCE|"+p)).Concat(added.Take(20).Select(p=>"MERGED|"+p)).ToArray();
                    File.WriteAllLines(Path.Combine(Evidence,"merge-mismatch.txt"),detail);
                    throw new InvalidOperationException("合并改变采用几何/法线/材质："+layout.Id+"；原面="+beforeRows.Count+" 合并面="+afterRows.Count+" 差异="+missing.Length+"/"+added.Length+"；详情见merge-mismatch.txt");
                }
                variant.SetActive(layout.RoundNumber==1);
                reports.Add(layout.Id+"|equivalent-triangles="+before+"|renderers="+variant.GetComponentsInChildren<Renderer>(true).Length);
            }
            // 四轮全部在内存中通过后才写持久资源，失败重烘焙不能先覆盖现有Prefab引用。
            Persist(prefabRoot.transform,owned);
            var saved=PrefabUtility.SaveAsPrefabAsset(prefabRoot,Prefab);
            if(saved==null)throw new InvalidOperationException("巨石Prefab保存失败");
            reports.Add("prefab="+Prefab);File.WriteAllLines(Path.Combine(Evidence,"bake.txt"),reports);
            return string.Join("\n",reports);
        }
        finally
        {
            if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            foreach(var item in owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);
        }
    }

    /// <summary>与采用源同机位、灯光和正式主角采样，逐轮对比采用几何与持久Prefab回读。</summary>
    public static string Capture()
    {
        Guard();Directory.CreateDirectory(Evidence);
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var owned=new List<Object>();var oldTarget=RenderTexture.active;RenderTexture target=null;Texture2D pixels=null;Camera camera=null;
        ArcadeCombatViewController markerView=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            LightAt("HengeKey",1.3f,new Vector3(48,-32,0));LightAt("HengeFill",.38f,new Vector3(38,145,0));LightAt("HengeSide",.18f,new Vector3(35,65,0));
            camera=new GameObject("HengeReadbackCamera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=27;camera.cullingMask=1<<30;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.15f,.19f);camera.farClipPlane=240;
            target=new RenderTexture(1600,1200,24);pixels=new Texture2D(1600,1200,TextureFormat.RGB24,false);camera.targetTexture=target;
            var actor=typeof(HengePolishPreview).GetMethod("CreateHero",Hidden).Invoke(null,new object[]{owned});
            var actorRoot=(GameObject)actor.GetType().GetField("Root").GetValue(actor);actorRoot.transform.SetPositionAndRotation(new Vector3(0,.025f,-4),Quaternion.Euler(0,30,0));
            var sample=typeof(HengePolishPreview).GetMethod("SampleHero",Hidden);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);if(prefab==null)throw new InvalidOperationException("先运行Run烘焙正式Prefab");
            var lines=new List<string>{"source-sha256="+AcceptedHash,"hero-local-scale="+actorRoot.transform.localScale};
            foreach(var layout in Layouts())
            {
                var accepted=HengeCandidateGeometry.Build(new GameObject("AcceptedHolder").transform,layout,owned);
                var holder=accepted.transform.parent.gameObject;
                var applied=Object.Instantiate(prefab);applied.name="PersistentReadback_"+layout.RoundNumber;
                foreach(Transform t in applied.transform)t.gameObject.SetActive(t.name=="Round_"+layout.RoundNumber);
                var appliedVariant=applied.transform.Find("Round_"+layout.RoundNumber);
                string sourceHash=GeometryHash(accepted.transform),appliedHash=GeometryHash(appliedVariant);
                if(sourceHash!=appliedHash)throw new InvalidOperationException("持久回读与采用源几何不同："+layout.Id);
                // 入口复用正式方法，两侧共享同一组标记，保证几何本体哈希不混入辅助标记。
                var markers=new GameObject("FormalEntryMarkers");markerView=new ArcadeCombatViewController();
                var instanceFlags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(ArcadeCombatViewController).GetField("root",instanceFlags).SetValue(markerView,markers);
                typeof(ArcadeCombatViewController).GetField("roundEnvironmentRoot",instanceFlags).SetValue(markerView,markers);
                typeof(ArcadeCombatViewController).GetMethod("BuildRoundEntryMarkers",instanceFlags).Invoke(markerView,new object[]{layout});
                Layer(accepted);Layer(applied);Layer(markers);sample.Invoke(null,new[]{actor});
                camera.orthographicSize=27;camera.transform.position=new Vector3(0,58,-38);camera.transform.LookAt(new Vector3(0,0,2));
                accepted.SetActive(true);applied.SetActive(false);Shot("round-"+layout.RoundNumber+"-accepted.png");
                accepted.SetActive(false);applied.SetActive(true);Shot("round-"+layout.RoundNumber+"-applied.png");
                if(layout.RoundNumber==1)
                {
                    var obstacle=layout.Obstacles[0];actorRoot.transform.position=new Vector3(obstacle.X,.025f,obstacle.Y-obstacle.HalfHeight-1.55f);
                    camera.orthographicSize=6.5f;camera.transform.position=new Vector3(obstacle.X+7,10,obstacle.Y-13);camera.transform.LookAt(new Vector3(obstacle.X,.9f,obstacle.Y));sample.Invoke(null,new[]{actor});
                    Shot("round-1-close-applied.png");accepted.SetActive(true);applied.SetActive(false);Shot("round-1-close-accepted.png");
                    actorRoot.transform.position=new Vector3(0,.025f,-4);
                }
                lines.Add(layout.Id+"|persistent-equivalence="+sourceHash);
                markerView.End();markerView=null;Object.DestroyImmediate(holder);Object.DestroyImmediate(applied);Object.DestroyImmediate(markers);
            }
            File.WriteAllLines(Path.Combine(Evidence,"readback.txt"),lines);return "4轮8张全景+2张近景，正式主角同框；采用源与持久资源逐三角形等效。";
            void Shot(string name){camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1200),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Evidence,name),pixels.EncodeToPNG());}
        }
        finally
        {
            markerView?.End();if(camera!=null)camera.targetTexture=null;RenderTexture.active=oldTarget;
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            foreach(var item in owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);
        }
    }

    /// <summary>按语义父节点、材质和阴影配置合并，保留17个独立可核验足迹。</summary>
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
                list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=RelativeMatrix(semantic,filter.transform)});
            }
        }
        var output=new List<GameObject>();
        foreach(var group in groups)
        {
            var source=settings[group.Key];string name="Round_"+round+"_"+semantic.name+"_"+group.Key;
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};owned.Add(mesh);mesh.CombineMeshes(group.Value.ToArray(),true,true);mesh.RecalculateBounds();
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

    /// <summary>直接累乘真实局部TRS，不求逆带世界平移/90度旋转的矩阵，避免无意义的末位舍入。</summary>
    private static Matrix4x4 RelativeMatrix(Transform parent,Transform child)
    {
        var matrix=Matrix4x4.identity;
        for(var current=child;current!=parent;current=current.parent)
        {
            if(current==null)throw new InvalidOperationException("合并网格不属于语义父节点");
            if(current.localPosition==Vector3.zero&&current.localRotation==Quaternion.identity&&current.localScale==Vector3.one)continue;
            matrix=Matrix4x4.TRS(current.localPosition,current.localRotation,current.localScale)*matrix;
        }
        return matrix;
    }

    /// <summary>只在四轮临时几何验证均通过后保存本项网格、共享材质，再由调用方保存Prefab。</summary>
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
    private static ArcadeRoundLayoutParameters[] Layouts()
    {
        var tables=new global::Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
        var layouts=tables.TbArcadeRoundLayout.DataList.Where(p=>p.ArenaConfigNumber==5).Select(ArcadeRoundLayoutParameters.FromConfig).OrderBy(p=>p.RoundNumber).ToArray();
        if(layouts.Length!=4||layouts.Any(p=>p.ThemeKey!="henge"||!p.FitsArena(19.5f,19.5f,.55f)))throw new InvalidOperationException("巨石真实配置不是采用的四轮");return layouts;
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
    private static void Layer(GameObject root){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;}
    private static void LightAt(string name,float intensity,Vector3 euler)
    {var light=new GameObject(name,typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=intensity;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(euler);}
    private static void Guard()
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("需要本工程空闲编辑态");
        var file=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Unity/HengePolishPreview.cs"));
        using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","")!=AcceptedHash)throw new InvalidOperationException("采用源已变化，禁止静默重设计");
    }
}
