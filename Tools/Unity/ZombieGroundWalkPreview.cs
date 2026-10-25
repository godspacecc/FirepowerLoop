using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Game;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>地面步态的独立候选：只采样正式克隆，临时落脚约束不写回资源或业务。</summary>
public static class ZombieGroundWalkPreview
{
    private const int Layer = 30;
    private const float Speed = .9f;
    private const float Cycle = .82f;
    private const float Stance = .60f;
    private static bool appliedCapture;
    private static string Folder => Path.GetFullPath(appliedCapture ? "../Backup/Artifacts/ZombieGroundWalkApplied-20261025/frames" : "../Backup/Artifacts/ZombieGroundWalk-v2-20261025/frames");
    internal sealed class Actor
    {
        public GameObject Root;
        public Animator Animator;
        public AnimationClip Clip;
        public Transform Hips;
        public Transform[] Upper, Knee, Foot;
        public Quaternion[] FootRotation;
        public float[] Side;
        public float Ground;
        public bool Candidate;
        public string Kind;
    }

    /// <summary>三种原尺寸丧尸，同一速度/镜头/主角参照，前半常用视角、后半较低机位。</summary>
    public static string Run() => Render(true);
    public static string Check() => Render(false);
    /// <summary>左侧重放已采用候选，右侧只使用正式Animator的Run状态，检验持久化绑定与姿态。</summary>
    public static string CaptureApplied()
    {
        appliedCapture = true;
        try { return Render(true); }
        finally { appliedCapture = false; }
    }

    private static string Render(bool render)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许本工程空闲编辑态制作预览");
        Directory.CreateDirectory(Folder);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var priorTarget = RenderTexture.active;
        var owned = new List<Object>();
        Camera camera = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("缺少正式渲染管线Shader");
            var groundMaterial = new Material(shader); groundMaterial.color = new Color(.25f, .29f, .29f); owned.Add(groundMaterial);
            var gridMaterial = new Material(shader); gridMaterial.color = new Color(.42f, .47f, .45f); owned.Add(gridMaterial);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "TEMP_Ground";
            ground.transform.position = new Vector3(0, -.065f, 0); ground.transform.localScale = new Vector3(14, .1f, 10);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            for (int i = -6; i <= 6; i++)
            {
                var grid = GameObject.CreatePrimitive(PrimitiveType.Cube); grid.name = "TEMP_FootfallReference";
                grid.transform.position = new Vector3(i * .5f, -.011f, 0); grid.transform.localScale = new Vector3(.012f, .005f, 8);
                grid.GetComponent<Renderer>().sharedMaterial = gridMaterial;
            }
            var hero = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var presenter = hero.GetComponent<CharacterWeaponPresenter>();
            if (!presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException(presenter.LastError);
            presenter.SetManualPoseSampling(true); presenter.BodyAnimator.Rebind(); presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject, .1f);
            presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle); Disable(hero);
            hero.transform.SetPositionAndRotation(new Vector3(-.2f, .015f, 1.65f), Quaternion.Euler(0, 235, 0));
            var lamp = new GameObject("TEMP_Key", typeof(Light)).GetComponent<Light>();
            lamp.type = LightType.Directional; lamp.intensity = 1.6f; lamp.color = new Color(1, .94f, .84f);
            lamp.transform.rotation = Quaternion.Euler(45, -40, 0); lamp.cullingMask = 1 << Layer;
            var fill = new GameObject("TEMP_Fill", typeof(Light)).GetComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .65f; fill.transform.rotation = Quaternion.Euler(35, 140, 0); fill.cullingMask = 1 << Layer;
            camera = new GameObject("TEMP_Camera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .13f, .15f); camera.farClipPlane = 100;
            target = new RenderTexture(640, 600, 24); pixels = new Texture2D(640, 600, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            var actors = new List<Actor>();
            foreach (var kind in new[] { "Shambler", "Gaunt", "Bloated" })
            {
                actors.Add(Create(kind, false)); actors.Add(Create(kind, true));
            }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            var samples = new List<string> { "kind|candidate|time|leftPhase|rightPhase|leftKnee|rightKnee|leftX|leftY|rightX|rightY|leftError|rightError|bodyDrop|bodyY|headY" };
            var info = new List<string>();
            for (int group = 0; group < 3; group++)
            {
                foreach (var actor in actors) actor.Root.SetActive(false);
                var before = actors[group * 2]; var after = actors[group * 2 + 1];
                info.Add(before.Kind + "|clip=" + before.Clip.name + "|length=" + before.Clip.length + "|groundPivot=" + before.Ground);
                for (int frame = 0; frame < 120; frame++)
                {
                    bool low = frame >= 60;
                    float time = low ? 1.2f + (frame - 60) / 40f : frame / 20f;
                    camera.orthographicSize = low ? 2.12f : 2.65f;
                    camera.transform.position = low ? new Vector3(2.2f, 2.6f, -7) : new Vector3(3.2f, 6.2f, -7.5f);
                    camera.transform.LookAt(new Vector3(0, low ? 1.04f : .75f, .5f));
                    foreach (var actor in new[] { before, after })
                    {
                        before.Root.SetActive(false); after.Root.SetActive(false); actor.Root.SetActive(true);
                        if (appliedCapture && actor.Candidate) SampleFormal(actor, time);
                        else
                        {
                            bool candidate = actor.Candidate;
                            if (appliedCapture) actor.Candidate = true;
                            Pose(actor, time, samples);
                            actor.Candidate = candidate;
                        }
                        if (!render) continue;
                        camera.Render(); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 640, 600), 0, 0); pixels.Apply();
                        File.WriteAllBytes(Path.Combine(Folder, group + "-" + (actor.Candidate ? "after" : "before") + "-" + frame.ToString("D3") + ".png"), pixels.EncodeToPNG());
                    }
                }
            }
            File.WriteAllLines(Path.Combine(Folder, "motion.tsv"), samples);
            File.WriteAllLines(Path.Combine(Folder, "clips.txt"), info);
            return "Temporary clone preview only; 3 kinds x 120 frames x 2 sides; " + Folder;
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            RenderTexture.active = priorTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (pixels != null) Object.DestroyImmediate(pixels);
            SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
        }
    }

    internal static Actor Create(string kind, bool candidate)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie" + kind + ".prefab"));
        root.name = "TEMP_" + kind + (candidate ? "_Candidate" : "_Current");
        var view = root.GetComponent<EnemyView>(); view.SetNavigationEnabled(false); view.BeginPresentationUse(); view.SetPresentationVariant(0);
        var animator = view.Animator; animator.Rebind(); animator.Update(0);
        // 固定采用预览的原始上身来源，正式Run改绑后重复制作不能二次叠加已烘焙步态。
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Art/Enemies/Zombies/Civilian/" + (kind == "Gaunt" ? "Run_Arms" : "Walk") + ".anim");
        if (clip == null) throw new InvalidOperationException("移动状态未绑定实际AnimationClip: " + kind);
        Disable(root);
        var all = root.GetComponentsInChildren<Transform>(true);
        Transform Bone(string name) => all.Single(t => t.name == name);
        var actor = new Actor { Root = root, Animator = animator, Clip = clip, Candidate = candidate, Kind = kind,
            Hips = Bone("Body"), Upper = new[] { Bone("UpperLeg.L"), Bone("UpperLeg.R") },
            Knee = new[] { Bone("LowerLeg.L"), Bone("LowerLeg.R") }, Foot = new[] { Bone("Foot.L"), Bone("Foot.R") },
            FootRotation = new Quaternion[2], Side = new float[2] };
        actor.Ground = float.MaxValue;
        for (int i = 0; i < 48; i++)
        {
            clip.SampleAnimation(animator.gameObject, i * clip.length / 48);
            foreach (var foot in actor.Foot) actor.Ground = Mathf.Min(actor.Ground, root.transform.InverseTransformPoint(foot.position).y);
        }
        clip.SampleAnimation(animator.gameObject, 0);
        for (int i = 0; i < 2; i++)
        {
            actor.FootRotation[i] = Quaternion.Inverse(root.transform.rotation) * actor.Foot[i].rotation;
            actor.Side[i] = root.transform.InverseTransformPoint(actor.Foot[i].position).x;
        }
        root.SetActive(false); return actor;
    }

    private static void SampleFormal(Actor actor, float time)
    {
        actor.Root.transform.SetPositionAndRotation(new Vector3(1.25f - Speed * time, .015f, 0), Quaternion.Euler(0, 270, 0));
        actor.Animator.enabled = true;
        actor.Animator.applyRootMotion = false;
        actor.Animator.SetFloat("MoveSpeed", Speed);
        actor.Animator.Play("Run", 0, Mathf.Repeat(time / Cycle, 1));
        actor.Animator.Update(0);
        actor.Root.transform.SetPositionAndRotation(new Vector3(1.25f - Speed * time, .015f, 0), Quaternion.Euler(0, 270, 0));
    }

    /// <summary>接触期足端与地面相对静止，摆腿期跨前并抬脚；速度只用于候选步频，不改变位移。</summary>
    internal static void Pose(Actor actor, float time, List<string> samples)
    {
        var rotation = Quaternion.Euler(0, 270, 0);
        actor.Root.transform.SetPositionAndRotation(new Vector3(1.25f - Speed * time, .015f, 0), rotation);
        actor.Clip.SampleAnimation(actor.Animator.gameObject, actor.Candidate ? Mathf.Repeat(time / Cycle, 1) * actor.Clip.length : time % actor.Clip.length);
        // 采样可能包含根曲线，显式恢复演示位移和朝向，左右两边速度完全相同。
        actor.Root.transform.SetPositionAndRotation(new Vector3(1.25f - Speed * time, .015f, 0), rotation);
        float phase = Mathf.Repeat(time / Cycle, 1);
        var errors = new float[2];
        float originalBodyY = actor.Hips.position.y;
        if (actor.Candidate)
        {
            var targets = new Vector3[2];
            var pitches = new float[2];
            for (int side = 0; side < 2; side++)
            {
                float p = Mathf.Repeat(phase + side * .5f, 1);
                float step = Speed * Cycle * Stance;
                float forward, lift = 0, pitch = 0;
                if (p < Stance) forward = step * .5f - Speed * Cycle * p;
                else
                {
                    float swing = (p - Stance) / (1 - Stance);
                    float smooth = Mathf.SmoothStep(0, 1, swing);
                    forward = Mathf.Lerp(-step * .5f, step * .5f, smooth);
                    lift = (actor.Kind == "Bloated" ? .05f : .065f) * Mathf.Pow(Mathf.Sin(swing * Mathf.PI), 1.15f);
                    pitch = -8 * Mathf.Sin(swing * Mathf.PI);
                }
                targets[side] = actor.Root.transform.position + rotation * new Vector3(actor.Side[side], actor.Ground + lift, forward);
                pitches[side] = pitch;
            }
            // 保留原站立高度；只在步幅超出原骨长时求出必要的小幅下降，禁止持续矮身。
            var originalBody = actor.Hips.position;
            bool Reachable()
            {
                for (int side = 0; side < 2; side++)
                {
                    var a = actor.Hips.InverseTransformPoint(actor.Upper[side].position);
                    var b = actor.Hips.InverseTransformPoint(actor.Knee[side].position);
                    var c = actor.Hips.InverseTransformPoint(actor.Foot[side].position);
                    float length = (Vector3.Distance(a, b) + Vector3.Distance(b, c)) * .998f;
                    if (Vector3.Distance(a, actor.Hips.InverseTransformPoint(targets[side])) > length) return false;
                }
                return true;
            }
            if (!Reachable())
            {
                float low = 0, high = .07f;
                for (int i = 0; i < 12; i++)
                {
                    float middle = (low + high) * .5f;
                    actor.Hips.position = originalBody + Vector3.down * middle;
                    if (Reachable()) high = middle; else low = middle;
                }
                actor.Hips.position = originalBody + Vector3.down * high;
            }
            for (int side = 0; side < 2; side++)
            {
                var target = targets[side];
                Solve(actor.Upper[side], actor.Knee[side], actor.Foot[side], target, actor.Upper[side].position + actor.Root.transform.forward);
                actor.Foot[side].rotation = Quaternion.AngleAxis(pitches[side], actor.Root.transform.right) * rotation * actor.FootRotation[side];
                errors[side] = Vector3.Distance(actor.Foot[side].position, target);
            }
        }
        float Knee(int side) => 180 - Vector3.Angle(actor.Upper[side].position - actor.Knee[side].position, actor.Foot[side].position - actor.Knee[side].position);
        if(samples!=null) samples.Add(FormattableString.Invariant($"{actor.Kind}|{actor.Candidate}|{time:F4}|{phase:F4}|{Mathf.Repeat(phase + .5f, 1):F4}|{Knee(0):F3}|{Knee(1):F3}|{actor.Foot[0].position.x:F4}|{actor.Foot[0].position.y:F4}|{actor.Foot[1].position.x:F4}|{actor.Foot[1].position.y:F4}|{errors[0]:F5}|{errors[1]:F5}|{originalBodyY-actor.Hips.position.y:F4}|{actor.Hips.position.y:F4}|{actor.Root.GetComponentsInChildren<Transform>(true).Single(b=>b.name=="Head").position.y:F4}"));
    }

    private static void Solve(Transform upper, Transform knee, Transform foot, Vector3 target, Vector3 pole)
    {
        // 在骨盆的局部空间解析求解，避免正式瘦弱/重型根节点的非等比缩放引入足端偏差。
        var reference = upper.parent;
        var start = upper.localPosition;
        var jointBefore = reference.InverseTransformPoint(knee.position);
        var endBefore = reference.InverseTransformPoint(foot.position);
        var localTarget = reference.InverseTransformPoint(target);
        float a = Vector3.Distance(start, jointBefore), b = Vector3.Distance(jointBefore, endBefore);
        var direction = (localTarget - start).normalized;
        float distance = Mathf.Clamp(Vector3.Distance(localTarget, start), Mathf.Abs(a - b) + .001f, (a + b) * .9999f);
        float along = (a * a - b * b + distance * distance) / (2 * distance);
        var bend = Vector3.ProjectOnPlane(reference.InverseTransformPoint(pole) - start, direction).normalized;
        var joint = start + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        upper.localRotation = Quaternion.FromToRotation(jointBefore - start, joint - start) * upper.localRotation;
        var kneeOrigin = knee.localPosition;
        var footInUpper = upper.InverseTransformPoint(foot.position);
        var targetInUpper = upper.InverseTransformPoint(reference.TransformPoint(start + direction * distance));
        knee.localRotation = Quaternion.FromToRotation(footInUpper - kneeOrigin, targetInUpper - kneeOrigin) * knee.localRotation;
    }

    private static void Disable(GameObject root)
    {
        foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
