using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>右侧通道移除与海中攀爬的独立效果试做；只克隆正式资源，不保存游戏资源或修改业务。</summary>
public static class DockSeaEntryPreview
{
    private const int Layer = 30;
    private static readonly List<Mesh> Meshes = new();
    private static string Folder => Path.GetFullPath("Temp/DockSeaEntryPreview-v1");
    private sealed class Actor
    {
        public GameObject Root;
        public Animator Animator;
        public AnimationClip Walk, Idle;
        public float Lane, Delay;
        public LineRenderer[] Ripples;
        public Dictionary<HumanBodyBones, Transform> Bones;
    }

    /// <summary>在同机位比较正式码头与候选，再按人工时间轴采样正式敌人骨架；非真实对局录像。</summary>
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("需本工程空闲编辑态");
        Directory.CreateDirectory(Folder);
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var oldTarget = RenderTexture.active;
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null; Material foam = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab");
            var before = Object.Instantiate(prefab); before.name = "TEMP_Before";
            var candidate = Object.Instantiate(prefab); candidate.name = "TEMP_SeaEntryCandidate";
            int removed = RemoveRightPassage(candidate);
            if (removed < 100) throw new InvalidOperationException("右侧木通道识别异常，停止输出");
            var hero = Hero(new Vector3(7.4f, .04f, -5.7f));
            foam = new Material(Shader.Find("Universal Render Pipeline/Unlit")); foam.color = new Color(.56f, .84f, .86f);
            var actors = Enumerable.Range(0, 3).Select(i => Enemy(-3f - i * 1.1f, i * .82f, foam)).ToArray();
            var go = new GameObject("TEMP_Camera", typeof(Camera)); camera = go.GetComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << Layer; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .14f, .17f); camera.farClipPlane = 180;
            var light = new GameObject("TEMP_Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.3f; light.cullingMask = 1 << Layer; light.transform.rotation = Quaternion.Euler(48, -32, 0);
            target = new RenderTexture(1280, 720, 24); pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); camera.targetTexture = target;
            foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var actor in actors) actor.Root.SetActive(false);
            camera.orthographicSize = 24; camera.transform.position = new Vector3(0, 57, -31); camera.transform.LookAt(Vector3.zero);
            candidate.SetActive(false); Capture("overview-before.png");
            before.SetActive(false); candidate.SetActive(true); Capture("overview-candidate.png");
            foreach (var actor in actors) actor.Root.SetActive(true);
            camera.orthographicSize = 4.9f; camera.transform.position = new Vector3(19, 9.2f, -16); camera.transform.LookAt(new Vector3(11, .1f, -4));
            for (int frame = 0; frame < 180; frame++)
            {
                float time = frame / 20f;
                foreach (var actor in actors) Pose(actor, time - actor.Delay);
                Capture(frame.ToString("D3") + ".png");
            }
            camera.orthographicSize = 2.8f; camera.transform.position = new Vector3(17.5f, 5.4f, -12); camera.transform.LookAt(new Vector3(12, .2f, -3.3f));
            for (int frame = 0; frame < 64; frame++)
            {
                float time = 2.2f + frame / 40f;
                foreach (var actor in actors) Pose(actor, time - actor.Delay);
                Capture("slow-" + frame.ToString("D3") + ".png");
            }
            return "candidate only; removedTriangles=" + removed + "; frames=244; path=" + Folder;

            void Capture(string name)
            {
                camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Folder, name), pixels.EncodeToPNG());
            }
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = oldTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true);
            foreach (var mesh in Meshes) if (mesh != null) Object.DestroyImmediate(mesh); Meshes.Clear();
            if (foam != null) Object.DestroyImmediate(foam);
        }
    }

    /// <summary>只过滤临时副本的东侧低位木板，保留主平台东边立面、绳栏、海面与正式原始网格。</summary>
    private static int RemoveRightPassage(GameObject instance)
    {
        int removed = 0;
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterials.Any(m => m != null && m.shader.name.Contains("DockWater"))) continue;
            var source = filter.sharedMesh; var mesh = Object.Instantiate(source); mesh.name = "TEMP_NoRightPassage"; Meshes.Add(mesh);
            var vertices = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var keep = new List<int>(); var triangles = source.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = filter.transform.TransformPoint(vertices[triangles[i]]);
                    var b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                    var c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                    bool right = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) >= 11.999f && Mathf.Max(a.x, Mathf.Max(b.x, c.x)) > 12.001f;
                    bool deck = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < .07f;
                    if (right && deck) removed++;
                    else { keep.Add(triangles[i]); keep.Add(triangles[i + 1]); keep.Add(triangles[i + 2]); }
                }
                mesh.SetTriangles(keep, sub);
            }
            mesh.RecalculateBounds(); filter.sharedMesh = mesh;
        }
        return removed;
    }

    private static GameObject Hero(Vector3 at)
    {
        var hero = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
        var presenter = hero.GetComponent<CharacterWeaponPresenter>();
        if (!presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException(presenter.LastError);
        presenter.SetManualPoseSampling(true); presenter.BodyAnimator.Rebind(); presenter.BodyAnimator.Update(0);
        presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, .1f); presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
        DisableRuntime(hero); hero.transform.position = at; hero.transform.rotation = Quaternion.Euler(0, 90, 0); return hero;
    }

    private static Actor Enemy(float lane, float delay, Material foam)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
        var view = root.GetComponent<EnemyView>(); view.SetNavigationEnabled(false); view.BeginPresentationUse(); view.SetPresentationVariant(0);
        var animator = view.Animator; animator.Rebind(); animator.Update(0);
        var clips = animator.runtimeAnimatorController.animationClips.Where(c => c != null).ToArray();
        var walk = clips.First(c => c.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0);
        var idle = clips.First(c => c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
        DisableRuntime(root);
        var rings = new LineRenderer[3];
        for (int i = 0; i < rings.Length; i++)
        {
            var go = new GameObject("TEMP_WaterRipple", typeof(LineRenderer));
            var line = go.GetComponent<LineRenderer>(); line.sharedMaterial = foam; line.loop = true; line.positionCount = 28; line.useWorldSpace = true;
            line.startWidth = line.endWidth = .018f; rings[i] = line;
        }
        var names = new Dictionary<HumanBodyBones, string>
        {
            { HumanBodyBones.LeftUpperArm, "UpperArm.L" }, { HumanBodyBones.LeftLowerArm, "LowerArm.L" }, { HumanBodyBones.LeftHand, "Middle1.L" },
            { HumanBodyBones.RightUpperArm, "UpperArm.R" }, { HumanBodyBones.RightLowerArm, "LowerArm.R" }, { HumanBodyBones.RightHand, "Middle1.R" },
            { HumanBodyBones.RightUpperLeg, "UpperLeg.R" }, { HumanBodyBones.RightLowerLeg, "LowerLeg.R" }, { HumanBodyBones.RightFoot, "Foot.R" }
        };
        var transforms = root.GetComponentsInChildren<Transform>(true);
        // 正式便服丧尸使用Generic骨架，没有Humanoid Avatar；按实际骨名缓存临时动作所需关节。
        var bones = names.ToDictionary(pair => pair.Key, pair => transforms.Single(t => t.name == pair.Value));
        return new Actor { Root = root, Animator = animator, Walk = walk, Idle = idle, Lane = lane, Delay = delay, Ripples = rings, Bones = bones };
    }

    /// <summary>仅预览的靠近、抓边、撑身、起身四段动作；没有伤害、碰撞或出生权威含义。</summary>
    private static void Pose(Actor actor, float time)
    {
        float t = Mathf.Max(0, time), x, y, tilt = 0;
        bool water = t < 2.1f, walking = t >= 4.75f;
        (water || walking ? actor.Walk : actor.Idle).SampleAnimation(actor.Animator.gameObject, t % (water || walking ? actor.Walk.length : actor.Idle.length));
        if (water) { float u = t / 2.1f; x = Mathf.Lerp(18.3f, 12.55f, u); y = -1.2f + .035f * Mathf.Sin(t * 7); tilt = 12; }
        else if (t < 2.55f) { x = 12.55f; y = -1.2f; tilt = Mathf.Lerp(12, 24, Smooth((t - 2.1f) / .45f)); }
        // 撑身时双脚继续留在海侧；先抬高髋部，再向平台内移动，避免身体直接穿过木板。
        else if (t < 3.35f) { float u = Smooth((t - 2.55f) / .8f); x = Mathf.Lerp(12.55f, 12.65f, u); y = Mathf.Lerp(-1.2f, -.35f, u); tilt = Mathf.Lerp(24, 55, u); }
        else if (t < 4.1f) { float u = Smooth((t - 3.35f) / .75f); x = Mathf.Lerp(12.65f, 11.75f, u); y = Mathf.Lerp(-.35f, .12f, Smooth(u * 1.5f)); tilt = Mathf.Lerp(55, 32, u); }
        else if (t < 4.75f) { float u = Smooth((t - 4.1f) / .65f); x = Mathf.Lerp(11.75f, 11.35f, u); y = Mathf.Lerp(.12f, .04f, u); tilt = Mathf.Lerp(32, 0, u); }
        else { x = Mathf.Max(8.2f, 11.35f - (t - 4.75f) * .9f); y = .04f; }
        actor.Root.transform.SetPositionAndRotation(new Vector3(x, y, actor.Lane), Quaternion.Euler(0, 270, 0) * Quaternion.Euler(tilt, 0, 0));
        if (t >= 2.1f && t < 4.25f)
        {
            float grip = t < 2.55f ? Smooth((t - 2.1f) / .45f) : 1 - Smooth((t - 3.8f) / .45f);
            Arm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -.26f);
            Arm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, .26f);
            void Arm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand, float side)
            {
                var end = actor.Bones[hand];
                Solve(actor.Bones[upper], actor.Bones[lower], end,
                    Vector3.Lerp(end.position, new Vector3(11.93f, .10f, actor.Lane + side), grip), new Vector3(12.6f, .6f, actor.Lane + side * 2));
            }
        }
        if (t >= 3.05f && t < 4.65f)
        {
            float lift = Smooth((t - 3.05f) / .65f) * (1 - Smooth((t - 4.1f) / .55f));
            var foot = actor.Bones[HumanBodyBones.RightFoot];
            Solve(actor.Bones[HumanBodyBones.RightUpperLeg], actor.Bones[HumanBodyBones.RightLowerLeg], foot,
                Vector3.Lerp(foot.position, new Vector3(11.1f, .09f, actor.Lane + .18f), lift), new Vector3(10, 1, actor.Lane + .25f));
        }
        for (int i = 0; i < actor.Ripples.Length; i++)
        {
            var line = actor.Ripples[i]; line.enabled = time >= 0 && t < 3.05f;
            float radius = .26f + Mathf.Repeat(t * .6f + i * .32f, 1) * .7f;
            for (int j = 0; j < line.positionCount; j++)
            { float angle = j * Mathf.PI * 2 / line.positionCount; line.SetPosition(j, new Vector3(x + Mathf.Cos(angle) * radius, -.332f, actor.Lane + Mathf.Sin(angle) * radius * .6f)); }
        }
    }

    private static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));

    /// <summary>临时骨架两段求解，用实际肢体长度把手掌固定到平台边，不写入动画资源。</summary>
    private static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        if (upper == null || lower == null || end == null) return;
        var origin = upper.position; float first = Vector3.Distance(origin, lower.position), second = Vector3.Distance(lower.position, end.position);
        var direction = (target - origin).normalized; float distance = Mathf.Clamp(Vector3.Distance(target, origin), .001f, first + second - .001f);
        float along = (first * first - second * second + distance * distance) / (2 * distance);
        var side = Vector3.ProjectOnPlane(pole - origin, direction).normalized;
        var elbow = origin + direction * along + side * Mathf.Sqrt(Mathf.Max(0, first * first - along * along));
        upper.rotation = Quaternion.FromToRotation(lower.position - origin, elbow - origin) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, origin + direction * distance - lower.position) * lower.rotation;
    }

    private static void DisableRuntime(GameObject root)
    {
        foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
