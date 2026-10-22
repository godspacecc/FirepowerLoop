using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>正式牛外观候选的独立可运行模型预览；未获确认前不保存任何正式资源。</summary>
public static class FarmCowArtPreview
{
    static readonly List<Object> Owned=new List<Object>();
    static Transform root;static Camera camera;static Material white,brown,hoof,horn,nose,eye;
    static readonly int Layer=30;static string folder;static bool Applied;
    public static string RunApplied(){Applied=true;try{return Run();}finally{Applied=false;}}
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态独立预览");
        var prior=SceneManager.GetActiveScene();var priorRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);folder=Path.GetFullPath(Applied?"Temp/FarmCowApplied-v1":"Temp/FarmCowArt-v1");Directory.CreateDirectory(folder);
            root=new GameObject("TEMP_COW_ART_CANDIDATE").transform;
            InitializeMaterials();
            Box(root,"SnowFloor",new Vector3(0,-.12f,0),new Vector3(18,.2f,15),Mat("Snow",new Color(.43f,.51f,.55f)));
            var cow=Applied?Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab"),root).transform:BuildCow(root);if(Applied)cow.GetComponent<Game.ArcadeHerdVisual>().Bind(10,1);cow.position=new Vector3(.75f,0,0);cow.rotation=Quaternion.Euler(0,-20,0);
            var hero=Hero(root,new Vector3(-1.65f,0,-.35f));
            var light=new GameObject("CowSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.35f;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var fill=new GameObject("CowFill",typeof(Light)).GetComponent<Light>();fill.type=LightType.Directional;fill.intensity=.4f;fill.cullingMask=1<<Layer;fill.transform.rotation=Quaternion.Euler(35,140,0);
            camera=new GameObject("CowCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<Layer;camera.orthographic=true;camera.orthographicSize=2.55f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.085f,.105f);camera.transform.position=new Vector3(6,3.4f,7);camera.transform.LookAt(new Vector3(0,.85f,0));
            SetLayer();Capture("cow-scale.png",1280,800);
            hero.SetActive(false);cow.position=Vector3.zero;camera.orthographicSize=1.85f;camera.transform.position=new Vector3(4,2.5f,5);camera.transform.LookAt(new Vector3(0,.85f,.15f));Capture("cow-detail.png",1120,800);
            hero.SetActive(true);cow.position=new Vector3(.75f,0,0);camera.orthographicSize=2.55f;camera.transform.position=new Vector3(6,3.4f,7);camera.transform.LookAt(new Vector3(0,.85f,0));
            for(int frame=0;frame<105;frame++)
            {
                float t=frame/15f;float angle=-20+Mathf.Sin(t*.7f)*16;
                if(Applied){if(!cow.GetComponent<Game.ArcadeHerdVisual>().Project(new FirepowerLoop.Arcade.ArcadeHerdSnapshot(10,1,t<.8f?FirepowerLoop.Arcade.ArcadeHerdPhase.Windup:FirepowerLoop.Arcade.ArcadeHerdPhase.Charging,.75f,0,Mathf.Sin(angle*Mathf.Deg2Rad),Mathf.Cos(angle*Mathf.Deg2Rad),t<.8f?t:t-.8f)))throw new InvalidOperationException("正式牛投影失败");}
                else{Pose(cow,t);cow.rotation=Quaternion.Euler(0,angle,0);}Capture("cow-"+frame.ToString("D3")+".png",960,600);
            }
            return folder;
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=priorRT;SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var item in Owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);Owned.Clear();
        }
    }
    static void InitializeMaterials()
    {
            white=Mat("WarmCoat",new Color(.79f,.76f,.66f));brown=Mat("BrownPatches",new Color(.20f,.095f,.045f));hoof=Mat("Hoof",new Color(.075f,.065f,.05f));
            horn=Mat("Horn",new Color(.84f,.77f,.57f));nose=Mat("Muzzle",new Color(.36f,.22f,.18f));eye=Mat("Eye",new Color(.025f,.018f,.01f));
    }
    /// <summary>仅在v1已获用户确认后调用，保存同源模型与显式动作引用。</summary>
    public static string ApplyConfirmed()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态正式接入");
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        const string assets="Assets/Game/Art/Arcade/Farm/Cow";
        try
        {
            SceneManager.SetActiveScene(scene);Directory.CreateDirectory(assets);AssetDatabase.Refresh();InitializeMaterials();root=new GameObject("ConfirmedCowAuthoring").transform;var cow=BuildCow(root);cow.name="ArcadeHerdVisual";
            var meshes=new Dictionary<Mesh,Mesh>();int index=0;
            foreach(var filter in cow.GetComponentsInChildren<MeshFilter>(true))
            {
                var source=filter.sharedMesh;if(!meshes.TryGetValue(source,out var saved))
                {
                    var candidate=Object.Instantiate(source);candidate.name="CowMesh"+index;var path=assets+"/CowMesh"+(index++)+".asset";
                    saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){AssetDatabase.CreateAsset(candidate,path);saved=candidate;}else{EditorUtility.CopySerialized(candidate,saved);Object.DestroyImmediate(candidate);AssetDatabase.SaveAssetIfDirty(saved);}
                    meshes[source]=saved;
                }
                filter.sharedMesh=saved;
            }
            var materials=new Dictionary<Material,Material>();
            foreach(var renderer in cow.GetComponentsInChildren<MeshRenderer>(true))
            {
                var palette=renderer.sharedMaterials;
                for(int i=0;i<palette.Length;i++)
                {
                    var source=palette[i];if(!materials.TryGetValue(source,out var saved))
                    {
                        string path=assets+"/"+source.name.Replace("TEMP_COW_","")+".mat";saved=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(saved==null){saved=new Material(source);saved.name=source.name.Replace("TEMP_COW_","");AssetDatabase.CreateAsset(saved,path);}else{EditorUtility.CopySerialized(source,saved);saved.name=source.name.Replace("TEMP_COW_","");AssetDatabase.SaveAssetIfDirty(saved);}materials[source]=saved;
                    }
                    palette[i]=saved;
                }
                renderer.sharedMaterials=palette;
            }
            var view=cow.gameObject.AddComponent<Game.ArcadeHerdVisual>();var serialized=new SerializedObject(view);
            serialized.FindProperty("bodyPose").objectReferenceValue=cow.Find("BodyPose");serialized.FindProperty("headPose").objectReferenceValue=cow.Find("BodyPose/HeadPose");serialized.FindProperty("tailPose").objectReferenceValue=cow.Find("BodyPose/TailPose");
            var hips=cow.Cast<Transform>().Where(t=>t.name.StartsWith("Leg_")).ToArray();var hipField=serialized.FindProperty("hips");var kneeField=serialized.FindProperty("knees");hipField.arraySize=kneeField.arraySize=hips.Length;
            for(int i=0;i<hips.Length;i++){hipField.GetArrayElementAtIndex(i).objectReferenceValue=hips[i];kneeField.GetArrayElementAtIndex(i).objectReferenceValue=hips[i].Find("Knee");}
            serialized.ApplyModifiedPropertiesWithoutUndo();if(!view.Validate(out var error))throw new InvalidOperationException(error);
            PrefabUtility.SaveAsPrefabAsset(cow.gameObject,"Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab");return "已接入用户确认的牛v1；全部Mesh/Material归Game/Art，尚未开启地图牛群";
        }
        finally
        {
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var item in Owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);Owned.Clear();
        }
    }
    /// <summary>体量保持已采用v3，椭球低模、花斑、弯角和分节腿构成实际可渲染模型。</summary>
    static Transform BuildCow(Transform parent)
    {
        var cow=Group(parent,"CowCandidate");var body=Group(cow,"BodyPose");
        Oval(body,"CoatBody",new Vector3(0,1.02f,-.10f),new Vector3(.53f,.43f,.93f),true,white);
        Oval(body,"Shoulder",new Vector3(0,1.08f,.48f),new Vector3(.48f,.42f,.51f),true,white);
        var head=Group(body,"HeadPose");head.localPosition=new Vector3(0,1.18f,.80f);
        Oval(head,"Forehead",new Vector3(0,.02f,.30f),new Vector3(.31f,.31f,.38f),false,brown);
        Oval(head,"WhiteFace",new Vector3(0,.10f,.61f),new Vector3(.13f,.21f,.075f),false,white);
        Oval(head,"Muzzle",new Vector3(0,-.18f,.68f),new Vector3(.30f,.18f,.22f),false,nose);
        for(int side=-1;side<=1;side+=2)
        {
            Oval(head,"Nostril",new Vector3(side*.15f,-.14f,.875f),new Vector3(.045f,.035f,.015f),false,eye);
            Oval(head,"EyeWhite",new Vector3(side*.284f,.07f,.48f),new Vector3(.045f,.065f,.074f),false,white);
            Oval(head,"Eye",new Vector3(side*.322f,.07f,.49f),new Vector3(.017f,.043f,.045f),false,eye);
            var ear=Oval(head,"Ear",new Vector3(side*.41f,.10f,.13f),new Vector3(.24f,.075f,.13f),false,brown);ear.localRotation=Quaternion.Euler(0,side*-20,side*18);
            Oval(ear,"EarInner",new Vector3(0,.58f,.05f),new Vector3(.66f,.14f,.55f),false,nose);
            Taper(head,"HornBase",new Vector3(side*.24f,.22f,.15f),new Vector3(side*.48f,.40f,.14f),.085f,.048f,horn);
            Taper(head,"HornTip",new Vector3(side*.48f,.40f,.14f),new Vector3(side*.54f,.58f,.30f),.048f,.008f,horn);
        }
        for(int side=-1;side<=1;side+=2)for(int front=-1;front<=1;front+=2)
        {
            var hip=Group(cow,"Leg_"+side+"_"+front);hip.localPosition=new Vector3(side*.36f,.88f,front*.60f);
            Taper(hip,"UpperLeg",Vector3.zero,new Vector3(0,-.43f,.02f),.14f,.09f,front>0?brown:white);
            var knee=Group(hip,"Knee");knee.localPosition=new Vector3(0,-.40f,.02f);
            Taper(knee,"LowerLeg",Vector3.zero,new Vector3(0,-.32f,.035f),.09f,.065f,white);
            Box(knee,"DarkHoof",new Vector3(0,-.38f,.075f),new Vector3(.20f,.16f,.27f),hoof);
        }
        var tail=Group(body,"TailPose");tail.localPosition=new Vector3(0,1.13f,-1.0f);
        Taper(tail,"Tail",Vector3.zero,new Vector3(.10f,-.60f,-.17f),.04f,.025f,white);
        Oval(tail,"TailTuft",new Vector3(.10f,-.66f,-.17f),new Vector3(.08f,.15f,.09f),false,brown);
        return cow;
    }
    /// <summary>独立起势与奔跑采样，只改局部骨架；不添加声音、碰撞或伤害。</summary>
    static void Pose(Transform cow,float seconds)
    {
        bool charge=seconds>=.8f;float time=Mathf.Max(0,seconds-.8f);var body=cow.Find("BodyPose");
        body.localPosition=new Vector3(0,charge?Mathf.Sin(time*18)*.025f:0,0);body.Find("HeadPose").localRotation=Quaternion.Euler(charge?7:Mathf.Sin(seconds*4)*3+12,0,0);
        foreach(Transform hip in cow)
        {
            if(!hip.name.StartsWith("Leg_"))continue;
            bool diagonal=(hip.localPosition.x>0)==(hip.localPosition.z>0);float phase=time*15+(diagonal?0:Mathf.PI);
            hip.localRotation=Quaternion.Euler(charge?Mathf.Sin(phase)*25:0,0,0);hip.Find("Knee").localRotation=Quaternion.Euler(charge?Mathf.Max(0,Mathf.Sin(phase+.6f))*24:0,0,0);
        }
        body.Find("TailPose").localRotation=Quaternion.Euler(0,0,Mathf.Sin(seconds*5)*8);
    }
    static Transform Oval(Transform parent,string name,Vector3 at,Vector3 scale,bool patches,Material material)
    {
        var vertices=new List<Vector3>();var triangles=new[]{new List<int>(),new List<int>()};
        int rows=7,columns=12;
        for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
        {
            var a=Point(row,col,rows,columns);var b=Point(row+1,col,rows,columns);var c=Point(row+1,col+1,rows,columns);var d=Point(row,col+1,rows,columns);
            Face(a,b,c);Face(a,c,d);
        }
        void Face(Vector3 a,Vector3 b,Vector3 c)
        {
            var mid=(a+b+c)/3;int index=patches&&Mathf.Sin(mid.z*6+mid.x*3)+Mathf.Cos(mid.y*5-mid.z*2)>.35f?1:0;
            int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles[index].AddRange(new[]{start,start+2,start+1});
        }
        var mesh=new Mesh{name="PreviewCow_"+name};Owned.Add(mesh);mesh.SetVertices(vertices);mesh.subMeshCount=2;mesh.SetTriangles(triangles[0],0);mesh.SetTriangles(triangles[1],1);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=scale;
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=new[]{material,patches?brown:material};return go.transform;
    }
    static Vector3 Point(int row,int col,int rows,int columns){float phi=Mathf.PI*row/rows,theta=2*Mathf.PI*col/columns;return new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta));}
    static void Taper(Transform parent,string name,Vector3 a,Vector3 b,float first,float last,Material material)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();var forward=(b-a).normalized;var right=Vector3.Cross(forward,Mathf.Abs(forward.y)>.9f?Vector3.forward:Vector3.up).normalized;var up=Vector3.Cross(right,forward);
        for(int i=0;i<8;i++)
        {
            float t=i*Mathf.PI/4,n=(i+1)*Mathf.PI/4;var r=right*Mathf.Cos(t)+up*Mathf.Sin(t);var s=right*Mathf.Cos(n)+up*Mathf.Sin(n);int at=vertices.Count;
            vertices.AddRange(new[]{a+r*first,b+r*last,b+s*last,a+r*first,b+s*last,a+s*first});triangles.AddRange(new[]{at,at+1,at+2,at+3,at+4,at+5});
        }
        var mesh=new Mesh{name="PreviewCow_"+name};Owned.Add(mesh);mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
    static Material Mat(string name,Color color){var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="TEMP_COW_"+name,color=color};m.SetFloat("_Smoothness",.1f);Owned.Add(m);return m;}
    static Transform Group(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
    static void Box(Transform parent,string name,Vector3 at,Vector3 size,Material material){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(go.GetComponent<Collider>());}
    static GameObject Hero(Transform parent,Vector3 position)
    {
        var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),parent);go.transform.position=position;
        var p=go.GetComponent<Game.CharacterWeaponPresenter>();p.SetWeaponId("arcade.weapon.basic");p.SetManualPoseSampling(true);p.BodyAnimator.Rebind();p.BodyAnimator.Update(0);p.Current.Animations.Idle.Clip.SampleAnimation(p.BodyAnimator.gameObject,.1f);p.ApplyOffhandGrip(p.Current.Animations.Idle);
        foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;return go;
    }
    static void SetLayer(){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
    static void Capture(string name,int width,int height)
    {
        var target=RenderTexture.GetTemporary(width,height,24);var prior=RenderTexture.active;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,name),texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=prior;Object.DestroyImmediate(texture);RenderTexture.ReleaseTemporary(target);}
    }
}
