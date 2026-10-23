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

/// <summary>二代后续三关的临时布局与动作候选；只在独立场景渲染，不保存或修改正式资源。</summary>
public static class NextThreeStagesPreview
{
    const int Layer=29;
    static readonly List<Object> owned=new List<Object>();
    static Transform root;static Camera camera;static Material ground,stone,lightStone,dark,wood,green,moss,blue,earth,cloth;
    static AnimationClip idle,run;static readonly List<Actor> actors=new List<Actor>();
    sealed class Actor {public GameObject Go;public Animator Animator;}
    public static string Graveyard()=>Capture(0);
    public static string Temple()=>Capture(1);
    public static string Marketplace()=>Capture(2);
    static string Capture(int stage)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态临时预览");
        var prior=SceneManager.GetActiveScene();var oldRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var oldAmbient=RenderSettings.ambientMode;
        try
        {
            SceneManager.SetActiveScene(scene);actors.Clear();Palette(stage);
            root=new GameObject("TEMP_NextThree_Unapproved_"+stage).transform;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.66f,.68f,.65f);
            RenderSettings.fog=false;
            idle=Clip("ArcadeEnemyIdle");run=Clip("ArcadeEnemyRun");
            var sun=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-28,0);sun.cullingMask=1<<Layer;
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<Layer;camera.farClipPlane=220;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.20f,.21f);
            Box("BackdropGround",0,-.4f,0,108,.5f,104,ground);
            if(stage==0)GraveMap();else if(stage==1)TempleMap();else MarketMap();
            var hero=Hero(stage==1?-3:0,stage==2?-6:-5);
            for(int i=0;i<6;i++)Zombie(new Vector3((i%3-1)*8,0,10+(i/3)*5),false);
            SetLayers();string name=stage==0?"graveyard":stage==1?"temple":"marketplace";
            string folder=Path.GetFullPath("Temp/NextThreeStages-v1/"+name);Directory.CreateDirectory(folder);Directory.CreateDirectory(folder+"/frames");
            camera.orthographicSize=36;camera.transform.position=new Vector3(0,74,-57);camera.transform.LookAt(Vector3.zero);
            Save(folder+"/overview.png",1500,1100);
            camera.orthographicSize=11;camera.transform.position=new Vector3(-5,17,-27);camera.transform.LookAt(new Vector3(-3,.6f,-5));
            Save(folder+"/detail.png",1280,800);
            foreach(var a in actors)a.Go.SetActive(false);
            if(stage==0)Burrow(folder);else if(stage==1)Shadow(folder);else Crowd(folder,hero);
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
        camera.orthographicSize=6.5f;camera.transform.position=new Vector3(-3,14,-22);camera.transform.LookAt(new Vector3(-3,.7f,-4));
        var zombie=Zombie(new Vector3(-7,0,-1),true);var crow=Group("TEMP_ShadowCrow",Vector3.zero);
        Poly("CrowBody",Vector3.zero,new Vector3(.24f,.22f,.45f),dark,6,crow);
        var wings=new[]{Box("WingL",-.55f,0,0,.95f,.06f,.42f,dark,crow).transform,Box("WingR",.55f,0,0,.95f,.06f,.42f,dark,crow).transform};
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
            for(int i=0;i<motes.Count;i++){motes[i].SetActive(orb.activeSelf);float angle=t*7+i*Mathf.PI/3;motes[i].transform.position=orb.transform.position+new Vector3(Mathf.Cos(angle),Mathf.Sin(angle)*.45f,Mathf.Sin(angle))*(.3f+i*.045f);}
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
    static void SetLayers(){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
    static void Save(string path,int width,int height)
    {
        var rt=RenderTexture.GetTemporary(width,height,24);var old=RenderTexture.active;var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(pixels);RenderTexture.ReleaseTemporary(rt);}
    }
}
