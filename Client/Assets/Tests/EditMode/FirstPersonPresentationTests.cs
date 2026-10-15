using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Game.Tests
{
    public sealed class FirstPersonPresentationTests
    {
        private GameObject player;
        private GameObject cameraObject;
        private PlayerCameraCtrl ctrl;
        private PlayerView view;
        private CharacterWeaponPresenter weapons;

        [SetUp]
        public void SetUp()
        {
            player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            view = player.GetComponent<PlayerView>();
            weapons = player.GetComponent<CharacterWeaponPresenter>();
            view.SetSharedWeaponAnimationActive(true);
            view.Animator.Rebind();
            view.Animator.Update(0f);
            cameraObject = new GameObject("FirstPersonTestCamera", typeof(Camera));
            ctrl = new PlayerCameraCtrl(cameraObject.GetComponent<Camera>(), player.transform,
                view.CameraProfile, view.ShoulderCameraProfile);
        }

        [TearDown]
        public void TearDown()
        {
            ctrl?.Dispose();
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(cameraObject);
        }

        // 缺少独立手臂、误隐藏独立武器、换装遗留旧模型或释放泄漏都会使本测试失败。
        [TestCase("weapon.short-sword")]
        [TestCase("weapon.bow")]
        [TestCase("arcade.weapon.basic")]
        [TestCase("arcade.weapon.rapid")]
        [TestCase("arcade.weapon.piercing")]
        [TestCase("arcade.weapon.spread")]
        [TestCase("arcade.weapon.rocket")]
        public void IdleGripStaysInsideFirstPersonViewport(string weaponId)
        {
            Assert.That(weapons.SetWeaponId(weaponId), Is.True, weapons.LastError);
            view.Animator.Rebind();
            view.Animator.Update(.1f);
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            var rig = cameraObject.transform.Find("PlayerFirstPersonRig");
            var arms = rig.GetComponentInChildren<Animator>();
            var hand = arms.GetBoneTransform(weapons.Current.MainHand);
            foreach (var bone in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
                Assert.That(cameraObject.transform.InverseTransformPoint(arms.GetBoneTransform(bone).position).z,
                    Is.LessThan(.05f), "持武器时肩部也必须在镜头外");
            var screen = cameraObject.GetComponent<Camera>().WorldToViewportPoint(hand.position);
            Assert.That(screen.z, Is.GreaterThan(.05f));
            Assert.That(screen.x, Is.InRange(.05f, .95f));
            Assert.That(screen.y, Is.InRange(.05f, .95f), "待机握点必须在视口中，不能只有启用的 Renderer");
            var skin = rig.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(skin.bounds.Contains(hand.position), Is.True,
                "手臂剔除包围盒必须包含弯曲后的手，不能仍停留在导入 T Pose");
            var firstPosition = hand.position;
            for (var frame = 0; frame < 20; frame++) ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 0f, 0f);
            Assert.That(Vector3.Distance(firstPosition, hand.position), Is.LessThan(.001f),
                "同一姿态重复刷新不能累计肩部偏移，导致握点漂移");
        }

        [TestCase("weapon.short-sword")]
        [TestCase("weapon.bow")]
        public void AttackPoseUpdatesVisibleArmsWithoutMovingSourceWeapon(string weaponId)
        {
            Assert.That(weapons.SetWeaponId(weaponId), Is.True, weapons.LastError);
            view.SetManualWeaponPoseSampling(true);
            weapons.SetManualPoseSampling(true);
            view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("FirstPersonAttackPose");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var definition = weapons.Current.Animations.Attack;
                var clip = AnimationClipPlayable.Create(graph, definition.Clip);
                AnimationPlayableOutput.Create(graph, "Player", view.Animator).SetSourcePlayable(clip);
                graph.Play();
                clip.SetTime(definition.Clip.length * .1f);
                graph.Evaluate(0f);
                weapons.ApplyOffhandGrip(definition);
                var sourcePosition = weapons.Visual.transform.position;
                var sourceRotation = weapons.Visual.transform.rotation;
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
                Assert.That(Vector3.Distance(sourcePosition, weapons.Visual.transform.position), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(sourceRotation, weapons.Visual.transform.rotation), Is.LessThan(.001f));
                var rig = cameraObject.transform.Find("PlayerFirstPersonRig");
                var rendered = System.Array.Find(rig.GetComponentsInChildren<MeshFilter>(),
                    m => m.name == "RenderedArms");
                var before = rendered.sharedMesh.vertices;

                clip.SetTime(definition.Clip.length * .6f);
                graph.Evaluate(0f);
                weapons.ApplyOffhandGrip(definition);
                ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 40f, 30f);
                var after = rendered.sharedMesh.vertices;
                var maximumMovement = 0f;
                for (var index = 0; index < before.Length; index++)
                    maximumMovement = Mathf.Max(maximumMovement, Vector3.Distance(before[index], after[index]));
                Assert.That(maximumMovement, Is.GreaterThan(.02f), "可见网格必须跟随实际攻击姿态变化");
                var mesh = rendered.sharedMesh;
                ctrl.Dispose();
                Assert.That(mesh == null, Is.True, "运行时烘焙网格必须随镜头释放");
            }
            finally { graph.Destroy(); }
        }

        [Test]
        public void UnarmedAttackMovesVisibleHandsAndReturnsToIdleWithoutMovingSource()
        {
            Assert.That(weapons.SetWeapon(null), Is.True, weapons.LastError);
            view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.Animator.Rebind();
            view.Animator.Update(.1f);
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            var rig = cameraObject.transform.Find("PlayerFirstPersonRig");
            var arms = rig.GetComponentInChildren<Animator>();
            var hand = arms.GetBoneTransform(HumanBodyBones.RightHand);
            var idle = cameraObject.transform.InverseTransformPoint(hand.position);
            view.PlayAttackFeedback();
            view.Animator.Update(.1f);
            Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") ||
                view.Animator.GetNextAnimatorStateInfo(0).IsName("Attack"), Is.True);
            var maximumMovement = 0f;
            for (var frame = 0; frame < 8; frame++)
            {
                view.Animator.Play("Attack", 0, .1f + frame * .1f);
                view.Animator.Update(0f);
                var original = view.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 0f, 0f);
                maximumMovement = Mathf.Max(maximumMovement,
                    Vector3.Distance(idle, cameraObject.transform.InverseTransformPoint(hand.position)));
                Assert.That(Vector3.Distance(original, view.Animator.GetBoneTransform(HumanBodyBones.RightHand).position),
                    Is.LessThan(.0001f), "第一人称副本不能改写正式角色的攻击骨骼");
                foreach (var bone in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
                    Assert.That(cameraObject.transform.InverseTransformPoint(arms.GetBoneTransform(bone).position).z,
                        Is.LessThan(.05f), "挥拳时肩部截口也不能进入镜头");
            }
            Assert.That(maximumMovement, Is.GreaterThan(.03f), "徒手攻击必须形成可见手部位移");
            view.Animator.Play("Idle", 0, 0f);
            view.Animator.Update(0f);
            ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 0f, 0f);
            Assert.That(Vector3.Distance(idle, cameraObject.transform.InverseTransformPoint(hand.position)),
                Is.LessThan(.001f), "攻击结束恢复空手待机，不能累计挥拳偏移");
        }

        [Test]
        public void MissingArmsRejectsModeWithoutHidingPlayer()
        {
            var serialized = new SerializedObject(view);
            serialized.FindProperty("firstPersonArmsPrefab").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var renderer = view.Animator.GetComponentInChildren<SkinnedMeshRenderer>();
            var culling = view.Animator.cullingMode;
            Assert.That(ctrl.SupportsMode(PlayerCameraMode.FirstPerson, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(view.Animator.cullingMode, Is.EqualTo(culling));
            Assert.That(cameraObject.transform.Find("PlayerFirstPersonRig"), Is.Null);
        }

        // 空手构图不能把第三人称垂手动作的肩膀一起搬到镜头前。
        [TestCase(0f, 0f)]
        [TestCase(90f, 60f)]
        [TestCase(-90f, -60f)]
        public void UnarmedHandsExtendFromBelowWithShouldersOutsideCamera(float yaw, float pitch)
        {
            Assert.That(weapons.SetWeapon(null), Is.True, weapons.LastError);
            view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            view.Animator.Rebind();
            view.Animator.Update(.1f);
            var originalShoulder = view.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
            var camera = cameraObject.GetComponent<Camera>();
            camera.aspect = 16f / 9f;
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, yaw, pitch);
            var arms = cameraObject.transform.Find("PlayerFirstPersonRig").GetComponentInChildren<Animator>();
            foreach (var right in new[] { false, true })
            {
                var upper = arms.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                var hand = arms.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                Assert.That(camera.transform.InverseTransformPoint(upper.position).z, Is.LessThan(.05f),
                    "肩部和上臂截口必须留在镜头外");
                var screen = camera.WorldToViewportPoint(hand.position);
                Assert.That(screen.z, Is.GreaterThan(.1f));
                Assert.That(screen.y, Is.InRange(.05f, .45f), "双手应在画面下方自然前伸");
                Assert.That(screen.x, right ? Is.InRange(.55f, .95f) : Is.InRange(.05f, .45f));
            }
            Assert.That(Vector3.Distance(originalShoulder,
                view.Animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position), Is.LessThan(.0001f));
        }

        [TestCase("weapon.short-sword")]
        [TestCase("arcade.weapon.basic")]
        public void UnequippingRightHandWeaponClearsVisualAndRestoresUnarmedPose(string weaponId)
        {
            Assert.That(weapons.SetWeaponId(weaponId), Is.True, weapons.LastError);
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            var rig = cameraObject.transform.Find("PlayerFirstPersonRig");
            Assert.That(rig.Find("FirstPersonWeapon_" + weaponId), Is.Not.Null);
            Assert.That(weapons.SetWeapon(null), Is.True, weapons.LastError);
            ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 0f, 0f);
            Assert.That(rig.Find("FirstPersonWeapon_" + weaponId), Is.Null,
                "源武器已销毁时也必须清理副本，不能因 Unity 的空值比较而遗漏");
            var arms = rig.GetComponentInChildren<Animator>();
            Assert.That(cameraObject.transform.InverseTransformPoint(
                arms.GetBoneTransform(HumanBodyBones.RightUpperArm).position).z, Is.LessThan(.05f));
        }

        // Avatar 手骨的局部轴不同；武器若直接照搬骨骼旋转，枪管就会转向侧面。
        [Test]
        public void FirstPersonPistolKeepsBarrelForward()
        {
            Assert.That(weapons.SetWeaponId("arcade.weapon.basic"), Is.True, weapons.LastError);
            view.SetManualWeaponPoseSampling(true);
            weapons.SetManualPoseSampling(true);
            view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("FirstPersonPistolPose");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, weapons.Current.Animations.Idle.Clip);
                AnimationPlayableOutput.Create(graph, "Player", view.Animator).SetSourcePlayable(clip);
                graph.Play();
                clip.SetTime(.1f);
                graph.Evaluate(0f);
                weapons.ApplyOffhandGrip(weapons.Current.Animations.Idle);
                Assert.That(Vector3.Angle(Vector3.forward,
                    weapons.Visual.GetComponentInChildren<MeshRenderer>().transform.forward), Is.LessThan(15f),
                    "源动作必须先形成有效的前向握枪姿态");
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
                var rig = cameraObject.transform.Find("PlayerFirstPersonRig");
                var copy = rig.Find("FirstPersonWeapon_arcade.weapon.basic").GetComponentInChildren<MeshRenderer>();
                Assert.That(Vector3.Angle(Vector3.forward, copy.transform.forward), Is.LessThan(15f));
            }
            finally { graph.Destroy(); }
        }

        [Test]
        public void FirstPersonShowsArmsAndCurrentWeaponThenReleasesOnExit()
        {
            Assert.That(weapons.SetWeaponId("weapon.short-sword"), Is.True, weapons.LastError);
            view.Animator.Update(0.1f);
            var culling = view.Animator.cullingMode;
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 35f, -25f);
            var rig = cameraObject.transform.Find("PlayerFirstPersonRig");
            Assert.That(rig, Is.Not.Null, "第一人称必须创建可见手臂和当前武器");
            var arm = rig.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(arm, Is.Not.Null);
            var renderedArm = rig.GetComponentsInChildren<MeshRenderer>();
            Assert.That(System.Array.Exists(renderedArm, r => r.name == "RenderedArms" && r.enabled), Is.True);
            Assert.That(view.Animator.cullingMode, Is.EqualTo(AnimatorCullingMode.AlwaysAnimate));
            var body = view.Animator.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(body.forceRenderingOff, Is.True);
            Assert.That(rig.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(rig.GetComponentsInChildren<CharacterWeaponPresenter>(), Is.Empty);
            Assert.That(rig.Find("FirstPersonWeapon_weapon.short-sword").GetComponentsInChildren<MeshRenderer>().Length,
                Is.GreaterThan(0));

            Assert.That(weapons.SetWeaponId("weapon.bow"), Is.True, weapons.LastError);
            view.Animator.Update(0.1f);
            ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 80f, 50f);
            Assert.That(rig.GetComponentsInChildren<MeshRenderer>().Length, Is.GreaterThan(0));
            Assert.That(rig.Find("FirstPersonWeapon_weapon.short-sword"), Is.Null);
            Assert.That(rig.Find("FirstPersonWeapon_weapon.bow"), Is.Not.Null);
            Assert.That(Quaternion.Angle(rig.rotation, cameraObject.transform.rotation), Is.LessThan(.01f));
            Assert.That(weapons.SetWeaponId(null), Is.True, weapons.LastError);
            ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 80f, 50f);
            Assert.That(rig.Find("FirstPersonWeapon_weapon.bow"), Is.Null, "空手不能保留旧武器");

            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 80f, 0f);
            Assert.That(cameraObject.transform.Find("PlayerFirstPersonRig"), Is.Null);
            Assert.That(body.forceRenderingOff, Is.False);
            Assert.That(view.Animator.cullingMode, Is.EqualTo(culling));
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            ctrl.Dispose();
            ctrl.Dispose();
            Assert.That(cameraObject.transform.Find("PlayerFirstPersonRig"), Is.Null);
            Assert.That(body.forceRenderingOff, Is.False);
        }
    }
}
