using System;
using System.Runtime.CompilerServices;
using System.Text;
using QFramework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 正式敌人的场景表现入口：负责动画、受击反馈和可见装备挂载；战斗装备身份与伤害规则不由此决定。
    /// 造型库复用同一个外观入口，预览目录只在临时实例上配置。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed partial class EnemyView : MonoBehaviour
    {
        [Serializable]
        private struct TrialPlayerWeaponPose
        {
            [SerializeField] private string resourceName;
            [SerializeField] private Vector3 handLocalPosition;
            [SerializeField] private Vector3 handLocalEulerAngles;
            [SerializeField] private float handLocalScale;

            public string ResourceName => resourceName;
            public Vector3 HandLocalPosition => handLocalPosition;
            public Quaternion HandLocalRotation => Quaternion.Euler(handLocalEulerAngles);
            public float HandLocalScale => handLocalScale;
        }

        private const float HitFeedbackDurationSeconds = 0.12f;
        private const float DefaultDeathAnimationDurationSeconds = 0.9f;
        private static readonly Color HitFeedbackColor = Color.red;
        private static readonly int MoveSpeedAnimatorParameter = Animator.StringToHash("MoveSpeed");
        private static readonly int AttackAnimatorParameter = Animator.StringToHash("Attack");
        private static readonly int HitAnimatorParameter = Animator.StringToHash("Hit");
        private static readonly int DeadAnimatorParameter = Animator.StringToHash("Dead");

        [SerializeField]
        private NavMeshAgent navMeshAgent;

        [SerializeField]
        private Animator animator;

        // Prefab 初始武器身份；地图/街机生成逻辑可在实例创建后改为目录中任意武器。
        [SerializeField] private string defaultWeaponId;

        [SerializeField, Min(0f)]
        private float deathAnimationDurationSeconds = DefaultDeathAnimationDurationSeconds;

        /// <summary>只声明共享特效资源依赖，活敌人不实例化死亡粒子；由EnemyCtrl地图池持有实例。</summary>
        [SerializeField] private GameObject explosionDeathTemplate;
        public GameObject ExplosionDeathTemplate => explosionDeathTemplate;

        [SerializeField]
        private Transform healthBarRoot;

        [SerializeField]
        private Image healthBarFill;

        [SerializeField]
        private Transform projectileFirePoint;

        [SerializeField]
        private GameObject telegraphObject;

        [SerializeField]
        private Collider combatCollider;

        [SerializeField]
        private UnitActionProgressView actionProgressView;

        [SerializeField]
        private Renderer[] livingRenderers;

        [SerializeField]
        private SkinnedMeshRenderer[] presentationVariants;

        // 造型库试装时按此敌人骨架校准玩家武器；只影响临时可见模型，不参与攻击与装备状态。
        [SerializeField]
        private TrialPlayerWeaponPose[] trialPlayerWeaponPoses = Array.Empty<TrialPlayerWeaponPose>();

        [SerializeField]
        private DamageNumberEmitter damageNumberEmitter;

        private bool unitHealthBarsVisible;
        private float feedbackRemainingSeconds;
        /// <summary>预制的纯表现骨骼反馈，所有推进均复用EnemyCtrl生命周期。</summary>
        private EnemyPhysicalReaction physicalReaction;
        /// <summary>尸体淡出缓存属于表现实例，回池还原、最终销毁释放，不参与战斗状态。</summary>
        private EnemyCorpseFade corpseFade;
        public bool HasPendingDeathPhysics => PhysicalReaction != null && PhysicalReaction.HasPendingPhysics;
        /// <summary>附着火焰由预制资源持有，和骨骼反馈共用现有表现生命周期。</summary>
        private EnemyFlameFeedback flameFeedback;
        private EnemyFlameFeedback FlameFeedback=>flameFeedback!=null?flameFeedback:flameFeedback=GetComponentInChildren<EnemyFlameFeedback>(true);
        private EnemyPhysicalReaction PhysicalReaction => physicalReaction != null ? physicalReaction : physicalReaction = GetComponent<EnemyPhysicalReaction>();
        private Renderer[] feedbackRenderers = new Renderer[0];
        // 保存受击前的完整覆盖块；多材质身体恢复时不能把首个材质颜色写到全部槽位。
        private MaterialPropertyBlock[] feedbackOriginalBlocks = Array.Empty<MaterialPropertyBlock>();
        private MaterialPropertyBlock feedbackPropertyBlock;
        private int unitId;
        private long mapInstanceId;
        private bool isCorpse;
        private bool animationDead;
        // 池化准备保持物体不可见，待激活且控制器绑定完成后再求值存活初态。
        private bool animationResetPending;
        // 实例选择独立于启停；空串是显式空手，未指定才采用 Prefab 默认武器。
        private bool hasWeaponSelection;
        private string selectedWeaponId;
        private Vector3 previousMovementPosition;
        private bool hasMovementSample;
        private int selectedPresentationVariant = -1;
        // 仅保存当前实例的外观试装来源；权威战斗装备仍由玩法系统决定，空值时只显示 Prefab 原配武器。
        private EnemyWeaponVisualCatalog weaponVisualCatalog;
        private Transform originalWeapon;
        private Transform rightHandWeaponMount;
        private GameObject temporaryWeaponVisual;

        public NavMeshAgent NavMeshAgent => navMeshAgent;
        public Transform HealthBarRoot => healthBarRoot;
        public Image HealthBarFill => healthBarFill;
        public Animator Animator => animator;
        /// <summary>造型预览可选的网格数量；只暴露外观容量，不参与敌人战斗身份。</summary>
        public int PresentationVariantCount => presentationVariants?.Length ?? 0;
        /// <summary>仅向造型预览提供指定外观的资源名；不改变当前外观或敌人身份。</summary>
        public string GetPresentationVariantName(int index) =>
            index >= 0 && index < PresentationVariantCount && presentationVariants[index] != null
                ? presentationVariants[index].name : null;
        public int WeaponVisualOptionCount => 2 + ValidMeleeVisualCount + ValidPlayerWeaponCount;

        public static float CalculateHealthFill(int current, int maximum)
        {
            if (maximum <= 0)
            {
                return 0f;
            }

            return Mathf.Clamp01((float)current / maximum);
        }

        public static bool ShouldShowHealthBar(bool unitHealthBarsVisible, bool isCorpse)
        {
            return unitHealthBarsVisible && !isCorpse;
        }

        /// <summary>把正整数运行时身份稳定映射为外观索引；对象池复用和联机重放保持一致。</summary>
        public static int SelectPresentationVariantIndex(int runtimeId, int variantCount)
        {
            return variantCount > 0 ? (int)((uint)runtimeId % (uint)variantCount) : -1;
        }

        /// <summary>切换 Prefab 内置的 Humanoid 网格；不改变 Animator、碰撞体或战斗属性。</summary>
        public void SetPresentationVariant(int runtimeId)
        {
            selectedPresentationVariant = SelectPresentationVariantIndex(runtimeId, presentationVariants?.Length ?? 0);
            ApplyPresentationVariant();
        }

        /// <summary>
        /// 指定可试装的外观目录，供独立预览或将来的正式装备表现调用；切换目录时恢复 Prefab 原配。
        /// 目录不写入角色状态或存档，未传入时正式游戏保持原配武器。
        /// </summary>
        public void ConfigureWeaponVisualCatalog(EnemyWeaponVisualCatalog catalog)
        {
            weaponVisualCatalog = catalog;
            SetWeaponPresentation(1);
        }

        public string GetWeaponVisualOptionName(int index)
        {
            if (index == 0) return "无装备";
            if (index == 1) return "原配武器";
            index -= 2;
            if (TryGetMeleeVisual(index, out var melee)) return melee.DisplayName + "（仅试装）";
            index -= ValidMeleeVisualCount;
            if (TryGetPlayerWeaponIndex(index, out var sourceIndex))
                return weaponVisualCatalog.GetPlayerWeaponName(sourceIndex) + "（仅试装）";
            return null;
        }

        public bool IsTrialWeaponOption(int index) => index >= 2 && index < WeaponVisualOptionCount;

        /// <summary>记录本实例武器选择并同步共享可见物与动作；失败仍保留选择，供启用时报告。</summary>
        public bool SetCombatWeapon(string weaponId)
        {
            hasWeaponSelection = true;
            selectedWeaponId = weaponId ?? string.Empty;
            SetWeaponPresentation(0);
            var presenter = GetComponent<CharacterWeaponPresenter>();
            return presenter != null && presenter.SetWeaponId(selectedWeaponId);
        }

        /// <summary>仅在回池或开始另一实例用途时清除选择；地图临时隐藏不调用。</summary>
        public void ResetWeaponSelection()
        {
            hasWeaponSelection = false;
            selectedWeaponId = null;
            GetComponent<CharacterWeaponPresenter>()?.SetWeapon(null);
        }

        /// <summary>新用途初始化存活表现；调用方须先保持实例不可见，随后再绑定本次武器和生命状态。</summary>
        public void BeginPresentationUse()
        {
            ResetGroundEntry();
            ResetDockSeaEntry();
            ResetMeleeAttack();
            corpseFade?.Reset();
            FlameFeedback?.ResetForReuse();
            PhysicalReaction?.ResetForReuse();
            EnsureAgent();
            ResetWeaponSelection();
            ResetMovementSampling();
            ResetAnimationPresentation();
            if (feedbackRemainingSeconds > 0f) RestoreFeedbackColors();
            feedbackRemainingSeconds = 0f;
            if (combatCollider == null) combatCollider = GetComponent<Collider>();
            if (combatCollider != null) combatCollider.enabled = true;
            if (navMeshAgent != null) navMeshAgent.enabled = true;
            SetTelegraphVisible(false);
            actionProgressView?.Initialize();
            SetCorpsePresentation(false);
        }

        /// <summary>
        /// 唯一的敌人手持外观切换入口：正式运行默认选原配；试装只替换当前 View 的可见物，
        /// 不改变敌人攻击动画、发射点、伤害或装备规则。无效索引保持已有表现不变。
        /// </summary>
        public bool SetWeaponPresentation(int index)
        {
            EnsureWeaponMounts();
            if (originalWeapon == null || index < 0 || index >= WeaponVisualOptionCount) return false;
            ReleaseTemporaryWeaponVisual();
            originalWeapon.gameObject.SetActive(index == 1);
            if (index < 2) return true;

            int optionIndex = index - 2;
            temporaryWeaponVisual = TryGetMeleeVisual(optionIndex, out var melee)
                ? CreateMeleeWeaponVisual(melee)
                : CreatePlayerWeaponVisual(optionIndex - ValidMeleeVisualCount);
            if (temporaryWeaponVisual != null) return true;
            originalWeapon.gameObject.SetActive(true);
            return false;
        }

        private int ValidMeleeVisualCount
        {
            get
            {
                EnsureWeaponMounts();
                if (originalWeapon == null || originalWeapon.name != "Weapon" || weaponVisualCatalog == null)
                    return 0;
                int count = 0;
                for (int i = 0; i < weaponVisualCatalog.MeleeCount; i++)
                    if (weaponVisualCatalog.GetMelee(i)?.IsValid == true) count++;
                return count;
            }
        }

        private int ValidPlayerWeaponCount
        {
            get
            {
                EnsureWeaponMounts();
                if (rightHandWeaponMount == null || weaponVisualCatalog?.GetPlayerRightHand() == null)
                    return 0;
                int count = 0;
                for (int i = 0; i < weaponVisualCatalog.PlayerWeaponCount; i++)
                    if (weaponVisualCatalog.GetPlayerWeaponVisual(i) != null &&
                        !string.IsNullOrWhiteSpace(weaponVisualCatalog.GetPlayerWeaponName(i))) count++;
                return count;
            }
        }

        private bool TryGetMeleeVisual(int ordinal, out EnemyWeaponVisualCatalog.MeleeVisual result)
        {
            result = null;
            if (ordinal < 0 || weaponVisualCatalog == null || originalWeapon == null ||
                originalWeapon.name != "Weapon") return false;
            for (int i = 0; i < weaponVisualCatalog.MeleeCount; i++)
            {
                var candidate = weaponVisualCatalog.GetMelee(i);
                if (candidate?.IsValid != true) continue;
                if (ordinal-- != 0) continue;
                result = candidate;
                return true;
            }
            return false;
        }

        private bool TryGetPlayerWeaponIndex(int ordinal, out int sourceIndex)
        {
            sourceIndex = -1;
            if (ordinal < 0 || weaponVisualCatalog == null || rightHandWeaponMount == null) return false;
            for (int i = 0; i < weaponVisualCatalog.PlayerWeaponCount; i++)
            {
                if (weaponVisualCatalog.GetPlayerWeaponVisual(i) == null ||
                    string.IsNullOrWhiteSpace(weaponVisualCatalog.GetPlayerWeaponName(i))) continue;
                if (ordinal-- != 0) continue;
                sourceIndex = i;
                return true;
            }
            return false;
        }

        private GameObject CreateMeleeWeaponVisual(EnemyWeaponVisualCatalog.MeleeVisual visual)
        {
            var replacement = new GameObject("EnemyWeaponVisual", typeof(MeshFilter), typeof(MeshRenderer));
            replacement.transform.SetParent(originalWeapon.parent, false);
            replacement.transform.localPosition = originalWeapon.localPosition;
            replacement.transform.localRotation = Quaternion.Euler(visual.HandLocalEulerAngles);
            replacement.transform.localScale = originalWeapon.localScale * visual.ScaleMultiplier;
            replacement.layer = originalWeapon.gameObject.layer;
            replacement.GetComponent<MeshFilter>().sharedMesh = visual.Mesh;
            replacement.GetComponent<MeshRenderer>().sharedMaterial = visual.Material;
            return replacement;
        }

        private GameObject CreatePlayerWeaponVisual(int ordinal)
        {
            if (!TryGetPlayerWeaponIndex(ordinal, out var sourceIndex)) return null;
            var source = weaponVisualCatalog.GetPlayerWeaponVisual(sourceIndex);
            if (source == null || rightHandWeaponMount == null) return null;
            bool hasOverride = TryGetTrialPlayerWeaponPose(weaponVisualCatalog.GetPlayerWeaponName(sourceIndex), out var pose);
            bool hasSourcePose = weaponVisualCatalog.TryGetPlayerWeaponHandPose(sourceIndex,
                out var sourcePosition, out var sourceRotation, out var sourceScale);
            if (!hasOverride && !hasSourcePose) return null;
            var replacement = Instantiate(source, rightHandWeaponMount);
            replacement.name = "EnemyWeaponVisual_" + source.name;
            var mount = replacement.transform;
            if (hasOverride)
            {
                mount.localPosition = pose.HandLocalPosition;
                mount.localRotation = pose.HandLocalRotation;
                mount.localScale = Vector3.one * pose.HandLocalScale;
            }
            else
            {
                mount.localPosition = sourcePosition;
                mount.localRotation = sourceRotation;
                mount.localScale = sourceScale;
            }
            foreach (var child in replacement.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = rightHandWeaponMount.gameObject.layer;
            foreach (var weaponAnimator in replacement.GetComponentsInChildren<Animator>(true))
                weaponAnimator.enabled = false;
            replacement.SetActive(true);
            return replacement;
        }

        /// <summary>按资源名查找本骨架的试装姿态；缺少配置时使用玩家的手部局部姿态。</summary>
        private bool TryGetTrialPlayerWeaponPose(string resourceName, out TrialPlayerWeaponPose pose)
        {
            var candidates = trialPlayerWeaponPoses;
            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (!string.Equals(candidate.ResourceName, resourceName, StringComparison.Ordinal) ||
                        candidate.HandLocalScale <= 0f) continue;
                    pose = candidate;
                    return true;
                }
            }
            pose = default;
            return false;
        }

        private void EnsureWeaponMounts()
        {
            if (originalWeapon != null && rightHandWeaponMount != null) return;
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (originalWeapon == null && (child.name == "Weapon" || child.name == "ArcherBow"))
                    originalWeapon = child;
                if (rightHandWeaponMount == null && child.name == "Hand_R")
                    rightHandWeaponMount = child;
            }
        }

        private void ReleaseTemporaryWeaponVisual()
        {
            if (temporaryWeaponVisual == null) return;
            temporaryWeaponVisual.SetActive(false);
            if (Application.isPlaying) Destroy(temporaryWeaponVisual);
            else DestroyImmediate(temporaryWeaponVisual);
            temporaryWeaponVisual = null;
        }

        private void ApplyPresentationVariant()
        {
            if (selectedPresentationVariant < 0 || presentationVariants == null) return;
            for (var i = 0; i < presentationVariants.Length; i++)
                if (presentationVariants[i] != null)
                    presentationVariants[i].enabled = !isCorpse && i == selectedPresentationVariant;
        }

        private void Reset()
        {
            EnsureAgent();
            if (navMeshAgent != null)
            {
                navMeshAgent.speed = 0f;
                navMeshAgent.stoppingDistance = 0f;
            }
        }

        private void OnEnable()
        {
            EnsureAgent();
            SetWeaponPresentation(1);
            var weaponId = hasWeaponSelection ? selectedWeaponId : defaultWeaponId;
            if (GetComponent<CharacterWeaponPresenter>() != null &&
                (!string.IsNullOrEmpty(weaponId) || hasWeaponSelection))
            {
                if (!SetCombatWeapon(weaponId))
                    Debug.LogError($"[{nameof(EnemyView)}] 武器表现不可用：{weaponId}", this);
            }
            if (animationResetPending) ResetAnimationPresentation();
            if (animationDead && animator != null)
            {
                animator.SetFloat(MoveSpeedAnimatorParameter, 0f);
                animator.SetBool(DeadAnimatorParameter, true);
            }
            actionProgressView?.Initialize();
            RefreshHealthBarVisibility();
        }

        private void OnDisable()
        {
            ResetGroundEntry();
            ResetDockSeaEntry();
            ResetHerdImpact();
            corpseFade?.Reset();
            PhysicalReaction?.ResetForReuse();
            if (feedbackRemainingSeconds > 0f) RestoreFeedbackColors();
            feedbackRemainingSeconds = 0f;
            ReleaseTemporaryWeaponVisual();
            GetComponent<CharacterWeaponPresenter>()?.SetWeapon(null);
            ResetMovementSampling();
        }

        public void ConfigureRuntime(int configuredUnitId, long configuredMapInstanceId)
        {
            unitId = configuredUnitId;
            mapInstanceId = configuredMapInstanceId;
        }

        public void ConfigureNavigation(float moveSpeed, float stoppingDistance)
        {
            EnsureAgent();
            if (navMeshAgent == null)
            {
                return;
            }

            navMeshAgent.speed = Mathf.Max(0f, moveSpeed);
            navMeshAgent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
        }

        public void SetHealthDisplay(int current, int maximum)
        {
            if (healthBarFill != null)
            {
                HealthFillUtility.Apply(
                    healthBarFill,
                    CalculateHealthFill(current, maximum));
            }
        }

        public void SetHealthBarVisible(bool visible)
        {
            unitHealthBarsVisible = visible;
            RefreshHealthBarVisibility();
        }

        public void SetAlerted(bool alerted) { }

        public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
        {
            var presenter = GetComponent<CharacterWeaponPresenter>();
            if (presenter != null && presenter.TryGetProjectileOrigin(out origin)) return true;
            if (presenter == null && projectileFirePoint != null)
            {
                origin = projectileFirePoint.position;
                return true;
            }

            direction.y = 0f;
            var forward = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            origin = transform.position + forward * 0.45f + Vector3.up * 1.15f;
            return true;
        }

        public bool IsOnNavigableSurface(float sampleRadius)
        {
            EnsureAgent();
            if (navMeshAgent == null || !navMeshAgent.enabled)
            {
                return false;
            }

            if (gameObject.activeInHierarchy && navMeshAgent.isOnNavMesh)
            {
                return true;
            }

            return NavMesh.SamplePosition(
                transform.position,
                out _,
                Mathf.Max(0.01f, sampleRadius),
                NavMesh.AllAreas);
        }

        /// <summary>读取候选出生的姿态与导航组件现场；仅供失败诊断，不激活对象或更改导航状态。</summary>
        /// <param name="sampleRadius">原出生校验半径，单位米；额外 3 米采样只用于定位误差。</param>
        internal void AppendNavigationDiagnostic(StringBuilder text, float sampleRadius)
        {
            var pose = transform;
            var position = pose.position;
            text.AppendLine($"scene={gameObject.scene.path} worldPosition={position.ToString("F3")} localPosition={pose.localPosition.ToString("F3")} activeSelf={gameObject.activeSelf} activeInHierarchy={gameObject.activeInHierarchy}");
            var parent = pose.parent;
            if (parent != null)
                text.AppendLine($"parent={parent.name} position={parent.position.ToString("F3")} scale={parent.lossyScale.ToString("F3")} activeInHierarchy={parent.gameObject.activeInHierarchy}");
            var agent = navMeshAgent;
            text.AppendLine($"agentExists={agent != null}");
            if (agent != null)
            {
                text.AppendLine($"agentEnabled={agent.enabled} agentActive={agent.isActiveAndEnabled}");
                try { AppendAgentDiagnostic(text, agent); }
                catch (Exception exception) { text.AppendLine($"agentReadFailed={exception.GetType().Name}: {exception.Message}"); }
            }
            try { AppendAllAreasDiagnostic(text, position, sampleRadius); }
            catch (Exception exception) { text.AppendLine($"allAreasReadFailed={exception.GetType().Name}: {exception.Message}"); }
            if (agent != null)
            {
                try { AppendAgentFilterDiagnostic(text, position, sampleRadius, agent); }
                catch (Exception exception) { text.AppendLine($"agentFilterReadFailed={exception.GetType().Name}: {exception.Message}"); }
            }
        }

        // 各导航步骤禁止内联，避免旧宿主缺失 API 时在解析调用方法阶段吞掉已可读取的基础姿态。
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AppendAgentDiagnostic(StringBuilder text, NavMeshAgent agent)
        {
            // 候选表现通常尚未激活，未绑定状态不作为导航数据丢失的证据。
            var onNavMesh = agent.isActiveAndEnabled ? agent.isOnNavMesh.ToString() : "unavailable(inactive)";
            text.AppendLine($"isOnNavMesh={onNavMesh} agentType={agent.agentTypeID} areaMask={agent.areaMask} baseOffset={agent.baseOffset} radius={agent.radius} height={agent.height}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AppendAllAreasDiagnostic(StringBuilder text, Vector3 position, float sampleRadius)
        {
            var exactHitFound = NavMesh.SamplePosition(position, out var exactHit, sampleRadius, NavMesh.AllAreas);
            text.AppendLine($"allAreasHit={exactHitFound}" + (exactHitFound ? $" position={exactHit.position.ToString("F3")} distance={exactHit.distance}" : ""));
            const float DiagnosticRadius = 3f;
            var wideHitFound = NavMesh.SamplePosition(position, out var wideHit, DiagnosticRadius, NavMesh.AllAreas);
            text.AppendLine($"wideSampleRadius={DiagnosticRadius} wideHit={wideHitFound}" + (wideHitFound ? $" position={wideHit.position.ToString("F3")} distance={wideHit.distance}" : ""));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AppendAgentFilterDiagnostic(StringBuilder text, Vector3 position, float sampleRadius, NavMeshAgent agent)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            var found = NavMesh.SamplePosition(position, out var hit, sampleRadius, filter);
            text.AppendLine($"agentFilterHit={found}" + (found ? $" position={hit.position.ToString("F3")} distance={hit.distance}" : ""));
        }

        public bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination)
        {
            EnsureAgent();
            if (navMeshAgent == null ||
                !navMeshAgent.enabled ||
                !navMeshAgent.isOnNavMesh)
            {
                destination = requestedPosition;
                return false;
            }

            if (NavMesh.SamplePosition(requestedPosition, out var hit, Mathf.Max(0.01f, sampleRadius), NavMesh.AllAreas))
            {
                destination = hit.position;
                return true;
            }

            destination = requestedPosition;
            return false;
        }

        public void SetTelegraphVisible(bool visible)
        {
            if (telegraphObject != null && telegraphObject.activeSelf != visible)
            {
                telegraphObject.SetActive(visible);
            }
        }

        public void SetActionProgressVisible(bool visible, float progress01 = 0f, string iconAddress = null)
        {
            if (!visible || isCorpse)
            {
                actionProgressView?.Hide();
                return;
            }

            actionProgressView?.Show(progress01, iconAddress ?? string.Empty);
        }

        public void FaceWorldHuds(Vector3 cameraPosition, Vector3 cameraUp)
        {
            FaceWorldHud(healthBarRoot, cameraPosition, cameraUp);
            actionProgressView?.FaceWorld(cameraPosition, cameraUp);
        }

        private static void FaceWorldHud(Transform root, Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (root == null || !root.gameObject.activeSelf)
            {
                return;
            }

            var direction = root.position - cameraPosition;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            root.rotation = Quaternion.LookRotation(direction, cameraUp);
        }

        public void MarkDead()
        {
            MarkCorpse();
        }

        public void MarkOnlineDead()
        {
            MarkCorpse();
        }

        public void ReviveOnline()
        {
            if(feedbackRemainingSeconds>0f)RestoreFeedbackColors();feedbackRemainingSeconds=0f;SetCorpsePresentation(false);
            if(combatCollider==null)combatCollider=GetComponent<Collider>();if(combatCollider!=null)combatCollider.enabled=true;
            SetHealthBarVisible(true);
        }

        public void SetNavigationEnabled(bool enabled)
        {
            EnsureAgent();if(navMeshAgent==null)return;if(navMeshAgent.enabled==enabled)return;if(!enabled)Stop();navMeshAgent.enabled=enabled;
        }

        public void MarkCorpse()
        {
            if (isCorpse)
            {
                SetTelegraphVisible(false);
                actionProgressView?.Hide();
                RefreshHealthBarVisibility();
                SetCorpsePresentation(true);
                return;
            }

            isCorpse = true;
            SetTelegraphVisible(false);
            actionProgressView?.Hide();
            RefreshHealthBarVisibility();

            Stop();
            if (navMeshAgent != null)
            {
                navMeshAgent.enabled = false;
            }

            if (combatCollider == null)
            {
                combatCollider = GetComponent<Collider>();
            }

            if (combatCollider != null)
            {
                combatCollider.enabled = false;
            }

            SetCorpsePresentation(true);
        }


        public void SetDestination(Vector3 position)
        {
            EnsureAgent();
            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = false;
                navMeshAgent.SetDestination(position);
            }
        }

        public void Stop()
        {
            EnsureAgent();
            if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.isStopped = true;
            }

            ResetMovementSampling();
        }

        /// <summary>离散定位、停下或禁用后重设采样基准，下一次 Tick 不把跳变算作移动。</summary>
        public void ResetMovementSampling()
        {
            previousMovementPosition = transform.position;
            hasMovementSample = false;
            if (animator != null && animator.isActiveAndEnabled && animator.isInitialized)
                animator.SetFloat(MoveSpeedAnimatorParameter, 0f);
            GetComponent<CharacterWeaponPresenter>()?.SetMovementDirection(Vector3.zero);
        }

        /// <summary>按规则层已经提交的世界速度刷新移动表现；不会写入 Transform 或 NavMesh 状态。</summary>
        public void SetMovementSpeed(float worldUnitsPerSecond)
        {
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized || animationDead)
            {
                return;
            }

            animator.SetFloat(MoveSpeedAnimatorParameter, Mathf.Max(0f, worldUnitsPerSecond));
        }

        /// <summary>播放一次攻击表现；攻击命中与伤害时机仍由战斗系统决定。</summary>
        public void PlayAttackAnimation()
        {
            // 空手抓挠已按权威进度从起手播到收招，命中时不能再由旧触发器重头播放。
            if(meleeAuthorityEnabled && meleeVisual.Ready)return;
            if (animator == null || animationDead)
            {
                return;
            }

            animator.ResetTrigger(HitAnimatorParameter);
            animator.SetTrigger(AttackAnimatorParameter);
        }

        /// <summary>开始死亡表现并返回建议保留时长；无 Animator 时由调用方立即回收。</summary>
        public bool TryPlayDeathAnimation(out float durationSeconds)
        {
            StopGroundEntryForDeath();
            StopDockSeaEntryForDeath();
            ResetMeleeAttack();
            durationSeconds = 0f;
            if (animator == null)
            {
                return false;
            }

            animationDead = true;
            animator.ResetTrigger(AttackAnimatorParameter);
            animator.ResetTrigger(HitAnimatorParameter);
            animator.SetFloat(MoveSpeedAnimatorParameter, 0f);
            animator.SetBool(DeadAnimatorParameter, true);
            ResetMovementSampling();
            durationSeconds = Mathf.Max(EnemyCorpseFade.LifetimeSeconds, deathAnimationDurationSeconds);
            return true;
        }

        /// <summary>死亡即关闭战斗碰撞；普通死亡保留短暂动画过渡，骨骼物理不参与战斗结算。</summary>
        public bool TryPlayPhysicalDeath(Vector3 direction, bool explosive, out float durationSeconds)
            => TryPlayPhysicalDeathWithImpact(direction, explosive, 0, out durationSeconds);

        /// <summary>消费最终伤害的力度类别，沿用尸体寿命、碰撞关闭与统一物理预算。</summary>
        public bool TryPlayPhysicalDeathWithImpact(Vector3 direction, bool explosive, int impactKind, out float durationSeconds)
        {
            bool preserveSampledPose = dockSampling||groundSampling;
            StopGroundEntryForDeath();
            StopDockSeaEntryForDeath();
            ResetMeleeAttack();
            var continuation=herdImpact;var continuedDirection=herdDirection;
            ResetHerdImpact();
            durationSeconds = 0f;
            if (PhysicalReaction == null || !(continuation.Active
                ?PhysicalReaction.BeginHerdDeathFromImpact(continuedDirection,continuation)
                :preserveSampledPose ? PhysicalReaction.BeginSampledPoseDeath(direction, explosive, impactKind)
                :PhysicalReaction.BeginDeathWithImpact(direction, explosive, impactKind))) return false;
            animationDead = true;
            // 独立挂载的武器不是物理骨架，死亡后收起，避免留在原位悬空。
            GetComponent<CharacterWeaponPresenter>()?.SetWeapon(null);
            SetWeaponPresentation(0);
            SetNavigationEnabled(false);
            if (combatCollider == null) combatCollider = GetComponent<Collider>();
            if (combatCollider != null) combatCollider.enabled = false;
            durationSeconds = EnemyCorpseFade.LifetimeSeconds;
            return true;
        }

        /// <summary>由现有LateUpdate在动画后叠加回弹，不新增逐敌人Update。</summary>
        public void LateTickCombatFeedback(){ApplyDockSeaEntry();ApplyGroundEntry();PhysicalReaction?.LateTick();if(!dockSampling&&!groundSampling){ApplyMeleeAttack();ApplyHerdImpact();}}
        /// <summary>池管理显式复位，不依赖编辑态或禁用父节点下的OnDisable回调。</summary>
        public void ResetPhysicalFeedback(){ResetGroundEntry();ResetDockSeaEntry();ResetMeleeAttack();ResetHerdImpact();corpseFade?.Reset();PhysicalReaction?.ResetForReuse();FlameFeedback?.ResetForReuse();}
        public bool IsHerdCorpse=>PhysicalReaction?.IsHerdDeath==true;

        /// <summary>预算只停止物理，保留尸体的显示寿命。</summary>
        public void StopDeathSimulation() => PhysicalReaction?.StopDeathSimulation();

        /// <summary>控制器按回收剩余时间推进淡出；长时间静止阶段不分配材质。</summary>
        public void SetCorpseFade(float remainingSeconds)
        {
            if (remainingSeconds >= EnemyCorpseFade.FadeSeconds) return;
            if (corpseFade == null) corpseFade = new EnemyCorpseFade(animator != null ? animator.transform : transform);
            corpseFade.Apply(remainingSeconds);
        }

        /// <summary>地图释放先归还实例持有的临时材质；编辑态隔离预览同样可显式释放。</summary>
        public void ReleasePresentationResources()
        {
            ReleaseGroundEntryResources();
            ReleaseDockSeaEntryResources();
            corpseFade?.Dispose();
            corpseFade = null;
        }

        private void OnDestroy() => ReleasePresentationResources();

        /// <summary>只消费实际喷火命中后的剩余表现秒数。</summary>
        public void SetFlameRemaining(float seconds)=>FlameFeedback?.SetRemaining(seconds);
        /// <summary>死亡类型由战斗裁决传入，不根据画面相交或当前武器猜测。</summary>
        public void PlayFlameDeath(bool flameKill)=>FlameFeedback?.PlayDeath(flameKill);

        public void PlayHitFeedback()
        {
            if (PhysicalReaction != null && PhysicalReaction.IsReady && !animationDead)
                PhysicalReaction.PlayHit(-transform.forward);
            else if (animator != null && !animationDead)
            {
                animator.ResetTrigger(AttackAnimatorParameter);
                animator.SetTrigger(HitAnimatorParameter);
            }

            // 连续受击只延长闪红，不能把上一发的红色覆盖保存成恢复基线。
            if (feedbackRemainingSeconds <= 0f) CaptureFeedbackColors();
            feedbackRemainingSeconds = HitFeedbackDurationSeconds;
            ApplyFeedbackColor(HitFeedbackColor);
        }
        /// <summary>专用阶段隐藏时显式还原既有受击闪色，避免禁用脚本不能收到OnDisable而遗留红色。</summary>
        public void ClearHitFeedback()
        {
            if(feedbackRemainingSeconds>0f)RestoreFeedbackColors();
            feedbackRemainingSeconds=0f;
        }

        public void ShowDamage(int damage)
        {
            damageNumberEmitter?.Show(damage, false);
        }

        public bool ValidateDamageNumbers(out string error)
        {
            if (damageNumberEmitter == null)
            {
                error = "敌人缺少伤害飘字发射器";
                return false;
            }
            return damageNumberEmitter.Validate(out error);
        }

        public void TickCombatFeedback(float deltaTime)
        {
            TickMeleeAttack(deltaTime);
            herdVisual.Restore();
            FlameFeedback?.Tick(deltaTime);
            PhysicalReaction?.Tick(deltaTime);
            SampleMovement(deltaTime);
            if (feedbackRemainingSeconds <= 0f)
            {
                return;
            }

            feedbackRemainingSeconds -= deltaTime;
            if (feedbackRemainingSeconds <= 0f)
            {
                RestoreFeedbackColors();
            }
        }

        private void SampleMovement(float deltaTime)
        {
            var displacement = transform.position - previousMovementPosition;
            displacement.y = 0f;
            previousMovementPosition = transform.position;
            if (!hasMovementSample || deltaTime <= 0f || animationDead || isCorpse || !isActiveAndEnabled)
            {
                hasMovementSample = true;
                SetMovementSpeed(0f);
                GetComponent<CharacterWeaponPresenter>()?.SetMovementDirection(Vector3.zero);
                return;
            }
            SetMovementSpeed(displacement.magnitude / deltaTime);
            GetComponent<CharacterWeaponPresenter>()?.SetMovementDirection(displacement);
        }

        private void RefreshHealthBarVisibility()
        {
            if (healthBarRoot != null)
            {
                healthBarRoot.gameObject.SetActive(ShouldShowHealthBar(unitHealthBarsVisible, isCorpse));
            }
        }

        /// <summary>在一段受击开始时保存现有覆盖；材质本色继续由各槽位独立提供。</summary>
        private void CaptureFeedbackColors()
        {
            feedbackRenderers = Array.FindAll(GetComponentsInChildren<Renderer>(true),renderer=>!(renderer is ParticleSystemRenderer) && renderer != dockRippleRenderer);
            if (feedbackOriginalBlocks.Length != feedbackRenderers.Length)
            {
                feedbackOriginalBlocks = new MaterialPropertyBlock[feedbackRenderers.Length];
            }

            for (var i = 0; i < feedbackRenderers.Length; i++)
            {
                feedbackOriginalBlocks[i] ??= new MaterialPropertyBlock();
                feedbackRenderers[i].GetPropertyBlock(feedbackOriginalBlocks[i]);
            }
        }

        private void ApplyFeedbackColor(Color color)
        {
            for (var i = 0; i < feedbackRenderers.Length; i++)
            {
                SetRendererColor(feedbackRenderers[i], color);
            }
        }

        /// <summary>反馈结束或回池时归还原覆盖块，空块清除覆盖，不改写共享材质。</summary>
        private void RestoreFeedbackColors()
        {
            for (var i = 0; i < feedbackRenderers.Length && i < feedbackOriginalBlocks.Length; i++)
            {
                if (feedbackRenderers[i] != null)
                    feedbackRenderers[i].SetPropertyBlock(feedbackOriginalBlocks[i].isEmpty ? null : feedbackOriginalBlocks[i]);
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

        private void EnsureAgent()
        {
            if (navMeshAgent == null)
            {
                navMeshAgent = GetComponent<NavMeshAgent>();
            }
        }

        /// <summary>对象池复用时恢复 Animator 到存活初态，并强制关闭 Root Motion。</summary>
        private void ResetAnimationPresentation()
        {
            animationDead = false;
            animationResetPending = false;
            if (animator == null)
            {
                return;
            }

            animator.applyRootMotion = false;
            if (!animator.isActiveAndEnabled)
            {
                animationResetPending = true;
                return;
            }
            animator.Rebind();
            animator.ResetTrigger(AttackAnimatorParameter);
            animator.ResetTrigger(HitAnimatorParameter);
            animator.SetBool(DeadAnimatorParameter, false);
            animator.SetFloat(MoveSpeedAnimatorParameter, 0f);
            animator.Update(0f);
        }

        private void SetCorpsePresentation(bool enabled)
        {
            if (!enabled)
            {
                isCorpse = false;
            }

            if (livingRenderers != null)
            {
                for (var i = 0; i < livingRenderers.Length; i++)
                {
                    livingRenderers[i].enabled = !enabled;
                }
            }
            ApplyPresentationVariant();



            RefreshHealthBarVisibility();
        }

    }
}
