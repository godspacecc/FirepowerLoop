using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>将本工程士兵副本制作成轻型感染敌人；只改外观资源，不改原包、主角或战斗配置。</summary>
public static class ArcadeInfectedSoldierAuthoring
{
    private const string Folder = "Assets/Game/Art/Enemies/InfectedSoldier";
    private const string Model = Folder + "/Source/SoldierA.fbx";
    private static readonly string[] Parts = { "Wehrmacht_soilder_A", "Jacket", "Throusers", "Helmet" };

    public static string Run()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("只能在FirepowerLoop编辑态制作");
        Directory.CreateDirectory(Folder + "/Materials");
        AssetDatabase.Refresh();
        foreach (string name in new[] { "ArcadeEnemyBasic", "ArcadeEnemyArcher" }) Replace(name);
        AssetDatabase.SaveAssets();
        return "轻型近战/远程正式Prefab已绑定士兵Humanoid、四色完整身体和六种感染配色；战斗根组件未修改";
    }

    private static void Replace(string name)
    {
        string path = "Assets/Game/Prefabs/Characters/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        GameObject source = null;
        try
        {
            var visual = root.transform.Find("ArcadeVisual");
            var oldAnimator = visual.GetComponentInChildren<Animator>(true);
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/" +
                (name == "ArcadeEnemyArcher" ? "ArcadeEnemyBossArcher" : "ArcadeEnemyBrute") + ".prefab");
            var referenceAnimator = reference.GetComponentInChildren<Animator>(true);
            // 保留旧骨架副本直到武器挂点完成坐标基转换，避免触碰全角色共用目录。
            source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            source.name = "InfectedSoldierBody";
            source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = source.GetComponent<Animator>();
            animator.runtimeAnimatorController = oldAnimator.runtimeAnimatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var presenter = root.GetComponent<CharacterWeaponPresenter>();
            var originalCatalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var catalog = MakeCatalog(originalCatalog, referenceAnimator, animator, reference.transform.Find("ArcadeVisual").lossyScale.x);
            string weaponName = name == "ArcadeEnemyArcher" ? "ArcherBow" : "Weapon";
            var oldWeapon = reference.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == weaponName);
            var hand = name == "ArcadeEnemyArcher" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            var weapon = Object.Instantiate(oldWeapon.gameObject);
            weapon.name = weaponName;
            var oldHand = referenceAnimator.GetBoneTransform(hand);
            var newHand = animator.GetBoneTransform(hand);
            Quaternion correction = Quaternion.Inverse(newHand.rotation) * oldHand.rotation;
            float scale = oldHand.lossyScale.x / reference.transform.Find("ArcadeVisual").lossyScale.x / newHand.lossyScale.x;
            weapon.transform.SetParent(newHand, false);
            weapon.transform.localPosition = correction * oldWeapon.transform.localPosition * scale;
            weapon.transform.localRotation = correction * oldWeapon.transform.localRotation;
            weapon.transform.localScale = oldWeapon.transform.localScale * scale;
            var alias = new GameObject("Hand_R").transform;
            alias.SetParent(animator.GetBoneTransform(HumanBodyBones.RightHand), false);

            var renderers = Parts.Select(part => source.transform.Find(part).GetComponent<SkinnedMeshRenderer>()).ToArray();
            var mesh = CombineBody(source.transform, renderers);
            string meshPath = Folder + "/SoldierBody.asset";
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (savedMesh == null) { AssetDatabase.CreateAsset(mesh, meshPath); savedMesh = mesh; }
            else { EditorUtility.CopySerialized(mesh, savedMesh); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(savedMesh); }
            var bones = renderers.SelectMany(r => r.bones).Distinct().ToArray();
            var variants = new SkinnedMeshRenderer[6];
            for (int index = 0; index < variants.Length; index++)
            {
                var body = new GameObject("InfectedSoldier_" + index);
                body.transform.SetParent(source.transform, false);
                var renderer = body.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = savedMesh; renderer.bones = bones;
                renderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                renderer.localBounds = savedMesh.bounds;
                renderer.sharedMaterials = Materials(index, name == "ArcadeEnemyArcher");
                renderer.enabled = index == 0;
                variants[index] = renderer;
            }
            foreach (Transform child in source.transform.Cast<Transform>().ToArray())
                if (child.name != "German_soilder_Rig" && !child.name.StartsWith("InfectedSoldier_"))
                    Object.DestroyImmediate(child.gameObject);
            foreach (Transform child in visual.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            source.transform.SetParent(visual, false);
            // 原城市场景角色以.75缩放使用；士兵原始身高约1.8米，保持同等战斗辨识尺寸。
            source.transform.localScale = Vector3.one;
            var view = new SerializedObject(root.GetComponent<EnemyView>());
            view.FindProperty("animator").objectReferenceValue = animator;
            SetArray(view.FindProperty("presentationVariants"), variants.Cast<Object>().ToArray());
            SetArray(view.FindProperty("livingRenderers"), new Object[] { variants[0], weapon.GetComponent<MeshRenderer>() });
            view.ApplyModifiedPropertiesWithoutUndo();
            var binding = new SerializedObject(presenter);
            binding.FindProperty("animator").objectReferenceValue = animator;
            binding.FindProperty("catalog").objectReferenceValue = catalog;
            binding.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { if (source != null && source.transform.parent == null) Object.DestroyImmediate(source); PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>统一四块蒙皮的骨骼索引及绑定矩阵；保留四个材质区，删除原包枪械网格。</summary>
    private static Mesh CombineBody(Transform root, SkinnedMeshRenderer[] renderers)
    {
        var bones = renderers.SelectMany(r => r.bones).Distinct().ToArray();
        var vertices = new List<Vector3>(); var normals = new List<Vector3>();
        var uv = new List<Vector2>(); var weights = new List<BoneWeight>(); var triangles = new List<int[]>();
        foreach (var renderer in renderers)
        {
            var mesh = renderer.sharedMesh; int offset = vertices.Count;
            Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            vertices.AddRange(mesh.vertices.Select(matrix.MultiplyPoint3x4));
            normals.AddRange(mesh.normals.Select(normal => matrix.inverse.transpose.MultiplyVector(normal).normalized));
            uv.AddRange(mesh.uv);
            foreach (var original in mesh.boneWeights)
            {
                var weight = original;
                weight.boneIndex0 = Array.IndexOf(bones, renderer.bones[original.boneIndex0]);
                weight.boneIndex1 = Array.IndexOf(bones, renderer.bones[original.boneIndex1]);
                weight.boneIndex2 = Array.IndexOf(bones, renderer.bones[original.boneIndex2]);
                weight.boneIndex3 = Array.IndexOf(bones, renderer.bones[original.boneIndex3]);
                weights.Add(weight);
            }
            triangles.Add(mesh.triangles.Select(index => index + offset).ToArray());
        }
        var combined = new Mesh { name = "InfectedSoldierBody", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combined.SetVertices(vertices); combined.SetNormals(normals); combined.SetUVs(0, uv);
        combined.boneWeights = weights.ToArray();
        combined.bindposes = bones.Select(bone => bone.worldToLocalMatrix * root.localToWorldMatrix).ToArray();
        combined.subMeshCount = triangles.Count;
        for (int index = 0; index < triangles.Count; index++) combined.SetTriangles(triangles[index], index);
        combined.RecalculateBounds(); combined.RecalculateTangents();
        return combined;
    }

    private static Material[] Materials(int variant, bool ranged)
    {
        var uniforms = ranged
            ? new[] { new Color(.54f,.27f,.18f), new Color(.63f,.35f,.22f), new Color(.47f,.22f,.20f) }
            : new[] { new Color(.23f,.34f,.26f), new Color(.33f,.40f,.25f), new Color(.21f,.33f,.35f) };
        var colors = new[] { new Color(.49f,.64f,.42f), uniforms[variant % 3], new Color(.18f,.23f,.22f), variant < 3 ? new Color(.24f,.30f,.25f) : new Color(.43f,.36f,.22f) };
        var materials = new Material[4];
        for (int part = 0; part < 4; part++)
        {
            string materialName = part == 1 ? (ranged ? "Ranged" : "Melee") + (variant % 3) + "Uniform"
                : part == 0 ? "InfectedSkin" : part == 2 ? "DarkTrousers" : variant < 3 ? "GreenHelmet" : "OchreHelmet";
            string path = Folder + "/Materials/" + materialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.color = colors[part]; material.SetFloat("_Smoothness", .12f); material.enableInstancing = true;
            EditorUtility.SetDirty(material); materials[part] = material;
        }
        return materials;
    }

    /// <summary>骨骼局部轴不同，复制本敌人目录并转换默认及动作覆盖姿态，主角共用源不写入。</summary>
    private static CharacterWeaponCatalog MakeCatalog(CharacterWeaponCatalog original, Animator oldAnimator, Animator animator, float visualScale)
    {
        string path = Folder + "/SoldierWeaponCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(path);
        if (catalog == null) { catalog = Object.Instantiate(original); AssetDatabase.CreateAsset(catalog, path); }
        else EditorUtility.CopySerialized(original, catalog);
        var serialized = new SerializedObject(catalog); var entries = serialized.FindProperty("weapons");
        for (int index = 0; index < entries.arraySize; index++)
        {
            var entry = entries.GetArrayElementAtIndex(index); var hand = original.Get(index).MainHand;
            Quaternion correction = Quaternion.Inverse(animator.GetBoneTransform(hand).rotation) * oldAnimator.GetBoneTransform(hand).rotation;
            float scale = oldAnimator.GetBoneTransform(hand).lossyScale.x / visualScale / animator.GetBoneTransform(hand).lossyScale.x;
            ConvertPose(entry, "handLocalPosition", "handLocalEulerAngles", "handLocalScale", correction, scale);
            var poses = entry.FindPropertyRelative("animationPoseOverrides");
            for (int poseIndex = 0; poseIndex < poses.arraySize; poseIndex++)
                ConvertPose(poses.GetArrayElementAtIndex(poseIndex), "localPosition", "localEulerAngles", "localScale", correction, scale);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(catalog); return catalog;
    }

    private static void ConvertPose(SerializedProperty entry, string position, string rotation, string scaleProperty, Quaternion correction, float scale)
    {
        entry.FindPropertyRelative(position).vector3Value = correction * entry.FindPropertyRelative(position).vector3Value * scale;
        entry.FindPropertyRelative(rotation).vector3Value = (correction * Quaternion.Euler(entry.FindPropertyRelative(rotation).vector3Value)).eulerAngles;
        entry.FindPropertyRelative(scaleProperty).vector3Value *= scale;
    }

    private static void SetArray(SerializedProperty array, Object[] values)
    {
        array.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++) array.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }
}
