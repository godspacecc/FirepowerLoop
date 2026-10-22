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
    static bool RaceOnly,DockArtOnly,DockApplied,FarmArtOnly,FarmApplied;
    static string Folder=>Path.GetFullPath(FarmApplied?"Temp/FarmApplied-v1":FarmArtOnly?"Temp/FarmArtPreview-v1":DockApplied?"Temp/DockApplied-v1":DockArtOnly?"Temp/DockArtPreview-v1":RaceOnly?"Temp/RaceCurvesPreview-v4":"Temp/RemainingStagesPreview-v3");
    public static string RunFarmArt(){FarmArtOnly=true;try{return Run();}finally{FarmArtOnly=false;}}
    public static string RunFarmApplied(){FarmApplied=FarmArtOnly=true;try{return Run();}finally{FarmApplied=FarmArtOnly=false;}}
    public static string RunRace(){RaceOnly=true;try{return Run();}finally{RaceOnly=false;}}
    public static string RunDockArt(){DockArtOnly=true;try{return Run();}finally{DockArtOnly=false;}}
    public static string RunDockApplied(){DockApplied=DockArtOnly=true;try{return Run();}finally{DockApplied=DockArtOnly=false;}}
    static readonly List<Material> Materials=new List<Material>();
    static readonly List<Mesh> Meshes=new List<Mesh>();
    static readonly List<Shader> Shaders=new List<Shader>();
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
            InitializeMaterials();
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<Layer;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.11f);camera.farClipPlane=250;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.cullingMask=1<<Layer;light.transform.rotation=Quaternion.Euler(48,-30,0);
            if(FarmArtOnly)
            {
                if(FarmApplied)
                {
                    Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeFarmVisual.prefab"),root);
                    Hero(-13,-16);Zombie(-9,14);Zombie(14,-5);Zombie(12,17);FarmCows();
                    camera.orthographicSize=36;camera.transform.position=new Vector3(0,68,-44);camera.transform.LookAt(Vector3.zero);
                }
                else FarmArt();
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
                Capture("farm-art-overview.png",1500,1100);
                camera.orthographicSize=11;camera.transform.position=new Vector3(-7,15,-31);camera.transform.LookAt(new Vector3(-5,.5f,-13));Capture("farm-art-detail.png",1400,1000);
            }
            else if(DockArtOnly)
            {
                Reset();
                if(DockApplied){var environment=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab"),root);Hero(0,-4);Zombie(-6,4);Zombie(6,1);foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
                else DockArt();camera.orthographicSize=18.5f;camera.transform.position=new Vector3(0,37,-30);camera.transform.LookAt(new Vector3(1,0,0));Capture("dock-art-overview.png",1440,1000);
                root.GetComponentsInChildren<Game.CharacterWeaponPresenter>(true).First().transform.position=new Vector3(3.8f,0,-9);
                camera.orthographicSize=7.2f;camera.transform.position=new Vector3(11,9,-23);camera.transform.LookAt(new Vector3(2,.6f,-10));Capture("dock-art-detail.png",1280,900);Reset();
            }
            else if(RaceOnly){Reset();RaceMotion();Reset();}
            else{Reset();Dock();Capture("docks.png",1440,900);Reset();Farm();Capture("farm.png",1440,900);
            Reset();Scale();Capture("scale.png",1280,720);Reset();BullMotion();Reset();RaceMotion();Reset();}
            return Folder;
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=oldRT;Object.DestroyImmediate(camera.gameObject);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var m in Materials)if(m!=null)Object.DestroyImmediate(m);Materials.Clear();foreach(var mesh in Meshes)if(mesh!=null)Object.DestroyImmediate(mesh);Meshes.Clear();foreach(var shader in Shaders)if(shader!=null)Object.DestroyImmediate(shader);Shaders.Clear();
        }
    }
    static void InitializeMaterials()
    {
            grass=Mat("Trees",.30f,.37f,.31f);soil=Mat("Dirt",.45f,.34f,.24f);wood=Mat("Wood",.45f,.31f,.18f);
            iron=Mat("Metal",.18f,.24f,.27f);water=Mat("Water",.13f,.35f,.46f);cream=Mat("Cream",.87f,.83f,.69f);
            purple=Mat("Vortex",.55f,.27f,.78f);dark=Mat("Dark",.10f,.12f,.13f);green=Mat("Truck",.28f,.44f,.34f);
    }
    /// <summary>FarmArt-v1明确采用后保存同源静态环境；正式主角和牛仍归业务独立拥有。</summary>
    public static string ApplyConfirmedFarm()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态正式制作");
        const string folder="Assets/Game/Art/Arcade/Farm/Environment";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);InitializeMaterials();root=new GameObject("ArcadeFarmVisual").transform;
            camera=new GameObject("TemporaryFraming",typeof(Camera)).GetComponent<Camera>();FarmArt();Object.DestroyImmediate(camera.gameObject);camera=null;
            foreach(var t in root.Cast<Transform>().Where(t=>t.GetComponentsInChildren<MonoBehaviour>(true).Length>0||t.GetComponentsInChildren<Animator>(true).Length>0).ToArray())Object.DestroyImmediate(t.gameObject);
            var groups=new Dictionary<Material,List<CombineInstance>>();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null||filter.sharedMesh==null)throw new InvalidOperationException("农场网格缺少Renderer");
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                {
                    var mat=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];if(!groups.TryGetValue(mat,out var items))groups[mat]=items=new List<CombineInstance>();
                    items.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                }
            }
            var output=new GameObject("CombinedEnvironment").transform;output.SetParent(root,false);int index=0;
            foreach(var pair in groups)
            {
                var mesh=new Mesh{name="FarmMesh"+index,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};Meshes.Add(mesh);mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();
                string meshPath=folder+"/FarmMesh"+index+".asset";var savedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(savedMesh==null){AssetDatabase.CreateAsset(mesh,meshPath);savedMesh=mesh;}else{EditorUtility.CopySerialized(mesh,savedMesh);AssetDatabase.SaveAssetIfDirty(savedMesh);}
                string materialPath=folder+"/Material"+index+".mat";var savedMaterial=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(savedMaterial==null){savedMaterial=new Material(pair.Key);AssetDatabase.CreateAsset(savedMaterial,materialPath);}else EditorUtility.CopySerialized(pair.Key,savedMaterial);
                savedMaterial.name="FarmMaterial"+index;EditorUtility.SetDirty(savedMaterial);AssetDatabase.SaveAssetIfDirty(savedMaterial);
                var go=new GameObject("EnvironmentPart"+index++,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(output,false);go.GetComponent<MeshFilter>().sharedMesh=savedMesh;go.GetComponent<MeshRenderer>().sharedMaterial=savedMaterial;
            }
            foreach(var t in root.Cast<Transform>().Where(t=>t!=output).ToArray())Object.DestroyImmediate(t.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/Game/Prefabs/SceneObjects/ArcadeFarmVisual.prefab");return "已保存确认农场，绘制组="+index;
        }
        finally
        {
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var material in Materials)if(material!=null&&!EditorUtility.IsPersistent(material))Object.DestroyImmediate(material);Materials.Clear();
            foreach(var mesh in Meshes)if(mesh!=null&&!EditorUtility.IsPersistent(mesh))Object.DestroyImmediate(mesh);Meshes.Clear();
        }
    }
    /// <summary>DockArt-v1已获明确采用后，制作独立低批次环境，不携带战斗单位。</summary>
    public static string ApplyConfirmedDock()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态正式制作");
        const string folder="Assets/Game/Art/Arcade/Docks";Directory.CreateDirectory(folder+"/Textures");AssetDatabase.Refresh();
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);InitializeMaterials();root=new GameObject("ArcadeDocksVisual").transform;DockArt();
            // 电塔/桶/角色由各自业务入口投影，环境不重复带入一份固定实例。
            foreach(var t in root.Cast<Transform>().Where(t=>t.name.Contains("GrayboxPlayer")||t.name.Contains("ArcadeZombieShambler")||t.name.Contains("Tower")||t.name.Contains("Vortex")||t.name.Contains("BarrelCandidate")).ToArray())Object.DestroyImmediate(t.gameObject);
            // 绳索转换为三维圆截面后合并，避免LineRenderer固定在世界原点或增加数十绘制批次。
            foreach(var line in root.GetComponentsInChildren<LineRenderer>(true))
            {
                for(int i=1;i<line.positionCount;i++)
                {
                    Vector3 a=line.GetPosition(i-1),b=line.GetPosition(i);if(!line.useWorldSpace){a=line.transform.TransformPoint(a);b=line.transform.TransformPoint(b);}
                    a=root.InverseTransformPoint(a);b=root.InverseTransformPoint(b);
                    var rope=new GameObject("StaticRope",typeof(MeshFilter),typeof(MeshRenderer));rope.transform.SetParent(root,false);rope.GetComponent<MeshFilter>().sharedMesh=RopeMesh(a,b,line.startWidth*.5f);rope.GetComponent<MeshRenderer>().sharedMaterial=line.sharedMaterial;
                }
                Object.DestroyImmediate(line.gameObject);
            }
            var shaderText=File.ReadAllText("Assets/Art/CoastStudy/CoastStudyWater.shader").Replace("CoastStudy/Water","FirepowerLoop/DockWater")
                .Replace("float d=p.x-Shore(p.y); clip(d+0.24);","float d=8;").Replace("float foam=(1-smoothstep(0.025,0.14,abs(d-0.06-Noise(p*4.2)*0.2)))*0.9;","float foam=0;");
            string shaderPath=folder+"/DockWater.shader";File.WriteAllText(shaderPath,shaderText);AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceSynchronousImport);
            var waterShader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);if(waterShader==null||ShaderUtil.ShaderHasError(waterShader))throw new InvalidOperationException("码头海面Shader不完整");
            var textureCopies=new Dictionary<Texture,Texture>();var materialCopies=new Dictionary<Material,Material>();
            Material SaveMaterial(Material source)
            {
                if(materialCopies.TryGetValue(source,out var saved))return saved;
                string path=folder+"/Material"+materialCopies.Count+".mat";saved=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(saved==null){saved=new Material(source);AssetDatabase.CreateAsset(saved,path);}else EditorUtility.CopySerialized(source,saved);
                saved.name="DockMaterial"+materialCopies.Count;
                if(source.shader.name=="Hidden/TempDockWaterCandidate")saved.shader=waterShader;
                foreach(var property in saved.GetTexturePropertyNames())
                {
                    var texture=saved.GetTexture(property);if(texture==null)continue;string original=AssetDatabase.GetAssetPath(texture);
                    if(!original.StartsWith("Assets/Art/"))continue;
                    if(!textureCopies.TryGetValue(texture,out var copy))
                    {
                        string target=folder+"/Textures/"+Path.GetFileName(original);
                        if(AssetDatabase.LoadAssetAtPath<Texture>(target)==null&&!AssetDatabase.CopyAsset(original,target))throw new InvalidOperationException("复制码头贴图失败："+original);
                        copy=AssetDatabase.LoadAssetAtPath<Texture>(target);if(copy==null)throw new InvalidOperationException("码头贴图副本为空："+target);textureCopies[texture]=copy;
                    }
                    saved.SetTexture(property,copy);
                }
                EditorUtility.SetDirty(saved);AssetDatabase.SaveAssetIfDirty(saved);materialCopies[source]=saved;return saved;
            }
            var groups=new Dictionary<Material,List<CombineInstance>>();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null||filter.sharedMesh==null)throw new InvalidOperationException("码头网格缺少Renderer");
                var palette=renderer.sharedMaterials;
                for(int sub=0;sub<filter.sharedMesh.subMeshCount;sub++)
                {
                    var material=palette[Mathf.Min(sub,palette.Length-1)];if(!groups.TryGetValue(material,out var list))groups[material]=list=new List<CombineInstance>();
                    list.Add(new CombineInstance{mesh=filter.sharedMesh,subMeshIndex=sub,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                }
            }
            var output=new GameObject("CombinedEnvironment").transform;output.SetParent(root,false);int index=0;
            foreach(var pair in groups)
            {
                var combined=new Mesh{name="DocksMesh"+index,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};Meshes.Add(combined);combined.CombineMeshes(pair.Value.ToArray(),true,true);combined.RecalculateBounds();
                string path=folder+"/DocksMesh"+index+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(combined,path);saved=combined;}else{EditorUtility.CopySerialized(combined,saved);AssetDatabase.SaveAssetIfDirty(saved);}
                var go=new GameObject("EnvironmentPart"+index++,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(output,false);go.GetComponent<MeshFilter>().sharedMesh=saved;go.GetComponent<MeshRenderer>().sharedMaterial=SaveMaterial(pair.Key);
            }
            foreach(var t in root.Cast<Transform>().Where(t=>t!=output).ToArray())Object.DestroyImmediate(t.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject,"Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab");return "已保存确认码头，绘制组="+index+"，贴图副本="+textureCopies.Count;
        }
        finally
        {
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var m in Materials)if(m!=null&&!EditorUtility.IsPersistent(m))Object.DestroyImmediate(m);Materials.Clear();
            foreach(var mesh in Meshes)if(mesh!=null&&!EditorUtility.IsPersistent(mesh))Object.DestroyImmediate(mesh);Meshes.Clear();foreach(var shader in Shaders)if(shader!=null&&!EditorUtility.IsPersistent(shader))Object.DestroyImmediate(shader);Shaders.Clear();
        }
    }
    static Mesh RopeMesh(Vector3 a,Vector3 b,float radius)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();var direction=(b-a).normalized;var right=Vector3.Cross(direction,Mathf.Abs(direction.y)>.9f?Vector3.forward:Vector3.up).normalized;var up=Vector3.Cross(right,direction);
        for(int i=0;i<8;i++)
        {
            var r=right*Mathf.Cos(i*Mathf.PI/4)+up*Mathf.Sin(i*Mathf.PI/4);var n=right*Mathf.Cos((i+1)*Mathf.PI/4)+up*Mathf.Sin((i+1)*Mathf.PI/4);int at=vertices.Count;
            vertices.AddRange(new[]{a+r*radius,b+r*radius,b+n*radius,a+r*radius,b+n*radius,a+n*radius});triangles.AddRange(new[]{at,at+1,at+2,at+3,at+4,at+5});
        }
        var mesh=new Mesh{name="TemporaryDockRope"};Meshes.Add(mesh);mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
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
    // 只在独立临时场景完善已确认码头v2外观，不保存正式Prefab或改动布局体量。
    static void DockArt()
    {
        Dock();
        var weathered=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/CoastStudy/Generated/WeatheredWood.mat");
        var metal=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/CoastStudy/Generated/RustIron.mat");
        var waterSource=File.ReadAllText("Assets/Art/CoastStudy/CoastStudyWater.shader").Replace("CoastStudy/Water","Hidden/TempDockWaterCandidate")
            .Replace("float d=p.x-Shore(p.y); clip(d+0.24);","float d=8;")
            .Replace("float foam=(1-smoothstep(0.025,0.14,abs(d-0.06-Noise(p*4.2)*0.2)))*0.9;","float foam=0;");
        var waterShader=ShaderUtil.CreateShaderAsset(waterSource,false);Shaders.Add(waterShader);
        var sea=new Material(waterShader){name="TEMP_DockWaterCandidate"};Materials.Add(sea);
        var hullMesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Game/Art/Arcade/Island/FixedRounds/DockedBoat.asset");
        if(weathered==null||metal==null||sea==null||hullMesh==null)throw new InvalidOperationException("码头既有候选资源缺失");
        foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if(r.name=="DeckPlank"||r.name=="DeckBase"||r.name=="Crate"||r.name=="HangingCargo")r.sharedMaterial=weathered;
            if(r.name.StartsWith("Crane"))r.sharedMaterial=metal;
            if(r.name=="Sea")r.sharedMaterial=sea;
        }
        var primitiveHull=root.Find("MooredHull");Object.DestroyImmediate(primitiveHull.gameObject);
        var hull=new GameObject("MooredHullCandidate",typeof(MeshFilter),typeof(MeshRenderer));hull.layer=Layer;hull.transform.SetParent(root,false);
        hull.GetComponent<MeshFilter>().sharedMesh=BoatHull();hull.GetComponent<MeshRenderer>().sharedMaterials=new[]{metal,weathered};hull.transform.position=new Vector3(2,0,-14.1f);
        foreach(var t in root.Cast<Transform>().Where(t=>(t.name=="DeckPlank"||t.name=="DeckBase")&&Mathf.Abs(t.localPosition.x-6)<.01f&&Mathf.Abs(t.localScale.x-2.5f)<.01f))
        {
            var at=t.localPosition;at.y=t.name=="DeckBase"?.09f:.03f+Mathf.InverseLerp(-10.9f,-13.1f,at.z)*.43f;t.localPosition=at;t.localRotation=Quaternion.Euler(11.5f,0,0);
        }
        // 箱体添加斜木条与金属角，保留v2的每个箱位与1.1米体量。
        foreach(var crate in root.Cast<Transform>().Where(t=>t.name=="Crate").ToArray())
        {
            for(int side=-1;side<=1;side+=2)
            {
                var at=crate.position;var brace=Box("CargoDiagonal",at.x,at.y,at.z+side*.56f,1.2f,.10f,.06f,weathered);brace.transform.rotation=Quaternion.Euler(0,0,35);
                Box("CargoTopBand",at.x,at.y+.48f,at.z+side*.56f,1.08f,.08f,.06f,metal);
                Box("CargoBottomBand",at.x,at.y-.48f,at.z+side*.56f,1.08f,.08f,.06f,metal);
            }
        }
        for(int side=-1;side<=1;side+=2)
        {
            Beam("CraneBrace",new Vector3(-10+side*.8f,.4f,-2),new Vector3(-10-side*.8f,5.6f,-2),.12f,metal);
            Beam("CraneBrace",new Vector3(-10+side*.8f,.4f,2),new Vector3(-10-side*.8f,5.6f,2),.12f,metal);
        }
        for(int i=0;i<3;i++)Box("CabinRoofBeam",1+i*.7f,2,-14.1f,.12f,.18f,2.55f,weathered);
        for(int side=-1;side<=1;side+=2)for(int i=0;i<3;i++)Box("BoatSideWindow",1+i*.75f,1.3f,-14.1f+side*1.22f,.52f,.48f,.05f,water);
        var rope=Mat("Rope",.43f,.33f,.19f);
        RailEdge(-14,-10,10,true,weathered,rope,p=>p>1.5f&&p<6.5f);
        RailEdge(12,-10,10,true,weathered,rope,p=>p>-6.5f&&p<-1.5f);
        RailEdge(-11.5f,-13,11,false,weathered,rope,p=>p>4.2f&&p<7.8f);
        RailEdge(11.5f,-13,11,false,weathered,rope,p=>p>.4f&&p<5.6f);
        for(int i=0;i<8;i++)
        {
            float x=-1.8f+i*1.05f;Box("BoatRailPost",x,.9f,-15.45f,.10f,1.15f,.10f,metal);
            if(i>0)Line("BoatRope",new Vector3(x-1.05f,1.3f,-15.45f),new Vector3(x,1.3f,-15.45f),.045f,rope);
        }
        // 漩涡桶外形沿用现有圆桶/箍/紫色口沿，不新增效果或碰撞。
        var barrel=new GameObject("ExistingVortexBarrelCandidate").transform;barrel.SetParent(root,false);barrel.position=new Vector3(-3,0,2);
        Shape("BarrelBody",PrimitiveType.Cylinder,new Vector3(0,.55f,0),new Vector3(.9f,.55f,.9f),purple,barrel);
        Shape("BarrelUpperHoop",PrimitiveType.Cylinder,new Vector3(0,.95f,0),new Vector3(.96f,.06f,.96f),metal,barrel);
        Shape("BarrelLowerHoop",PrimitiveType.Cylinder,new Vector3(0,.15f,0),new Vector3(.96f,.06f,.96f),metal,barrel);
        foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
    }
    static Mesh BoatHull()
    {
        var outline=new[]{new Vector2(-4.5f,-1.4f),new Vector2(3.9f,-1.5f),new Vector2(4.5f,0),new Vector2(3.9f,1.5f),new Vector2(-4.5f,1.4f)};
        var vertices=new List<Vector3>();var sides=new List<int>();var top=new List<int>();
        for(int i=0;i<outline.Length;i++)
        {
            var a=outline[i];var b=outline[(i+1)%outline.Length];var ta=new Vector3(a.x,.45f,a.y);var tb=new Vector3(b.x,.45f,b.y);
            var ba=new Vector3(a.x*.9f,-.75f,a.y*.72f);var bb=new Vector3(b.x*.9f,-.75f,b.y*.72f);
            Face(ta,tb,bb,sides);Face(ta,bb,ba,sides);Face(new Vector3(0,.45f,0),tb,ta,top);
        }
        void Face(Vector3 a,Vector3 b,Vector3 c,List<int> indices){int at=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.AddRange(new[]{at,at+1,at+2});}
        var mesh=new Mesh{name="TEMP_DockCargoBoat"};Meshes.Add(mesh);mesh.SetVertices(vertices);mesh.subMeshCount=2;mesh.SetTriangles(sides,0);mesh.SetTriangles(top,1);mesh.SetUVs(0,vertices.Select(v=>new Vector2(v.x*.25f,v.z*.4f)).ToList());mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static void RailEdge(float fixedCoordinate,float from,float to,bool alongZ,Material posts,Material rope,Func<float,bool> gap)
    {
        Vector3? previous=null;
        for(float p=from;p<=to;p+=2)
        {
            if(gap(p)){previous=null;continue;}
            var at=alongZ?new Vector3(fixedCoordinate,.48f,p):new Vector3(p,.48f,fixedCoordinate);Box("DockEdgePost",at.x,at.y,at.z,.18f,1.15f,.18f,posts);
            if(previous.HasValue)Line("DockEdgeRope",previous.Value+Vector3.up*.3f,at+Vector3.up*.3f,.045f,rope);previous=at;
        }
    }
    static void Beam(string name,Vector3 from,Vector3 to,float width,Material material)
    {var at=(from+to)*.5f;var beam=Box(name,at.x,at.y,at.z,width,(to-from).magnitude,width,material);beam.transform.rotation=Quaternion.FromToRotation(Vector3.up,to-from);}
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
    // 保持已采用v3布局，仅展示雪地/石墙/棚屋的独立临时外观候选。
    static void FarmArt()
    {
        Farm();var stone=Mat("FarmStone",.30f,.34f,.36f);var snow=Mat("FreshSnow",.80f,.85f,.87f);var bale=Mat("DryHay",.65f,.49f,.24f);
        var shedFinish=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Docks/Material0.mat")??wood;
        foreach(var child in root.Cast<Transform>().ToArray())
        {
            var renderer=child.GetComponent<Renderer>();if(renderer==null)continue;
            string n=child.name;
            if(n=="SnowGround"||n=="YardSnow"||n=="SnowRoof"||n=="CrownSnow")renderer.sharedMaterial=snow;
            if(n=="WestEastDivider"||n=="NorthSouthDivider"||n=="OuterWall")
            {
                renderer.sharedMaterial=stone;var s=child.localScale;var p=child.position;bool alongZ=s.z>s.x;float length=alongZ?s.z:s.x;
                Box("WallSnowCap",p.x,2.17f,p.z,s.x+.16f,.20f,s.z+.16f,snow);
                for(int i=0;i<Mathf.FloorToInt(length/1.6f);i++)
                {
                    float offset=-length*.5f+.8f+i*1.6f;
                    Box("StoneCourse",p.x+(alongZ?0:offset),.64f+(i%2)*.10f,p.z+(alongZ?offset:0),alongZ?.76f:1.43f,.73f,alongZ?1.43f:.76f,stone);
                }
            }
            if(n=="Shed")
            {
                renderer.sharedMaterial=shedFinish;var p=child.position;
                Box("ShedDoor",p.x,.95f,p.z-3.02f,1.5f,1.9f,.09f,dark);
                Box("DoorFrame",p.x-1.0f,1.15f,p.z-3.12f,.16f,2.3f,.16f,wood);Box("DoorFrame",p.x+1.0f,1.15f,p.z-3.12f,.16f,2.3f,.16f,wood);
                Box("DoorFrame",p.x,2.24f,p.z-3.12f,2.2f,.17f,.16f,wood);
            }
            if(n=="HayBale")
            {
                renderer.sharedMaterial=bale;var p=child.position;
                for(int i=-1;i<=1;i+=2)Box("HayTie",p.x+i*.62f,p.y,p.z, .06f,1.14f,1.45f,wood);
                Box("HaySnow",p.x,p.y+.58f,p.z,2.24f,.11f,1.43f,snow);
            }
            if(n=="BrokenWallRubble")renderer.sharedMaterial=stone;
            if(n=="OpenDoorPost")renderer.sharedMaterial=wood;
        }
        foreach(var cow in root.Cast<Transform>().Where(t=>t.name=="TEMP_BULL_GEOMETRY").ToArray())Object.DestroyImmediate(cow.gameObject);
        FarmCows();
        foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
    }
    static void FarmCows()
    {
        for(int i=0;i<5;i++)
        {
            var cow=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeHerdVisual.prefab"),root);
            cow.GetComponent<Game.ArcadeHerdVisual>().Bind(22,i+1);
            cow.GetComponent<Game.ArcadeHerdVisual>().Project(new FirepowerLoop.Arcade.ArcadeHerdSnapshot(22,i+1,FirepowerLoop.Arcade.ArcadeHerdPhase.Charging,-18,-14+(i-2)*1.65f,1,0,.4f));
        }
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
