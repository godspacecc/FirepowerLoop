using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>后两关与竞速的独立项目方案预览；仅临时几何，不保存正式资源，不推断原作方向。</summary>
public static class RemainingStagesPreview
{
    static bool RaceOnly;
    static string Folder=>Path.GetFullPath(RaceOnly?"Temp/RaceCurvesPreview-v4":"Temp/RemainingStagesPreview-v3");
    public static string RunRace(){RaceOnly=true;try{return Run();}finally{RaceOnly=false;}}
    static readonly List<Material> Materials=new List<Material>();
    static Transform root;static Camera camera;static int Layer=30;
    static Material grass,soil,wood,iron,water,cream,purple,dark,green;
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态临时预览");
        var prior=SceneManager.GetActiveScene();var oldRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);Directory.CreateDirectory(Folder);Directory.CreateDirectory(Folder+"/frames");
            root=new GameObject("TEMP_PROJECT_LAYOUT_V3").transform;
            grass=Mat("Trees",.30f,.37f,.31f);soil=Mat("Dirt",.45f,.34f,.24f);wood=Mat("Wood",.45f,.31f,.18f);
            iron=Mat("Metal",.18f,.24f,.27f);water=Mat("Water",.13f,.35f,.46f);cream=Mat("Cream",.87f,.83f,.69f);
            purple=Mat("Vortex",.55f,.27f,.78f);dark=Mat("Dark",.10f,.12f,.13f);green=Mat("Truck",.28f,.44f,.34f);
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<Layer;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.11f);camera.farClipPlane=250;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(48,-30,0);
            if(RaceOnly){Reset();RaceMotion();Reset();}
            else{Reset();Dock();Capture("docks.png",1440,900);Reset();Farm();Capture("farm.png",1440,900);
            Reset();Scale();Capture("scale.png",1280,720);Reset();BullMotion();Reset();RaceMotion();Reset();}
            return Folder;
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=oldRT;Object.DestroyImmediate(camera.gameObject);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var m in Materials)if(m!=null)Object.DestroyImmediate(m);Materials.Clear();
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
    static void Line(string name,Vector3 a,Vector3 b,float width,Material material)
    {
        var line=new GameObject(name,typeof(LineRenderer));line.layer=Layer;line.transform.SetParent(root,false);
        var r=line.GetComponent<LineRenderer>();r.sharedMaterial=material;r.useWorldSpace=true;r.widthMultiplier=width;r.positionCount=2;r.SetPosition(0,a);r.SetPosition(1,b);
    }
    static void Fence(float x,float z,float length,bool turn=false)
    {
        var group=new GameObject("TemporaryFence").transform;group.SetParent(root,false);group.position=new Vector3(x,0,z);if(turn)group.rotation=Quaternion.Euler(0,90,0);
        for(float p=-length/2;p<=length/2+.01f;p+=1.6f)Box("Post",p,.65f,0,.15f,1.3f,.15f,wood,group);
        Box("Rail",0,.55f,0,length,.13f,.12f,cream,group);Box("Rail",0,1.05f,0,length,.13f,.12f,cream,group);
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
    static void Capture(string name,int width,int height)
    {
        var target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);var old=RenderTexture.active;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Folder+"/"+name,texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(texture);RenderTexture.ReleaseTemporary(target);}
    }
    static GameObject Box(string name,float x,float y,float z,float w,float h,float d,Material mat,Transform parent=null)=>Shape(name,PrimitiveType.Cube,new Vector3(x,y,z),new Vector3(w,h,d),mat,parent);
    static void Reset()
    {
        foreach(var child in root.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
        camera.orthographicSize=15.4f;camera.transform.position=new Vector3(0,32,-25);camera.transform.LookAt(Vector3.zero);
    }
    // 正式主角及已确认 AK 同框；预览实例不运行战斗逻辑。
    static GameObject Hero(float x,float z)
    {
        var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),root);
        var p=go.GetComponent<Game.CharacterWeaponPresenter>();p.SetWeaponId("arcade.weapon.basic");p.SetManualPoseSampling(true);
        p.BodyAnimator.Rebind();p.BodyAnimator.Update(0);p.Current.Animations.Idle.Clip.SampleAnimation(p.BodyAnimator.gameObject,.1f);p.ApplyOffhandGrip(p.Current.Animations.Idle);
        foreach(var c in go.GetComponentsInChildren<Canvas>(true))c.enabled=false;
        foreach(var c in go.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
        go.transform.position=new Vector3(x,0,z);go.transform.rotation=Quaternion.Euler(0,30,0);return go;
    }
    static void Zombie(float x,float z)
    {
        var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),root);
        foreach(var t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
        foreach(var c in go.GetComponentsInChildren<Canvas>(true))c.enabled=false;
        var a=go.GetComponentInChildren<Animator>(true);a.Rebind();a.Update(0);
        var idle=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Arcade/Enemies/Humanoid/Animations/ArcadeEnemyIdle.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));idle.SampleAnimation(a.gameObject,.1f);a.enabled=false;
        go.transform.position=new Vector3(x,0,z);go.transform.rotation=Quaternion.Euler(0,150,0);
    }
    static void Rock(float x,float z,float width=3)
    {Shape("BoundaryRock",PrimitiveType.Sphere,new Vector3(x,1.3f,z),new Vector3(width,3.7f,width*.86f),iron);}
    static void Deck(float x,float z,float w,float d,Material mat)
    {
        Box("DeckBase",x,-.20f,z,w,.25f,d,wood);
        int count=Mathf.CeilToInt(d/.24f);for(int i=0;i<count;i++)Box("DeckPlank",x,-.03f,z-d*.5f+(i+.5f)*d/count,w,.12f,d/count-.018f,mat);
    }
    static void Dock()
    {
        var deck=Mat("Deck",.39f,.37f,.30f);Box("Sea",0,-.65f,0,70,.6f,62,water);Deck(-1,0,26,23,deck);
        // 三段固定平面通路是项目布局候选，不是原作方向测绘或刷怪提示。
        Deck(-16.5f,4,5,3.5f,deck);Deck(3,15,4,7,deck);Deck(16,-4,8,4,deck);Deck(18,0,3,23,deck);
        for(int i=0;i<6;i++){Box("Crate",-10+i*1.3f,.55f,9.7f,1.1f,1.1f,1.1f,wood);Box("Crate",-10+i*1.3f,.55f,-9.7f,1.1f,1.1f,1.1f,iron);}
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)Box("CraneLeg",-10+x*.8f,3,z*2,.22f,6,.22f,iron);
        Box("CraneBeam",-10,6,0,2,.25f,5.2f,iron);Line("Cable",new Vector3(-9,6,1.6f),new Vector3(-9,1.8f,1.6f),.08f,iron);
        Box("HangingCargo",-9,1.7f,1.6f,1.2f,.75f,1.2f,wood);
        Box("MooredHull",2,.05f,-14.1f,9,1.3f,3,iron);Box("Cabin",2,1.2f,-14.1f,3,1.5f,2.4f,cream);
        for(int i=0;i<4;i++)Box("BoatWindow",1+i*.65f,1.4f,-15.32f,.43f,.55f,.04f,water);
        Deck(6,-12,2.5f,2.2f,deck);
        // 复用正式桶与电塔的外观候选，不新增 HUD。
        var content=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content");
        foreach(Transform child in content)
        {
            if(child.name.IndexOf("Vortex",StringComparison.OrdinalIgnoreCase)<0&&child.name.IndexOf("Tower",StringComparison.OrdinalIgnoreCase)<0)continue;
            var o=Object.Instantiate(child.gameObject,root);o.SetActive(true);o.transform.position=child.name.IndexOf("Tower",StringComparison.OrdinalIgnoreCase)>=0?new Vector3(7,0,7):new Vector3(-3,0,2);
            foreach(var t in o.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
        }
        Hero(0,-4);Zombie(-6,4);Zombie(6,1);
    }
    // 四个约28米区域组成大农场，破墙缺口和门洞始终通行，不演示额外破坏/开门功能。
    static void Farm()
    {
        var snow=Mat("Snow",.48f,.54f,.57f);var hay=Mat("Hay",.63f,.51f,.29f);
        Box("SnowGround",0,-.22f,0,86,.4f,80,snow);
        for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)
        {
            float cx=sx*15,cz=sz*15;
            Box("YardSnow",cx,-.025f,cz,28,.10f,28,snow);
            for(int i=0;i<3;i++)
            {
                float hx=cx+sx*8, hz=sz<0?cz+9+(i-1)*2:cz+(i-1)*3.5f;
                Box("HayPatch",hx,.015f,hz,3,.10f,2.8f,soil);
                Box("HayBale",hx,.55f,hz,2.2f,1.1f,1.4f,hay);
            }
            if(sz>0){Box("Shed",cx+sx*8,1.4f,cz+8,4,2.8f,6,wood);Box("SnowRoof",cx+sx*8,2.9f,cz+8,4.5f,.3f,6.5f,snow);}
            else Fence(cx,cz-11,12);
            for(int i=0;i<4;i++)
            {
                float x=cx-9+i*5,z=cz+sz*11;
                Box("Trunk",x,1.5f,z,.4f,3,.4f,wood);Shape("Crown",PrimitiveType.Sphere,new Vector3(x,3,z),new Vector3(3,2.8f,3),grass);
                Shape("CrownSnow",PrimitiveType.Sphere,new Vector3(x,3.8f,z),new Vector3(2.7f,1.4f,2.7f),snow);
            }
        }
        // 中间十字隔墙：下方横穿缺口供一排牛通过，其余相邻区也有门洞或破口。
        Wall("WestEastDivider",true,0,-30,-19,iron);Wall("WestEastDivider",true,0,-9,11,iron);Wall("WestEastDivider",true,0,17,30,iron);
        Wall("NorthSouthDivider",false,0,-30,-18,iron);Wall("NorthSouthDivider",false,0,-10,10,iron);Wall("NorthSouthDivider",false,0,18,30,iron);
        Gate(0,14,true,cream);Gate(-14,0,false,cream);
        Rubble(0,-19);Rubble(0,-9);Rubble(10,0);Rubble(18,0);
        for(int side=-1;side<=1;side+=2)
        {
            Wall("OuterWall",true,side*30,-30,-19,iron);Wall("OuterWall",true,side*30,-9,30,iron);
            Wall("OuterWall",false,side*30,-30,-4,iron);Wall("OuterWall",false,side*30,4,30,iron);
            for(int i=0;i<20;i++){float edge=-32+i*3.5f;if(Mathf.Abs(edge+14)>6)Rock(side*35,edge,4.8f);if(Mathf.Abs(edge)>5)Rock(edge,side*35,4.8f);}
        }
        Hero(-13,-16);Zombie(-9,14);Zombie(14,-5);Zombie(12,17);
        for(int i=0;i<5;i++)Bull(new Vector3(-18,0,-14+(i-2)*1.65f),90);
        camera.orthographicSize=36;camera.transform.position=new Vector3(0,68,-44);camera.transform.LookAt(Vector3.zero);
    }
    static void Wall(string name,bool alongZ,float fixedCoordinate,float from,float to,Material mat)
    {
        float mid=(from+to)*.5f,length=to-from;
        Box(name,alongZ?fixedCoordinate:mid,1.05f,alongZ?mid:fixedCoordinate,alongZ?.7f:length,2.1f,alongZ?length:.7f,mat);
    }
    static void Gate(float x,float z,bool alongZ,Material mat)
    {
        for(int side=-1;side<=1;side+=2)Box("OpenDoorPost",x+(alongZ?0:side*3),1.5f,z+(alongZ?side*3:0),.75f,3,.75f,mat);
        Box("OpenDoorLintel",x,3.05f,z,alongZ?.9f:6.7f,.3f,alongZ?6.7f:.9f,wood);
    }
    static void Rubble(float x,float z)
    {for(int i=0;i<3;i++)Box("BrokenWallRubble",x+(i-1)*.45f,.2f,z+(i%2)*.6f,.65f,.4f,.75f,iron);}
    static void Scale()
    {
        Box("Snow",0,-.15f,0,20,.2f,15,Mat("SnowScale",.48f,.54f,.57f));Hero(-3,0);Bull(new Vector3(0,0,0),15);Truck(new Vector3(3.7f,0,0));
        camera.orthographicSize=5.5f;camera.transform.position=new Vector3(9,6,-13);camera.transform.LookAt(new Vector3(.6f,.8f,0));
    }
    static void BullMotion()
    {
        Farm();var hero=root.GetComponentsInChildren<Game.CharacterWeaponPresenter>(true).First().gameObject;
        var bulls=root.Cast<Transform>().Where(t=>t.name=="TEMP_BULL_GEOMETRY").ToArray();
        camera.orthographicSize=20;camera.transform.position=new Vector3(0,40,-46);camera.transform.LookAt(new Vector3(0,0,-14));
        for(int frame=0;frame<150;frame++)
        {
            float t=frame/15f,progress=Mathf.Clamp01((t-.8f)/7f),x=Mathf.Lerp(-33,34,progress);
            for(int i=0;i<bulls.Length;i++)
            {
                bulls[i].position=new Vector3(x,0,-14+(i-2)*1.65f);bulls[i].localEulerAngles=new Vector3(t<.8f?Mathf.Sin(t*8)*3:0,90,0);
                foreach(Transform leg in bulls[i])if(leg.name=="Leg")leg.localRotation=Quaternion.Euler(t>.8f&&t<7.8f?Mathf.Sin(t*24+leg.localPosition.x*4+i)*22:0,0,0);
            }
            hero.transform.position=new Vector3(-7,0,Mathf.Lerp(-14,-23,Mathf.Clamp01((t-1.4f)/1.3f)));
            Capture("frames/herd-"+frame.ToString("D3")+".png",960,600);
        }
    }
    // 多弯道路先用样条路线和连续轮胎界定真实通路，预览不包含输入或伤害裁决。
    static void RaceMotion()
    {
        Box("CanyonFloor",0,-.25f,0,110,.4f,90,soil);
        for(int side=-1;side<=1;side+=2)
        {
            for(int i=0;i<40;i++)Rock(-48+i*2.5f,side*40,4.5f);
            for(int i=0;i<31;i++)Rock(side*49,-37+i*2.5f,4.5f);
        }
        var anchors=new[]{new Vector3(-42,0,-26),new Vector3(-21,0,-26),new Vector3(-9,0,-16),new Vector3(5,0,-19),
            new Vector3(16,0,-29),new Vector3(31,0,-25),new Vector3(37,0,-13),new Vector3(25,0,-5),new Vector3(11,0,0),
            new Vector3(16,0,12),new Vector3(32,0,15),new Vector3(37,0,27),new Vector3(24,0,32),new Vector3(5,0,31),
            new Vector3(-7,0,20),new Vector3(-24,0,22),new Vector3(-35,0,12),new Vector3(-37,0,-1),new Vector3(-25,0,-8),
            new Vector3(-15,0,-1),new Vector3(-15,0,9),new Vector3(-4,0,10)};
        var path=new List<Vector3>();
        for(int i=0;i<anchors.Length-1;i++)for(int j=0;j<16;j++)
        {
            float t=j/16f;var p0=anchors[Mathf.Max(0,i-1)];var p1=anchors[i];var p2=anchors[i+1];var p3=anchors[Mathf.Min(anchors.Length-1,i+2)];
            path.Add(.5f*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t));
        }
        path.Add(anchors[anchors.Length-1]);
        var road=Mat("PackedSnowDirt",.36f,.29f,.22f);var railPoints=new List<Vector3>();float railDistance=0,total=0;
        var lengths=new float[path.Count];
        for(int i=1;i<path.Count;i++)
        {
            var delta=path[i]-path[i-1];float length=delta.magnitude;total+=length;lengths[i]=total;
            var mid=(path[i]+path[i-1])*.5f;var strip=Box("Road",mid.x,-.01f,mid.z,6.8f,.10f,length+.15f,road);strip.transform.rotation=Quaternion.LookRotation(delta);
            Shape("RoadCurveJoin",PrimitiveType.Cylinder,path[i]+Vector3.up*.04f,new Vector3(6.8f,.01f,6.8f),road);
            railDistance+=length;if(railDistance<1.35f)continue;railDistance=0;
            var normal=new Vector3(delta.z,0,-delta.x).normalized;
            foreach(int side in new[]{-1,1})
            {
                var point=path[i]+normal*side*3.9f;railPoints.Add(point);
                Shape("CurveTireBarrier",PrimitiveType.Cylinder,point+Vector3.up*.34f,new Vector3(.95f,.34f,.95f),dark);
            }
        }
        // 独立坡台置于起跑后的长直段，不放在弯心或挡住车辆落地点。
        var ramp=Box("JumpRamp",-30,.4f,-26.5f,4.4f,.6f,5,wood);ramp.transform.rotation=Quaternion.Euler(0,90,8);
        var truck=Truck(path[0]);Hero(-44,-20);for(int i=4;i<anchors.Length-1;i+=4)Zombie(anchors[i].x,anchors[i].z);
        camera.orthographicSize=43;camera.transform.position=new Vector3(0,90,-62);camera.transform.LookAt(Vector3.zero);Capture("race.png",1440,1100);
        for(int frame=0;frame<330;frame++)
        {
            float distance=frame/329f*total;int i=1;while(i<lengths.Length-1&&lengths[i]<distance)i++;
            var at=Vector3.Lerp(path[i-1],path[i],Mathf.InverseLerp(lengths[i-1],lengths[i],distance));var direction=(path[i]-path[i-1]).normalized;
            if(at.x>-32.5f&&at.x<-25.5f&&at.z<-24&&distance<30)at.y=Mathf.Sin((at.x+32.5f)/7*Mathf.PI)*1.35f;
            truck.position=at;truck.rotation=Quaternion.LookRotation(direction);
            if(at.y>.05f)truck.rotation*=Quaternion.Euler(at.x<-29?-12:12,0,0);
            foreach(Transform wheel in truck)if(wheel.name=="Wheel")wheel.localRotation=Quaternion.Euler(frame*26,0,90);
            camera.orthographicSize=14.5f;camera.transform.position=at+new Vector3(0,30,-24);camera.transform.LookAt(at+Vector3.forward*2);
            Capture("frames/race-"+frame.ToString("D3")+".png",960,600);
        }
    }
}
