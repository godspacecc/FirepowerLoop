using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>丧尸空手动作v2：正式三种丧尸与主角同框，既有扑抓与临时咬击草案，不修改正式资源。</summary>
public static class ZombieUnarmedActionPreview
{
    private sealed class Actor
    {
        public GameObject Root;
        public Animator Animator;
        public CharacterWeaponPresenter Presenter;
        public AnimationClip Idle, Move, Attack;
        public Transform Abdomen, Torso, Head;
        public Quaternion AbdomenRest, TorsoRest, HeadRest;
        public readonly List<SkinnedMeshRenderer> Skins = new List<SkinnedMeshRenderer>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("只允许本项目编辑态隔离预览");
        string folder = Path.GetFullPath("Temp/ZombieUnarmedAction-v2-20261023");
        Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var owned = new List<Object>();
        var actors = new List<Actor>();
        var oldTarget = RenderTexture.active;
        Camera camera = null; RenderTexture target = null; Texture2D pixels = null;
        GameObject New(string name)
        { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go; }
        Actor Clone(string prefab, bool player)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/" + prefab + ".prefab"));
            SceneManager.MoveGameObjectToScene(go, scene);
            var actor = new Actor { Root = go, Presenter = go.GetComponent<CharacterWeaponPresenter>() };
            var view = go.GetComponent<EnemyView>();
            if (view != null) { view.BeginPresentationUse(); view.SetNavigationEnabled(false);
                view.SetPresentationVariant(prefab == "ArcadeZombieGaunt" ? 1 : prefab == "ArcadeZombieBloated" ? 2 : 0); }
            if (player && actor.Presenter != null)
            {
                if (!actor.Presenter.SetWeaponId("arcade.weapon.basic"))
                    throw new InvalidOperationException(actor.Presenter.LastError);
                actor.Presenter.SetManualPoseSampling(true);
            }
            actor.Animator = player ? actor.Presenter.BodyAnimator : view.Animator;
            actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            actor.Animator.Rebind(); actor.Animator.Update(0);
            if (!player)
            {
                var bones = actor.Animator.GetComponentsInChildren<Transform>(true);
                actor.Abdomen = bones.First(t => t.name == "Abdomen");
                actor.Torso = bones.First(t => t.name == "Torso");
                actor.Head = bones.First(t => t.name == "Head");
                actor.AbdomenRest = actor.Abdomen.localRotation;
                actor.TorsoRest = actor.Torso.localRotation;
                actor.HeadRest = actor.Head.localRotation;
            }
            var clips = actor.Animator.runtimeAnimatorController.animationClips;
            actor.Idle = actor.Presenter != null ? actor.Presenter.Current.Animations.Idle.Clip : clips.First(c => c.name.EndsWith("|Idle"));
            actor.Move = actor.Presenter != null ? actor.Presenter.Current.Animations.MoveForward.Clip :
                clips.First(c => c.name.EndsWith(prefab == "ArcadeZombieGaunt" ? "|Run_Arms" : "|Walk"));
            actor.Attack = actor.Presenter != null ? actor.Presenter.Current.Animations.Attack.Clip : clips.First(c => c.name.EndsWith("|Idle_Attack"));
            foreach (var canvas in go.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
            foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                var proxy = new GameObject("TemporarySampledSkin", typeof(MeshFilter), typeof(MeshRenderer));
                proxy.transform.SetParent(skin.transform, false);
                var mesh = Object.Instantiate(skin.sharedMesh); owned.Add(mesh);
                proxy.GetComponent<MeshFilter>().sharedMesh = mesh;
                proxy.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                actor.Skins.Add(skin); actor.Meshes.Add(mesh); skin.enabled = false;
            }
            actors.Add(actor); return actor;
        }
        void Pose(Actor actor, AnimationClip clip, float seconds, float biteLean = 0)
        {
            if (actor.Head != null)
            {
                actor.Abdomen.localRotation = actor.AbdomenRest;
                actor.Torso.localRotation = actor.TorsoRest;
                actor.Head.localRotation = actor.HeadRest;
            }
            clip.SampleAnimation(actor.Animator.gameObject, Mathf.Clamp(seconds, 0, clip.length));
            if (actor.Presenter != null)
                actor.Presenter.ApplyOffhandGrip(clip == actor.Move ? actor.Presenter.Current.Animations.MoveForward :
                    clip == actor.Attack ? actor.Presenter.Current.Animations.Attack : actor.Presenter.Current.Animations.Idle);
            if (biteLean > 0)
            {
                // 源骨架的局部轴不一致，以角色世界右轴前倾，避免把探咬采样成扭身。
                var axis = actor.Root.transform.right;
                actor.Abdomen.rotation = Quaternion.AngleAxis(22 * biteLean, axis) * actor.Abdomen.rotation;
                actor.Torso.rotation = Quaternion.AngleAxis(20 * biteLean, axis) * actor.Torso.rotation;
                actor.Head.rotation = Quaternion.AngleAxis(-12 * biteLean, axis) * actor.Head.rotation;
            }
            for (int i = 0; i < actor.Skins.Count; i++) UpdateSkin(actor.Skins[i], actor.Meshes[i]);
        }
        try
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); owned.Add(mat); mat.color = new Color(.28f, .33f, .29f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0, -.08f, 0); floor.transform.localScale = new Vector3(30, .12f, 30);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var sun = New("TemporaryKeyLight").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 1.6f; sun.transform.rotation = Quaternion.Euler(50, -35, 0);
            camera = New("TemporaryPreviewCamera").AddComponent<Camera>(); camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 2.15f;
            camera.transform.position = new Vector3(1.8f, 3.1f, -6); camera.transform.LookAt(new Vector3(.2f, .85f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .11f, .13f);
            camera.cullingMask = ~(1 << 5);
            target = new RenderTexture(900, 580, 24); camera.targetTexture = target;
            pixels = new Texture2D(900, 580, TextureFormat.RGB24, false);
            var player = Clone("GrayboxPlayer", true);
            var enemies = new[] { Clone("ArcadeZombieShambler", false), Clone("ArcadeZombieGaunt", false), Clone("ArcadeZombieBloated", false) };
            var report = new List<string>();
            foreach (var enemy in enemies)
            {
                // 先验证正式的Attack触发确实能切入攻击片段，随后候选仅作手动姿态采样。
                enemy.Root.SetActive(true); enemy.Animator.enabled = true; enemy.Animator.Rebind(); enemy.Animator.Update(0);
                enemy.Root.GetComponent<EnemyView>().PlayAttackAnimation(); enemy.Animator.Update(.1f);
                bool attacking = enemy.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") ||
                    enemy.Animator.GetNextAnimatorStateInfo(0).IsName("Attack");
                report.Add(enemy.Root.name + ": attackTrigger=" + attacking + ", attack=" + enemy.Attack.name +
                    ", length=" + enemy.Attack.length + ", scale=" + enemy.Root.transform.localScale + ", attachedWeapons=" +
                    enemy.Root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("HeldWeapon_")) + ", jaw=" +
                    enemy.Animator.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Jaw"));
                if (!attacking) throw new InvalidOperationException("正式攻击触发未切入Attack：" + enemy.Root.name);
                enemy.Root.SetActive(false);
            }
            foreach (var actor in actors) foreach (var animator in actor.Root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            for (int kind = 0; kind < 4; kind++)
            {
                bool bite = kind == 3;
                var enemy = enemies[bite ? 0 : kind]; enemy.Root.SetActive(true);
                float strike = .40f;
                string frames = Path.Combine(folder, new[] { "shambler-claw", "gaunt-claw", "bloated-claw", "shambler-bite-draft" }[kind]); Directory.CreateDirectory(frames);
                for (int frame = 0; frame < 80; frame++)
                {
                    float t = frame * .05f, enemyX = bite ? -.1f : -.9f, playerX = .6f;
                    float nearX = enemyX;
                    AnimationClip clip = enemy.Idle; float clipTime = .1f;
                    if (t < .6f) { enemyX = Mathf.Lerp(-2.7f, nearX, t / .6f); clip = enemy.Move; clipTime = t; }
                    else if (t < 1f) { clip = enemy.Attack; clipTime = enemy.Attack.length * strike * (t - .6f) / .4f; }
                    else if (t < 1.4f) { clip = enemy.Attack; clipTime = enemy.Attack.length * Mathf.Lerp(strike, 1f, (t - 1f) / .4f); }
                    else if (t < 2.3f) { enemyX = Mathf.Lerp(-2.7f, nearX, Mathf.Clamp01((t - 1.7f) / .6f)); clip = enemy.Move; clipTime = t; }
                    else if (t < 2.55f) { clip = enemy.Attack; clipTime = enemy.Attack.length * strike * (t - 2.3f) / .4f; }
                    else
                    {
                        // 取消时不采样挥出片段；退回待机后追击。这里只是可采用的临时动作方向。
                        playerX = .6f + 2.2f * Mathf.Clamp01((t - 2.55f) / .25f);
                        enemyX = nearX + 1.6f * Mathf.Clamp01((t - 2.7f) / 1.3f);
                        clip = t < 2.7f ? enemy.Idle : enemy.Move; clipTime = t;
                    }
                    enemy.Root.transform.SetPositionAndRotation(new Vector3(enemyX, 0, 0), Quaternion.Euler(0, 90, 0));
                    player.Root.transform.SetPositionAndRotation(new Vector3(playerX, 0, 0), Quaternion.Euler(0, -90, 0));
                    float lean = 0;
                    if (bite)
                    {
                        // 仅预览用身体前倾/头部探咬草案，不宣称已有正式撕咬片段。
                        if (t >= .6f && t < 1f) lean = Mathf.SmoothStep(0, .65f, (t - .6f) / .4f);
                        else if (t >= 1f && t < 1.4f) lean = Mathf.Lerp(1, 0, (t - 1f) / .4f);
                        else if (t >= 2.3f && t < 2.55f) lean = Mathf.SmoothStep(0, .65f, (t - 2.3f) / .4f);
                    }
                    Pose(enemy, clip, clipTime, lean);
                    Pose(player, t >= 2.55f && t < 2.8f ? player.Move : player.Idle, t);
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 900, 580), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(frames, "frame-" + frame.ToString("000") + ".png"), pixels.EncodeToPNG());
                }
                enemy.Root.SetActive(false);
            }
            File.WriteAllLines(Path.Combine(folder, "inspection.txt"), report);
            return folder + "\n" + string.Join("\n", report);
        }
        finally
        {
            RenderTexture.active = oldTarget;
            if (camera != null) camera.targetTexture = null;
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var item in owned) if (item != null) Object.DestroyImmediate(item);
        }
    }

    /// <summary>按采样后的实际骨矩阵蒙皮，避开编辑态BakeMesh缓存造成身体与持刀骨骼错位。</summary>
    private static void UpdateSkin(SkinnedMeshRenderer skin, Mesh output)
    {
        var source = skin.sharedMesh; var vertices = source.vertices; var normals = source.normals;
        var weights = source.boneWeights; var bind = source.bindposes; var bones = skin.bones;
        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) matrices[i] = skin.transform.worldToLocalMatrix * bones[i].localToWorldMatrix * bind[i];
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i]; var v = vertices[i]; var n = normals[i];
            vertices[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            normals[i] = (matrices[w.boneIndex0].MultiplyVector(n) * w.weight0 + matrices[w.boneIndex1].MultiplyVector(n) * w.weight1 +
                matrices[w.boneIndex2].MultiplyVector(n) * w.weight2 + matrices[w.boneIndex3].MultiplyVector(n) * w.weight3).normalized;
        }
        output.vertices = vertices; output.normals = normals; output.RecalculateBounds();
    }
}
