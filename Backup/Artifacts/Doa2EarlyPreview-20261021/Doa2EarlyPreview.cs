using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// 仅生成独立临时布局及动作预览，不创建正式资源、不运行游戏规则、不写存档。
public static class Doa2EarlyPreview
{
    // 这些方向尚未逐段对照实录核实，旧版箭头已撤回；完成核实前禁止再次输出。
    static readonly bool DirectionsVerified = false;
    static readonly string Folder = Path.GetFullPath("../Docs/Plans/Artifacts/Doa2EarlyPreview-20261021");
    static readonly List<Material> Materials = new List<Material>();
    static Transform root;
    static Camera camera;
    static Font font;
    // 标注字体仅在临时预览使用，不导入项目正式字体资源。
    static Font annotationFont;
    static Material enemyArrow, exitArrow, herdArrow, routeArrow, labelPanel;
    static Material grass, soil, wood, iron, water, red, cream, purple, yellow, dark, green;
    static int Layer = 30;

    public static string Run()
    {
        if (!DirectionsVerified) throw new InvalidOperationException("旧方向预览已撤回：须先按原始实录核实刷怪和转场位置，禁止继续用拟定布局推断原作。");
        if (EditorApplication.isPlaying) throw new InvalidOperationException("预览只在非播放状态执行");
        var prior = SceneManager.GetActiveScene();
        var oldRT = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Folder + "/frames");
            root = new GameObject("DOA2_TEMP_PREVIEW").transform;
            grass = Mat("Grass", .33f, .42f, .23f); soil = Mat("Dirt", .45f, .34f, .21f);
            wood = Mat("Wood", .47f, .33f, .20f); iron = Mat("Metal", .18f, .23f, .25f);
            water = Mat("Water", .12f, .35f, .45f); red = Mat("Red", .65f, .19f, .12f);
            cream = Mat("Cream", .83f, .79f, .63f); purple = Mat("Vortex", .51f, .24f, .73f);
            yellow = Mat("Warning", .96f, .65f, .12f); dark = Mat("Dark", .12f, .14f, .14f);
            green = Mat("Truck", .27f, .43f, .32f);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            annotationFont=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",64);
            enemyArrow=Mat("WarningEnemy",1,.16f,.12f);exitArrow=Mat("WarningExit",.12f,1,.38f);
            herdArrow=Mat("WarningHerd",1,.58f,.08f);routeArrow=Mat("WarningRoute",.12f,.8f,1);
            labelPanel=Mat("WarningPanel",.055f,.075f,.085f);
            camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 14.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f,.11f,.13f);
            camera.cullingMask = 1 << Layer; camera.transform.position = new Vector3(0,27,-24);
            camera.transform.LookAt(Vector3.zero); camera.farClipPlane = 200;
            var light = new GameObject("PreviewLight", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.8f; light.transform.rotation = Quaternion.Euler(48,-30,0); light.cullingMask=1<<Layer;
            Dock(); Capture("docks-layout.png",1280,800); Clear();
            Farm(); Capture("farm-layout.png",1280,800); Clear();
            Scale(); Capture("bull-player-scale.png",1280,800); Clear();
            BullMotion(); Clear();
            RaceMotion(); Clear();
            File.WriteAllText(Folder+"/preview-notes.txt", "独立临时灰盒预览，非正式资源、非自然对局。正式主角Prefab同框；牛和车辆为临时几何。拟定新路线1->21->22->5，旧2/3/4/15资源和配置保留，计划从新局主线移出；正式关卡尚未接入。\n红箭头表示战斗来敌，橙色表示牛群穿场，绿色表示清场后去向，蓝色表示竞速行驶。来敌缺口与传送点为预览拟定位置，不是原作精确测绘。码头第8波后去农场；农场第11波后竞速并返回第12波，第12波后去巨石环阵。\n");
            return Folder;
        }
        finally
        {
            RenderTexture.active = oldRT;
            SceneManager.SetActiveScene(prior);
            EditorSceneManager.CloseScene(scene,true);
            foreach(var material in Materials) if(material!=null) Object.DestroyImmediate(material);
            Materials.Clear();
            if(annotationFont!=null)Object.DestroyImmediate(annotationFont);
        }
    }

    static Material Mat(string name,float r,float g,float b)
    {
        var shader = Shader.Find(name.StartsWith("Warning") || name=="Vortex" ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if(shader==null) shader=Shader.Find("Unlit/Color");
        var m=new Material(shader){name="PREVIEW_"+name,color=new Color(r,g,b)};
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",m.color);
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.15f);
        Materials.Add(m);return m;
    }
    static GameObject Shape(string name,PrimitiveType kind,Vector3 pos,Vector3 size,Material mat,Transform parent=null)
    {
        var go=GameObject.CreatePrimitive(kind);go.name=name;go.layer=Layer;
        go.transform.SetParent(parent??root,false);go.transform.localPosition=pos;go.transform.localScale=size;
        go.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    static GameObject Box(string name,float x,float y,float z,float w,float h,float d,Material mat,Transform parent=null)=>
        Shape(name,PrimitiveType.Cube,new Vector3(x,y,z),new Vector3(w,h,d),mat,parent);
    static void Label(string text,float x,float z,float size=1)
    {
        var go=new GameObject("Label",typeof(TextMesh));go.layer=Layer;go.transform.SetParent(root,false);
        go.transform.localPosition=new Vector3(x,.11f,z);go.transform.localRotation=Quaternion.Euler(90,0,0);
        var t=go.GetComponent<TextMesh>();t.text=text;t.font=font;t.fontSize=64;t.characterSize=.11f*size;t.anchor=TextAnchor.MiddleCenter;t.color=new Color(.95f,.95f,.86f);
        go.GetComponent<MeshRenderer>().sharedMaterial=font.material;
    }
    static void Header(string title,string subtitle,float top=13)
    {Label(title,0,top,1.45f);Label(subtitle,0,top-1.4f,.74f);}
    static void Ring(string name,Vector3 position,float radius,Material mat)
    {
        var line=new GameObject(name,typeof(LineRenderer));line.layer=Layer;line.transform.SetParent(root,false);
        var r=line.GetComponent<LineRenderer>();r.sharedMaterial=mat;r.useWorldSpace=true;r.widthMultiplier=.065f;r.loop=true;r.positionCount=48;
        for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;r.SetPosition(i,position+new Vector3(Mathf.Cos(a)*radius,.07f,Mathf.Sin(a)*radius));}
    }
    static void Line(string name,Vector3 a,Vector3 b,float width,Material material)
    {
        var line=new GameObject(name,typeof(LineRenderer));line.layer=Layer;line.transform.SetParent(root,false);
        var r=line.GetComponent<LineRenderer>();r.sharedMaterial=material;r.useWorldSpace=true;r.widthMultiplier=width;r.positionCount=2;r.SetPosition(0,a);r.SetPosition(1,b);
    }
    // 箭头沿地面指向目的地；红色用于战斗来敌，绿色仅表示清场后的流向。
    static void Arrow(string name,float ax,float az,float bx,float bz,Material material)
    {
        var a=new Vector3(ax,.18f,az);var b=new Vector3(bx,.18f,bz);
        var direction=(b-a).normalized;var side=Vector3.Cross(Vector3.up,direction);
        Line(name,a,b,.14f,material);
        Line(name+"HeadL",b,b-direction*.75f+side*.4f,.14f,material);
        Line(name+"HeadR",b,b-direction*.75f-side*.4f,.14f,material);
    }
    // 中文标注置于深色底板上，说明波次和传送目的地，避免与地图物件混淆。
    static void Note(string text,float x,float z,float width,float height,Color color,float size=.95f)
    {
        Box("AnnotationPanel",x,.21f,z,width,.025f,height,labelPanel);
        var go=new GameObject("ChineseAnnotation",typeof(TextMesh));go.layer=Layer;go.transform.SetParent(root,false);
        go.transform.localPosition=new Vector3(x,.25f,z);go.transform.localRotation=Quaternion.Euler(90,0,0);
        var mesh=go.GetComponent<TextMesh>();mesh.text=text;mesh.font=annotationFont;mesh.fontSize=64;
        mesh.characterSize=.11f*size;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=color;
        annotationFont.RequestCharactersInTexture(text,64,FontStyle.Normal);
        go.GetComponent<MeshRenderer>().sharedMaterial=annotationFont.material;
    }
    static void FlowLegend()
    {
        Note("红箭头：战斗时来敌　　绿箭头：清场后去向",0,-13.5f,25,1.2f,Color.white,.9f);
    }
    static void Fence(float x,float z,float length,bool turn=false)
    {
        var group=new GameObject("TemporaryFence").transform;group.SetParent(root,false);group.position=new Vector3(x,0,z);if(turn)group.rotation=Quaternion.Euler(0,90,0);
        for(float p=-length/2;p<=length/2+.01f;p+=1.6f)Box("Post",p,.65f,0,.15f,1.3f,.15f,wood,group);
        Box("Rail",0,.55f,0,length,.13f,.12f,cream,group);Box("Rail",0,1.05f,0,length,.13f,.12f,cream,group);
    }
    static GameObject Actor(string path,Vector3 pos,float yaw=0)
    {
        var parent=new GameObject("PreviewActor");parent.SetActive(false);parent.transform.SetParent(root,false);
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(asset==null)throw new InvalidOperationException("预览引用缺失："+path);
        var actor=Object.Instantiate(asset,parent.transform,false);
        foreach(var script in actor.GetComponentsInChildren<MonoBehaviour>(true))Object.DestroyImmediate(script);
        foreach(var ps in actor.GetComponentsInChildren<ParticleSystem>(true))ps.gameObject.SetActive(false);
        foreach(var c in actor.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var rb in actor.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.detectCollisions=false;}
        parent.transform.position=pos;parent.transform.rotation=Quaternion.Euler(0,yaw,0);
        actor.SetActive(true);parent.SetActive(true);
        foreach(var a in actor.GetComponentsInChildren<Animator>(true))
        {
            a.Rebind();a.Update(0);
            if(path.EndsWith("GrayboxPlayer.prefab"))
            {
                a.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Art/Player/Humanoid/PlayerHumanoid.controller");
                a.Rebind();a.Update(.1f);
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Art/Player/Animations/Mixamo/Imported/PistolIdleMatched.anim");
                if(clip!=null)clip.SampleAnimation(a.gameObject,.3f);
            }
            a.enabled=false;
        }
        foreach(var t in parent.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
        return parent;
    }
    static GameObject Hero(float x,float z)=>Actor("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab",new Vector3(x,0,z));
    static void Zombie(float x,float z)=>Actor("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab",new Vector3(x,0,z),180);
    static void Tower(float x,float z)
    {
        Shape("Base",PrimitiveType.Cylinder,new Vector3(x,.12f,z),new Vector3(.8f,.12f,.8f),iron);
        Box("Tower",x,1.1f,z,.18f,2.2f,.18f,iron);Shape("Coil",PrimitiveType.Sphere,new Vector3(x,2.3f,z),Vector3.one*.6f,yellow);
        Ring("WarningRange",new Vector3(x,0,z),1.2f,yellow);
    }
    static void Barrel(float x,float z)
    {Shape("VortexBarrel",PrimitiveType.Cylinder,new Vector3(x,.5f,z),new Vector3(.8f,.5f,.8f),purple);Ring("Vortex",new Vector3(x,0,z),1.35f,purple);}
    static void Dock()
    {
        var deckWood=Mat("WeatheredDeck",.32f,.34f,.29f);
        Header("ROUND 5-8 / DOCKS","VIDEO REFERENCE 05:21 - TEMPORARY GEOMETRY");
        Box("Sea",0,-.36f,0,30,.4f,23,water);
        for(int i=0;i<90;i++)Box("MainDeck",-1,-.02f,-9+i*.2f,23,.12f,.18f,deckWood);
        // 参照实录，以连续木板平台和右侧水槽为主体，不再使用泥地加双栈桥。
        for(int side=-1;side<=1;side+=2)
        {
            for(int i=0;i<22;i++)Box("SideDock",11.5f,-.02f,side*6+i*.08f,4,.12f,.07f,deckWood);
            for(int i=0;i<4;i++)Box("EdgeCrate",-8+i*1.5f,.45f,side*8,.9f,.9f,.9f,iron);
            Tower(side*6,side*5);Barrel(side*3,-1);
        }
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
            Box("CraneLeg",-9+x*.6f,3,z*2,.18f,6,.18f,iron);
        Box("CraneTop",-9,6,0,1.5f,.2f,4.5f,iron);
        Line("HangingCable",new Vector3(-8.3f,6,1.5f),new Vector3(-8.3f,1.8f,1.5f),.055f,iron);
        Ring("CraneHook",new Vector3(-8.3f,1.8f,1.5f),.25f,iron);
        Box("TemporaryMooredBoat",1,.4f,-10.3f,7,.8f,1.6f,iron);
        Box("BoatCabin",1,1.1f,-10.3f,2,1.2f,1.3f,cream);
        Hero(0,-4);Zombie(-9,1);Zombie(10,4);Zombie(-5,7);Zombie(5,7);
        Arrow("WaterSpawnLeft",-14,0,-10.5f,0,enemyArrow);Arrow("WaterSpawnRight",14,0,10.5f,0,enemyArrow);
        Arrow("WaterSpawnRearLeft",-1,10.5f,-1,8,enemyArrow);Arrow("WaterSpawnRearRight",5,10.5f,5,8,enemyArrow);
        Note("水侧来敌",-15,2.5f,5,1.3f,enemyArrow.color);Note("水侧来敌",15,2.5f,5,1.3f,enemyArrow.color);
        Note("水侧来敌",0,10.4f,5,1.3f,enemyArrow.color);
        Ring("ClearExit",new Vector3(0,.18f,1),.95f,exitArrow);
        Arrow("ToClearExit",0,-3.2f,0,-.1f,exitArrow);
        Note("第8波清场后\n进入传送点 → 农场",0,5.5f,10,2.7f,exitArrow.color,1);
        Note("预览拟定传送位置；第6—8波按清场选路继续",0,-11.6f,24,1.2f,Color.white,.8f);
        FlowLegend();
    }
    static void Farm()
    {
        Header("ROUND 9-12 / FARM","VIDEO REFERENCE 11:27 / 12:08 - TEMPORARY GEOMETRY");
        Box("SnowGround",0,-.2f,0,27,.3f,21,Mat("Snow",.56f,.58f,.56f));
        Fence(-6,8,10);Fence(7,8,8);
        Fence(-12,-2,8,true);Fence(-12,5,2,true);Fence(12,2,8,true);Fence(12,-5,2,true);
        // 草料堆、树木和边缘棚屋只用于确认布局；不把草地和红谷仓作为主体。
        foreach(var p in new[]{new Vector2(-7,0),new Vector2(4,5),new Vector2(-2,-6)})
        {
            Box("HayPatch",p.x,0,p.y,3,.09f,2.5f,soil);
            for(int i=0;i<3;i++)Box("HayBale",p.x+(i-1)*.8f,.4f,p.y,.7f,.8f,1.2f,wood);
        }
        Box("EdgeShed",12,1.1f,5,2,2.2f,4,wood);
        foreach(float x in new[]{-10f,-3f,5f,10f})
        {
            Box("TreeTrunk",x,1.1f,7.8f,.35f,2.2f,.35f,wood);
            Shape("TreeCrown",PrimitiveType.Sphere,new Vector3(x,2.2f,7.8f),new Vector3(2.2f,1.6f,2.2f),grass);
        }
        Hero(0,-4);
        for(int i=0;i<12;i++)Bull(new Vector3(-9+i%6*3.4f+Mathf.Sin(i*2)*.35f,0,-1+i/6*3+Mathf.Cos(i*3)*.55f),i%3==0?-80:75+i%4*10);
        Zombie(-4,-5);Zombie(4,4);
        Arrow("FarmSpawnLeft",-14,3,-10.5f,3,enemyArrow);Arrow("FarmSpawnRight",14,-3,10.5f,-3,enemyArrow);
        Arrow("FarmSpawnRear",1,10.5f,1,7.3f,enemyArrow);
        Note("围栏缺口来敌",-15,5.3f,6.2f,1.3f,enemyArrow.color,.85f);
        Note("围栏缺口来敌",15,-.6f,6.2f,1.3f,enemyArrow.color,.85f);
        Note("后侧来敌",1,10.6f,5,1.3f,enemyArrow.color,.85f);
        Arrow("HerdCrossing",-10,-3.5f,-5,-3.5f,herdArrow);Note("牛群穿场",-7.5f,-5.3f,5,1.2f,herdArrow.color,.85f);
        Ring("FarmClearExit",new Vector3(.7f,.18f,-7.5f),.8f,exitArrow);
        Arrow("ToFarmExit",0,-4.8f,.7f,-6.5f,exitArrow);
        Note("第11波后 → 竞速 → 返回第12波\n第12波后 → 巨石环阵",6,-9.6f,17,2.5f,exitArrow.color,.9f);
        Note("来敌缺口及传送点：本预览拟定位置",0,-11.9f,24,1.2f,Color.white,.85f);
        FlowLegend();
    }
    static Transform Bull(Vector3 position,float yaw)
    {
        var bull=new GameObject("TEMP_BULL_GEOMETRY").transform;bull.SetParent(root,false);bull.position=position;bull.rotation=Quaternion.Euler(0,yaw,0);
        Box("Body",0,1.05f,0,1.15f,.9f,1.85f,wood,bull);Box("Shoulders",0,1.26f,.5f,1.25f,.9f,.9f,dark,bull);
        Box("Head",0,1.15f,1.12f,.75f,.65f,.7f,wood,bull);Box("Nose",0,.95f,1.5f,.67f,.35f,.22f,dark,bull);
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box("Leg",x*.41f,.38f,z*.62f,.23f,.76f,.23f,dark,bull);
        for(int side=-1;side<=1;side+=2)
        {var horn=Box("Horn",side*.58f,1.52f,1.17f,.16f,.65f,.16f,cream,bull);horn.transform.localRotation=Quaternion.Euler(0,0,-side*45);Box("Eye",side*.39f,1.3f,1.27f,.045f,.08f,.08f,cream,bull);}
        var tail=Box("Tail",0,.9f,-1.15f,.13f,.8f,.13f,dark,bull);tail.transform.localRotation=Quaternion.Euler(-35,0,0);
        return bull;
    }
    static Transform Truck(Vector3 position)
    {
        var truck=new GameObject("TEMP_TRUCK_GEOMETRY").transform;truck.SetParent(root,false);truck.position=position;
        Box("Body",0,.9f,0,1.8f,.55f,3.1f,green,truck);Box("Cab",0,1.5f,.15f,1.6f,.85f,1.35f,green,truck);Box("Windscreen",0,1.55f,.85f,1.32f,.5f,.04f,water,truck);
        Box("Hood",0,1.13f,1.06f,1.65f,.3f,.9f,green,truck);Box("Bed",0,1.12f,-1.05f,1.6f,.18f,.8f,wood,truck);
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
        {var wheel=Shape("Wheel",PrimitiveType.Cylinder,new Vector3(x*.97f,.55f,z),new Vector3(1.05f,.23f,1.05f),dark,truck);wheel.transform.localRotation=Quaternion.Euler(0,0,90);}
        return truck;
    }
    static void Scale()
    {
        camera.orthographicSize=5.8f;camera.transform.position=new Vector3(8,6,-12);camera.transform.LookAt(new Vector3(0,.8f,0));
        Box("Floor",0,-.1f,0,15,.18f,11,soil);Hero(-2,0);Bull(new Vector3(1,0,0),0);Truck(new Vector3(4.3f,0,0));
        Label("FORMAL PLAYER",-2,-2.4f,.55f);Label("TEMP BULL",1,-2.4f,.55f);Label("TEMP TRUCK",4.3f,-2.4f,.55f);
        for(int i=-6;i<=6;i++)Line("MeterGrid",new Vector3(i,.02f,-4),new Vector3(i,.02f,4),.012f,cream);
    }
    static void BullMotion()
    {
        camera.orthographicSize=10;camera.transform.position=new Vector3(0,22,-18);camera.transform.LookAt(Vector3.zero);
        Box("Field",0,-.12f,0,24,.2f,14,Mat("SnowMotion",.56f,.58f,.56f));
        var hero=Hero(0,-1);var bulls=new List<Transform>();
        for(int j=0;j<12;j++)bulls.Add(Bull(new Vector3(-11-j%4*3.3f,0,(j/4-1)*2.1f),90));
        Zombie(3,0);Zombie(5,1.6f);
        var warning=Box("ProjectWarningProposal",-2,.03f,0,14,.025f,4.5f,yellow);
        Header("HERD CROSSING / MOTION STUDY","VIDEO-INSPIRED HERD - YELLOW WARNING IS OUR PROPOSAL",11);
        for(int i=0;i<60;i++)
        {
            float t=i/10f;warning.SetActive(t<1.2f);
            for(int k=0;k<bulls.Count;k++)
            {
                var bull=bulls[k];bull.position=new Vector3(t<1.2f?-11-k%4*3.3f:Mathf.Lerp(-11-k%4*3.3f,27-k%4*3.3f,Mathf.Clamp01((t-1.2f)/3.2f)),0,(k/4-1)*2.1f);
                for(int j=0;j<bull.childCount;j++)if(bull.GetChild(j).name=="Leg")bull.GetChild(j).localRotation=Quaternion.Euler(t>=1.2f&&t<4.4f?Mathf.Sin(t*25+j+k)*18:0,0,0);
            }
            hero.transform.position=new Vector3(0,0,t<.8f?-1:Mathf.Lerp(-1,-4.8f,Mathf.Clamp01((t-.8f)/.85f)));
            Capture("frames/bull-"+i.ToString("D3")+".png",800,500);
        }
    }
    static void RaceMotion()
    {
        camera.orthographicSize=12;camera.transform.position=new Vector3(0,25,-19);camera.transform.LookAt(Vector3.zero);
        Box("CanyonFloor",0,-.2f,0,29,.3f,21,soil);
        Header("RACE AFTER ROUND 11 / CANYON STUDY","VIDEO REFERENCE 16:21 - ROCK WALLS, TIRE BARRIERS AND RAMPS");
        for(int i=0;i<14;i++)for(int side=-1;side<=1;side+=2)
            Shape("RockWall",PrimitiveType.Sphere,new Vector3(-13+i*2,1,side*9),new Vector3(2.8f,3+i%3*.5f,2.5f),wood);
        for(int side=-1;side<=1;side+=2)for(int i=0;i<7;i++)
            Shape("EndRock",PrimitiveType.Sphere,new Vector3(side*13,1,-6+i*2),new Vector3(2.8f,3,2.8f),wood);
        for(int i=0;i<9;i++)
        {
            Shape("TireBarrier",PrimitiveType.Cylinder,new Vector3(-7+i*1.1f,.32f,2),new Vector3(.85f,.32f,.85f),dark);
            Shape("TireBarrier",PrimitiveType.Cylinder,new Vector3(-1+i*1.1f,.32f,-2),new Vector3(.85f,.32f,.85f),dark);
        }
        var ramp=Box("Ramp",-7,.4f,-5,3,.7f,2.5f,wood);ramp.transform.rotation=Quaternion.Euler(-12,0,0);
        var truck=Truck(new Vector3(-10,0,-6));Zombie(-3,-6);Zombie(5,6);
        for(int i=0;i<8;i++)Shape("Gem",PrimitiveType.Sphere,new Vector3(-6+i*1.8f,.5f,6),Vector3.one*.35f,purple);
        // 折返路线仅展示岩壁与轮胎夹道中的转向，尚非原作完整赛道测绘。
        var path=new[]{new Vector3(-10,0,-6),new Vector3(8,0,-6),new Vector3(10,0,-4),new Vector3(8,0,0),new Vector3(-8,0,0),new Vector3(-10,0,4),new Vector3(-7,0,6),new Vector3(10,0,6)};
        for(int j=0;j<path.Length-1;j++)
        {
            var from=Vector3.Lerp(path[j],path[j+1],.25f);var to=Vector3.Lerp(path[j],path[j+1],.75f);
            Arrow("DrivingDirection",from.x,from.z,to.x,to.z,routeArrow);
        }
        Note("行驶方向",0,-8,8,1.3f,routeArrow.color,.9f);
        Note("结束或超时 → 返回农场第12波",0,8,21,1.4f,exitArrow.color,.9f);
        Capture("race-layout.png",1280,800);
        for(int i=0;i<60;i++)
        {
            float progress=i/59f*(path.Length-1);int segment=Mathf.Min((int)progress,path.Length-2);
            truck.position=Vector3.Lerp(path[segment],path[segment+1],progress-segment);truck.rotation=Quaternion.LookRotation(path[segment+1]-path[segment]);
            Capture("frames/race-"+i.ToString("D3")+".png",800,500);
        }
    }
    static void Clear()
    {
        foreach(var child in root.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
        camera.orthographicSize=14.5f;camera.transform.position=new Vector3(0,27,-24);camera.transform.LookAt(Vector3.zero);
    }
    static void Capture(string name,int width,int height)
    {
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Folder+"/"+name,texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(texture);RenderTexture.ReleaseTemporary(target);}
    }
}
