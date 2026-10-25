using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>只制作已采用的码头右路撤板及三种海中攀爬；依赖同编译单元的采用版预览采样。</summary>
public static class DockSeaEntryBake
{
    private const string DockPath = "Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab";
    private const string FoamPath = "Assets/Game/Art/Arcade/Docks/DockSeaEntryFoam.mat";
    private const string Art = "Assets/Game/Art/Enemies/Zombies/Civilian/";
    private const string Prefabs = "Assets/Game/Prefabs/Characters/";
    private const float ClimbSeconds = 5.75f, Duration = 6.75f, BlendSeconds = .25f;
    private const int FrameRate = 40, Samples = 270;
    private static readonly string[] Kinds = { "Shambler", "Gaunt", "Bloated" };

    /// <summary>暂存全部候选后才写正式资源；旧路径覆盖保留身份，不全局保存或改控制器。</summary>
    public static string Run()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅允许本工程空闲编辑态制作已采用码头攀爬");
        var dock = ValidateDependencies();
        var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var temporary = new List<Object>();
        var lines = new List<string>();
        try
        {
            SceneManager.SetActiveScene(scene);
            var meshWrites = PrepareDockMeshes(dock, temporary);
            var foam = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            foam.name = "DockSeaEntryFoam"; foam.color = new Color(.56f, .84f, .86f, 1); temporary.Add(foam);
            var clips = new Dictionary<string, AnimationClip>();
            foreach (string kind in Kinds)
            {
                var actor = DockSeaEntryPreview.Enemy(-3f, 0, foam, kind);
                actor.Root.SetActive(true);
                actor.Animator.applyRootMotion = false;
                var clip = BakeClip(actor, kind);
                temporary.Add(clip); clips.Add(kind, clip);
                // 波纹由预览工厂放入当前临时场景，在统一finally关闭场景时一起销毁。
                Object.DestroyImmediate(actor.Root);
            }

            int removed = 0, changedMeshes = 0;
            foreach (var write in meshWrites.Values)
            {
                removed += write.Removed;
                if (write.Removed == 0) continue;
                EditorUtility.CopySerialized(write.Candidate, write.Source);
                EditorUtility.SetDirty(write.Source); AssetDatabase.SaveAssetIfDirty(write.Source);
                changedMeshes++;
                lines.Add("mesh=" + AssetDatabase.GetAssetPath(write.Source) + "; removedTriangles=" + write.Removed);
            }
            lines.Insert(0, "passageTrianglesRemoved=" + removed + "; changedMeshAssets=" + changedMeshes + "; idempotentZeroAllowed=true");
            var rippleMaterial = SaveRippleMaterial(foam);
            foreach (string kind in Kinds)
            {
                var saved = SaveClip(clips[kind], Art + kind + "DockSeaEntry.anim");
                bool bound = BindResources(kind, saved, rippleMaterial);
                lines.Add(kind + " -> " + AssetDatabase.GetAssetPath(saved) + "; length=" + saved.length +
                    "; frameRate=" + saved.frameRate + "; bindingChanged=" + bound);
            }
            return string.Join("\n", lines);
        }
        finally
        {
            if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            foreach (var item in temporary)
                if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        }
    }

    /// <summary>与采用预览完全相同的三角形条件；源网格只读，水面返回原样克隆供重制入口复用。</summary>
    public static Mesh FilterPassage(Mesh source, Matrix4x4 meshToDock, bool water, out int removed)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        removed = 0;
        var candidate = Object.Instantiate(source); candidate.name = source.name;
        try
        {
            if (water) return candidate;
            if (!source.isReadable) throw new InvalidOperationException("撤板网格不可读：" + source.name);
            var vertices = source.vertices;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles)
                    throw new InvalidOperationException("撤板网格含非三角形子网格：" + source.name);
                var triangles = source.GetTriangles(sub); var keep = new List<int>(triangles.Length);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = meshToDock.MultiplyPoint3x4(vertices[triangles[i]]);
                    var b = meshToDock.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                    var c = meshToDock.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                    bool right = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) >= 11.999f &&
                        Mathf.Max(a.x, Mathf.Max(b.x, c.x)) > 12.001f;
                    bool deck = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < .07f;
                    if (right && deck) removed++;
                    else { keep.Add(triangles[i]); keep.Add(triangles[i + 1]); keep.Add(triangles[i + 2]); }
                }
                candidate.SetTriangles(keep, sub, false);
            }
            candidate.RecalculateBounds();
            return candidate;
        }
        catch { Object.DestroyImmediate(candidate); throw; }
    }

    /// <summary>预检正式网格、原Walk、正式GroundWalk及唯一序列化绑定字段，依赖不全时不写任何资源。</summary>
    private static GameObject ValidateDependencies()
    {
        var dock = AssetDatabase.LoadAssetAtPath<GameObject>(DockPath);
        if (dock == null || dock.GetComponentsInChildren<MeshFilter>(true).Length == 0)
            throw new InvalidOperationException("缺少正式码头网格：" + DockPath);
        if (Shader.Find("Universal Render Pipeline/Unlit") == null || !AssetDatabase.IsValidFolder(Art.TrimEnd('/')))
            throw new InvalidOperationException("缺少预览泡沫Shader或正式便服丧尸动画目录");
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + "Walk.anim");
        if (walk == null || walk.length <= 0) throw new InvalidOperationException("缺少采用预览使用的原Walk.anim");
        var existingFoam = AssetDatabase.LoadMainAssetAtPath(FoamPath);
        if (!AssetDatabase.IsValidFolder("Assets/Game/Art/Arcade/Docks") || existingFoam != null && !(existingFoam is Material))
            throw new InvalidOperationException("正式码头泡沫材质目录不存在或目标被其他资源占用：" + FoamPath);
        foreach (string kind in Kinds)
        {
            string prefabPath = Prefabs + "ArcadeZombie" + kind + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var view = prefab == null ? null : prefab.GetComponent<EnemyView>();
            if (view == null || view.Animator == null || view.Animator.applyRootMotion)
                throw new InvalidOperationException("正式丧尸缺少EnemyView/Animator或开启了Root Motion：" + prefabPath);
            if (new SerializedObject(view).FindProperty("dockSeaEntryClip") == null)
                throw new InvalidOperationException("EnemyView缺少dockSeaEntryClip序列化字段：" + prefabPath);
            if (new SerializedObject(view).FindProperty("dockSeaEntryRippleMaterial") == null)
                throw new InvalidOperationException("EnemyView缺少dockSeaEntryRippleMaterial序列化字段：" + prefabPath);
            var ground = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + kind + "GroundWalk.anim");
            if (ground == null || ground.length <= 0) throw new InvalidOperationException("缺少已接入GroundWalk：" + kind);
            var existing = AssetDatabase.LoadMainAssetAtPath(Art + kind + "DockSeaEntry.anim");
            if (existing != null && !(existing is AnimationClip))
                throw new InvalidOperationException("攀爬动画目标已被其他资源占用：" + kind);
        }
        return dock;
    }

    /// <summary>同一持久网格只写一次；若不同实例要求不一致则在写入前报错，避免破坏共享引用。</summary>
    private static Dictionary<Mesh, MeshWrite> PrepareDockMeshes(GameObject dock, List<Object> temporary)
    {
        var writes = new Dictionary<Mesh, MeshWrite>();
        foreach (var filter in dock.GetComponentsInChildren<MeshFilter>(true))
        {
            var source = filter.sharedMesh; var renderer = filter.GetComponent<MeshRenderer>();
            if (source == null || renderer == null || !EditorUtility.IsPersistent(source) ||
                !AssetDatabase.GetAssetPath(source).EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("码头MeshFilter缺少独立持久网格或MeshRenderer：" + filter.name);
            bool water = renderer.sharedMaterials.Any(m => m != null && m.shader != null && m.shader.name.Contains("DockWater"));
            var candidate = FilterPassage(source, dock.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix, water, out int removed);
            temporary.Add(candidate);
            if (writes.TryGetValue(source, out var previous))
            {
                if (previous.Removed != removed || Enumerable.Range(0, source.subMeshCount)
                    .Any(sub => !previous.Candidate.GetTriangles(sub).SequenceEqual(candidate.GetTriangles(sub))))
                    throw new InvalidOperationException("共享码头网格在不同位置需要不同撤板结果：" + source.name);
                continue;
            }
            writes.Add(source, new MeshWrite { Source = source, Candidate = candidate, Removed = removed });
        }
        return writes;
    }

    /// <summary>全骨骼局部旋转加Body局部位移；排除Actor世界变换、Animator根、缩放与运行组件。</summary>
    private static AnimationClip BakeClip(DockSeaEntryPreview.Actor actor, string kind)
    {
        if (actor.Root == null || actor.Animator == null || actor.Walk == null ||
            AssetDatabase.GetAssetPath(actor.Walk) != Art + "Walk.anim")
            throw new InvalidOperationException("预览工厂未使用采用版原Walk：" + kind);
        var animatorRoot = actor.Animator.transform;
        var body = animatorRoot.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Body");
        var bones = new HashSet<Transform>(actor.Bones.Values);
        foreach (var renderer in actor.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (var bone in renderer.bones) if (bone != null) bones.Add(bone);
            if (renderer.rootBone != null) bones.Add(renderer.rootBone);
        }
        foreach (var bone in bones.ToArray())
            for (var parent = bone.parent; parent != null && parent != animatorRoot; parent = parent.parent)
                bones.Add(parent);
        var ordered = bones.Where(t => t != animatorRoot && t.IsChildOf(animatorRoot))
            .OrderBy(t => AnimationUtility.CalculateTransformPath(t, animatorRoot), StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0 || !ordered.Contains(body)) throw new InvalidOperationException("正式骨架缺少Body骨盆：" + kind);
        var paths = ordered.Select(t => AnimationUtility.CalculateTransformPath(t, animatorRoot)).ToArray();
        if (paths.Any(string.IsNullOrEmpty) || paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new InvalidOperationException("骨骼绑定含根路径或同名冲突：" + kind);
        var ground = AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + kind + "GroundWalk.anim");
        var rotation = ordered.Select(_ => Enumerable.Range(0, 4).Select(axis => new AnimationCurve()).ToArray()).ToArray();
        var position = Enumerable.Range(0, 3).Select(_ => new AnimationCurve()).ToArray();
        var oldRotation = new Quaternion[ordered.Length];
        var clip = new AnimationClip { name = "Civilian|DockSeaEntry" + kind, frameRate = FrameRate, legacy = false, wrapMode = WrapMode.Once };
        try
        {
            for (int sample = 0; sample <= Samples; sample++)
            {
                float time = Duration * sample / Samples;
                DockSeaEntryPreview.Pose(actor, time);
                if (time >= ClimbSeconds)
                {
                    // 5.75秒之前完全沿用采用姿态；接步只在局部骨骼上由旧Walk平滑过渡到正式GroundWalk。
                    for (int i = 0; i < ordered.Length; i++) oldRotation[i] = ordered[i].localRotation;
                    var oldPosition = body.localPosition;
                    ground.SampleAnimation(actor.Animator.gameObject, (time - ClimbSeconds) % ground.length);
                    float blend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - ClimbSeconds) / BlendSeconds));
                    for (int i = 0; i < ordered.Length; i++) ordered[i].localRotation = Quaternion.Slerp(oldRotation[i], ordered[i].localRotation, blend);
                    body.localPosition = Vector3.Lerp(oldPosition, body.localPosition, blend);
                }
                for (int i = 0; i < ordered.Length; i++)
                {
                    var q = ordered[i].localRotation;
                    for (int axis = 0; axis < 4; axis++)
                    {
                        if (!Finite(q[axis])) throw new InvalidOperationException("攀爬骨骼产生无效旋转：" + paths[i]);
                        rotation[i][axis].AddKey(time, q[axis]);
                    }
                }
                for (int axis = 0; axis < 3; axis++)
                {
                    float value = body.localPosition[axis];
                    if (!Finite(value)) throw new InvalidOperationException("攀爬Body产生无效局部位移：" + kind);
                    position[axis].AddKey(time, value);
                }
            }
            for (int i = 0; i < ordered.Length; i++) for (int axis = 0; axis < 4; axis++)
                SetLinearCurve(clip, paths[i], "m_LocalRotation." + "xyzw"[axis], rotation[i][axis]);
            for (int axis = 0; axis < 3; axis++)
                SetLinearCurve(clip, AnimationUtility.CalculateTransformPath(body, animatorRoot), "m_LocalPosition." + "xyz"[axis], position[axis]);
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0; settings.stopTime = Duration; settings.loopTime = false; settings.loopBlend = false;
            settings.loopBlendOrientation = false; settings.loopBlendPositionY = false; settings.loopBlendPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }
        catch { Object.DestroyImmediate(clip); throw; }
    }

    /// <summary>同路径更新已有片段保持meta/GUID；新片段由AssetDatabase创建对应meta。</summary>
    private static AnimationClip SaveClip(AnimationClip candidate, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null) { AssetDatabase.CreateAsset(candidate, path); existing = candidate; }
        else EditorUtility.CopySerialized(candidate, existing);
        EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    /// <summary>泡沫保持采用版URP/Unlit和浅青色；同路径更新保留材质身份，只定点保存该材质。</summary>
    private static Material SaveRippleMaterial(Material candidate)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(FoamPath);
        if (existing == null) { AssetDatabase.CreateAsset(candidate, FoamPath); existing = candidate; }
        else { existing.shader = candidate.shader; existing.color = candidate.color; }
        EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    /// <summary>只写指定EnemyView的攀爬动画及泡沫依赖；不改Prefab层级、骨长、缩放或控制器。</summary>
    private static bool BindResources(string kind, AnimationClip clip, Material rippleMaterial)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "ArcadeZombie" + kind + ".prefab");
        var view = prefab.GetComponent<EnemyView>(); var serialized = new SerializedObject(view);
        var property = serialized.FindProperty("dockSeaEntryClip");
        var rippleProperty = serialized.FindProperty("dockSeaEntryRippleMaterial");
        if (property.objectReferenceValue == clip && rippleProperty.objectReferenceValue == rippleMaterial) return false;
        property.objectReferenceValue = clip; rippleProperty.objectReferenceValue = rippleMaterial;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view); PrefabUtility.SavePrefabAsset(prefab);
        return true;
    }

    /// <summary>线性关键帧避免采样之间因切线过冲改变已采用的膝足姿态。</summary>
    private static void SetLinearCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }
        clip.SetCurve(path, typeof(Transform), property, curve);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>待提交的同身份网格候选，仅本次制作事务持有。</summary>
    private sealed class MeshWrite
    {
        public Mesh Source, Candidate;
        public int Removed;
    }
}
