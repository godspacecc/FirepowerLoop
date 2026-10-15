using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>使用正式模型在隔离预览场景采样单次劈砍；不保存场景或修改装备、掉落和战斗状态。</summary>
public static class SingleMeleeSlashPreview
{
    private const string Root = "Assets/Tests/Results~/SingleMeleeSlash";
    private static readonly string[] Weapons = { "weapon.knife", "weapon.axe", "weapon.pickaxe", "weapon.han.axe", "weapon.han.pick" };

    /// <summary>候选图按四段原钢刀片段逐行采样，选择完整单刀的运动方向；不修改目录。</summary>
    public static string Candidates()
    {
        Directory.CreateDirectory(Root);
        foreach (var id in new[] { "weapon.knife", "weapon.han.axe" })
        {
            var definitions = Enumerable.Range(1, 4).Select(i => AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(
                "Assets/Game/Art/Arcade/Weapons/Animations/SteelBladeSlash" + i + ".asset")).ToArray();
            Capture(id, definitions, Root + "/candidate-" + id + ".png", false);
        }
        return Path.GetFullPath(Root);
    }

    /// <summary>实际目录的静止/移动攻击与切换回短剑采样，另保留短剑和双手剑对照。</summary>
    public static string Accepted()
    {
        Directory.CreateDirectory(Root);
        foreach (var id in Weapons.Concat(new[] { "weapon.short-sword", "weapon.greatsword" }))
            Capture(id, null, Root + "/accepted-" + id + ".png", true);
        var overview = new Texture2D(1920, 320 * Weapons.Length, TextureFormat.RGB24, false);
        try
        {
            for (int row = 0; row < Weapons.Length; row++)
            {
                var sampled = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    sampled.LoadImage(File.ReadAllBytes(Root + "/accepted-" + Weapons[row] + ".png"));
                    overview.SetPixels(0, (Weapons.Length - row - 1) * 320, 1920, 320,
                        sampled.GetPixels(0, 320, 1920, 320));
                }
                finally { Object.DestroyImmediate(sampled); }
            }
            overview.Apply();
            File.WriteAllBytes(Root + "/overview.png", overview.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(overview); }
        return Path.GetFullPath(Root);
    }

    private static void Capture(string id, CharacterAnimationDefinition[] candidates, string path, bool runtime)
    {
        const int width = 320, height = 320, columns = 6;
        int rows = runtime ? 2 : candidates.Length;
        var scene = EditorSceneManager.NewPreviewScene();
        var graph = PlayableGraph.Create("SingleMeleeSlashPreview");
        var sheet = new Texture2D(width * columns, height * rows, TextureFormat.RGB24, false);
        var target = new RenderTexture(width, height, 24);
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        Mesh[] baked = Array.Empty<Mesh>();
        Camera previewCamera = null;
        try
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), scene);
            foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            var view = player.GetComponent<PlayerView>();
            var presenter = player.GetComponent<CharacterWeaponPresenter>();
            var animator = presenter.BodyAnimator;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            presenter.SetManualPoseSampling(true);
            var entry = presenter.Catalog.Find(id);
            if (!presenter.SetWeapon(entry)) throw new InvalidOperationException(presenter.LastError);
            // 预览场景不经过编辑器渲染帧更新：显式烘焙采样后的蒙皮，避免画面仍显示缓存站姿。
            var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            baked = skins.Select(r =>
            {
                var mesh = new Mesh();
                var bakedObject = new GameObject("SampledSkin", typeof(MeshFilter), typeof(MeshRenderer));
                bakedObject.transform.SetParent(r.transform, false);
                bakedObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                bakedObject.GetComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                r.enabled = false;
                return mesh;
            }).ToArray();
            var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            previewCamera = camera;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.19f, .22f, .25f);
            camera.orthographic = true;
            camera.orthographicSize = 1.28f;
            camera.transform.position = new Vector3(3.8f, 2.1f, 4.4f);
            camera.transform.LookAt(new Vector3(0, .9f, 0));
            camera.targetTexture = target;
            var lightObject = new GameObject("PreviewLight", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.5f;
            light.transform.rotation = Quaternion.Euler(35, 160, 0);
            for (int row = 0; row < rows; row++)
            {
                animator.Rebind();
                animator.Update(0f);
                var definition = runtime ? entry.Animations.Attack : candidates[row];
                AnimationClipPlayable playable = default;
                if (!runtime)
                {
                    if (graph.GetOutputCount() > 0) graph.Destroy();
                    graph = PlayableGraph.Create("SingleSlashCandidate");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    playable = AnimationClipPlayable.Create(graph, definition.Clip);
                    playable.SetApplyFootIK(false);
                    AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                    graph.Play();
                }
                else
                {
                    view.SetMovementSpeed(row == 0 ? 0f : 2f);
                    animator.Update(.12f);
                    view.PlayWeaponSwingFeedback();
                }
                for (int column = 0; column < columns; column++)
                {
                    float progress = column / (float)(columns - 1);
                    if (runtime)
                    {
                        if (column > 0)
                            for (int frame = 0; frame < 6; frame++)
                            {
                                animator.Update(1f / 60f);
                                view.TickCombatFeedback(1f / 60f);
                            }
                        presenter.ApplyOffhandGrip(definition, progress);
                    }
                    else
                    {
                        playable.SetTime(definition.Clip.length * progress);
                        graph.Evaluate(0f);
                        presenter.ApplyOffhandGrip(definition, progress);
                    }
                    for (int skin = 0; skin < skins.Length; skin++) skins[skin].BakeMesh(baked[skin]);
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    sheet.SetPixels(column * width, (rows - row - 1) * height, width, height, image.GetPixels());
                }
            }
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
        }
        finally
        {
            if (previewCamera != null) previewCamera.targetTexture = null;
            RenderTexture.active = old;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(sheet);
            foreach (var mesh in baked) Object.DestroyImmediate(mesh);
            if (graph.IsValid()) graph.Destroy();
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
