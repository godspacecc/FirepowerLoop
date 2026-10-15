using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>补齐基地加载图所需的固定景观。只修改 BaseMap 与自有美术，不写配置、存档或可拆摆件。</summary>
public static class BaseCampLakesideAuthoring
{
    private const string Root = "Assets/Game/Art/Environment/BaseCamp/Lakeside/";
    private const string ScenePath = "Assets/Game/Scenes/BaseMap.unity";
    /// <summary>拒绝编辑未保存场景；同名景观组可重复替换，交互点和动态根保持权威，北边界接到木屋围合。</summary>
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出 Play Mode");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.isLoaded && scene.isDirty) throw new InvalidOperationException("基地有未保存更改");
        if (scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
        AssetDatabase.Refresh();
        foreach (var model in new[]{"BaseLakesideCabin","BaseLakesideLandscape"}) PrepareMaterials(model);
        scene = EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            var ctx=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MapContext>(true)).Single();
            var decor=ctx.MapContentRoot.transform.Find("BaseCampDecoration");
            var previous=decor.Find("LakesideArt");
            if(previous!=null)Object.DestroyImmediate(previous.gameObject);
            var root=new GameObject("LakesideArt").transform;root.SetParent(decor,false);
            var landscape=Place(root,Root+"BaseLakesideLandscape.fbx","湖岸与雪山",Vector3.zero,180,1);
            PrefabUtility.UnpackPrefabInstance(landscape,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            // 景观中央网格比平台碰撞面低 1 厘米；只校准地表表现，保持碰撞与出生高度。
            var terrain = landscape.transform.Find("LakesideTerrain");
            if (terrain == null) throw new InvalidOperationException("湖岸景观缺少 LakesideTerrain");
            terrain.position += Vector3.up * .01f;
            var mountain = landscape.transform.Find("DistantMountainRidge");
            // FBX 子节点带轴向转换，压低山脊必须在 Unity 父节点的 Y 轴完成。
            var mountainScale=new GameObject("山脊高度").transform;mountainScale.SetParent(landscape.transform,false);
            mountain.SetParent(mountainScale,false);mountainScale.localScale=new Vector3(1,.36f,1);
            var cabin=Place(root,Root+"BaseLakesideCabin.fbx","湖畔木屋",new Vector3(-9,0,22.6f),180,1.4f);
            ConfigureInterior(ctx,cabin);
            var pinePath="Assets/Game/Art/Environment/HeJiaVillage/Nature/Prefabs/HeJia_Pine_Tree_03.prefab";
            var rockPath="Assets/Game/Art/Environment/HeJiaVillage/Nature/Prefabs/HeJia_Generic_Rock_01.prefab";
            var rng=new System.Random(1011);
            for(int i=0;i<80;i++)
            {
                float x,z;
                if(i<40){x=-52+(float)rng.NextDouble()*32;z=-35+(float)rng.NextDouble()*96;}
                else if(i<68){x=-45+(float)rng.NextDouble()*65;z=34+(float)rng.NextDouble()*9;}
                else {x=20+(float)rng.NextDouble()*4;z=9+(float)rng.NextDouble()*24;}
                if(x> -19 && x<1 && z>14 && z<33)continue;
                var tree=Place(root,pinePath,"湖岸松林_"+i,new Vector3(x,GroundHeight(x,z),z),(float)rng.NextDouble()*360,.85f+(float)rng.NextDouble()*.9f);
                StripExtras(tree);
            }
            for(int i=0;i<32;i++)
            {
                float x=21+(float)rng.NextDouble()*5,z=-22+(float)rng.NextDouble()*64;
                var rock=Place(root,rockPath,"湖岸岩石_"+i,new Vector3(x,GroundHeight(x,z)-.25f,z),i*73,5+(float)rng.NextDouble()*8);
                StripExtras(rock);
            }
            // 北侧树不挡住木屋正面，仍全部位于原交互路线外。
            var northTrees=decor.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="北侧松树").OrderBy(t=>t.localPosition.x).ToArray();
            for(int i=0;i<northTrees.Length;i++)northTrees[i].localPosition=new Vector3(i<2?-18-i*2:16+(i-2)*3,0,18+i*2);
            var sky=GameObject.CreatePrimitive(PrimitiveType.Sphere);sky.name="基地天空穹顶";sky.transform.SetParent(root,false);sky.transform.localScale=Vector3.one*1100;
            Object.DestroyImmediate(sky.GetComponent<Collider>());
            var skyMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"CampSky.mat");
            if(skyMaterial==null){skyMaterial=new Material(Shader.Find("AnotherDay/BaseCampSky"));AssetDatabase.CreateAsset(skyMaterial,Root+"CampSky.mat");}
            var skyRenderer=sky.GetComponent<Renderer>();skyRenderer.sharedMaterial=skyMaterial;skyRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;skyRenderer.receiveShadows=false;
            ctx.MapContentRoot.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Ground").GetComponent<Renderer>().enabled=false;
            // 平台碰撞不动；外延地形遮住原方形平台侧壁。
            foreach(var r in landscape.GetComponentsInChildren<Renderer>())
                if(r.name=="LakeSurface") {r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;}
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("基地保存失败");
            return "BaseMap lakeside authored; enterable cabin, lake, mountain ridge, forest; dynamic root children="+ctx.DynamicObjectRoot.childCount;
        }
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
    /// <summary>只升级已有木屋与入口，不重排树林、远景或玩家动态摆件；重复执行替换自有碰撞组。</summary>
    public static string UpgradeCabin()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("请先退出 Play Mode");
        var scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.isLoaded && scene.isDirty)throw new InvalidOperationException("基地有未保存更改");
        bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            PrepareMaterials("BaseLakesideCabin");
            var ctx=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MapContext>(true)).Single();
            var cabin=ctx.MapContentRoot.transform.Find("BaseCampDecoration/LakesideArt/湖畔木屋");
            if(cabin==null)throw new InvalidOperationException("缺少已有湖畔木屋");
            ConfigureInterior(ctx,cabin.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("基地保存失败");
            return "木屋门窗洞、透明玻璃、室内支撑、分墙碰撞、北侧通路和暖光已保存；动态摆件未修改";
        }
        finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
    }

    /// <summary>碰撞尺寸按 Blender 米制声明并统一转换轴向；屋顶始终保留，不挂俯视淡化组件。</summary>
    private static void ConfigureInterior(MapContext ctx,GameObject cabin)
    {
        // 留出原存档围栏与门廊之间的间隙，不迁移玩家摆件。
        cabin.transform.localPosition=new Vector3(-9,0,22.6f);
        foreach(var c in cabin.GetComponents<Collider>())Object.DestroyImmediate(c);
        foreach(var c in cabin.GetComponents<CameraOccluderView>())Object.DestroyImmediate(c);
        var previous=cabin.transform.Find("InteriorPhysics");
        if(previous!=null)Object.DestroyImmediate(previous.gameObject);
        var physics=new GameObject("InteriorPhysics").transform;physics.SetParent(cabin.transform,false);
        CabinBox(physics,"室内地板",new Vector3(0,0,.33f),new Vector3(9.4f,6.12f,.60f));
        CabinBox(physics,"门廊地板",new Vector3(0,-4.285f,.33f),new Vector3(9.8f,2.47f,.60f));
        CabinBox(physics,"后墙",new Vector3(0,3.04f,2.13f),new Vector3(9.55f,.30f,3.02f));
        foreach(float x in new[]{-4.67f,4.67f})CabinBox(physics,"侧墙",new Vector3(x,0,2.13f),new Vector3(.26f,6.25f,3.02f));
        // 前墙按真实窗洞分段；玻璃独立阻挡玩家与镜头，墙体不再填满窗洞。
        foreach(float side in new[]{-1f,1f})
        {
            CabinBox(physics,"前墙内柱",new Vector3(side*1.525f,-3.07f,2.13f),new Vector3(1.05f,.34f,3.02f));
            CabinBox(physics,"前墙外柱",new Vector3(side*4.105f,-3.07f,2.13f),new Vector3(1.31f,.34f,3.02f));
            CabinBox(physics,"窗下墙",new Vector3(side*2.75f,-3.07f,1.0375f),new Vector3(1.4f,.34f,.835f));
            CabinBox(physics,"窗上墙",new Vector3(side*2.75f,-3.07f,3.2725f),new Vector3(1.4f,.34f,.735f));
            CabinBox(physics,"窗玻璃"+side,new Vector3(side*2.75f,-3.08f,2.18f),new Vector3(1.4f,.025f,1.45f));
        }
        foreach(var pane in cabin.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Glass"))
        {
            pane.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            pane.receiveShadows=false;
        }
        foreach(float x in new[]{-1.07f,1.07f})CabinBox(physics,"门框",new Vector3(x,-3.15f,1.96f),new Vector3(.16f,.40f,2.66f));
        CabinBox(physics,"门楣",new Vector3(0,-3.15f,3.3f),new Vector3(2.3f,.40f,.18f));
        CabinBox(physics,"常开门",new Vector3(1.08f,-4.24f,1.94f),new Vector3(.16f,1.96f,2.58f));
        CabinBox(physics,"室内顶板",new Vector3(0,0,3.65f),new Vector3(9.5f,6.25f,.16f));
        for(int i=0;i<3;i++)CabinBox(physics,"入口台阶"+i,new Vector3(-11f/7,-6.15f+i*.4f,(i+1)*.09f),new Vector3(1.42f,.46f,(i+1)*.18f));
        foreach(float x in new[]{-4.87f,4.87f})CabinBox(physics,"门廊侧栏",new Vector3(x,-4.28f,1.05f),new Vector3(.16f,2.5f,.86f));
        CabinBox(physics,"左侧栏",new Vector3(-3.75f,-5.35f,1.05f),new Vector3(1.85f,.20f,.86f));
        CabinBox(physics,"右侧栏",new Vector3(3.25f,-5.35f,1.05f),new Vector3(2.9f,.20f,.86f));
        foreach(float x in new[]{-4.7f,-2.8f,1.75f,4.7f})CabinBox(physics,"门廊立柱",new Vector3(x,-5.35f,1.91f),new Vector3(.20f,.20f,2.82f));
        CabinBox(physics,"门廊顶",new Vector3(0,-4.35f,3.55f),new Vector3(10,2.8f,.12f));
        // 原北边界只保留木屋右侧；左侧由原西边界、门廊侧栏、木屋侧墙闭合。
        var north=ctx.MapContentRoot.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="North");
        north.localPosition=new Vector3(6.6f,1,15.5f);north.localScale=new Vector3(16.8f,2,1);
        var approach=ctx.MapContentRoot.transform.Find("BaseCampDecoration/LakesideArt/木屋引路板");
        if(approach!=null)Object.DestroyImmediate(approach.gameObject);
        var path=GameObject.CreatePrimitive(PrimitiveType.Cube);path.name="木屋引路板";
        path.transform.SetParent(cabin.transform.parent,false);path.transform.localPosition=new Vector3(-11.2f,-.03f,12.3f);
        path.transform.localScale=new Vector3(1.9f,.12f,3.2f);
        path.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"CabinWood2.mat");
        foreach(float x in new[]{-3.8f,3.8f})
        {
            var lamp=new GameObject("室内壁灯");lamp.transform.SetParent(physics,false);
            lamp.transform.localPosition=new Vector3(-x,2.5f,-2.45f);
            var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.69f,.37f);light.intensity=2.2f;light.range=9;light.shadows=LightShadows.Soft;
        }
    }

    private static void CabinBox(Transform parent,string name,Vector3 center,Vector3 size)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        go.transform.localPosition=new Vector3(-center.x,center.z,-center.y);
        var collider=go.AddComponent<BoxCollider>();collider.size=new Vector3(size.x,size.z,size.y);
    }
    private static float GroundHeight(float x,float z)
    {
        float beyond=Mathf.Max(Mathf.Abs(x)-15,Mathf.Abs(z)-15,0);
        float edge=Mathf.Min(26+5*Mathf.Sin(z*.05f)-x,44+6*Mathf.Sin(x*.07f)-z);
        float h=(Mathf.Sin(x*.13f)*Mathf.Cos(z*.1f)*1.3f+.9f*Mathf.Sin(x*.06f+z*.13f))*Mathf.Min(1,beyond/18);
        return Mathf.Min(1,edge/5)*(h+.1f)-1.2f*(1-Mathf.Min(1,edge/5));
    }
    private static GameObject Place(Transform parent,string path,string name,Vector3 position,float yaw,float scale)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(asset==null)throw new InvalidOperationException("缺少资源 "+path);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);go.name=name;
        go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;
        return go;
    }
    private static void StripExtras(GameObject go)
    {
        foreach(var c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        foreach(var c in go.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(c);
    }
    /// <summary>把 FBX 材质映射到正式 URP 资源；保留模型 GUID 与已有材质身份。</summary>
    private static void PrepareMaterials(string model)
    {
        string path=Root+model+".fbx";
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        if(importer==null)throw new InvalidOperationException("缺少模型 "+path);
        var colors=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(Root+"LakesidePalette.json"))["colors"].ToDictionary(c=>(string)c["name"],c=>new Color((float)c["rgba"][0],(float)c["rgba"][1],(float)c["rgba"][2],(float)c["rgba"][3]));
        var sources=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
        foreach(var source in sources)
        {
            string dest=Root+source.name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(dest);
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,dest);}
            var color=colors[source.name];
            if(source.name.StartsWith("Mountain")&&!source.name.StartsWith("MountainSnow"))color=Color.Lerp(color,new Color(.53f,.64f,.69f),.32f);
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",source.name.StartsWith("LakeWater")?.5f:.13f);
            if(source.name=="CabinWindow"){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(1,.46f,.10f)*.5f);}
            if(source.name=="CabinGlass")
            {
                // 透明窗玻璃与暖光灯罩分开；URP 使用 Alpha 混合且不写深度，保留室外视线。
                material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);
                material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha",(float)UnityEngine.Rendering.BlendMode.One);
                material.SetFloat("_DstBlendAlpha",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite",0);material.SetFloat("_AlphaClip",0);
                material.SetFloat("_Smoothness",.72f);material.SetFloat("_SpecularHighlights",0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType","Transparent");material.renderQueue=3000;
                material.SetShaderPassEnabled("ShadowCaster",false);
            }
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),source.name),material);EditorUtility.SetDirty(material);
        }
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
    }
}
