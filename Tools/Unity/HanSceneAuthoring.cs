using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>离线装配汉末运输现场；只更新生成 Kit 与正式美术副本，不修改场景、剧情配置或存档。</summary>
public static class HanSceneAuthoring
{
    private const string KitPath="Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab";
    private const string Art="Assets/Game/Art/Environment/CivilisationExpedition";
    private const string Story=Art+"/HanStory";
    private const string Items="Assets/Game/Art/Civilisation/HanItems";
    private static Material wood,cord,stone,ash,paper,clay,cargoWood;
    private static void Folder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    public static string Run()
    {
        PrepareAssets();
        return DressKit(false);
    }
    /// <summary>本轮只精修运输遗迹；六个搜索点和另一类线索模板保持原样。</summary>
    public static string RunTransport()
    {
        PrepareAssets();
        return DressKit(true);
    }
    /// <summary>只替换运输车造型，保持周围已验收的布景、地面痕迹与交互引用。</summary>
    public static string RunCartOnly()
    {
        PrepareAssets();
        var root=PrefabUtility.LoadPrefabContents(KitPath);
        try
        {
            var kit=root.GetComponent<ValleyGenerationKit>();
            var view=(CivilisationPointView)new SerializedObject(kit).FindProperty("transportClueTemplate").objectReferenceValue;
            var story=view.transform.Find("StoryScene");var old=story.Find("StrandedCart");
            if(old==null)throw new InvalidOperationException("运输遗迹缺少货车节点");
            var position=old.localPosition;Object.DestroyImmediate(old.gameObject);
            Model(story,Story+"/Models/HanBrokenCart.fbx","StrandedCart",position,3.6f,new Vector3(0,-18,0));
            PrefabUtility.SaveAsPrefabAsset(root,KitPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();return "Replaced transport cart only with original Blender model";
    }
    private static void PrepareAssets()
    {
        Folder(Story+"/Models");Folder(Story+"/Materials");
        const string wagon=Story+"/Models/HanBrokenCart.fbx";
        if(AssetDatabase.LoadMainAssetAtPath(wagon)==null)throw new Exception("请先执行 Tools/Art/HanCart/generate.py 生成货车模型");
        var importer=(ModelImporter)AssetImporter.GetAtPath(wagon);
        foreach(var key in importer.GetExternalObjectMap().Keys.ToArray())importer.RemoveRemap(key);
        var names=new[]{"CartWeatheredWood","CartDarkWood","CartSplitWood","CartForgedIron","CartRope"};
        var colors=new[]{new Color(.73f,.64f,.51f),new Color(.43f,.36f,.27f),new Color(.64f,.47f,.28f),new Color(.14f,.16f,.15f),new Color(.42f,.33f,.20f)};
        for(int i=0;i<names.Length;i++)
        {
            var material=Mat(names[i],colors[i]);material.SetFloat("_Metallic",i==3?.65f:0);material.SetFloat("_Smoothness",i==3?.24f:.12f);
            if(i<2)material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Story+"/Textures/CartWood.png"));
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),names[i]),material);
        }
        importer.SaveAndReimport();
        wood=AssetDatabase.LoadAssetAtPath<Material>(Items+"/Materials/HanWood.mat");cord=AssetDatabase.LoadAssetAtPath<Material>(Items+"/Materials/HanCord.mat");clay=AssetDatabase.LoadAssetAtPath<Material>(Items+"/Materials/HanClay.mat");
        stone=Mat("WornStone",new Color(.34f,.35f,.31f));ash=Mat("ColdAsh",new Color(.095f,.085f,.07f));paper=Mat("RecordCloth",new Color(.57f,.48f,.29f));
        var cargoPath=Story+"/Materials/WeatheredCargo.mat";cargoWood=AssetDatabase.LoadAssetAtPath<Material>(cargoPath);
        if(cargoWood==null){cargoWood=new Material(AssetDatabase.LoadAssetAtPath<Material>(Art+"/Sources/RPGPP_LT/Materials/rpgpp_lt_mat_a.mat"));AssetDatabase.CreateAsset(cargoWood,cargoPath);}
        cargoWood.SetColor("_BaseColor",new Color(.66f,.61f,.54f));EditorUtility.SetDirty(cargoWood);
    }
    private static string DressKit(bool transportOnly)
    {
        var root=PrefabUtility.LoadPrefabContents(KitPath);
        try
        {
            var kit=root.GetComponent<ValleyGenerationKit>();
            var ids=new[]{"han.iron-tools","han.steel-blade","han.bow","han.grain","han.pottery","han.proof"};
            if(!transportOnly)foreach(var id in ids)Dress(kit.FindTemplate(id),id);
            var serialized=new SerializedObject(kit);
            foreach(var pair in new[]{("transportClueTemplate","TransportTrace"),("trailClueTemplate","TrailTrace")})
            {
                if(transportOnly&&pair.Item1!="transportClueTemplate")continue;
                var property=serialized.FindProperty(pair.Item1);
                var view=property.objectReferenceValue as CivilisationPointView;
                if(view==null){var go=Object.Instantiate(kit.PreparationTemplate.gameObject,root.transform);go.name=pair.Item2;view=go.GetComponent<CivilisationPointView>();property.objectReferenceValue=view;}
                Dress(view,pair.Item2);view.gameObject.SetActive(false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if(!kit.Validate(out var error))throw new Exception(error);
            PrefabUtility.SaveAsPrefabAsset(root,KitPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        if(AssetDatabase.GetDependencies(KitPath).Any(p=>p.StartsWith("Assets/Art/")))throw new Exception("正式 Kit 仍含备用资源依赖");
        return transportOnly?"Refined transport scene only":"Dressed six chapter search scenes and two route clue templates";
    }
    private static Material Mat(string name,Color color)
    {
        var path=Story+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.12f);EditorUtility.SetDirty(mat);return mat;
    }
    private static Transform Group(Transform parent,string name,Vector3 pos)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=pos;return go.transform;}
    private static GameObject Shape(Transform parent,string name,PrimitiveType type,Vector3 pos,Vector3 size,Material mat,Vector3 angles=default)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;go.transform.localEulerAngles=angles;
        Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;return go;
    }
    /// <summary>把完整模型归一到底面原点；每个道具组在生成时独立贴地，堆放的局部高度保留。</summary>
    private static Transform Model(Transform parent,string path,string name,Vector3 pos,float extent,Vector3 angles=default)
    {
        var group=Group(parent,name,pos);var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(asset==null)throw new Exception(path);
        var model=Object.Instantiate(asset,group);model.transform.localRotation=Quaternion.Euler(angles)*model.transform.localRotation;
        foreach(var component in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(component);
        foreach(var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(behaviour);
        var renderers=model.GetComponentsInChildren<Renderer>(true);
        foreach(var r in renderers)
        {
            if(path.Contains("TransportCart"))r.sharedMaterials=r.sharedMaterials.Select(m=>cargoWood).ToArray();
        }
        Bounds Bounds(){var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);return b;}
        var bounds=Bounds();model.transform.localScale*=extent/Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));bounds=Bounds();
        model.transform.position+=new Vector3(group.position.x-bounds.center.x,group.position.y-bounds.min.y,group.position.z-bounds.center.z);
        return group;
    }
    private static void Sack(Transform root,Vector3 pos,float size=.65f)
        =>Model(root,Art+"/Sources/RPGPP_LT/Prefabs/Props/Containers/rpgpp_lt_sack_01.prefab","TiedCargo",pos,size);
    private static void Pot(Transform root,Vector3 pos,float size=.55f)
        =>Model(root,Items+"/HanWaterPot.prefab","ClayVessel",pos,size);
    private static Transform Log(Transform root,Vector3 pos,float length=1.1f)
    {
        var group=Group(root,"RepairTimber",pos);
        Model(group,"Assets/Game/Art/Environment/BaseCamp/Sources/RPGPP_LT/Models/Props/rpgpp_lt_log_wood_01.fbx","WeatheredTimber",Vector3.zero,length);
        foreach(var renderer in group.GetComponentsInChildren<Renderer>())renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>cargoWood).ToArray();
        return group;
    }
    private static float Top(Transform group)=>group.GetComponentsInChildren<Renderer>(true).Max(r=>r.bounds.max.y)-group.position.y;
    private static void Hearth(Transform root,Vector3 pos)
    {
        var group=Group(root,"ColdHearth",pos);
        Shape(group,"Ash",PrimitiveType.Cylinder,new Vector3(0,.015f,0),new Vector3(.85f,.015f,.85f),ash);
        for(int i=0;i<9;i++){float angle=i*Mathf.PI*2/9;var rock=Model(group,Art+"/Scenery/RiverRock.prefab","HearthStone",new Vector3(Mathf.Cos(angle)*.47f,0,Mathf.Sin(angle)*.47f),.25f,new Vector3(0,i*40,0));foreach(var r in rock.GetComponentsInChildren<Renderer>())r.sharedMaterial=stone;}
        Shape(group,"BurntWood",PrimitiveType.Cube,new Vector3(0,.07f,0),new Vector3(.64f,.09f,.11f),ash,new Vector3(0,35,0));
        Shape(group,"BurntWood",PrimitiveType.Cube,new Vector3(0,.1f,0),new Vector3(.55f,.09f,.11f),ash,new Vector3(0,-45,0));
    }
    private static void Record(Transform root)
    {
        Shape(root,"ClothBacking",PrimitiveType.Cube,new Vector3(0,.02f,0),new Vector3(.34f,.035f,.24f),paper);
        for(int i=0;i<7;i++)Shape(root,"RecordSlip",PrimitiveType.Cube,new Vector3(-.15f+i*.05f,.043f,0),new Vector3(.044f,.025f,.23f),cord);
        foreach(float z in new[]{-.075f,.075f})Shape(root,"Binding",PrimitiveType.Cube,new Vector3(0,.06f,z),new Vector3(.36f,.012f,.014f),wood);
    }
    /// <summary>围绕受损货车组织散落方向和可接近入口；地面薄片由地图生成器逐顶点贴地。</summary>
    private static void Transport(Transform story,Transform owner)
    {
        var ground=Group(owner,"GroundSurface",Vector3.zero);
        var dirt=SoilMaterial("TrampledEarth",new Color(.43f,.33f,.21f),.87f);
        var rut=SoilMaterial("WheelRut",new Color(.19f,.145f,.09f),.86f);
        var grain=Mat("SpilledGrain",new Color(.66f,.52f,.27f));
        var paleWood=Mat("BrokenWood",new Color(.48f,.34f,.19f));
        var grid=SurfaceGrid();
        Patch(ground,"WornClearing",new Vector3(-.45f,.025f,.4f),new Vector2(6.7f,5.9f),-15,dirt,grid);
        Patch(ground,"Approach",new Vector3(.5f,.026f,-2.8f),new Vector2(2.9f,5.8f),-16,dirt,grid);
        Patch(ground,"CargoDrag",new Vector3(1.1f,.03f,.3f),new Vector2(3.7f,1.2f),32,dirt,grid);
        // 两条车辙在车前汇入现场，末端偏折表现失衡；拖拽痕迹通向散落货袋。
        for(int i=0;i<9;i++)
        {
            float z=-4.6f+i*.55f;float x=.9f-(z+4.6f)*.25f;
            foreach(float side in new[]{-.66f,.66f})Patch(ground,"WheelTrack",new Vector3(x+side,.042f,z),new Vector2(.17f,.82f),-13,rut,grid);
        }
        for(int i=0;i<5;i++)Patch(ground,"DragScrape",new Vector3(.25f+i*.37f,.046f,.2f+i*.17f),new Vector2(.3f,.7f),65,rut,grid);
        for(int i=0;i<7;i++)Patch(ground,"Footfall",new Vector3(1.5f+(i%2)*.28f,.048f,-3.1f+i*.43f),new Vector2(.16f,.3f),i%2==0?-12:8,rut,grid);

        Model(story,Story+"/Models/HanBrokenCart.fbx","StrandedCart",new Vector3(-1.15f,0,.95f),3.6f,new Vector3(0,-18,0));
        // 断轴与尖碎片连成同一破口，避免一根完整方木充当损坏痕迹。
        var axle=Group(story,"BrokenShaft",new Vector3(-.1f,0,-.5f));
        Shape(axle,"SnappedAxle",PrimitiveType.Cube,new Vector3(0,.095f,0),new Vector3(1.05f,.15f,.16f),paleWood,new Vector3(0,-23,4));
        for(int i=0;i<5;i++)Shape(axle,"SplitWood",PrimitiveType.Cube,new Vector3(.42f+i*.08f,.055f,-.13f+i*.055f),new Vector3(.38f-i*.04f,.035f,.04f),paleWood,new Vector3(0,-28+i*13,7));
        Model(story,Items+"/HanGrainPouch.prefab","FallenCargo",new Vector3(.5f,0,.8f),.83f,new Vector3(25,65,75));
        Model(story,Items+"/HanGrainPouch.prefab","DraggedCargo",new Vector3(1.65f,0,1.3f),.68f,new Vector3(0,-32,16));
        Model(story,Items+"/HanGrainPouch.prefab","CargoByWheel",new Vector3(-2f,0,-.1f),.65f,new Vector3(15,15,85));
        var spill=Group(story,"GrainSpill",new Vector3(.88f,0,.22f));
        var random=new System.Random(472);
        for(int i=0;i<38;i++)
        {
            float angle=(float)random.NextDouble()*Mathf.PI*2,radius=Mathf.Sqrt((float)random.NextDouble())*.52f;
            Shape(spill,"Grain",PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*radius,.028f,Mathf.Sin(angle)*radius*.55f),new Vector3(.035f,.025f,.06f),grain,new Vector3(0,i*47,0));
        }
        Pot(story,new Vector3(1.8f,0,-.65f),.5f);
        var fragments=Group(story,"BrokenPacking",new Vector3(1.45f,0,-.1f));
        for(int i=0;i<7;i++)Shape(fragments,"ClayShard",PrimitiveType.Cube,new Vector3((i%3)*.13f,.025f,(i/3)*.15f),new Vector3(.1f,.035f,.14f),clay,new Vector3(8,i*37,12));
        var cloth=Group(story,"TornWrapping",new Vector3(.1f,0,1.75f));
        Shape(cloth,"FoldedCloth",PrimitiveType.Cube,new Vector3(0,.025f,0),new Vector3(.85f,.04f,.48f),paper,new Vector3(0,23,0));
        for(int i=0;i<3;i++)Shape(cloth,"PackingSlat",PrimitiveType.Cube,new Vector3(-.27f+i*.19f,.06f,.06f),new Vector3(.065f,.025f,.62f),paleWood,new Vector3(0,20+i*8,0));

        // 后侧与两翼形成不对称边缘，朝车辙的一侧保持开口，不围成规则花坛。
        var edges=new[]{new Vector3(-3,0,1.7f),new Vector3(-2.3f,0,3.1f),new Vector3(-.5f,0,3.3f),new Vector3(1.5f,0,2.9f),new Vector3(2.8f,0,1.2f),new Vector3(-3.2f,0,-.7f)};
        for(int i=0;i<edges.Length;i++)
        {
            TintFoliage(Model(story,Art+"/Vegetation/Bush_0"+(i%3+1)+".prefab","EdgeShrub",edges[i],1.2f+i%2*.55f,new Vector3(0,i*73,0)));
            Model(story,Art+"/Scenery/RiverRock.prefab","EdgeRock",edges[i]+new Vector3(.4f,0,-.32f),.55f+i%3*.18f,new Vector3(0,i*53,0));
        }
        for(int i=0;i<25;i++)
        {
            float angle=.15f+i*2.399f,radius=2.6f+(i%4)*.22f;
            var pos=new Vector3(-.3f+Mathf.Cos(angle)*radius,0,.45f+Mathf.Sin(angle)*radius);
            if(pos.z<-.9f&&pos.x>-.7f&&pos.x<2.1f)continue;
            TintFoliage(Model(story,Art+"/Vegetation/Grass_02.prefab","VergeGrass",pos,.4f+(i%3)*.18f,new Vector3(0,i*63,0)));
        }
        var fallen=Group(story,"FallenBranch",new Vector3(-1.1f,0,2.7f));
        Shape(fallen,"WeatheredTrunk",PrimitiveType.Cylinder,new Vector3(0,.18f,0),new Vector3(.27f,1.18f,.27f),wood,new Vector3(0,0,83));
        Shape(fallen,"SplitBranch",PrimitiveType.Cylinder,new Vector3(.3f,.17f,.27f),new Vector3(.1f,.5f,.1f),paleWood,new Vector3(60,35,0));
    }
    /// <summary>生成带不规则羽化边缘的泥土材质；纹理与网格均保存为正式资源。</summary>
    private static Material SoilMaterial(string name,Color tint,float opacity)
    {
        Folder(Story+"/Textures");
        var path=Story+"/Textures/SoilEdge.png";
        {
            var texture=new Texture2D(256,256,TextureFormat.RGBA32,false);var pixels=new Color[256*256];
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                float u=(x+.5f)/256,v=(y+.5f)/256;
                float radius=new Vector2((u-.5f)*2,(v-.5f)*2).magnitude;
                float noise=Mathf.PerlinNoise(u*19+7,v*19+3);
                float edge=Mathf.Clamp01((1-radius+(.5f-noise)*.18f)*5);
                float shade=.93f+.07f*Mathf.PerlinNoise(u*47,v*47);
                pixels[y*256+x]=new Color(shade,shade,shade,Mathf.SmoothStep(0,1,edge));
            }
            texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
        }
        var mat=Mat(name,new Color(tint.r,tint.g,tint.b,opacity));mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);mat.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);mat.SetFloat("_ZWrite",0);mat.SetFloat("_Cull",0);
        mat.SetFloat("_BlendModePreserveSpecular",0);mat.SetFloat("_SpecularHighlights",0);mat.SetFloat("_EnvironmentReflections",0);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetOverrideTag("RenderType","Transparent");mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.renderQueue=3000;mat.SetShaderPassEnabled("ShadowCaster",false);EditorUtility.SetDirty(mat);return mat;
    }
    private static void TintFoliage(Transform root)
    {
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
        {
            var path=Story+"/Materials/Transport_"+source.name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",source.GetColor("_BaseColor")*new Color(.53f,.62f,.42f,1));EditorUtility.SetDirty(material);return material;
        }).ToArray();
    }
    private static Mesh SurfaceGrid()
    {
        var path=Story+"/Models/SurfaceGrid.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null)return existing;
        const int n=16;var vertices=new Vector3[(n+1)*(n+1)];var uv=new Vector2[vertices.Length];var triangles=new int[n*n*6];int cursor=0;
        for(int z=0;z<=n;z++)for(int x=0;x<=n;x++){int i=z*(n+1)+x;uv[i]=new Vector2((float)x/n,(float)z/n);vertices[i]=new Vector3(uv[i].x-.5f,0,uv[i].y-.5f);}
        for(int z=0;z<n;z++)for(int x=0;x<n;x++){int a=z*(n+1)+x;foreach(int i in new[]{a,a+n+1,a+1,a+1,a+n+1,a+n+2})triangles[cursor++]=i;}
        var mesh=new Mesh{name="TransportSurfaceGrid",vertices=vertices,uv=uv,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    private static void Patch(Transform root,string name,Vector3 position,Vector2 size,float yaw,Material material,Mesh grid)
    {
        var patch=Group(root,name,position);patch.localEulerAngles=new Vector3(0,yaw,0);patch.localScale=new Vector3(size.x,1,size.y);
        patch.gameObject.AddComponent<MeshFilter>().sharedMesh=grid;var renderer=patch.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    /// <summary>保留交互组件和名称节点，静态现场与可耗尽证物分离；重复运行替换本工具拥有的视觉节点。</summary>
    private static void Dress(CivilisationPointView view,string id)
    {
        var nameRoot=new SerializedObject(view).FindProperty("nameRoot").objectReferenceValue as GameObject;
        foreach(Transform child in view.transform.Cast<Transform>().ToArray())if(child.gameObject!=nameRoot)Object.DestroyImmediate(child.gameObject);
        var story=Group(view.transform,"StoryScene",Vector3.zero);
        var evidence=Group(view.transform,"EvidenceRoot",new Vector3(0,.055f,0));Record(evidence);
        var so=new SerializedObject(view);so.FindProperty("harvestedVisualRoot").objectReferenceValue=evidence.gameObject;so.ApplyModifiedPropertiesWithoutUndo();
        if(id=="TransportTrace")
        {
            Transport(story,view.transform);
        }
        else if(id=="TrailTrace"||id=="han.proof")
        {
            Hearth(story,new Vector3(-1.4f,0,.55f));Sack(story,new Vector3(.9f,0,.7f),.75f);Sack(story,new Vector3(1.4f,0,.3f),.55f);Pot(story,new Vector3(.6f,0,-.75f));Log(story,new Vector3(-.3f,0,1.65f),1.3f);
        }
        else if(id=="han.iron-tools")
        {
            var log=Log(story,new Vector3(-.65f,0,.5f),1.4f);
            Model(log,"Assets/Game/Prefabs/Characters/Weapons/HanForgedAxe.prefab","RepairAxe",new Vector3(0,Top(log)+.015f,0),.85f,new Vector3(90,0,25));
            Model(story,"Assets/Game/Prefabs/Characters/Weapons/HanForgedPick.prefab","RepairPick",new Vector3(.8f,0,.5f),.85f,new Vector3(90,0,-25));Sack(story,new Vector3(.85f,0,-.65f));
        }
        else if(id=="han.steel-blade")
        {
            var station=Group(story,"SharpeningBench",new Vector3(-.45f,0,.6f));
            Shape(station,"WornStone",PrimitiveType.Cube,new Vector3(0,.15f,0),new Vector3(.9f,.3f,.6f),stone,new Vector3(0,12,0));
            Model(station,"Assets/Game/Prefabs/Characters/Weapons/HanSteelBlade.prefab","BladeBeingRepaired",new Vector3(0,.3f,0),.95f,new Vector3(0,65,0));
            Shape(station,"Whetstone",PrimitiveType.Cube,new Vector3(.34f,.34f,-.18f),new Vector3(.16f,.06f,.08f),stone);Sack(story,new Vector3(.9f,0,.35f));Pot(story,new Vector3(.65f,0,-.7f),.38f);
        }
        else if(id=="han.bow")
        {
            var bench=Log(story,new Vector3(-.55f,0,.65f),1.5f);Model(bench,"Assets/Game/Prefabs/Characters/Weapons/HanBow.prefab","BowRepair",new Vector3(0,Top(bench)+.015f,0),1.2f,new Vector3(90,0,80));
            var bundle=Group(story,"ArrowShafts",new Vector3(.9f,0,.35f));for(int i=0;i<5;i++)Shape(bundle,"WoodShaft",PrimitiveType.Cylinder,new Vector3(i*.055f,.05f,0),new Vector3(.025f,.45f,.025f),cord,new Vector3(90,0,15));
            Sack(story,new Vector3(.55f,0,-.85f),.5f);
        }
        else if(id=="han.grain")
        {
            Hearth(story,new Vector3(-1.3f,0,.65f));Sack(story,new Vector3(.8f,0,.8f),.85f);
            Model(story,Items+"/HanGrainPouch.prefab","PreparedMeal",new Vector3(-.5f,0,-.7f),.55f);
            Model(story,Art+"/Sources/RPGPP_LT/Prefabs/Props/Containers/rpgpp_lt_basket_01.prefab","FoodBasket",new Vector3(.75f,0,-.5f),.65f);Pot(story,new Vector3(-.15f,0,1.1f));
        }
        else if(id=="han.pottery")
        {
            Pot(story,new Vector3(-.6f,0,.6f),.8f);Pot(story,new Vector3(.45f,0,.75f),.65f);Pot(story,new Vector3(.8f,0,-.3f),.4f);Sack(story,new Vector3(-.85f,0,-.5f),.55f);
            var shards=Group(story,"PotteryFragments",new Vector3(.1f,0,-.6f));for(int i=0;i<4;i++)Shape(shards,"ClayShard",PrimitiveType.Cube,new Vector3(i*.15f,.025f,i%2*.12f),new Vector3(.14f,.035f,.12f),clay,new Vector3(10,i*39,15));
        }
        if(id=="TransportTrace"||id=="TrailTrace")Object.DestroyImmediate(evidence.gameObject);
    }
}
