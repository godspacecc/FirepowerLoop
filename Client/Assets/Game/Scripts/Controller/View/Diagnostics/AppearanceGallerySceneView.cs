using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;
using QFramework;

namespace Game
{
    /// <summary>
    /// 造型库场景专用的预览 View：只实例化正式 Prefab、采样动画和显示 UI，
    /// 不调用战斗、装备事务或存档入口。场景卸载时一并释放预览实例与 PlayableGraph。
    /// </summary>
    public sealed class AppearanceGallerySceneView : MonoBehaviour
    {
        private static readonly int PlayerRunState = Animator.StringToHash("Base Layer.Run");
        private static readonly int PlayerIdleState = Animator.StringToHash("Base Layer.Idle");
        private static readonly int PlayerRifleAimState = Animator.StringToHash("Ranged Upper Body.Rifle Aim");
        private static readonly int PlayerShotgunFireState = Animator.StringToHash("Ranged Upper Body.Shotgun Fire");
        private static readonly int PlayerPistolAimState = Animator.StringToHash("Pistol Upper Body.Pistol Aim");
        private static readonly int PlayerPistolFireState = Animator.StringToHash("Pistol Upper Body.Pistol Fire");
        private static readonly int PlayerPistolGripState = Animator.StringToHash("Pistol Hand Grip.Pistol Grip");
        private const int PlayerPistolGripLayer = 3;
        private static readonly int PlayerEmptyUpperBodyState = Animator.StringToHash("Ranged Upper Body.Empty");
        private static readonly int PlayerRangedWeaponParameter = Animator.StringToHash("RangedWeaponActive");
        private static readonly int PlayerPistolWeaponParameter = Animator.StringToHash("PistolWeaponActive");
        private const string GalleryBowResourceName = "GrayboxBow";
        // 预览角色面朝 +Z，近景相机从 +Z 方向观察其正面。
        private const float DefaultCameraYawDegrees = 180f;

        [SerializeField] private GameObject[] characterPrefabs = Array.Empty<GameObject>();
        [SerializeField] private EnemyWeaponVisualCatalog weaponVisualCatalog;
        [SerializeField] private CharacterWeaponCatalog characterWeaponCatalog;
        [Tooltip("与正式装备外观配置一致的灰盒弓 Prefab；仅在测试场景实例化。")]
        [SerializeField] private GameObject bowAppearancePrefab;
        [SerializeField] private Transform previewRoot;
        [SerializeField] private Renderer previewStage;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Dropdown characterDropdown;
        [SerializeField] private Dropdown variantDropdown;
        [SerializeField] private Dropdown equipmentDropdown;
        [SerializeField] private Dropdown animationDropdown;
        [Tooltip("造型库专用的玩家候选动画；只补充预览列表，不参与正式 Animator 状态。")]
        [SerializeField] private AnimationClip[] playerPreviewAnimations = Array.Empty<AnimationClip>();
        [SerializeField] private Slider timeline;
        [SerializeField] private Text timelineText;
        [SerializeField] private Text equipmentHintText;
        [SerializeField] private Text playPauseText;
        [SerializeField] private Text cameraModeText;
        [SerializeField] private Button playPauseButton;
        [SerializeField] private Button cameraModeButton;
        [SerializeField] private Button resetCameraButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private AppearanceGalleryPoseTuningPanel poseTuningPanel;

        private readonly List<AnimationClip> animationClips = new List<AnimationClip>();
        private readonly List<CharacterAnimationDefinition> animationDefinitions = new List<CharacterAnimationDefinition>();
        private readonly List<GameObject> availableCharacterPrefabs = new List<GameObject>();
        private AppearanceGallerySceneFlowSystem flow;
        private GameObject characterInstance;
        private Animator animator;
        private PlayerView playerView;
        private GameObject bowAppearanceInstance;
        private PlayerWeaponAppearanceDefinition bowAppearance;
        private bool hasBowAppearance;
        private EnemyView enemyView;
        private CharacterWeaponPresenter weaponPresenter;
        private string selectedSharedWeaponId = string.Empty;
        /// <summary>微调页排除空手项；目录索引仍保持原来的一基选择协议。</summary>
        private bool tuningWeaponSelection;
        private PlayableGraph graph;
        private AnimationClipPlayable clipPlayable;
        private AnimatorControllerPlayable controllerPlayable;
        private AnimationClip selectedClip;
        private CharacterAnimationDefinition selectedDefinition;
        private struct SharedPreviewState
        {
            public string WeaponId;
            public CharacterAnimationDefinition Animation;
            public float Seconds;
            public bool Playing;
        }
        // 正式组合模式复用玩家 Animator 的现有层与状态；仅候选动作由原始 Clip 独立采样。
        private AnimationClip compositeRunClip;
        private AnimationClip compositeAimClip;
        /// <summary>当前玩家预览武器的稳定资源名；null 表示无装备，仅用于筛选造型库动作。</summary>
        private string selectedPlayerWeaponResourceName;
        private bool playerCompositePreview;
        private float compositePlaybackSeconds;
        private float clipTimeSeconds;
        private bool playing = true;
        private bool topDown;
        private float yawDegrees = DefaultCameraYawDegrees;
        private float pitchDegrees = 18f;
        private float cameraDistance = 5f;
        private Vector2 lastPointer;
        private float lastPinchDistance;
        // 手势归属只在按下时确定；从面板滑入预览区不能中途接管相机。
        private bool mouseOrbitStartedInPreview;
        private bool firstTouchStartedInPreview;
        private bool secondTouchStartedInPreview;
        private bool firstTouchObserved;
        private bool secondTouchObserved;
        private readonly PointerUiBlocker previewUiBlocker = new PointerUiBlocker();
        /// <summary>场景序列化的原始视口；安全区变化时始终从原值映射，避免多次缩小。</summary>
        private Rect authoredCameraViewport;
        private bool cameraViewportCaptured;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        private void Awake()
        {
            RefreshPreviewViewport();
            characterDropdown.onValueChanged.AddListener(SelectCharacter);
            variantDropdown.onValueChanged.AddListener(SelectVariant);
            equipmentDropdown.onValueChanged.AddListener(SelectEquipment);
            animationDropdown.onValueChanged.AddListener(SelectAnimation);
            timeline.onValueChanged.AddListener(Scrub);
            playPauseButton.onClick.AddListener(TogglePlayback);
            cameraModeButton.onClick.AddListener(ToggleCamera);
            resetCameraButton.onClick.AddListener(ResetCamera);
            exitButton.onClick.AddListener(() => { if (flow != null) flow.CloseAsync().Forget(); });
            if (poseTuningPanel != null) poseTuningPanel.TuningModeChanged += SetTuningWeaponSelection;
        }

        /// <summary>切页时按稳定武器身份重建选项；空手进入微调时自动选择目录第一把武器。</summary>
        private void SetTuningWeaponSelection(bool tuning)
        {
            if (tuningWeaponSelection == tuning) return;
            var previous = CaptureSharedState();
            tuningWeaponSelection = tuning;
            if (characterWeaponCatalog != null && weaponPresenter != null)
                RefreshEquipmentOptionsWithState(previous);
        }

        /// <summary>附加场景加载并验证后调用一次；选项来自场景引用和正式 Animator/武器外观。</summary>
        public void Initialize(AppearanceGallerySceneFlowSystem owner, IRuntimeConfigProvider configProvider)
        {
            flow = owner;
            if (poseTuningPanel != null && owner != null)
                poseTuningPanel.Initialize(owner.GetSystem<WeaponPoseSettingsSystem>(), characterWeaponCatalog);
            // 正式与预览读取同一条装备外观定义，不复制弓的挂点和局部姿态数值。
            hasBowAppearance = bowAppearancePrefab != null && configProvider != null &&
                               configProvider.TryGetPlayerWeaponAppearance(ItemIds.Bow, out bowAppearance, out _);
            var names = new List<string>();
            availableCharacterPrefabs.Clear();
            foreach (var prefab in characterPrefabs)
                if (prefab != null)
                {
                    availableCharacterPrefabs.Add(prefab);
                    names.Add(GetCharacterDisplayName(prefab.name));
                }
            characterDropdown.ClearOptions();
            characterDropdown.AddOptions(names);
            if (names.Count > 0) SelectCharacter(0);
            UpdateCamera();
        }

        private void SelectCharacter(int index)
        {
            // 先保存动作身份与时间，再释放旧图；下拉选项刷新不得覆盖这份状态。
            var sharedState = CaptureSharedState();
            ReleaseAnimation();
            if (characterInstance != null)
            {
                characterInstance.SetActive(false);
                DestroyPreviewObject(characterInstance);
            }
            characterInstance = null;
            playerView = null;
            bowAppearanceInstance = null;
            enemyView = null;
            weaponPresenter = null;
            selectedPlayerWeaponResourceName = null;
            if (index < 0 || index >= availableCharacterPrefabs.Count) return;

            var staging = new GameObject("GalleryPresentationStaging");
            staging.SetActive(false);
            characterInstance = Instantiate(availableCharacterPrefabs[index], staging.transform);
            characterInstance.SetActive(false);
            characterInstance.transform.SetParent(previewRoot, false);
            DestroyPreviewObject(staging);
            characterInstance.transform.localPosition = Vector3.zero;
            characterInstance.transform.localRotation = Quaternion.identity;
            foreach (var child in characterInstance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            enemyView = characterInstance.GetComponent<EnemyView>();
            enemyView?.BeginPresentationUse();
            // 预览对象不参与地图导航或碰撞；新用途复位须先于这些禁用操作。
            foreach (var agent in characterInstance.GetComponentsInChildren<NavMeshAgent>(true)) agent.enabled = false;
            foreach (var collider in characterInstance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            playerView = characterInstance.GetComponent<PlayerView>();
            playerView?.SetManualWeaponPoseSampling(true);
            weaponPresenter = characterInstance.GetComponent<CharacterWeaponPresenter>();
            weaponPresenter?.SetManualPoseSampling(true);
            animator = weaponPresenter != null ? weaponPresenter.BodyAnimator : playerView != null ? playerView.Animator :
                characterInstance.GetComponentInChildren<Animator>(true);
            characterInstance.SetActive(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                // 临时实例可能不在编辑器当前镜头内；手动采样仍须更新骨骼供展台和挂点检查。
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            RefreshVariantOptions();
            RefreshEquipmentOptionsWithState(sharedState);
            if (weaponPresenter == null) RefreshAnimationOptions();
        }

        private void RefreshVariantOptions()
        {
            int count = enemyView != null ? Mathf.Max(1, enemyView.PresentationVariantCount) : 1;
            var names = new List<string>(count);
            for (int i = 0; i < count; i++)
                names.Add(GetVariantDisplayName(i, enemyView?.GetPresentationVariantName(i)));
            variantDropdown.ClearOptions();
            variantDropdown.AddOptions(names);
            variantDropdown.SetValueWithoutNotify(0);
            SelectVariant(0);
            RefreshVariantSelectionPresentation();
        }

        /// <summary>外观选项刷新后同步展示：单项只显示名称，多项恢复选择背景和箭头，不改变当前外观。</summary>
        private void RefreshVariantSelectionPresentation()
        {
            var canChoose = variantDropdown.options.Count > 1;
            variantDropdown.interactable = canChoose;
            variantDropdown.targetGraphic.enabled = canChoose;
            variantDropdown.transform.Find("Arrow").gameObject.SetActive(canChoose);
        }

        private void SelectVariant(int index)
        {
            enemyView?.SetPresentationVariant(index);
            if (selectedClip != null) AlignVisibleSkinToStage(characterInstance, previewStage);
        }

        private SharedPreviewState CaptureSharedState() => new SharedPreviewState
        {
            WeaponId = selectedSharedWeaponId,
            Animation = selectedDefinition,
            Seconds = clipTimeSeconds,
            Playing = playing
        };

        private void RefreshEquipmentOptionsWithState(SharedPreviewState sharedState)
        {
            if (characterWeaponCatalog != null)
            {
                var sharedNames = new List<string>();
                if (!tuningWeaponSelection) sharedNames.Add("无武器");
                for (var index = 0; index < characterWeaponCatalog.Count; index++)
                    sharedNames.Add(characterWeaponCatalog.Get(index).DisplayName);
                equipmentDropdown.ClearOptions();
                equipmentDropdown.AddOptions(sharedNames);
                var selectedIndex = 0;
                if (!string.IsNullOrEmpty(sharedState.WeaponId))
                    for (var i = 0; i < characterWeaponCatalog.Count; i++)
                        if (characterWeaponCatalog.Get(i)?.Id == sharedState.WeaponId) selectedIndex = i + 1;
                if (tuningWeaponSelection && selectedIndex == 0 && characterWeaponCatalog.Count > 0)
                    selectedIndex = 1;
                equipmentDropdown.SetValueWithoutNotify(tuningWeaponSelection ? Mathf.Max(0, selectedIndex - 1) : selectedIndex);
                SelectSharedEquipment(selectedIndex, sharedState);
                return;
            }
            var names = new List<string> { "无装备" };
            if (playerView != null)
            {
                for (int i = 0; i < playerView.ArcadeWeaponVisualCount; i++)
                    names.Add(GetEquipmentDisplayName(playerView.GetArcadeWeaponVisualName(i)));
                if (hasBowAppearance) names.Add("灰盒弓（仅预览）");
            }
            else if (enemyView != null)
            {
                // 选项过滤、右手挂载与替换件生命周期都由正式 EnemyView 负责。
                enemyView.ConfigureWeaponVisualCatalog(weaponVisualCatalog);
                names.Clear();
                for (int i = 0; i < enemyView.WeaponVisualOptionCount; i++)
                    names.Add(GetEquipmentDisplayName(enemyView.GetWeaponVisualOptionName(i)));
            }
            equipmentDropdown.ClearOptions();
            equipmentDropdown.AddOptions(names);
            int initial = enemyView != null ? 1 : 0;
            equipmentDropdown.SetValueWithoutNotify(initial);
            SelectEquipment(initial);
        }

        private void RefreshEquipmentOptions() => RefreshEquipmentOptionsWithState(CaptureSharedState());

        private void SelectEquipment(int index)
        {
            if (characterWeaponCatalog != null)
            {
                SelectSharedEquipment(tuningWeaponSelection ? index + 1 : index, CaptureSharedState());
                return;
            }
            if (equipmentHintText != null)
                equipmentHintText.text = "仅预览正式素材，不修改角色数据";
            if (playerView != null)
            {
                bool bowPreview = hasBowAppearance &&
                                  index == playerView.ArcadeWeaponVisualCount + 1;
                if (bowPreview)
                {
                    if (!ShowBowAppearance()) return;
                    selectedPlayerWeaponResourceName = GalleryBowResourceName;
                    RefreshAnimationOptions();
                    return;
                }
                string resourceName = index > 0 ? playerView.GetArcadeWeaponVisualName(index - 1) : null;
                if (bowAppearanceInstance != null) bowAppearanceInstance.SetActive(false);
                playerView.SetWeaponPresentation(WeaponSlot.Melee, false, false);
                selectedPlayerWeaponResourceName = resourceName;
                // 与正式控制器共用姿态选择；造型库只手动采样动画，不改变装备状态。
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(playerView, resourceName, false, Vector3.zero);
                // 武器变化后重新建立合法动作集合，同时释放可能已不兼容的采样图。
                RefreshAnimationOptions();
                return;
            }
            if (enemyView == null || !enemyView.SetWeaponPresentation(index)) return;
            if (equipmentHintText != null && enemyView.IsTrialWeaponOption(index))
                equipmentHintText.text = "仅外观试装；敌人沿用原攻击动画，不会开枪";
        }

        private void SelectSharedEquipment(int index, SharedPreviewState previous)
        {
            if (characterInstance == null || weaponPresenter == null || index < 0 ||
                index > characterWeaponCatalog.Count) return;
            ReleaseAnimation();
            playerView?.SetSharedWeaponAnimationActive(true);
            playerView?.SetWeaponPresentation(WeaponSlot.Melee, false, false);
            enemyView?.SetWeaponPresentation(0);
            var entry = index == 0 ? null : characterWeaponCatalog.Get(index - 1);
            selectedSharedWeaponId = entry?.Id ?? string.Empty;
            if (!weaponPresenter.SetWeapon(entry))
            {
                ClearSharedAnimationOptions();
                if (equipmentHintText != null) equipmentHintText.text = weaponPresenter.LastError;
                return;
            }
            if (equipmentHintText != null) equipmentHintText.text = "片段预览，实战状态过渡需在游戏中观察";
            RefreshSharedAnimationOptions(previous);
            if (poseTuningPanel != null) poseTuningPanel.Bind(entry, selectedDefinition);
        }

        private void ClearSharedAnimationOptions()
        {
            animationDefinitions.Clear();
            animationClips.Clear();
            animationDropdown.ClearOptions();
            animationDropdown.SetValueWithoutNotify(-1);
            animationDropdown.interactable = false;
            timeline.interactable = false;
            timeline.SetValueWithoutNotify(0f);
            RefreshTimelineLabel();
        }

        /// <summary>按资源身份恢复兼容片段；旧动作不在默认集合时仅追加临时延续选项。</summary>
        private void RefreshSharedAnimationOptions(SharedPreviewState previous)
        {
            animationDefinitions.Clear();
            animationClips.Clear();
            var names = new List<string>();
            var seen = new HashSet<CharacterAnimationDefinition>();
            var set = weaponPresenter.Current?.Animations ?? weaponPresenter.Profile?.UnarmedAnimations;
            if (set != null)
            {
                AddSharedOption(set.Idle, seen, names);
                AddSharedOption(set.MoveForward, seen, names);
                AddSharedOption(set.MoveBackward, seen, names);
                AddSharedOption(set.MoveLeft, seen, names);
                AddSharedOption(set.MoveRight, seen, names);
                AddSharedOption(set.Attack, seen, names);
                if (set.Extras != null)
                    foreach (var extra in set.Extras) AddSharedOption(extra, seen, names);
            }
            var incompatibility = string.Empty;
            var priorCompatible = previous.Animation != null &&
                                  weaponPresenter.IsAnimationCompatible(previous.Animation, out incompatibility);
            if (priorCompatible && seen.Add(previous.Animation))
            {
                animationDefinitions.Add(previous.Animation);
                animationClips.Add(previous.Animation.Clip);
                names.Add("延续：" + GetAnimationDefinitionDisplayName(previous.Animation));
            }
            animationDropdown.ClearOptions();
            animationDropdown.AddOptions(names);
            if (animationDefinitions.Count == 0)
            {
                ClearSharedAnimationOptions();
                if (equipmentHintText != null) equipmentHintText.text = "当前骨架与武器没有可预览动作";
                return;
            }
            var selected = priorCompatible ? animationDefinitions.IndexOf(previous.Animation) : 0;
            animationDropdown.SetValueWithoutNotify(selected);
            animationDropdown.interactable = true;
            if (!priorCompatible && previous.Animation != null && equipmentHintText != null)
                equipmentHintText.text = "原动作不兼容：" + incompatibility;
            StartSharedAnimation(animationDefinitions[selected], priorCompatible ? previous.Seconds : 0f,
                previous.Playing);
        }

        private void AddSharedOption(CharacterAnimationDefinition definition,
            HashSet<CharacterAnimationDefinition> seen, List<string> names)
        {
            if (definition == null || !weaponPresenter.IsAnimationCompatible(definition, out _) ||
                !seen.Add(definition)) return;
            animationDefinitions.Add(definition);
            animationClips.Add(definition.Clip);
            names.Add(GetAnimationDefinitionDisplayName(definition));
        }

        private void StartSharedAnimation(CharacterAnimationDefinition definition, float seconds, bool resume)
        {
            ReleaseAnimation();
            selectedDefinition = definition;
            selectedClip = definition.Clip;
            playing = resume;
            RefreshPlayPauseLabel();
            clipTimeSeconds = selectedClip.isLooping ? Mathf.Repeat(seconds, selectedClip.length) :
                Mathf.Clamp(seconds, 0f, selectedClip.length);
            graph = PlayableGraph.Create("AppearanceGallerySharedPreview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clipPlayable = AnimationClipPlayable.Create(graph, selectedClip);
            AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(clipPlayable);
            graph.Play();
            clipPlayable.SetTime(clipTimeSeconds);
            graph.Evaluate(0f);
            weaponPresenter.ApplyOffhandGrip(definition,
                selectedClip.length > 0f ? clipTimeSeconds / selectedClip.length : 0f);
            AlignVisibleSkinToStage(characterInstance, previewStage);
            timeline.interactable = true;
            timeline.SetValueWithoutNotify(selectedClip.length > 0f ? clipTimeSeconds / selectedClip.length : 0f);
            RefreshTimelineLabel();
        }

        /// <summary>用正式 Prefab、配置与 PlayerView 挂点创建临时弓；失败时保留已有预览选择。</summary>
        private bool ShowBowAppearance()
        {
            if (bowAppearanceInstance == null)
            {
                if (!playerView.TryGetWeaponAttachRoot(bowAppearance.AttachPoint, out var root)) return false;
                bowAppearanceInstance = Instantiate(bowAppearancePrefab, root);
                PlayerView.ApplyWeaponAppearancePose(bowAppearanceInstance.transform, bowAppearance);
                foreach (var child in bowAppearanceInstance.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = 31;
                foreach (var collider in bowAppearanceInstance.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
            }
            playerView.SetWeaponPresentation(WeaponSlot.Ranged, false, true);
            PlayerViewCtrl.ConfigureArcadeWeaponPresentation(playerView, null, false, Vector3.zero);
            bowAppearanceInstance.SetActive(true);
            return true;
        }

        private void RefreshAnimationOptions()
        {
            if (characterWeaponCatalog != null && weaponPresenter != null) return;
            // 用片段身份而非旧下拉索引恢复选择；过滤后索引可能指向完全不同的动作。
            var previousClip = selectedClip;
            var previousComposite = playerCompositePreview;
            animationClips.Clear();
            compositeRunClip = null;
            compositeAimClip = null;
            var names = new List<string>();
            var seen = new HashSet<AnimationClip>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    AddAnimationOption(clip, seen, names);
            }
            if (playerView != null)
                foreach (var clip in playerPreviewAnimations)
                    AddAnimationOption(clip, seen, names);
            if (playerView != null && PlayerViewCtrl.UsesRifleAimPose(selectedPlayerWeaponResourceName) &&
                animator != null && animator.layerCount > 1 &&
                animator.HasState(0, PlayerRunState) && animator.HasState(1, PlayerRifleAimState) &&
                animator.HasState(1, PlayerEmptyUpperBodyState))
            {
                foreach (var clip in animationClips)
                {
                    if (clip.name.EndsWith("Run01_Forward", StringComparison.Ordinal)) compositeRunClip = clip;
                    if (clip.name == "Rifle Idle Aiming" || clip.name == "RifleIdleAiming")
                        compositeAimClip = clip;
                }
                if (compositeRunClip != null && compositeAimClip != null)
                    names.Add("实战组合：跑动（按当前装备）");
            }
            animationDropdown.ClearOptions();
            animationDropdown.AddOptions(names);
            int selectedIndex = previousComposite
                ? compositeRunClip != null && compositeAimClip != null ? animationClips.Count : -1
                : animationClips.IndexOf(previousClip);
            if (selectedIndex < 0) selectedIndex = FindEquipmentIdleIndex();
            if (selectedIndex < 0 && animationClips.Count > 0) selectedIndex = 0;
            if (selectedIndex < 0) return;
            animationDropdown.SetValueWithoutNotify(selectedIndex);
            SelectAnimation(selectedIndex);
        }

        /// <summary>原动作不再适用时选择当前装备的待机；未配置专属待机则退回通用待机。</summary>
        private int FindEquipmentIdleIndex()
        {
            string idleName = selectedPlayerWeaponResourceName == "ArcadePistol" ? "PistolIdle" :
                selectedPlayerWeaponResourceName == GalleryBowResourceName ? "StandingAimIdle01" :
                PlayerViewCtrl.UsesRifleAimPose(selectedPlayerWeaponResourceName) ? "RifleIdleAiming" :
                "HumanM@Idle01";
            for (int index = 0; index < animationClips.Count; index++)
                if (animationClips[index].name == idleName) return index;
            for (int index = 0; index < animationClips.Count; index++)
                if (animationClips[index].name == "HumanM@Idle01") return index;
            return -1;
        }

        /// <summary>向预览列表追加唯一动画；显式候选不会反向修改正式 Animator。</summary>
        private void AddAnimationOption(AnimationClip clip, HashSet<AnimationClip> seen, List<string> names)
        {
            if (clip == null || !IsAnimationCompatibleWithCurrentEquipment(clip) || !seen.Add(clip)) return;
            animationClips.Add(clip);
            names.Add(selectedPlayerWeaponResourceName == "ArcadeScifiShotgun" &&
                      clip.name == "RifleIdleAiming" ? "霰弹枪瞄准待机" :
                selectedPlayerWeaponResourceName == "ArcadeScifiShotgun" &&
                clip.name == "RifleFire" ? "霰弹枪开火" : GetAnimationDisplayName(clip.name));
        }

        /// <summary>
        /// 按当前试装武器逐类筛选玩家动作；敌人的原配动作仍由其 Animator 提供。
        /// 未分类的新片段只在无装备时展示，避免误当作已验证的持武器动作。
        /// </summary>
        private bool IsAnimationCompatibleWithCurrentEquipment(AnimationClip clip)
        {
            if (playerView == null || clip == null) return true;
            if (clip.name == "HumanM@Idle01" || clip.name == "HumanM@Run01_Forward" ||
                clip.name == "HumanM@CombatDamage01" || clip.name == "HumanM@Death01")
                return true;
            if (clip.name == "HumanM@Attack1H01_R" || clip.name == "HumanM@Attack2H01" ||
                clip.name == "HumanM@CombatIdle2H01")
                return string.IsNullOrEmpty(selectedPlayerWeaponResourceName);
            if (clip.name == "PistolIdle" || clip.name == "PistolFire")
                return selectedPlayerWeaponResourceName == "ArcadePistol";
            if (clip.name == "Rifle Idle Aiming" || clip.name.StartsWith("Rifle", StringComparison.Ordinal))
                return PlayerViewCtrl.UsesRifleAimPose(selectedPlayerWeaponResourceName);
            if (clip.name == "StandingAimIdle01" || clip.name == "StandingDrawArrow" ||
                clip.name == "ShootingArrow" || clip.name == "BowAimWalkRightComposite" ||
                clip.name.StartsWith("StandingAimWalk", StringComparison.Ordinal))
                return selectedPlayerWeaponResourceName == GalleryBowResourceName;
            return string.IsNullOrEmpty(selectedPlayerWeaponResourceName);
        }

        /// <summary>正式手枪动作与跑动组合采样玩家 Animator 分层；候选片段保留原始采样。</summary>
        private void SelectAnimation(int index)
        {
            if (characterWeaponCatalog != null && weaponPresenter != null)
            {
                if (index >= 0 && index < animationDefinitions.Count)
                    StartSharedAnimation(animationDefinitions[index], 0f, true);
                return;
            }
            ReleaseAnimation();
            if (animator == null || index < 0) return;
            if (playerView != null && compositeRunClip != null && compositeAimClip != null &&
                index == animationClips.Count)
            {
                playing = true;
                RefreshPlayPauseLabel();
                playerCompositePreview = true;
                selectedClip = compositeRunClip;
                graph = PlayableGraph.Create("AppearanceGalleryPlayerComposite");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                controllerPlayable = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
                AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(controllerPlayable);
                graph.Play();
                SamplePlayerComposite();
                AlignVisibleSkinToStage(characterInstance, previewStage);
                timeline.SetValueWithoutNotify(0f);
                RefreshTimelineLabel();
                RefreshPlayerAnimationHint();
                return;
            }
            if (index >= animationClips.Count) return;
            playing = true;
            RefreshPlayPauseLabel();
            selectedClip = animationClips[index];
            if (playerView != null && selectedPlayerWeaponResourceName == "ArcadePistol" &&
                (selectedClip.name == "PistolIdle" || selectedClip.name == "PistolFire") &&
                animator.HasState(0, PlayerIdleState) &&
                animator.HasState(2, selectedClip.name == "PistolIdle"
                    ? PlayerPistolAimState : PlayerPistolFireState))
            {
                // 单段条目也要呈现正式基础待机＋手枪上半身层，避免预览与实战姿态分叉。
                playerCompositePreview = true;
                graph = PlayableGraph.Create("AppearanceGalleryPistolPresentation");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                controllerPlayable = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
                AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(controllerPlayable);
                graph.Play();
                clipTimeSeconds = 0f;
                compositePlaybackSeconds = 0f;
                SamplePlayerComposite();
                AlignVisibleSkinToStage(characterInstance, previewStage);
                timeline.SetValueWithoutNotify(0f);
                RefreshTimelineLabel();
                RefreshPlayerAnimationHint();
                return;
            }
            if (playerView != null && PlayerViewCtrl.UsesRifleAimPose(selectedPlayerWeaponResourceName) &&
                (selectedClip.name == "RifleIdleAiming" || selectedClip.name == "RifleFire") &&
                animator.HasState(0, PlayerIdleState) &&
                animator.HasState(1, PlayerRifleAimState))
            {
                // 长枪直接采样正式 Animator；PlayableGraph 的零时间求值不会可靠重置后坐骨骼。
                playerCompositePreview = true;
                clipTimeSeconds = 0f;
                compositePlaybackSeconds = 0f;
                SamplePlayerComposite();
                AlignVisibleSkinToStage(characterInstance, previewStage);
                timeline.SetValueWithoutNotify(0f);
                RefreshTimelineLabel();
                RefreshPlayerAnimationHint();
                return;
            }
            graph = PlayableGraph.Create("AppearanceGalleryPreview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clipPlayable = AnimationClipPlayable.Create(graph, selectedClip);
            AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(clipPlayable);
            graph.Play();
            clipTimeSeconds = 0f;
            graph.Evaluate(0f);
            playerView?.TickCombatFeedback(0f);
            ApplyPreviewGrip(selectedClip.name);
            AlignVisibleSkinToStage(characterInstance, previewStage);
            timeline.SetValueWithoutNotify(0f);
            RefreshTimelineLabel();
            RefreshPlayerAnimationHint();
        }

        private void RefreshPlayerAnimationHint()
        {
            if (playerView == null || equipmentHintText == null) return;
            if (!string.IsNullOrEmpty(selectedPlayerWeaponResourceName) && selectedClip != null &&
                (selectedClip.name == "HumanM@Idle01" || selectedClip.name == "HumanM@Run01_Forward"))
            {
                equipmentHintText.text = "通用基础动作仅供查看；不代表当前武器的正确握持姿势";
                return;
            }
            if (selectedPlayerWeaponResourceName == GalleryBowResourceName)
            {
                var formalBowClip = selectedClip != null &&
                    (selectedClip.name == "StandingAimIdle01" ||
                     selectedClip.name == "StandingAimWalkForward" ||
                     selectedClip.name == "StandingAimWalkBack" ||
                     selectedClip.name == "StandingAimWalkLeft" ||
                     selectedClip.name == "BowAimWalkRightComposite" ||
                     selectedClip.name == "ShootingArrow");
                equipmentHintText.text = formalBowClip
                    ? "当前弓动作已接入正式角色的四方向移动表现"
                    : "当前弓动作仅供姿态筛选；尚未接入正式角色";
                return;
            }
            if (selectedPlayerWeaponResourceName == "ArcadePistol" && playerCompositePreview)
            {
                equipmentHintText.text = "当前按正式玩家待机与手枪上半身动画预览";
                return;
            }
            if (PlayerViewCtrl.UsesRifleAimPose(selectedPlayerWeaponResourceName) && playerCompositePreview)
            {
                equipmentHintText.text = "当前按正式玩家待机与长枪上半身姿态预览";
                return;
            }
            equipmentHintText.text = animator != null &&
                                     animator.GetBool(PlayerRangedWeaponParameter) && !playerCompositePreview
                ? "当前为单段动画；选“实战组合”查看正式持枪姿态"
                : "仅预览正式素材，不修改角色数据";
        }

        /// <summary>
        /// 动画采样后的骨架原点因资源而异，只按当前可见角色网格的最低点校正该预览实例；
        /// 没有 SkinnedMeshRenderer 的角色保持 Prefab 原位，不移动共享展台或其他角色。
        /// </summary>
        internal static bool AlignVisibleSkinToStage(GameObject character, Renderer stage)
        {
            if (character == null || stage == null) return false;
            var minimumY = float.PositiveInfinity;
            var baked = new Mesh();
            try
            {
                foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    baked.Clear();
                    renderer.BakeMesh(baked);
                    foreach (var vertex in baked.vertices)
                        minimumY = Mathf.Min(minimumY, renderer.transform.TransformPoint(vertex).y);
                }
            }
            finally
            {
                if (Application.isPlaying) Destroy(baked);
                else DestroyImmediate(baked);
            }
            if (float.IsPositiveInfinity(minimumY)) return false;
            character.transform.position += Vector3.up * (stage.bounds.max.y + 0.01f - minimumY);
            return true;
        }

        private void Scrub(float normalized)
        {
            if (selectedClip == null || (!graph.IsValid() && !playerCompositePreview)) return;
            playing = false;
            RefreshPlayPauseLabel();
            clipTimeSeconds = Mathf.Clamp01(normalized) * selectedClip.length;
            if (playerCompositePreview)
            {
                compositePlaybackSeconds = clipTimeSeconds;
                SamplePlayerComposite();
            }
            else
            {
                clipPlayable.SetTime(clipTimeSeconds);
                graph.Evaluate(0f);
                playerView?.TickCombatFeedback(0f);
                ApplyPreviewGrip(selectedClip.name);
            }
            RefreshTimelineLabel();
        }

        /// <summary>
        /// 只在临时玩家实例上按指定时间采样正式分层姿态；不推进移动、射击或伤害。
        /// 上半身是否瞄准仍由 PlayerView 按当前装备设置的 Animator 参数决定。
        /// </summary>
        private void SamplePlayerComposite()
        {
            if (!playerCompositePreview || selectedClip == null) return;
            var runTime = Mathf.Repeat(compositePlaybackSeconds, selectedClip.length) / selectedClip.length;
            if (PlayerViewCtrl.UsesRifleAimPose(selectedPlayerWeaponResourceName) &&
                (selectedClip.name == "RifleIdleAiming" || selectedClip.name == "RifleFire"))
            {
                animator.Play(PlayerIdleState, 0, runTime);
                // 只有霰弹枪有独立开火状态；其他长枪保持正式瞄准层并叠加程序后坐。
                animator.Play(selectedPlayerWeaponResourceName == "ArcadeScifiShotgun" &&
                    selectedClip.name == "RifleFire" && animator.HasState(1, PlayerShotgunFireState)
                    ? PlayerShotgunFireState : PlayerRifleAimState, 1, runTime);
                animator.Update(0.001f);
                playerView.TickCombatFeedback(0f);
                ApplyPreviewGrip(selectedClip.name);
                if (selectedClip.name == "RifleFire")
                    playerView.ApplyProjectileFirePreviewPose(Mathf.Repeat(compositePlaybackSeconds,
                        selectedClip.length));
                return;
            }
            if (!graph.IsValid()) return;
            if (selectedPlayerWeaponResourceName == "ArcadePistol" &&
                (selectedClip.name == "PistolIdle" || selectedClip.name == "PistolFire"))
            {
                controllerPlayable.SetBool(PlayerPistolWeaponParameter,
                    animator.GetBool(PlayerPistolWeaponParameter));
                controllerPlayable.SetLayerWeight(2, 1f);
                // 手动采样不会等待状态机过渡，握手层须与待机和开火同帧生效。
                controllerPlayable.SetLayerWeight(PlayerPistolGripLayer, 1f);
                controllerPlayable.Play(PlayerIdleState, 0, runTime);
                controllerPlayable.Play(selectedClip.name == "PistolIdle"
                    ? PlayerPistolAimState : PlayerPistolFireState, 2, runTime);
                controllerPlayable.Play(PlayerPistolGripState, PlayerPistolGripLayer, runTime);
                graph.Evaluate(0f);
                playerView.TickCombatFeedback(0f);
                ApplyPreviewGrip(selectedClip.name);
                return;
            }
            controllerPlayable.SetBool(PlayerRangedWeaponParameter, animator.GetBool(PlayerRangedWeaponParameter));
            controllerPlayable.SetLayerWeight(1, 1f);
            controllerPlayable.Play(PlayerRunState, 0, runTime);
            if (controllerPlayable.GetBool(PlayerRangedWeaponParameter))
            {
                var aimTime = Mathf.Repeat(compositePlaybackSeconds, compositeAimClip.length) /
                              compositeAimClip.length;
                controllerPlayable.Play(PlayerRifleAimState, 1, aimTime);
            }
            else
            {
                controllerPlayable.Play(PlayerEmptyUpperBodyState, 1, runTime);
            }
            graph.Evaluate(0f);
            playerView.TickCombatFeedback(0f);
            ApplyPreviewGrip("RifleIdleAiming");
        }

        private void TogglePlayback() { playing = !playing; RefreshPlayPauseLabel(); }
        private void RefreshPlayPauseLabel() { if (playPauseText != null) playPauseText.text = playing ? "暂停" : "播放"; }
        private void RefreshTimelineLabel()
        {
            if (timelineText != null) timelineText.text = selectedClip == null
                ? "无动画" : $"{clipTimeSeconds:0.00} / {selectedClip.length:0.00} 秒";
        }

        private void Update()
        {
            RefreshPreviewViewport();
            poseTuningPanel?.Tick(Time.unscaledDeltaTime);
            if (flow == null) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                flow.CloseAsync().Forget();
                return;
            }
            if (playing && selectedClip != null && selectedClip.length > 0f &&
                (graph.IsValid() || playerCompositePreview))
            {
                if (playerCompositePreview)
                {
                    compositePlaybackSeconds += Time.unscaledDeltaTime;
                    clipTimeSeconds = Mathf.Repeat(compositePlaybackSeconds, selectedClip.length);
                }
                else
                {
                    clipTimeSeconds = (clipTimeSeconds + Time.unscaledDeltaTime) % selectedClip.length;
                }
                timeline.SetValueWithoutNotify(clipTimeSeconds / selectedClip.length);
                RefreshTimelineLabel();
            }
            HandleCameraInput();
        }

        // 仅转换选项显示名；索引和资源身份仍由正式 Prefab、Animator 与武器目录决定。
        private static string GetCharacterDisplayName(string resourceName)
        {
            string alias = resourceName switch
            {
                "GrayboxPlayer" => "玩家",
                "ArcadeEnemyBasic" => "普通敌人",
                "ArcadeEnemyBrute" => "重型敌人",
                "ArcadeEnemyArcher" => "弓箭敌人",
                "ArcadeEnemyBossArcher" => "首领弓箭敌人",
                _ => "未命名角色"
            };
            return alias;
        }

        private static string GetVariantDisplayName(int index, string resourceName)
        {
            string alias = resourceName switch
            {
                "Character_Dummy_Female_01_1" => "女性假人",
                "Character_Dummy_Male_01" => "男性假人",
                "Character_Female_Face_01" => "女性（有脸）",
                "Character_Female_NoFace_01" => "女性（无脸）",
                "Character_Male_Face_01" => "男性（有脸）",
                "Character_Male_NoFace_01" => "男性（无脸）",
                _ => null
            };
            return string.IsNullOrEmpty(alias) ? $"外观 {index + 1}" : $"外观 {index + 1}：{alias}";
        }

        private static string GetEquipmentDisplayName(string optionName)
        {
            if (string.IsNullOrEmpty(optionName)) return optionName;
            const string trialSuffix = "（仅试装）";
            bool trial = optionName.EndsWith(trialSuffix, StringComparison.Ordinal);
            string resourceName = trial ? optionName.Substring(0, optionName.Length - trialSuffix.Length) : optionName;
            string alias = resourceName switch
            {
                "ArcadePistol" => "手枪",
                "ArcadeRifle" => "迷你机枪",
                "ArcadeScifiShotgun" => "霰弹枪",
                "ArcadePiercingGun" => "弩",
                "ArcadePanzerschreck" => "火箭发射器",
                "ArcadeGrenade" => "榴弹发射器",
                "ArcadeScifiPlasmaRifle" => "镭射枪",
                _ => "未命名武器"
            };
            return alias + (trial ? trialSuffix : string.Empty);
        }

        private static string GetAnimationDisplayName(string resourceName)
        {
            string alias = resourceName switch
            {
                "HumanM@Idle01" => "待机",
                "HumanM@CombatIdle2H01" => "双手武器待机",
                "HumanM@Run01_Forward" => "向前跑动",
                "HumanM@Attack1H01_R" => "右手单手攻击",
                "HumanM@Attack2H01" => "双手攻击",
                "2Hand-Sword-Idle" => "重型敌人双手剑待机",
                "2Hand-Sword-Run-Forward" => "重型敌人持剑奔跑",
                "2Hand-Sword-Attack1" => "重型敌人双手剑攻击",
                "HumanM@CombatDamage01" => "受击",
                "HumanM@Death01" => "死亡",
                "Rifle Idle Aiming" => "持枪瞄准待机",
                "RifleIdleAiming" => "步枪瞄准待机",
                "RifleFire" => "步枪开火",
                "RifleReload" => "步枪换弹",
                "RifleWalkForward" => "持步枪向前走",
                "RifleWalkBackward" => "持步枪向后走",
                "RifleWalkLeft" => "持步枪向左走",
                "RifleWalkRight" => "持步枪向右走",
                "PistolIdle" => "双手手枪瞄准待机",
                "PistolFire" => "双手手枪开火",
                "StandingAimIdle01" => "举弓瞄准待机",
                "StandingDrawArrow" => "搭箭·候选",
                "ShootingArrow" => "射箭",
                "StandingAimWalkForward" => "举弓前移",
                "StandingAimWalkBack" => "举弓后移",
                "StandingAimWalkLeft" => "举弓左移",
                "BowAimWalkRightComposite" => "举弓右移·直立组合",
                _ => null
            };
            return alias ?? "未命名动作";
        }

        /// <summary>预览和握持来源提示共用中文动作名称；只去掉标识括注，保留“仅预览”等中文说明。</summary>
        internal static string GetAnimationDefinitionDisplayName(CharacterAnimationDefinition definition)
        {
            if (definition == null) return "未命名动作";
            var name = definition.DisplayName;
            if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, "[\\u4e00-\\u9fff]"))
                return GetAnimationDisplayName(definition.name);
            return System.Text.RegularExpressions.Regex.Replace(name, @"\s*[（(][A-Za-z0-9_@ .-]+[）)]", string.Empty).Trim();
        }

        /// <summary>Animator 常规更新结束后采样预览动画；暂停时也维持当前帧，避免骨骼被原控制器覆盖。</summary>
        private void LateUpdate()
        {
            if (flow == null) return;
            RefreshSharedWeaponPose();
            if (selectedClip == null || (!graph.IsValid() && !playerCompositePreview)) return;
            if (playerCompositePreview)
            {
                SamplePlayerComposite();
                poseTuningPanel?.RefreshAxisGuide(weaponPresenter?.Visual?.transform);
                return;
            }
            clipPlayable.SetTime(clipTimeSeconds);
            graph.Evaluate(0f);
            playerView?.TickCombatFeedback(0f);
            ApplyPreviewGrip(selectedClip.name);
            poseTuningPanel?.RefreshAxisGuide(weaponPresenter?.Visual?.transform);
        }

        /// <summary>Inspector 调整目录中的姿态数值后，当前预览实例下一帧原位更新。</summary>
        private void RefreshSharedWeaponPose()
        {
            if (poseTuningPanel != null)
                poseTuningPanel.Bind(weaponPresenter?.Current, selectedDefinition);
            if (characterWeaponCatalog == null || weaponPresenter == null ||
                string.IsNullOrEmpty(selectedSharedWeaponId)) return;
            for (var index = 0; index < characterWeaponCatalog.Count; index++)
            {
                var entry = characterWeaponCatalog.Get(index);
                if (entry?.Id != selectedSharedWeaponId) continue;
                weaponPresenter.RefreshCurrentPose(entry);
                return;
            }
        }

        /// <summary>手动采样后按正式控制器的动作兼容规则选择垂手或瞄准旋转，并修正副手。</summary>
        private void ApplyPreviewGrip(string animationName)
        {
            if (characterWeaponCatalog != null)
            {
                weaponPresenter?.ApplyOffhandGrip(selectedDefinition,
                    selectedClip != null && selectedClip.length > 0f ? clipTimeSeconds / selectedClip.length : 0f);
                return;
            }
            if (PlayerViewCtrl.AllowsOffhandGrip(selectedPlayerWeaponResourceName, animationName))
                playerView?.ApplyWeaponGripPose();
            else
                playerView?.SetWeaponVisualPose(0f);
        }

        private void HandleCameraInput()
        {
            if (topDown || previewCamera == null) return;
            if (poseTuningPanel != null && poseTuningPanel.IsAdjusting) { lastPinchDistance = 0f; return; }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 current = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    lastPointer = current;
                    mouseOrbitStartedInPreview = IsPreviewPointer(current);
                }
                if (mouse.leftButton.isPressed && mouseOrbitStartedInPreview && IsPreviewPointer(current))
                    Orbit(current - lastPointer);
                lastPointer = current;
                float scroll = mouse.scroll.ReadValue().y;
                if (IsPreviewPointer(current) && Mathf.Abs(scroll) > 0.01f)
                {
                    cameraDistance = Mathf.Clamp(cameraDistance - scroll / 120f * 0.35f, 2f, 10f);
                    UpdateCamera();
                }
            }
            var touch = Touchscreen.current;
            if (touch == null) return;
            var first = touch.touches[0];
            if (!first.press.isPressed)
            {
                lastPinchDistance = 0f;
                firstTouchStartedInPreview = secondTouchStartedInPreview = false;
                firstTouchObserved = secondTouchObserved = false;
                return;
            }
            var second = touch.touches[1];
            Vector2 firstPosition = first.position.ReadValue();
            if (!firstTouchObserved)
            {
                firstTouchObserved = true;
                firstTouchStartedInPreview = IsPreviewPointer(first.startPosition.ReadValue());
            }
            if (second.press.isPressed)
            {
                if (!secondTouchObserved)
                {
                    secondTouchObserved = true;
                    secondTouchStartedInPreview = IsPreviewPointer(second.startPosition.ReadValue());
                }
                var secondPosition = second.position.ReadValue();
                // 双指都属于实际预览区才缩放；跨面板、刘海或下拉菜单的触点不能带动人物相机。
                if (!firstTouchStartedInPreview || !secondTouchStartedInPreview ||
                    !IsPreviewPointer(firstPosition) || !IsPreviewPointer(secondPosition))
                {
                    lastPinchDistance = 0f;
                    return;
                }
                float distance = Vector2.Distance(firstPosition, secondPosition);
                if (lastPinchDistance > 0f)
                {
                    cameraDistance = Mathf.Clamp(cameraDistance - (distance - lastPinchDistance) * 0.01f, 2f, 10f);
                    UpdateCamera();
                }
                lastPinchDistance = distance;
            }
            else
            {
                lastPinchDistance = 0f;
                secondTouchStartedInPreview = false;
                secondTouchObserved = false;
                if (firstTouchStartedInPreview && IsPreviewPointer(firstPosition))
                    Orbit(first.delta.ReadValue());
            }
        }

        /// <summary>预览输入按相机真实像素视口判断，并排除下拉菜单等覆盖预览区的 UI。</summary>
        private bool IsPreviewPointer(Vector2 position) =>
            previewCamera != null && previewCamera.pixelRect.Contains(position) && !previewUiBlocker.IsBlocked(position);

        /// <summary>仅屏幕尺寸或安全区改变时映射场景视口，与控制面板的 SafeAreaFitter 保持同一边界。</summary>
        private void RefreshPreviewViewport()
        {
            if (previewCamera == null || Screen.width <= 0 || Screen.height <= 0) return;
            if (!cameraViewportCaptured)
            {
                authoredCameraViewport = previewCamera.rect;
                cameraViewportCaptured = true;
            }
            var size = new Vector2Int(Screen.width, Screen.height);
            var safe = Screen.safeArea;
            if (size == lastScreenSize && safe == lastSafeArea) return;
            previewCamera.rect = MapPreviewViewportToSafeArea(authoredCameraViewport, safe, size);
            lastScreenSize = size;
            lastSafeArea = safe;
            lastPinchDistance = 0f;
        }

        /// <summary>按设备安全区映射场景原始视口；分屏比例由场景维护，零屏幕尺寸保持原值。</summary>
        internal static Rect MapPreviewViewportToSafeArea(Rect viewport, Rect safeArea, Vector2 screenSize)
        {
            if (screenSize.x <= 0f || screenSize.y <= 0f) return viewport;
            SafeAreaFitter.CalculateAnchors(safeArea, screenSize, out var safeMin, out var safeMax);
            SafeAreaFitter.MapAnchorsIntoSafeArea(viewport.min, viewport.max, safeMin, safeMax, out var min, out var max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Orbit(Vector2 delta)
        {
            yawDegrees += delta.x * 0.25f;
            pitchDegrees = Mathf.Clamp(pitchDegrees - delta.y * 0.25f, -10f, 75f);
            UpdateCamera();
        }

        private void ToggleCamera()
        {
            topDown = !topDown;
            if (cameraModeText != null) cameraModeText.text = topDown ? "切到近景" : "切到俯视";
            UpdateCamera();
        }

        private void ResetCamera()
        {
            yawDegrees = DefaultCameraYawDegrees;
            pitchDegrees = 18f;
            cameraDistance = 5f;
            UpdateCamera();
        }

        private void UpdateCamera()
        {
            RefreshPreviewViewport();
            if (previewCamera == null || previewRoot == null) return;
            // 展台与人物整体向画面上方构图，避免近景下脚部和展台落到视口外。
            var target = previewRoot.position + Vector3.up * 0.55f;
            if (topDown)
                previewCamera.transform.position = target + new Vector3(0f, 8f, -3f);
            else
                previewCamera.transform.position = target +
                    Quaternion.Euler(pitchDegrees, yawDegrees, 0f) * Vector3.back * cameraDistance;
            previewCamera.transform.LookAt(target);
        }

        private void ReleaseAnimation()
        {
            if (graph.IsValid()) graph.Destroy();
            controllerPlayable = default;
            playerCompositePreview = false;
            compositePlaybackSeconds = 0f;
            selectedClip = null;
            selectedDefinition = null;
            clipTimeSeconds = 0f;
        }

        private void OnDisable()
        {
            poseTuningPanel?.CancelHold();
            mouseOrbitStartedInPreview = firstTouchStartedInPreview = secondTouchStartedInPreview = false;
            firstTouchObserved = secondTouchObserved = false;
            ReleaseAnimation();
            if (characterInstance != null)
            {
                characterInstance.SetActive(false);
                DestroyPreviewObject(characterInstance);
                characterInstance = null;
            }
        }

        private static void DestroyPreviewObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            if (poseTuningPanel != null) poseTuningPanel.TuningModeChanged -= SetTuningWeaponSelection;
            OnDisable();
        }
    }
}
