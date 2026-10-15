using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>
    /// 本机相机拥有的手臂与武器表现副本；读取已采样的 Humanoid 动作和当前外观。
    /// 不创建碰撞、投射物或装备状态，也不改写原武器枪口与伤害结算。
    /// </summary>
    internal sealed class PlayerFirstPersonCtrl : IDisposable
    {
        private readonly Camera camera;
        private readonly PlayerView view;
        private readonly Animator sourceAnimator;
        private readonly AnimatorCullingMode originalCulling;
        private readonly GameObject rig;
        private readonly Animator armsAnimator;
        private readonly HumanPoseHandler sourcePose;
        private readonly HumanPoseHandler armsPose;
        private readonly SkinnedMeshRenderer armSkin;
        // 手臂仅约千顶点；显式烘焙避免手动 PoseHandler 与 GPU 蒙皮缓存的帧序不一致。
        private readonly Mesh armMesh;
        // 固定 Avatar 的重心高度，不能归零，否则整副骨架会下沉约一米。
        private readonly Vector3 restBodyPosition;
        private HumanPose pose;
        private HumanPose unarmedPose;
        private static readonly int AttackStateHash = Animator.StringToHash("Attack");
        /// <summary>最近空手非攻击帧的重定向握点，单位为副本根局部米；换装后重新采样。</summary>
        private readonly Vector3[] unarmedReferenceHands = new Vector3[2];
        /// <summary>当前源攻击帧相对空手基准的镜头局部位移；每次刷新重算，不累计。</summary>
        private readonly Vector3[] unarmedAttackOffsets = new Vector3[2];
        private bool unarmedReferenceCaptured;
        private Vector3 framingOffset;
        private bool framingCaptured;
        // 副本只包含网格与 Renderer；每次换装销毁旧副本，材质/网格继续由资源拥有者管理。
        private Transform weaponSource;
        private GameObject weaponRoot;
        private HumanBodyBones mainHand;
        private readonly Dictionary<HumanBodyBones, Quaternion> handAxes = new();
        private readonly List<(Renderer source, Renderer copy)> weaponRenderers = new();
        private readonly List<Mesh> ownedMeshes = new();
        private bool disposed;
        /// <summary>仅街机水平射击校正表现副本；普通第一人称沿用原动作，不修改源枪口。</summary>
        private readonly bool horizontalArcadeAim;

        public PlayerFirstPersonCtrl(Camera camera, PlayerView view, bool horizontalArcadeAim = false)
        {
            this.horizontalArcadeAim = horizontalArcadeAim;
            this.camera = camera;
            this.view = view;
            sourceAnimator = view.Animator;
            if (sourceAnimator == null || !sourceAnimator.isHuman || view.FirstPersonArmsPrefab == null) return;
            originalCulling = sourceAnimator.cullingMode;
            try
            {
                // 原身体被隐藏后仍须求值动画，否则手臂、武器和正式枪口都会停留在旧帧。
                sourceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                rig = new GameObject("PlayerFirstPersonRig") { hideFlags = HideFlags.DontSave };
                rig.SetActive(false);
                rig.transform.SetParent(camera.transform, false);
                var arms = Object.Instantiate(view.FirstPersonArmsPrefab, rig.transform);
                armsAnimator = arms.GetComponent<Animator>();
                armsAnimator.runtimeAnimatorController = null;
                // 肌肉姿态由本控制器采样后写入，避免无 Controller 的 Animator 自动求值覆盖。
                armsAnimator.enabled = false;
                armsAnimator.applyRootMotion = false;
                armsAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                sourcePose = new HumanPoseHandler(sourceAnimator.avatar, sourceAnimator.transform);
                armsPose = new HumanPoseHandler(armsAnimator.avatar, armsAnimator.transform);
                var restPose = new HumanPose();
                armsPose.GetHumanPose(ref restPose);
                restBodyPosition = restPose.bodyPosition;
                unarmedPose = new HumanPose
                {
                    bodyPosition = restBodyPosition,
                    bodyRotation = Quaternion.identity,
                    muscles = new float[HumanTrait.MuscleCount]
                };
                armSkin = arms.GetComponentInChildren<SkinnedMeshRenderer>();
                armMesh = new Mesh { name = "FirstPersonAnimatedArms" };
                armMesh.MarkDynamic();
                var renderedArms = new GameObject("RenderedArms", typeof(MeshFilter), typeof(MeshRenderer));
                renderedArms.transform.SetParent(armSkin.transform, false);
                renderedArms.GetComponent<MeshFilter>().sharedMesh = armMesh;
                renderedArms.GetComponent<MeshRenderer>().sharedMaterials = armSkin.sharedMaterials;
                armSkin.forceRenderingOff = true;
                foreach (var hand in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                    handAxes[hand] = Quaternion.Inverse(GetBindRotation(armsAnimator, hand)) *
                                     GetBindRotation(sourceAnimator, hand);
                foreach (var renderer in arms.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    if (renderer is SkinnedMeshRenderer skin)
                    {
                        skin.updateWhenOffscreen = true;
                        // 关闭自动 Animator 后不会自动扩展 T Pose 的狭窄包围盒；覆盖可达手臂范围。
                        skin.localBounds = new Bounds(new Vector3(0f, 1f, 0f), Vector3.one * 4f);
                    }
                }
                rig.SetActive(true);
                Camera.onPreCull += OnCameraPreCull;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            }
            catch
            {
                // 构造尚未返回，外层拿不到实例；在此释放已分配资源，避免拾取失败遗留隐藏手臂。
                Dispose();
                throw;
            }
        }

        // 在最终渲染前再读一次原骨架，兼容共享武器 Presenter 的 LateUpdate 顺序与 URP。
        private void OnCameraPreCull(Camera renderingCamera)
        {
            if (renderingCamera == camera) Refresh();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera == camera) Refresh();
        }

        /// <summary>相机定位及原 Animator 求值后刷新；复用原动作的挥砍/射击，不另起战斗计时。</summary>
        public void Refresh()
        {
            if (disposed || rig == null || sourceAnimator == null || view == null) return;
            sourcePose.GetHumanPose(ref pose);
            // 去掉全身动画的根位移，镜头不跟随走路晃动；手臂仍保留肌肉与攻击姿态。
            pose.bodyPosition = restBodyPosition;
            armsPose.SetHumanPose(ref pose);
            rig.transform.localPosition = new Vector3(0f, -1.45f, .15f);
            rig.transform.localRotation = Quaternion.identity;
            var current = view.GetFirstPersonWeapon(out var currentHand, out var identity);
            // Unity 会把已销毁的源 Transform 与 null 判为相等；副本仍存活时必须显式清理。
            if (current != weaponSource || currentHand != mainHand || current == null && weaponRoot != null)
            {
                ClearWeapon();
                weaponSource = current;
                mainHand = currentHand;
                if (current != null) CreateWeapon(identity);
                framingCaptured = false;
                unarmedReferenceCaptured = false;
            }
            if (current == null)
            {
                ApplyUnarmedPose();
            }
            else
            {
                if (!framingCaptured)
                {
                    var hand = armsAnimator.GetBoneTransform(currentHand);
                    if (hand != null)
                    {
                        // 每次换装只捕获一次偏移；后续挥砍和后坐保留原动作位移。
                        var desired = new Vector3(currentHand == HumanBodyBones.LeftHand ? -.22f : .22f, -.22f, .5f);
                        framingOffset = desired - camera.transform.InverseTransformPoint(hand.position);
                        framingCaptured = true;
                    }
                }
                rig.transform.localPosition += framingOffset;
                PoseHeldArm(false);
                PoseHeldArm(true);
            }
            var sleeves=view.ClothingAppearance?.CurrentSleeves;
            if(sleeves!=null && armSkin.sharedMesh!=sleeves) armSkin.sharedMesh=sleeves;
            armSkin.BakeMesh(armMesh);
            RefreshWeapon();
            if (horizontalArcadeAim) AlignArcadeWeaponToCrosshair();
        }

        /// <summary>重定向后将整副手臂和枪的可见朝向汇聚至中心前方 20 米；只调整镜头子对象。</summary>
        private void AlignArcadeWeaponToCrosshair()
        {
            var entry = view.GetComponent<CharacterWeaponPresenter>()?.Current;
            if (entry == null || !entry.IsProjectile || weaponSource == null || weaponRenderers.Count == 0) return;
            var pair = weaponRenderers[0];
            if (pair.source == null || pair.copy == null) return;
            var sourceMuzzle = weaponSource.TransformPoint(entry.ProjectileFirePointLocalPosition);
            var muzzle = pair.copy.transform.TransformPoint(pair.source.transform.InverseTransformPoint(sourceMuzzle));
            var forward = pair.copy.transform.TransformDirection(
                pair.source.transform.InverseTransformDirection(view.transform.forward));
            var target = camera.transform.position + camera.transform.forward * 20f;
            var correction = Quaternion.FromToRotation(forward, target - muzzle);
            // 围绕副本枪口一起转动手臂，保持握持关系；源 Renderer/Animator 和正式枪口保持原值。
            rig.transform.position = muzzle + correction * (rig.transform.position - muzzle);
            rig.transform.rotation = correction * rig.transform.rotation;
        }

        /// <summary>空手保留前伸构图并叠加已采样攻击位移；不另起计时，也不改原骨架。</summary>
        private void ApplyUnarmedPose()
        {
            CaptureUnarmedAttackMotion();
            armsPose.SetHumanPose(ref unarmedPose);
            // 肩部留在近裁剪面后，肘部从屏幕下方进入；位置均为镜头局部米制坐标。
            rig.transform.localPosition = new Vector3(0f, -1.66f, -.05f);
            PoseUnarmedArm(false);
            PoseUnarmedArm(true);
        }

        private void PoseUnarmedArm(bool right)
        {
            var target = camera.transform.TransformPoint(new Vector3(right ? .24f : -.24f, -.22f, .46f) +
                unarmedAttackOffsets[right ? 1 : 0]);
            SolveArm(right, target, false);
        }

        /// <summary>在覆盖为空手待机前读取源动作；进出 Attack 的混合帧也保留挥拳，结束即清零。</summary>
        private void CaptureUnarmedAttackMotion()
        {
            var attacking = sourceAnimator.layerCount > 0 &&
                (sourceAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == AttackStateHash ||
                 sourceAnimator.IsInTransition(0) && sourceAnimator.GetNextAnimatorStateInfo(0).shortNameHash == AttackStateHash);
            for (var index = 0; index < 2; index++)
            {
                var hand = armsAnimator.GetBoneTransform(index == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var sampled = rig.transform.InverseTransformPoint(hand.position);
                if (!attacking || !unarmedReferenceCaptured) unarmedReferenceHands[index] = sampled;
                var delta = attacking ? sampled - unarmedReferenceHands[index] : Vector3.zero;
                // 第三人称拳头从垂手抬起；第一人称已有前伸基准，压缩抬手并保留前击，避免穿镜头。
                unarmedAttackOffsets[index] = new Vector3(
                    Mathf.Clamp(delta.x * .45f, -.14f, .14f),
                    Mathf.Clamp(delta.y * .15f, -.08f, .12f),
                    Mathf.Clamp(delta.z * .65f, -.1f, .3f));
            }
            unarmedReferenceCaptured = true;
        }

        /// <summary>持武器只调整独立副本的肩肘；保留采样手腕朝向及握点运动，不扭转原武器。</summary>
        private void PoseHeldArm(bool right)
        {
            var upper = armsAnimator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var hand = armsAnimator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            var target = hand.position;
            var clavicle = armsAnimator.GetBoneTransform(right ? HumanBodyBones.RightShoulder : HumanBodyBones.LeftShoulder);
            var desiredShoulder = camera.transform.TransformPoint(new Vector3(right ? .24f : -.24f, -.3f, -.04f));
            // 连同锁骨的蒙皮截口一起移出镜头，避免只移上臂骨后拉长肩部网格。
            if (clavicle != null) clavicle.position += desiredShoulder - upper.position;
            else upper.position = desiredShoulder;
            SolveArm(right, target, true);
        }

        private void SolveArm(bool right, Vector3 target, bool preserveWristRotation)
        {
            var upper = armsAnimator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = armsAnimator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = armsAnimator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            var wristRotation = hand.rotation;
            var upperLength = Vector3.Distance(upper.position, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);
            var toTarget = target - upper.position;
            var distance = Mathf.Clamp(toTarget.magnitude, .0001f, upperLength + lowerLength - .0001f);
            var direction = toTarget.normalized;
            var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            // 镜头向下为弯肘平面，避免人体侧向肘弯在第一人称里形成朝上的断臂。
            var bend = Vector3.ProjectOnPlane(-camera.transform.up, direction).normalized;
            var elbow = upper.position + direction * along + bend * height;
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
            if (preserveWristRotation) hand.rotation = wristRotation;
        }

        private void CreateWeapon(string identity)
        {
            weaponRoot = new GameObject("FirstPersonWeapon_" + identity);
            weaponRoot.transform.SetParent(rig.transform, false);
            foreach (var source in weaponSource.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter))
                    mesh = filter.sharedMesh;
                else if (source is SkinnedMeshRenderer skin)
                {
                    // 少数武器自带蒙皮；烘焙表现网格，不复制其动画回调或产品组件。
                    mesh = new Mesh();
                    skin.BakeMesh(mesh);
                    ownedMeshes.Add(mesh);
                }
                else if (source is ParticleSystemRenderer particles && source.GetComponentInParent<HandheldTorchView>() != null)
                {
                    // 仅复制火把的可见粒子网格，不复制点光源、声音、领域组件或粒子推进权威。
                    mesh = new Mesh();
                    particles.BakeMesh(mesh, camera, false);
                    ownedMeshes.Add(mesh);
                }
                if (mesh == null) continue;
                var go = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(weaponRoot.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var copy = go.GetComponent<MeshRenderer>();
                copy.sharedMaterials = source.sharedMaterials;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                weaponRenderers.Add((source, copy));
            }
        }

        /// <summary>按源主手到目标主手映射模型；后坐、挂点覆盖与左右手持弓均沿用当前表现。</summary>
        private void RefreshWeapon()
        {
            if (weaponRoot == null || weaponSource == null) return;
            var sourceHand = sourceAnimator.GetBoneTransform(mainHand);
            var destinationHand = armsAnimator.GetBoneTransform(mainHand);
            if (sourceHand == null || destinationHand == null) return;
            // Humanoid 重定向的是人体肌肉，不保证不同模型手骨的局部轴一致。
            var mappedRotation = destinationHand.rotation * handAxes[mainHand];
            foreach (var pair in weaponRenderers)
            {
                if (pair.source == null) { pair.copy.enabled = false; continue; }
                pair.copy.enabled = pair.source.enabled && pair.source.gameObject.activeInHierarchy;
                pair.copy.transform.SetPositionAndRotation(
                    destinationHand.position + mappedRotation * sourceHand.InverseTransformPoint(pair.source.transform.position),
                    mappedRotation * Quaternion.Inverse(sourceHand.rotation) * pair.source.transform.rotation);
                pair.copy.transform.localScale = pair.source.transform.lossyScale;
                if (pair.source is SkinnedMeshRenderer skin)
                    skin.BakeMesh(pair.copy.GetComponent<MeshFilter>().sharedMesh);
                else if (pair.source is ParticleSystemRenderer particles)
                    particles.BakeMesh(pair.copy.GetComponent<MeshFilter>().sharedMesh, camera, false);
                else if (pair.source.TryGetComponent<MeshFilter>(out var sourceFilter))
                    // 弓第一次拉弦会替换为自有动态网格，副本只跟随引用，不拥有或销毁源网格。
                    pair.copy.GetComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            }
        }

        /// <summary>从 Avatar 导入时的绑定姿态计算手骨基底，不临时改写正在播放的角色骨骼。</summary>
        private static Quaternion GetBindRotation(Animator animator, HumanBodyBones hand)
        {
            var rotations = new Dictionary<string, Quaternion>();
            foreach (var bone in animator.avatar.humanDescription.skeleton) rotations[bone.name] = bone.rotation;
            var result = Quaternion.identity;
            for (var current = animator.GetBoneTransform(hand); current != null && current != animator.transform;
                 current = current.parent)
                result = (rotations.TryGetValue(current.name, out var value) ? value : current.localRotation) * result;
            return result;
        }

        private void ClearWeapon()
        {
            DestroyOwned(weaponRoot);
            weaponRoot = null;
            weaponSource = null;
            weaponRenderers.Clear();
            foreach (var mesh in ownedMeshes) DestroyOwned(mesh);
            ownedMeshes.Clear();
        }

        /// <summary>退出视角、解绑或地图释放时调用；幂等释放 PoseHandler、网格和镜头子节点。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Camera.onPreCull -= OnCameraPreCull;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ClearWeapon();
            sourcePose?.Dispose();
            armsPose?.Dispose();
            DestroyOwned(armMesh);
            if (rig != null && sourceAnimator != null) sourceAnimator.cullingMode = originalCulling;
            DestroyOwned(rig);
        }

        private static void DestroyOwned(Object owned)
        {
            if (owned == null) return;
            if (owned is GameObject go)
            {
                go.SetActive(false);
                go.transform.SetParent(null, false);
            }
            if (Application.isPlaying) Object.Destroy(owned);
            else Object.DestroyImmediate(owned);
        }
    }
}
