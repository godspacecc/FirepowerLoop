using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using FirepowerLoop.Arcade;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>正式Controller和正式资源的隔离快照回放，不冒充自然对局。</summary>
public static class BurrowerHoundAppliedCapture
{
    private const int Layer=30,Width=1280,Height=720;
    private const string Characters="Assets/Game/Prefabs/Characters/";
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static string Folder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures/Applied"));
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅允许本工程空闲编辑态回放");
        Directory.CreateDirectory(Folder);var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var owned=new List<Object>();var old=RenderTexture.active;Camera camera=null;RenderTexture target=null;Texture2D pixels=null;
        var report=new List<string>{"正式Controller/资源隔离快照采样；非自然对局；为看清腿和口部隐藏环境和头顶HUD。"};
        try
        {
            SceneManager.SetActiveScene(scene);Ground(owned);var hero=CreateHero(owned);SampleHero(hero);var hb=Bounds(hero.Root);
            var key=new GameObject("TEMP_CreatureKey",typeof(Light)).GetComponent<Light>();key.type=LightType.Directional;key.intensity=1.35f;key.cullingMask=1<<Layer;key.transform.rotation=Quaternion.Euler(48,-32,0);
            var fill=new GameObject("TEMP_CreatureFill",typeof(Light)).GetComponent<Light>();fill.type=LightType.Directional;fill.intensity=.55f;fill.cullingMask=1<<Layer;fill.transform.rotation=Quaternion.Euler(27,125,0);
            camera=new GameObject("TEMP_CreatureAppliedCamera",typeof(Camera)).GetComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.08f,.09f);camera.farClipPlane=100;
            target=new RenderTexture(Width,Height,24);pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false);camera.targetTexture=target;
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            foreach(int kind in new[]{4,5})
            {
                string name=kind==4?"burrower":"hound";string frames=Path.Combine(Folder,name);Directory.CreateDirectory(frames);
                var owner=new GameObject("TEMP_CreatureAppliedOwner");var view=new ArcadeCombatViewController();
                typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,owner);typeof(ArcadeCombatViewController).GetField("mapRoot",Hidden).SetValue(view,owner.transform);
                typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
                var layout=tables.TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).First(p=>p.ArenaConfigNumber==kind+1&&p.RoundNumber==1);
                try
                {
                    view.ConfigureRoundBounds(19.5f,19.5f,.55f);view.SetRoundLayout(layout);if(!view.IsRoundLayoutReady)throw new InvalidOperationException(view.RoundLayoutLoadError);
                    view.RecordRoundEnemyPosition(7,new Vector2(0,kind==4?-1:-2.5f));view.SetEnemyContent(7,kind,0,false);
                    var root=owner.GetComponentsInChildren<Transform>().Single(t=>t.name==(kind==4?"Burrower_7":"Hound_7"));
                    foreach(Transform n in root)if(n.name.StartsWith("Health",StringComparison.Ordinal)||n.name=="SkillWarning")n.gameObject.SetActive(false);
                    owner.GetComponentsInChildren<Transform>().Single(t=>t.name=="Environment_"+layout.ThemeKey).gameObject.SetActive(false);SetLayer(owner);
                    hero.Root.transform.SetPositionAndRotation(new Vector3(0,-hb.min.y,kind==4?.35f:.15f),Quaternion.Euler(0,180,0));
                    camera.orthographicSize=kind==4?1.37f:1.65f;var focus=new Vector3(0,.58f,-.45f);camera.transform.position=focus+new Vector3(5.5f,2.8f,-4.5f);camera.transform.LookAt(focus);
                    var timeline=new List<string>{"frame,time,x,z,visualY,jawAngle"};
                    for(int frame=0;frame<192;frame++)
                    {
                        float t=frame/24f;Vector2 pos;Vector2 facing;
                        if(kind==4){pos=new Vector2(-Mathf.Clamp(t-4.4f,0,1.6f)*.23f,-1+Mathf.Clamp(t-1.2f,0,2.2f)*.3f);facing=t<4.4f?Vector2.up:Vector2.left;}
                        else{pos=new Vector2(-Mathf.Clamp(t-4.5f,0,1.5f)*.9f,-2.5f+Mathf.Min(t,.6f)*.9f+Mathf.Clamp(t-1.2f,0,1f)*1.4f);facing=t<4.5f?Vector2.up:Vector2.left;}
                        view.RecordRoundEnemyPosition(7,pos);view.SetCreatureState(7,t,facing);
                        float remaining=t>=3.4f&&t<3.8f?3.8f-t:0,recovery=t>=3.8f&&t<4.2f?4.2f-t:0;
                        view.SetEnemyMelee(7,.4f,remaining,recovery,facing);SampleHero(hero);
                        var visual=root.Find("CreatureActor/Visual");var jaw=visual.Find("Body/Neck/Head/Jaw");
                        timeline.Add(FormattableString.Invariant($"{frame},{t:F5},{pos.x:F5},{pos.y:F5},{visual.localPosition.y:F5},{jaw.localEulerAngles.x:F5}"));
                        camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(frames,frame.ToString("D4")+".png"),pixels.EncodeToPNG());
                    }
                    File.WriteAllLines(Path.Combine(Folder,name+"-timeline.csv"),timeline);report.Add(name+"=192 frames @24fps; skillPeriod="+layout.SkillPeriod+" warning="+layout.SkillWarning);
                    Fidelity(root.Find("CreatureActor").GetComponent<ArcadeCreatureView>(),kind,owned,report);
                }
                finally{typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);Object.DestroyImmediate(owner);}
            }
            File.WriteAllLines(Path.Combine(Folder,"capture.txt"),report);return string.Join("\n",report);
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;RenderTexture.active=old;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);foreach(var o in owned)if(o!=null&&!EditorUtility.IsPersistent(o))Object.DestroyImmediate(o);
        }
    }
    /// <summary>用采用源的绝对姿态独立对照烘焙片段，限制位置及角度误差。</summary>
    private static void Fidelity(ArcadeCreatureView formal,int kind,List<Object> owned,List<string> report)
    {
        var candidate=BurrowerHoundPreview.Create(kind,null,owned);candidate.SetActive(false);
        try
        {
            foreach(var sample in kind==4?new[]{("Idle",0f,0f),("Move",.31f,5.46f),("Sink",.8f,.8f),("Underground",.5f,1.85f),("Emerge",.8f,3.8f)}:new[]{("Idle",0f,0f),("Move",.2f,.2f),("Windup",.5f,3.25f),("Pounce",.4f,4.16f)})
            {
                formal.SamplePose(sample.Item1,sample.Item2);BurrowerHoundPreview.Sample(candidate,kind,sample.Item3);
                float position=0,angle=0;
                foreach(var n in candidate.GetComponentsInChildren<Transform>(true).Where(n=>n!=candidate.transform))
                {
                    string path=AnimationUtility.CalculateTransformPath(n,candidate.transform);var f=formal.transform.Find(path);
                    position=Mathf.Max(position,Vector3.Distance(n.localPosition,f.localPosition));angle=Mathf.Max(angle,Quaternion.Angle(n.localRotation,f.localRotation));
                }
                if(position>.003f||angle>.35f)throw new InvalidOperationException("采用姿态不一致："+kind+"/"+sample.Item1+" position="+position+" angle="+angle);
                report.Add(kind+"/"+sample.Item1+" positionError="+position.ToString("F6")+" angleError="+angle.ToString("F6"));
            }
        }
        finally{Object.DestroyImmediate(candidate);}
    }
    private static Bounds Bounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b;
    }

    /// <summary>统一简洁实心地面及半米参考网格，便于看清脚掌支撑与移动。</summary>
    private static void Ground(List<Object> owned)
    {
        Quad("TEMP_Floor", -8, 8, -8, 8, 0, new Color(.24f, .25f, .22f), owned);
        for (int i = -12; i <= 12; i++)
        {
            float x = i * .5f;
            Quad("TEMP_Grid", x - .003f, x + .003f, -6, 6, .001f, new Color(.29f, .30f, .26f), owned);
            Quad("TEMP_Grid", -6, 6, x - .003f, x + .003f, .001f, new Color(.29f, .30f, .26f), owned);
        }
    }

    private static void Quad(string name, float x0, float x1, float z0, float z1, float y, Color color, List<Object> owned)
    {
        var mesh = new Mesh { vertices = new[] { new Vector3(x0,y,z0), new Vector3(x0,y,z1), new Vector3(x1,y,z1), new Vector3(x1,y,z0) }, triangles = new[] {0,1,2,0,2,3} };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); owned.Add(mesh);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color }; owned.Add(material);
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.layer = Layer;
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
    /// <summary>保持正式主角尺寸与既有持枪待机，仅关闭临时实例的自动推进组件。</summary>
    private static Actor CreateHero(List<Object> owned)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "GrayboxPlayer.prefab")); root.SetActive(true);
        var presenter = root.GetComponent<CharacterWeaponPresenter>();
        if (presenter == null || !presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException("正式主角持枪依赖未就绪");
        presenter.SetManualPoseSampling(true);
        var animator = presenter.BodyAnimator; animator.enabled = true; animator.Rebind(); animator.Update(0);
        var actor = new Actor { Root = root, Animator = animator, Presenter = presenter, Idle = presenter.Current.Animations.Idle.Clip };
        DisableAutomatic(root); CreateSkinProxies(actor, owned); SetLayer(root); return actor;
    }

    /// <summary>正式Idle及原有双手握持求解，与角色/武器资源自身表现一致。</summary>
    private static void SampleHero(Actor actor)
    {
        actor.Idle.SampleAnimation(actor.Animator.gameObject, .1f); actor.Presenter.ApplyOffhandGrip(actor.Presenter.Current.Animations.Idle);
        foreach (var skin in actor.Skins) UpdateSkin(skin);
    }

    /// <summary>关闭编辑态实例的自动动画、碰撞和粒子，不改正式资产。</summary>
    private static void DisableAutomatic(GameObject root)
    {
        foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static void SetLayer(GameObject root)
    { foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = Layer; }

    /// <summary>用正式骨矩阵手动刷新蒙皮代理，避开禁用Animator时编辑器的蒙皮缓存。</summary>
    private static void CreateSkinProxies(Actor actor, List<Object> owned)
    {
        foreach (var source in actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || source.sharedMesh == null || source.bones.Length == 0) continue;
            var original = source.sharedMesh;
            var output = Object.Instantiate(original); output.name = "TEMP_FormalSampledSkin"; owned.Add(output);
            var proxy = new GameObject("TEMP_FormalSampledSkin", typeof(MeshFilter), typeof(MeshRenderer)); proxy.transform.SetParent(source.transform, false);
            proxy.GetComponent<MeshFilter>().sharedMesh = output; proxy.GetComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            var skin = new Skin { Source = source, Output = output, Vertices = original.vertices, Normals = original.normals,
                Weights = original.boneWeights, Bindposes = original.bindposes, Bones = source.bones };
            if (skin.Weights.Length != skin.Vertices.Length || skin.Normals.Length != skin.Vertices.Length || skin.Bindposes.Length != skin.Bones.Length)
                throw new InvalidOperationException("正式蒙皮数据不完整：" + source.name);
            skin.Positions = new Vector3[skin.Vertices.Length]; skin.Directions = new Vector3[skin.Vertices.Length]; skin.Matrices = new Matrix4x4[skin.Bones.Length];
            actor.Skins.Add(skin); source.enabled = false;
        }
        if (actor.Skins.Count == 0) throw new InvalidOperationException("正式演员缺少可采样蒙皮：" + actor.Root.name);
    }

    private static void UpdateSkin(Skin skin)
    {
        for (int i = 0; i < skin.Bones.Length; i++) skin.Matrices[i] = skin.Source.transform.worldToLocalMatrix * skin.Bones[i].localToWorldMatrix * skin.Bindposes[i];
        for (int i = 0; i < skin.Vertices.Length; i++)
        {
            var weight = skin.Weights[i]; var vertex = skin.Vertices[i]; var normal = skin.Normals[i]; var matrices = skin.Matrices;
            skin.Positions[i] = matrices[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0 + matrices[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1 +
                matrices[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2 + matrices[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
            skin.Directions[i] = (matrices[weight.boneIndex0].MultiplyVector(normal) * weight.weight0 + matrices[weight.boneIndex1].MultiplyVector(normal) * weight.weight1 +
                matrices[weight.boneIndex2].MultiplyVector(normal) * weight.weight2 + matrices[weight.boneIndex3].MultiplyVector(normal) * weight.weight3).normalized;
        }
        skin.Output.vertices = skin.Positions; skin.Output.normals = skin.Directions; skin.Output.RecalculateBounds();
    }

    private sealed class Actor
    {
        public GameObject Root;
        public Animator Animator;
        public CharacterWeaponPresenter Presenter;
        public AnimationClip Idle;
        public readonly List<Skin> Skins = new List<Skin>();
    }
    private sealed class Skin
    {
        public SkinnedMeshRenderer Source;
        public Mesh Output;
        public Vector3[] Vertices, Normals, Positions, Directions;
        public BoneWeight[] Weights;
        public Matrix4x4[] Bindposes, Matrices;
        public Transform[] Bones;
    }
}
