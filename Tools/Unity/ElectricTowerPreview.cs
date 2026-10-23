using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Luban;
using FirepowerLoop.Arcade;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>第二关电塔同源预览、用户采用后的资源制作与正式接入实拍入口。</summary>
public static class ElectricTowerPreview
{
    private static readonly List<Object> Owned=new List<Object>();
    private static Material steel,copper,ceramic,dark,cyan,white,red,green,wood;
    private static Transform candidate,charge;
    private static readonly List<LineRenderer> charging=new List<LineRenderer>(),discharge=new List<LineRenderer>();
    private static readonly List<LineRenderer> corona=new List<LineRenderer>();
    private static Renderer status;
    private static LineRenderer boundary;
    /// <summary>读取正式码头周期，左右同一时间采样；左正式表现，右独立候选。</summary>
    public static string Run()=>Render(false);
    /// <summary>右侧经正式机关入口加载已接入模板，左侧保留旧电塔作同机位对照。</summary>
    public static string RunApplied()=>Render(true);
    private static string Render(bool applied)
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态预览");
        var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
        if(!provider.TryGetArcadeCatalog(out var catalog,out var error))throw new InvalidOperationException(error);
        var p=catalog.Arenas.Single(x=>x.ArenaNumber==21).Hazards.Single();
        p=new ArcadeHazardParameters(p.Id,p.Kind,0,0,p.DirectionDegrees,p.Radius,p.ArmLength,p.ArmHalfWidth,p.IdleSeconds,p.WarningSeconds,p.ActiveSeconds,p.AngularSpeedDegrees,p.HitIntervalSeconds,p.PlayerDamageRatio,p.BossDamageRatio);
        var prior=SceneManager.GetActiveScene();var priorRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture rt=null;Texture2D pixels=null;object current=null,formal=null;Camera camera=null;
        string folder=Path.GetFullPath(applied?"Temp/ElectricTowerApplied":"Temp/ElectricTowerPreview");Directory.CreateDirectory(folder);
        try
        {
            SceneManager.SetActiveScene(scene);Owned.Clear();charging.Clear();discharge.Clear();corona.Clear();
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.66f,.70f);
            SetupPalette();
            var holder=new GameObject("TEMP_TOWER_COMPARISON");
            var left=new GameObject("Current");left.transform.SetParent(holder.transform);left.transform.localPosition=new Vector3(-3.2f,0,0);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/ElectricTower").gameObject;
            var type=typeof(ArcadeCombatViewController).Assembly.GetType("Game.ArcadeHazardPresentation");
            current=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{left.transform,source},null);
            var sync=type.GetMethod("Sync");sync.Invoke(current,new object[]{new[]{p},0d});
            candidate=new GameObject(applied?"FORMAL_TOWER":"TEMP_CANDIDATE").transform;candidate.SetParent(holder.transform);candidate.localPosition=new Vector3(3.2f,0,0);
            if(applied)
            {
                var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/DocksElectricTower").gameObject;
                formal=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{candidate,template},null);
                sync.Invoke(formal,new object[]{new[]{p},0d});
            }
            else Build(p.Radius);
            var hero=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),holder.transform);hero.transform.localPosition=new Vector3(0,0,-.15f);hero.transform.rotation=Quaternion.Euler(0,180,0);
            var presenter=hero.GetComponent<CharacterWeaponPresenter>();if(!presenter.SetWeaponId("arcade.weapon.basic"))throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            for(int i=0;i<28;i++)Part(holder.transform,"临时木平台",PrimitiveType.Cube,new Vector3(0,-.1f,-5+i*.38f),new Vector3(19,.18f,.36f),wood);
            foreach(var t in holder.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var light=new GameObject("PreviewSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.45f;light.shadows=LightShadows.Soft;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(50,-30,0);
            camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=3.55f;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.transform.position=new Vector3(0,10,-10);camera.transform.LookAt(new Vector3(0,1,.2f));camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.20f,.24f);
            rt=new RenderTexture(1280,720,24);rt.antiAliasing=4;rt.Create();pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);camera.targetTexture=rt;
            int count=(int)Math.Round(p.CycleSeconds*20);
            for(int frame=0;frame<count;frame++)
            {
                double time=frame/20d;sync.Invoke(current,new object[]{new[]{p},time});
                if(applied)sync.Invoke(formal,new object[]{new[]{p},time});else Animate(p,time);
                camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            File.WriteAllText(Path.Combine(folder,"timing.txt"),$"idle={p.IdleSeconds};warning={p.WarningSeconds};active={p.ActiveSeconds};radius={p.Radius};frames={count};fps=20");
            return folder+"; "+File.ReadAllText(Path.Combine(folder,"timing.txt"));
        }
        finally
        {
            if(current is IDisposable d)d.Dispose();if(camera!=null)camera.targetTexture=null;RenderTexture.active=priorRT;
            if(formal is IDisposable f)f.Dispose();
            if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var o in Owned)if(o!=null)Object.DestroyImmediate(o);Owned.Clear();charging.Clear();discharge.Clear();corona.Clear();
        }
    }
    /// <summary>按用户已采用几何持久化；重复制作更新相同资源身份，不写入其他脏资源。</summary>
    public static string BakeApproved()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态接入已采用电塔");
        const string folder="Assets/Game/Art/Arcade/Docks/ElectricTower";
        const string library=folder+"/DocksElectricTower.asset";
        const string prefabPath="Assets/Game/Prefabs/SceneObjects/Arcade/DocksElectricTower.prefab";
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);Owned.Clear();corona.Clear();charging.Clear();discharge.Clear();SetupPalette();
            candidate=new GameObject("DocksElectricTower").transform;Build(1.2f);
            var redReference=Part(candidate,"状态红灯材质",PrimitiveType.Cube,Vector3.zero,Vector3.one,red);redReference.SetActive(false);
            // 安全初态与预览一致，实际范围和相位由正式快照重新投影。
            foreach(var l in corona.Concat(charging).Concat(discharge))l.enabled=false;charge.gameObject.SetActive(false);
            Directory.CreateDirectory(Path.GetFullPath(folder));AssetDatabase.Refresh();
            var old=AssetDatabase.LoadAllAssetsAtPath(library).Where(o=>o!=null).ToDictionary(o=>o.name);
            var map=new Dictionary<Object,Object>();
            for(int i=0;i<Owned.Count;i++)
            {
                var source=Owned[i];source.name="DocksTowerResource_"+i.ToString("D3");
                // CreateAsset会按文件名重命名主对象，首项须按主资源身份复用。
                Object saved=i==0?AssetDatabase.LoadMainAssetAtPath(library):old.TryGetValue(source.name,out var found)?found:null;
                if(saved!=null)
                {
                    if(saved.GetType()!=source.GetType())throw new InvalidOperationException("电塔素材类型不一致");
                    EditorUtility.CopySerialized(source,saved);EditorUtility.SetDirty(saved);map[source]=saved;
                }
                else
                {
                    if(i==0&&old.Count==0)AssetDatabase.CreateAsset(source,library);else AssetDatabase.AddObjectToAsset(source,library);
                    map[source]=source;
                }
            }
            foreach(var r in candidate.GetComponentsInChildren<Renderer>(true))if(map.TryGetValue(r.sharedMaterial,out var material))r.sharedMaterial=(Material)material;
            foreach(var m in candidate.GetComponentsInChildren<MeshFilter>(true))if(map.TryGetValue(m.sharedMesh,out var mesh))m.sharedMesh=(Mesh)mesh;
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(library));
            var savedPrefab=PrefabUtility.SaveAsPrefabAsset(candidate.gameObject,prefabPath);
            if(savedPrefab==null)throw new InvalidOperationException("正式电塔保存失败");
            const string collection="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
            var root=PrefabUtility.LoadPrefabContents(collection);
            try
            {
                var content=root.transform.Find("Content");var existing=content.Find("DocksElectricTower");
                if(existing==null){var nested=(GameObject)PrefabUtility.InstantiatePrefab(savedPrefab,content);nested.name="DocksElectricTower";nested.transform.localPosition=Vector3.zero;}
                else if(PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject)!=savedPrefab)throw new InvalidOperationException("同名电塔不是本次已采用模板");
                PrefabUtility.SaveAsPrefabAsset(root,collection);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            return prefabPath+"; persistent resources="+map.Count;
        }
        finally
        {
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var o in Owned)if(o!=null&&!EditorUtility.IsPersistent(o))Object.DestroyImmediate(o);Owned.Clear();corona.Clear();charging.Clear();discharge.Clear();
        }
    }
    private static void SetupPalette()
    {
        steel=Mat("钢",new Color(.23f,.30f,.34f),true,.65f);copper=Mat("铜",new Color(.68f,.31f,.10f),true,.6f);
        ceramic=Mat("陶瓷",new Color(.73f,.80f,.75f));dark=Mat("绝缘",new Color(.08f,.13f,.15f));
        cyan=Mat("蓝电",new Color(.05f,.72f,1f),false);white=Mat("白芯",new Color(.72f,.95f,1),false);
        red=Mat("红灯",new Color(1,.09f,.025f),false);green=Mat("绿灯",new Color(.12f,.85f,.35f),false);wood=Mat("木平台",new Color(.28f,.24f,.19f));
    }
    /// <summary>金属底座、瓷绝缘盘、真实螺旋铜线与冠顶放电环构成可辨认轮廓。</summary>
    private static void Build(float radius)
    {
        Part(candidate,"底座",PrimitiveType.Cube,new Vector3(0,.17f,0),new Vector3(1.12f,.34f,1.12f),steel);
        Part(candidate,"变压器",PrimitiveType.Cube,new Vector3(0,.48f,0),new Vector3(.73f,.42f,.73f),dark);
        for(int i=0;i<4;i++)Part(candidate,"底座螺栓",PrimitiveType.Cylinder,new Vector3((i%2==0?-.43f:.43f),.36f,(i<2?-.43f:.43f)),new Vector3(.09f,.04f,.09f),copper);
        Part(candidate,"绝缘芯",PrimitiveType.Cylinder,new Vector3(0,1.43f,0),new Vector3(.37f,.78f,.37f),ceramic);
        for(int i=0;i<8;i++)Torus(candidate,"绝缘盘",new Vector3(0,.78f+i*.18f,0),.28f,.055f,ceramic);
        var coil=Line(candidate,"螺旋铜线",copper,.07f,230);for(int i=0;i<230;i++){float t=i/229f;float a=t*Mathf.PI*14;coil.SetPosition(i,new Vector3(Mathf.Cos(a)*.35f,.76f+t*1.45f,Mathf.Sin(a)*.35f));}
        Torus(candidate,"冠顶放电环",new Vector3(0,2.36f,0),.57f,.13f,steel);
        Part(candidate,"冠顶电极",PrimitiveType.Sphere,new Vector3(0,2.40f,0),new Vector3(.42f,.42f,.42f),copper);
        for(int side=-1;side<=1;side+=2)
        {
            Part(candidate,"侧臂",PrimitiveType.Cube,new Vector3(side*.5f,.6f,0),new Vector3(.7f,.12f,.15f),steel);
            Part(candidate,"侧绝缘柱",PrimitiveType.Cylinder,new Vector3(side*.77f,1.02f,0),new Vector3(.16f,.42f,.16f),ceramic);
            for(int i=0;i<4;i++)Torus(candidate,"侧绝缘片",new Vector3(side*.77f,.78f+i*.16f,0),.15f,.035f,ceramic);
            Part(candidate,"侧电极",PrimitiveType.Sphere,new Vector3(side*.77f,1.5f,0),new Vector3(.24f,.24f,.24f),copper);
        }
        status=Part(candidate,"状态灯",PrimitiveType.Cube,new Vector3(0,.48f,-.38f),new Vector3(.28f,.12f,.05f),green).GetComponent<Renderer>();
        charge=Part(candidate,"塔顶聚电核",PrimitiveType.Sphere,new Vector3(0,2.75f,0),Vector3.one*.01f,white).transform;
        for(int i=0;i<3;i++){var l=Line(candidate,"塔顶聚电火花",cyan,.024f,24);l.loop=true;corona.Add(l);}
        for(int i=0;i<4;i++)charging.Add(Line(candidate,"向上聚电",cyan,.035f,18));
        for(int i=0;i<12;i++)discharge.Add(Line(candidate,"分叉放电",i%3==0?white:cyan,.03f,9));
        boundary=Line(candidate,"危险边界",green,.06f,96);boundary.loop=true;for(int i=0;i<96;i++){float a=i*Mathf.PI*2/96;boundary.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,.055f,Mathf.Sin(a)*radius));}
    }
    /// <summary>候选动作按正式相位采样：安全熄弧、预警上行聚电、通电从塔顶分叉。</summary>
    private static void Animate(ArcadeHazardParameters p,double time)
    {
        ArcadeHazardRules.Evaluate(p,time,out int phase,out float ignored);float t=(float)(time%p.CycleSeconds);float progress=phase==1?(t-p.IdleSeconds)/p.WarningSeconds:phase==2?1:0;
        status.sharedMaterial=phase==0?green:red;boundary.sharedMaterial=phase==0?green:phase==1?red:cyan;
        boundary.widthMultiplier=phase==2?.10f:.06f;status.enabled=phase!=1||((int)(t*6)%2==0);
        charge.gameObject.SetActive(phase!=0);charge.localScale=Vector3.one*(.12f+progress*.26f+Mathf.Sin(t*22)*.025f);
        int tick=(int)(time*18);
        for(int i=0;i<corona.Count;i++)
        {
            var arc=corona[i];arc.enabled=phase==2||phase==1&&progress>i*.25f;
            for(int j=0;j<24;j++){float a=j*Mathf.PI*2/24;float r=.13f+progress*.19f+Noise(tick,i,j)*.055f;var v=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);v=Quaternion.Euler(i*57,20+i*43,t*90)*v;arc.SetPosition(j,new Vector3(0,2.75f,0)+v);}
        }
        for(int i=0;i<charging.Count;i++)
        {
            var arc=charging[i];arc.enabled=phase==2||phase==1&&i<1+(int)(progress*3);
            for(int j=0;j<arc.positionCount;j++)
            {
                float u=j/(float)(arc.positionCount-1);float a=u*Mathf.PI*4+t*8+i*Mathf.PI*.5f;float y=.8f+u*1.55f;
                float r=.37f+Noise(tick,i,j)*.09f;arc.SetPosition(j,new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r));
            }
        }
        for(int i=0;i<discharge.Count;i++)
        {
            var arc=discharge[i];arc.enabled=phase==2||phase==1&&progress>.35f&&i<2&&tick%3!=0;
            float a=(i%8)*Mathf.PI/4+Noise(tick,i,9)*.24f;bool branch=i>=8;
            Vector3 start=branch?new Vector3(Mathf.Cos(a)*p.Radius*.45f,1.5f,Mathf.Sin(a)*p.Radius*.45f):new Vector3(Mathf.Cos(a)*.43f,2.39f,Mathf.Sin(a)*.43f);
            Vector3 end=phase==1?new Vector3((i==0?-1:1)*.77f,1.5f,0):new Vector3(Mathf.Cos(a)*p.Radius*(branch?.78f:.96f),.12f,Mathf.Sin(a)*p.Radius*(branch?.78f:.96f));
            for(int j=0;j<9;j++){float u=j/8f;var v=Vector3.Lerp(start,end,u);if(j>0&&j<8)v+=new Vector3(Noise(tick,i,j)*.14f,Noise(tick+3,i,j)*.13f,Noise(tick+7,i,j)*.14f);arc.SetPosition(j,v);}
            arc.widthMultiplier=phase==1?.023f:i%3==0?.033f:.045f;
        }
    }
    private static float Noise(int tick,int i,int j)=>Mathf.Sin(tick*17.137f+i*31.73f+j*11.53f);
    private static Material Mat(string name,Color color,bool lit=true,float metallic=0)
    {
        var m=new Material(Shader.Find(lit?"Universal Render Pipeline/Lit":"Universal Render Pipeline/Unlit")){name="TEMP_"+name,color=color};if(lit){m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",.42f);}Owned.Add(m);return m;
    }
    private static GameObject Part(Transform root,string name,PrimitiveType type,Vector3 pos,Vector3 scale,Material mat)
    {
        var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(root,false);g.transform.localPosition=pos;g.transform.localScale=scale;Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=mat;return g;
    }
    private static LineRenderer Line(Transform root,string name,Material mat,float width,int count)
    {
        var l=new GameObject(name,typeof(LineRenderer)).GetComponent<LineRenderer>();l.transform.SetParent(root,false);l.useWorldSpace=false;l.sharedMaterial=mat;l.widthMultiplier=width;l.positionCount=count;l.numCapVertices=2;l.numCornerVertices=2;return l;
    }
    /// <summary>低多边形圆环为内存网格，预览结束统一销毁。</summary>
    private static void Torus(Transform parent,string name,Vector3 pos,float radius,float tube,Material mat)
    {
        const int n=24,m=8;var verts=new Vector3[n*m];var indices=new int[n*m*6];
        for(int i=0;i<n;i++)for(int j=0;j<m;j++){float a=i*Mathf.PI*2/n,b=j*Mathf.PI*2/m;verts[i*m+j]=new Vector3(Mathf.Cos(a)*(radius+Mathf.Cos(b)*tube),Mathf.Sin(b)*tube,Mathf.Sin(a)*(radius+Mathf.Cos(b)*tube));int k=(i*m+j)*6;int next=(i+1)%n*m;indices[k]=i*m+j;indices[k+1]=next+j;indices[k+2]=next+(j+1)%m;indices[k+3]=i*m+j;indices[k+4]=next+(j+1)%m;indices[k+5]=i*m+(j+1)%m;}
        var mesh=new Mesh{name="TEMP_"+name};mesh.vertices=verts;mesh.triangles=indices;mesh.RecalculateNormals();Owned.Add(mesh);var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.GetComponent<MeshFilter>().sharedMesh=mesh;g.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
}
