using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>制作喷火器正式模型并接入共享目录、旧玩家绑定与现有投射物载体；不写玩法参数。</summary>
public static class ArcadeFlamethrowerAuthoring
{
    private const string GunPath = "Assets/Game/Prefabs/SceneObjects/ArcadeFlamethrower.prefab";
    private const string Art = "Assets/Game/Art/Arcade/Weapons/";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态制作喷火器");
        BindFlameProjectile();
        var gun = BuildGun();
        BindSharedCatalog(gun);
        BindLegacyPlayer(gun);
        AssetDatabase.SaveAssets();
        return "喷火器模型、共享动作目录、地面模型与火焰弹已保存";
    }

    /// <summary>枪管沿局部 Z 轴，枪口与配置的 0.72 米挂点保持一致；模型不携带碰撞。</summary>
    private static GameObject BuildGun()
    {
        var steel = MakeMaterial("FlamethrowerSteel", new Color(.12f, .15f, .16f));
        var brass = MakeMaterial("FlamethrowerBrass", new Color(.65f, .38f, .11f));
        var tank = MakeMaterial("FlamethrowerFuel", new Color(.42f, .12f, .06f));
        var gun = new GameObject("ArcadeFlamethrower");
        try
        {
            Part(gun, "Body", PrimitiveType.Cube, new Vector3(0, 0, .21f), new Vector3(.15f, .14f, .38f), steel);
            Part(gun, "Barrel", PrimitiveType.Cylinder, new Vector3(0, .015f, .49f), new Vector3(.085f, .18f, .085f), steel, new Vector3(90, 0, 0));
            Part(gun, "Nozzle", PrimitiveType.Cylinder, new Vector3(0, .015f, .685f), new Vector3(.14f, .035f, .14f), brass, new Vector3(90, 0, 0));
            Part(gun, "Grip", PrimitiveType.Cube, new Vector3(0, -.11f, .075f), new Vector3(.075f, .18f, .09f), steel);
            Part(gun, "FuelTank", PrimitiveType.Cylinder, new Vector3(0, -.15f, .3f), new Vector3(.19f, .14f, .19f), tank);
            Part(gun, "FuelBand", PrimitiveType.Cylinder, new Vector3(0, -.15f, .3f), new Vector3(.198f, .025f, .198f), brass);
            Part(gun, "ForeGrip", PrimitiveType.Cube, new Vector3(0, -.08f, .48f), new Vector3(.11f, .11f, .15f), steel);
            BuildMuzzleFlame(gun);
            return PrefabUtility.SaveAsPrefabAsset(gun, GunPath);
        }
        finally { Object.DestroyImmediate(gun); }
    }

    /// <summary>共享目录保留步枪动作，使用独立的喷火器枪口和副手握点，不借用物品身份。</summary>
    private static void BindSharedCatalog(GameObject gun)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(Art + "CharacterWeaponCatalog.asset");
        var entriesField = typeof(CharacterWeaponCatalog).GetField("weapons", Hidden);
        var entries = ((CharacterWeaponCatalog.Entry[])entriesField.GetValue(catalog)).ToList();
        var entry = JsonUtility.FromJson<CharacterWeaponCatalog.Entry>(JsonUtility.ToJson(catalog.Find("arcade.weapon.rapid")));
        Set(entry, "id", "arcade.weapon.flamethrower"); Set(entry, "worldItemId", 0);
        Set(entry, "displayName", "喷火器"); Set(entry, "visualSource", gun);
        Set(entry, "handLocalScale", Vector3.one);
        Set(entry, "projectileFirePointLocalPosition", new Vector3(0, .015f, .72f));
        Set(entry, "offhandGripLocalPosition", new Vector3(0, -.08f, .48f));
        Set(entry, "muzzleFlash", false);
        BindFlamethrowerGripDefinitions(entry);
        CalibrateCombatPoses(entry);
        entries.RemoveAll(e => e.Id == entry.Id); entries.Add(entry);
        entriesField.SetValue(catalog, entries.ToArray());
        if (!catalog.ValidateIdentities(out var error)) throw new InvalidOperationException(error);
        EditorUtility.SetDirty(catalog);
    }

    /// <summary>喷火器保留原片段，独立开启双手握持，避免改动其他枪械的动作资源。</summary>
    private static void BindFlamethrowerGripDefinitions(CharacterWeaponCatalog.Entry entry)
    {
        foreach(var slot in new[]{"idle","attack","moveForward","moveBackward","moveLeft","moveRight"})
        {
            var source=(CharacterAnimationDefinition)typeof(CharacterAnimationSet).GetField(slot,Hidden).GetValue(entry.Animations);
            string path=Art+"Flamethrower_"+slot+".asset";
            var definition=AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(path);
            if(definition==null){definition=ScriptableObject.CreateInstance<CharacterAnimationDefinition>();AssetDatabase.CreateAsset(definition,path);}
            EditorUtility.CopySerialized(source,definition);definition.name="Flamethrower_"+slot;
            Set(definition,"allowOffhandGrip",true);EditorUtility.SetDirty(definition);
            Set(entry.Animations,slot,definition);
        }
    }

    /// <summary>新模型使用正Z枪管，按实际手骨采样独立校准，不能照搬FBX步枪的轴向。</summary>
    private static void CalibrateCombatPoses(CharacterWeaponCatalog.Entry entry)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
        try
        {
            var animator = player.GetComponent<CharacterWeaponPresenter>().BodyAnimator;
            animator.Rebind();animator.Update(0);
            var a = entry.Animations;
            var definitions = new[]{a.Idle,a.Attack,a.MoveForward,a.MoveBackward,a.MoveLeft,a.MoveRight}.Concat(a.Extras).Where(d=>d!=null).Distinct();
            var poses = new List<CharacterWeaponCatalog.Entry.AnimationPoseOverride>();
            foreach(var definition in definitions)
            {
                definition.Clip.SampleAnimation(animator.gameObject,definition.Clip.length*.5f);
                var hand=animator.GetBoneTransform(entry.MainHand);
                var rotation=Quaternion.Inverse(hand.rotation)*Quaternion.LookRotation(player.transform.forward,player.transform.up);
                var position=-(rotation*new Vector3(0,-.08f,.075f));
                var pose=new CharacterWeaponCatalog.Entry.AnimationPoseOverride();
                Set(pose,"definition",definition);Set(pose,"overridePosition",true);Set(pose,"localPosition",position);
                Set(pose,"overrideRotation",true);Set(pose,"localEulerAngles",rotation.eulerAngles);
                poses.Add(pose);
                if(definition==a.Idle){Set(entry,"handLocalPosition",position);Set(entry,"handLocalEulerAngles",rotation.eulerAngles);}
            }
            Set(entry,"animationPoseOverrides",poses.ToArray());
        }
        finally{Object.DestroyImmediate(player);}
    }

    /// <summary>喷嘴只连接近端火焰；远端软火团跟随真实判定载体，保留第一人称显示和墙体遮挡。</summary>
    private static void BuildMuzzleFlame(GameObject gun)
    {
        var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+n+".bytes"))));
        if(!provider.TryGetArcadeCatalog(out var catalog,out var error))throw new InvalidOperationException(error);
        var weapon=catalog.Weapons.Single(w=>w.Id=="arcade.weapon.flamethrower");
        var flame=new GameObject("FlamethrowerMuzzle",typeof(ParticleSystem));
        flame.transform.SetParent(gun.transform,false);flame.transform.localPosition=new Vector3(0,.015f,.72f);
        var particles=flame.GetComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=particles.main;main.loop=false;main.playOnAwake=false;main.duration=.14f;
        main.startLifetime=new ParticleSystem.MinMaxCurve(.18f,.24f);main.startSpeed=new ParticleSystem.MinMaxCurve(4,6);
        main.startSize=new ParticleSystem.MinMaxCurve(.2f,.35f);main.startColor=new Color(1.2f,1,.8f,.95f);
        main.maxParticles=64;main.simulationSpace=ParticleSystemSimulationSpace.World;
        var emission=particles.emission;emission.enabled=true;emission.rateOverTime=160;
        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=weapon.SpreadDegrees*.5f;shape.radius=.012f;
        var size=particles.sizeOverLifetime;size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.12f,1,1));
        var colors=particles.colorOverLifetime;colors.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.25f,.02f),1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.6f),new GradientAlphaKey(0,1)});colors.color=gradient;
        var sheet=particles.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=16;sheet.numTilesY=5;
        sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1));
        var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Art+"FlamethrowerFire.mat");
        renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }

    /// <summary>兼容现有造型库与旧表现调用；正式对局仍由共享目录持有武器。</summary>
    private static void BindLegacyPlayer(GameObject gun)
    {
        const string path = "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var data = new SerializedObject(root.GetComponent<PlayerView>());
            var bindings = data.FindProperty("arcadeWeaponVisuals");
            var source = Enumerable.Range(0, bindings.arraySize).Select(bindings.GetArrayElementAtIndex)
                .Single(p => p.FindPropertyRelative("resourceName").stringValue == "ArcadeRifle");
            var sourceRoot = ((GameObject)source.FindPropertyRelative("visualRoot").objectReferenceValue).transform;
            var old = sourceRoot.parent.Find("ArcadeFlamethrowerWeapon");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(gun, sourceRoot.parent);
            model.name = "ArcadeFlamethrowerWeapon";
            model.transform.localPosition = sourceRoot.localPosition; model.transform.localRotation = sourceRoot.localRotation;
            model.SetActive(false);
            var aim = source.FindPropertyRelative("aimLocalRotation").quaternionValue;
            var binding = FindBinding(bindings, "ArcadeFlamethrower");
            binding.FindPropertyRelative("visualRoot").objectReferenceValue = model;
            binding.FindPropertyRelative("hasLeftHandGrip").boolValue = true;
            binding.FindPropertyRelative("leftHandGripLocalPosition").vector3Value = new Vector3(0, -.08f, .48f);
            binding.FindPropertyRelative("aimLocalRotation").quaternionValue = aim;
            data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>使用相互重叠的软火团代替三条拉伸火束；载体被墙移除时火团也随之清理。</summary>
    private static void BindFlameProjectile()
    {
        const string path = "Assets/Game/Prefabs/Combat/GrayboxArrow.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var old = root.transform.Find("ArcadeFlameBurst");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var flame = new GameObject("ArcadeFlameBurst", typeof(ParticleSystem));
            flame.transform.SetParent(root.transform, false);
            var scale = root.transform.localScale;
            flame.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            var particles = flame.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true; main.playOnAwake = true; main.duration = .45f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.1f, .3f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 1.5f);
            main.startColor = new Color(1.2f, 1f, .8f, .9f);
            main.maxParticles = 32; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = particles.emission; emission.enabled=true;emission.rateOverTime = 90;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 0; shape.radius = .025f;
            var size=particles.sizeOverLifetime;size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.05f),new Keyframe(.3f,1),new Keyframe(1,1)));
            var colors = particles.colorOverLifetime; colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, .3f, .03f), .45f), new GradientColorKey(new Color(.5f, .03f, 0), 1) },
                new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(.6f, .5f), new GradientAlphaKey(0, 1) });
            colors.color = gradient;
            var sheet = particles.textureSheetAnimation; sheet.enabled = true;
            sheet.numTilesX = 16; sheet.numTilesY = 5;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            const string materialPath = Art + "FlamethrowerFire.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null &&
                !AssetDatabase.CopyAsset("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/Fire.mat", materialPath))
                throw new InvalidOperationException("火焰材质创建失败");
            var renderer = flame.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/Fire.mat"), renderer.sharedMaterial);
            renderer.sharedMaterial.name = "FlamethrowerFire"; EditorUtility.SetDirty(renderer.sharedMaterial);
            // 居中的软粒子随寿命扩大并覆盖相邻判定线，不再出现三个叉状尾端。
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 1f; renderer.velocityScale = 0f;renderer.pivot=Vector3.zero;
            renderer.enabled=true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            flame.SetActive(false);
            var data = new SerializedObject(root.GetComponent<ProjectileView>());
            FindBinding(data.FindProperty("arcadeVisuals"), "ArcadeFlameBurst")
                .FindPropertyRelative("visualRoot").objectReferenceValue = flame;
            data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static SerializedProperty FindBinding(SerializedProperty bindings, string resource)
    {
        for (int i = 0; i < bindings.arraySize; i++)
            if (bindings.GetArrayElementAtIndex(i).FindPropertyRelative("resourceName").stringValue == resource)
                return bindings.GetArrayElementAtIndex(i);
        int index = bindings.arraySize; bindings.InsertArrayElementAtIndex(index);
        var binding = bindings.GetArrayElementAtIndex(index);
        binding.FindPropertyRelative("resourceName").stringValue = resource; return binding;
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);

    private static Material MakeMaterial(string name, Color color)
    {
        string path = Art + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .3f);
        EditorUtility.SetDirty(material); return material;
    }

    private static void Part(GameObject root, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Vector3 rotation = default)
    {
        var part = GameObject.CreatePrimitive(type); part.name = name; Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(root.transform, false); part.transform.localPosition = position;
        part.transform.localScale = scale; part.transform.localEulerAngles = rotation;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }
}
