using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>将CC0僵尸原生Generic动画接入独立空手预制体，不修改旧持械敌人资源。</summary>
public static class ArcadeZombieAuthoring
{
    private const string Art = "Assets/Game/Art/Enemies/Zombies/";
    private const string Prefabs = "Assets/Game/Prefabs/Characters/";
    private static readonly string[] BoneNames = { "Body", "Torso", "Head", "UpperArm.L", "LowerArm.L", "UpperArm.R", "LowerArm.R", "UpperLeg.L", "LowerLeg.L", "UpperLeg.R", "LowerLeg.R" };
    private static readonly string[] Ends = { "Hips", "Neck", null, "LowerArm.L", "Middle1.L", "LowerArm.R", "Middle1.R", "LowerLeg.L", "Foot.L", "LowerLeg.R", "Foot.R" };
    private static readonly int[] Parents = { -1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9 };

    /// <summary>只允许在本工程编辑态执行；可重复生成并保留现有资源GUID。</summary>
    public static string Run()
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets") throw new InvalidOperationException("项目或运行状态不匹配");
        foreach (string source in new[] { "ZombieBasic", "ZombieChubby", "ZombieRibcage" })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Art + "Source/" + source + ".fbx");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                string suffix = clip.name.Split('|').Last();
                clip.loopTime = suffix == "Idle" || suffix == "Walk" || suffix == "Run_Arms";
                clip.lockRootPositionXZ = true; clip.lockRootHeightY = true; clip.lockRootRotation = true;
                clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }
        var result = new System.Text.StringBuilder();
        result.AppendLine(Create("Shambler", "ZombieBasic", new Vector3(1, 1, 1), new Color(.83f, .94f, .81f), "Walk"));
        result.AppendLine(Create("Gaunt", "ZombieBasic", new Vector3(.78f, 1.06f, .84f), new Color(.72f, .83f, .89f), "Run_Arms"));
        result.AppendLine(Create("Bloated", "ZombieChubby", new Vector3(1, 1, 1), new Color(.94f, .83f, .72f), "Walk"));
        AssetDatabase.SaveAssets(); return result.ToString();
    }

    /// <summary>沿用敌人HUD和燃烧节点，替换整套可见骨架、动画和休眠物理。</summary>
    private static string Create(string kind, string source, Vector3 proportions, Color tint, string locomotion)
    {
        string name = "ArcadeZombie" + kind, path = Prefabs + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(Prefabs + "ArcadeEnemyBasic.prefab");
        try
        {
            root.name = name;
            Object.DestroyImmediate(root.GetComponent<CharacterWeaponPresenter>());
            var flame = root.GetComponentInChildren<EnemyFlameFeedback>(true);
            flame.transform.SetParent(root.transform, true);
            var visual = root.transform.Find("ArcadeVisual");
            foreach (Transform child in visual.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity; visual.localScale = Vector3.one;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Source/" + source + ".fbx"), root.scene);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(visual, false); model.transform.localScale = proportions;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float targetHeight = kind == "Bloated" ? 1.85f : kind == "Gaunt" ? 1.92f : 1.8f;
            visual.localScale = Vector3.one * targetHeight / bounds.size.y;
            bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            visual.position -= Vector3.up * (bounds.min.y - root.transform.position.y);
            var matPath = Art + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, matPath); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Source/ZombieAtlas.png")); material.SetColor("_BaseColor", tint); material.SetFloat("_Smoothness", .15f);
            foreach (var renderer in renderers) { renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray(); renderer.gameObject.SetActive(true); }
            EditorUtility.SetDirty(material);
            var controllerPath = Art + name + ".controller";
            if (!File.Exists(controllerPath)) AssetDatabase.CopyAsset("Assets/Game/Art/Arcade/Enemies/Humanoid/ArcadeEnemyBasic.controller", controllerPath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Art + "Source/" + source + ".fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            foreach (var state in controller.layers[0].stateMachine.states.Select(s => s.state))
            {
                string motion = state.name == "Run" ? locomotion : state.name == "Attack" ? "Idle_Attack" : state.name == "Hit" ? "HitReact" : state.name;
                state.motion = clips.Single(c => c.name.EndsWith("|" + motion)); state.speed = 1f;
                EditorUtility.SetDirty(state);
            }
            var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            flame.transform.SetParent(model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Abdomen"), true);
            ConfigurePhysics(root, animator, model.transform, targetHeight);
            var view = new SerializedObject(root.GetComponent<EnemyView>());
            view.FindProperty("animator").objectReferenceValue = animator;
            view.FindProperty("defaultWeaponId").stringValue = "weapon.zombie-claw";
            view.FindProperty("projectileFirePoint").objectReferenceValue = null;
            // 同一僵尸的眼睑、舌头与身体是组成部件，不能当互斥外观关闭。
            view.FindProperty("presentationVariants").arraySize = 0;
            foreach (string field in new[] { "livingRenderers" })
            {
                var array = view.FindProperty(field); array.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }
            view.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return name + ": " + renderers.Length + " mesh(es), " + clips.Length + " native clips, height=" + targetHeight;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>独立动画脚骨只在物理阶段挂到小腿，保证活体动画与死亡落地都保留正确层级。</summary>
    private static void ConfigurePhysics(GameObject root, Animator animator, Transform model, float height)
    {
        var all = model.GetComponentsInChildren<Transform>(true);
        Func<string, Transform> bone = name => all.Single(t => t.name == name);
        var bodies = new Rigidbody[BoneNames.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var transform = bone(BoneNames[i]); transform.gameObject.layer = LayerMask.NameToLayer(EnemyPhysicalReaction.BodyLayerName);
            var body = transform.gameObject.AddComponent<Rigidbody>(); bodies[i] = body;
            body.isKinematic = true; body.detectCollisions = false; body.mass = i == 0 ? 8 : i == 1 ? 10 : i == 2 ? 3 : 2;
            body.drag = .25f; body.angularDrag = 1f; body.solverIterations = 4; body.solverVelocityIterations = 1;
            var end = Ends[i] == null ? Vector3.up * height * .13f : bone(Ends[i]).position - transform.position;
            if (i == 0) end = Vector3.up * height * .18f;
            var localEnd = transform.InverseTransformVector(end);
            int axis = Mathf.Abs(localEnd.x) > Mathf.Abs(localEnd.y) ? 0 : 1;
            if (Mathf.Abs(localEnd.z) > Mathf.Abs(localEnd[axis])) axis = 2;
            var capsule = transform.gameObject.AddComponent<CapsuleCollider>(); capsule.direction = axis; capsule.center = localEnd * .5f;
            capsule.radius = height * (i < 2 ? .10f : i == 2 ? .085f : .04f) / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z)));
            capsule.height = Mathf.Max(capsule.radius * 2, Mathf.Abs(localEnd[axis])); capsule.enabled = false;
        }
        for (int i = 1; i < bodies.Length; i++)
        {
            var joint = bodies[i].gameObject.AddComponent<CharacterJoint>(); joint.connectedBody = bodies[Parents[i]];
            joint.enableProjection = true; joint.projectionDistance = .08f; joint.projectionAngle = 15f;
            joint.lowTwistLimit = new SoftJointLimit { limit = -25 }; joint.highTwistLimit = new SoftJointLimit { limit = 25 };
            joint.swing1Limit = new SoftJointLimit { limit = i < 3 ? 25 : 45 }; joint.swing2Limit = new SoftJointLimit { limit = i < 3 ? 20 : 35 };
        }
        var serialized = new SerializedObject(root.GetComponent<EnemyPhysicalReaction>());
        serialized.FindProperty("animator").objectReferenceValue = animator; serialized.FindProperty("chest").objectReferenceValue = bone("Torso"); serialized.FindProperty("hips").objectReferenceValue = bone("Body");
        foreach (string field in new[] { "bodies", "restPositions", "restRotations" }) serialized.FindProperty(field).arraySize = bodies.Length;
        for (int i = 0; i < bodies.Length; i++)
        {
            serialized.FindProperty("bodies").GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            serialized.FindProperty("restPositions").GetArrayElementAtIndex(i).vector3Value = bodies[i].transform.localPosition;
            serialized.FindProperty("restRotations").GetArrayElementAtIndex(i).quaternionValue = bodies[i].transform.localRotation;
        }
        serialized.FindProperty("detachedEndBones").arraySize = 2; serialized.FindProperty("endBonePhysicsParents").arraySize = 2;
        for (int i = 0; i < 2; i++)
        {
            string side = i == 0 ? ".L" : ".R";
            serialized.FindProperty("detachedEndBones").GetArrayElementAtIndex(i).objectReferenceValue = bone("Foot" + side);
            serialized.FindProperty("endBonePhysicsParents").GetArrayElementAtIndex(i).objectReferenceValue = bone("LowerLeg" + side);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
