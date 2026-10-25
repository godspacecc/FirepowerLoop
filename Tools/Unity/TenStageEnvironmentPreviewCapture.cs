using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>三关待采用候选的隔离实拍工具：正式现版与临时几何同机位、同灯光、正式主角同框。</summary>
public static class TenStageEnvironmentPreviewCapture
{
    private const int Layer = 30, Width = 1600, Height = 1200;
    private const string Characters = "Assets/Game/Prefabs/Characters/";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Backup/Artifacts/TenStageEnvironments-v1-20261025"));

    /// <summary>仅外部取得Unity独占使用后调用；12个真实布局，共24全景、6近景，候选无自动动态效果。</summary>
    public static string RunStills() { return RunFor(0); }
    /// <summary>允许分关拍摄，避免每次微调重复捕获其余两关；0为全部，其他只接受7/10/20。</summary>
    public static string RunFor(int arena)
    {
        if (arena != 0 && arena != 7 && arena != 10 && arena != 20) throw new ArgumentException("只接受0/7/10/20");
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" ||
            EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("需要本项目空闲编辑态，且由主会话持有Unity独占权");
        Directory.CreateDirectory(Folder);
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var owned = new List<Object>(); var oldTarget = RenderTexture.active;
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null; ArcadeCombatViewController view = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var tables = new global::Tables(n => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes"))));
            var provider = new LubanRuntimeConfigProvider(() => tables);
            if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
            float hw = catalog.Rule.ArenaHalfWidth, hh = catalog.Rule.ArenaHalfHeight, radius = catalog.Rule.PlayerRadius;
            if (hw != 19.5f || hh != 19.5f) throw new InvalidOperationException("候选只支持正式39米场地");
            var layouts = tables.TbArcadeRoundLayout.DataList.Where(p => (p.ArenaConfigNumber == 7 || p.ArenaConfigNumber == 10 || p.ArenaConfigNumber == 20) &&
                (arena == 0 || p.ArenaConfigNumber == arena)).Select(ArcadeRoundLayoutParameters.FromConfig).OrderBy(p => p.ArenaConfigNumber).ThenBy(p => p.RoundNumber).ToArray();
            if (layouts.Length != (arena == 0 ? 12 : 4)) throw new InvalidOperationException("三关每关必须是四个真实轮布局");
            Light("Key", 1.3f, new Vector3(48, -32, 0)); Light("Fill", .38f, new Vector3(38, 145, 0)); Light("Side", .18f, new Vector3(35, 65, 0));
            camera = new GameObject("TEMP_TenStagePreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.cullingMask = 1 << Layer; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.10f, .15f, .19f); camera.farClipPlane = 240;
            target = new RenderTexture(Width, Height, 24); pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false); camera.targetTexture = target;
            var hero = CreateHero(owned); hero.Root.transform.SetPositionAndRotation(new Vector3(0, .025f, -4), Quaternion.Euler(0, 30, 0));
            var checks = new List<string> { "layout|check|value" };
            foreach (var layout in layouts)
            {
                if (!layout.FitsArena(hw, hh, radius)) throw new InvalidOperationException("原布局通路无效：" + layout.Id);
                string themeFolder = Path.Combine(Folder, layout.ArenaConfigNumber == 7 ? "Fort" : layout.ArenaConfigNumber == 10 ? "Koi" : "Nest");
                Directory.CreateDirectory(themeFolder);
                var formal = new GameObject("TEMP_Formal_" + layout.Id);
                view = new ArcadeCombatViewController(); typeof(ArcadeCombatViewController).GetField("root", Hidden).SetValue(view, formal);
                view.ConfigureRoundBounds(hw, hh, radius); view.SetRoundLayout(layout);
                if (!view.IsRoundLayoutReady) throw new InvalidOperationException(view.RoundLayoutLoadError);
                SetLayer(formal);
                var candidateHolder = new GameObject("TEMP_CandidateHolder_" + layout.Id);
                var candidate = TenStageEnvironmentCandidates.Build(candidateHolder.transform, layout, owned);
                foreach (var marker in formal.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Entry_")))
                    Object.Instantiate(marker.gameObject, candidateHolder.transform, true);
                SetLayer(candidateHolder); candidateHolder.SetActive(false);
                VerifyFootprints(layout, candidate, checks);
                checks.Add(layout.Id + "|FitsArena|true"); checks.Add(layout.Id + "|previewKind|temporary environment candidate, formal hero, no rules changed");
                checks.Add(FormattableString.Invariant($"{layout.Id}|bounds/playerRadius|{hw},{hh},{radius}"));
                checks.Add(FormattableString.Invariant($"{layout.Id}|spawn|{layout.SpawnX},{layout.SpawnY}"));
                for (int i = 0; i < layout.Entries.Count; i++) checks.Add(FormattableString.Invariant($"{layout.Id}|entry{i}|{layout.Entries[i].X},{layout.Entries[i].Y}"));
                for (int i = 0; i < layout.ExitCount; i++) { var p = layout.GetExitPosition(i, hw, hh, radius); checks.Add(FormattableString.Invariant($"{layout.Id}|exit{i}|{p.X},{p.Y}")); }
                Wide(); Capture(Path.Combine(themeFolder, "round-" + layout.RoundNumber + "-before.png"));
                formal.SetActive(false); candidateHolder.SetActive(true); Capture(Path.Combine(themeFolder, "round-" + layout.RoundNumber + "-candidate.png"));
                if (layout.RoundNumber == 1)
                {
                    var o = layout.Obstacles[0];
                    hero.Root.transform.position = new Vector3(o.X, .025f, o.Y - o.HalfHeight - 1.55f);
                    camera.orthographicSize = 6.9f; camera.transform.position = new Vector3(o.X + 7, 10, o.Y - 13);
                    camera.transform.LookAt(new Vector3(o.X, .7f, o.Y)); SampleHero(hero);
                    Capture(Path.Combine(themeFolder, "round-1-close-candidate.png"));
                    formal.SetActive(true); candidateHolder.SetActive(false); Capture(Path.Combine(themeFolder, "round-1-close-before.png"));
                    hero.Root.transform.position = new Vector3(0, .025f, -4);
                }
                checks.Add(layout.Id + "|formalHeroScale|" + hero.Root.transform.localScale);
                view.End(); view = null; Object.DestroyImmediate(candidateHolder);
            }
            File.WriteAllLines(Path.Combine(Folder, arena == 0 ? "layout-footprint-checks.tsv" : "layout-footprint-checks-" + arena + ".tsv"), checks);
            return "Temporary unapproved candidates; layouts=" + layouts.Length + "; PNGs=" + layouts.Length * 2 + "+" + (arena == 0 ? 6 : 2) + " close; no dynamic effects; " + Folder;

            void Light(string name, float intensity, Vector3 angles)
            {
                var l = new GameObject("TEMP_TenStage" + name, typeof(Light)).GetComponent<Light>();
                l.type = LightType.Directional; l.intensity = intensity; l.cullingMask = 1 << Layer; l.transform.rotation = Quaternion.Euler(angles);
                if (name != "Key") l.shadows = LightShadows.None;
            }
            void Wide() { camera.orthographicSize = 27; camera.transform.position = new Vector3(0, 58, -38); camera.transform.LookAt(new Vector3(0, 0, 2)); SampleHero(hero); }
            void Capture(string path) { camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
        }
        finally
        {
            if (view != null) view.End(); if (camera != null) camera.targetTexture = null; RenderTexture.active = oldTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); } if (pixels != null) Object.DestroyImmediate(pixels);
            if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var item in owned) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        }
    }

    /// <summary>每轮逐顶点验证真实矩形足迹，外景不侵入战场；失败时拒绝继续出图。</summary>
    private static void VerifyFootprints(ArcadeRoundLayoutParameters layout, GameObject candidate, List<string> rows)
    {
        if (candidate.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("候选不能创建碰撞器");
        for (int i = 0; i < layout.Obstacles.Count; i++)
        {
            var o = layout.Obstacles[i]; var t = candidate.transform.Find("ConfiguredObstacle_" + i);
            if (t == null) throw new InvalidOperationException("候选缺少障碍" + i);
            var points = t.GetComponentsInChildren<MeshFilter>().SelectMany(f => f.sharedMesh.vertices.Select(v => candidate.transform.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();
            float xmin = points.Min(p => p.x), xmax = points.Max(p => p.x), zmin = points.Min(p => p.z), zmax = points.Max(p => p.z);
            if (Mathf.Abs(xmin - (o.X - o.HalfWidth)) > .001f || Mathf.Abs(xmax - (o.X + o.HalfWidth)) > .001f ||
                Mathf.Abs(zmin - (o.Y - o.HalfHeight)) > .001f || Mathf.Abs(zmax - (o.Y + o.HalfHeight)) > .001f)
                throw new InvalidOperationException("障碍投影越界/缩边：" + layout.Id + "/" + i + " actual=" + xmin + "," + xmax + "," + zmin + "," + zmax);
            rows.Add(FormattableString.Invariant($"{layout.Id}|obstacle{i} exact footprint|{xmin:F3},{xmax:F3},{zmin:F3},{zmax:F3}"));
        }
        var outside = candidate.transform.Find("OutsidePlayableArea_Landmarks");
        foreach (var f in outside.GetComponentsInChildren<MeshFilter>()) foreach (var v in f.sharedMesh.vertices)
        {
            var p = candidate.transform.InverseTransformPoint(f.transform.TransformPoint(v));
            if (Mathf.Abs(p.x) < 19.5f && Mathf.Abs(p.z) < 19.5f) throw new InvalidOperationException("外景侵入39米场地：" + f.name);
        }
        rows.Add(layout.Id + "|outsideLandmarks|all vertices outside playable rectangle"); rows.Add(layout.Id + "|candidateColliders|0");
    }

    /// <summary>正式主角原Prefab、正式武器和正式待机，手动蒙皮代理仅用于隔离场景采样。</summary>
    private static Actor CreateHero(List<Object> owned)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "GrayboxPlayer.prefab");
        if (prefab == null) throw new InvalidOperationException("缺少正式主角");
        var root = Object.Instantiate(prefab); root.SetActive(true);
        var presenter = root.GetComponent<CharacterWeaponPresenter>();
        if (presenter == null || !presenter.SetWeaponId("arcade.weapon.basic")) throw new InvalidOperationException("正式主角武器未就绪");
        presenter.SetManualPoseSampling(true); var animator = presenter.BodyAnimator; animator.enabled = true; animator.Rebind(); animator.Update(0);
        var actor = new Actor { Root = root, Animator = animator, Presenter = presenter, Idle = presenter.Current.Animations.Idle.Clip };
        foreach (var a in root.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var a in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) a.enabled = false;
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var c in root.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || source.sharedMesh == null || source.bones.Length == 0) continue;
            var original = source.sharedMesh; var output = Object.Instantiate(original); output.name = "TEMP_FormalSampledSkin"; owned.Add(output);
            var proxy = new GameObject("TEMP_FormalSampledSkin", typeof(MeshFilter), typeof(MeshRenderer)); proxy.transform.SetParent(source.transform, false);
            proxy.GetComponent<MeshFilter>().sharedMesh = output; proxy.GetComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            var skin = new Skin { Source = source, Output = output, Vertices = original.vertices, Normals = original.normals, Weights = original.boneWeights, Bindposes = original.bindposes, Bones = source.bones };
            if (skin.Weights.Length != skin.Vertices.Length || skin.Normals.Length != skin.Vertices.Length || skin.Bindposes.Length != skin.Bones.Length) throw new InvalidOperationException("正式蒙皮数据不完整");
            skin.Positions = new Vector3[skin.Vertices.Length]; skin.Directions = new Vector3[skin.Vertices.Length]; skin.Matrices = new Matrix4x4[skin.Bones.Length]; actor.Skins.Add(skin); source.enabled = false;
        }
        if (actor.Skins.Count == 0) throw new InvalidOperationException("正式主角缺少可采样蒙皮");
        SetLayer(root); return actor;
    }

    private static void SampleHero(Actor actor)
    {
        actor.Idle.SampleAnimation(actor.Animator.gameObject, .1f); actor.Presenter.ApplyOffhandGrip(actor.Presenter.Current.Animations.Idle);
        foreach (var skin in actor.Skins)
        {
            for (int i = 0; i < skin.Bones.Length; i++) skin.Matrices[i] = skin.Source.transform.worldToLocalMatrix * skin.Bones[i].localToWorldMatrix * skin.Bindposes[i];
            for (int i = 0; i < skin.Vertices.Length; i++)
            {
                var w = skin.Weights[i]; var v = skin.Vertices[i]; var n = skin.Normals[i]; var m = skin.Matrices;
                skin.Positions[i] = m[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + m[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 + m[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + m[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                skin.Directions[i] = (m[w.boneIndex0].MultiplyVector(n) * w.weight0 + m[w.boneIndex1].MultiplyVector(n) * w.weight1 + m[w.boneIndex2].MultiplyVector(n) * w.weight2 + m[w.boneIndex3].MultiplyVector(n) * w.weight3).normalized;
            }
            skin.Output.vertices = skin.Positions; skin.Output.normals = skin.Directions; skin.Output.RecalculateBounds();
        }
    }
    private static void SetLayer(GameObject root) { foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer; }
    private sealed class Actor
    {
        public GameObject Root; public Animator Animator; public CharacterWeaponPresenter Presenter; public AnimationClip Idle;
        public readonly List<Skin> Skins = new List<Skin>();
    }
    private sealed class Skin
    {
        public SkinnedMeshRenderer Source; public Mesh Output; public Vector3[] Vertices, Normals, Positions, Directions;
        public BoneWeight[] Weights; public Matrix4x4[] Bindposes, Matrices; public Transform[] Bones;
    }
}
