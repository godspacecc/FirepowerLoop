using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>二代后续三关的正式美术的临时实例候选；只在独立场景渲染，不保存或修改正式资源。</summary>
public static class NextThreeStagesArtPreview
{
    const int Layer=29;
    static readonly List<Object> owned=new List<Object>();
    static Transform root;static Camera camera;static Material ground,stone,lightStone,dark,wood,green,moss,blue,earth,cloth;
    static AnimationClip idle,run;static readonly List<Actor> actors=new List<Actor>();
    static bool tombMode;
    sealed class Actor {public GameObject Go;public Animator Animator;}
    public static string Graveyard()=>Capture(0);
    /// <summary>用户修正版：只录制从固定墓穴土面爬出的候选，不覆盖v1证据。</summary>
    public static string GraveyardTombsV2(){tombMode=true;try{return Capture(0);}finally{tombMode=false;}}
    public static string Temple()=>Capture(1);
    public static string Marketplace()=>Capture(2);
    static string Capture(int stage)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态临时预览");
        var prior=SceneManager.GetActiveScene();var oldRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);actors.Clear();Palette(stage);
            root=new GameObject("TEMP_NextThree_Unapproved_"+stage).transform;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.53f);
            RenderSettings.fog=false;
            idle=Clip("ArcadeEnemyIdle");run=Clip("ArcadeEnemyRun");
            var sun=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.05f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-28,0);sun.cullingMask=1<<Layer;
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<Layer;camera.farClipPlane=220;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.20f,.21f);
            Box("BackdropGround",0,-.4f,0,108,.5f,104,ground);
            if(stage==0)GraveArt();else if(stage==1)TempleArt();else MarketArt();
            var hero=Hero(stage==1?-3:0,stage==2?-6:-5);
            for(int i=0;i<6;i++)Zombie(new Vector3((i%3-1)*8,0,10+(i/3)*5),false);
            SetLayers();string name=stage==0?"graveyard":stage==1?"temple":"marketplace";
            string folder=Path.GetFullPath("Temp/"+(tombMode?"NextThreeStagesArt-v2/":"NextThreeStagesArt-v1/")+name);Directory.CreateDirectory(folder);Directory.CreateDirectory(folder+"/frames");
            camera.orthographicSize=36;camera.transform.position=new Vector3(0,74,-57);camera.transform.LookAt(Vector3.zero);
            Save(folder+"/overview.png",1500,1100);
            camera.orthographicSize=11;camera.transform.position=new Vector3(-5,17,-27);camera.transform.LookAt(new Vector3(-3,.6f,-5));
            Save(folder+"/detail.png",1280,800);
            // 移动预览主角位置，给出与代表物件同框的近景；拍完恢复，不改变正式角色尺寸。
            var heroAt=hero.transform.position;var focus=stage==0?new Vector3(8,1,-8):stage==1?new Vector3(0,1,0):new Vector3(-15,1,-14);
            if(stage==0)hero.transform.position=new Vector3(5,0,-8);else if(stage==2)hero.transform.position=new Vector3(-11,0,-12);
            camera.orthographicSize=stage==1?6:5.5f;camera.transform.position=focus+new Vector3(-5,9,-12);camera.transform.LookAt(focus);Save(folder+"/landmark.png",1280,800);hero.transform.position=heroAt;
            foreach(var a in actors)a.Go.SetActive(false);
            if(stage==0&&tombMode)TombBurrow(folder,hero);else if(stage==0)Burrow(folder);else if(stage==1)Shadow(folder);else Crowd(folder,hero);
            return folder+"；临时场景/手工动作采样，不是正式接入或自然对局录像";
        }
        finally
        {
            RenderTexture.active=oldRT;
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();actors.Clear();
        }
    }
    static void Palette(int stage)
    {
        ground=Mat("Earth",stage==0?new Color(.29f,.40f,.32f):stage==1?new Color(.60f,.55f,.38f):new Color(.62f,.51f,.35f));
        stone=Mat("Stone",stage==1?new Color(.55f,.55f,.44f):new Color(.38f,.46f,.45f));lightStone=Mat("PaleStone",new Color(.67f,.69f,.58f));
        dark=Mat("Dark",new Color(.10f,.16f,.17f));wood=Mat("Wood",new Color(.37f,.24f,.13f));green=Mat("Leaves",new Color(.24f,.38f,.21f));
        moss=Mat("Moss",new Color(.35f,.45f,.28f));blue=Mat("ShadowBlue",new Color(.12f,.67f,1),true);earth=Mat("TurnedEarth",new Color(.29f,.23f,.15f));cloth=Mat("OchreCanvas",new Color(.71f,.38f,.18f));
    }
    static Material Mat(string name,Color c,bool unlit=false)
    {var m=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit")){name="TEMP_"+name,color=c};if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.05f);owned.Add(m);return m;}
    static GameObject Box(string n,float x,float y,float z,float w,float h,float d,Material m,Transform parent=null)
    {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(parent??root,false);g.transform.localPosition=new Vector3(x,y,z);g.transform.localScale=new Vector3(w,h,d);g.GetComponent<Renderer>().sharedMaterial=m;Object.DestroyImmediate(g.GetComponent<Collider>());g.layer=Layer;return g;}
    static Transform Group(string n,Vector3 at)
    {var g=new GameObject(n);g.transform.SetParent(root,false);g.transform.localPosition=at;g.layer=Layer;return g.transform;}
    static GameObject Poly(string n,Vector3 at,Vector3 scale,Material m,int sides=7,Transform parent=null)
    {
        var points=new List<Vector3>();var indices=new List<int>();
        void Tri(Vector3 a,Vector3 b,Vector3 c){int k=points.Count;points.Add(a);points.Add(b);points.Add(c);indices.AddRange(new[]{k,k+1,k+2});}
        for(int i=0;i<sides;i++)
        {
            float a=i*2*Mathf.PI/sides,b=(i+1)*2*Mathf.PI/sides;
            var p=new Vector3(Mathf.Cos(a),-.5f,Mathf.Sin(a));var q=new Vector3(Mathf.Cos(b),-.5f,Mathf.Sin(b));
            var u=new Vector3(p.x*.73f,.35f,p.z*.73f);var v=new Vector3(q.x*.73f,.35f,q.z*.73f);
            Tri(p,u,q);Tri(q,u,v);Tri(u,new Vector3(.1f,.8f,-.1f),v);Tri(Vector3.down*.5f,p,q);
        }
        var mesh=new Mesh{name="TEMP_Faceted_"+n};mesh.SetVertices(points);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
        var g=new GameObject(n,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent??root,false);g.transform.localPosition=at;g.transform.localScale=scale;g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<MeshRenderer>().sharedMaterial=m;g.layer=Layer;return g;
    }
    static void Gate(float x,float z,float yaw,Material m,bool iron=false)
    {
        var t=Group("VisiblePassage",new Vector3(x,0,z));t.localRotation=Quaternion.Euler(0,yaw,0);
        for(int side=-1;side<=1;side+=2){Box("GatePillar",side*3.5f,2,0,1,4,1.35f,m,t);Box("Cap",side*3.5f,4.05f,0,1.35f,.3f,1.65f,lightStone,t);}
        if(!iron)Box("ArchLintel",0,4.45f,0,8.2f,.7f,1.5f,m,t);
        Box("PassageFloor",0,.015f,3,6,.04f,14,earth,t);
    }
    static void Tree(float x,float z,float size=1)
    {Box("Trunk",x,1.7f*size,z,.42f*size,3.4f*size,.42f*size,wood);Poly("LowPolyCrown",new Vector3(x,3.5f*size,z),new Vector3(2.1f,2.3f,1.8f)*size,green,7);}
    static void Wall(float x,float z,float w,float d,float h=3)
    {
        Box("SolidBoundaryWall",x,h/2,z,w,h,d,stone);Box("WallCoping",x,h+.14f,z,w+.15f,.28f,d+.2f,lightStone);
        for(float at=-w/2+.9f;at<w/2;at+=2.6f)Box("StoneJoint",x+at,h*.55f,z-d/2-.015f,.04f,h*.8f,.02f,dark);
    }
    static void Backdrop(int stage)
    {
        for(int side=-1;side<=1;side+=2)for(int i=-3;i<=3;i++)
        {
            if(i==0)continue;float a=i*8;
            if(stage==2)
            {
                Building(a,side*33,7,8,5+(i+3)%3);Building(side*36,a,8,7,5+(i+4)%3);
            }
            else
            {
                Poly("ImpassableRockMass",new Vector3(a,1.2f,side*35),new Vector3(6,5+(i+3)%3,7),stone);
                Poly("ImpassableRockMass",new Vector3(side*35,1.2f,a),new Vector3(7,5+(i+3)%3,6),stone);
                Tree(a,side*36,1.6f);Tree(side*36,a,1.4f);
            }
        }
    }
    static void GraveMap()
    {
        Poly("GraveyardGround",new Vector3(0,-.15f,0),new Vector3(28,.2f,28),moss,20);
        for(int i=0;i<24;i++)
        {
            if(i%6==0)continue;float a=i*Mathf.PI/12;var g=Box("EnclosingMasonry",Mathf.Sin(a)*26,1.35f,Mathf.Cos(a)*26,6.9f,2.7f,1.15f,stone);g.transform.rotation=Quaternion.Euler(0,i*15,0);
            for(int j=-1;j<=1;j++)Box("IronFinial",Mathf.Sin(a)*26+j*Mathf.Cos(a)*1.7f,3.05f,Mathf.Cos(a)*26-j*Mathf.Sin(a)*1.7f,.09f,1,.09f,dark);
        }
        for(int i=0;i<4;i++){float a=i*Mathf.PI/2;Gate(Mathf.Sin(a)*26,Mathf.Cos(a)*26,i*90,stone,true);}
        Box("WideCrossPath",0,.015f,0,9,.04f,52,earth);Box("WideCrossPath",0,.018f,0,52,.04f,7,earth);
        foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
        {
            for(int j=0;j<4;j++)
            {
                float x=sx*(8+j%2*5),z=sz*(8+j/2*5);
                Box("BurialSlab",x,.19f,z,2.2f,.38f,3.2f,stone);Box("Headstone",x,.95f,z+1.15f,1.55f,1.5f,.4f,lightStone);
                Poly("RoundedHeadstoneTop",new Vector3(x,1.6f,z+1.15f),new Vector3(.8f,.35f,.24f),lightStone,6);
                Box("OldSoil",x,.4f,z-.1f,1.6f,.03f,2,earth);
            }
            Tree(sx*20,sz*16,1.4f);
        }
        Building(-18,18,5,6,4);Backdrop(0);
    }
    static void TempleMap()
    {
        Box("Courtyard",0,-.1f,0,54,.2f,54,ground);
        foreach(int side in new[]{-1,1}){Wall(-16,side*27,22,1.4f,3.2f);Wall(16,side*27,22,1.4f,3.2f);Wall(side*27,-16,1.4f,22,3.2f);Wall(side*27,16,1.4f,22,3.2f);Gate(0,side*27,side<0?180:0,stone);Gate(side*27,0,side*90,stone);}
        var paving=Mat("MutedPaving",new Color(.57f,.55f,.43f));
        var pavingLight=Mat("MutedPavingLight",new Color(.62f,.60f,.48f));
        for(int x=-24;x<=24;x+=4)for(int z=-24;z<=24;z+=4)
        {
            if((x*7+z*13)%20==0&&Math.Abs(x)>8&&Math.Abs(z)>8)continue;
            Box("WeatheredPaving",x,.015f,z,3.88f,.035f,3.88f,((x+z)/4&1)==0?paving:pavingLight);
            if((x+z)%12==0&&Math.Abs(x)>12)Poly("StoneMoss",new Vector3(x,.055f,z),new Vector3(.7f,.03f,.9f),moss,6);
        }
        Box("LionPedestal",0,.38f,2,5,.76f,5,stone);Box("LionPedestalStep",0,.86f,2,4.2f,.25f,4.2f,lightStone);
        var lion=Group("TEMP_LionStatueCandidate",new Vector3(0,1,2));
        Poly("LionHaunch",new Vector3(0,1.25f,.45f),new Vector3(1.25f,1.15f,1.3f),stone,8,lion);
        Poly("Mane",new Vector3(0,2.2f,-.45f),new Vector3(1,1.1f,.8f),lightStone,9,lion);
        Box("Muzzle",0,2,-1.12f,1.0f,.55f,.7f,stone,lion);Box("Nose",0,2.15f,-1.5f,.52f,.28f,.13f,dark,lion);
        for(int side=-1;side<=1;side+=2){Box("FrontLeg",side*.72f,.6f,-.7f,.5f,1.2f,.6f,stone,lion);Box("Paw",side*.72f,.18f,-1.05f,.65f,.35f,1,lightStone,lion);Box("Eye",side*.39f,2.4f,-1.17f,.15f,.13f,.08f,dark,lion);}
        foreach(int side in new[]{-1,1})for(int i=-1;i<=1;i++){Column(side*21,i*11,4);Poly("MossBed",new Vector3(side*23,.07f,i*11),new Vector3(2,.1f,3),moss);}
        Backdrop(1);
    }
    static void Column(float x,float z,float h)
    {Box("ColumnBase",x,.2f,z,1.8f,.4f,1.8f,stone);Poly("FacetedColumn",new Vector3(x,h*.46f,z),new Vector3(.65f,h*.5f,.65f),lightStone,8);Box("Capital",x,h,z,1.7f,.4f,1.7f,stone);}
    static void Building(float x,float z,float w,float d,float h)
    {Box("PeripheralBuilding",x,h/2,z,w,h,d,stone);Box("RoofBand",x,h+.2f,z,w+.5f,.4f,d+.5f,lightStone);Box("DarkRecess",x,h*.48f,z-d*.501f,w*.38f,h*.7f,.06f,dark);}
    static void MarketMap()
    {
        Box("CompactMarketGround",0,-.1f,0,48,.2f,44,ground);
        for(int side=-1;side<=1;side+=2)
        {
            Wall(0,side*22,10,1.2f,3.2f);Wall(-20,side*22,8,1.2f,3.2f);Wall(20,side*22,8,1.2f,3.2f);
            Wall(side*24,0,1.2f,8,3.2f);Wall(side*24,-18,1.2f,8,3.2f);Wall(side*24,18,1.2f,8,3.2f);
            Gate(-11,side*22,side<0?180:0,stone);Gate(11,side*22,side<0?180:0,stone);
            Gate(side*24,-9,side*90,stone);Gate(side*24,9,side*90,stone);
        }
        Box("MarketCrosswalk",0,.015f,0,8,.035f,44,lightStone);Box("MarketCrosswalk",0,.018f,0,48,.035f,7,lightStone);
        for(int side=-1;side<=1;side+=2)for(int j=-1;j<=1;j+=2)Stall(side*15,j*14,side<0?90:-90);
        for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Box("WornPaver",Mathf.Cos(a)*8,.02f,Mathf.Sin(a)*8,2.5f,.04f,2.5f,stone);}
        Backdrop(2);
    }
    static void Stall(float x,float z,float yaw)
    {
        var t=Group("PeripheralMarketStall",new Vector3(x,0,z));t.localRotation=Quaternion.Euler(0,yaw,0);
        Box("Counter",0,.65f,0,5,1.3f,1.5f,wood,t);
        for(int i=-1;i<=1;i+=2){Box("AwningPost",i*2.5f,1.6f,.7f,.16f,3.2f,.16f,wood,t);}
        for(int i=0;i<6;i++){var g=Box("CanvasStrip",-2.5f+i*.85f,3,0,.85f,.12f,3.4f,i%2==0?cloth:lightStone,t);g.transform.localRotation=Quaternion.Euler(-8,0,0);}
        for(int i=-2;i<=2;i++)Poly("ProducePile",new Vector3(i,1.4f,0),new Vector3(.38f,.23f,.4f),i%2==0?green:cloth,6,t);
    }
    static AnimationClip Clip(string n)=>AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Arcade/Enemies/Humanoid/Animations/"+n+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
    static GameObject Hero(float x,float z)
    {
        var g=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),root);
        var p=g.GetComponent<CharacterWeaponPresenter>();p.SetWeaponId("arcade.weapon.basic");p.SetManualPoseSampling(true);p.BodyAnimator.Rebind();p.BodyAnimator.Update(0);p.Current.Animations.Idle.Clip.SampleAnimation(p.BodyAnimator.gameObject,.1f);p.ApplyOffhandGrip(p.Current.Animations.Idle);
        foreach(var c in g.GetComponentsInChildren<Canvas>(true))c.enabled=false;foreach(var c in g.GetComponentsInChildren<Collider>(true))c.enabled=false;g.transform.position=new Vector3(x,0,z);g.transform.rotation=Quaternion.Euler(0,0,0);return g;
    }
    static Actor Zombie(Vector3 at,bool shadow)
    {
        var g=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),root);
        foreach(var c in g.GetComponentsInChildren<Canvas>(true))c.enabled=false;foreach(var c in g.GetComponentsInChildren<Collider>(true))c.enabled=false;
        var a=g.GetComponentInChildren<Animator>(true);a.Rebind();a.Update(0);idle.SampleAnimation(a.gameObject,.1f);a.enabled=false;g.transform.position=at;g.transform.rotation=Quaternion.Euler(0,180,0);
        if(shadow)foreach(var r in g.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(_=>dark).ToArray();
        foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;var actor=new Actor{Go=g,Animator=a};actors.Add(actor);return actor;
    }
    static void Pose(Actor actor,Vector3 at,float time)
    {actor.Go.SetActive(true);run.SampleAnimation(actor.Animator.gameObject,time);actor.Go.transform.position=at;actor.Go.transform.rotation=Quaternion.Euler(0,180,0);}
    static void Burrow(string folder)
    {
        camera.orthographicSize=6;camera.transform.position=new Vector3(0,12,-18);camera.transform.LookAt(new Vector3(0,.7f,-1));
        var zombies=new List<Actor>();var soilPatches=new List<Transform>();
        foreach(var at in new[]{new Vector3(-5,0,0),new Vector3(5,0,0),new Vector3(0,0,5)})
        {zombies.Add(Zombie(at,false));soilPatches.Add(Poly("DisturbedSoil",at+Vector3.up*.07f,new Vector3(1,.13f,.85f),earth,9).transform);}
        for(int frame=0;frame<84;frame++)
        {
            float t=frame/12f;
            for(int i=0;i<zombies.Count;i++)
            {
                float age=t-.55f-i*.55f;var at=new[]{new Vector3(-5,0,0),new Vector3(5,0,0),new Vector3(0,0,5)}[i];
                zombies[i].Go.SetActive(age>=0);soilPatches[i].gameObject.SetActive(age>=0);
                if(age<0)continue;
                float rise=Mathf.Clamp01(age/1.1f);var pos=at+Vector3.up*Mathf.Lerp(-1.9f,0,rise);
                if(age>1.1f)pos=Vector3.MoveTowards(at,new Vector3(0,0,-5),Mathf.Min(3.6f,(age-1.1f)*1.1f));
                Pose(zombies[i],pos,age);soilPatches[i].localScale=new Vector3(1+.08f*Mathf.Sin(age*12)*(1-rise),.13f,.85f);
            }
            Save(folder+"/frames/"+frame.ToString("D3")+".png",960,600);
        }
    }
    static void Shadow(string folder)
    {
        camera.orthographicSize=6.5f;camera.transform.position=new Vector3(-3,14,-22);camera.transform.LookAt(new Vector3(-3,.7f,-3));
        var zombie=Zombie(new Vector3(-7,0,-1),true);var crow=Group("TEMP_ShadowCrow",Vector3.zero);
        Poly("CrowBody",Vector3.zero,new Vector3(.24f,.22f,.45f),dark,6,crow);
        var wings=ArtCrow(crow);
        var orb=Poly("BlueShadowTravel",Vector3.zero,Vector3.one*.4f,blue,7);var motes=new List<GameObject>();
        for(int i=0;i<6;i++)motes.Add(Poly("BlueMote",Vector3.zero,Vector3.one*.1f,blue,5));
        for(int frame=0;frame<96;frame++)
        {
            float t=frame/12f;crow.gameObject.SetActive(t<1.25f);zombie.Go.SetActive(t>=1.25f&&t<3.2f||t>=4.55f&&t<7.2f);orb.SetActive(t<1.45f||t>=3.2f&&t<4.65f||t>=7.2f);
            var a=new Vector3(-7,0,-1);var b=new Vector3(2,0,-2);
            crow.position=Vector3.Lerp(a+new Vector3(-5,5,2),a+Vector3.up*.5f,Mathf.Clamp01(t/1.25f));
            wings[0].localRotation=Quaternion.Euler(0,0,Mathf.Sin(t*25)*38);wings[1].localRotation=Quaternion.Euler(0,0,-Mathf.Sin(t*25)*38);
            if(t>=1.25f&&t<3.2f)Pose(zombie,Vector3.Lerp(a,new Vector3(-5,0,-4),Mathf.Clamp01((t-1.25f)/1.95f)),t);
            if(t>=4.55f&&t<7.2f)Pose(zombie,Vector3.Lerp(b,new Vector3(0,0,-4.5f),Mathf.Clamp01((t-4.55f)/2.65f)),t);
            float travel=Mathf.Clamp01((t-3.2f)/1.35f);orb.transform.position=(t<1.45f?a:Vector3.Lerp(new Vector3(-5,0,-4),b,travel))+Vector3.up*.65f;
            float scale=t<1.45f?.2f+.25f*Mathf.Sin(t*18)*Mathf.Sin(t*18):t<4.65f?.38f:t>=7.2f?Mathf.Max(0,.5f-(t-7.2f)*.7f):.01f;orb.transform.localScale=Vector3.one*scale;
            for(int i=0;i<motes.Count;i++){motes[i].SetActive(orb.activeSelf && scale>.015f);motes[i].transform.localScale=Vector3.one*Mathf.Min(.1f,scale*.35f);float angle=t*7+i*Mathf.PI/3;motes[i].transform.position=orb.transform.position+new Vector3(Mathf.Cos(angle),Mathf.Sin(angle)*.45f,Mathf.Sin(angle))*(.3f+i*.045f);}
            Save(folder+"/frames/"+frame.ToString("D3")+".png",960,600);
        }
    }
    static void Crowd(string folder,GameObject hero)
    {
        camera.orthographicSize=22;camera.transform.position=new Vector3(0,46,-34);camera.transform.LookAt(Vector3.zero);
        var crowd=new List<Actor>();for(int i=0;i<24;i++)crowd.Add(Zombie(Vector3.zero,i==23));
        for(int frame=0;frame<96;frame++)
        {
            float t=frame/12f;float h=t/8*2.2f;hero.transform.position=new Vector3(Mathf.Sin(h)*8,0,-6+Mathf.Cos(h)*3);
            for(int i=0;i<crowd.Count;i++)
            {
                int group=i/6;int side=new[]{2,0,3,1}[group];float age=t-(i%6)*.2f-group*.6f;crowd[i].Go.SetActive(age>=0);if(age<0)continue;
                var start=side==0?new Vector3(-11+(i%2)*22,0,29):side==1?new Vector3(31,0,-9+(i%2)*18):side==2?new Vector3(-11+(i%2)*22,0,-29):new Vector3(-31,0,-9+(i%2)*18);
                var destination=new Vector3((i%3-1)*3,0,-3+(i%5));var pos=Vector3.MoveTowards(start,destination,age*4.5f);Pose(crowd[i],pos,t+i*.2f);crowd[i].Go.transform.rotation=Quaternion.LookRotation((destination-start).normalized);
            }
            Save(folder+"/frames/"+frame.ToString("D3")+".png",960,600);
        }
    }
    /// <summary>先从墓碑前的土面升出，再跨过低矮墓沿；结束后才进入普通追击示意。</summary>
    static void TombBurrow(string folder,GameObject hero)
    {
        hero.transform.position=new Vector3(3,0,-12);camera.orthographicSize=8.3f;camera.transform.position=new Vector3(5,15,-28);camera.transform.LookAt(new Vector3(8,1,-10));
        var tombs=new[]{new Vector3(8,0,-8),new Vector3(13,0,-8),new Vector3(8,0,-13)};var enemies=new List<Actor>();var holes=new List<GameObject>();
        for(int i=0;i<tombs.Length;i++){enemies.Add(Zombie(tombs[i],false));holes.Add(Poly("OpenedGraveSoil",tombs[i]+new Vector3(0,.44f,-.35f),new Vector3(.69f,.035f,.65f),earth,7));}
        for(int frame=0;frame<108;frame++)
        {
            float t=frame/12f;for(int i=0;i<enemies.Count;i++)
            {
                float age=t-.4f-i*1.15f;enemies[i].Go.SetActive(age>=0);holes[i].SetActive(age>=0);if(age<0)continue;
                var at=tombs[i]+new Vector3(0,0,-.35f);float rise=Mathf.Clamp01(age/1.2f);var position=at+Vector3.up*Mathf.Lerp(-1.65f,.4f,rise);
                // 起身时停在墓穴内；随后越过墓沿到可行走地面，不在石碑实体内开始追击。
                if(age<=1.2f){idle.SampleAnimation(enemies[i].Animator.gameObject,age);enemies[i].Go.transform.position=position;enemies[i].Go.transform.rotation=Quaternion.Euler(0,180,0);}
                else if(age<2.1f){float u=(age-1.2f)/.9f;position=Vector3.Lerp(at+Vector3.up*.4f,tombs[i]+new Vector3(0,0,-2.5f),u)+Vector3.up*Mathf.Sin(u*Mathf.PI)*.23f;Pose(enemies[i],position,age);}
                else{position=Vector3.MoveTowards(tombs[i]+new Vector3(0,0,-2.5f),hero.transform.position,(age-2.1f)*1.1f);Pose(enemies[i],position,age);var facing=hero.transform.position-position;if(facing.sqrMagnitude>.001f)enemies[i].Go.transform.rotation=Quaternion.LookRotation(facing.normalized);}
            }
            Save(folder+"/frames/"+frame.ToString("D3")+".png",960,600);
        }
    }
    // 以下造型只挂在临时场景；采用之前不会持久化为正式网格、材质或Prefab。
    static Material tintA,tintB,tintC,metal,brick,roof,leafA,leafB;
    static Material ColorMat(string n,float r,float g,float b)=>Mat(n,new Color(r,g,b));
    static void ArtPalette(int stage)
    {
        metal=ColorMat("AgedIron",.12f,.19f,.20f);brick=ColorMat("BrickShadow",.32f,.36f,.33f);
        leafA=ColorMat("DeepLeaves",.16f,.29f,.22f);leafB=ColorMat("OliveLeaves",.33f,.40f,.23f);
        if(stage==0){ground.color=new Color(.24f,.31f,.28f);stone.color=new Color(.36f,.41f,.40f);lightStone.color=new Color(.53f,.58f,.52f);earth.color=new Color(.30f,.27f,.21f);moss.color=new Color(.30f,.38f,.28f);tintA=ColorMat("SlateLight",.42f,.47f,.44f);tintB=ColorMat("GraveGrass",.26f,.34f,.27f);tintC=ColorMat("Gravel",.40f,.39f,.30f);roof=ColorMat("OldSlate",.21f,.28f,.30f);}
        else if(stage==1){ground.color=new Color(.46f,.45f,.33f);stone.color=new Color(.48f,.48f,.38f);lightStone.color=new Color(.64f,.63f,.47f);tintA=ColorMat("OchreStone",.56f,.53f,.38f);tintB=ColorMat("MutedPaving",.53f,.52f,.40f);tintC=ColorMat("CarvedTeal",.19f,.35f,.33f);roof=ColorMat("TempleTerracotta",.38f,.27f,.20f);}
        else{ground.color=new Color(.63f,.53f,.37f);stone.color=new Color(.58f,.55f,.41f);lightStone.color=new Color(.75f,.68f,.47f);tintA=ColorMat("WarmPlaster",.67f,.56f,.39f);tintB=ColorMat("CoolPlaster",.45f,.55f,.49f);tintC=ColorMat("FadedRedCloth",.61f,.28f,.18f);roof=ColorMat("TerracottaRoof",.49f,.29f,.18f);}
    }
    static GameObject Form(string n,Vector3[] vertices,int[] tris,Material material,Transform parent)
    {
        var mesh=new Mesh{name="TEMP_Art_"+n};mesh.vertices=vertices;mesh.triangles=tris;mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
        var g=new GameObject(n,typeof(MeshFilter),typeof(MeshRenderer));g.layer=Layer;g.transform.SetParent(parent,false);g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<MeshRenderer>().sharedMaterial=material;return g;
    }
    static void Beam(string n,Vector3 a,Vector3 b,float width,Material m,Transform parent=null)
    {
        var g=Box(n,0,0,0,width,(b-a).magnitude,width,m,parent);g.transform.localPosition=(a+b)*.5f;g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
    }
    static void PitchedRoof(Transform t,float w,float d,float y,Material m)
    {
        var v=new[]{new Vector3(-w/2,y,-d/2),new Vector3(w/2,y,-d/2),new Vector3(-w/2,y,d/2),new Vector3(w/2,y,d/2),new Vector3(0,y+1.7f,-d/2),new Vector3(0,y+1.7f,d/2)};
        Form("SlopingRoof",v,new[]{0,2,4,2,5,4,4,5,1,1,5,3,0,4,1,2,3,5},m,t);
        for(int i=0;i<5;i++)Beam("RoofRidge",new Vector3(0,y+1.73f,-d/2+i*d/4),new Vector3(w/2,y+.02f,-d/2+i*d/4),.09f,brick,t);
    }
    static void Cypress(float x,float z,float size)
    {
        Box("CypressTrunk",x,1.5f*size,z,.45f*size,3*size,.45f*size,wood);
        for(int j=0;j<3;j++)Poly("FacetedCypress",new Vector3(x,(2.4f+j*1.05f)*size,z),new Vector3((1.4f-j*.3f)*size,1.8f*size,(1.2f-j*.25f)*size),j%2==0?leafA:leafB,6);
    }
    static void GroundDetails(int stage)
    {
        for(int i=0;i<85;i++)
        {
            float x=((i*37)%97-48)*.51f,z=((i*53)%101-50)*.48f;
            if(stage==0&&(Mathf.Abs(x)<5||Mathf.Abs(z)<4))continue;
            if(stage==1&&Mathf.Abs(x)<5&&Mathf.Abs(z-2)<5)continue;
            var g=Poly(stage==0?"GroundGrassPatch":"GroundWearPatch",new Vector3(x,.015f+(i%3)*.003f,z),new Vector3(1.2f+(i%5)*.38f,.015f,.55f+(i%4)*.27f),i%3==0?ground:stage==0?tintB:tintA,5+i%3);g.transform.rotation=Quaternion.Euler(0,i*47,0);
            if(i%6==0)for(int j=0;j<3;j++)Poly("SmallGroundStone",new Vector3(x+j*.22f,.08f,z+j*.16f),new Vector3(.16f,.10f,.12f),lightStone,5);
        }
    }
    static void SurroundingMasses(int stage)
    {
        if(stage==2)
        {
            for(int i=-3;i<=3;i++)foreach(int side in new[]{-1,1}){if(i==0)continue;MarketHouse(i*9,side*34,7.5f,7,5+i%2,side<0?0:180,(i+4)%3);MarketHouse(side*37,i*8.5f,8,7,5+(i+4)%2,side*90,(i+5)%3);}
            return;
        }
        for(int i=0;i<44;i++)
        {
            float a=i*Mathf.PI*2/44;float x=Mathf.Sin(a),z=Mathf.Cos(a);if(Mathf.Min(Mathf.Abs(x),Mathf.Abs(z))<.115f)continue;
            float r=stage==0?34.5f:39.5f;
            Poly("ContinuousOuterRockMass",new Vector3(x*r,1.1f,z*r),new Vector3(4.7f,3.7f+i%4,4.9f),i%3==0?tintA:stone,6+i%3);
            if(i%2==0)Cypress(x*(r+3),z*(r+3),1.2f+(i%3)*.15f);
        }
    }
    static void Tomb(float x,float z,int variation)
    {
        var t=Group("TombGroup_"+variation,new Vector3(x,0,z));t.localRotation=Quaternion.Euler(0,(variation%3-1)*5,0);
        Box("FoundationSlab",0,.16f,0,2.35f,.32f,3.35f,stone,t);Box("RecessedSoil",0,.34f,-.1f,1.94f,.06f,2.65f,earth,t);
        for(int side=-1;side<=1;side+=2)Box("GraveStoneRim",side*1.03f,.39f,0,.16f,.18f,3.1f,lightStone,t);
        if(variation%3==0)
        {Box("CrossUpright",0,1.25f,1.2f,.32f,1.9f,.35f,lightStone,t);Box("CrossArms",0,1.65f,1.2f,1.42f,.3f,.35f,lightStone,t);Box("CrossFoot",0,.48f,1.2f,.8f,.32f,.65f,stone,t);}
        else
        {Box("EngravedHeadstone",0,1.04f,1.2f,1.5f,1.45f,.4f,lightStone,t);if(variation%3==1)Poly("StoneCrown",new Vector3(0,1.73f,1.2f),new Vector3(.79f,.4f,.23f),lightStone,7,t);else Box("StoneCap",0,1.81f,1.2f,1.7f,.19f,.55f,stone,t);for(int j=0;j<3;j++)Box("CarvedInscription",0,1.36f-j*.16f,.993f,.68f-j*.12f,.035f,.014f,brick,t);}
        Poly("LowGraveMoss",new Vector3(-.72f,.38f,-.7f),new Vector3(.4f,.06f,.65f),moss,6,t);
        if(variation%4==0){Box("SmallCandle",.72f,.6f,.85f,.14f,.4f,.14f,lightStone,t);Box("CandleWick",.72f,.82f,.85f,.025f,.08f,.025f,dark,t);}
    }
    static void GraveArt()
    {
        ArtPalette(0);Poly("CircularGraveyardTurf",new Vector3(0,-.11f,0),new Vector3(28,.17f,28),moss,40);
        // 墓园四门、52米围墙和中央通路沿用已采用布局，仅细化表面与构件。
        for(int i=0;i<24;i++)
        {
            if(i%6==0)continue;float a=i*Mathf.PI/12;var t=Group("MasonryAndIron",new Vector3(Mathf.Sin(a)*26,0,Mathf.Cos(a)*26));t.localRotation=Quaternion.Euler(0,i*15,0);
            Box("StoneWallPlinth",0,.95f,0,6.9f,1.9f,1.15f,stone,t);Box("Coping",0,1.98f,0,7.05f,.22f,1.3f,tintA,t);
            for(int j=-3;j<=3;j++){Box("IronPicket",j*.93f,2.68f,0,.075f,1.35f,.075f,metal,t);Poly("IronSpear",new Vector3(j*.93f,3.38f,0),new Vector3(.10f,.22f,.10f),metal,4,t);Box("StoneFaceBlock",j*.97f,.6f+(j%2)*.28f,-.59f,.88f,.48f,.06f,j%2==0?tintA:brick,t);}
            Box("IronRail",0,2.85f,0,6.7f,.075f,.075f,metal,t);
        }
        for(int i=0;i<4;i++){float a=i*Mathf.PI/2;Gate(Mathf.Sin(a)*26,Mathf.Cos(a)*26,i*90,stone,true);}
        Box("WideCrossPath",0,.015f,0,9,.035f,52,earth);Box("WideCrossPath",0,.018f,0,52,.035f,7,earth);
        for(int i=0;i<55;i++){float x=(i*17%89-44)*.55f,z=(i*31%93-46)*.52f;if(Mathf.Abs(x)<4.5f||Mathf.Abs(z)<3.5f){var g=Poly("PathGravel",new Vector3(x,.046f,z),new Vector3(.35f,.014f,.21f),tintC,5);g.transform.rotation=Quaternion.Euler(0,i*31,0);}}
        int n=0;foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){for(int j=0;j<4;j++)Tomb(sx*(8+j%2*5),sz*(8+j/2*5),n++);Cypress(sx*20,sz*16,1.45f);}
        var chapel=Group("SmallOuterChapel",new Vector3(-18,0,18));Box("ChapelBase",0,.2f,0,5.4f,.4f,6.4f,stone,chapel);Box("ChapelWalls",0,2.1f,0,5,4,6,stone,chapel);PitchedRoof(chapel,5.8f,6.7f,4.1f,roof);Box("DarkChapelDoor",0,1.5f,-3.03f,1.7f,2.5f,.04f,metal,chapel);Box("DoorFrameTop",0,2.9f,-3.12f,2.2f,.35f,.3f,lightStone,chapel);for(int side=-1;side<=1;side+=2){Box("DoorSide",side*1.02f,1.5f,-3.12f,.28f,2.6f,.3f,lightStone,chapel);Box("ChapelNarrowWindow",side*1.78f,2.7f,-3.05f,.35f,1,.08f,metal,chapel);}Box("ChapelCross",0,5.35f,-2.3f,.18f,1.6f,.18f,lightStone,chapel);Box("ChapelCross",0,5.65f,-2.3f,.85f,.18f,.18f,lightStone,chapel);
        GroundDetails(0);SurroundingMasses(0);
    }
    static void TempleGate(float x,float z,float yaw)
    {
        var t=Group("CarvedTempleGate",new Vector3(x,0,z));t.localRotation=Quaternion.Euler(0,yaw,0);
        for(int side=-1;side<=1;side+=2){Box("GateFoundation",side*3.5f,.22f,0,1.5f,.44f,2,stone,t);Box("Pillar",side*3.5f,2.2f,0,1.05f,4.4f,1.4f,tintA,t);Box("VerticalTealInlay",side*3.5f,2.3f,-.72f,.25f,2.3f,.05f,tintC,t);Box("Cap",side*3.5f,4.42f,0,1.55f,.34f,2,lightStone,t);}
        Box("TempleArchitrave",0,4.75f,0,8.3f,.55f,1.7f,lightStone,t);Box("ArchTealBand",0,4.78f,-.88f,7.8f,.14f,.05f,tintC,t);Box("SteppedLintel",0,5.12f,0,8.8f,.2f,2.1f,tintA,t);Box("PassageFloor",0,.015f,3,6,.035f,14,earth,t);
    }
    static void LionArt()
    {
        Box("LionPedestal",0,.38f,2,5,.76f,5,stone);Box("CarvedPedestalStep",0,.86f,2,4.2f,.25f,4.2f,lightStone);for(int side=-1;side<=1;side+=2)Box("PedestalInlay",0,.48f,2+side*2.51f,3.5f,.12f,.025f,tintC);
        var t=Group("FacetedGuardianLion",new Vector3(0,1,2));
        Poly("SeatedLionHaunch",new Vector3(0,1.13f,.48f),new Vector3(1.22f,1.04f,1.14f),stone,9,t);Poly("LionChest",new Vector3(0,1.42f,-.12f),new Vector3(.82f,1.22f,.8f),lightStone,8,t);
        for(int i=0;i<12;i++){float a=i*Mathf.PI*2/12;Poly("CarvedManeLock",new Vector3(Mathf.Sin(a)*.78f,2.15f+Mathf.Cos(a)*.79f,-.5f),new Vector3(.32f,.42f,.38f),i%2==0?stone:tintA,5,t);}
        Poly("LionFace",new Vector3(0,2.24f,-.74f),new Vector3(.66f,.7f,.48f),lightStone,7,t);for(int side=-1;side<=1;side+=2){Poly("RoundEar",new Vector3(side*.56f,2.79f,-.48f),new Vector3(.22f,.22f,.20f),stone,6,t);Poly("MuzzleLobe",new Vector3(side*.23f,1.99f,-1.12f),new Vector3(.31f,.24f,.25f),tintA,7,t);Box("CarvedEye",side*.30f,2.41f,-1.055f,.18f,.08f,.06f,brick,t);var brow=Box("Brow",side*.31f,2.51f,-1.04f,.38f,.10f,.12f,stone,t);brow.transform.localRotation=Quaternion.Euler(0,0,side*10);Box("StraightForeleg",side*.72f,.66f,-.56f,.5f,1.25f,.65f,lightStone,t);Poly("BroadPaw",new Vector3(side*.73f,.18f,-.98f),new Vector3(.5f,.22f,.65f),lightStone,6,t);for(int j=0;j<3;j++)Box("PawToeGroove",side*.73f+(j-1)*.17f,.25f,-1.48f,.022f,.12f,.11f,stone,t);}
        Poly("Nose",new Vector3(0,2.15f,-1.35f),new Vector3(.23f,.14f,.11f),stone,3,t);Box("Mouth",0,1.83f,-1.26f,.44f,.045f,.04f,brick,t);
        Beam("CurledTail",new Vector3(.9f,.45f,.8f),new Vector3(1.37f,.55f,1.22f),.20f,stone,t);Beam("CurledTail",new Vector3(1.37f,.55f,1.22f),new Vector3(1.1f,.85f,1.45f),.20f,stone,t);
    }
    static void TempleArt()
    {
        ArtPalette(1);Box("TempleGround",0,-.1f,0,54,.2f,54,ground);
        foreach(int side in new[]{-1,1}){Wall(-16,side*27,22,1.4f,3.2f);Wall(16,side*27,22,1.4f,3.2f);Wall(side*27,-16,1.4f,22,3.2f);Wall(side*27,16,1.4f,22,3.2f);TempleGate(0,side*27,side<0?180:0);TempleGate(side*27,0,side*90);for(int i=-4;i<=4;i++){if(Mathf.Abs(i)<2)continue;Box("WallTealBand",i*5,2.5f,side*26.28f,4.8f,.17f,.05f,tintC);Box("WallTealBand",side*26.28f,2.5f,i*5,.05f,.17f,4.8f,tintC);}}
        for(int x=-24;x<=24;x+=3)for(int z=-24;z<=24;z+=3)
        {int k=Mathf.Abs(x/3*13+z/3*7);Box("IrregularCourtyardPaver",x,.025f,z,2.91f,.045f,2.91f,k%7==0?tintA:k%3==0?lightStone:tintB);if(k%9==0&&Mathf.Abs(x)>10){Box("TileHairline",x+.5f,.052f,z,.025f,.005f,1.4f,ground);Poly("PaverCornerMoss",new Vector3(x+1.2f,.055f,z-.8f),new Vector3(.3f,.02f,.45f),moss,5);}}
        for(int j=-12;j<=12;j+=3){Box("CentralPathBorder",-5,.055f,j, .12f,.015f,2.8f,tintC);Box("CentralPathBorder",5,.055f,j,.12f,.015f,2.8f,tintC);}
        LionArt();foreach(int side in new[]{-1,1})for(int i=-1;i<=1;i++){Column(side*21,i*11,4);for(int j=0;j<3;j++)Box("ColumnRing",side*21,1+j*1.1f,i*11,1.35f,.13f,1.35f,tintA);Poly("ColumnMossBed",new Vector3(side*23,.07f,i*11),new Vector3(2,.09f,3),moss);}
        GroundDetails(1);SurroundingMasses(1);
    }
    static void MarketHouse(float x,float z,float w,float d,float h,float yaw,int variation)
    {
        var t=Group("PeripheralPlasterHouse",new Vector3(x,0,z));t.localRotation=Quaternion.Euler(0,yaw,0);var plaster=variation==0?tintA:variation==1?tintB:lightStone;
        Box("StoneFoundation",0,.35f,0,w+.3f,.7f,d+.3f,stone,t);Box("PlasterWalls",0,h/2,0,w,h,d,plaster,t);Box("WallShadowBase",0,.85f,-d/2-.015f,w,.22f,.06f,brick,t);PitchedRoof(t,w+.7f,d+.9f,h,roof);
        Box("InsetDoor",0,1.4f,-d/2-.025f,1.4f,2.8f,.08f,wood,t);for(int side=-1;side<=1;side+=2){Box("DoorFrame",side*.8f,1.5f,-d/2-.1f,.18f,3,.15f,stone,t);Box("WindowRecess",side*w*.3f,h*.67f,-d/2-.04f,1.2f,1.2f,.05f,dark,t);for(int bar=-1;bar<=1;bar++)Box("WindowIron",side*w*.3f+bar*.32f,h*.67f,-d/2-.09f,.055f,1.2f,.055f,metal,t);Box("WindowSill",side*w*.3f,h*.67f-.68f,-d/2-.18f,1.55f,.15f,.4f,stone,t);}
        Box("Chimney",w*.28f,h+1,0,.65f,2,.7f,stone,t);Box("ChimneyCap",w*.28f,h+2.07f,0,.85f,.18f,.9f,brick,t);
        for(int j=0;j<3;j++)Box("PlasterRepair",-w*.35f+j*.22f,1+j*.17f,-d/2-.031f,.3f,.16f,.02f,lightStone,t);
    }
    static void MarketStallArt(float x,float z,float yaw,int variant)
    {
        var t=Group("StripedMarketStall",new Vector3(x,0,z));t.localRotation=Quaternion.Euler(0,yaw,0);
        Box("StallPlankCounter",0,.72f,0,5,1.44f,1.5f,wood,t);for(int i=-5;i<=5;i++)Box("CounterFrontPlank",i*.44f,.74f,-.77f,.40f,1.27f,.08f,i%2==0?wood:brick,t);
        for(int side=-1;side<=1;side+=2){Box("AwningPost",side*2.5f,1.6f,.7f,.15f,3.2f,.15f,wood,t);Beam("AwningBrace",new Vector3(side*2.5f,2.35f,.7f),new Vector3(side*1.8f,3,.7f),.12f,wood,t);}
        for(int i=0;i<10;i++){var m=i%2==0?(variant%2==0?tintC:tintB):lightStone;var g=Box("SlopedFabricStripe",-2.25f+i*.5f,3,0,.5f,.10f,3.4f,m,t);g.transform.localRotation=Quaternion.Euler(-8,0,0);Box("ScallopedClothEdge",-2.25f+i*.5f,2.67f,-1.65f,.49f,.24f,.055f,m,t);}
        for(int i=-2;i<=2;i++){Box("ProduceTray",i,.1f+1.43f,0,.91f,.13f,1.1f,lightStone,t);for(int j=0;j<4;j++)Poly("LowPolyProduce",new Vector3(i+(j%2-.5f)*.30f,1.7f,(j/2-.5f)*.34f),new Vector3(.21f,.19f,.20f),i%2==0?leafB:tintC,6,t);}
        for(int side=-1;side<=1;side+=2){Poly("Barrel",new Vector3(side*3.25f,.62f,.35f),new Vector3(.52f,1,.52f),wood,10,t);for(int j=0;j<2;j++)Poly("BarrelIronHoop",new Vector3(side*3.25f,.35f+j*.52f,.35f),new Vector3(.55f,.08f,.55f),metal,10,t);}
    }
    static void MarketArt()
    {
        ArtPalette(2);Box("MarketGround",0,-.1f,0,48,.2f,44,ground);
        for(int side=-1;side<=1;side+=2){Wall(0,side*22,10,1.2f,3.2f);Wall(-20,side*22,8,1.2f,3.2f);Wall(20,side*22,8,1.2f,3.2f);Wall(side*24,0,1.2f,8,3.2f);Wall(side*24,-18,1.2f,8,3.2f);Wall(side*24,18,1.2f,8,3.2f);Gate(-11,side*22,side<0?180:0,stone);Gate(11,side*22,side<0?180:0,stone);Gate(side*24,-9,side*90,stone);Gate(side*24,9,side*90,stone);}
        Box("MarketCrosswalk",0,.015f,0,8,.035f,44,tintA);Box("MarketCrosswalk",0,.018f,0,48,.035f,7,tintA);
        for(int i=-10;i<=10;i++)for(int j=-2;j<=2;j++){Box("MarketWalkPaver",i*2.15f,.045f,j*1.25f,2.03f,.025f,1.15f,(i+j)%3==0?lightStone:stone);if(Mathf.Abs(i)>2)Box("MarketWalkPaver",j*1.25f,.043f,i*1.92f,1.15f,.022f,1.79f,(i+j)%3==0?lightStone:stone);}
        int n=0;for(int side=-1;side<=1;side+=2)for(int j=-1;j<=1;j+=2)MarketStallArt(side*15,j*14,side<0?90:-90,n++);
        for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Box("WornPaver",Mathf.Cos(a)*8,.02f,Mathf.Sin(a)*8,2.5f,.04f,2.5f,stone);}
        GroundDetails(2);SurroundingMasses(2);
    }
    static Transform[] ArtCrow(Transform crow)
    {
        Poly("CrowNeck",new Vector3(0,.12f,-.33f),new Vector3(.18f,.24f,.21f),dark,7,crow);Poly("CrowHead",new Vector3(0,.26f,-.46f),new Vector3(.22f,.20f,.24f),dark,7,crow);Poly("CrowBeak",new Vector3(0,.23f,-.7f),new Vector3(.08f,.08f,.23f),metal,3,crow);
        Form("PointedTail",new[]{new Vector3(-.16f,0,.25f),new Vector3(.16f,0,.25f),new Vector3(0,-.07f,.86f)},new[]{0,1,2,2,1,0},dark,crow);
        var wings=new List<Transform>();foreach(int side in new[]{-1,1}){var t=new GameObject("ArticulatedWing").transform;t.SetParent(crow,false);var v=new[]{new Vector3(0,0,0),new Vector3(side*.85f,.03f,-.25f),new Vector3(side*1.12f,0,.15f),new Vector3(side*.82f,0,.48f),new Vector3(side*.37f,0,.35f)};Form("AngularFlightFeathers",v,new[]{0,1,2,0,2,3,0,3,4,2,1,0,3,2,0,4,3,0},dark,t);for(int j=0;j<4;j++)Beam("SeparateWingTip",new Vector3(side*(.6f+j*.12f),0,.12f),new Vector3(side*(.72f+j*.12f),0,.48f-j*.04f),.075f,dark,t);wings.Add(t);}
        return wings.ToArray();
    }
    static void SetLayers(){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
    static void Save(string path,int width,int height)
    {
        var rt=RenderTexture.GetTemporary(width,height,24);var old=RenderTexture.active;var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(pixels);RenderTexture.ReleaseTemporary(rt);}
    }
}
