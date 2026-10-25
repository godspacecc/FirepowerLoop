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

/// <summary>潜地怪与恶犬临时候选隔离采样；不写入正式资产，不执行战斗或自动更新。</summary>
public static class BurrowerHoundPreviewCapture
{
    private const int Layer = 30, Width = 1280, Height = 720;
    private const string Characters = "Assets/Game/Prefabs/Characters/";
    private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/HengeVolcanoPolish-v1-20261025/Creatures"));

    /// <summary>先用少量静帧检查机位；完整版各24fps，原速与半速由编码器独立导出。</summary>
    public static string Run(bool full = false)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许FirepowerLoop空闲编辑态隔离拍摄");
        Directory.CreateDirectory(Folder);
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var oldTarget = RenderTexture.active;
        var owned = new List<Object>();
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        var report = new List<string> { "临时候选：隔离手动采样；不是实战或单双人规则验证。", "正式主角保持Prefab原始缩放。" };
        try
        {
            SceneManager.SetActiveScene(scene);
            Ground(owned);
            var hero = CreateHero(owned); SampleHero(hero);
            var hb = Bounds(hero.Root);
            report.Add("hero bounds=" + hb.size.ToString("F4") + "; scale=" + hero.Root.transform.localScale.ToString("F4"));
            var key = new GameObject("TEMP_CreatureKey", typeof(Light)).GetComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.35f; key.cullingMask = 1 << Layer;
            key.transform.rotation = Quaternion.Euler(48, -32, 0);
            var fill = new GameObject("TEMP_CreatureFill", typeof(Light)).GetComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .55f; fill.cullingMask = 1 << Layer;
            fill.transform.rotation = Quaternion.Euler(27, 125, 0);
            camera = new GameObject("TEMP_CreatureCamera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.065f, .08f, .09f);
            camera.farClipPlane = 100; camera.nearClipPlane = .05f;
            target = new RenderTexture(Width, Height, 24); pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            foreach (int kind in new[] { 4, 5 })
            {
                string label = kind == 4 ? "burrower" : "hound";
                string frames = Path.Combine(Folder, label); Directory.CreateDirectory(frames);
                var actor = BurrowerHoundPreview.Create(kind, null, owned); SetLayer(actor);
                var old = OldSilhouette(kind, owned); old.SetActive(false);
                float start = kind == 4 ? -1f : -2.5f;
                float heroZ = kind == 4 ? .35f : 1.30f;
                hero.Root.transform.SetPositionAndRotation(new Vector3(0, -hb.min.y, heroZ), Quaternion.Euler(0, 180, 0));
                camera.orthographicSize = kind == 4 ? 1.37f : 1.65f;
                Vector3 focus = new Vector3(0, .58f, kind == 4 ? -.38f : -.45f);
                camera.transform.position = focus + new Vector3(5.5f, 2.8f, -4.5f); camera.transform.LookAt(focus);
                Place(actor, kind, 0, start); SampleHero(hero);
                report.Add(label + " standing bounds=" + Bounds(actor).size.ToString("F4"));
                Capture(Path.Combine(Folder, label + "-candidate.png"));
                old.transform.SetPositionAndRotation(actor.transform.position, actor.transform.rotation);
                actor.SetActive(false); old.SetActive(true); Capture(Path.Combine(Folder, label + "-current.png"));
                old.SetActive(false); actor.SetActive(true);
                CheckRepeat(actor, kind, start, report);
                float[] moments = kind == 4 ? new[] { .5f, 1.7f, 3.35f, 3.7f, 4.3f, 5.8f, 7.3f } : new[] { .3f, .9f, 2.8f, 3.4f, 4.12f, 4.45f, 4.9f, 6.5f };
                foreach (float time in moments)
                {
                    Place(actor, kind, time, start); Capture(Path.Combine(Folder, label + "-pose-" + time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
                }
                var timeline = new List<string> { "frame,time,rootZ,visualY,leg,pawX,pawY,pawZ,kneeX,kneeY,kneeZ,upperLength,lowerLength,pawBottom,stance" };
                float seconds = kind == 4 ? BurrowerHoundPreview.BurrowerSeconds : BurrowerHoundPreview.HoundSeconds;
                int count = full ? Mathf.RoundToInt(seconds * 24) : 0;
                for (int frame = 0; frame < count; frame++)
                {
                    float t = frame / 24f; Place(actor, kind, t, start); SampleHero(hero);
                    for (int leg = 0; leg < 4; leg++)
                    {
                        var l = actor.transform.Find("Visual/Leg" + leg); var paw = l.Find("Paw"); var knee = l.Find("Joint");
                        Vector3 p = paw.position, k = knee.position;
                        float bottom = paw.GetComponent<MeshRenderer>().bounds.min.y;
                        bool stance = bottom >= -.008f && bottom < .04f;
                        timeline.Add(FormattableString.Invariant($"{frame},{t:F5},{actor.transform.position.z:F5},{actor.transform.Find("Visual").localPosition.y:F5},{leg},{p.x:F5},{p.y:F5},{p.z:F5},{k.x:F5},{k.y:F5},{k.z:F5},{l.Find("Upper").localScale.z:F5},{l.Find("Lower").localScale.z:F5},{bottom:F5},{stance}"));
                    }
                    Capture(Path.Combine(frames, frame.ToString("D4") + ".png"));
                }
                if (full) File.WriteAllLines(Path.Combine(Folder, label + "-trajectory.csv"), timeline);
                report.Add(label + " frames=" + count + ";24fps; start=" + start + ";heroZ=" + heroZ);
                Object.DestroyImmediate(actor); Object.DestroyImmediate(old);
            }
            File.WriteAllLines(Path.Combine(Folder, "capture-inspection.txt"), report);
            return string.Join("\n", report);

            void Capture(string file)
            {
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(); File.WriteAllBytes(file, pixels.EncodeToPNG());
            }
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = oldTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var item in owned) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        }
    }

    /// <summary>由距离驱动平移；地下保持原位，犬恢复时转身退离主角，避免穿身。</summary>
    private static void Place(GameObject actor, int kind, float t, float start)
    {
        BurrowerHoundPreview.Sample(actor, kind, t);
        float yaw = kind == 5 ? 180 * Smooth(5.55f, 6f, t) : 0;
        actor.transform.SetPositionAndRotation(new Vector3(0, 0, start + BurrowerHoundPreview.PlanarDistance(kind, t)), Quaternion.Euler(0, yaw, 0));
    }

    /// <summary>同一时间经过终态和乱序采样仍完全复现，防止关键帧累计旋转或残留显隐。</summary>
    private static void CheckRepeat(GameObject actor, int kind, float start, List<string> report)
    {
        foreach (float t in new[] { 0f, .8f, 1.7f, 3.3f, 4.2f, 5.1f, 6.5f, 7.7f })
        {
            Place(actor, kind, t, start); string a = Snapshot(actor);
            Place(actor, kind, 8.8f, start); Place(actor, kind, 3.38f, start); Place(actor, kind, .1f, start); Place(actor, kind, t, start);
            if (Snapshot(actor) != a) throw new InvalidOperationException("重复采样不一致：" + kind + "/" + t);
        }
        report.Add("kind=" + kind + " repeat=8/8 exact transform/active snapshots");
    }

    private static string Snapshot(GameObject actor)
    {
        return string.Join("|", actor.GetComponentsInChildren<Transform>(true).Select(t => t.name + t.localPosition.ToString("F6") + t.localRotation.ToString("F6") + t.localScale.ToString("F6") + t.gameObject.activeSelf));
    }

    private static float Smooth(float a, float b, float t) { float p = Mathf.Clamp01((t - a) / (b - a)); return p * p * (3 - 2 * p); }

    /// <summary>读取正式轮廓参数做原尺度回读；只创建临时几何，不含血条和预警图标。</summary>
    private static GameObject OldSilhouette(int kind, List<Object> owned)
    {
        var root = new GameObject("TEMP_CurrentSilhouette");
        foreach (var p in ArcadeRoundContentRules.GetVisualProfile(kind, 0).Parts)
        {
            var part = GameObject.CreatePrimitive((PrimitiveType)Enum.Parse(typeof(PrimitiveType), p.Primitive));
            part.transform.SetParent(root.transform, false); part.transform.localPosition = new Vector3(p.X, p.Y, p.Z);
            part.transform.localScale = new Vector3(p.ScaleX, p.ScaleY, p.ScaleZ);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); uint c = p.Color;
            material.color = new Color(((c >> 24) & 255) / 255f, ((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f);
            owned.Add(material); part.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(part.GetComponent<Collider>());
        }
        SetLayer(root); return root;
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
