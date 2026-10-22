using Game;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Tests
{
/// <summary>验证街机武器正式资源、玩家挂点和投射物变体保持一致。</summary>
public sealed class ArcadeWeaponVisualTests
{
    private const string WeaponFolder = "Assets/Game/Art/Arcade/Weapons";

    [Test]
    public void FormalWeaponAssetsExistWithDedicatedMaterials()
    {
        foreach (var name in new[] { "ArcadePistol", "ArcadeRifle", "ArcadeScifiShotgun", "ArcadeScifiPlasmaRifle", "ArcadePiercingGun", "ArcadeGrenade", "ArcadePiercingRay", "ArcadePanzerschreck", "ArcadePanzerschreckRocket" })
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponFolder}/{name}.fbx"), Is.Not.Null, name);

        foreach (var name in new[] { "ArcadePistol", "ArcadeRifle", "ArcadeScifiShotgun", "ArcadeScifiPlasmaRiflePt1", "ArcadeScifiPlasmaRiflePt2", "ArcadePiercing", "ArcadeGrenade", "ArcadePanzerschreck", "ArcadePanzerschreckRocket" })
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>($"{WeaponFolder}/{name}.mat"), Is.Not.Null, name);
        Assert.That(AssetDatabase.LoadAssetAtPath<Material>($"{WeaponFolder}/ArcadeScifiShotgunScope.mat"), Is.Not.Null);
    }

    [Test]
    public void PlayerPrefabSwitchesTheApprovedWeaponModels()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            Assert.That(view, Is.Not.Null);
            AssertWeapon(view, instance.transform, "ArcadePistol", "ArcadeBasicWeapon");
            AssertWeapon(view, instance.transform, "ArcadeRifle", "ArcadeRapidWeapon");
            AssertWeapon(view, instance.transform, "ArcadeScifiShotgun", "ArcadeSpreadWeapon");
            AssertWeapon(view, instance.transform, "ArcadeScifiPlasmaRifle", "ArcadeRaygunWeapon");
            AssertWeapon(view, instance.transform, "ArcadePiercingGun", "ArcadePiercingWeapon");
            AssertWeapon(view, instance.transform, "ArcadePanzerschreck", "ArcadeRocketWeapon");
            AssertWeapon(view, instance.transform, "ArcadeGrenade", "ArcadeGrenadeWeapon");
            AssertWeapon(view, instance.transform, "ArcadeFlamethrower", "ArcadeFlamethrowerWeapon");
            AssertWeapon(view, instance.transform, string.Empty, null);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ProjectilePrefabSwitchesConfiguredWeaponVisuals()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<ProjectileView>();
            var defaultRenderer = instance.GetComponent<Renderer>();
            var ray = Find(instance.transform, "ArcadePiercingRay");
            var grenade = Find(instance.transform, "ArcadeGrenade");
            var rocket = Find(instance.transform, "ArcadePanzerschreckRocket");

            view.SetArcadePresentation("ArcadePiercingRay", false);
            Assert.That(defaultRenderer.enabled, Is.False);
            Assert.That(ray.gameObject.activeSelf, Is.True);
            Assert.That(grenade.gameObject.activeSelf, Is.False);
            Assert.That(rocket.gameObject.activeSelf, Is.False);

            view.SetArcadePresentation("ArcadePanzerschreckRocket", false);
            Assert.That(defaultRenderer.enabled, Is.False);
            Assert.That(ray.gameObject.activeSelf, Is.False);
            Assert.That(grenade.gameObject.activeSelf, Is.False);
            Assert.That(rocket.gameObject.activeSelf, Is.True);

            view.SetArcadePresentation("ArcadeGrenade", false);
            Assert.That(defaultRenderer.enabled, Is.False);
            Assert.That(ray.gameObject.activeSelf, Is.False);
            Assert.That(grenade.gameObject.activeSelf, Is.True);
            Assert.That(rocket.gameObject.activeSelf, Is.False);

            view.SetArcadePresentation("ArcadePiercingRay", true);
            Assert.That(defaultRenderer.enabled, Is.True, "敌方投射物不能继承玩家武器变体");
            Assert.That(ray.gameObject.activeSelf, Is.False);
            Assert.That(grenade.gameObject.activeSelf, Is.False);
            Assert.That(rocket.gameObject.activeSelf, Is.False);

            view.SetArcadePresentation("ArcadeFlameBurst", false);
            Assert.That(defaultRenderer.enabled, Is.False);
            Assert.That(Find(instance.transform, "ArcadeFlameBurst").gameObject.activeSelf, Is.True);
            view.SetArcadePresentation(null, false);
            Assert.That(Find(instance.transform, "ArcadeFlameBurst").gameObject.activeSelf, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ProjectileRuntimeResetClearsPresentationIdentity()
    {
        var projectile = new ArcadeProjectileRuntime
        {
            ProjectileVisualResourceName = "ArcadeGrenade",
            FromPlayerWeapon = true
        };
        projectile.Reset();
        Assert.That(projectile.ProjectileVisualResourceName, Is.Null);
        Assert.That(projectile.FromPlayerWeapon, Is.False);
    }

    [Test]
    public void PlayerProjectileBodyRecoil_UsesShortSymmetricKickInsteadOfSwordAttackScale()
    {
        Assert.That(PlayerView.CalculateProjectileBodyRecoilDegrees(0f), Is.Zero.Within(0.0001f));
        Assert.That(PlayerView.CalculateProjectileBodyRecoilDegrees(0.5f), Is.EqualTo(8f).Within(0.0001f));
        Assert.That(PlayerView.CalculateProjectileBodyRecoilDegrees(1f), Is.Zero.Within(0.0001f));
    }

    [Test]
    public void PlayerAnimatorKeepsStableRifleAimAndImportsWeaponPreviewClips()
    {
        const string folder = "Assets/Game/Art/Player/Animations/Mixamo/Imported";
        var clips = new[]
            {
                "RifleIdleAiming", "RifleFire", "RifleReload", "RifleWalkForward",
                "RifleWalkBackward", "RifleWalkLeft", "RifleWalkRight"
            }
            .ToDictionary(name => name, name => AssetDatabase.LoadAllAssetsAtPath($"{folder}/{name}.fbx")
                .OfType<AnimationClip>().Single(candidate => candidate.name == name));
        Assert.That(clips.Values.All(clip => clip.humanMotion), Is.True);
        foreach (var name in new[]
                 {
                     "RifleIdleAiming", "RifleWalkForward", "RifleWalkBackward", "RifleWalkLeft",
                     "RifleWalkRight"
                 })
            Assert.That(clips[name].isLooping, Is.True, name);
        Assert.That(clips["RifleFire"].isLooping, Is.False);
        Assert.That(clips["RifleReload"].isLooping, Is.False);
        var pistolClips = new[] { "PistolIdle", "PistolFire" }
            .ToDictionary(name => name, name => AssetDatabase.LoadAllAssetsAtPath($"{folder}/{name}.fbx")
                .OfType<AnimationClip>().Single(candidate => candidate.name == name));
        Assert.That(pistolClips.Values.All(clip => clip.humanMotion), Is.True);
        Assert.That(pistolClips["PistolIdle"].isLooping, Is.True);
        Assert.That(pistolClips["PistolFire"].isLooping, Is.False);
        var matchedPistolIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            $"{folder}/PistolIdleMatched.anim");
        Assert.That(matchedPistolIdle, Is.Not.Null);
        Assert.That(matchedPistolIdle.humanMotion, Is.True);
        Assert.That(matchedPistolIdle.isLooping, Is.True);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Game/Art/Player/Humanoid/PlayerHumanoid.controller");
        Assert.That(controller.parameters.Any(parameter =>
            parameter.name == "RangedWeaponActive" &&
            parameter.type == AnimatorControllerParameterType.Bool), Is.True);
        Assert.That(controller.parameters.Any(parameter =>
            parameter.name == "PistolWeaponActive" &&
            parameter.type == AnimatorControllerParameterType.Bool), Is.True);
        Assert.That(controller.parameters.Any(parameter =>
            parameter.name == "PistolFire" &&
            parameter.type == AnimatorControllerParameterType.Trigger), Is.True);
        foreach (var unstableParameter in new[] { "RangedFire", "RangedMoveX", "RangedMoveZ" })
            Assert.That(controller.parameters.Any(parameter => parameter.name == unstableParameter), Is.False,
                unstableParameter);

        var upperLayer = controller.layers.Single(candidate => candidate.name == "Ranged Upper Body");
        Assert.That(upperLayer.avatarMask, Is.Not.Null);
        Assert.That(upperLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root), Is.True,
            "持枪跑动需要覆盖前倾的跑动根部姿态，否则枪械会随躯干压向地面");
        Assert.That(upperLayer.stateMachine.states.Any(state =>
            state.state.name == "Rifle Aim" && state.state.motion == clips["RifleIdleAiming"]), Is.True);
        var pistolLayer = controller.layers.Single(candidate => candidate.name == "Pistol Upper Body");
        Assert.That(pistolLayer.avatarMask, Is.Not.Null);
        Assert.That(pistolLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root), Is.False,
            "手枪待机不得覆盖基础层的直立根部姿态");
        Assert.That(pistolLayer.stateMachine.states.Any(state =>
            state.state.name == "Pistol Aim" && state.state.motion == matchedPistolIdle), Is.True);
        Assert.That(pistolLayer.stateMachine.states.Any(state =>
            state.state.name == "Pistol Fire" && state.state.motion == pistolClips["PistolFire"]), Is.True);
        Assert.That(controller.layers.Any(candidate => candidate.name == "Ranged Lower Body"), Is.False,
            "尚未稳定序列化的四向层不能留在正式 Controller 中");
        Assert.That(controller.layers.All(layer => layer.stateMachine != null), Is.True);
    }

    [Test]
    public void OnlyLongGunsUseTheRifleAimPose()
    {
        foreach (var name in new[]
                 {
                     "ArcadeRifle", "ArcadePiercingGun", "ArcadeScifiShotgun", "ArcadeScifiPlasmaRifle"
                 })
            Assert.That(PlayerViewCtrl.UsesRifleAimPose(name), Is.True, name);
        foreach (var name in new[] { "ArcadePistol", "ArcadeGrenade", "ArcadePanzerschreck", null })
            Assert.That(PlayerViewCtrl.UsesRifleAimPose(name), Is.False, name);
        Assert.That(PlayerViewCtrl.AllowsOffhandGrip("ArcadePistol", "PistolIdle"), Is.True);
        Assert.That(PlayerViewCtrl.AllowsOffhandGrip("ArcadePistol", "PistolFire"), Is.True);
        Assert.That(PlayerViewCtrl.AllowsOffhandGrip("ArcadePistol", "RifleIdleAiming"), Is.False);
        Assert.That(PlayerViewCtrl.AllowsOffhandGrip("ArcadeRifle", "RifleWalkForward"), Is.True);
        Assert.That(PlayerViewCtrl.AllowsOffhandGrip("ArcadeRifle", "RifleReload"), Is.False);
        Assert.That(PlayerViewCtrl.AllowsOffhandGrip("ArcadeGrenade", "RifleIdleAiming"), Is.False);
    }

    [Test]
    public void PlayerRifleAimIsSuppressedDuringHurtFeedbackThenRestored()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            var animator = view.Animator;
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeRifle", true,
                new Vector3(0f, 0.04f, 0.5f));
            Assert.That(animator.GetBool("RangedWeaponActive"), Is.True);

            view.PlayHurtFeedback();
            Assert.That(animator.GetBool("RangedWeaponActive"), Is.False);

            view.TickCombatFeedback(1f);
            Assert.That(animator.GetBool("RangedWeaponActive"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PlayerPistolUpperBodyRunsAimFireAndRecoveryWithoutReplacingBaseRun()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            var animator = view.Animator;
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadePistol", true,
                new Vector3(0f, 0.04f, 0.43f));
            Assert.That(animator.GetBool("PistolWeaponActive"), Is.True);
            Assert.That(animator.GetBool("RangedWeaponActive"), Is.False);

            animator.SetFloat("MoveSpeed", 1f);
            animator.Update(0.2f);
            animator.Update(0.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), Is.True);
            Assert.That(animator.GetCurrentAnimatorStateInfo(2).IsName("Pistol Aim"), Is.True);

            view.PlayProjectileFireFeedback();
            animator.Update(0.2f);
            animator.Update(0.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(2).IsName("Pistol Fire"), Is.True);
            for (var index = 0; index < 6; index++) animator.Update(0.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(2).IsName("Pistol Aim"), Is.True);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), Is.True);

            view.PlayHurtFeedback();
            Assert.That(animator.GetBool("PistolWeaponActive"), Is.False);
            view.TickCombatFeedback(1f);
            Assert.That(animator.GetBool("PistolWeaponActive"), Is.True);

            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeRifle", true,
                new Vector3(0f, 0.04f, 0.5f));
            Assert.That(animator.GetBool("PistolWeaponActive"), Is.False);
            Assert.That(animator.GetBool("RangedWeaponActive"), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ShotgunFireReturnsToAimAndReleasesPoseWhenSwitchingWeapons()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeScifiShotgun", true, Vector3.zero);
            animator.Update(0.2f);
            animator.Update(0.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Rifle Aim"), Is.True);

            view.PlayProjectileFireFeedback();
            for (var frame = 0; frame < 3; frame++) animator.Update(0.02f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Shotgun Fire"), Is.True,
                "霰弹枪开火应进入专属上身状态");
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var visual = Find(instance.transform, "ArcadeSpreadWeapon");
            for (var frame = 0; frame < 20; frame++)
            {
                animator.Update(0.02f);
                var animatedHandPosition = rightHand.position;
                view.ApplyWeaponGripPose();
                Assert.That(Vector3.Distance(rightHand.position, animatedHandPosition), Is.LessThan(0.001f),
                    $"开火帧 {frame} 主手应由原动画决定");
                Assert.That(Vector3.Angle(instance.transform.forward, visual.forward), Is.LessThan(8f),
                    $"开火帧 {frame} 枪口不再朝前");
            }
            animator.Update(0.1f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Rifle Aim"), Is.True,
                "开火结束后应回到持枪待机");

            view.PlayProjectileFireFeedback();
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeRifle", true, Vector3.zero);
            animator.Update(0.1f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Shotgun Fire"), Is.False,
                "切枪不能把待触发的霰弹枪动作带给另一把枪");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ShotgunAimPreservesAnimatedMainHandPose()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeScifiShotgun", true, Vector3.zero);
            animator.Update(0.2f);
            animator.Update(0.2f);
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var animatedHandPosition = rightHand.position;
            view.ApplyWeaponGripPose();
            Assert.That(Vector3.Distance(rightHand.position, animatedHandPosition), Is.LessThan(0.001f),
                "霰弹枪主手应由原动画决定，不被通用枪口校准拉到固定位置");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void LongGunAimPreservesAnimatedMainHandPose()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        foreach (var weapon in new[] { "ArcadeRifle", "ArcadePiercingGun", "ArcadeScifiPlasmaRifle" })
        {
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, weapon, true, Vector3.zero);
                animator.Update(0.2f);
                animator.Update(0.2f);
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var animatedPosition = hand.position;
                view.ApplyWeaponGripPose();
                Assert.That(Vector3.Distance(hand.position, animatedPosition), Is.LessThan(0.001f),
                    $"{weapon} 主手应由持枪动画决定");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void LongGunGalleryFirePreviewKeepsForwardPoseAndDoesNotAccumulateRecoil()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var fireClip = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/Game/Art/Player/Animations/Mixamo/Imported/RifleFire.fbx")
            .OfType<AnimationClip>().Single(clip => clip.name == "RifleFire");
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var galleryType = typeof(AppearanceGallerySceneView);
        var sample = galleryType.GetMethod("SamplePlayerComposite", fields);
        Assert.That(sample, Is.Not.Null);
        foreach (var pair in new[]
                 {
                     (resource: "ArcadeRifle", visual: "ArcadeRapidWeapon"),
                     (resource: "ArcadePiercingGun", visual: "ArcadePiercingWeapon"),
                     (resource: "ArcadeScifiShotgun", visual: "ArcadeSpreadWeapon"),
                     (resource: "ArcadeScifiPlasmaRifle", visual: "ArcadeRaygunWeapon")
                 })
        {
            var instance = Object.Instantiate(prefab);
            var preview = new GameObject("LongGunGalleryFirePreviewTest");
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.SetManualWeaponPoseSampling(true);
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, pair.resource, true, Vector3.zero);
                var gallery = preview.AddComponent<AppearanceGallerySceneView>();
                galleryType.GetField("animator", fields)?.SetValue(gallery, animator);
                galleryType.GetField("playerView", fields)?.SetValue(gallery, view);
                galleryType.GetField("selectedClip", fields)?.SetValue(gallery, fireClip);
                galleryType.GetField("selectedPlayerWeaponResourceName", fields)?.SetValue(gallery, pair.resource);
                galleryType.GetField("playerCompositePreview", fields)?.SetValue(gallery, true);
                galleryType.GetField("compositePlaybackSeconds", fields)?.SetValue(gallery, 0.05f);
                var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                            animator.GetBoneTransform(HumanBodyBones.Chest);
                var weapon = Find(instance.transform, pair.visual);
                Quaternion firstRecoil = Quaternion.identity;
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    sample.Invoke(gallery, null);
                    var expectedState = pair.resource == "ArcadeScifiShotgun" ? "Shotgun Fire" : "Rifle Aim";
                    Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName(expectedState), Is.True,
                        pair.resource);
                    Assert.That(Vector3.Angle(instance.transform.forward, weapon.forward), Is.LessThan(10f),
                        pair.resource);
                    if (repeat == 0) firstRecoil = chest.localRotation;
                    else Assert.That(Quaternion.Angle(chest.localRotation, firstRecoil), Is.LessThan(0.5f),
                        $"{pair.resource} 暂停在同一帧时后坐不能累积");
                }
                galleryType.GetField("compositePlaybackSeconds", fields)?.SetValue(gallery, 0.25f);
                sample.Invoke(gallery, null);
                Assert.That(Vector3.Angle(instance.transform.forward, weapon.forward), Is.LessThan(8f),
                    $"{pair.resource} 开火末段枪口应朝前");
            }
            finally
            {
                Object.DestroyImmediate(preview);
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void PistolGripClosesRightIndexFingerAndReleasesForRifle()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var indexMuscle = System.Array.IndexOf(HumanTrait.MuscleName, "Right Index 1 Stretched");
            Assert.That(indexMuscle, Is.GreaterThanOrEqualTo(0));

            float ReadIndexStretch()
            {
                for (var frame = 0; frame < 5; frame++) animator.Update(0.2f);
                using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
                {
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);
                    return pose.muscles[indexMuscle];
                }
            }

            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadePistol", false, Vector3.zero);
            Assert.That(ReadIndexStretch(), Is.LessThan(-0.5f), "手枪应让主握手食指弯向握把");

            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeRifle", false, Vector3.zero);
            Assert.That(ReadIndexStretch(), Is.GreaterThan(0.3f), "换成步枪后不得残留手枪手指姿态");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PlayerPistolBarrelFacesForwardDuringFormalIdle()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadePistol", false, Vector3.zero);
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var pistolIndex = Enumerable.Range(0, view.ArcadeWeaponVisualCount)
                .Single(index => view.GetArcadeWeaponVisualName(index) == "ArcadePistol");
            var pistol = view.GetArcadeWeaponVisualRoot(pistolIndex).transform;

            animator.Update(0.2f);
            animator.Update(0.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(2).IsName("Pistol Aim"), Is.True);
            // 枪模型的 +Z 是枪管方向；整段待机都应随角色朝前，且枪身保持正立。
            for (var frame = 0; frame < 12; frame++)
            {
                animator.Update(0.1f);
                typeof(PlayerView).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)?.Invoke(view, null);
                Assert.That(Vector3.Angle(instance.transform.forward, pistol.forward), Is.LessThan(8f),
                    $"PistolIdle frame {frame}");
                Assert.That(Vector3.Dot(instance.transform.up, pistol.up), Is.GreaterThan(0.98f),
                    $"PistolIdle frame {frame}");
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PlayerPistolBarrelStaysForwardDuringFormalFire()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadePistol", false, Vector3.zero);
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var pistolIndex = Enumerable.Range(0, view.ArcadeWeaponVisualCount)
                .Single(index => view.GetArcadeWeaponVisualName(index) == "ArcadePistol");
            var pistol = view.GetArcadeWeaponVisualRoot(pistolIndex).transform;

            foreach (var time in new[] { 0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.95f })
            {
                animator.Play("Base Layer.Idle", 0, 0f);
                animator.Play("Pistol Upper Body.Pistol Fire", 2, time);
                animator.Update(0.001f);
                view.ApplyWeaponGripPose();
                Assert.That(Vector3.Angle(instance.transform.forward, pistol.forward), Is.LessThan(8f),
                    $"PistolFire time {time}");
                Assert.That(Vector3.Dot(instance.transform.up, pistol.up), Is.GreaterThan(0.98f),
                    $"PistolFire time {time}");
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PlayerPistolFireKeepsIdleUpperBodyWithSmallRecoil()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadePistol", false, Vector3.zero);
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            float[] Sample(string state, float time)
            {
                animator.Play("Base Layer.Idle", 0, 0f);
                animator.Play($"Pistol Upper Body.{state}", 2, time);
                animator.Update(0.001f);
                using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
                {
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);
                    return (float[])pose.muscles.Clone();
                }
            }

            var idle = Sample("Pistol Aim", 0.5f);
            foreach (var time in new[] { 0f, 0.25f, 0.5f, 0.95f })
            {
                var fire = Sample("Pistol Fire", time);
                for (var muscle = 0; muscle < fire.Length; muscle++)
                {
                    var name = HumanTrait.MuscleName[muscle];
                    if (!name.Contains("Chest") && !name.Contains("Shoulder") &&
                        !name.Contains("Arm") && !name.Contains("Forearm") &&
                        !name.Contains("Hand")) continue;
                    Assert.That(Mathf.Abs(fire[muscle] - idle[muscle]), Is.LessThan(0.25f),
                        $"PistolFire time {time}: {name}");
                }
            }
            var recoilMuscle = System.Array.IndexOf(HumanTrait.MuscleName, "Right Hand Down-Up");
            Assert.That(Mathf.Abs(Sample("Pistol Fire", 0.25f)[recoilMuscle] - idle[recoilMuscle]),
                Is.GreaterThan(0.05f), "开火仍需保留短促的手腕后坐");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PlayerWeaponRotationsReturnToRestDuringHurtThenAimAgain()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var lateUpdate = typeof(PlayerView).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.That(lateUpdate, Is.Not.Null);
        foreach (var weaponName in new[] { "ArcadePistol", "ArcadeRifle" })
        {
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var weaponIndex = Enumerable.Range(0, view.ArcadeWeaponVisualCount)
                    .Single(index => view.GetArcadeWeaponVisualName(index) == weaponName);
                var weapon = view.GetArcadeWeaponVisualRoot(weaponIndex).transform;
                var restRotation = weapon.localRotation;
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, weaponName, false, Vector3.zero);

                for (var frame = 0; frame < 4; frame++)
                {
                    animator.Update(0.2f);
                    lateUpdate.Invoke(view, null);
                }
                Assert.That(Vector3.Angle(instance.transform.forward, weapon.forward), Is.LessThan(8f), weaponName);

                view.PlayHurtFeedback();
                for (var frame = 0; frame < 4; frame++)
                {
                    animator.Update(0.2f);
                    lateUpdate.Invoke(view, null);
                }
                Assert.That(Quaternion.Angle(restRotation, weapon.localRotation), Is.LessThan(1f),
                    $"{weaponName} should return to its non-aim rotation");

                view.TickCombatFeedback(1f);
                for (var frame = 0; frame < 4; frame++)
                {
                    animator.Update(0.2f);
                    lateUpdate.Invoke(view, null);
                }
                Assert.That(Vector3.Angle(instance.transform.forward, weapon.forward), Is.LessThan(8f),
                    $"{weaponName} should aim again after hurt");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void PlayerRifleBarrelsFaceForwardAndOffhandsReachTheirGrips()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        foreach (var weaponName in new[]
                 {
                     "ArcadeRifle", "ArcadePiercingGun", "ArcadeScifiShotgun", "ArcadeScifiPlasmaRifle"
                 })
        {
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, weaponName, false, Vector3.zero);
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var weaponIndex = Enumerable.Range(0, view.ArcadeWeaponVisualCount)
                    .Single(index => view.GetArcadeWeaponVisualName(index) == weaponName);
                var weapon = view.GetArcadeWeaponVisualRoot(weaponIndex).transform;
                var grip = new SerializedObject(view).FindProperty("arcadeWeaponVisuals")
                    .GetArrayElementAtIndex(weaponIndex).FindPropertyRelative("leftHandGripLocalPosition")
                    .vector3Value;
                var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

                animator.Update(0.2f);
                animator.Update(0.2f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(1).IsName("Rifle Aim"), Is.True, weaponName);
                // 四种枪模型均以局部 +Z 为枪管；正式瞄准期间还要保持副手可达。
                for (var frame = 0; frame < 12; frame++)
                {
                    animator.Update(0.1f);
                    view.ApplyWeaponGripPose();
                    Assert.That(Vector3.Angle(instance.transform.forward, weapon.forward), Is.LessThan(8f),
                        $"{weaponName} frame {frame}");
                    Assert.That(Vector3.Dot(instance.transform.up, weapon.up), Is.GreaterThan(0.98f),
                        $"{weaponName} frame {frame}");
                    Assert.That(Vector3.Distance(leftHand.position, weapon.TransformPoint(grip)),
                        Is.LessThan(0.025f), $"{weaponName} frame {frame}");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void IncompatibleWeaponsDoNotUseRifleAimPose()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            foreach (var name in new[] { "ArcadePanzerschreck", "ArcadeGrenade" })
            {
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, name, false, Vector3.zero);
                Assert.That(view.Animator.GetBool("RangedWeaponActive"), Is.False, name);
            }
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, null, false, Vector3.zero);
            Assert.That(view.Animator.GetBool("RangedWeaponActive"), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void LubanWeaponConfigurationProvidesPresentationResourcesAndFirePoint()
    {
        var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
        Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
        var basic = catalog.Weapons.Single(weapon => weapon.Id == "arcade.weapon.basic");
        Assert.That(basic.HeldVisualResourceName, Is.EqualTo("ArcadeBasicRifle"));
        Assert.That(basic.ProjectileVisualResourceName, Is.EqualTo("ArcadeBulletBasic"));
        Assert.That(basic.MuzzleFlash, Is.True);
        Assert.That(basic.ProjectileFirePointLocalPosition, Is.EqualTo(new Vector3(0f, 0f, 0.575f)));
        var grenade = catalog.Weapons.Single(weapon => weapon.Id == "arcade.weapon.grenade");
        Assert.That(grenade.ProjectileVisualResourceName, Is.EqualTo("ArcadeGrenade"));
        Assert.That(grenade.MuzzleFlash, Is.False);
        var spread = catalog.Weapons.Single(weapon => weapon.Id == "arcade.weapon.spread");
        Assert.That(spread.HeldVisualResourceName, Is.EqualTo("ArcadeScifiShotgun"));
        Assert.That(spread.MuzzleFlash, Is.True);
        var rocket = catalog.Weapons.Single(weapon => weapon.Id == "arcade.weapon.rocket");
        Assert.That(rocket.HeldVisualResourceName, Is.EqualTo("ArcadePanzerschreck"));
        Assert.That(rocket.ProjectileVisualResourceName, Is.EqualTo("ArcadePanzerschreckRocket"));
        var raygun = catalog.Weapons.Single(weapon => weapon.Id == "arcade.weapon.raygun");
        Assert.That(raygun.HeldVisualResourceName, Is.EqualTo("ArcadeScifiPlasmaRifle"));
        Assert.That(raygun.MuzzleFlash, Is.True);
    }

    [Test]
    public void AllArcadeWeaponFirePointsSitAtModelFront()
    {
        var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
        Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var visualRoots = new Dictionary<string, string>
        {
            ["ArcadePistol"] = "ArcadeBasicWeapon",
            ["ArcadeRifle"] = "ArcadeRapidWeapon",
            ["ArcadeFlamethrower"] = "ArcadeFlamethrowerWeapon",
            ["ArcadeScifiShotgun"] = "ArcadeSpreadWeapon",
            ["ArcadePiercingGun"] = "ArcadePiercingWeapon",
            ["ArcadePanzerschreck"] = "ArcadeRocketWeapon",
            ["ArcadeGrenade"] = "ArcadeGrenadeWeapon",
            ["ArcadeScifiPlasmaRifle"] = "ArcadeRaygunWeapon"
        };
        foreach (var weapon in catalog.Weapons.Where(w=>w.Id.StartsWith("arcade.weapon.")))
        {
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                if(weapon.Id=="arcade.weapon.basic")
                {
                    var presenter=instance.GetComponent<CharacterWeaponPresenter>();
                    PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view,weapon);
                    presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
                    presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,0);
                    presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                    var mesh=presenter.Visual.GetComponentsInChildren<MeshFilter>(true).First(m=>m.sharedMesh.name=="SM_Prop_Rifle_01");
                    var akTip=mesh.transform.TransformPoint(new Vector3(0,0,mesh.sharedMesh.bounds.max.z));
                    Assert.That(presenter.TryGetProjectileOrigin(out var origin),Is.True);
                    Assert.That(Vector3.Distance(origin,akTip),Is.LessThan(.1f),weapon.Id);
                    continue;
                }
                Object.DestroyImmediate(instance.GetComponent<CharacterWeaponPresenter>());
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, weapon.HeldVisualResourceName,
                    weapon.MuzzleFlash, weapon.ProjectileFirePointLocalPosition);
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(0.2f);
                animator.Update(0.2f);
                view.ApplyWeaponGripPose();
                var visual = Find(instance.transform, visualRoots[weapon.HeldVisualResourceName]);
                var vertices = new List<Vector3>();
                foreach (var mesh in visual.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.sharedMesh != null)
                        vertices.AddRange(mesh.sharedMesh.vertices.Select(vertex => mesh.transform.TransformPoint(vertex)));
                foreach (var mesh in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (mesh.sharedMesh != null)
                        vertices.AddRange(mesh.sharedMesh.vertices.Select(vertex => mesh.transform.TransformPoint(vertex)));
                Assert.That(vertices, Is.Not.Empty, weapon.Id);
                var forward = view.transform.forward;
                var foremost = vertices.Max(vertex => Vector3.Dot(vertex, forward));
                var tipBand = vertices.Where(vertex => Vector3.Dot(vertex, forward) >= foremost - 0.04f).ToList();
                var tip = tipBand.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) / tipBand.Count;
                Assert.That(Vector3.Distance(view.GetProjectileOrigin(forward), tip), Is.LessThan(0.1f),
                    weapon.Id);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void FlamethrowerSharedPresentationUsesItsNozzleAndRestoresBasicWeapon()
    {
        var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
        Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
        try
        {
            var view = instance.GetComponent<PlayerView>();
            var presenter = instance.GetComponent<CharacterWeaponPresenter>();
            var weapon = catalog.Weapons.Single(w => w.Id == "arcade.weapon.flamethrower");
            PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view, weapon);
            Assert.That(presenter.Current.Id, Is.EqualTo(weapon.Id));
            Assert.That(presenter.Current.MuzzleFlash, Is.False);
            var nozzle = presenter.Visual.GetComponentsInChildren<Transform>().Single(t => t.name == "Nozzle");
            Assert.That(presenter.TryGetProjectileOrigin(out var origin), Is.True);
            var tip = nozzle.TransformPoint(Vector3.up);
            Assert.That(Vector3.Distance(origin, tip), Is.LessThan(.01f));
            Assert.That(presenter.Visual.GetComponentsInChildren<Collider>(), Is.Empty);
            PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view,
                catalog.Weapons.Single(w => w.Id == catalog.Rule.BaseWeaponId));
            Assert.That(presenter.Current.Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
        }
        finally { Object.DestroyImmediate(instance); }
    }

    /// <summary>实际动作采样后枪管仍沿角色射击方向，不能只验证枪口点落在模型上。</summary>
    [Test]
    public void FlamethrowerBarrelFacesFiringDirectionAcrossCombatPoses()
    {
        var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
        try
        {
            var presenter=instance.GetComponent<CharacterWeaponPresenter>();
            Assert.That(presenter.SetWeaponId("arcade.weapon.flamethrower"),Is.True);
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
            var a=presenter.Current.Animations;
            foreach(var definition in new[]{a.Idle,a.Attack,a.MoveForward,a.MoveBackward,a.MoveLeft,a.MoveRight})
            foreach(float time in new[]{0f,.25f,.5f,.75f})
            {
                definition.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,definition.Clip.length*time);presenter.ApplyOffhandGrip(definition,time);
                var nozzle=presenter.Visual.GetComponentsInChildren<Transform>().Single(t=>t.name=="Nozzle");
                Assert.That(Vector3.Dot(nozzle.TransformDirection(Vector3.up),instance.transform.forward),Is.GreaterThan(.94f),definition.name+" "+time);
            }
        }
        finally{Object.DestroyImmediate(instance);}
    }

    /// <summary>烘焙喷嘴火团网格检查发射平面，避免火焰向后盖住枪管和角色。</summary>
    [Test]
    public void FlameParticleGeometryStaysAheadOfEmissionPlane()
    {
        var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeFlamethrower.prefab"));
        var cameraObject=new GameObject("FlameGeometryCamera",typeof(Camera));var mesh=new Mesh();
        try
        {
            var particles=instance.GetComponentsInChildren<ParticleSystem>().Single();
            particles.useAutoRandomSeed=false;particles.randomSeed=42;particles.Play(false);particles.Simulate(.08f,false,false,false);particles.Pause();
            var renderer=particles.GetComponent<ParticleSystemRenderer>();var camera=cameraObject.GetComponent<Camera>();
            foreach(var offset in new[]{new Vector3(6,8,-6),new Vector3(-6,8,6)})
            {
                camera.transform.position=offset;camera.transform.LookAt(Vector3.forward);renderer.BakeMesh(mesh,camera,true);
                Assert.That(mesh.vertexCount,Is.GreaterThan(0));
                Assert.That(mesh.vertices.Select(v=>renderer.transform.TransformPoint(v).z-particles.transform.position.z).Min(),Is.GreaterThanOrEqualTo(-.03f));
            }
        }
        finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(instance);}
    }

    /// <summary>世界火团采用软粒子重叠成云，不能拉成三股，也不能隐藏第一人称所依赖的世界表现。</summary>
    [Test]
    public void FlamethrowerUsesOneMuzzleCloudInsteadOfSeparateProjectileStreams()
    {
        var gun=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeFlamethrower.prefab"));
        var projectile=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"));
        try
        {
            projectile.GetComponent<ProjectileView>().SetArcadePresentation("ArcadeFlameBurst",false);
            var separate=projectile.GetComponentsInChildren<ParticleSystem>().Single(p=>p.name=="ArcadeFlameBurst");
            var renderer=separate.GetComponent<ParticleSystemRenderer>();
            Assert.That(renderer.enabled,Is.True,"第一人称仍需要世界火团显示");
            Assert.That(renderer.renderMode,Is.EqualTo(ParticleSystemRenderMode.Billboard),"不能将三个判定载体拉成长火束");
            Assert.That(separate.emission.enabled,Is.True);
            Assert.That(separate.main.startSize.constantMin,Is.GreaterThanOrEqualTo(.9f),"火团应覆盖相邻判定线而非形成分叉");
            Assert.That(separate.main.simulationSpace,Is.EqualTo(ParticleSystemSimulationSpace.Local),"墙体销毁载体时不能残留独立世界火云");
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{n}.bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var weapon=catalog.Weapons.Single(w=>w.Id=="arcade.weapon.flamethrower");
            var systems=gun.GetComponentsInChildren<ParticleSystem>();
            Assert.That(systems.Length,Is.EqualTo(1));
            var cloud=systems.Single();
            Assert.That(cloud.shape.angle,Is.EqualTo(weapon.SpreadDegrees*.5f).Within(.01f));
            Assert.That(cloud.main.startSpeed.constantMax*cloud.main.startLifetime.constantMax,Is.LessThan(weapon.ProjectileSpeed*weapon.ProjectileLifetimeSeconds*.5f),"喷嘴效果只连接近端，不独立飞过墙体");
            Assert.That(cloud.GetComponent<ParticleSystemRenderer>().renderMode,Is.EqualTo(ParticleSystemRenderMode.Billboard));
        }
        finally{Object.DestroyImmediate(gun);Object.DestroyImmediate(projectile);}
    }

    /// <summary>正式开火入口从喷嘴发射近端火焰，连续开火不清空旧粒子，停火和切枪不会残留。</summary>
    [Test]
    public void FlamethrowerMuzzleEmissionStartsAtNozzleAndEndsAfterFireOrWeaponChange()
    {
        var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
        try
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{n}.bytes"))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            var presenter=instance.GetComponent<CharacterWeaponPresenter>();var view=instance.GetComponent<PlayerView>();
            PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view,catalog.Weapons.Single(w=>w.Id=="arcade.weapon.flamethrower"));
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);
            presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,0);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            var flame=presenter.Visual.GetComponentInChildren<ParticleSystem>();
            Assert.That(flame,Is.Not.Null);Assert.That(flame.particleCount,Is.Zero);
            Assert.That(presenter.TryGetProjectileOrigin(out var muzzle),Is.True);
            Assert.That(Vector3.Distance(flame.transform.position,muzzle),Is.LessThan(.001f));
            view.PlayProjectileFireFeedback();
            Assert.That(flame.particleCount,Is.GreaterThan(0));
            var particles=new ParticleSystem.Particle[64];int count=flame.GetParticles(particles);
            for(int index=0;index<count;index++)Assert.That(Vector3.Distance(particles[index].position,muzzle),Is.LessThan(.03f));
            flame.Simulate(.05f,false,false,false);count=flame.particleCount;
            Assert.That(count,Is.GreaterThan(2),"开火窗口应连续发射");
            view.PlayProjectileFireFeedback();Assert.That(flame.particleCount,Is.GreaterThanOrEqualTo(count),"重复开火不能清掉已有火焰");
            flame.Simulate(.6f,false,false,false);Assert.That(flame.particleCount,Is.Zero,"停止开火后应自动结束");
            view.PlayProjectileFireFeedback();
            PlayerViewCtrl.ConfigureSharedArcadeWeaponPresentation(view,catalog.Weapons.Single(w=>w.Id==catalog.Rule.BaseWeaponId));
            Assert.That(flame==null,Is.True,"切枪必须销毁喷嘴火焰");
            Assert.DoesNotThrow(()=>presenter.PlayWeaponMuzzleEffect());
        }
        finally{Object.DestroyImmediate(instance);}
    }

    [Test]
    public void ArcadeWeaponGripKeepsMuzzlesNearPlayerFiringLane()
    {
        var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
        Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        foreach (var weapon in catalog.Weapons)
        {
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, weapon.HeldVisualResourceName,
                    weapon.MuzzleFlash, weapon.ProjectileFirePointLocalPosition);
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(0.2f);
                animator.Update(0.2f);
                view.ApplyWeaponGripPose();
                var muzzle = view.transform.InverseTransformPoint(view.GetProjectileOrigin(view.transform.forward));
                var lateralGap = Mathf.Abs(muzzle.x);
                Assert.That(lateralGap, Is.LessThan(0.5f), $"{weapon.Id}: {muzzle}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void RocketAndGrenadeFaceForwardWithMuzzlesAtTipsDuringFormalIdle()
    {
        var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
            new ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes"))));
        Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        foreach (var visualName in new[] { "ArcadePanzerschreck", "ArcadeGrenade" })
        {
            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var weapon = catalog.Weapons.Single(item => item.HeldVisualResourceName == visualName);
                Object.DestroyImmediate(instance.GetComponent<CharacterWeaponPresenter>());
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, visualName, weapon.MuzzleFlash,
                    weapon.ProjectileFirePointLocalPosition);
                var animator = view.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(0.2f);
                animator.Update(0.2f);
                view.ApplyWeaponGripPose();

                var visual = Find(instance.transform, visualName == "ArcadePanzerschreck"
                    ? "ArcadeRocketWeapon" : "ArcadeGrenadeWeapon");
                Assert.That(Vector3.Angle(view.transform.forward, visual.forward), Is.LessThan(8f), visualName);
                var vertices = visual.GetComponentsInChildren<MeshFilter>(true)
                    .Where(mesh => mesh.sharedMesh != null)
                    .SelectMany(mesh => mesh.sharedMesh.vertices.Select(vertex => mesh.transform.TransformPoint(vertex)))
                    .ToList();
                Assert.That(vertices, Is.Not.Empty, visualName);
                var foremost = vertices.Max(vertex => Vector3.Dot(vertex, view.transform.forward));
                var tipBand = vertices.Where(vertex => Vector3.Dot(vertex, view.transform.forward) >= foremost - 0.04f)
                    .ToList();
                var tip = tipBand.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) / tipBand.Count;
                Assert.That(Vector3.Distance(view.GetProjectileOrigin(view.transform.forward), tip),
                    Is.LessThan(0.1f), visualName);
                var muzzleLocal = view.transform.InverseTransformPoint(view.GetProjectileOrigin(view.transform.forward));
                Assert.That(Mathf.Abs(muzzleLocal.x), Is.LessThan(0.5f), visualName);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }

    [Test]
    public void PistolMuzzleFollowsVisibleBarrelThroughAimAndFire()
    {
        // 保留旧手枪资源的独立回归；当前基础枪已是AK，不能再用它驱动手枪动画。
        const string legacyVisual="ArcadePistol";
        var legacyMuzzle=new Vector3(0,.05f,.23f);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
        var instance = Object.Instantiate(prefab);
        try
        {
            var view = instance.GetComponent<PlayerView>();
            Object.DestroyImmediate(instance.GetComponent<CharacterWeaponPresenter>());
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, legacyVisual,true,legacyMuzzle);
            var animator = view.Animator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var visual = Find(instance.transform, "ArcadeBasicWeapon");
            foreach (var state in new[] { "Pistol Upper Body.Pistol Aim", "Pistol Upper Body.Pistol Fire" })
            foreach (var time in new[] { 0f, 0.25f, 0.5f, 0.75f })
            {
                animator.Play("Base Layer.Idle", 0, 0f);
                animator.Play(state, 2, time);
                animator.Update(0.001f);
                view.ApplyWeaponGripPose();
                // 战斗同步会先刷新当前武器，再在同一帧读取开火枪口。
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, legacyVisual,true,legacyMuzzle);
                Assert.That(Vector3.Angle(view.transform.forward, visual.forward), Is.LessThan(8f),
                    $"{state} {time} after refresh");
                var vertices = visual.GetComponentsInChildren<MeshFilter>(true)
                    .Where(mesh => mesh.sharedMesh != null)
                    .SelectMany(mesh => mesh.sharedMesh.vertices.Select(vertex => mesh.transform.TransformPoint(vertex)))
                    .ToList();
                var foremost = vertices.Max(vertex => Vector3.Dot(vertex, view.transform.forward));
                var tipBand = vertices.Where(vertex => Vector3.Dot(vertex, view.transform.forward) >= foremost - 0.04f)
                    .ToList();
                var tip = tipBand.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) / tipBand.Count;
                Assert.That(Vector3.Distance(view.GetProjectileOrigin(view.transform.forward), tip),
                    Is.LessThan(0.1f), $"{state} {time}");
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void AssertWeapon(PlayerView view, Transform root, string resourceName, string activeName)
    {
        PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, resourceName, false,
            new Vector3(0f, 0.04f, 0.5f));
        foreach (var name in new[] { "ArcadeBasicWeapon", "ArcadeRapidWeapon", "ArcadeSpreadWeapon", "ArcadeRaygunWeapon", "ArcadePiercingWeapon", "ArcadeRocketWeapon", "ArcadeGrenadeWeapon" })
        {
            var child = Find(root, name);
            Assert.That(child, Is.Not.Null, name);
            Assert.That(child.gameObject.activeSelf, Is.EqualTo(name == activeName), $"{resourceName}: {name}");
        }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }
}
}
