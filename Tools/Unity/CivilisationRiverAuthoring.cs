using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Pinwheel.Poseidon;
using Object = UnityEngine.Object;

/// <summary>制作河谷的简化河流样片并烘焙静态资源；不改变交互、奖励或原始资源包。</summary>
public static class CivilisationRiverAuthoring
{
    private const string ScenePath = "Assets/Game/Scenes/CivilisationValleyMap.unity";
    private const string Art = "Assets/Game/Art/Environment/CivilisationExpedition/River";
    private const string SourceShaders = "Assets/Art/Polaris - Low Poly Ecosystem/Poseidon - Low Poly Water System/Runtime/Shaders";
    // 米；与样条锚点共享河道中心，浅滩保留在原土路的 x=0 附近。
    private static float RiverZ(float x) => -2 + 3 * Mathf.Sin(x * .13f);
    private static float Height(float x, float z)
    {
        float d = Mathf.Abs(z - RiverZ(x));
        float bed = Mathf.Lerp(-.34f, -.85f, Mathf.SmoothStep(0, 1, (Mathf.Abs(x) - 2.2f) / 1.3f));
        if (d < 2.6f) return bed;
        if (d < 5) return Mathf.Lerp(bed, 0, (d - 2.6f) / 2.4f);
        // 外围低坡形成谷地轮廓，营地和原交互位置保持平整。
        return Mathf.SmoothStep(0, 2.4f, Mathf.Clamp01((Mathf.Abs(z) - 22) / 8));
    }
    private static void Folder(string p)
    {
        if (AssetDatabase.IsValidFolder(p)) return;
        int i = p.LastIndexOf('/'); Folder(p.Substring(0, i));
        AssetDatabase.CreateFolder(p.Substring(0, i), p.Substring(i + 1));
    }
    private static T Save<T>(T asset, string name) where T : Object
    {
        string p = Art + "/" + name;
        var old = AssetDatabase.LoadAssetAtPath<T>(p);
        if (old != null)
        {
            var sourceMesh = asset as Mesh; var targetMesh = old as Mesh;
            if (sourceMesh != null && targetMesh != null)
            {
                // 显式刷新网格缓冲；CopySerialized 会出现 CPU 数据更新而预览仍绘制旧缓冲的情况。
                targetMesh.Clear(); targetMesh.indexFormat = sourceMesh.indexFormat;
                targetMesh.vertices = sourceMesh.vertices; targetMesh.normals = sourceMesh.normals;
                targetMesh.colors = sourceMesh.colors;
                for (int channel=0; channel<2; channel++)
                {
                    var uv = new List<Vector4>(); sourceMesh.GetUVs(channel, uv); targetMesh.SetUVs(channel, uv);
                }
                targetMesh.subMeshCount = sourceMesh.subMeshCount;
                for (int i=0; i<sourceMesh.subMeshCount; i++) targetMesh.SetTriangles(sourceMesh.GetTriangles(i), i);
                targetMesh.bounds = sourceMesh.bounds; targetMesh.UploadMeshData(false);
            }
            else EditorUtility.CopySerialized(asset, old);
            EditorUtility.SetDirty(old); Object.DestroyImmediate(asset); return old;
        }
        AssetDatabase.CreateAsset(asset, p); return asset;
    }
    /// <summary>复制 Shader 的实际 include 闭包，正式材质不引用备用目录。</summary>
    private static void CopyShader(string relative, HashSet<string> copied)
    {
        relative = relative.Replace('\\', '/');
        if (!copied.Add(relative)) return;
        string source = SourceShaders + "/" + relative;
        string destination = Art + "/Shaders/" + relative;
        Folder(Path.GetDirectoryName(destination).Replace('\\', '/'));
        if (AssetDatabase.LoadMainAssetAtPath(destination) == null && !File.Exists(destination))
            if (!AssetDatabase.CopyAsset(source, destination)) throw new InvalidOperationException("复制失败：" + source);
        string sourceText = File.ReadAllText(source);
        foreach (Match match in Regex.Matches(sourceText, "#include\\s+\"([^\"]+)\""))
        {
            string include = match.Groups[1].Value;
            if (include.StartsWith("Packages/")) continue;
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), include));
            string sourceRoot = Path.GetFullPath(SourceShaders) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("include 越界");
            CopyShader(full.Substring(sourceRoot.Length).Replace('\\', '/'), copied);
        }
        if (relative.EndsWith(".shader"))
            File.WriteAllText(destination, sourceText.Replace("Shader \"Poseidon/URP/RiverURP\"", "Shader \"AnotherDay/River/PoseidonURP\""));
        if (relative == "CGIncludes/PRipple.cginc")
            // 静态烘焙后无需 PWater 运行脚本，用 Unity 内置时间推进正式副本的水纹。
            File.WriteAllText(destination, sourceText.Replace("_PoseidonTime", "_Time.y"));
    }
    private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material, bool collider)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
    private static Mesh GroundMesh()
    {
        const int n = 100;
        var vertices = new Vector3[(n + 1) * (n + 1)];
        var colors = new Color[vertices.Length]; var indices = new List<int>(); var bankIndices = new List<int>();
        for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++)
        {
            float x = -30 + i * .6f, z = -30 + j * .6f;
            int k = j * (n + 1) + i; vertices[k] = new Vector3(x, Height(x, z), z);
            float d = Mathf.Abs(z - RiverZ(x));
            colors[k] = d < 4.5f ? new Color(.49f,.43f,.30f) : new Color(.30f,.38f,.24f);
            if (i < n && j < n)
            {
                // 分开序列化岸边泥沙与草地，避免依赖 Lit 不读取的顶点颜色。
                var destination = Mathf.Abs(z + .3f - RiverZ(x + .3f)) < 4.8f ? bankIndices : indices;
                destination.AddRange(new[] { k, k+n+1, k+1, k+1, k+n+1, k+n+2 });
            }
        }
        var mesh = new Mesh { name = "河谷河床与低坡" }; mesh.vertices = vertices; mesh.colors = colors;
        mesh.subMeshCount = 2; mesh.SetTriangles(indices, 0); mesh.SetTriangles(bankIndices, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    /// <summary>重建工具拥有的外围边界；四墙闭合并深入地面，不依赖导航约束人物移动。</summary>
    private static void CreateBoundary(Transform riverRoot)
    {
        var previous = riverRoot.Find("ValleyBoundary");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        var boundary = new GameObject("ValleyBoundary"); boundary.transform.SetParent(riverRoot, false);
        // 米；内侧为 ±29，低于地面 -2 到高于边坡 10，转角重叠防止斜向漏出。
        var centers = new[] {new Vector3(-29.5f,4,0), new Vector3(29.5f,4,0), new Vector3(0,4,-29.5f), new Vector3(0,4,29.5f)};
        var names = new[] {"WestWall", "EastWall", "SouthWall", "NorthWall"};
        for (int i = 0; i < centers.Length; i++)
        {
            var wall = new GameObject(names[i]); wall.transform.SetParent(boundary.transform, false);
            wall.transform.localPosition = centers[i]; wall.isStatic = true;
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = i < 2 ? new Vector3(1,12,61) : new Vector3(61,12,1);
        }
        var rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Environment/CivilisationExpedition/Sources/SimpleNaturePack/Prefabs/Rock_01.prefab");
        for (int side = 0; side < 4; side++) for (int i = 0; i < 19; i++)
        {
            float along = -27 + i * 3;
            float x = side < 2 ? (side == 0 ? -29.5f : 29.5f) : along;
            float z = side < 2 ? along : (side == 2 ? -29.5f : 29.5f);
            // 河口两端仍呈现水流延伸；实体墙独立保持闭合。
            if (Mathf.Abs(z - RiverZ(x)) < 4.5f) continue;
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab, boundary.transform);
            rock.name = "BoundaryRock_" + side + "_" + i;
            rock.transform.localPosition = new Vector3(x,Height(x,z),z);
            rock.transform.localRotation = Quaternion.Euler(0, i * 47 + side * 31, 0);
            rock.transform.localScale = new Vector3(4, 5 + i % 3, 4);
            // 装饰不增加导航碎片或额外碰撞成本，阻挡权威统一交给四面墙。
            foreach (var collider in rock.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            rock.isStatic = true;
        }
    }

    /// <summary>仅补齐已制作河谷的边界并更新导航；重复调用替换同名根，不重建河流资源。</summary>
    public static string ApplyBoundary()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请保持编辑模式");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("河谷有未保存修改，停止补齐边界");
        var content = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "MapContentRoot");
        var riverRoot = content.Find("RiverPreview");
        if (riverRoot == null) throw new InvalidOperationException("缺少河流根，停止补齐边界");
        CreateBoundary(riverRoot); BakeNavigation(content); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败");
        return "河谷四面实体边界及外围岩石已保存，导航已更新。";
    }

    /// <summary>保存到正式场景；仅重建本工具拥有的河流根，保留五个交互点与营地。</summary>
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请保持编辑模式");
        Folder(Art); CopyShader("UniversalRP/RiverURP.shader", new HashSet<string>()); AssetDatabase.Refresh();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Art + "/Shaders/UniversalRP/RiverURP.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("水面 Shader 编译失败");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("河谷有未保存修改，停止重建");
        SceneManager.SetActiveScene(scene);
        var content = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "MapContentRoot");
        var previous = content.Find("RiverPreview"); if (previous != null) Object.DestroyImmediate(previous.gameObject);
        var root = new GameObject("RiverPreview"); root.transform.SetParent(content, false);
        var ground = content.Find("ValleyGround"); ground.gameObject.SetActive(false);
        var terrainMat = Save(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name="河岸土色" }, "RiverBank.mat");
        terrainMat.SetColor("_BaseColor", new Color(.34f,.40f,.26f)); terrainMat.SetFloat("_Smoothness", 0);
        var sandMat = Save(new Material(Shader.Find("Universal Render Pipeline/Lit")) {name="河岸泥沙"}, "RiverSand.mat");
        sandMat.SetColor("_BaseColor", new Color(.49f,.44f,.33f)); sandMat.SetFloat("_Smoothness",0);
        MeshObject(root.transform, "RiverbedAndValley", Save(GroundMesh(), "Riverbed.asset"), terrainMat, true)
            .GetComponent<Renderer>().sharedMaterials = new[] {terrainMat,sandMat};
        var profile = ScriptableObject.CreateInstance<PWaterProfile>();
        profile.CopyFrom(AssetDatabase.LoadAssetAtPath<PWaterProfile>("Assets/Art/Polaris - Low Poly Ecosystem/Poseidon - Low Poly Water System/Runtime/Default Profiles/CalmWater.asset"));
        profile.Color = new Color(.27f,.63f,.67f,.84f); profile.SpecColor = new Color(.14f,.19f,.18f);
        profile.EnableReflection = false; profile.EnableRefraction = false; profile.EnableCaustic = false;
        profile.EnableLightAbsorption = false; profile.EnableFoam = false; profile.EnableWave = false;
        // 禁用特效也要清除源贴图引用，保证正式资产依赖闭包不回到备用包。
        // Poseidon 的属性 setter 不接受 null，使用内置黑贴图移除原包依赖。
        profile.CausticTexture = Texture2D.blackTexture;
        profile.RippleHeight = .065f; profile.RippleSpeed = 1.2f; profile.RippleNoiseScale = 25; profile.Smoothness = .35f;
        var mat = new Material(shader) {name="Poseidon 简化河流"}; profile.UpdateMaterialProperties(mat);
        mat = Save(mat, "RiverWater.mat");
        profile.Color = new Color(.48f,.73f,.66f,.40f);
        var shallowMat = new Material(shader) {name="浅滩水面"}; profile.UpdateMaterialProperties(shallowMat);
        shallowMat = Save(shallowMat,"ShallowWater.mat"); Object.DestroyImmediate(profile);
        var temp = new GameObject("PoseidonBakeTemporary"); temp.SetActive(false);
        try
        {
            var water = temp.AddComponent<PWater>(); water.MeshType = PWaterMeshType.Spline;
            water.SplineWidth = 5.2f; water.SplineResolutionX = 2; water.SplineResolutionY = 24;
            float[] xs = {-30,-16,-2.2f,2.2f,16,30};
            foreach (float x in xs)
            {
                var direction = new Vector3(1,0,.39f*Mathf.Cos(x*.13f));
                water.Spline.Anchors.Add(new PSplineAnchor(new Vector3(x,-.28f,RiverZ(x))) {Rotation=Quaternion.LookRotation(direction), Scale=Vector3.one});
            }
            for (int i=0;i<xs.Length-1;i++)
            {
                var a=water.Spline.Anchors[i]; var b=water.Spline.Anchors[i+1]; float len=(xs[i+1]-xs[i])/3;
                water.Spline.Segments.Add(new PSplineSegment {StartIndex=i,EndIndex=i+1,StartTangent=a.Position+a.Rotation*Vector3.forward*len,EndTangent=b.Position-b.Rotation*Vector3.forward*len});
            }
            water.GenerateSplineMesh();
            for(int i=0;i<water.Spline.Segments.Count;i++)
                MeshObject(root.transform,"PoseidonRiver_"+i,Save(Object.Instantiate(water.Spline.Segments[i].Mesh),"RiverSegment"+i+".asset"),i==2?shallowMat:mat,false);
        }
        finally {Object.DestroyImmediate(temp);}
        // 原直路在河道处断开，浅滩通过河床导航连通；营地不受影响。
        var path=content.Find("ApproachPath"); path.localPosition=new Vector3(0,.015f,-9); path.localScale=new Vector3(3,.025f,9);
        var pathMat=path.GetComponent<Renderer>().sharedMaterial;
        var north=GameObject.CreatePrimitive(PrimitiveType.Cube); north.name="NorthBankPath"; north.transform.SetParent(root.transform,false);
        north.transform.localPosition=new Vector3(0,.015f,4.6f); north.transform.localScale=new Vector3(3,.025f,10.8f); north.GetComponent<Renderer>().sharedMaterial=pathMat;
        var rockPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Environment/CivilisationExpedition/Sources/SimpleNaturePack/Prefabs/Rock_01.prefab");
        for(int i=0;i<22;i++)
        {
            float x=-27+i*2.6f; if(Mathf.Abs(x)<3) continue;
            float z=RiverZ(x)+(i%2==0?-3.5f:3.5f);
            var rock=(GameObject)PrefabUtility.InstantiatePrefab(rockPrefab,root.transform);
            rock.name="RiverbankRock_"+i; rock.transform.localPosition=new Vector3(x,Height(x,z),z);
            rock.transform.localScale=Vector3.one*(.55f+(i%3)*.15f); rock.transform.localRotation=Quaternion.Euler(0,i*47,0);
        }
        // 移开落在水中的原装饰，保留原树木及其碰撞。
        foreach(var t in content.GetComponentsInChildren<Transform>().Where(t=>t.parent==content&&(t.name.StartsWith("ValleyTree_")||t.name.StartsWith("ValleyRock_"))))
        {
            var p=t.localPosition; if(Mathf.Abs(p.z-RiverZ(p.x))<5) p.z=RiverZ(p.x)+(p.z>RiverZ(p.x)?6:-6);
            p.y=Height(p.x,p.z); t.localPosition=p;
        }
        CreateBoundary(root.transform);
        BakeNavigation(content);
        EditorUtility.SetDirty(terrainMat); EditorUtility.SetDirty(sandMat); EditorUtility.SetDirty(mat); EditorUtility.SetDirty(shallowMat); AssetDatabase.SaveAssets();
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败");
        Selection.activeGameObject=root;
        if(SceneView.lastActiveSceneView!=null) SceneView.lastActiveSceneView.LookAt(new Vector3(0,0,1),Quaternion.Euler(55,0,0),36);
        return "河流样片已保存；Poseidon 水面5段，浅滩1处，泥沙河岸与低坡静态网格，四面边界闭合，交互点保持原位。";
    }

    /// <summary>从正式碰撞几何重新烘焙导航；深水不可走，四面墙限制边缘导航范围。</summary>
    private static void BakeNavigation(Transform content)
    {
        var sources=new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(content,~0,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
        for(int i=0;i<60;i++)
        {
            float x=-29.5f+i; if(Mathf.Abs(x)<2.5f) continue;
            sources.Add(new NavMeshBuildSource {shape=NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(new Vector3(x,-.3f,RiverZ(x)),Quaternion.identity,Vector3.one),size=new Vector3(1.2f,2,5.3f),area=1});
        }
        var nav=NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0),sources,new Bounds(Vector3.zero,new Vector3(64,14,64)),Vector3.zero,Quaternion.identity);
        if(nav==null) throw new InvalidOperationException("导航为空");
        var navPath="Assets/Game/Art/Environment/CivilisationExpedition/Navigation/CivilisationValleyNavMesh.asset";
        var oldNav=AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath); EditorUtility.CopySerialized(nav,oldNav); Object.DestroyImmediate(nav);
        EditorUtility.SetDirty(oldNav);
        var settings=new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject); settings.FindProperty("m_NavMeshData").objectReferenceValue=oldNav; settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
