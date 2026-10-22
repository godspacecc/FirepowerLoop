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
    static readonly string Folder=Path.GetFullPath("Temp/RemainingStagesPreview-v2");
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
            root=new GameObject("TEMP_PROJECT_LAYOUT_V2").transform;
            grass=Mat("Trees",.30f,.37f,.31f);soil=Mat("Dirt",.45f,.34f,.24f);wood=Mat("Wood",.45f,.31f,.18f);
            iron=Mat("Metal",.18f,.24f,.27f);water=Mat("Water",.13f,.35f,.46f);cream=Mat("Cream",.87f,.83f,.69f);
            purple=Mat("Vortex",.55f,.27f,.78f);dark=Mat("Dark",.10f,.12f,.13f);green=Mat("Truck",.28f,.44f,.34f);
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<Layer;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.11f);camera.farClipPlane=250;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(48,-30,0);
            Reset();Dock();Capture("docks.png",1440,900);Reset();Farm();Capture("farm.png",1440,900);
            Reset();Scale();Capture("scale.png",1280,720);Reset();BullMotion();Reset();RaceMotion();Reset();
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
    static void Farm()
    {
        var snow=Mat("Snow",.48f,.54f,.57f);var hay=Mat("Hay",.63f,.51f,.29f);
        Box("SnowGround",0,-.22f,0,70,.4f,62,snow);
        for(int side=-1;side<=1;side+=2)
        {
            Fence(side*8.3f,13,10);Fence(side*8.3f,-13,10);Fence(side*15,8.2f,7,true);Fence(side*15,-8.2f,7,true);
            for(int i=0;i<10;i++)for(int row=0;row<2;row++)Rock(side*(18+row*2.4f),-15+i*3.3f);
        }
        foreach(var p in new[]{new Vector2(-9,6),new Vector2(8,7),new Vector2(-9,-8)})
        {
            Box("HayPatch",p.x,-.005f,p.y,3.5f,.10f,2.8f,soil);
            for(int i=0;i<3;i++)Box("HayBale",p.x+(i-1)*.9f,.45f,p.y,.8f,.9f,1.4f,hay);
        }
        Box("Shed",12,1.3f,8,3,2.6f,5,wood);Box("SnowRoof",12,2.7f,8,3.4f,.3f,5.4f,snow);
        for(int i=0;i<9;i++)
        {
            float x=-15+i*3.8f,z=16+i%2*1.1f;
            Box("Trunk",x,1.5f,z,.4f,3,.4f,wood);Shape("Crown",PrimitiveType.Sphere,new Vector3(x,3,z),new Vector3(3,2.8f,3),grass);
            Shape("CrownSnow",PrimitiveType.Sphere,new Vector3(x,3.8f,z),new Vector3(2.7f,1.4f,2.7f),snow);
        }
        Hero(0,-6);for(int i=0;i<9;i++)Bull(new Vector3(-10+i%5*4.6f,0,-1+i/5*3.2f),80+i%3*10);
        Zombie(-4,-5);Zombie(6,-3);
    }
    static void Scale()
    {
        Box("Snow",0,-.15f,0,20,.2f,15,Mat("SnowScale",.48f,.54f,.57f));Hero(-3,0);Bull(new Vector3(0,0,0),15);Truck(new Vector3(3.7f,0,0));
        camera.orthographicSize=5.5f;camera.transform.position=new Vector3(9,6,-13);camera.transform.LookAt(new Vector3(.6f,.8f,0));
    }
    static void BullMotion()
    {
        Farm();var hero=root.GetComponentsInChildren<Game.CharacterWeaponPresenter>(true).First(p=>p.gameObject.name.Contains("GrayboxPlayer")).gameObject;
        foreach(var t in root.Cast<Transform>().Where(t=>t.name=="TEMP_BULL_GEOMETRY").ToArray())Object.DestroyImmediate(t.gameObject);
        var bulls=new List<Transform>();for(int i=0;i<12;i++)bulls.Add(Bull(Vector3.zero,90));
        camera.orthographicSize=12.7f;camera.transform.position=new Vector3(0,27,-21);camera.transform.LookAt(Vector3.zero);
        for(int frame=0;frame<90;frame++)
        {
            float t=frame/15f;
            for(int i=0;i<bulls.Count;i++)
            {
                float progress=Mathf.Clamp01((t-.8f)/3.5f),x=Mathf.Lerp(-19-i%4*2.9f,24-i%4*2.9f,progress);
                bulls[i].position=new Vector3(x,0,(i/4-1)*2.5f);bulls[i].localEulerAngles=new Vector3(t<.8f?Mathf.Sin(t*8)*3:0,90,0);
                foreach(Transform leg in bulls[i])if(leg.name=="Leg")leg.localRotation=Quaternion.Euler(t>.8f&&t<4.3f?Mathf.Sin(t*24+leg.localPosition.x*4+i)*22:0,0,0);
            }
            hero.transform.position=new Vector3(0,0,Mathf.Lerp(-1,-7,Mathf.Clamp01((t-.65f)/1.1f)));
            Capture("frames/herd-"+frame.ToString("D3")+".png",960,600);
        }
    }
    static void RaceMotion()
    {
        Box("CanyonFloor",0,-.25f,0,38,.4f,27,soil);
        for(int i=0;i<15;i++)for(int side=-1;side<=1;side+=2)Rock(-17+i*2.4f,side*11,3.8f);
        for(int side=-1;side<=1;side+=2)for(int i=0;i<8;i++)Rock(side*18,-8+i*2.3f,3.5f);
        for(int i=0;i<12;i++)
        {
            Shape("TireBarrier",PrimitiveType.Cylinder,new Vector3(-11+i*1.25f,.3f,2.6f),new Vector3(1,.30f,1),dark);
            Shape("TireBarrier",PrimitiveType.Cylinder,new Vector3(-2+i*1.25f,.3f,-2.6f),new Vector3(1,.30f,1),dark);
        }
        var ramp=Box("JumpRamp",-6,.40f,-6.5f,3,.6f,3.6f,wood);ramp.transform.localRotation=Quaternion.Euler(0,0,13);
        var truck=Truck(new Vector3(-13,0,-6.5f));Hero(-14,-2);Zombie(-1,-6.5f);Zombie(7,6.5f);
        var path=new[]{new Vector3(-13,0,-6.5f),new Vector3(10,0,-6.5f),new Vector3(13,0,-4),new Vector3(10,0,0),new Vector3(-11,0,0),new Vector3(-14,0,4),new Vector3(-10,0,6.5f),new Vector3(13,0,6.5f)};
        camera.orthographicSize=15;camera.transform.position=new Vector3(0,33,-26);camera.transform.LookAt(Vector3.zero);
        Capture("race.png",1440,900);
        for(int frame=0;frame<105;frame++)
        {
            float p=frame/104f*(path.Length-1);int segment=Mathf.Min((int)p,path.Length-2);var at=Vector3.Lerp(path[segment],path[segment+1],p-segment);
            if(segment==0&&at.x>-8&&at.x<-3)at.y=Mathf.Sin((at.x+8)/5*Mathf.PI)*1.35f;
            truck.position=at;truck.rotation=Quaternion.LookRotation(path[segment+1]-path[segment]);
            if(segment==0&&at.y>.05f)truck.rotation*=Quaternion.Euler((at.x<-5.5f?-1:1)*12,0,0);
            foreach(Transform wheel in truck)if(wheel.name=="Wheel")wheel.localRotation=Quaternion.Euler(frame*26,0,90);
            Capture("frames/race-"+frame.ToString("D3")+".png",960,600);
        }
    }
}
