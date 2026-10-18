using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>将已拥有的Wyder副本适配现役重型敌人；只重建视觉子树，根战斗组件与主角目录不写入。</summary>
public static class ArcadeInfectedBruteAuthoring
{
    private const string Folder = "Assets/Game/Art/Enemies/InfectedBrute";
    private const string Prefab = "Assets/Game/Prefabs/Characters/ArcadeEnemyBrute.prefab";

    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅在本项目编辑态制作重型副本");
        Directory.CreateDirectory(Folder + "/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        GameObject body = null;
        try
        {
            var visual = root.transform.Find("ArcadeVisual");
            var prior = visual.GetComponentInChildren<Animator>(true);
            if (prior.transform.name == "InfectedBruteBody") throw new InvalidOperationException("已制作；重做须先恢复旧Prefab，禁止重复转换挂点");
            var priorWeapon = visual.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Weapon");
            body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Source/BruteWyder.fbx"));
            body.name = "InfectedBruteBody";
            body.transform.localScale = Vector3.one * 1.2f;
            var animator = body.GetComponent<Animator>();
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("重型Avatar无效，保持旧Prefab");
            animator.runtimeAnimatorController = prior.runtimeAnimatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var sourceRenderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
            // 保留原始绑定矩阵和骨骼，按脚底对齐；不通过伸缩骨骼改变动作或碰撞。
            body.transform.position -= Vector3.up * sourceRenderer.bounds.min.y;
            var catalog = ConvertCatalog(root.GetComponent<CharacterWeaponPresenter>().Catalog, prior, animator, visual.lossyScale.x);
            var oldHand = prior.GetBoneTransform(HumanBodyBones.RightHand);
            var newHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var correction = Quaternion.Inverse(newHand.rotation) * oldHand.rotation;
            float ratio = oldHand.lossyScale.x / visual.lossyScale.x / newHand.lossyScale.x;
            var weapon = Object.Instantiate(priorWeapon.gameObject);
            weapon.transform.SetParent(newHand, false);
            weapon.transform.localPosition = correction * priorWeapon.transform.localPosition * ratio;
            weapon.transform.localRotation = correction * priorWeapon.transform.localRotation;
            weapon.transform.localScale = priorWeapon.transform.localScale * ratio;
            weapon.name = "Weapon";
            var alias = new GameObject("Hand_R"); alias.transform.SetParent(newHand, false);
            var variants = new SkinnedMeshRenderer[6];
            for (int i = 0; i < variants.Length; i++)
            {
                var part = Object.Instantiate(sourceRenderer.gameObject, sourceRenderer.transform.parent);
                part.name = "InfectedBrute_" + i;
                var renderer = part.GetComponent<SkinnedMeshRenderer>();
                renderer.bones = sourceRenderer.bones;
                renderer.rootBone = sourceRenderer.rootBone;
                renderer.sharedMaterial = Material(i);
                renderer.enabled = i == 0;
                variants[i] = renderer;
            }
            Object.DestroyImmediate(sourceRenderer.gameObject);
            foreach (var child in visual.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            body.transform.SetParent(visual, false);
            var view = new SerializedObject(root.GetComponent<EnemyView>());
            view.FindProperty("animator").objectReferenceValue = animator;
            SetArray(view.FindProperty("presentationVariants"), variants.Cast<Object>().ToArray());
            SetArray(view.FindProperty("livingRenderers"), new Object[] { variants[0], weapon.GetComponent<MeshRenderer>() });
            view.ApplyModifiedPropertiesWithoutUndo();
            var presenter = new SerializedObject(root.GetComponent<CharacterWeaponPresenter>());
            presenter.FindProperty("animator").objectReferenceValue = animator;
            presenter.FindProperty("catalog").objectReferenceValue = catalog;
            presenter.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            AssetDatabase.SaveAssets();
            return "重型Humanoid、六变体及独立武器姿态已绑定；根组件保持";
        }
        finally
        {
            if (body != null && body.transform.parent == null) Object.DestroyImmediate(body);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>原包调色板已按项目必要依赖复制，URP材质保留不同部位纹理，不制造整体单色替代。</summary>
    private static Material Material(int variant)
    {
        string path = Folder + "/Materials/" + (variant == 0 ? "ArcadeEnemyBoss" : "InfectedBrute" + variant) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        string[] palettes = { "Green", "Red", "Gold", "Blue", "Green", "Gold" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/BarbarianWyder-" + palettes[variant] + ".tga"));
        material.SetColor("_BaseColor", variant < 4 ? new Color(.88f, .94f, .85f) : new Color(.72f, .85f, .78f));
        material.SetFloat("_Smoothness", .12f); material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material); return material;
    }

    /// <summary>只复制本敌人的目录，转换两种骨架的局部手轴和尺度，避免改变主角共用武器姿态。</summary>
    private static CharacterWeaponCatalog ConvertCatalog(CharacterWeaponCatalog original, Animator oldAnimator, Animator animator, float visualScale)
    {
        string path = Folder + "/BruteWeaponCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(path);
        if (catalog == null) { catalog = Object.Instantiate(original); AssetDatabase.CreateAsset(catalog, path); }
        else EditorUtility.CopySerialized(original, catalog);
        var serialized = new SerializedObject(catalog); var entries = serialized.FindProperty("weapons");
        for (int i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i); var hand = original.Get(i).MainHand;
            var correction = Quaternion.Inverse(animator.GetBoneTransform(hand).rotation) * oldAnimator.GetBoneTransform(hand).rotation;
            float ratio = oldAnimator.GetBoneTransform(hand).lossyScale.x / visualScale / animator.GetBoneTransform(hand).lossyScale.x;
            Pose(entry, "handLocalPosition", "handLocalEulerAngles", "handLocalScale", correction, ratio);
            var poses = entry.FindPropertyRelative("animationPoseOverrides");
            for (int j = 0; j < poses.arraySize; j++) Pose(poses.GetArrayElementAtIndex(j), "localPosition", "localEulerAngles", "localScale", correction, ratio);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(catalog); return catalog;
    }

    private static void Pose(SerializedProperty entry, string position, string rotation, string scale, Quaternion correction, float ratio)
    {
        entry.FindPropertyRelative(position).vector3Value = correction * entry.FindPropertyRelative(position).vector3Value * ratio;
        entry.FindPropertyRelative(rotation).vector3Value = (correction * Quaternion.Euler(entry.FindPropertyRelative(rotation).vector3Value)).eulerAngles;
        entry.FindPropertyRelative(scale).vector3Value *= ratio;
    }

    private static void SetArray(SerializedProperty array, Object[] items)
    {
        array.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }
}
