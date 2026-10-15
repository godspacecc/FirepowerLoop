using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>制作与检查独立街机特效；不接入玩法、不修改共享场景、不决定技能参数。</summary>
public static class ArcadeArtFirstBatch
{
    const string Folder = "Assets/Game/Art/Arcade/VFX/FirstBatch";
    const string Preview = "Assets/Tests/Results~/ArcadeArtFirstBatch-20261011";
    static readonly string[] Names = { "HammerTelegraph", "Fireball", "FireImpact", "BurningGround", "FlamePath", "TeslaStorm" };
    static Material fire, glow, red, amber, blue, electric, blastGlow, smoke;
    static Mesh ring, bolt;

    /// <summary>只重建本批资产，已有文件通过原对象更新保留 GUID；临时预览场景始终关闭。</summary>
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("请在非播放且编译完成时制作美术");
        Directory.CreateDirectory(Folder + "/Textures");
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Meshes");
        Directory.CreateDirectory(Folder + "/Animations");
        Directory.CreateDirectory(Folder + "/Prefabs");
        Directory.CreateDirectory(Preview);
        AssetDatabase.Refresh();
        string firePath = Folder + "/Textures/FireAtlas.png";
        if (!File.Exists(firePath))
            AssetDatabase.CopyAsset("Assets/Art/ARPG Effects/Textures/fire_16x4.png", firePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(firePath);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        fire = MaterialAsset("Fire", AssetDatabase.LoadAssetAtPath<Texture2D>(firePath), Color.white, true);
        glow = MaterialAsset("Glow", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Effects/ArcadeARPG/Textures/basicglow.png"), Color.white, true);
        red = MaterialAsset("WarningRed", null, new Color(1f,.045f,.025f,.85f), false);
        amber = MaterialAsset("HazardAmber", null, new Color(1f,.24f,.025f,.68f), false);
        blue = MaterialAsset("ElectricBlue", null, new Color(.25f,.8f,1f,1f), false);
        electric = MaterialAsset("ElectricArc", null, new Color(.35f,.85f,1f,1f), true);
        string smokePath = Folder + "/Textures/Smoke.png";
        if (!File.Exists(smokePath)) AssetDatabase.CopyAsset("Assets/Art/ARPG Effects/Textures/cloud.png", smokePath);
        smoke = MaterialAsset("BlastSmoke", AssetDatabase.LoadAssetAtPath<Texture2D>(smokePath), Color.white, true);
        blastGlow = MaterialAsset("BlastGlow", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Effects/ArcadeARPG/Textures/basicglow.png"), Color.white, true);
        blastGlow.SetFloat("_Blend", 2); blastGlow.SetFloat("_DstBlend", (int)BlendMode.One);
        EditorUtility.SetDirty(blastGlow);
        ring = MeshAsset("Ring", RingMesh());
        bolt = MeshAsset("Bolt", BoltMesh());
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            foreach (string name in Names)
            {
                var root = new GameObject(name);
                try
                {
                    Build(root, name);
                    PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Prefabs/" + name + ".prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            RenderPreview();
            string audit = Audit();
            File.WriteAllText(Preview + "/asset-audit.txt", audit);
            return audit + "\nPreview=" + Path.GetFullPath(Preview);
        }
        finally { SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true); }
    }

    static Material MaterialAsset(string name, Texture texture, Color color, bool particle)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var shader = Shader.Find(particle ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("缺少 URP shader");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader; mat.SetFloat("_Surface",1); mat.SetFloat("_Blend",0);
        mat.SetFloat("_SrcBlend",(int)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite",0); mat.SetFloat("_Cull",0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType","Transparent"); mat.renderQueue = 3000;
        mat.SetColor("_BaseColor",color); mat.SetTexture("_BaseMap",texture);
        if (particle) { mat.SetFloat("_ColorMode",0); mat.DisableKeyword("_SOFTPARTICLES_ON"); }
        EditorUtility.SetDirty(mat); return mat;
    }
    static Mesh MeshAsset(string name, Mesh mesh)
    {
        string path = Folder + "/Meshes/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { mesh.name = name; AssetDatabase.CreateAsset(mesh,path); return mesh; }
        EditorUtility.CopySerialized(mesh,existing); Object.DestroyImmediate(mesh); return existing;
    }
    static Mesh RingMesh()
    {
        var vertices = new List<Vector3>(); var tris = new List<int>();
        for (int i=0;i<=96;i++)
        {
            float a=i*Mathf.PI*2/96; Vector3 v=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            vertices.Add(v); vertices.Add(v*.94f);
            if(i<96) { int k=i*2; tris.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3}); }
        }
        var mesh=new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(tris,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    static Mesh BoltMesh()
    {
        var mesh=new Mesh(); var verts=new List<Vector3>(); var tris=new List<int>();
        Vector3[] points={new Vector3(0,0,0),new Vector3(.3f,.02f,.08f),new Vector3(.48f,0,-.12f),new Vector3(.64f,.04f,.11f),new Vector3(.82f,0,-.06f),new Vector3(1,0,0)};
        for(int i=0;i<points.Length;i++)
        {
            verts.Add(points[i]+Vector3.forward*.018f); verts.Add(points[i]-Vector3.forward*.018f);
            if(i<points.Length-1){int k=i*2;tris.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});}
        }
        mesh.SetVertices(verts);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    static GameObject Ring(Transform parent,string name,float radius,Material mat)
    {
        var g=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));g.transform.SetParent(parent,false);
        g.transform.localPosition=new Vector3(0,.045f,0);g.transform.localScale=Vector3.one*radius;
        g.GetComponent<MeshFilter>().sharedMesh=ring;g.GetComponent<MeshRenderer>().sharedMaterial=mat;return g;
    }
    /// <summary>尺寸以米作预览基准；这里只配置视觉，运行时须按权威范围缩放。</summary>
    static ParticleSystem Particles(Transform parent,string name,Material mat,Color color,float size,float rate,float life,float speed,bool loop=true)
    {
        var g=new GameObject(name,typeof(ParticleSystem));g.transform.SetParent(parent,false);
        var ps=g.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.duration=2;main.loop=loop;main.playOnAwake=true;main.startLifetime=life;
        main.startSpeed=speed;main.startSize=new ParticleSystem.MinMaxCurve(size*.65f,size);
        main.startColor=color;main.maxParticles=96;main.simulationSpace=ParticleSystemSimulationSpace.Local;
        ps.useAutoRandomSeed=false;ps.randomSeed=37;
        var emission=ps.emission;emission.rateOverTime=rate;
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=.15f;shape.rotation=new Vector3(90,0,0);
        var col=ps.colorOverLifetime;col.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.8f,.6f),new GradientAlphaKey(0,1)});
        col.color=gradient;
        var renderer=g.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        return ps;
    }
    static ParticleSystem Flames(Transform parent,string name,float radius,float size,float rate)
    {
        var ps=Particles(parent,name,fire,Color.white,size,rate,.62f,.35f);
        var shape=ps.shape;shape.radius=radius;
        // 原资源名为 16x4，但实际图像与原 Prefab 均为 16 列 5 行。
        var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=16;sheet.numTilesY=5;
        sheet.animation=ParticleSystemAnimationType.WholeSheet;sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1));
        return ps;
    }
    static void Build(GameObject root,string name)
    {
        var t=root.transform;
        if(name=="HammerTelegraph")
        {
            Ring(t,"Boundary",2,red);var pulse=Ring(t,"ContractingRing",2,red);
            var clip=new AnimationClip { legacy=true,wrapMode=WrapMode.Loop };
            foreach(string axis in new[]{"x","y","z"}) clip.SetCurve("ContractingRing",typeof(Transform),"localScale."+axis,AnimationCurve.Linear(0,2,1.2f,.08f));
            var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/Animations/HammerContract.anim");
            if(old==null) {AssetDatabase.CreateAsset(clip,Folder+"/Animations/HammerContract.anim");old=clip;}
            else {EditorUtility.CopySerialized(clip,old);Object.DestroyImmediate(clip);}
            var animation=root.AddComponent<Animation>();animation.clip=old;animation.AddClip(old,"Contract");animation.playAutomatically=true;
        }
        if(name=="Fireball")
        {
            Flames(t,"FlameCore",.18f,.75f,24).transform.localPosition=Vector3.up*.8f;
            var tail=Flames(t,"ShortTail",.12f,.5f,22);tail.transform.localPosition=new Vector3(0,.75f,-.5f);
            var shape=tail.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.rotation=Vector3.zero;shape.scale=new Vector3(.15f,.15f,1);
            Particles(t,"HotCore",glow,new Color(1,.46f,.08f,.8f),.52f,15,.24f,0).transform.localPosition=Vector3.up*.8f;
        }
        if(name=="FireImpact")
        {
            // 对照官方 DOA4 演示的白亮爆心、径向火星与烟尘；不据此推定 Guardian 技能判定。
            var flash=Particles(t,"ImpactFlash",blastGlow,new Color(8,5,2,.9f),3.2f,0,.2f,0,false);
            var flashShape=flash.shape;flashShape.enabled=false;
            var flashEmission=flash.emission;flashEmission.SetBursts(new[]{new ParticleSystem.Burst(0,1)});
            var expansion=flash.sizeOverLifetime;expansion.enabled=true;expansion.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.35f,1,1));
            var ps=Particles(t,"Burst",fire,new Color(2,1.5f,1,.8f),1.15f,0,.48f,3.4f,false);
            var emission=ps.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(0,16)});
            var shape=ps.shape;shape.radius=.3f;
            var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=16;sheet.numTilesY=5;
            var sparks=Particles(t,"Sparks",blastGlow,new Color(5,2.4f,.6f,1),.09f,0,.65f,6,false);
            var se=sparks.emission;se.SetBursts(new[]{new ParticleSystem.Burst(0,42)});
            var velocity=sparks.velocityOverLifetime;velocity.enabled=true;velocity.y=.7f;
            var sr=sparks.GetComponent<ParticleSystemRenderer>();sr.renderMode=ParticleSystemRenderMode.Stretch;sr.velocityScale=.08f;sr.lengthScale=3;
            var dust=Particles(t,"Smoke",smoke,new Color(.32f,.29f,.26f,.65f),1.1f,0,1.15f,.65f,false);
            var de=dust.emission;de.SetBursts(new[]{new ParticleSystem.Burst(.08f,12)});
            var ds=dust.shape;ds.radius=.45f;
            var dm=dust.main;dm.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
            var size=dust.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.5f,1,2));
        }
        if(name=="BurningGround")
        {
            Ring(t,"Boundary",1.8f,amber);Flames(t,"LowFlames",1.65f,.85f,36);
            Particles(t,"Embers",glow,new Color(1,.34f,.03f,.8f),.11f,12,.8f,.15f);
        }
        if(name=="FlamePath")
        {
            // 本段沿局部 +Z 延伸；由调用方按权威路径排列，无碰撞或自动扩张。
            var flames=Flames(t,"DirectionalFlames",.2f,.8f,38);
            var shape=flames.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.rotation=Vector3.zero;shape.scale=new Vector3(.8f,.08f,3);
            for(int i=-1;i<=1;i++) { var r=Ring(t,"Segment"+i,.62f,amber);r.transform.localPosition=new Vector3(0,.035f,i); }
        }
        if(name=="TeslaStorm")
        {
            Ring(t,"InnerHalo",.62f,blue);
            for(int i=0;i<8;i++)
            {
                var ps=Particles(t,"Arc"+i,electric,Color.white,1.5f,12,.16f,0);
                ps.transform.localPosition=Vector3.up*.22f;ps.transform.localRotation=Quaternion.Euler(0,i*45,0);
                var main=ps.main;main.startSize=1.5f;main.startRotation3D=true;main.startRotationY=0;
                main.startDelay=i*.019f;
                var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.separateAxes=true;rotation.y=new ParticleSystem.MinMaxCurve(-.8f,.8f);
                var shape=ps.shape;shape.enabled=false;
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=bolt;renderer.alignment=ParticleSystemRenderSpace.Local;
                ps.randomSeed=(uint)(37+i);
            }
            Particles(t,"ElectricSparks",glow,new Color(.3f,.85f,1,1),.12f,25,.3f,.35f).shapeRadius(1.35f);
        }
    }
    static void shapeRadius(this ParticleSystem ps,float radius) { var shape=ps.shape;shape.radius=radius; }

    /// <summary>仅在临时场景中按固定时间采样；恢复相机 RT，不保存 Boot、不运行局内事务。</summary>
    static void RenderPreview()
    {
        var cameraGO=new GameObject("ArtPreviewCamera",typeof(Camera));var camera=cameraGO.GetComponent<Camera>();
        camera.transform.position=new Vector3(0,18,-11);camera.transform.LookAt(Vector3.zero);
        camera.orthographic=true;camera.orthographicSize=6.3f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.055f);
        camera.allowHDR=true;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        // Bloom 仅供隔离预览；正式场景是否开启须由接入方按平台预算确认。
        var volumeRoot=new GameObject("PreviewOnlyBloom",typeof(Volume));var volume=volumeRoot.GetComponent<Volume>();
        var volumeProfile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=volumeProfile;volume.isGlobal=true;volume.priority=100;
        var bloom=volumeProfile.Add<Bloom>(true);bloom.threshold.Override(.8f);bloom.intensity.Override(1.6f);bloom.scatter.Override(.7f);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="PreviewOnlyGround";floor.transform.position=new Vector3(0,-.16f,0);floor.transform.localScale=new Vector3(24,.2f,16);
        var floorMat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));floorMat.SetColor("_BaseColor",new Color(.095f,.12f,.14f));floor.GetComponent<Renderer>().sharedMaterial=floorMat;
        var objects=new List<GameObject>();
        for(int i=0;i<Names.Length;i++)
        {
            var g=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Prefabs/"+Names[i]+".prefab"));
            g.transform.position=new Vector3((i%3-1)*6,0,i<3?3.1f:-3.1f);objects.Add(g);
        }
        // 相机内部保持 HDR；最终导出使用 sRGB，避免线性颜色直接写 PNG 后地面与烟尘过暗。
        var rt=new RenderTexture(1200,800,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var old=RenderTexture.active;var texture=new Texture2D(1200,800,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=rt;
            for(int frame=0;frame<30;frame++)
            {
                float time=frame/15f;
                foreach(var g in objects)
                {
                    foreach(var ps in g.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(g.name.Contains("FireImpact")?time%1.5f:time+.25f,false,true,true);
                    var animation=g.GetComponent<Animation>();if(animation!=null) animation.clip.SampleAnimation(g,time%1.2f);
                }
                camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,800),0,0);texture.Apply();
                File.WriteAllBytes(Preview+"/frame-"+frame.ToString("D2")+".png",texture.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(texture);Object.DestroyImmediate(rt);
            foreach(var g in objects)Object.DestroyImmediate(g);Object.DestroyImmediate(cameraGO);Object.DestroyImmediate(floor);Object.DestroyImmediate(floorMat);
            Object.DestroyImmediate(volumeRoot);Object.DestroyImmediate(bloom);Object.DestroyImmediate(volumeProfile);
        }
    }
    static string Audit()
    {
        var lines=new List<string>();
        foreach(string name in Names)
        {
            string path=Folder+"/Prefabs/"+name+".prefab";var g=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var deps=AssetDatabase.GetDependencies(path,true);
            var errors=new List<string>();
            if(deps.Any(p=>p.StartsWith("Assets/Art/")))errors.Add("备用目录依赖");
            if(g.GetComponentsInChildren<MonoBehaviour>(true).Length>0)errors.Add("行为脚本");
            if(g.GetComponentsInChildren<Collider>(true).Length>0)errors.Add("碰撞组件");
            if(g.GetComponentsInChildren<Renderer>(true).Any(r=>r.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.name.StartsWith("Universal Render Pipeline"))))errors.Add("缺少或非 URP 材质");
            lines.Add(name+": "+(errors.Count==0?"PASS":string.Join(",",errors))+" particles="+g.GetComponentsInChildren<ParticleSystem>(true).Length+" dependencies="+deps.Length);
            if(errors.Count>0)throw new InvalidOperationException(lines.Last());
        }
        return string.Join("\n",lines);
    }
}
