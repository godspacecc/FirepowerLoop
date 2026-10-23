using UnityEngine;
using UnityEngine.UI;
using System;

namespace Game
{
    /// <summary>执行玩家 Animator、挂点、骨骼和世界交互表现；生命由屏幕 HUD 展示，不持有战斗权威。</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed partial class PlayerView : MonoBehaviour
    {
        [Serializable]
        private struct ArcadeWeaponVisualBinding
        {
            [SerializeField] private string resourceName;
            [SerializeField] private GameObject visualRoot;
            [SerializeField] private bool hasLeftHandGrip;
            [SerializeField] private Vector3 leftHandGripLocalPosition;
            [SerializeField] private Quaternion aimLocalRotation;
            public string ResourceName => resourceName;
            public GameObject VisualRoot => visualRoot;
            public bool HasLeftHandGrip => hasLeftHandGrip;
            public Vector3 LeftHandGripLocalPosition => leftHandGripLocalPosition;
            /// <summary>瞄准时的模型局部旋转；垂手姿态取 Prefab 的原始 Transform。</summary>
            public Quaternion AimLocalRotation => aimLocalRotation;
        }

        private static readonly int MoveSpeedAnimatorParameter = Animator.StringToHash("MoveSpeed");
        private static readonly int AttackAnimatorParameter = Animator.StringToHash("Attack");
        /// <summary>仅缩放 Attack 状态的播放速度；默认 1，不影响移动、受击与死亡。</summary>
        private static readonly int AttackPlaybackSpeedAnimatorParameter = Animator.StringToHash("AttackPlaybackSpeed");
        private static readonly int HitAnimatorParameter = Animator.StringToHash("Hit");
        private static readonly int DeadAnimatorParameter = Animator.StringToHash("Dead");
        private static readonly int RangedWeaponAnimatorParameter = Animator.StringToHash("RangedWeaponActive");
        private static readonly int PistolWeaponAnimatorParameter = Animator.StringToHash("PistolWeaponActive");
        private static readonly int PistolFireAnimatorParameter = Animator.StringToHash("PistolFire");
        private static readonly int ShotgunWeaponAnimatorParameter = Animator.StringToHash("ShotgunWeaponActive");
        private static readonly int ShotgunFireAnimatorParameter = Animator.StringToHash("ShotgunFire");
        private static readonly int IdleAnimatorState = Animator.StringToHash("Idle");
        /// <summary>首次收到已倒地状态时采样终态，不重演加载前的死亡过程。</summary>
        private static readonly int DeathAnimatorState = Animator.StringToHash("Death");
        // 步枪与手枪的遮罩不同：步枪保留根部覆盖，手枪仅覆盖上身以维持直立站姿。
        private const int RifleUpperBodyLayerIndex = 1;
        private const int PistolUpperBodyLayerIndex = 2;
        private const float FeedbackDurationSeconds = 0.12f;
        private const float HurtAimSuppressionSeconds = 0.24f;
        private const float WeaponSwingDurationSeconds = 0.16f;
        /// <summary>挥刀周期预留的收刀混合时间，单位秒；对应共享控制器的 0.08 秒退出混合及余量。</summary>
        private const float WeaponSwingRecoverySeconds = 0.1f;
        private const float ProjectileRecoilDurationSeconds = 0.1f;
        private const float ProjectileBodyRecoilDegrees = 8f;
        private static readonly Vector3 AttackFeedbackScale = new Vector3(1.08f, 1f, 1.2f);
        private static readonly Quaternion WeaponSwingRotation = Quaternion.Euler(0f, 55f, 0f);
        // 以右手局部坐标校准枪管：垂手时朝下，抬手瞄准时随骨骼转向前方。
        private static readonly Quaternion RangedWeaponHandRotation =
            Quaternion.LookRotation(new Vector3(2f, 1f, 0f), Vector3.back);
        private static readonly Vector3 ProjectileRecoilOffset = new Vector3(0f, 0f, -0.08f);
        private static readonly Vector3 DefaultProjectileFirePoint = new Vector3(0f, 0.04f, 0.5f);
        // 玩家本地坐标中的主手目标位置；让各枪枪口靠近角色前方的既有射击线。
        // 手枪双手握持时，主手目标必须留在左臂可达范围内，避免副手始终悬空。
        private static readonly Vector3 ArcadeAimHandLocalPosition = new Vector3(0.10f, 1.25f, 0.40f);
        private static readonly Color HurtFeedbackColor = Color.red;

        [SerializeField]
        private CharacterController characterController;

        [Tooltip("只负责玩家 Humanoid 的移动与战斗表现，不保存任何移动或战斗状态。")]
        [SerializeField]
        private Animator animator;
        [SerializeField, Tooltip("当前人物骨架的衣裤表现绑定，不保存装备状态。")]
        private ClothingAppearanceView clothingAppearance;
        public ClothingAppearanceView ClothingAppearance => clothingAppearance;


        [Header("Formal Bow Animation")]
        [SerializeField] private AnimationClip bowIdleClip;
        [SerializeField] private AnimationClip bowWalkForwardClip;
        [SerializeField] private AnimationClip bowWalkBackClip;
        [SerializeField] private AnimationClip bowWalkLeftClip;
        [SerializeField] private AnimationClip bowWalkRightClip;
        [SerializeField] private AnimationClip bowFireClip;
        private RuntimeAnimatorController baseAnimatorController;
        private AnimatorOverrideController bowAnimatorController;
        // -1 表示尚未选定方向；只在方向象限改变时替换 Run 片段，避免每帧重建控制器。
        private int bowRunDirection = -1;

        [SerializeField]
        private TopDownCameraProfile cameraProfile;

        [SerializeField]
        private ShoulderCameraProfile shoulderCameraProfile;

        [SerializeField]
        private GameObject tiliRingCanvas;

        [SerializeField]
        private Image tiliRingFill;

        [SerializeField]
        private UnitActionProgressView actionProgressView;

        [SerializeField]
        private GameObject shortSwordObject;

        [SerializeField]
        private Transform meleeWeaponAttachRoot;

        [SerializeField]
        private Transform weaponSwingRoot;

        [SerializeField]
        private GameObject bowObject;

        [SerializeField]
        private Transform rangedWeaponAttachRoot;

        [SerializeField]
        private Transform projectileFirePoint;

        [Tooltip("仅播放枪械枪口火光，不参与弹丸生成与命中判定。")]
        [SerializeField]
        private ParticleSystem muzzleFlash;

        [Header("Arcade Weapon Presentation")]
        [SerializeField]
        private ArcadeWeaponVisualBinding[] arcadeWeaponVisuals = Array.Empty<ArcadeWeaponVisualBinding>();

        // 仅保存当前可见外观的副手握点；装备身份和战斗状态仍由上层权威系统管理。
        private GameObject activeArcadeWeaponVisual;
        // Awake 捕获 Prefab 的垂手旋转，防止切装时把运行中的瞄准旋转误当默认值。
        private Quaternion[] arcadeWeaponRestLocalRotations;
        private Quaternion activeArcadeWeaponRestLocalRotation;
        private Quaternion activeArcadeWeaponAimLocalRotation;
        // 控制器选定的当前武器姿态；切换或隐藏武器时复位为 None。
        private PlayerWeaponGripPose activeGripPose;
        // 仅标识当前显示的霰弹枪，避免共用步枪层的其他长枪进入霰弹枪开火状态。
        private bool activeShotgun;
        private Vector3 activeLeftHandGripLocalPosition;
        private bool activeLeftHandGrip;
        // 五角色共享动作启用时，旧的手枪/步枪上身层不得覆盖目录中的完整 Humanoid 片段。
        private bool sharedWeaponAnimationActive;
        // 造型库手动 PlayableGraph 采样时跳过正式 Animator 的副手修正，改由预览入口按当前片段调用。
        private bool manualWeaponPoseSampling;

        [SerializeField]
        private Light flashlight;

        [SerializeField]
        private DamageNumberEmitter damageNumberEmitter;

        [Tooltip("街机模式用于区分本机玩家的脚底识别环；不参与碰撞或玩法判定。")]
        [SerializeField]
        private GameObject arcadePlayerMarker;

        [SerializeField, Range(0f, 60f)]
        private float flashlightDownwardAngleDegrees = 18f;

        private Vector3 baseScale = Vector3.one;
        private float scaleFeedbackRemainingSeconds;
        private float colorFeedbackRemainingSeconds;
        private float weaponSwingRemainingSeconds;
        /// <summary>单次劈砍速度的表现恢复计时，单位秒；切装、受击和死亡立即清零。</summary>
        private float singleAttackSpeedResetRemainingSeconds;
        private bool weaponSwingBaseCaptured;
        private Quaternion weaponSwingBaseRotation = Quaternion.identity;
        private float projectileRecoilRemainingSeconds;
        private bool projectileRecoilBaseCaptured;
        private Vector3 projectileRecoilBasePosition;
        private Transform projectileBodyRecoilBone;
        private float projectileBodyRecoilEndTime;
        private Renderer[] feedbackRenderers = new Renderer[0];
        /// <summary>入场时准备颜色覆盖缓冲；受击只处理当前可见模型，不重新遍历角色子树。</summary>
        private MaterialPropertyBlock[] feedbackOriginalBlocks = Array.Empty<MaterialPropertyBlock>();
        private bool[] feedbackTinted = Array.Empty<bool>();
        private bool feedbackTargetsPrepared;
        private MaterialPropertyBlock feedbackPropertyBlock;
        private bool presentationAlive = true;
        /// <summary>仅管理当前实例的脚底贴地表现与采样缓冲，权威位置仍属于移动系统。</summary>
        private PlayerGroundContactPresenter groundContact;
        private PlayerSwimmingPresenter swimmingPresenter;
        /// <summary>领域状态的只读表现镜像，不供玩法资格裁决。</summary>
        public bool SwimmingPresentation {get;private set;}
        public void SetSwimmingPresentation(bool value)
        {
            SwimmingPresentation=value;
            if(swimmingPresenter==null)swimmingPresenter=new PlayerSwimmingPresenter(animator);
            swimmingPresenter.SetActive(value);
            if(value)SetMovementSpeed(0);
        }
        private void ApplySwimmingPresentation(float deltaSeconds)=>swimmingPresenter?.Apply(deltaSeconds);
        private bool muzzleFlashEnabled;
        // 受击暂时关闭上半身武器层；恢复时根据当前控制器选定的姿态重新计算。
        private float rangedAimSuppressionRemainingSeconds;
        private bool arcadePresentationActive;
        /// <summary>随玩家 Prefab 显式加载保护环 Shader，避免安卓依赖全局查找缓存。</summary>
        [SerializeField] private Shader arcadeProtectionShader;
        /// <summary>旧资源缺少保护环依赖时只提示一次；纯表现缺失不能冻结街机活动。</summary>
        private bool protectionShaderWarningReported;
        /// <summary>当前角色独占的保护表现；不保存保护时长或无敌来源。</summary>
        private ArcadeProtectionPresentation arcadeProtection;


        public CharacterController CharacterController
        {
            get
            {
                if (characterController == null)
                {
                    characterController = GetComponent<CharacterController>();
                }

                return characterController;
            }
        }

        public TopDownCameraProfile CameraProfile => cameraProfile;
        public ShoulderCameraProfile ShoulderCameraProfile => shoulderCameraProfile;
        public Animator Animator => animator;

        /// <summary>第一人称专用手臂资源；只供本机镜头实例化，不参与角色碰撞和战斗。</summary>
        [SerializeField] private GameObject firstPersonArmsPrefab;
        public GameObject FirstPersonArmsPrefab => firstPersonArmsPrefab;

        /// <summary>读取已经提交的当前武器表现；共享目录优先，旧挂点只返回活动外观。</summary>
        internal Transform GetFirstPersonWeapon(out HumanBodyBones mainHand, out string identity)
        {
            var shared = GetComponent<CharacterWeaponPresenter>();
            if (shared != null)
            {
                mainHand = shared.Current?.MainHand ?? HumanBodyBones.RightHand;
                identity = shared.Current?.Id;
                return shared.Visual != null && shared.Visual.activeInHierarchy ? shared.Visual.transform : null;
            }
            mainHand = HumanBodyBones.RightHand;
            var visual = activeArcadeWeaponVisual != null && activeArcadeWeaponVisual.activeInHierarchy
                ? activeArcadeWeaponVisual.transform : FindActiveWeapon(meleeWeaponAttachRoot) ?? FindActiveWeapon(rangedWeaponAttachRoot);
            identity = visual != null ? visual.name : null;
            return visual;
        }

        private static Transform FindActiveWeapon(Transform root)
        {
            if (root == null || !root.gameObject.activeInHierarchy) return null;
            foreach (Transform child in root)
                if (child.gameObject.activeInHierarchy && child.GetComponentInChildren<Renderer>() != null)
                    return child;
            return null;
        }

        /// <summary>读取正式街机 Prefab 内置的武器外观，供独立预览使用；不改变装备或存档。</summary>
        public int ArcadeWeaponVisualCount => arcadeWeaponVisuals?.Length ?? 0;

        public string GetArcadeWeaponVisualName(int index) =>
            index >= 0 && index < ArcadeWeaponVisualCount ? arcadeWeaponVisuals[index].ResourceName : null;

        /// <summary>只向造型库提供正式玩家武器的外观源，不代表敌人已具备该武器的战斗能力。</summary>
        public GameObject GetArcadeWeaponVisualRoot(int index) =>
            index >= 0 && index < ArcadeWeaponVisualCount ? arcadeWeaponVisuals[index].VisualRoot : null;

        /// <summary>从正式武器根推导运行时绑定右手后的局部姿态，供其他角色临时试装；不改变玩家 Prefab。</summary>
        public bool TryGetArcadeWeaponHandPose(int index, out Vector3 position, out Quaternion rotation,
            out Vector3 scale)
        {
            position = default;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            var visual = GetArcadeWeaponVisualRoot(index);
            if (visual == null || rangedWeaponAttachRoot == null || visual.transform.parent != rangedWeaponAttachRoot)
                return false;
            CaptureArcadeWeaponRestRotations();
            position = RangedWeaponHandRotation * visual.transform.localPosition;
            rotation = RangedWeaponHandRotation * arcadeWeaponRestLocalRotations[index];
            scale = visual.transform.localScale;
            return true;
        }

        private void Awake()
        {
            baseScale = transform.localScale;
            baseAnimatorController = animator != null ? animator.runtimeAnimatorController : null;
            UpdateWeaponAimAnimator(false);
            BindWeaponAttachRoots();
            CaptureArcadeWeaponRestRotations();
            CaptureWeaponSwingBase();
            HideLegacyWeaponObjects();
            actionProgressView?.Initialize();
            SetFlashlightActive(false);
        }

        private void OnDestroy()
        {
            arcadeDamage?.Dispose();arcadeDamage=null;
            arcadeProtection?.Dispose();
            arcadeProtection = null;
            swimmingPresenter?.Dispose();swimmingPresenter=null;
            groundContact?.Dispose();
            if (bowAnimatorController == null) return;
            if (Application.isPlaying) Destroy(bowAnimatorController);
            else DestroyImmediate(bowAnimatorController);
        }

        /// <summary>生成一次对称的上身后坐曲线，起点和终点归零，中点达到最大角度。</summary>
        internal static float CalculateProjectileBodyRecoilDegrees(float normalizedProgress)
        {
            return Mathf.Sin(Mathf.Clamp01(normalizedProgress) * Mathf.PI) * ProjectileBodyRecoilDegrees;
        }

        public void ShowDamage(int damage, bool playerTarget)
        {
            damageNumberEmitter?.Show(damage, playerTarget);
        }

        /// <summary>
        /// 切换存活表现。死亡时保留 Humanoid 渲染以播放动画，只停用碰撞、武器、HUD 与手电筒。
        /// </summary>
        public void SetAlivePresentation(bool alive)
            => SetAlivePresentation(alive,true);

        /// <summary>联机首次加载可关闭历史死亡补播，后续状态变化沿用正式死亡动画。</summary>
        public void SetAlivePresentation(bool alive,bool playTransition)
        {
            var changed = presentationAlive != alive;
            if(arcadePresentationActive)
            {
                PrepareArcadeDamage();arcadeDamage?.SetAlive(alive,playTransition);
            }
            presentationAlive = alive;
            if (animator != null && changed)
            {
                ResetWeaponAttackPresentation();
                animator.SetFloat(MoveSpeedAnimatorParameter, 0f);
                animator.ResetTrigger(AttackAnimatorParameter);
                animator.ResetTrigger(HitAnimatorParameter);
                animator.ResetTrigger(PistolFireAnimatorParameter);
                animator.ResetTrigger(ShotgunFireAnimatorParameter);
                animator.SetBool(DeadAnimatorParameter, !alive);
                if (alive)
                {
                    // Death 状态没有退出转换；对象复活时显式回到初始待机态。
                    animator.Play(IdleAnimatorState, 0, 0f);
                    ApplyRequestedWeaponAim();
                }
                else
                {
                    UpdateWeaponAimAnimator(false);
                    if(!playTransition)animator.Play(DeathAnimatorState,0,1f);
                }
            }

            if (CharacterController != null)
            {
                CharacterController.enabled = alive;
            }

            SetActive(arcadePlayerMarker, alive && arcadePresentationActive);
            if (!alive) arcadeProtection?.SetActive(false);
            SetActive(meleeWeaponAttachRoot != null ? meleeWeaponAttachRoot.gameObject : null, alive);
            SetActive(rangedWeaponAttachRoot != null ? rangedWeaponAttachRoot.gameObject : null, alive);
            if (!alive)
            {
                ResetProjectileRecoil();
                muzzleFlash?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                HideActionProgress();
                SetFlashlightActive(false);
            }
        }

        public void SetOnlineAlive(bool alive) => SetAlivePresentation(alive);

        public void SetTiliRing(
            float current,
            float maximum,
            bool isSprinting,
            bool exhausted)
        {
            if (tiliRingCanvas == null)
            {
                return;
            }

            tiliRingCanvas.SetActive(false);
            if (tiliRingFill != null)
            {
                tiliRingFill.enabled = false;
            }
        }

        public void SetWeaponPresentation(bool ranged, bool showShortSword, bool showBow)
        {
            if (GetComponent<CharacterWeaponPresenter>() != null)
            {
                // 共享 Presenter 已拥有 Animator；清旧模型时不可切回旧 Bow Controller。
                HideLegacyWeaponObjects();
                muzzleFlashEnabled = false;
                activeGripPose = PlayerWeaponGripPose.None;
                activeShotgun = false;
                activeLeftHandGrip = false;
                ApplyRequestedWeaponAim();
                return;
            }
            SetWeaponVisualPose(0f);
            HideLegacyWeaponObjects();
            muzzleFlashEnabled = false;
            activeGripPose = PlayerWeaponGripPose.None;
            activeShotgun = false;
            activeLeftHandGrip = false;
            animator?.ResetTrigger(PistolFireAnimatorParameter);
            animator?.ResetTrigger(ShotgunFireAnimatorParameter);
            ApplyRequestedWeaponAim();
            SetBowAnimatorActive(ranged && showBow);
            ConfigureProjectileFirePoint(DefaultProjectileFirePoint);
        }

        /// <summary>切装备时复用基础状态机，只替换弓待机、移动和射箭片段；不改变领域规则。</summary>
        private void SetBowAnimatorActive(bool active)
        {
            if (animator == null) return;
            // 嵌套 Humanoid 的 Controller 可能在玩家 Awake 后才完成绑定，首次切装备时再捕获。
            if (baseAnimatorController == null) baseAnimatorController = animator.runtimeAnimatorController;
            if (baseAnimatorController == null) return;
            if (!active)
            {
                if (animator.runtimeAnimatorController != baseAnimatorController)
                    animator.runtimeAnimatorController = baseAnimatorController;
                return;
            }
            if (bowIdleClip == null || bowWalkForwardClip == null || bowWalkBackClip == null ||
                bowWalkLeftClip == null || bowWalkRightClip == null || bowFireClip == null) return;
            if (bowAnimatorController == null)
            {
                bowAnimatorController = new AnimatorOverrideController(baseAnimatorController);
                bowAnimatorController["HumanM@Idle01"] = bowIdleClip;
                bowAnimatorController["HumanM@Attack1H01_R"] = bowFireClip;
            }
            // 重新持弓从确定的前进片段起步；下一次实际移动会选取正确方向。
            if (bowRunDirection != 0)
                bowAnimatorController["HumanM@Run01_Forward"] = bowWalkForwardClip;
            bowRunDirection = 0;
            if (animator.runtimeAnimatorController != bowAnimatorController)
                animator.runtimeAnimatorController = bowAnimatorController;
        }

        /// <summary>按控制器选定的姿态切换街机外观；只执行 Unity 对象与 Animator 表现。</summary>
        internal void SetArcadeWeaponPresentation(string visualResourceName, bool showMuzzleFlash,
            Vector3 projectileFirePointLocalPosition, PlayerWeaponGripPose gripPose)
        {
            if (!string.IsNullOrEmpty(visualResourceName)) SetBowAnimatorActive(false);
            // 嵌套 Humanoid 的 Avatar 可能在 Awake 之后才完成初始化，首次使用武器时再兜底绑定。
            BindWeaponAttachRoots();
            CaptureArcadeWeaponRestRotations();
            muzzleFlashEnabled = showMuzzleFlash;
            var matched = false;
            var previousArcadeWeaponVisual = activeArcadeWeaponVisual;
            activeArcadeWeaponVisual = null;
            activeGripPose = PlayerWeaponGripPose.None;
            activeShotgun = false;
            activeLeftHandGrip = false;
            for (var index = 0; index < arcadeWeaponVisuals.Length; index++)
            {
                var binding = arcadeWeaponVisuals[index];
                // 同一把枪每帧同步时保留已叠加的动画朝向，避免开火前重置枪身和枪口。
                if (binding.VisualRoot != null && binding.VisualRoot != previousArcadeWeaponVisual)
                    binding.VisualRoot.transform.localRotation = arcadeWeaponRestLocalRotations[index];
                var active = !matched && !string.IsNullOrWhiteSpace(visualResourceName) &&
                             string.Equals(binding.ResourceName, visualResourceName, StringComparison.Ordinal);
                SetActive(binding.VisualRoot, active);
                if (active)
                {
                    matched = true;
                    activeArcadeWeaponVisual = binding.VisualRoot;
                    activeGripPose = gripPose;
                    activeShotgun = visualResourceName == "ArcadeScifiShotgun";
                    activeLeftHandGrip = gripPose != PlayerWeaponGripPose.None && binding.HasLeftHandGrip;
                    activeLeftHandGripLocalPosition = binding.LeftHandGripLocalPosition;
                    activeArcadeWeaponRestLocalRotation = arcadeWeaponRestLocalRotations[index];
                    activeArcadeWeaponAimLocalRotation = Quaternion.Dot(binding.AimLocalRotation,
                        binding.AimLocalRotation) > 0.5f
                        ? binding.AimLocalRotation : activeArcadeWeaponRestLocalRotation;
                }
            }

            // 无专用动作的武器会转动共用挂点；切回其他枪前恢复右手基础挂载朝向。
            if (activeArcadeWeaponVisual != previousArcadeWeaponVisual && rangedWeaponAttachRoot != null &&
                animator != null && animator.isHuman &&
                rangedWeaponAttachRoot.parent == animator.GetBoneTransform(HumanBodyBones.RightHand))
                rangedWeaponAttachRoot.localRotation = RangedWeaponHandRotation;
            ConfigureProjectileFirePoint(projectileFirePointLocalPosition);

            animator?.ResetTrigger(PistolFireAnimatorParameter);
            // 每帧重复同步同一外观时不能清掉刚设置的开火触发；只在真正切枪时清理。
            if (activeArcadeWeaponVisual != previousArcadeWeaponVisual)
                animator?.ResetTrigger(ShotgunFireAnimatorParameter);
            ApplyRequestedWeaponAim();
        }

        /// <summary>
        /// 切换街机专用识别表现。街机 HUD 显示权威生命，此处只切换脚底识别环；
        /// 离开街机时按当前存活状态恢复普通地图表现。
        /// </summary>
        public void SetArcadePresentationActive(bool active)
        {
            if(!active)ResetHerdImpact();
            arcadePresentationActive = active;
            SetActive(arcadePlayerMarker, presentationAlive && active);
            if (active)
            {
                // 把对象和缓冲准备移到进场阶段，首次受击只切换既有表现。
                PrepareFeedbackRenderers();
                PrepareArcadeDamage();
                PrepareArcadeProtection();
                damageNumberEmitter?.Prepare();
            }
            else
            {
                RestoreFeedbackColors();
                colorFeedbackRemainingSeconds = 0f;
                arcadeDamage?.Dispose();arcadeDamage=null;
                arcadeProtection?.Dispose();
                arcadeProtection = null;
            }
        }

        /// <summary>仅镜像战斗权威合并后的有效保护；死亡和隐藏时禁止留下悬空表现。</summary>
        public void SetArcadeProtectionActive(bool active)
        {
            active &= arcadePresentationActive && presentationAlive && isActiveAndEnabled;
            if (active) PrepareArcadeProtection();
            arcadeProtection?.SetActive(active);
        }

        /// <summary>只创建依赖已就绪的保护表现；旧 Prefab 缺少引用时保留角色移动和战斗同步。</summary>
        private void PrepareArcadeProtection()
        {
            if (arcadeProtection != null) return;
            if (arcadeProtectionShader == null)
            {
                if (!protectionShaderWarningReported)
                {
                    protectionShaderWarningReported = true;
                    Log.Warning("[PlayerView] 玩家 Prefab 缺少保护环 Shader 引用，已跳过保护环表现。");
                }
                return;
            }
            arcadeProtection = new ArcadeProtectionPresentation(transform, arcadeProtectionShader);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        public bool TryGetWeaponAttachRoot(global::PlayerWeaponAttachPoint attachPoint, out Transform root)
        {
            BindWeaponAttachRoots();
            root = attachPoint == global::PlayerWeaponAttachPoint.LeftHandWeapon
                ? animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.LeftHand)
                    : null
                : attachPoint == global::PlayerWeaponAttachPoint.MeleeWeapon
                ? meleeWeaponAttachRoot
                : attachPoint == global::PlayerWeaponAttachPoint.RangedWeapon
                    ? rangedWeaponAttachRoot
                    : null;
            return root != null;
        }

        /// <summary>正式装备和造型库共用同一份武器局部姿态；调用方仍负责实例及资源 Handle 的生命周期。</summary>
        public static void ApplyWeaponAppearancePose(Transform visual, PlayerWeaponAppearanceDefinition appearance)
        {
            visual.localPosition = appearance.LocalPosition;
            visual.localRotation = Quaternion.Euler(appearance.LocalEulerAngles);
            visual.localScale = appearance.LocalScale;
        }

        public Vector3 GetProjectileOrigin(Vector3 direction)
        {
            var presenter = GetComponent<CharacterWeaponPresenter>();
            if (presenter != null)
            {
                if (presenter.TryGetProjectileOrigin(out var origin)) return origin;
            }
            else if (projectileFirePoint != null)
            {
                return projectileFirePoint.position;
            }

            direction.y = 0f;
            var forward = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            return transform.position + forward * 0.6f + Vector3.up * 1.1f;
        }

        public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp)
        {
            FaceTransform(tiliRingCanvas != null ? tiliRingCanvas.transform : null, cameraPosition, cameraUp);
            actionProgressView?.FaceWorld(cameraPosition, cameraUp);
        }

        public void ShowActionProgress(float progress01, string iconAddress)
        {
            GetComponent<CharacterWeaponPresenter>()?.ShowReload(progress01);
            actionProgressView?.Show(progress01, iconAddress);
        }

        public void HideActionProgress()
        {
            GetComponent<CharacterWeaponPresenter>()?.ClearReload();
            actionProgressView?.Hide();
        }

        public void PlayAttackFeedback()
        {
            SetAttackPlaybackSpeed(1f);
            TriggerAttackAnimation();
            EnsureBaseScale();
            scaleFeedbackRemainingSeconds = FeedbackDurationSeconds;
            transform.localScale = Vector3.Scale(baseScale, AttackFeedbackScale);
        }

        public void PlayWeaponSwingFeedback() => PlayWeaponSwingFeedback(0f);

        /// <summary>街机按既有间隔压缩挥刀；普通单次劈砍读取动作的表现时长，其余零值沿用原速。</summary>
        public void PlayWeaponSwingFeedback(float attackIntervalSeconds)
        {
            if (!presentationAlive) return;
            var definition = GetComponent<CharacterWeaponPresenter>()?.Current?.Animations?.Attack;
            var clip = definition?.Clip;
            var duration = attackIntervalSeconds > 0f
                ? Mathf.Max(0.01f, attackIntervalSeconds - WeaponSwingRecoverySeconds)
                : definition?.SingleAttackDurationSeconds ?? 0f;
            // 只加速 Attack 状态，不使用 Animator.speed，避免连带加速移动、受击与死亡。
            var speed = clip != null && duration > 0f ? Mathf.Max(1f, clip.length / duration) : 1f;
            SetAttackPlaybackSpeed(speed);
            singleAttackSpeedResetRemainingSeconds = definition != null && definition.SingleAttackDurationSeconds > 0f
                ? clip.length / speed + WeaponSwingRecoverySeconds : 0f;
            if (definition != null && definition.SingleAttackDurationSeconds > 0f && animator != null)
            {
                // 单刀起手覆盖尚未结束的移动混合，避免跑动时先等移动转换而拖过既有攻击间隔。
                animator.ResetTrigger(AttackAnimatorParameter);
                animator.ResetTrigger(HitAnimatorParameter);
                animator.CrossFadeInFixedTime("Base Layer.Attack", 0.02f, 0, 0f);
            }
            else TriggerAttackAnimation();
            CaptureWeaponSwingBase();
            weaponSwingRemainingSeconds = WeaponSwingDurationSeconds;
            if (weaponSwingRoot != null)
            {
                weaponSwingRoot.localRotation = weaponSwingBaseRotation * WeaponSwingRotation;
            }
        }

        /// <summary>只播放系统已接受的一刀；专用状态没有自动接刀过渡，段末由系统收刀。</summary>
        public void PlayMeleeComboStage(int stage, float durationSeconds)
        {
            var held = GetComponent<CharacterWeaponPresenter>()?.Current;
            var clips = held?.MeleeCombo;
            if (!presentationAlive || animator == null || durationSeconds <= 0f || clips == null ||
                stage < 0 || stage >= clips.Length || clips[stage]?.Clip == null) return;
            var state = Animator.StringToHash("Base Layer.SteelBladeSlash" + (stage + 1));
            if (!animator.HasState(0, state)) return;
            animator.ResetTrigger(HitAnimatorParameter);
            animator.ResetTrigger(AttackAnimatorParameter);
            SetAttackPlaybackSpeed(clips[stage].Clip.length / durationSeconds);
            // 相邻段共享同一边界姿态，直接接段避免混合吞掉短促的落刀。
            if (stage == 0) animator.CrossFadeInFixedTime(state, 0.04f, 0, 0f);
            else animator.Play(state, 0, 0f);
        }

        /// <summary>完成/中断时撤销专用状态；死亡或受击已经接管时不覆盖其表现。</summary>
        public void StopMeleeCombo()
        {
            if (animator == null || !presentationAlive) return;
            var current = animator.GetCurrentAnimatorStateInfo(0);
            var next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : default;
            var ownsState = false;
            for (var stage = 1; stage <= 4; stage++)
                ownsState |= animator.IsInTransition(0)
                    ? next.IsName("SteelBladeSlash" + stage) : current.IsName("SteelBladeSlash" + stage);
            if (ownsState) animator.CrossFadeInFixedTime("Base Layer.Idle", 0.08f, 0);
        }

        public void PlayProjectileFireFeedback()
        {
            SetAttackPlaybackSpeed(1f);
            var held = GetComponent<CharacterWeaponPresenter>()?.Current;
            if (sharedWeaponAnimationActive)
            {
                if (held == null || !held.IsProjectile) return;
                if (presentationAlive && rangedAimSuppressionRemainingSeconds <= 0f && animator != null)
                {
                    var presenter = GetComponent<CharacterWeaponPresenter>();
                    if (presenter.UsesUpperBodyGunFire) presenter.PlayUpperBodyGunFire();
                    else animator.SetTrigger(AttackAnimatorParameter);
                }
                PlaySharedMuzzleFlash(held.MuzzleFlash);
                return;
            }
            if (activeGripPose == PlayerWeaponGripPose.Pistol && presentationAlive &&
                rangedAimSuppressionRemainingSeconds <= 0f && animator != null)
                animator.SetTrigger(PistolFireAnimatorParameter);
            if (!sharedWeaponAnimationActive && activeShotgun && presentationAlive &&
                rangedAimSuppressionRemainingSeconds <= 0f && animator != null)
                animator.SetTrigger(ShotgunFireAnimatorParameter);
            PlayRangedFireFeedback(muzzleFlashEnabled);
        }

        public void PlayBowFireFeedback()
        {
            SetAttackPlaybackSpeed(1f);
            if (sharedWeaponAnimationActive)
            {
                if (presentationAlive && animator != null)
                    animator.SetTrigger(AttackAnimatorParameter);
                var held = GetComponent<CharacterWeaponPresenter>()?.Current;
                PlaySharedMuzzleFlash(held != null && held.MuzzleFlash);
                return;
            }
            if (animator != null && (sharedWeaponAnimationActive ||
                                     animator.runtimeAnimatorController == bowAnimatorController))
                animator.SetTrigger(AttackAnimatorParameter);
            PlayRangedFireFeedback(false);
        }

        private void PlaySharedMuzzleFlash(bool enabled)
        {
            var presenter = GetComponent<CharacterWeaponPresenter>();
            presenter?.PlayWeaponMuzzleEffect();
            if (!enabled || muzzleFlash == null) return;
            if (presenter == null || !presenter.TryGetProjectileOrigin(out var origin)) return;
            muzzleFlash.transform.position = origin;
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureArcadeGunMuzzle(presenter.Current?.Id);
            muzzleFlash.Play(true);
        }

        /// <summary>三种普通枪按稳定身份选择短促闪光；其余共享武器恢复原模板参数，不继承上把枪。</summary>
        private void ConfigureArcadeGunMuzzle(string weaponId)
        {
            float size = .42f, lifetime = .045f;
            switch (weaponId)
            {
                case "arcade.weapon.basic": size = .44f; lifetime = .04f; break;
                case "arcade.weapon.rapid": size = .30f; lifetime = .025f; break;
                case "arcade.weapon.spread": size = .70f; lifetime = .055f; break;
            }
            gunMuzzleParticles ??= muzzleFlash.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particles in gunMuzzleParticles)
            {
                var main = particles.main;
                main.startSize = particles == muzzleFlash ? size : size * (.25f / .42f);
                main.startLifetime = lifetime;
            }
        }

        /// <summary>缓存当前玩家自有的枪口子粒子，避免每次连射查询并分配数组。</summary>
        private ParticleSystem[] gunMuzzleParticles;

        private void PlayRangedFireFeedback(bool showMuzzleFlash)
        {
            // 枪械不再复用单手剑 Attack；上身回弹与枪体后坐共同组成独立射击动作。
            projectileBodyRecoilEndTime = Time.time + ProjectileRecoilDurationSeconds;
            CaptureProjectileRecoilBase();
            projectileRecoilRemainingSeconds = ProjectileRecoilDurationSeconds;
            if (rangedWeaponAttachRoot != null)
            {
                // 后坐方向以枪管为准，避免手骨旋转后仍沿旧的局部 Z 轴偏移。
                rangedWeaponAttachRoot.localPosition = projectileRecoilBasePosition +
                                                     rangedWeaponAttachRoot.localRotation * ProjectileRecoilOffset;
            }

            if (showMuzzleFlash && muzzleFlash != null)
            {
                // 连射时清空上一帧残留，保证每次开火都有独立的短促闪光。
                muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                muzzleFlash.Play(true);
            }
        }

        /// <summary>正式 Animator 求值后统一叠加后坐与副手握持；造型库手动采样也调用同一握持入口。</summary>
        private void LateUpdate()
        {
            herdVisual.Restore();
            if (animator != null)
            {
                if (groundContact == null) groundContact = new PlayerGroundContactPresenter(animator, CharacterController);
                groundContact.Apply(presentationAlive && !SwimmingPresentation);
            }
            if(ApplyHerdImpact())return;
            if (animator == null || !animator.isActiveAndEnabled) return;
            arcadeDamage?.ApplyHurtPose(animator);
            if(SwimmingPresentation){ApplySwimmingPresentation(Time.deltaTime);return;}
            if (GetComponent<CharacterWeaponPresenter>() != null && sharedWeaponAnimationActive) return;
            if (Time.time < projectileBodyRecoilEndTime)
            {
                if (projectileBodyRecoilBone == null)
                    projectileBodyRecoilBone = animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                                               animator.GetBoneTransform(HumanBodyBones.Chest);
                if (projectileBodyRecoilBone != null)
                {
                    var remaining = Mathf.Max(0f, projectileBodyRecoilEndTime - Time.time);
                    var progress = 1f - Mathf.Clamp01(remaining / ProjectileRecoilDurationSeconds);
                    var recoil = CalculateProjectileBodyRecoilDegrees(progress);
                    projectileBodyRecoilBone.localRotation *= Quaternion.Euler(-recoil, 0f, 0f);
                }
            }
            if (manualWeaponPoseSampling) return;
            var aimBlend = ResolveWeaponAimBlend();
            // 长枪原动画负责主手姿态；通用枪口校准会把步枪类主手抬过头顶。
            if (activeGripPose != PlayerWeaponGripPose.Rifle && presentationAlive &&
                rangedAimSuppressionRemainingSeconds <= 0f)
                AlignArcadeMainHand(activeGripPose == PlayerWeaponGripPose.None ? 1f : aimBlend);
            SetWeaponVisualPose(aimBlend);
            if (rangedAimSuppressionRemainingSeconds > 0f || !presentationAlive) return;
            var rifleGrip = activeGripPose == PlayerWeaponGripPose.Rifle &&
                            animator.layerCount > RifleUpperBodyLayerIndex &&
                            (animator.GetCurrentAnimatorStateInfo(RifleUpperBodyLayerIndex).IsName("Rifle Aim") ||
                             activeShotgun && animator.GetCurrentAnimatorStateInfo(RifleUpperBodyLayerIndex)
                                 .IsName("Shotgun Fire"));
            var pistolGrip = activeGripPose == PlayerWeaponGripPose.Pistol &&
                             animator.layerCount > PistolUpperBodyLayerIndex &&
                             (animator.GetCurrentAnimatorStateInfo(PistolUpperBodyLayerIndex).IsName("Pistol Aim") ||
                              animator.GetCurrentAnimatorStateInfo(PistolUpperBodyLayerIndex).IsName("Pistol Fire"));
            if (rifleGrip || pistolGrip)
                AlignOffhandGrip();
        }

        /// <summary>手动动画采样开始时切换姿态来源；正式 Animator 更新保持默认自动处理。</summary>
        public void SetManualWeaponPoseSampling(bool enabled) => manualWeaponPoseSampling = enabled;

        /// <summary>手动采样确认当前动作为持枪动作后，设置瞄准旋转并对齐副手握点。</summary>
        public void ApplyWeaponGripPose()
        {
            if (activeGripPose != PlayerWeaponGripPose.Rifle) AlignArcadeMainHand(1f);
            SetWeaponVisualPose(1f);
            AlignOffhandGrip();
        }

        /// <summary>造型库手动采样开火时叠加正式战斗的短后坐；传入当前开火循环内的秒数。</summary>
        internal void ApplyProjectileFirePreviewPose(float elapsedSeconds)
        {
            if (animator == null || elapsedSeconds < 0f ||
                elapsedSeconds >= ProjectileRecoilDurationSeconds) return;
            var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                        animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) return;
            var progress = elapsedSeconds / ProjectileRecoilDurationSeconds;
            chest.localRotation *= Quaternion.Euler(-CalculateProjectileBodyRecoilDegrees(progress), 0f, 0f);
        }

        /// <summary>Animator 求值后移动持枪主手，保留手腕朝向和枪身原有瞄准旋转。</summary>
        private void AlignArcadeMainHand(float weight)
        {
            if (activeArcadeWeaponVisual == null || animator == null || !animator.isHuman) return;
            var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (upper == null || hand == null) return;
            var fromShoulder = hand.position - upper.position;
            var target = Vector3.Lerp(hand.position, transform.TransformPoint(ArcadeAimHandLocalPosition),
                Mathf.Clamp01(weight));
            var toTarget = target - upper.position;
            if (fromShoulder.sqrMagnitude <= 0.000001f || toTarget.sqrMagnitude <= 0.000001f) return;

            // 仅旋转大臂，Animator 下一帧会重新求值；恢复手腕世界旋转以免枪管跟着翻转。
            var handRotation = hand.rotation;
            upper.rotation = Quaternion.FromToRotation(fromShoulder, toTarget) * upper.rotation;
            hand.rotation = handRotation;
        }

        /// <summary>手动预览与正式 Animator 共用枪模型的垂手／瞄准旋转；输入为 0～1 的瞄准权重。</summary>
        internal void SetWeaponVisualPose(float aimBlend)
        {
            if (activeArcadeWeaponVisual == null) return;
            var weapon = activeArcadeWeaponVisual.transform;
            if (activeGripPose == PlayerWeaponGripPose.None)
            {
                // 无专用上身动画的火箭筒和投掷武器也要朝向前方；整组挂点同转，枪口不脱离模型。
                if (rangedWeaponAttachRoot != null)
                    rangedWeaponAttachRoot.rotation = Quaternion.LookRotation(transform.forward, transform.up) *
                                                     Quaternion.Inverse(weapon.localRotation);
                return;
            }
            var weight = Mathf.Clamp01(aimBlend);
            weapon.localRotation = Quaternion.Slerp(
                activeArcadeWeaponRestLocalRotation, activeArcadeWeaponAimLocalRotation,
                weight);
            if (activeGripPose == PlayerWeaponGripPose.Pistol && weight > 0f)
            {
                // 开火片段会转动右腕；只校正手枪枪身方向，保留手腕位移和后坐动画。
                var forwardPose = Quaternion.LookRotation(transform.forward, transform.up);
                weapon.rotation = Quaternion.Slerp(weapon.rotation, forwardPose, weight);
            }
        }

        /// <summary>按正式上半身状态与过渡进度计算旋转，避免装备、受击和恢复时枪身跳转。</summary>
        private float ResolveWeaponAimBlend()
        {
            var layer = activeGripPose == PlayerWeaponGripPose.Rifle ? RifleUpperBodyLayerIndex :
                activeGripPose == PlayerWeaponGripPose.Pistol ? PistolUpperBodyLayerIndex : -1;
            if (animator == null || layer < 0 || animator.layerCount <= layer) return 0f;
            var currentAim = IsWeaponAimState(animator.GetCurrentAnimatorStateInfo(layer));
            if (!animator.IsInTransition(layer)) return currentAim ? 1f : 0f;
            var nextAim = IsWeaponAimState(animator.GetNextAnimatorStateInfo(layer));
            return Mathf.Lerp(currentAim ? 1f : 0f, nextAim ? 1f : 0f,
                Mathf.Clamp01(animator.GetAnimatorTransitionInfo(layer).normalizedTime));
        }

        private bool IsWeaponAimState(AnimatorStateInfo state) => activeGripPose == PlayerWeaponGripPose.Rifle
            ? state.IsName("Rifle Aim") || activeShotgun && state.IsName("Shotgun Fire")
            : state.IsName("Pistol Aim") || state.IsName("Pistol Fire");

        private void CaptureArcadeWeaponRestRotations()
        {
            if (arcadeWeaponRestLocalRotations != null &&
                arcadeWeaponRestLocalRotations.Length == arcadeWeaponVisuals.Length) return;
            arcadeWeaponRestLocalRotations = new Quaternion[arcadeWeaponVisuals.Length];
            for (var index = 0; index < arcadeWeaponVisuals.Length; index++)
                arcadeWeaponRestLocalRotations[index] = arcadeWeaponVisuals[index].VisualRoot != null
                    ? arcadeWeaponVisuals[index].VisualRoot.transform.localRotation : Quaternion.identity;
        }

        /// <summary>只处理副手骨骼，不修改枪身或装备权威状态。</summary>
        private void AlignOffhandGrip()
        {
            if (!activeLeftHandGrip || activeArcadeWeaponVisual == null || animator == null ||
                !animator.isHuman) return;

            var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;
            var target = activeArcadeWeaponVisual.transform.TransformPoint(activeLeftHandGripLocalPosition);
            var shoulder = upper.position;
            var upperLength = Vector3.Distance(shoulder, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);
            var toTarget = target - shoulder;
            if (upperLength <= 0.0001f || lowerLength <= 0.0001f || toTarget.sqrMagnitude <= 0.000001f)
                return;
            // 握点超出双臂长度时先让锁骨伸向目标；仅转动肘部会永久留下可见间隙。
            var clavicle = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            if (clavicle != null && toTarget.magnitude > upperLength + lowerLength)
            {
                clavicle.position += toTarget.normalized *
                                     Mathf.Min(0.08f, toTarget.magnitude - upperLength - lowerLength + 0.001f);
                shoulder = upper.position;
                toTarget = target - shoulder;
            }
            var distance = Mathf.Clamp(toTarget.magnitude, 0.0001f,
                upperLength + lowerLength - 0.0001f);
            var direction = toTarget.normalized;
            // 保持动画原有的肘部弯曲侧，避免在武器移动时翻肘。
            var elbowSide = Vector3.ProjectOnPlane(lower.position - shoulder, direction);
            if (elbowSide.sqrMagnitude <= 0.000001f)
                elbowSide = Vector3.ProjectOnPlane(transform.forward, direction);
            if (elbowSide.sqrMagnitude <= 0.000001f) return;
            var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) /
                        (2f * distance);
            var height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            var desiredElbow = shoulder + direction * along + elbowSide.normalized * height;
            var handRotation = hand.rotation;
            upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, desiredElbow - shoulder) *
                             upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) *
                             lower.rotation;
            hand.rotation = handRotation;
        }

        public void PlayHurtFeedback()
        {
            if(arcadePresentationActive){if(!presentationAlive)return;PrepareArcadeDamage();arcadeDamage?.Hurt();}
            // 街机以闪红和飘字提示受伤，保持权威移动、瞄准与射击的连续表现。
            // 普通地图及独立角色预览仍沿用原有全身受击动作，死亡继续走存活切换入口。
            if (!arcadePresentationActive)
            {
                ResetWeaponAttackPresentation();
                if (animator != null)
                {
                    rangedAimSuppressionRemainingSeconds = HurtAimSuppressionSeconds;
                    UpdateWeaponAimAnimator(false);
                    animator.ResetTrigger(PistolFireAnimatorParameter);
                    animator.ResetTrigger(ShotgunFireAnimatorParameter);
                    animator.ResetTrigger(AttackAnimatorParameter);
                    animator.SetTrigger(HitAnimatorParameter);
                }
            }

            CaptureFeedbackColors();
            colorFeedbackRemainingSeconds = FeedbackDurationSeconds;
            ApplyFeedbackColor(HurtFeedbackColor);
        }

        public void SetFlashlightActive(bool active)
        {
            if (flashlight != null)
            {
                flashlight.enabled = active;
            }
        }

        public void SetFlashlightDirection(Vector3 direction)
        {
            if (flashlight == null)
            {
                return;
            }

            direction.y = 0f;
            if (!IsFinite(direction) || direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var horizontal = direction.normalized;
            var downward = Mathf.Tan(flashlightDownwardAngleDegrees * Mathf.Deg2Rad);
            var worldDirection = (horizontal + Vector3.down * downward).normalized;
            flashlight.transform.rotation = Quaternion.LookRotation(worldDirection, Vector3.up);
        }

        public void TickCombatFeedback(float deltaTime)
        {
            arcadeDamage?.Tick(deltaTime);
            GetComponent<CharacterWeaponPresenter>()?.TickUpperBodyGunFire(deltaTime);
            if (singleAttackSpeedResetRemainingSeconds > 0f)
            {
                singleAttackSpeedResetRemainingSeconds -= deltaTime;
                if (singleAttackSpeedResetRemainingSeconds <= 0f) SetAttackPlaybackSpeed(1f);
            }
            if (rangedAimSuppressionRemainingSeconds > 0f)
            {
                rangedAimSuppressionRemainingSeconds -= deltaTime;
                if (rangedAimSuppressionRemainingSeconds <= 0f)
                {
                    rangedAimSuppressionRemainingSeconds = 0f;
                    ApplyRequestedWeaponAim();
                }
            }

            if (scaleFeedbackRemainingSeconds > 0f)
            {
                scaleFeedbackRemainingSeconds -= deltaTime;
                if (scaleFeedbackRemainingSeconds <= 0f)
                {
                    transform.localScale = baseScale;
                }
            }

            if (colorFeedbackRemainingSeconds > 0f)
            {
                colorFeedbackRemainingSeconds -= deltaTime;
                if (colorFeedbackRemainingSeconds <= 0f)
                {
                    RestoreFeedbackColors();
                }
            }

            if (weaponSwingRemainingSeconds > 0f)
            {
                weaponSwingRemainingSeconds -= deltaTime;
                if (weaponSwingRemainingSeconds <= 0f && weaponSwingRoot != null)
                {
                    weaponSwingRoot.localRotation = weaponSwingBaseRotation;
                }
            }

            if (projectileRecoilRemainingSeconds > 0f && rangedWeaponAttachRoot != null)
            {
                projectileRecoilRemainingSeconds -= deltaTime;
                var progress = 1f - Mathf.Clamp01(
                    projectileRecoilRemainingSeconds / ProjectileRecoilDurationSeconds);
                rangedWeaponAttachRoot.localPosition = Vector3.Lerp(
                    projectileRecoilBasePosition + ProjectileRecoilOffset,
                    projectileRecoilBasePosition,
                    progress);
                if (projectileRecoilRemainingSeconds <= 0f)
                {
                    rangedWeaponAttachRoot.localPosition = projectileRecoilBasePosition;
                }
            }

        }

        /// <summary>接收控制器已经结算出的水平移动速度，只驱动 Humanoid 表现。</summary>
        public void SetMovementSpeed(float worldUnitsPerSecond)
        {
            if (animator != null)
            {
                animator.SetFloat(MoveSpeedAnimatorParameter, SwimmingPresentation?0:Mathf.Max(0f, worldUnitsPerSecond));
            }
        }

        /// <summary>转发体力系统结算后的实际加速状态；本视图不决定是否允许加速。</summary>
        public void SetSprinting(bool value)
        {
            GetComponent<CharacterWeaponPresenter>()?.SetSprinting(value);
        }

        /// <summary>正式移动端口传入世界位移方向；举弓时按角色本地方向选择对应 Run 片段。</summary>
        public void SetMovementDirection(Vector3 worldMotion)
        {
            if(SwimmingPresentation)return;
            var presenter = GetComponent<CharacterWeaponPresenter>();
            if (presenter != null)
            {
                presenter.SetMovementDirection(worldMotion);
                return;
            }
            if (animator == null || animator.runtimeAnimatorController != bowAnimatorController) return;
            worldMotion.y = 0f;
            if (worldMotion.sqrMagnitude <= 0.000001f) return;
            var local = transform.InverseTransformDirection(worldMotion.normalized);
            var direction = Mathf.Abs(local.x) > Mathf.Abs(local.z)
                ? local.x > 0f ? 3 : 2
                : local.z >= 0f ? 0 : 1;
            if (direction == bowRunDirection) return;
            var clip = direction == 0 ? bowWalkForwardClip : direction == 1 ? bowWalkBackClip :
                direction == 2 ? bowWalkLeftClip : bowWalkRightClip;
            bowAnimatorController["HumanM@Run01_Forward"] = clip;
            bowRunDirection = direction;
        }

        private void OnDisable()
        {
            ResetHerdImpact();
            arcadeDamage?.Reset();
            RestoreFeedbackColors();
            colorFeedbackRemainingSeconds = 0f;
            arcadeProtection?.SetActive(false);
            ResetWeaponAttackPresentation();
            SetSprinting(false);
            SetMovementSpeed(0f);
            SetMovementDirection(Vector3.zero);
        }

        public bool Validate(out string error)
        {
            if (CharacterController == null)
            {
                error = "玩家缺少 CharacterController";
                return false;
            }

            if (cameraProfile == null)
            {
                error = "玩家缺少固定俯视相机配置";
                return false;
            }

            if (tiliRingCanvas == null)
            {
                error = "缺少玩家体力环画布";
                return false;
            }

            if (tiliRingFill == null)
            {
                error = "缺少玩家体力环填充图";
                return false;
            }

            if (meleeWeaponAttachRoot == null)
            {
                error = "玩家缺少近战武器挂点";
                return false;
            }

            if (rangedWeaponAttachRoot == null)
            {
                error = "玩家缺少投射武器挂点";
                return false;
            }

            if (flashlight == null || flashlight.type != LightType.Spot || !flashlight.transform.IsChildOf(transform))
            {
                error = "玩家缺少有效的手电筒聚光灯";
                return false;
            }

            if (damageNumberEmitter == null)
            {
                error = "玩家缺少伤害飘字发射器";
                return false;
            }

            if (!damageNumberEmitter.Validate(out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void HideLegacyWeaponObjects()
        {
            if (shortSwordObject != null)
            {
                shortSwordObject.SetActive(false);
            }

            if (bowObject != null)
            {
                bowObject.SetActive(false);
            }
        }

        private void TriggerAttackAnimation()
        {
            if (animator == null)
            {
                return;
            }

            animator.ResetTrigger(HitAnimatorParameter);
            animator.SetTrigger(AttackAnimatorParameter);
        }

        /// <summary>参数只作用于共享 Attack 状态；旧控制器没有该参数时保留原速且不产生警告。</summary>
        private void SetAttackPlaybackSpeed(float speed)
        {
            if (animator == null) return;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash != AttackPlaybackSpeedAnimatorParameter ||
                    parameter.type != AnimatorControllerParameterType.Float)
                    continue;
                animator.SetFloat(parameter.nameHash, speed);
                return;
            }
        }

        /// <summary>切装或既有中断发生时撤销攻击速度及旧触发，不抢占受击和死亡状态。</summary>
        internal void ResetWeaponAttackPresentation(bool switchingWeapon = false)
        {
            GetComponent<CharacterWeaponPresenter>()?.ClearUpperBodyGunFire();
            singleAttackSpeedResetRemainingSeconds = 0f;
            SetAttackPlaybackSpeed(1f);
            if (animator != null) animator.ResetTrigger(AttackAnimatorParameter);
            if (switchingWeapon && animator != null && presentationAlive &&
                GetComponent<CharacterWeaponPresenter>()?.Current?.Animations?.Attack?.SingleAttackDurationSeconds > 0f)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                var next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : default;
                if (state.IsName("Attack") || next.IsName("Attack"))
                    animator.Play("Base Layer.Idle", 0, 0f);
            }
            weaponSwingRemainingSeconds = 0f;
            if (weaponSwingRoot != null) weaponSwingRoot.localRotation = weaponSwingBaseRotation;
        }

        /// <summary>仅在一次后坐结束后重采样基准，避免连射时累积位置偏移。</summary>
        private void CaptureProjectileRecoilBase()
        {
            if (rangedWeaponAttachRoot == null ||
                projectileRecoilBaseCaptured && projectileRecoilRemainingSeconds > 0f)
            {
                return;
            }

            projectileRecoilBasePosition = rangedWeaponAttachRoot.localPosition;
            projectileRecoilBaseCaptured = true;
        }

        private void ResetProjectileRecoil()
        {
            projectileRecoilRemainingSeconds = 0f;
            if (projectileRecoilBaseCaptured && rangedWeaponAttachRoot != null)
            {
                rangedWeaponAttachRoot.localPosition = projectileRecoilBasePosition;
            }
        }

        /// <summary>装备、死亡与受击共用一个上半身层开关；同一时刻只启用一种枪械姿态。</summary>
        private void ApplyRequestedWeaponAim()
        {
            UpdateWeaponAimAnimator(!sharedWeaponAnimationActive && presentationAlive &&
                                    rangedAimSuppressionRemainingSeconds <= 0f);
        }

        /// <summary>共享完整动作期间关闭旧上身枪械层及旧程序后坐。</summary>
        public void SetSharedWeaponAnimationActive(bool active)
        {
            if (sharedWeaponAnimationActive == active) return;
            sharedWeaponAnimationActive = active;
            if (active && animator != null)
            {
                animator.ResetTrigger(PistolFireAnimatorParameter);
                animator.ResetTrigger(ShotgunFireAnimatorParameter);
                projectileBodyRecoilEndTime = 0f;
                projectileRecoilRemainingSeconds = 0f;
            }
            ApplyRequestedWeaponAim();
        }

        private void UpdateWeaponAimAnimator(bool active)
        {
            if (animator != null)
            {
                animator.SetBool(RangedWeaponAnimatorParameter,
                    active && activeGripPose == PlayerWeaponGripPose.Rifle);
                animator.SetBool(PistolWeaponAnimatorParameter,
                    active && activeGripPose == PlayerWeaponGripPose.Pistol);
                animator.SetBool(ShotgunWeaponAnimatorParameter, active && activeShotgun);
            }
        }

        /// <summary>把表现发射点挂到当前模型枪口；切装时重新绑定，不改变任何射击规则。</summary>
        private void ConfigureProjectileFirePoint(Vector3 localPosition)
        {
            if (projectileFirePoint == null) return;
            var parent = activeArcadeWeaponVisual != null
                ? activeArcadeWeaponVisual.transform : rangedWeaponAttachRoot;
            if (parent != null && projectileFirePoint.parent != parent)
                projectileFirePoint.SetParent(parent, false);
            projectileFirePoint.localPosition = localPosition;
            projectileFirePoint.localRotation = Quaternion.identity;
        }

        /// <summary>共享武器成为可见模型后，枪口特效和抛射表现直接跟随其枪管。</summary>
        internal void SetSharedProjectileFirePoint(Transform sharedVisual, Vector3 localPosition)
        {
            if (projectileFirePoint == null || sharedVisual == null) return;
            if (projectileFirePoint.parent != sharedVisual)
                projectileFirePoint.SetParent(sharedVisual, false);
            projectileFirePoint.localPosition = localPosition;
            projectileFirePoint.localRotation = Quaternion.identity;
        }

        /// <summary>把武器根挂到右手骨，并按武器类型设置握持朝向；Animator 缺失时保留原灰盒挂点。</summary>
        private void BindWeaponAttachRoots()
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                return;
            }

            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand == null)
            {
                return;
            }

            BindWeaponAttachRoot(meleeWeaponAttachRoot, rightHand, Quaternion.Euler(0f, 180f, 0f));
            if (rangedWeaponAttachRoot != meleeWeaponAttachRoot)
            {
                BindWeaponAttachRoot(rangedWeaponAttachRoot, rightHand, RangedWeaponHandRotation);
            }
        }

        private static void BindWeaponAttachRoot(Transform attachRoot, Transform hand, Quaternion localRotation)
        {
            if (attachRoot != null && attachRoot.parent != hand)
            {
                attachRoot.SetParent(hand, false);
                attachRoot.localPosition = Vector3.zero;
                attachRoot.localRotation = localRotation;
                attachRoot.localScale = Vector3.one;
            }
        }

        private static void FaceTransform(Transform target, Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (target == null)
            {
                return;
            }

            var direction = target.position - cameraPosition;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            target.rotation = Quaternion.LookRotation(direction, cameraUp);
        }

        private void EnsureBaseScale()
        {
            if (scaleFeedbackRemainingSeconds <= 0f)
            {
                baseScale = transform.localScale;
            }
        }

        private void CaptureWeaponSwingBase()
        {
            if (weaponSwingBaseCaptured || weaponSwingRoot == null)
            {
                return;
            }

            weaponSwingBaseRotation = weaponSwingRoot.localRotation;
            weaponSwingBaseCaptured = true;
        }

        /// <summary>缓存人物身体及服装；武器、保护线和 HUD 不闪红，切装无需重扫，隐藏服装显示后才参与。</summary>
        private void PrepareFeedbackRenderers()
        {
            if (feedbackTargetsPrepared) return;
            feedbackRenderers = GetComponentsInChildren<Renderer>(true);
            feedbackOriginalBlocks = new MaterialPropertyBlock[feedbackRenderers.Length];
            feedbackTinted = new bool[feedbackRenderers.Length];
            for (int i = 0; i < feedbackRenderers.Length; i++)
            {
                var target = feedbackRenderers[i];
                // 正式人物只处理骨骼蒙皮；无 Animator 的简单角色沿用网格反馈。
                if ((target is SkinnedMeshRenderer || animator == null && target is MeshRenderer) &&
                    target.GetComponentInParent<Canvas>() == null)
                    feedbackOriginalBlocks[i] = new MaterialPropertyBlock();
            }
            feedbackPropertyBlock ??= new MaterialPropertyBlock();
            feedbackTargetsPrepared = true;
        }

        /// <summary>重复受击延长闪红时间，不把上次红色覆盖误记为角色原色。</summary>
        private void CaptureFeedbackColors()
        {
            PrepareFeedbackRenderers();
            for (var i = 0; i < feedbackRenderers.Length; i++)
            {
                var target = feedbackRenderers[i];
                if (feedbackTinted[i] || feedbackOriginalBlocks[i] == null || target == null ||
                    !target.enabled || !target.gameObject.activeInHierarchy) continue;
                target.GetPropertyBlock(feedbackOriginalBlocks[i]);
                feedbackTinted[i] = true;
            }
        }

        private void ApplyFeedbackColor(Color color)
        {
            for (var i = 0; i < feedbackRenderers.Length; i++)
            {
                if (feedbackTinted[i]) SetRendererColor(feedbackRenderers[i], color);
            }
        }

        private void RestoreFeedbackColors()
        {
            for (var i = 0; i < feedbackRenderers.Length; i++)
            {
                if (!feedbackTinted[i]) continue;
                if (feedbackRenderers[i] != null)
                    feedbackRenderers[i].SetPropertyBlock(feedbackOriginalBlocks[i].isEmpty ? null : feedbackOriginalBlocks[i]);
                feedbackTinted[i] = false;
            }
        }

        private void SetRendererColor(Renderer targetRenderer, Color color)
        {
            if (targetRenderer == null)
            {
                return;
            }

            var propertyBlock = FeedbackPropertyBlock;
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_Color", color);
            propertyBlock.SetColor("_BaseColor", color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        private MaterialPropertyBlock FeedbackPropertyBlock
        {
            get
            {
                if (feedbackPropertyBlock == null)
                {
                    feedbackPropertyBlock = new MaterialPropertyBlock();
                }

                return feedbackPropertyBlock;
            }
        }
    }
}
