using System;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Game;
using Luban;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>只制作首场正式岛岸资源；第三方原件不修改，场地规则不由此工具决定。</summary>
public static class ArcadeIslandAuthoring
{
    const string Folder = "Assets/Game/Art/Arcade/Island";
    const string Prefab = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab";
    static Material sand, water, shallowWater, shoreline, rock, trunk, leaves, ember;

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("制作资源须在编辑态执行");
        Directory.CreateDirectory(Folder + "/Models");
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Meshes");
        AssetDatabase.Refresh();
        sand = Mat("Sand", new Color(.67f,.53f,.29f));
        water = Mat("Water", new Color(.08f,.43f,.53f));
        shallowWater = Mat("ShallowWater", new Color(.22f,.65f,.67f));
        shoreline = Mat("Shoreline", new Color(.72f,.88f,.80f));
        rock = Mat("Rock", new Color(.34f,.37f,.35f));
        trunk = Mat("Trunk", new Color(.30f,.19f,.10f));
        leaves = Mat("PalmLeaves", new Color(.15f,.39f,.17f));
        ember = Mat("Ember", new Color(.30f,.16f,.07f));
        var palm = CopyModel("Assets/Art/PolygonPrototype/Models/SM_Prop_Tree_Palm_01.fbx", "IslandPalm.fbx");
        var rocks = CopyModel("Assets/Art/PolygonPrototype/Models/SM_Generic_Small_Rocks_01.fbx", "IslandRock.fbx");
        var camp = CopyModel("Assets/Art/ARPG Effects/Demo/Models/RPGFXCampfire.FBX", "IslandCampfire.fbx");
        var root = new GameObject("ArcadeIslandVisualSet");
        try
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes"))));
            if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
            var region = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight);
            Shape(root.transform, "Ocean", PrimitiveType.Cube, new Vector3(0,-.09f,0),new Vector3(78,.05f,78),water);
            Surface(root.transform,"Sand",ArcadeIslandMesh.Sand(region),sand,.02f);
            Surface(root.transform,"Water",ArcadeIslandMesh.Water(region),shallowWater,.02f);
            Surface(root.transform,"Shoreline",ArcadeIslandMesh.Shoreline(region),shoreline,.024f);
            var decor = new GameObject("ShoreDecor").transform; decor.SetParent(root.transform,false);
            var positions = new[] {new Vector3(-17, .12f, 15),new Vector3(16,.12f,16),new Vector3(18,.12f,8),new Vector3(-18,.12f,5)};
            for(int i=0;i<positions.Length;i++)
            {
                var point=region.LandPoint(positions[i].x,positions[i].z,1.5f);
                positions[i]=new Vector3(point.X,.02f,point.Y);
                var obj = Place(palm,decor,"Palm"+i,positions[i],3.7f);
                foreach(var r in obj.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = r.sharedMaterials.Select((m,n)=> n==0?leaves:trunk).ToArray();
            }
            for(int i=0;i<8;i++)
            {
                var point=region.Constrain(i%2==0?-19:19,-10+i*3.5f,.3f);
                Place(rocks,decor,"Rock"+i,new Vector3(point.X,.02f,point.Y),.4f,rock);
            }
            var fireplace = new GameObject("Campfire").transform; fireplace.SetParent(root.transform,false);
            fireplace.localPosition = new Vector3(4,.12f,3);
            Place(camp,fireplace,"Logs",Vector3.zero,.4f,ember);
            var flamePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/FirstBatch/Prefabs/BurningGround.prefab");
            if(flamePrefab==null) throw new InvalidOperationException("已存在的 BurningGround 资源缺失");
            var flame=Object.Instantiate(flamePrefab,fireplace); flame.name="Flame"; flame.SetActive(false);
            // 独立边界读取接触半径；火焰和烟仍按火堆本体半径缩放，不夸大危险范围。
            var boundary=flame.transform.Find("Boundary");
            if(boundary==null) throw new InvalidOperationException("BurningGround 缺少范围网格");
            boundary.SetParent(fireplace,false);boundary.name="DangerBoundary";boundary.gameObject.SetActive(false);
            Smoke(flame.transform);
            foreach(var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            // 在已有地址增加休眠子模板；其他地图只启用四套障碍变体，不显示岛岸。
            const string bankPath = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
            var bank = PrefabUtility.LoadPrefabContents(bankPath);
            try
            {
                var old=bank.transform.Find("Island"); int sibling=old!=null?old.GetSiblingIndex():bank.transform.childCount;
                if(old!=null) Object.DestroyImmediate(old.gameObject);
                var saved=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                var island=(GameObject)PrefabUtility.InstantiatePrefab(saved,bank.transform);
                island.name="Island";island.SetActive(false);
                island.transform.SetSiblingIndex(sibling);
                PrefabUtility.SaveAsPrefabAsset(bank,bankPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(bank);}
            AssetDatabase.SaveAssets();
            return Prefab;
        }
        finally {Object.DestroyImmediate(root);}
    }
    static void Surface(Transform parent,string name,Mesh mesh,Material material,float height)
    {
        string path=Folder+"/Meshes/"+mesh.name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);existing=mesh;}
        else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);}
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.transform.localPosition=new Vector3(0,height,0);go.GetComponent<MeshFilter>().sharedMesh=existing;
        go.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
    /// <summary>复用已收集的轻烟材质；少量透明粒子向上散开，不增加灯光和伤害逻辑。</summary>
    static void Smoke(Transform parent)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/BlastSmoke.mat");
        if(material==null)throw new InvalidOperationException("现有烟材质缺失");
        var go=new GameObject("Smoke");go.transform.SetParent(parent,false);go.transform.localPosition=new Vector3(0,.4f,0);
        var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=true;main.playOnAwake=true;main.duration=2f;main.startLifetime=2.2f;
        main.startSpeed=.85f;main.startSize=new ParticleSystem.MinMaxCurve(.65f,.9f);
        main.startColor=new Color(.22f,.22f,.2f,.45f);main.maxParticles=16;
        main.simulationSpace=ParticleSystemSimulationSpace.Local;
        var emission=ps.emission;emission.rateOverTime=5f;
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=10f;shape.radius=.15f;
        shape.rotation=new Vector3(-90,0,0);
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    static GameObject CopyModel(string source,string name)
    {
        var path=Folder+"/Models/"+name;
        if(!File.Exists(path) && !AssetDatabase.CopyAsset(source,path)) throw new InvalidOperationException(source);
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        foreach(var key in importer.GetExternalObjectMap().Keys) importer.RemoveRemap(key);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
    static Material Mat(string name,Color color)
    {
        var path=Folder+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",name=="Water"?.75f:.15f);EditorUtility.SetDirty(m);return m;
    }
    static GameObject Place(GameObject prefab,Transform parent,string name,Vector3 position,float height,Material material=null)
    {
        // 先在原点计算模型尺寸，再挂父级；父级营火坐标不能进入模型对齐偏移。
        var obj=Object.Instantiate(prefab);obj.name=name;obj.transform.position=Vector3.zero;
        var renderers=obj.GetComponentsInChildren<Renderer>();
        if(renderers.Length==0) throw new InvalidOperationException(name+" 模型无 Renderer");
        var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        float scale=height/Mathf.Max(.01f,bounds.size.y);
        obj.transform.SetParent(parent,false);
        obj.transform.localScale*=scale;
        obj.transform.localPosition=position-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
        if(material!=null)foreach(var r in renderers)r.sharedMaterials=r.sharedMaterials.Select(_=>material).ToArray();
        foreach(var c in obj.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        return obj;
    }
    static void Shape(Transform root,string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material material)
    {
        var obj=GameObject.CreatePrimitive(type);obj.name=name;obj.transform.SetParent(root,false);
        obj.transform.localPosition=pos;obj.transform.localScale=scale;obj.GetComponent<Renderer>().sharedMaterial=material;
        Object.DestroyImmediate(obj.GetComponent<Collider>());
    }
}
