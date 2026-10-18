using System;
using System.Linq;
using Game;
using UnityEditor;
using UnityEngine;

/// <summary>为正式人形Prefab预制11刚体，避免命中或死亡帧构建骨架。</summary>
public static class EnemyPhysicalReactionAuthoring
{
    private static readonly HumanBodyBones[] Bones = {
        HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg };
    private static readonly HumanBodyBones[] Ends = {
        HumanBodyBones.Spine, HumanBodyBones.Neck, HumanBodyBones.LastBone,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
    private static readonly int[] Parents = { -1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9 };

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在FirepowerLoop编辑态制作");
        EnsureLayer(EnemyPhysicalReaction.BodyLayerName); EnsureLayer(EnemyPhysicalReaction.FloorLayerName);
        EnemyPhysicalReaction.ConfigureCollisionLayers();
        foreach (string name in new[] { "ArcadeEnemyBasic", "ArcadeEnemyArcher", "ArcadeEnemyBrute", "ArcadeEnemyBossArcher" })
        {
            string path = "Assets/Game/Prefabs/Characters/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Configure(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        return "Authored 4 humanoid enemy rigs: 11 rigidbodies and 10 joints each.";
    }

    private static void Configure(GameObject root)
    {
        var animator = root.GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isHuman) throw new InvalidOperationException(root.name + "缺少人形骨架");
        var reaction = root.GetComponent<EnemyPhysicalReaction>();
        if (reaction == null) reaction = root.AddComponent<EnemyPhysicalReaction>();
        var transforms = Bones.Select(animator.GetBoneTransform).ToArray();
        if (transforms.Any(bone => bone == null)) throw new InvalidOperationException(root.name + "缺少必要骨骼");
        var bodies = new Rigidbody[Bones.Length];
        float height = Vector3.Distance(transforms[2].position, animator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
        for (int i = 0; i < Bones.Length; i++)
        {
            var bone = transforms[i]; bone.gameObject.layer = LayerMask.NameToLayer(EnemyPhysicalReaction.BodyLayerName);
            bodies[i] = bone.GetComponent<Rigidbody>();
            if (bodies[i] == null) bodies[i] = bone.gameObject.AddComponent<Rigidbody>();
            var body = bodies[i]; body.isKinematic = true; body.detectCollisions = false;
            body.mass = i == 0 ? 8 : i == 1 ? 10 : i == 2 ? 3 : 2;
            body.drag = .25f; body.angularDrag = 1f; body.interpolation = RigidbodyInterpolation.None;
            body.solverIterations = 4; body.solverVelocityIterations = 1;
            var capsule = bone.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            var end = Ends[i] == HumanBodyBones.LastBone ? null : animator.GetBoneTransform(Ends[i]);
            var localEnd = end == null ? Vector3.up * height * .13f / bone.lossyScale.y : bone.InverseTransformPoint(end.position);
            if (i == 0) localEnd = bone.InverseTransformVector(Vector3.up * height * .18f);
            int axis = Mathf.Abs(localEnd.x) > Mathf.Abs(localEnd.y) ? 0 : 1;
            if (Mathf.Abs(localEnd.z) > Mathf.Abs(localEnd[axis])) axis = 2;
            capsule.direction = axis; capsule.center = localEnd * .5f;
            float worldRadius = height * (i < 2 ? .1f : i == 2 ? .085f : .04f);
            capsule.radius = worldRadius / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(bone.lossyScale.x), Mathf.Abs(bone.lossyScale.y), Mathf.Abs(bone.lossyScale.z)));
            capsule.height = Mathf.Max(capsule.radius * 2, Mathf.Abs(localEnd[axis]));
            capsule.enabled = false;
        }
        for (int i = 1; i < bodies.Length; i++)
        {
            var joint = bodies[i].GetComponent<CharacterJoint>();
            if (joint == null) joint = bodies[i].gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = bodies[Parents[i]]; joint.enableCollision = false; joint.enableProjection = true;
            joint.projectionDistance = .08f; joint.projectionAngle = 15f;
            joint.lowTwistLimit = new SoftJointLimit { limit = -25 };
            joint.highTwistLimit = new SoftJointLimit { limit = 25 };
            joint.swing1Limit = new SoftJointLimit { limit = i < 3 ? 25 : 45 };
            joint.swing2Limit = new SoftJointLimit { limit = i < 3 ? 20 : 35 };
        }
        var serialized = new SerializedObject(reaction);
        serialized.FindProperty("animator").objectReferenceValue = animator;
        serialized.FindProperty("chest").objectReferenceValue = animator.GetBoneTransform(HumanBodyBones.Chest);
        foreach (string field in new[] { "bodies", "restPositions", "restRotations" }) serialized.FindProperty(field).arraySize = bodies.Length;
        for (int i = 0; i < bodies.Length; i++)
        {
            serialized.FindProperty("bodies").GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            serialized.FindProperty("restPositions").GetArrayElementAtIndex(i).vector3Value = bodies[i].transform.localPosition;
            serialized.FindProperty("restRotations").GetArrayElementAtIndex(i).quaternionValue = bodies[i].transform.localRotation;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureLayer(string name)
    {
        if (LayerMask.NameToLayer(name) >= 0) return;
        var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = manager.FindProperty("layers");
        for (int i = 8; i < 32; i++) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
        { layers.GetArrayElementAtIndex(i).stringValue = name; manager.ApplyModifiedPropertiesWithoutUndo(); return; }
        throw new InvalidOperationException("没有空闲物理层：" + name);
    }
}
