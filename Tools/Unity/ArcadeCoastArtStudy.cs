using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>独立海岸美术样板；只保存备用目录场景与截图，不替换正式地图、不运行战斗或写入存档。</summary>
public static class ArcadeCoastArtStudy
{
    private const string Folder="Assets/Art/CoastStudy";
    private const string Generated=Folder+"/Generated";
    private static Transform root;
    private static Material sand,wetSand,stone,wood,iron,bark,leaf,leafLight,leafDry,fern,grass;
    private static GameObject rockSource,fernSource,grassSource;
    private static Mesh[] palms;
    private static System.Random random;
    private static readonly List<BoxCollider> dockPosts=new();
    private static BoxCollider deck;
    private static readonly List<Collider> borderColliders=new();

    public static string Run()
    {
        if(Application.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本项目编辑态样板制作");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("请先保留当前脏场景，不能用样板覆盖");
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        PrepareTextures();
        var previousScene=SceneManager.GetActiveScene();
        var previousTarget=RenderTexture.active;
        var previousPipeline=QualitySettings.renderPipeline;
        var previousShadowDistance=QualitySettings.shadowDistance;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        UniversalRenderPipelineAsset pipeline=null;
        random=new System.Random(51021);dockPosts.Clear();borderColliders.Clear();
        try
        {
            SceneManager.SetActiveScene(scene);
            root=new GameObject("CoastArtStudy_仅样板未接入正式地图").transform;
            Materials();
            var rockAsset=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/PolyHaven/boulder_01.fbx");
            rockSource=rockAsset!=null?rockAsset.GetComponentsInChildren<MeshFilter>().OrderByDescending(m=>m.name.Contains("LOD2")).First().gameObject:null;
            fernSource=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/PolyHaven/fern_02.fbx");
            grassSource=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Forest/GrassPlant02.fbx");
            if(rockSource==null||fernSource==null)throw new InvalidOperationException("海岸岩石或蕨类源模型未导入");
            Ground();
            Borders();
            Dock();
            Props();
            Vegetation();
            Actors();
            var camera=CameraAndLight();
            // 仅样板渲染临时提高阴影精度，结束后还原项目当前管线。
            var current=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(current!=null)
            {
                pipeline=Object.Instantiate(current);
                pipeline.shadowDistance=110;
                var serializedPipeline=new SerializedObject(pipeline);
                serializedPipeline.FindProperty("m_MainLightShadowmapResolution").intValue=4096;
                serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
                pipeline.shadowCascadeCount=4;
                pipeline.msaaSampleCount=4;
                QualitySettings.renderPipeline=pipeline;
            }
            QualitySettings.shadowDistance=110;
            Physics.SyncTransforms();
            string checks=CheckSample();
            var output=Path.GetFullPath("Temp/CoastArtStudy");Directory.CreateDirectory(output);
            File.WriteAllText(output+"/checks.txt",checks);
            // 相同真实几何给出全图和码头近景，不用后期图像代替场景。
            Capture(camera,output+"/coast-overview.png",new Vector3(1,39,-31),new Vector3(1,0,2),19.5f);
            Capture(camera,output+"/dock-detail.png",new Vector3(27,18,-9),new Vector3(16,0,7),8.2f);
            camera.transform.position=new Vector3(1,39,-31);camera.transform.LookAt(new Vector3(1,0,2));camera.orthographicSize=19.5f;
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene,Folder+"/CoastArtStudy.unity");
            File.WriteAllText(output+"/note.txt","Unity 编辑器真实样板渲染；正式地图未替换；未运行战斗；码头碰撞仅在样板内核对。\n"+checks);
            return output+"\n"+checks;
        }
        finally
        {
            RenderTexture.active=previousTarget;
            QualitySettings.renderPipeline=previousPipeline;QualitySettings.shadowDistance=previousShadowDistance;
            if(pipeline!=null)Object.DestroyImmediate(pipeline);
            SceneManager.SetActiveScene(previousScene);EditorSceneManager.CloseScene(scene,true);
        }
    }

    /// <summary>只配置新样板贴图的导入设置，保留第三方源缓存及正式资源。</summary>
    private static void PrepareTextures()
    {
        foreach(string path in AssetDatabase.FindAssets("t:Texture2D",new[]{Folder}).Select(AssetDatabase.GUIDToAssetPath))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            bool normal=path.Contains("_nor_gl_")||path.EndsWith("_N.png");
            bool changed=importer.maxTextureSize!=2048||importer.textureType!=(normal?TextureImporterType.NormalMap:TextureImporterType.Default);
            importer.maxTextureSize=2048;importer.wrapMode=TextureWrapMode.Repeat;
            importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=!normal;importer.alphaIsTransparency=!normal;
            importer.anisoLevel=8;
            if(changed)importer.SaveAndReimport();
        }
    }

    private static Material Lit(string name,Color tint,string texture=null,float smooth=.12f)
    {
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;
        m.SetColor("_BaseColor",tint);m.SetFloat("_Smoothness",smooth);
        if(texture!=null)
        {
            m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/PolyHaven/"+texture+"_diff_1k.jpg"));
            var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/PolyHaven/"+texture+"_nor_gl_1k.jpg");
            m.SetTexture("_BumpMap",normal);m.SetFloat("_BumpScale",.75f);m.EnableKeyword("_NORMALMAP");
            var ao=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/PolyHaven/"+texture+"_ao_1k.jpg");
            m.SetTexture("_OcclusionMap",ao);m.SetFloat("_OcclusionStrength",.75f);m.EnableKeyword("_OCCLUSIONMAP");
        }
        return Save(m,name);
    }
    private static void Materials()
    {
        sand=Lit("Sand",new Color(1,1,.95f),"coast_sand_01");
        wetSand=Lit("WetSand",new Color(.73f,.73f,.64f),"coast_sand_01",.28f);
        stone=Lit("CoastalRock",new Color(.65f,.72f,.84f),"boulder_01");
        wood=Lit("WeatheredWood",new Color(.96f,.94f,.89f),"weathered_brown_planks");
        iron=Lit("RustIron",new Color(.21f,.16f,.10f),null,.25f);
        bark=Lit("PalmBark",new Color(.60f,.47f,.30f),"weathered_brown_planks");
        leaf=Lit("PalmGreen",new Color(.13f,.28f,.035f),null,.3f);
        leafLight=Lit("PalmSunlit",new Color(.28f,.39f,.065f),null,.25f);
        leafDry=Lit("PalmDry",new Color(.43f,.34f,.13f),null,.12f);
        foreach(var m in new[]{leaf,leafLight,leafDry}){m.shader=Shader.Find("CoastStudy/Foliage");m.SetFloat("_Cull",0);EditorUtility.SetDirty(m);}
        fern=Lit("Fern",new Color(.78f,.93f,.62f),"fern_02");fern.shader=Shader.Find("CoastStudy/Foliage");fern.SetFloat("_Cull",0);
        var fernAlpha=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/PolyHaven/fern_02_diff_1k.png");
        if(fernAlpha!=null)fern.SetTexture("_BaseMap",fernAlpha);
        fern.SetFloat("_AlphaClip",1);fern.SetFloat("_Cutoff",.35f);fern.EnableKeyword("_ALPHATEST_ON");fern.renderQueue=2450;EditorUtility.SetDirty(fern);
        grass=Lit("Grass",new Color(.54f,.73f,.35f));grass.shader=Shader.Find("CoastStudy/Foliage");grass.SetFloat("_Cull",0);
        grass.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Forest/diffuseGrassPlant02.png"));
        grass.SetFloat("_AlphaClip",1);grass.SetFloat("_Cutoff",.42f);grass.EnableKeyword("_ALPHATEST_ON");
        grass.renderQueue=2450;EditorUtility.SetDirty(grass);
        palms=new[]{PalmMesh(0),PalmMesh(1),PalmMesh(2)};
    }

    private static float Shore(float z)=>14.8f+.9f*Mathf.Sin(z*.27f)+.42f*Mathf.Sin(z*.63f);
    private static float GroundY(float x,float z)
    {
        float noise=(Mathf.PerlinNoise(x*.38f+37,z*.38f+47)-.5f)*.09f;
        float shoreDistance=Shore(z)-x;
        return Mathf.Lerp(-.28f,.02f,Mathf.Clamp01(shoreDistance/2.2f))+noise;
    }
    private static void Ground()
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
        const int nx=160,nz=140;
        for(int j=0;j<=nz;j++)for(int i=0;i<=nx;i++)
        {
            float z=-38+j*76f/nz;
            float x=Mathf.Lerp(-44,Shore(z)+.1f,i/(float)nx);
            v.Add(new Vector3(x,GroundY(x,z),z));uv.Add(new Vector2(x/6,z/6));
        }
        for(int j=0;j<nz;j++)for(int i=0;i<nx;i++)
        {int a=j*(nx+1)+i;t.AddRange(new[]{a,a+nx+1,a+1,a+1,a+nx+1,a+nx+2});}
        MeshObject("SandTerrain",v,t,uv,sand,true);
        var water=new GameObject("SeaSurface",typeof(MeshFilter),typeof(MeshRenderer));water.transform.SetParent(root,false);
        water.transform.position=new Vector3(28,-.18f,0);
        var plane=PrimitiveMesh(PrimitiveType.Plane);water.GetComponent<MeshFilter>().sharedMesh=plane;
        water.transform.localScale=new Vector3(9,1,10);
        water.GetComponent<Renderer>().sharedMaterial=Save(new Material(Shader.Find("CoastStudy/Water")),"Sea");
        // 浅滩湿沙窄带覆盖地表边缘，接水面而不是绘制硬直线。
        v.Clear();t.Clear();uv.Clear();
        for(int j=0;j<=140;j++)
        {float z=-38+j*76f/140;for(int i=0;i<2;i++){float x=Shore(z)-(i==0?1.2f:.04f);v.Add(new Vector3(x,GroundY(x,z)+.008f,z));uv.Add(new Vector2(x/6,z/6));}}
        for(int j=0;j<140;j++){int a=j*2;t.AddRange(new[]{a,a+2,a+1,a+1,a+2,a+3});}
        MeshObject("WetSandEdge",v,t,uv,wetSand,false);
    }

    /// <summary>外围高岩壁围出战斗区，三个陆地缺口和东岸码头保持清楚；样板入口线不放装饰。</summary>
    private static bool Corridor(float x,float z)
    {
        return (x<-12&&Mathf.Abs(z)<3.7f)||(z>12&&Mathf.Abs(x+1)<3.7f)||
            (z<-12&&Mathf.Abs(x-1)<3.7f)||(x>11&&Mathf.Abs(z-7)<2.8f);
    }
    private static void Borders()
    {
        for(int i=0;i<42;i++)
        {
            float angle=(i/42f)*Mathf.PI*2;
            float x=Mathf.Cos(angle)*21.5f-2.5f;
            float z=Mathf.Sin(angle)*19;
            if(x>13||Corridor(x,z))continue;
            PlaceRock(new Vector3(x,0,z),Range(3.2f,5.1f),Range(2.1f,3.8f),true);
            if(i%2==0)PlaceRock(new Vector3(x-1,1.2f,z+1),Range(2.7f,4.1f),Range(1.8f,3),true);
            var outside=new Vector3(x,0,z).normalized*3.3f;
            if(!Corridor(x+outside.x,z+outside.z))PlaceRock(new Vector3(x+outside.x,.3f,z+outside.z),Range(4.1f,5.6f),Range(3,4.6f),true);
        }
        foreach(var dir in new[]{Vector3.left,Vector3.forward,Vector3.back})
        foreach(int side in new[]{-1,1})
        for(int i=0;i<4;i++)
        {
            var p=dir*(21+i*3.2f)+new Vector3(-dir.z,0,dir.x)*(side*5.1f);
            if(dir==Vector3.forward)p.x-=1;if(dir==Vector3.back)p.x+=1;
            PlaceRock(p,Range(3.1f,4.3f),Range(2.2f,3.8f),true);
        }
        for(int i=0;i<17;i++)
        {
            float z=-25+i*3.25f;if(Mathf.Abs(z-7)<4)continue;
            PlaceRock(new Vector3(Shore(z)+Range(-.7f,1.2f),-.22f,z),Range(1.7f,3.4f),Range(.7f,1.7f),true);
        }
        for(int i=0;i<100;i++)
        {
            float a=Range(0,Mathf.PI*2);float x=Mathf.Cos(a)*Range(16,24)-2;float z=Mathf.Sin(a)*Range(15,21);
            if(x>14||Corridor(x,z))continue;
            PlaceRock(new Vector3(x,0,z),Range(.13f,.54f),Range(.08f,.33f),false);
        }
    }
    private static void PlaceRock(Vector3 p,float width,float height,bool collision)
    {
        var go=new GameObject("CoastalBoulder");go.transform.SetParent(root,false);
        Object.Instantiate(rockSource,go.transform,false);
        go.transform.rotation=Quaternion.Euler(0,Range(0,360),0);
        Fit(go,p,width,height);
        foreach(var r in go.GetComponentsInChildren<Renderer>())r.sharedMaterials=Enumerable.Repeat(stone,r.sharedMaterials.Length).ToArray();
        foreach(var collider in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        if(collision)
        {
            var b=WorldBounds(go);var c=go.AddComponent<BoxCollider>();
            c.center=go.transform.InverseTransformPoint(b.center);
            c.size=new Vector3(b.size.x/go.transform.lossyScale.x,b.size.y/go.transform.lossyScale.y,b.size.z/go.transform.lossyScale.z)*.85f;
            borderColliders.Add(c);
        }
    }

    private static void Dock()
    {
        var pier=new GameObject("Dock_WalkableDeckAndSolidPosts").transform;pier.SetParent(root,false);
        for(int i=0;i<33;i++)
        {
            var b=Box(pier,"AgedPlank",new Vector3(13.5f+i*.32f,.12f+Range(-.018f,.018f),7),new Vector3(.29f,.24f,3.8f),wood,false);
            b.transform.rotation=Quaternion.Euler(0,Range(-.35f,.35f),Range(-1.3f,1.3f));
        }
        var floor=new GameObject("DeckCollision",typeof(BoxCollider));floor.transform.SetParent(pier,false);
        floor.transform.position=new Vector3(18.6f,.09f,7);deck=floor.GetComponent<BoxCollider>();deck.size=new Vector3(10.7f,.28f,3.8f);
        foreach(float x in new[]{13.7f,17.7f,21.8f,23.7f})foreach(float z in new[]{4.95f,9.05f})
        {
            var post=GameObject.CreatePrimitive(PrimitiveType.Cylinder);post.name="SolidDockPost";post.transform.SetParent(pier,false);
            post.transform.position=new Vector3(x,.48f,z);post.transform.localScale=new Vector3(.28f,1.12f,.28f);
            post.GetComponent<Renderer>().sharedMaterial=wood;
            Object.DestroyImmediate(post.GetComponent<Collider>());
            var collider=post.AddComponent<BoxCollider>();collider.size=new Vector3(1,2,1);dockPosts.Add(collider);
            Box(pier,"PostCap",new Vector3(x,1.63f,z),new Vector3(.36f,.08f,.36f),wood,false);
        }
        foreach(float z in new[]{5.4f,8.6f})Box(pier,"SupportBeam",new Vector3(18.5f,-.12f,z),new Vector3(10.6f,.27f,.22f),wood,false);
    }
    private static void Props()
    {
        foreach(var p in new[]{new Vector3(11.1f,.52f,10),new Vector3(12.4f,.6f,11.1f),new Vector3(13.2f,.5f,-5),new Vector3(-12,.5f,-13)})
        {
            var crate=Box(root,"SolidCrate",p,new Vector3(1.15f,1.15f,1.15f),wood,true);
            crate.transform.rotation=Quaternion.Euler(0,Range(-18,18),0);
            foreach(float x in new[]{-.43f,.43f})Box(crate.transform,"IronStrap",new Vector3(x/1.15f,0,0),new Vector3(.05f,1.02f,1.02f),iron,false,true);
        }
        for(int i=0;i<3;i++)
        {
            var barrel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);barrel.name="SolidRustBarrel";barrel.transform.SetParent(root,false);
            barrel.transform.position=new Vector3(i==0?11.5f:-16.5f+i,.62f,i==0?-1.5f:12+i);
            barrel.transform.localScale=new Vector3(.8f,.62f,.8f);barrel.GetComponent<Renderer>().sharedMaterial=iron;
        }
    }

    private static void Vegetation()
    {
        var positions=new[]{new Vector3(-20,0,15),new Vector3(-10,1,19),new Vector3(6,1,18),new Vector3(11,0,14),new Vector3(-22,0,6),new Vector3(-24,0,-8),new Vector3(-17,0,-16),new Vector3(-9,0,-21),new Vector3(10,0,-17),new Vector3(13,0,-11),new Vector3(18,0,20),new Vector3(-27,0,22)};
        for(int i=0;i<positions.Length;i++)
        {
            var go=new GameObject("DetailedPalm",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
            go.transform.position=positions[i];go.transform.rotation=Quaternion.Euler(0,i*137,0);
            go.transform.localScale=Vector3.one*Range(.8f,1.15f);
            go.GetComponent<MeshFilter>().sharedMesh=palms[i%palms.Length];
            go.GetComponent<MeshRenderer>().sharedMaterials=new[]{bark,leaf,leafLight,leafDry};
        }
        for(int i=0;i<450;i++)
        {
            float a=Range(0,Mathf.PI*2);float x=Mathf.Cos(a)*Range(16.2f,28)-2;float z=Mathf.Sin(a)*Range(15.5f,26);
            if(x>14||Corridor(x,z))continue;
            var go=new GameObject("BorderUnderstory");go.transform.SetParent(root,false);
            var source=i%3==0&&grassSource!=null?grassSource:fernSource.GetComponentsInChildren<MeshFilter>()[i%4].gameObject;
            Object.Instantiate(source,go.transform,false);
            go.transform.rotation=Quaternion.Euler(0,Range(0,360),0);
            Fit(go,new Vector3(x,GroundY(x,z),z),Range(.8f,2.2f),Range(.4f,1.25f));
            foreach(var r in go.GetComponentsInChildren<Renderer>())r.sharedMaterials=Enumerable.Repeat(i%3==0?grass:fern,r.sharedMaterials.Length).ToArray();
        }
    }

    /// <summary>自制弯曲棕榈网格包含细叶片和树干环节，仅样板使用，不替换现有模型。</summary>
    private static Mesh PalmMesh(int variant)
    {
        var v=new List<Vector3>();var uv=new List<Vector2>();var indices=new[]{new List<int>(),new List<int>(),new List<int>(),new List<int>()};
        float height=6.6f+variant*.6f,bend=.8f+variant*.28f;
        const int rings=44,sides=12;
        for(int j=0;j<=rings;j++)for(int i=0;i<=sides;i++)
        {
            float t=j/(float)rings,a=i/(float)sides*Mathf.PI*2;
            float r=Mathf.Lerp(.25f,.105f,t)*(j%2==0?1.07f:.97f);
            v.Add(new Vector3(bend*t*t+Mathf.Cos(a)*r,height*t,Mathf.Sin(a)*r));uv.Add(new Vector2(i/(float)sides,j*.18f));
        }
        for(int j=0;j<rings;j++)for(int i=0;i<sides;i++)
        {int n=j*(sides+1)+i;indices[0].AddRange(new[]{n,n+1,n+sides+1,n+1,n+sides+2,n+sides+1});}
        var crown=new Vector3(bend,height,0);
        for(int frond=0;frond<15;frond++)
        {
            float angle=(frond*137.5f+variant*41)*Mathf.Deg2Rad;
            var dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var side=new Vector3(-dir.z,0,dir.x);
            float length=3.5f+(frond%4)*.32f;float lift=frond<4?1.3f:.6f;
            Func<float,Vector3> spine=t=>crown+dir*(t*length)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*lift-t*t*(frond<4?.6f:2));
            for(int j=1;j<28;j++)
            {
                float t=j/29f;float spread=Mathf.Pow(Mathf.Sin(t*Mathf.PI),.7f)*.68f;
                foreach(int sign in new[]{-1,1})
                {
                    var start=spine(t);var end=spine(Mathf.Min(1,t+.12f))+side*(spread*sign)+Vector3.down*(.13f+spread*.16f);
                    var middle=Vector3.Lerp(start,end,.45f)+Vector3.up*.045f;
                    var across=Vector3.Cross((end-start).normalized,Vector3.up)*.045f;
                    int n=v.Count;v.AddRange(new[]{start,middle+across,end,middle-across});
                    uv.AddRange(new[]{new Vector2(0,0),new Vector2(.45f,0),new Vector2(1,0),new Vector2(.45f,1)});
                    int material=frond>13?3:frond%3==0?2:1;
                    indices[material].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                }
            }
        }
        var mesh=new Mesh{name="DetailedPalm"+variant};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.subMeshCount=4;
        for(int i=0;i<4;i++)mesh.SetTriangles(indices[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();
        return Save(mesh,mesh.name);
    }

    private static void Actors()
    {
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        if(player!=null)
        {
            var p=Object.Instantiate(player,root);p.name="PlayerScaleReference";p.transform.position=new Vector3(-1,.03f,-1);p.transform.rotation=Quaternion.Euler(0,80,0);
            foreach(var component in p.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
            var animator=p.GetComponentInChildren<Animator>();
            if(animator!=null&&animator.runtimeAnimatorController!=null)
            {var clip=animator.runtimeAnimatorController.animationClips.FirstOrDefault(c=>c.name.Contains("Idle"));if(clip!=null)clip.SampleAnimation(animator.gameObject,.2f);animator.enabled=false;}
        }
        var zombie=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");
        if(zombie!=null)foreach(var point in new[]{new Vector3(-12,0,-5),new Vector3(-8,0,9),new Vector3(-2,0,13),new Vector3(8,0,8),new Vector3(7,0,-7),new Vector3(1,0,-11)})
        {
            var go=Object.Instantiate(zombie,root);go.name="ZombieScaleReference";go.transform.position=point+Vector3.up*.05f;
            go.transform.LookAt(new Vector3(-1,.05f,-1));
            foreach(var c in go.GetComponentsInChildren<MonoBehaviour>(true))c.enabled=false;
            foreach(var a in go.GetComponentsInChildren<Animator>())a.enabled=false;
        }
    }
    private static Camera CameraAndLight()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.40f,.49f,.57f);
        RenderSettings.ambientEquatorColor=new Color(.28f,.32f,.26f);
        RenderSettings.ambientGroundColor=new Color(.15f,.13f,.08f);
        RenderSettings.ambientIntensity=.9f;
        RenderSettings.fog=false;
        var light=new GameObject("WarmAfternoonSun",typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(root,false);light.type=LightType.Directional;light.color=new Color(1,.95f,.86f);light.intensity=1.75f;
        light.transform.rotation=Quaternion.Euler(43,-42,0);light.shadows=LightShadows.Soft;light.shadowStrength=.85f;
        light.shadowBias=.025f;light.shadowNormalBias=.12f;RenderSettings.sun=light;
        var camera=new GameObject("StudyCamera",typeof(Camera)).GetComponent<Camera>();camera.transform.SetParent(root,false);
        camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.23f,.27f);camera.allowHDR=true;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var volume=new GameObject("StudyColorGrade",typeof(Volume)).GetComponent<Volume>();volume.transform.SetParent(root,false);volume.isGlobal=true;
        var profile=ScriptableObject.CreateInstance<VolumeProfile>();
        var tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
        var color=profile.Add<ColorAdjustments>();color.postExposure.Override(.2f);color.contrast.Override(10);color.saturation.Override(5);
        var bloom=profile.Add<Bloom>();bloom.intensity.Override(.11f);bloom.threshold.Override(1.3f);
        volume.sharedProfile=Save(profile,"CoastColorGrade");
        return camera;
    }

    /// <summary>限定样板几何核对，不把此结果当作正式移动系统或实机验收。</summary>
    private static string CheckSample()
    {
        bool floor=deck.Raycast(new Ray(new Vector3(19,3,7),Vector3.down),out var hit,5)&&hit.point.y>.1f;
        bool posts=dockPosts.Count==8&&dockPosts.All(c=>c.enabled&&!c.isTrigger&&c.size.x>0);
        var channels=new[]{new Vector3(-24,.5f,0),new Vector3(-1,.5f,25),new Vector3(1,.5f,-25)};
        bool clear=channels.All(p=>borderColliders.All(c=>!c.bounds.Contains(p)));
        if(!floor||!posts||!clear)throw new InvalidOperationException("样板几何核对失败：deck="+floor+" posts="+posts+" threeLandChannels="+clear);
        return "样板几何：木板顶部向下射线命中="+floor+"；8根实体木桩="+posts+"；3条陆路中心未被岩石碰撞覆盖="+clear+"。正式地图/敌人路线未修改。";
    }
    private static void Capture(Camera camera,string path,Vector3 position,Vector3 target,float size)
    {
        camera.transform.position=position;camera.transform.LookAt(target);camera.orthographicSize=size;
        var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.antiAliasing=4;
        var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);}
    }
    private static float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    private static Bounds WorldBounds(GameObject go)
    {var renderers=go.GetComponentsInChildren<Renderer>();var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;}
    private static void Fit(GameObject go,Vector3 p,float width,float height)
    {
        var b=WorldBounds(go);go.transform.localScale=Vector3.Scale(go.transform.localScale,new Vector3(width/Mathf.Max(.001f,b.size.x),height/Mathf.Max(.001f,b.size.y),width/Mathf.Max(.001f,b.size.z)));
        b=WorldBounds(go);go.transform.position+=new Vector3(p.x-b.center.x,p.y-b.min.y,p.z-b.center.z);
    }
    private static GameObject Box(Transform parent,string name,Vector3 p,Vector3 size,Material material,bool collision,bool local=false)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
        if(local)go.transform.localPosition=p;else go.transform.position=p;
        go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    private static Mesh PrimitiveMesh(PrimitiveType type)
    {var go=GameObject.CreatePrimitive(type);var mesh=go.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(go);return mesh;}
    private static void MeshObject(string name,List<Vector3> v,List<int> t,List<Vector2> uv,Material material,bool collision)
    {
        var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh=Save(mesh,name);var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<Renderer>().sharedMaterial=material;
        if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
    private static T Save<T>(T asset,string name)where T:Object
    {
        string path=Generated+"/"+name+(asset is Material?".mat":".asset");
        var prior=AssetDatabase.LoadAssetAtPath<T>(path);
        if(prior==null){AssetDatabase.CreateAsset(asset,path);return asset;}
        EditorUtility.CopySerialized(asset,prior);EditorUtility.SetDirty(prior);Object.DestroyImmediate(asset);return prior;
    }
}
