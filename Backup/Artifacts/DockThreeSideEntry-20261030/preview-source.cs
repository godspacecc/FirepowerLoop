using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>隔离场景采样正式地图、正式三种敌人和正式主角；用于接入实拍，不冒充自然对局录像。</summary>
public static class DockThreeSideEntryPreview
{
    private const int Layer = 30, Width = 1280, Height = 720;
    private const string Characters = "Assets/Game/Prefabs/Characters/";
    private const string Art = "Assets/Game/Art/Enemies/Zombies/Civilian/";
    private static readonly string[] Kinds = { "Shambler", "Gaunt", "Bloated" };
    private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/DockThreeSideEntry-20261030/frames"));

    /// <summary>编辑态手动时间轴，不运行MonoBehaviour.Update；主段180帧20fps，近景100帧40fps。</summary>
    public static string Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许本工程空闲编辑态拍摄已正式接入的码头攀爬");
        ValidateDependencies();
        Directory.CreateDirectory(Folder);
        var prior = SceneManager.GetActiveScene();
        bool inPlay = EditorApplication.isPlaying;
        var scene = inPlay ? SceneManager.CreateScene("TEMP_DockThreeSideEntryPreview") : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var oldTarget = RenderTexture.active;
        var owned = new List<Object>();
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var map = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab"));
            map.name = "TEMP_FormalArcadeDocksVisual"; map.SetActive(true); SetLayer(map);
            RemoveTemporaryPassages(map, owned);
            var hero = CreateHero(owned);
            var enemies = Kinds.Select((kind, index) => CreateEnemy(kind, index, owned)).ToArray();
            var light = new GameObject("TEMP_AppliedKey", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f; light.cullingMask = 1 << Layer;
            light.transform.rotation = Quaternion.Euler(48, -32, 0);
            camera = new GameObject("TEMP_AppliedCamera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .14f, .17f); camera.farClipPlane = 180;
            target = new RenderTexture(Width, Height, 24); pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var timeline = new List<string> { "capture|frame|time|kind|age|active|rootX|rootZ|height|tilt" };
            camera.orthographicSize = 21f; camera.transform.position = new Vector3(0, 38, -29);
            camera.transform.LookAt(new Vector3(-1, .1f, 1));
            hero.Root.transform.SetPositionAndRotation(new Vector3(-1, .04f, -2), Quaternion.Euler(0, 90, 0));
            for (int frame = 0; frame < 180; frame++)
            {
                float time = frame / 20f;
                SampleHero(hero);
                for (int i = 0; i < enemies.Length; i++) SampleEnemy(enemies[i], time, 1 / 20f, "main", frame, time, timeline);
                Capture(frame.ToString("D3") + ".png");
            }

            // 三段近景分别展示三处平台边缘，同一正式主角原尺寸比较；不更改正式相机。
            for (int direction = 0; direction < 3; direction++)
            {
                foreach (var enemy in enemies) ResetEnemy(enemy);
                Vector3 edge = direction == 0 ? new Vector3(12,0,-3) : direction == 1 ? new Vector3(-14,0,4.1f) : new Vector3(3,0,11.5f);
                Vector3 forward = EntryRotation(direction) * Vector3.left;
                hero.Root.transform.SetPositionAndRotation(edge + forward * 3 + Vector3.right * (direction == 2 ? -2 : 0) + Vector3.up * .04f, Quaternion.LookRotation(-forward));
                camera.orthographicSize = 4.2f;
                camera.transform.position = edge - forward * 7 + Vector3.up * 8 + (direction == 2 ? Vector3.right * 5 : Vector3.back * 8);
                camera.transform.LookAt(edge + Vector3.up * .3f);
                for (int frame = 0; frame < 140; frame++)
                {
                    float time = frame / 20f;
                    SampleHero(hero);
                    for (int i = 0; i < enemies.Length; i++)
                    {
                        if(i == direction)SampleEnemy(enemies[i],time,1/20f,"detail"+direction,frame,time,timeline);
                        else enemies[i].Root.SetActive(false);
                    }
                    Capture("detail-"+direction+"-"+frame.ToString("D3")+".png");
                }
            }

            File.WriteAllLines(Path.Combine(Folder, "timeline.tsv"), timeline);
            return "TEMP candidate, three entries: main180 + detail3x140 @20fps, formal actors reused; path="+Folder;

            void Capture(string name)
            {
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Folder, name), pixels.EncodeToPNG());
            }
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = oldTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            if (scene.IsValid() && scene.isLoaded)
            {
                if(inPlay){foreach(var root in scene.GetRootGameObjects())Object.DestroyImmediate(root);SceneManager.UnloadSceneAsync(scene);}
                else EditorSceneManager.CloseScene(scene, true);
            }
            foreach (var item in owned) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        }
    }

    /// <summary>正式片段和泡沫必须已挂到实际Prefab；未接入时拒绝退回原型或生成替代美术。</summary>
    private static void ValidateDependencies()
    {
        var dock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab");
        if (dock == null || AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "GrayboxPlayer.prefab") == null)
            throw new InvalidOperationException("缺少正式码头或正式主角");
        foreach (var filter in dock.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null) throw new InvalidOperationException("正式码头缺少网格或Renderer");
            if (renderer.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name.Contains("DockWater"))) continue;
            var vertices = filter.sharedMesh.vertices;
            var matrix = dock.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
            {
                var triangles = filter.sharedMesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = matrix.MultiplyPoint3x4(vertices[triangles[i]]); var b = matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    var c = matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                    if (Mathf.Min(a.x, Mathf.Min(b.x, c.x)) >= 11.999f && Mathf.Max(a.x, Mathf.Max(b.x, c.x)) > 12.001f && Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < .07f)
                        throw new InvalidOperationException("正式码头尚未执行已采用的右侧低位撤板");
                }
            }
        }
        foreach (string kind in Kinds)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "ArcadeZombie" + kind + ".prefab");
            var view = prefab == null ? null : prefab.GetComponent<EnemyView>();
            var entry = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + kind + "DockSeaEntry.anim");
            var ground = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + kind + "GroundWalk.anim");
            if (view == null || view.Animator == null || entry == null || ground == null || ground.length <= 0)
                throw new InvalidOperationException("缺少正式三体型攀爬或地面片段：" + kind);
            var serialized = new SerializedObject(view);
            var clip = serialized.FindProperty("dockSeaEntryClip"); var material = serialized.FindProperty("dockSeaEntryRippleMaterial");
            if (clip == null || clip.objectReferenceValue != entry || material == null || material.objectReferenceValue == null ||
                AssetDatabase.GetAssetPath(material.objectReferenceValue) != "Assets/Game/Art/Arcade/Docks/DockSeaEntryFoam.mat")
                throw new InvalidOperationException("正式Prefab尚未绑定攀爬及泡沫资源：" + kind);
        }
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

    /// <summary>直接实例化正式EnemyView，不调用候选Pose或临时IK。</summary>
    private static Actor CreateEnemy(string kind, int index, List<Object> owned)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "ArcadeZombie" + kind + ".prefab")); root.SetActive(false);
        var view = root.GetComponent<EnemyView>();
        var actor = new Actor { Root = root, View = view, Animator = view.Animator, Kind = kind, Index = index,
            Ground = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + kind + "GroundWalk.anim") };
        ResetEnemy(actor);
        actor.Idle = actor.Animator.runtimeAnimatorController.animationClips.First(c => c != null && c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
        CreateSkinProxies(actor, owned); SetLayer(root); return actor;
    }

    /// <summary>重新绑定同一实例的新用途；显式禁止导航并清除终态后，近景从指定权威年龄直接采样。</summary>
    private static void ResetEnemy(Actor actor)
    {
        actor.Root.SetActive(false); actor.View.BeginPresentationUse(); actor.View.SetNavigationEnabled(false); actor.View.SetPresentationVariant(actor.Index);
        actor.Animator.enabled = true; actor.Root.SetActive(true); actor.Animator.Rebind(); actor.Animator.Update(0); DisableAutomatic(actor.Root);
        foreach (var skin in actor.Skins) skin.Source.enabled = false;
    }

    /// <summary>正式Idle及原有双手握持求解，与角色/武器资源自身表现一致。</summary>
    private static void SampleHero(Actor actor)
    {
        actor.Idle.SampleAnimation(actor.Animator.gameObject, .1f); actor.Presenter.ApplyOffhandGrip(actor.Presenter.Current.Animations.Idle);
        foreach (var skin in actor.Skins) UpdateSkin(skin);
    }

    /// <summary>位移与姿态分别由共享权威路径和正式View消费；结束后仅演示既有地面步态的参考续步。</summary>
    private static void SampleEnemy(Actor actor, float localTime, float step, string capture, int frame, float globalTime, List<string> timeline)
    {
        bool visible = localTime >= 0;
        if (actor.Root.activeSelf != visible) actor.Root.SetActive(visible);
        if (!visible) return;
        if (!ArcadeDockSeaEntryRules.TryStart("docks", 2, actor.Index, out var state)) throw new InvalidOperationException("正式右路进场规则未启用");
        state.Age = Mathf.Min(localTime, ArcadeDockSeaEntryRules.TotalSeconds);
        var pose = ArcadeDockSeaEntryRules.Sample(state.Age);
        float after = Mathf.Max(0, localTime - ArcadeDockSeaEntryRules.TotalSeconds);
        var planar = new Vector3(pose.X - after * .9f, 0, state.Lane);
        var rotation = EntryRotation(actor.Index);
        planar = rotation * planar + (actor.Index == 1 ? new Vector3(-2,0,0) : actor.Index == 2 ? new Vector3(-2.2f,0,-.5f) : Vector3.zero);
        var forward = rotation * Vector3.left;
        actor.Root.transform.SetPositionAndRotation(planar, Quaternion.LookRotation(forward, Vector3.up));
        actor.View.TickCombatFeedback(step);
        if (state.Active) actor.Idle.SampleAnimation(actor.Animator.gameObject, .1f);
        else actor.Ground.SampleAnimation(actor.Animator.gameObject, (localTime - ArcadeDockSeaEntryRules.ClimbSeconds) % actor.Ground.length);
        actor.View.SetDockSeaEntry(state, forward, Vector3.up); actor.View.LateTickCombatFeedback();
        if ((actor.Root.transform.position - planar).sqrMagnitude > .000001f) throw new InvalidOperationException("正式View意外改变战斗根位置：" + actor.Kind);
        foreach (var skin in actor.Skins) UpdateSkin(skin);
        timeline.Add(FormattableString.Invariant($"{capture}|{frame}|{globalTime:F3}|{actor.Kind}|{state.Age:F3}|{state.Active}|{planar.x:F3}|{planar.z:F3}|{pose.Height:F3}|{pose.Tilt:F3}"));
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

    /// <summary>仅候选场景中旋转既有攀爬姿态和位置，使左、上入口对准主平台。</summary>
    private static Quaternion EntryRotation(int direction) => Quaternion.Euler(0,direction==1?180:direction==2?-90:0,0);

    /// <summary>只克隆网格后移除左、上外伸几何，水面和正式资产不变。</summary>
    private static void RemoveTemporaryPassages(GameObject map,List<Object> owned)
    {
        int removed=0;
        foreach(var filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer=filter.GetComponent<MeshRenderer>();
            if(renderer.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.Contains("DockWater")))continue;
            var mesh=Object.Instantiate(filter.sharedMesh);owned.Add(mesh);filter.sharedMesh=mesh;
            var vertices=mesh.vertices;var matrix=map.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var triangles=mesh.GetTriangles(sub);var keep=new List<int>();
                for(int i=0;i<triangles.Length;i+=3)
                {
                    var a=matrix.MultiplyPoint3x4(vertices[triangles[i]]);var b=matrix.MultiplyPoint3x4(vertices[triangles[i+1]]);var c=matrix.MultiplyPoint3x4(vertices[triangles[i+2]]);
                    bool left=Mathf.Max(a.x,Mathf.Max(b.x,c.x))<=-13.999f&&Mathf.Min(a.x,Mathf.Min(b.x,c.x))<-14.001f;
                    bool top=Mathf.Min(a.z,Mathf.Min(b.z,c.z))>=11.499f&&Mathf.Max(a.z,Mathf.Max(b.z,c.z))>11.501f;
                    if(left||top)removed++;else{keep.Add(triangles[i]);keep.Add(triangles[i+1]);keep.Add(triangles[i+2]);}
                }
                mesh.SetTriangles(keep,sub,false);
            }
            mesh.RecalculateBounds();
        }
        File.WriteAllText(Path.Combine(Folder,"temporary-mesh-audit.txt"),"removed triangles="+removed+"; all meshes cloned in memory; formal resources untouched");
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
        public EnemyView View;
        public Animator Animator;
        public CharacterWeaponPresenter Presenter;
        public AnimationClip Idle, Ground;
        public string Kind;
        public int Index;
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
