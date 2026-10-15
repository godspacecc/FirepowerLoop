using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>通过 Unity MCP 执行的离线基地美术脚本；只整理资源和场景，不生成玩法对象或修改配置。</summary>
public static class BaseCampArtAuthoring
{
    private const string ArtRoot = "Assets/Game/Art/Environment/BaseCamp";
    private const string ScenePath = "Assets/Game/Scenes/BaseMap.unity";
    private const string RpgRoot = "Assets/Art/RPGPP_LT/";
    private static readonly string[] Selected =
    {
        "Prefabs/Exterior/Wood_path/rpgpp_lt_shed_wood_01.prefab",
        "Prefabs/Exterior/Wood_path/rpgpp_lt_awning_standing_01a.prefab",
        "Prefabs/Exterior/Wood_path/rpgpp_lt_fence_wood_01a.prefab",
        "Prefabs/Props/Containers/rpgpp_lt_crate_01.prefab",
        "Prefabs/Props/Containers/rpgpp_lt_crate_02.prefab",
        "Prefabs/Props/Containers/rpgpp_lt_barrel_01.prefab",
        "Prefabs/Props/Containers/rpgpp_lt_sack_02_set.prefab",
        "Prefabs/Props/Benches/rpgpp_lt_bench_wood_01.prefab",
        "Prefabs/Props/Tables/rpgpp_lt_table_01.prefab",
        "Prefabs/Props/Wood/rpgpp_lt_log_wood_01.prefab",
        "Prefabs/Props/Items/rpgpp_lt_rake_01.prefab",
        "Prefabs/Props/Items/rpgpp_lt_broom_01.prefab"
    };

    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static string Destination(string source) => ArtRoot + "/Sources/" + source.Substring("Assets/Art/".Length);

    /// <summary>复制实际使用的完整依赖；保留导入设置，引用改为 Unity 分配的新副本身份，原资源保持不变。</summary>
    public static string CopySelectedArt()
    {
        var roots = Selected.Select(path => RpgRoot + path).Concat(new[]
        {
            "Assets/Art/ARPG Effects/Demo/Models/RPGFXCampfire.FBX",
            "Assets/Art/ARPG Effects/Textures/fire_torch_16x4.png"
        }).ToArray();
        var dependencies = AssetDatabase.GetDependencies(roots, true)
            .Where(path => path.StartsWith("Assets/Art/") && !path.EndsWith(".cs")).ToArray();
        var guidMap = new Dictionary<string, string>();
        foreach (var source in dependencies)
        {
            var destination = Destination(source);
            Folder(Path.GetDirectoryName(destination).Replace('\\', '/'));
            if (AssetDatabase.LoadMainAssetAtPath(destination) == null && !AssetDatabase.CopyAsset(source, destination))
                throw new InvalidOperationException("复制失败：" + source);
            guidMap[AssetDatabase.AssetPathToGUID(source)] = AssetDatabase.AssetPathToGUID(destination);
        }
        // FBX 二进制保持原样；副本 YAML 和导入器外部引用同步映射，避免暗中引用备用目录。
        foreach (var source in dependencies)
        {
            var destination = Destination(source);
            foreach (var file in new[] { destination, destination + ".meta" })
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".meta" && extension != ".prefab" && extension != ".mat" && extension != ".asset") continue;
                var text = File.ReadAllText(file);
                foreach (var pair in guidMap) text = text.Replace("guid: " + pair.Key, "guid: " + pair.Value);
                File.WriteAllText(file, text, new UTF8Encoding(false));
            }
        }
        AssetDatabase.Refresh();
        return "Copied " + dependencies.Length + " selected assets and remapped their dependencies.";
    }

    private static Material Material(string name, Color color)
    {
        var path = ArtRoot + "/Materials/" + name + ".mat";
        Folder(ArtRoot + "/Materials");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", .08f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Transform Group(Transform parent, string name)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    private static GameObject Place(Transform parent, string path, string name, Vector3 position, float yaw = 0, float scale = 1)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("资源缺失：" + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(true);
        // 首轮美术不改变现有移动边界，不引入第三方运行脚本、音频或新的交互物。
        foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (var script in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
        foreach (var audio in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
        foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
        return go;
    }

    private static GameObject Prop(Transform parent, string suffix, string name, float x, float z, float yaw = 0, float scale = 1) =>
        Place(parent, Destination(RpgRoot + Selected.Single(path => path.EndsWith(suffix + ".prefab"))), name, new Vector3(x, 0, z), yaw, scale);

    private static void Patch(Transform parent, string name, Vector3 position, Vector2 size, Material material, int seed)
    {
        var path = ArtRoot + "/Meshes/" + name + ".asset";
        Folder(ArtRoot + "/Meshes");
        var random = new System.Random(seed);
        var vertices = new Vector3[15];
        var triangles = new int[14 * 3];
        for (var i = 0; i < 14; i++)
        {
            var angle = i * Mathf.PI * 2 / 14;
            var radius = .88f + (float)random.NextDouble() * .12f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * size.x * .5f * radius, 0, Mathf.Sin(angle) * size.y * .5f * radius);
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = (i + 1) % 14 + 1;
            triangles[i * 3 + 2] = i + 1;
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear(); mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    private static void Fire(Transform parent, Vector3 position)
    {
        var model = Place(parent, Destination("Assets/Art/ARPG Effects/Demo/Models/RPGFXCampfire.FBX"), "营火石圈", position, 0, .75f);
        var stone = Material("CampfireStone", new Color(.35f,.36f,.34f));
        var wood = Material("CampfireWood", new Color(.28f,.17f,.08f));
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            renderer.sharedMaterials = renderer.sharedMaterials.Select(mat => mat != null && mat.name.ToLowerInvariant().Contains("wood") ? wood : stone).ToArray();
        var effectPath = ArtRoot + "/Materials/CampfireFlame.mat";
        var flameMaterial = AssetDatabase.LoadAssetAtPath<Material>(effectPath);
        if (flameMaterial == null)
        {
            flameMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(flameMaterial, effectPath);
        }
        flameMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Destination("Assets/Art/ARPG Effects/Textures/fire_torch_16x4.png")));
        flameMaterial.SetColor("_BaseColor", new Color(1.6f,.9f,.4f,1));
        flameMaterial.SetFloat("_Surface", 1); flameMaterial.SetFloat("_Blend", 2);
        flameMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); flameMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
        flameMaterial.SetFloat("_ZWrite", 0); flameMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); flameMaterial.renderQueue = 3000;
        var go = new GameObject("营火火焰", typeof(ParticleSystem));
        go.transform.SetParent(parent,false); go.transform.localPosition = position + Vector3.up * .35f;
        var particles = go.GetComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main; main.loop=true; main.playOnAwake=true; main.startLifetime=.65f; main.startSpeed=.45f; main.startSize=.95f; main.maxParticles=12; main.simulationSpace=ParticleSystemSimulationSpace.Local;
        var emission=particles.emission; emission.rateOverTime=12;
        var shape=particles.shape; shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=9;shape.radius=.15f;shape.rotation=new Vector3(-90,0,0);
        var sheet=particles.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=16;sheet.numTilesY=4;sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1);
        var rendererFlame=go.GetComponent<ParticleSystemRenderer>();rendererFlame.sharedMaterial=flameMaterial;rendererFlame.shadowCastingMode=ShadowCastingMode.Off;
        var lamp=new GameObject("营火暖光",typeof(Light));lamp.transform.SetParent(parent,false);lamp.transform.localPosition=position+Vector3.up*.8f;
        var light=lamp.GetComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.58f,.22f);light.range=5;light.intensity=2;light.shadows=LightShadows.None;
    }

    /// <summary>按已确认的功能点周围布置；场景内容根和五个碰撞体保留，只替换地表、边界的视觉表现。</summary>
    public static string DressScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请在非运行状态布置基地。");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var context = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapContext>(true)).Single();
            var content = context.MapContentRoot.transform;
            var old = content.Find("BaseCampDecoration");
            if (old != null) throw new InvalidOperationException("基地已布置；避免重建时覆盖手工调整。");
            var ground = content.GetComponentsInChildren<MeshRenderer>(true).Single(renderer => renderer.name == "Ground");
            ground.sharedMaterial = Material("CampGround",new Color(.28f,.32f,.20f));
            foreach (var wall in content.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => new[]{"North","South","East","West"}.Contains(renderer.name))) wall.enabled=false;
            var root = Group(content,"BaseCampDecoration");
            var surfaces=Group(root,"地表与通路");
            var dirt=Material("CampDirt",new Color(.42f,.33f,.23f));
            var gravel=Material("CampGravel",new Color(.46f,.40f,.30f));
            Patch(surfaces,"活动区土坪",new Vector3(0,.013f,2),new Vector2(13,13),dirt,1);
            Patch(surfaces,"入口土路",new Vector3(0,.018f,-8),new Vector2(3.6f,15),dirt,2);
            Patch(surfaces,"西侧物资步道",new Vector3(-5,.019f,2),new Vector2(6,8),gravel,3);
            Patch(surfaces,"东侧摊位步道",new Vector3(5,.02f,2),new Vector2(6,8),gravel,4);
            Patch(surfaces,"南侧休息土坪",new Vector3(-6,.018f,-6),new Vector2(7,6),dirt,5);
            var warehouse=Group(root,"仓库物资区");
            Prop(warehouse,"rpgpp_lt_shed_wood_01","仓库木棚",-7.2f,0,90,1.0f);
            Prop(warehouse,"rpgpp_lt_crate_02","仓库大箱",-7.7f,.5f,10,1.15f);
            Prop(warehouse,"rpgpp_lt_crate_01","仓库木箱",-6.5f,-.8f,-8,1.1f);
            Prop(warehouse,"rpgpp_lt_sack_02_set","仓库麻袋",-8.2f,-1,20,1.0f);
            Prop(warehouse,"rpgpp_lt_barrel_01","仓库水桶",-6.1f,.9f,0,1);
            Prop(warehouse,"rpgpp_lt_barrel_01","仓库侧桶",-8.5f,1.0f,0,.9f);
            var workshop=Group(root,"制作工作区");
            Prop(workshop,"rpgpp_lt_shed_wood_01","制作棚",-6.6f,6.3f,90,.95f);
            Prop(workshop,"rpgpp_lt_table_01","制作木桌",-6.6f,5.7f,90,.75f);
            Prop(workshop,"rpgpp_lt_log_wood_01","加工木料",-8.1f,4.2f,10,1.1f);
            Prop(workshop,"rpgpp_lt_rake_01","靠棚工具",-7.7f,5.8f,25,.8f);
            Prop(workshop,"rpgpp_lt_crate_01","制作零件箱",-5.6f,6.7f,5,.85f);
            var trader=Group(root,"商人物资摊位");
            var awning=Prop(trader,"rpgpp_lt_awning_standing_01a","商人布棚",6.7f,6.1f,0,.7f);
            var canopy = new Material(AssetDatabase.LoadAssetAtPath<Material>(Destination(RpgRoot+"Materials/rpgpp_lt_mat_a.mat")));
            canopy.SetColor("_BaseColor",new Color(.87f,.76f,.54f));
            AssetDatabase.CreateAsset(canopy,ArtRoot+"/Materials/TraderCanopy.mat");
            foreach(var renderer in awning.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial=canopy;
            Prop(trader,"rpgpp_lt_table_01","商人货桌",6.7f,5.9f,0,.7f);
            Prop(trader,"rpgpp_lt_crate_02","商人货箱",7.9f,4.7f,15,.85f);
            Prop(trader,"rpgpp_lt_sack_02_set","商人物资袋",8.2f,6.5f,40,.75f);
            Prop(trader,"rpgpp_lt_broom_01","摊位扫帚",5.4f,6.4f,-10,.8f);
            var guide=Group(root,"出发导览区");
            Prop(guide,"rpgpp_lt_table_01","出发准备桌",6.6f,0,90,.65f);
            Prop(guide,"rpgpp_lt_crate_01","出发补给箱",7.2f,-1.1f,-12,.9f);
            Prop(guide,"rpgpp_lt_barrel_01","出发水桶",7.5f,1.0f,0,.85f);
            var arcade=Group(root,"街机休闲角");
            Prop(arcade,"rpgpp_lt_shed_wood_01","街机休闲棚",0,9.3f,0,.8f);
            Prop(arcade,"rpgpp_lt_bench_wood_01","街机长凳",0,9.2f,0,1);
            Prop(arcade,"rpgpp_lt_barrel_01","休闲物资桶",1.8f,9,0,.8f);
            var resting=Group(root,"营火休息区");
            Fire(resting,new Vector3(-6,0,-6));
            Prop(resting,"rpgpp_lt_bench_wood_01","营火后长凳",-6,-4,0,1.2f);
            Prop(resting,"rpgpp_lt_bench_wood_01","营火侧长凳",-8,-6,90,1.2f);
            Prop(resting,"rpgpp_lt_log_wood_01","营火柴堆",-8.5f,-8,0,.85f);
            Prop(resting,"rpgpp_lt_barrel_01","休息区水桶",-4.5f,-7.6f,0,.75f);
            var boundary=Group(root,"木栅栏与自然边界");
            for(int i=-2;i<=2;i++)
            {
                Prop(boundary,"rpgpp_lt_fence_wood_01a","北侧木栅栏"+i,i*4.3f,14.4f,0,1.05f);
                Prop(boundary,"rpgpp_lt_fence_wood_01a","西侧木栅栏"+i,-14.4f,i*4.3f,90,1.05f);
                Prop(boundary,"rpgpp_lt_fence_wood_01a","东侧木栅栏"+i,14.4f,i*4.3f,90,1.05f);
            }
            foreach(var x in new[]{-10f,-5.5f,5.5f,10f}) Prop(boundary,"rpgpp_lt_fence_wood_01a","入口侧栅栏",x,-14.5f,0,1.05f);
            var nature="Assets/Game/Art/Environment/HeJiaVillage/Nature/Prefabs/";
            var pine=nature+"HeJia_Pine_Tree_03.prefab";
            var shrub=nature+"HeJia_Generic_Shrub_01.prefab";
            var rock=nature+"HeJia_Generic_Rock_01.prefab";
            var random=new System.Random(20261004);
            for(int i=0;i<12;i++)
            {
                float x=i<6?-13.2f:13.2f;float z=-10+(i%6)*4.5f;
                Place(boundary,pine,"外围松树"+i,new Vector3(x,0,z),(float)random.NextDouble()*360,.65f+(float)random.NextDouble()*.22f);
            }
            foreach(var x in new[]{-10f,-5f,5f,10f})Place(boundary,pine,"北侧松树",new Vector3(x,0,13.2f),x*15,.9f);
            for(int i=0;i<24;i++)
            {
                var angle=i*Mathf.PI*2/24;var x=Mathf.Cos(angle)*(12+(float)random.NextDouble());var z=Mathf.Sin(angle)*(12+(float)random.NextDouble());
                Place(boundary,shrub,"外围灌木"+i,new Vector3(x,0,z),i*31,.8f+(float)random.NextDouble()*.5f);
                if(i%2==0)Place(boundary,rock,"外围岩石"+i,new Vector3(x*.93f,0,z*.93f),i*23,.4f+(float)random.NextDouble()*.4f);
            }
            var grass="Assets/Game/Art/Environment/HeJiaVillage/SimpleNaturePack/Prefabs/Grass_01.prefab";
            for(int i=0;i<35;i++)
            {
                var x=-11+(float)random.NextDouble()*22;var z=-12+(float)random.NextDouble()*24;
                if(Mathf.Abs(x)<3 || (Mathf.Abs(x)<9 && z>-2 && z<8))continue;
                Place(boundary,grass,"边缘草簇"+i,new Vector3(x,.01f,z),i*29,.65f);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("基地场景保存失败。");
            return "Base camp authored under existing map content root; runtime spawn configuration unchanged.";
        }
        finally { EditorSceneManager.CloseScene(scene,true); }
    }

    /// <summary>第一轮画面验收后的细化：修正模型原点、植被尺度及入口净空，不重建已布置场景。</summary>
    public static string RefineScene()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            var root=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<MapContext>(true)).Single()
                .MapContentRoot.transform.Find("BaseCampDecoration");
            var model=root.Find("营火休息区/营火石圈");
            var bounds=model.GetComponentsInChildren<Renderer>().First().bounds;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
            model.position += new Vector3(-6-bounds.center.x,.025f-bounds.min.y,-6-bounds.center.z);
            var fire=root.Find("营火休息区/营火火焰").GetComponent<ParticleSystem>();
            var shape=fire.shape;shape.rotation=new Vector3(-90,0,0);
            var random=new System.Random(47);
            var nature=root.Find("木栅栏与自然边界");
            var foliageMaterials=new Dictionary<Material,Material>();
            foreach(var item in nature.Cast<Transform>())
            {
                if(item.name.StartsWith("外围岩石"))item.localScale=Vector3.one*(3.3f+(float)random.NextDouble()*3.2f);
                if(item.name.StartsWith("边缘草簇"))item.localScale=Vector3.one*(2.7f+(float)random.NextDouble()*2);
                if(!item.name.StartsWith("外围灌木"))continue;
                item.localPosition += new Vector3((float)random.NextDouble()*1.4f-.7f,0,(float)random.NextDouble()*1.4f-.7f);
                if(Mathf.Abs(item.localPosition.x)<2.2f && item.localPosition.z< -9)
                    item.localPosition=new Vector3(item.localPosition.x<0?-4.2f:4.2f,0,item.localPosition.z);
                foreach(var renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
                    {
                        if(!foliageMaterials.TryGetValue(source,out var copy))
                        {
                            var path=ArtRoot+"/Materials/CampShrub_"+source.name+".mat";
                            copy=AssetDatabase.LoadAssetAtPath<Material>(path);
                            if(copy==null){copy=new Material(source);AssetDatabase.CreateAsset(copy,path);}
                            copy.SetColor("_BaseColor",new Color(.68f,.76f,.56f));
                            foliageMaterials[source]=copy;
                        }
                        return copy;
                    }).ToArray();
                }
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return "Refined fire origin, perimeter rocks, grass and entry clearance.";
        }
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
}
