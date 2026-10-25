using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>五种待采用演员与正式主角的隔离手动采样，旧轮廓直接读取正式VisualProfile；不运行AI或结算命中。</summary>
public static class TenStageCreaturePreviewCapture
{
    private const int Layer = 30, Width = 1280, Height = 720, Frames = 160;
    private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/TenStageCreatures-v1-20261025"));
    private sealed class Kind
    {
        public string Name, AttackPath; public int Special, Boss; public float ContactTime;
        public Kind(string name, int special, int boss, string path, float time)
        { Name = name; Special = special; Boss = boss; AttackPath = path; ContactTime = time; }
    }
    private static readonly Kind[] Kinds =
    {
        new Kind("Spider",6,0,"Body/Head/FangR",4.9f),
        new Kind("Ghoul",10,0,"Body/ArmR/Hand",5.4f),
        new Kind("RedMangler",17,0,"Body/ArmR/Hand/BarrelRecoil",3.15f),
        new Kind("ZRex",0,2,"Body/Core/Head/Jaw",3.8f),
        new Kind("Papaback",0,4,"Body/ArmR/Hand",5.8f)
    };

    /// <summary>主会话取得Unity独占权后执行：800张主帧及10张静态同尺度对照，无慢放重复帧。</summary>
    public static string Run()
    {
        if (Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets" ||
            EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("只允许本项目空闲编辑态，由主会话统一执行");
        Directory.CreateDirectory(Folder);
        var prior = SceneManager.GetActiveScene(); var oldTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); var owned = new List<Object>();
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var world = new GameObject("TEMP_CreaturePreviewWorld"); Ground(world.transform, owned);
            Key("Key", 1.3f, new Vector3(48,-32,0)); Key("Fill", .40f, new Vector3(38,145,0)); Key("Side", .18f, new Vector3(35,65,0));
            camera = new GameObject("TEMP_CreaturePreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.cullingMask = 1 << Layer; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f,.16f,.18f); camera.farClipPlane = 160; camera.nearClipPlane = .02f;
            target = new RenderTexture(Width, Height, 24); pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false); camera.targetTexture = target;
            var hero = CreateHero(owned); SampleHero(hero);
            var summary = new List<string> { "kind|measure|value" };
            var timeline = new List<string> { "kind|frame|time|phase|rootX|rootY|rootZ|heroX|heroZ|attackX|attackY|attackZ|attackToHeroBounds|footLX|footLY|footLZ|footRX|footRY|footRZ|candidateWidth|candidateHeight|candidateDepth" };
            foreach (var kind in Kinds)
            {
                string output = Path.Combine(Folder, "frames", kind.Name); Directory.CreateDirectory(output);
                var actor = TenStageCreatureCandidates.Create(kind.Special, kind.Boss, world.transform, owned);
                var old = CreateOldProfile(kind, world.transform, owned); SetLayer(actor); SetLayer(old); old.SetActive(false);
                if (actor.GetComponentsInChildren<Collider>(true).Length != 0 || old.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("候选与旧轮廓不得有碰撞器");
                TenStageCreatureCandidates.Sample(actor, kind.Special, kind.Boss, kind.ContactTime);
                var attackPart = actor.transform.Find(kind.AttackPath);
                if (attackPart == null) throw new InvalidOperationException("候选攻击参考缺失：" + kind.AttackPath);
                Vector3 contactLocal = ForwardPoint(attackPart);
                // 依据真实手指/下颌/獠牙网格设定近身展示落点，不凭空声明命中。
                var heroContact = new Vector3(contactLocal.x, .025f, contactLocal.z + .12f);
                if (kind.Special == 17) heroContact = new Vector3(-.15f, .025f, 3.5f);
                hero.Root.transform.SetPositionAndRotation(heroContact, Quaternion.Euler(0,180,0)); SampleHero(hero);
                Bounds envelope = BoundsOf(hero.Root); Bounds candidateEnvelope = default; bool first = true;
                Vector3 minimumPoseSize = new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity), maximumPoseSize = Vector3.zero;
                float minFoot = float.PositiveInfinity, nearestGap = float.PositiveInfinity;
                for (int f = 0; f < Frames; f++)
                {
                    float t = f / 20f; Pose(actor, kind, t); var cb = BoundsOf(actor); envelope.Encapsulate(cb);
                    if (first) { candidateEnvelope = cb; first = false; } else candidateEnvelope.Encapsulate(cb);
                    minimumPoseSize = Vector3.Min(minimumPoseSize,cb.size); maximumPoseSize = Vector3.Max(maximumPoseSize,cb.size);
                    var point = ForwardPoint(attackPart); float gap = Vector3.Distance(point, BoundsOf(hero.Root).ClosestPoint(point));
                    if (t >= kind.ContactTime - .3f && t <= kind.ContactTime + .3f) nearestGap = Mathf.Min(nearestGap, gap);
                    minFoot = Mathf.Min(minFoot, cb.min.y);
                }
                Fit(camera, envelope, new Vector3(8.5f,5.5f,8));
                // 主段只展示候选与正式主角；接触目标不位移，镜头由整段实际包围范围统一计算。
                for (int frame = 0; frame < Frames; frame++)
                {
                    float t = frame / 20f; Pose(actor, kind, t); SampleHero(hero);
                    var point = ForwardPoint(attackPart); var hb = BoundsOf(hero.Root); var cb = BoundsOf(actor);
                    var feet = Feet(actor, kind); float gap = Vector3.Distance(point, hb.ClosestPoint(point));
                    timeline.Add(FormattableString.Invariant($"{kind.Name}|{frame}|{t:F3}|{Phase(kind,t)}|{actor.transform.position.x:F4}|{actor.transform.position.y:F4}|{actor.transform.position.z:F4}|{hero.Root.transform.position.x:F4}|{hero.Root.transform.position.z:F4}|{point.x:F4}|{point.y:F4}|{point.z:F4}|{gap:F4}|{feet[0].x:F4}|{feet[0].y:F4}|{feet[0].z:F4}|{feet[1].x:F4}|{feet[1].y:F4}|{feet[1].z:F4}|{cb.size.x:F4}|{cb.size.y:F4}|{cb.size.z:F4}"));
                    Save(Path.Combine(output, frame.ToString("D3") + ".png"));
                }
                // 两张静态用完全相同机位/尺寸与主角位置。旧版是权威Profile静态形状，候选为待采用草稿。
                Pose(actor, kind, .35f); actor.transform.position = Vector3.zero;
                hero.Root.transform.SetPositionAndRotation(new Vector3(kind.Boss > 0 ? -2.4f : -1.65f,.025f,.2f), Quaternion.Euler(0,20,0)); SampleHero(hero);
                Bounds comparison = BoundsOf(actor); comparison.Encapsulate(BoundsOf(hero.Root)); comparison.Encapsulate(BoundsOf(old));
                Fit(camera, comparison, new Vector3(8.5f,5.5f,8));
                Save(Path.Combine(output,"static-candidate.png"));
                actor.SetActive(false); old.SetActive(true); Save(Path.Combine(output,"static-old.png")); old.SetActive(false);
                summary.Add(kind.Name + "|candidateEnvelopeWidthHeightDepth|" + Triple(candidateEnvelope.size));
                summary.Add(kind.Name + "|candidatePhysicalPoseSizeMin|" + Triple(minimumPoseSize));
                summary.Add(kind.Name + "|candidatePhysicalPoseSizeMax|" + Triple(maximumPoseSize));
                summary.Add(kind.Name + "|oldProfileWidthHeightDepth|" + Triple(BoundsOf(old).size));
                summary.Add(kind.Name + "|formalHeroWidthHeightDepthIncludingHeldWeapon|" + Triple(BoundsOf(hero.Root).size));
                summary.Add(kind.Name + "|formalHeroPrefabScale|" + Triple(hero.Root.transform.localScale));
                summary.Add(kind.Name + "|candidateRootScale|" + Triple(actor.transform.localScale));
                summary.Add(kind.Name + "|heroContactRoot|" + Triple(heroContact));
                summary.Add(kind.Name + "|contactReferenceAtTime|" + kind.AttackPath + " @ " + kind.ContactTime);
                summary.Add(FormattableString.Invariant($"{kind.Name}|nearestContactReferenceToHeroAABB|{nearestGap:F4}"));
                summary.Add(FormattableString.Invariant($"{kind.Name}|candidateLowestVertexAllFrames|{minFoot:F4}"));
                summary.Add(kind.Name + "|contactMeaning|hand/mouth visual reference to renderer AABB only; no collider, no AI, no attack sequence, no hit or damage validation");
                summary.Add(kind.Name + "|frames|160 at 20fps; 8s manual candidate motion; no slow-frame duplication");
                Object.DestroyImmediate(actor); Object.DestroyImmediate(old);
            }
            File.WriteAllLines(Path.Combine(Folder,"sizes-and-contact-audit.tsv"),summary);
            File.WriteAllLines(Path.Combine(Folder,"motion-timeline.tsv"),timeline);
            File.WriteAllText(Path.Combine(Folder,"capture-notes.txt"),
                "TEMPORARY UNAPPROVED visual candidates. Five 8-second clips, 20fps, 1280x720, formal hero and weapon at original scale. " +
                "Static old renders use ArcadeRoundContentRules.GetVisualProfile without behavior. Candidate root paths are authored absolute positions, not live AI. " +
                "Spider/Ghoul/Papaback approach before the sampled near-contact gesture; ZRex presents charge and mouth/expulsion poses; RedMangler presents armor/cannon posture without spawning a shot. " +
                "Contact audit is a mesh-reference distance to hero renderer AABB, not combat hit validation. No health changes, danger regions, particles, projectiles, collision, scene saves or formal resources. " +
                "Gait is a temporary authored sample; foot positions are logged for visual QA, not a claim of game-speed synchronization. " +
                "Main videos keep the formal hero stationary as a size and contact reference. Pose and path are designed separately; local recovery steps may show some foot sliding. " +
                "Each static pair uses one shared camera framing; old and candidate are separate images so neither obscures the other.");
            return "Five temporary candidates: 800 frames + 10 static comparisons; audits=" + Folder;

            void Key(string name,float intensity,Vector3 angles)
            {
                var light = new GameObject("TEMP_Creature"+name,typeof(Light)).GetComponent<Light>(); light.type=LightType.Directional;
                light.intensity=intensity; light.cullingMask=1<<Layer; light.transform.rotation=Quaternion.Euler(angles); if(name!="Key")light.shadows=LightShadows.None;
            }
            void Save(string path)
            { camera.Render(); RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,Width,Height),0,0); pixels.Apply(); File.WriteAllBytes(path,pixels.EncodeToPNG()); }
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null; RenderTexture.active=oldTarget;
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            foreach(var item in owned)if(item!=null&&!EditorUtility.IsPersistent(item))Object.DestroyImmediate(item);
        }
    }

    /// <summary>真实时间的显式轨迹：先走近，再停步蓄势，近身段不继续穿过主角。</summary>
    private static void Pose(GameObject actor,Kind kind,float t)
    {
        float z;
        if(kind.Special==6)z=Mathf.Lerp(-2.1f,0,Smooth(0,3.8f,t));
        else if(kind.Special==10)z=Mathf.Lerp(-2.1f,-.8f,Smooth(0,2,t))+Mathf.Lerp(0,.8f,Smooth(3.1f,4.2f,t));
        else if(kind.Special==17)z=Mathf.Lerp(-1.3f,-.65f,Smooth(0,2,t))+Mathf.Lerp(0,.65f,Smooth(3.3f,4.7f,t));
        else if(kind.Boss==2)z=Mathf.Lerp(-2.3f,-1.2f,Smooth(0,2,t))+Mathf.Lerp(0,1.2f,Smooth(3.3f,4.4f,t));
        else z=Mathf.Lerp(-2.1f,-1.0f,Smooth(0,2,t))+Mathf.Lerp(0,1.0f,Smooth(3.15f,4.4f,t));
        actor.transform.SetPositionAndRotation(new Vector3(0,0,z),Quaternion.identity);
        TenStageCreatureCandidates.Sample(actor,kind.Special,kind.Boss,t);
    }
    private static float Smooth(float a,float b,float t)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
    private static string Phase(Kind k,float t)
    {
        if(k.Special==6)return t<3.8f?"approach":t<4.4f?"settle":t<5.55f?"near-contact gesture":"recover/step";
        if(t<2)return "approach";if(t<3.15f)return "windup";if(t<4.7f)return "charge/fire pose";
        if(t<5)return "settle";if(t<6.25f)return k.Boss==2?"jaw/expulsion pose":"near-contact gesture";
        return k.Boss==4?"existing ranged windup/recover":"recover/step";
    }

    /// <summary>取指定末端实际网格中前向最远的顶点；不以节点原点冒充獠牙或手指尖。</summary>
    private static Vector3 ForwardPoint(Transform t)
    {
        var points=t.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v))).ToArray();
        if(points.Length==0)throw new InvalidOperationException("末端没有可读网格："+t.name);
        return points.OrderByDescending(p=>p.z).First();
    }
    private static Vector3[] Feet(GameObject actor,Kind kind)
    {
        if(kind.Special==6)return new[]{actor.transform.Find("Body/Leg-1_1/C").TransformPoint(new Vector3(0,.5f,0)),actor.transform.Find("Body/Leg1_1/C").TransformPoint(new Vector3(0,.5f,0))};
        return new[]{actor.transform.Find("Body/LegL/Foot").position,actor.transform.Find("Body/LegR/Foot").position};
    }
    private static Bounds BoundsOf(GameObject go)
    {
        var filters=go.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();
        if(filters.Length==0)throw new InvalidOperationException("没有可读网格："+go.name);
        bool first=true;Bounds b=default;
        foreach(var filter in filters)foreach(var v in filter.sharedMesh.vertices)
        {var p=filter.transform.TransformPoint(v);if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);}
        return b;
    }
    private static string Triple(Vector3 v)=>FormattableString.Invariant($"{v.x:F4},{v.y:F4},{v.z:F4}");

    /// <summary>按整段实际网格包围范围投影求镜头，保证头尾手脚和正式主角完整同框。</summary>
    private static void Fit(Camera camera,Bounds bounds,Vector3 direction)
    {
        var center=bounds.center;camera.transform.position=center+direction.normalized*24;camera.transform.LookAt(center);
        float width=0,height=0;
        foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
        {
            var offset=Vector3.Scale(bounds.extents,new Vector3(x,y,z));
            width=Mathf.Max(width,Mathf.Abs(Vector3.Dot(offset,camera.transform.right)));height=Mathf.Max(height,Mathf.Abs(Vector3.Dot(offset,camera.transform.up)));
        }
        camera.orthographicSize=Mathf.Max(1.8f,Mathf.Max(height,width/((float)Width/Height))+.48f);
    }

    /// <summary>旧版只克隆权威轮廓定义，内置原始体网格直接借用；不创建原始体GameObject或碰撞器。</summary>
    private static GameObject CreateOldProfile(Kind kind,Transform parent,List<Object> owned)
    {
        var root=new GameObject("TEMP_OldProfile_"+kind.Name);root.transform.SetParent(parent,false);
        var palette=new Dictionary<uint,Material>();var profile=ArcadeRoundContentRules.GetVisualProfile(kind.Special,kind.Boss);
        foreach(var part in profile.Parts)
        {
            var mesh=Resources.GetBuiltinResource<Mesh>(part.Primitive+".fbx");if(mesh==null)throw new InvalidOperationException("找不到Unity内置静态网格："+part.Primitive);
            var go=new GameObject("Old_"+part.Primitive,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(root.transform,false);
            go.transform.localPosition=new Vector3(part.X,part.Y,part.Z);go.transform.localScale=new Vector3(part.ScaleX,part.ScaleY,part.ScaleZ);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            if(!palette.TryGetValue(part.Color,out var mat)){mat=Material(part.Color,owned);palette.Add(part.Color,mat);}go.GetComponent<MeshRenderer>().sharedMaterial=mat;
        }
        return root;
    }
    private static Material Material(uint rgba,List<Object> owned)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");if(shader==null)throw new InvalidOperationException("缺少Lit Shader");
        var c=new Color((rgba>>24&255)/255f,(rgba>>16&255)/255f,(rgba>>8&255)/255f,1);var m=new Material(shader){name="TEMP_CaptureMaterial",color=c};
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.08f);if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",.08f);owned.Add(m);return m;
    }
    private static void Ground(Transform t,List<Object> owned)
    {
        var m=Material(0xA2ADA3FFu,owned);Plane("NeutralFloor",new Vector3(0,-.012f,0),new Vector2(32,32),m);
        var line=Material(0x929E97FFu,owned);
        for(int i=-12;i<=12;i++){Plane("MeterGrid",new Vector3(i,-.010f,0),new Vector2(.014f,26),line);Plane("MeterGrid",new Vector3(0,-.010f,i),new Vector2(26,.014f),line);}
        void Plane(string name,Vector3 p,Vector2 size,Material mat)
        {
            var mesh=new Mesh{name="TEMP_"+name};float x=size.x*.5f,z=size.y*.5f;
            mesh.vertices=new[]{new Vector3(-x,0,-z),new Vector3(-x,0,z),new Vector3(x,0,z),new Vector3(x,0,-z)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(t,false);go.transform.localPosition=p;go.layer=Layer;
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
        }
    }

    /// <summary>正式Prefab原尺寸持枪待机，显式禁用自动推进；仅克隆蒙皮输出，不改正式源。</summary>
    private static Actor CreateHero(List<Object> owned)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");if(prefab==null)throw new InvalidOperationException("缺少正式主角");
        var root=Object.Instantiate(prefab);root.SetActive(true);var presenter=root.GetComponent<CharacterWeaponPresenter>();
        if(presenter==null||!presenter.SetWeaponId("arcade.weapon.basic"))throw new InvalidOperationException("正式主角持枪失败");
        presenter.SetManualPoseSampling(true);var animator=presenter.BodyAnimator;animator.enabled=true;animator.Rebind();animator.Update(0);
        var actor=new Actor{Root=root,Animator=animator,Presenter=presenter,Idle=presenter.Current.Animations.Idle.Clip};
        foreach(var a in root.GetComponentsInChildren<Animator>(true))a.enabled=false;
        foreach(var a in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))a.enabled=false;
        foreach(var c in root.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var c in root.GetComponentsInChildren<Canvas>(true))c.gameObject.SetActive(false);
        foreach(var body in root.GetComponentsInChildren<Rigidbody>(true))body.isKinematic=true;
        foreach(var p in root.GetComponentsInChildren<ParticleSystem>(true))p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach(var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!source.enabled||!source.gameObject.activeInHierarchy||source.sharedMesh==null||source.bones.Length==0)continue;
            var original=source.sharedMesh;var output=Object.Instantiate(original);output.name="TEMP_FormalSampledSkin";owned.Add(output);
            var proxy=new GameObject("TEMP_FormalSampledSkin",typeof(MeshFilter),typeof(MeshRenderer));proxy.transform.SetParent(source.transform,false);
            proxy.GetComponent<MeshFilter>().sharedMesh=output;proxy.GetComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
            var skin=new Skin{Source=source,Output=output,Vertices=original.vertices,Normals=original.normals,Weights=original.boneWeights,Bindposes=original.bindposes,Bones=source.bones};
            if(skin.Weights.Length!=skin.Vertices.Length||skin.Normals.Length!=skin.Vertices.Length||skin.Bindposes.Length!=skin.Bones.Length)throw new InvalidOperationException("正式蒙皮数据不完整");
            skin.Positions=new Vector3[skin.Vertices.Length];skin.Directions=new Vector3[skin.Vertices.Length];skin.Matrices=new Matrix4x4[skin.Bones.Length];actor.Skins.Add(skin);source.enabled=false;
        }
        if(actor.Skins.Count==0)throw new InvalidOperationException("正式主角没有采样蒙皮");SetLayer(root);return actor;
    }
    private static void SampleHero(Actor actor)
    {
        actor.Idle.SampleAnimation(actor.Animator.gameObject,.1f);actor.Presenter.ApplyOffhandGrip(actor.Presenter.Current.Animations.Idle);
        foreach(var skin in actor.Skins)
        {
            for(int i=0;i<skin.Bones.Length;i++)skin.Matrices[i]=skin.Source.transform.worldToLocalMatrix*skin.Bones[i].localToWorldMatrix*skin.Bindposes[i];
            for(int i=0;i<skin.Vertices.Length;i++)
            {
                var w=skin.Weights[i];var v=skin.Vertices[i];var n=skin.Normals[i];var m=skin.Matrices;
                skin.Positions[i]=m[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+m[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+m[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+m[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                skin.Directions[i]=(m[w.boneIndex0].MultiplyVector(n)*w.weight0+m[w.boneIndex1].MultiplyVector(n)*w.weight1+m[w.boneIndex2].MultiplyVector(n)*w.weight2+m[w.boneIndex3].MultiplyVector(n)*w.weight3).normalized;
            }
            skin.Output.vertices=skin.Positions;skin.Output.normals=skin.Directions;skin.Output.RecalculateBounds();
        }
    }
    private static void SetLayer(GameObject root){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
    private sealed class Actor
    { public GameObject Root;public Animator Animator;public CharacterWeaponPresenter Presenter;public AnimationClip Idle;public readonly List<Skin> Skins=new List<Skin>(); }
    private sealed class Skin
    { public SkinnedMeshRenderer Source;public Mesh Output;public Vector3[] Vertices,Normals,Positions,Directions;public BoneWeight[] Weights;public Matrix4x4[] Bindposes,Matrices;public Transform[] Bones; }
}
