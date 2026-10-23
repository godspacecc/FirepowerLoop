using UnityEngine;
using FirepowerLoop.Arcade;

namespace Game
{
    /// <summary>只控制可见骨骼：存活时叠加胸部回弹，死亡后短时启用预制物理；由EnemyCtrl统一推进。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPhysicalReaction : MonoBehaviour
    {
        // 旧特殊轮火焰表现仍消费此短时常量；普通敌人的可见寿命由EnemyCorpseFade独立管理。
        public const float CorpseSeconds = 2.2f;
        public const string BodyLayerName = "EnemyRagdoll";
        public const string FloorLayerName = "EnemyRagdollFloor";
        /// <summary>制作阶段固定的动画源与回弹骨骼，不在命中时遍历层级。</summary>
        [SerializeField] private Animator animator;
        [SerializeField] private Transform chest;
        /// <summary>Generic僵尸显式绑定承重骨骼；旧Humanoid预制体仍自动使用Hips。</summary>
        [SerializeField] private Transform hips;
        /// <summary>带独立脚部控制骨的Generic资源：物理交接时挂到小腿，回池时还原动画层级。</summary>
        [SerializeField] private Transform[] detachedEndBones = new Transform[0];
        [SerializeField] private Transform[] endBonePhysicsParents = new Transform[0];
        private Transform[] endBoneAnimationParents;
        private Vector3[] endBoneRestPositions;
        private Quaternion[] endBoneRestRotations;
        /// <summary>预制的简化刚体与对应局部静止姿态，用于每次归还时完整复位。</summary>
        [SerializeField] private Rigidbody[] bodies = new Rigidbody[0];
        [SerializeField] private Vector3[] restPositions = new Vector3[0];
        [SerializeField] private Quaternion[] restRotations = new Quaternion[0];
        /// <summary>当前用途的物理阶段与上一帧叠加状态。</summary>
        private bool dead, simulating, offsetApplied, staggering, hipsOffsetApplied;
        private float hitRemaining, deathAge, simulationSeconds;
        /// <summary>本次死亡使用的力度分层；不在存活阶段施加物理。</summary>
        private int deathImpactKind;
        /// <summary>牛撞尸体沿已采用的抛物线采样，停止后保持落地，不启动爆炸物理或血雾。</summary>
        private readonly ArcadeHerdImpactVisual herdDeathVisual=new ArcadeHerdImpactVisual();
        private float herdDeathInitialProgress;
        public bool IsHerdDeath=>dead&&deathImpactKind==ArcadeDeathImpactRules.Herd;
        private Quaternion chestBeforeOffset;
        private Vector3 hitAxis;
        private const float HitSeconds = .18f;
        private const float StaggerSeconds = .16f;
        /// <summary>首次实例化时缓存骨盆及碰撞体；死亡与回池不再遍历查找组件或分配数组。</summary>
        private Rigidbody hipsBody, chestBody;
        private Collider[] bodyColliders;
        private Vector3 hipsBeforeOffsetPosition, deathDirection;
        private Quaternion hipsBeforeOffsetRotation;
        private static readonly int AttackParameter = Animator.StringToHash("Attack");
        private static readonly int HitParameter = Animator.StringToHash("Hit");
        private static readonly int MoveParameter = Animator.StringToHash("MoveSpeed");
        public bool IsRagdoll => dead;
        public bool IsSimulating => simulating;
        /// <summary>将尚未交接物理的失衡阶段也计入预算，避免同帧批量死亡突破上限。</summary>
        public bool HasPendingPhysics => simulating || staggering;
        public bool IsReady => animator != null && chest != null && (hips != null || animator.isHuman) && bodies.Length == 11 &&
            restPositions.Length == bodies.Length && restRotations.Length == bodies.Length;

        // detectCollisions不随Prefab序列化，实例创建时显式恢复存活休眠态。
        private void Awake() => ResetForReuse();

        /// <summary>专用碰撞层只允许尸体接触平面，不干扰活体、射线、弹丸或其它尸体。</summary>
        public static bool ConfigureCollisionLayers()
        {
            int body = LayerMask.NameToLayer(BodyLayerName), floor = LayerMask.NameToLayer(FloorLayerName);
            if (body < 0 || floor < 0) return false;
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(body, i, i != floor);
                Physics.IgnoreLayerCollision(floor, i, i != body);
            }
            return true;
        }

        /// <summary>命中只刷新短时偏移，不切换物理，也不累积修改上一帧的动画基线。</summary>
        public void PlayHit(Vector3 worldDirection)
        {
            if (!IsReady || dead) return;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < .0001f) worldDirection = -transform.forward;
            hitAxis = chest.parent.InverseTransformDirection(Vector3.Cross(Vector3.up, worldDirection.normalized));
            hitRemaining = HitSeconds;
        }

        /// <summary>Update阶段先撤销上帧叠加；活动时间为零时不消耗回弹或死亡寿命。</summary>
        public void Tick(float deltaTime)
        {
            RemoveOffset();
            float seconds = Mathf.Max(0f, deltaTime);
            if (!dead) { hitRemaining = Mathf.Max(0f, hitRemaining - seconds); return; }
            deathAge += seconds;
            if(deathImpactKind==ArcadeDeathImpactRules.Herd)
            {
                var state=new ArcadeHerdImpactState{Remaining=Mathf.Max(0,ArcadeHerdImpactRules.TotalSeconds-deathAge)};
                float travel=ArcadeHerdImpactRules.Distance*(Mathf.Clamp01(deathAge/ArcadeHerdImpactRules.FlightSeconds)-herdDeathInitialProgress);
                herdDeathVisual.Apply(animator.transform,state,deathDirection,true,travel);return;
            }
            if (simulating && deathAge >= simulationSeconds) Freeze();
        }

        /// <summary>Animator求值后叠加局部回弹或胸部／骨盆失衡，并在此刻交接物理，不修改战斗根。</summary>
        public void LateTick()
        {
            if (!IsReady || offsetApplied) return;
            if (staggering)
            {
                ApplyStagger();
                if (deathAge >= StaggerSeconds)
                {
                    // 最后一次动画求值和失衡叠加完成后，物理直接继承此刻姿态，不先还原站姿。
                    offsetApplied = hipsOffsetApplied = false;
                    ActivatePhysics(false);
                    if (deathAge >= simulationSeconds) Freeze();
                }
                return;
            }
            if (dead || hitRemaining <= 0f) return;
            float progress = 1f - hitRemaining / HitSeconds;
            float angle = 16f * Mathf.Sin(progress * Mathf.PI) * Mathf.Exp(-progress * 1.5f);
            chestBeforeOffset = chest.localRotation;
            chest.localRotation = Quaternion.AngleAxis(angle, hitAxis) * chestBeforeOffset;
            offsetApplied = true;
        }

        /// <summary>只接受一次死亡；速度按表现设置，伤害和击退仍由战斗权威决定。</summary>
        public bool BeginDeath(Vector3 worldDirection, bool explosive)
            => BeginDeathWithImpact(worldDirection, explosive, ArcadeDeathImpactRules.Default);

        /// <summary>空中死亡继承当前牛撞进度，已由权威推进的水平位移不重复施加。</summary>
        public bool BeginHerdDeathFromImpact(Vector3 direction,ArcadeHerdImpactState state)
        {
            if(!BeginDeathWithImpact(direction,false,ArcadeDeathImpactRules.Herd))return false;
            deathAge=Mathf.Clamp(ArcadeHerdImpactRules.TotalSeconds-state.Remaining,0,ArcadeHerdImpactRules.TotalSeconds);
            herdDeathInitialProgress=Mathf.Clamp01(deathAge/ArcadeHerdImpactRules.FlightSeconds);
            herdDeathVisual.Apply(animator.transform,state,deathDirection,true,0);return true;
        }

        /// <summary>最终伤害已裁决，能量爆炸降低抛升，近距霰弹只做低幅后倒。</summary>
        public bool BeginDeathWithImpact(Vector3 worldDirection, bool explosive, int impactKind)
        {
            if (!IsReady || dead) return false;
            CacheRig(); RemoveOffset(); hitRemaining = 0f; deathAge = 0f; dead = true;
            herdDeathInitialProgress=0;
            deathImpactKind = ArcadeDeathImpactRules.Normalize(explosive, impactKind);
            bool shotgun = deathImpactKind == ArcadeDeathImpactRules.CloseShotgun;
            simulationSeconds = explosive ? 1.8f : shotgun ? 1.4f : StaggerSeconds + 1.4f;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < .0001f) worldDirection = -transform.forward;
            deathDirection = worldDirection.normalized;
            if(deathImpactKind==ArcadeDeathImpactRules.Herd)
            {animator.enabled=false;return true;}
            if (explosive || shotgun) ActivatePhysics(explosive);
            else
            {
                staggering = true;
                animator.ResetTrigger(AttackParameter); animator.SetFloat(MoveParameter, 0f); animator.SetTrigger(HitParameter);
            }
            return true;
        }

        /// <summary>短暂保留动画并叠加胸部前倾、骨盆下沉；只改骨骼，不改战斗根或生命状态。</summary>
        private void ApplyStagger()
        {
            float progress = Mathf.Clamp01(deathAge / StaggerSeconds);
            progress = progress * progress * (3f - 2f * progress);
            var worldAxis = Vector3.Cross(Vector3.up, deathDirection);
            hipsBeforeOffsetPosition = hips.localPosition; hipsBeforeOffsetRotation = hips.localRotation;
            hips.localPosition += hips.parent.InverseTransformVector(Vector3.down * (.1f * progress));
            hips.localRotation = Quaternion.AngleAxis(9f * progress, hips.parent.InverseTransformDirection(worldAxis)) * hipsBeforeOffsetRotation;
            hipsOffsetApplied = true;
            chestBeforeOffset = chest.localRotation;
            chest.localRotation = Quaternion.AngleAxis(18f * progress, chest.parent.InverseTransformDirection(worldAxis)) * chestBeforeOffset;
            offsetApplied = true;
        }

        /// <summary>仅向胸部与骨盆提供主要速度，四肢从静止由关节带动，形成非同步甩动。</summary>
        private void ActivatePhysics(bool explosive)
        {
            staggering = false; simulating = true; animator.enabled = false;
            for (int i = 0; i < detachedEndBones.Length; i++)
                detachedEndBones[i].SetParent(endBonePhysicsParents[i], true);
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                bodyColliders[i].enabled = true;
                body.detectCollisions = true; body.isKinematic = false;
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
            var axis = Vector3.Cross(Vector3.up, deathDirection);
            hipsBody.velocity = deathDirection * (explosive ? 7f : .65f) + Vector3.up * (explosive ? 6.2f : -.1f);
            chestBody.velocity = deathDirection * (explosive ? 8.5f : 1.4f) + Vector3.up * (explosive ? 8.2f : -.15f);
            hipsBody.angularVelocity = axis * (explosive ? 1.2f : .45f);
            chestBody.angularVelocity = axis * (explosive ? 4.5f : 1.5f) + Vector3.up * (explosive ? .65f : .15f);
            if (deathImpactKind == ArcadeDeathImpactRules.EnergyBlast)
            {
                hipsBody.velocity = deathDirection * 3.2f + Vector3.up * 1.6f;
                chestBody.velocity = deathDirection * 4f + Vector3.up * 2.2f;
                hipsBody.angularVelocity = axis * .6f;
                chestBody.angularVelocity = axis * 1.6f + Vector3.up * .2f;
            }
            else if (deathImpactKind == ArcadeDeathImpactRules.CloseShotgun)
            {
                hipsBody.velocity = deathDirection * 2.8f + Vector3.up * .25f;
                chestBody.velocity = deathDirection * 3.8f + Vector3.up * .65f;
                hipsBody.angularVelocity = axis * .35f;
                chestBody.angularVelocity = axis * .9f;
            }
        }

        /// <summary>每个实例只建立一次缓存；Generic使用预制引用，Humanoid兼容旧骨盆绑定。</summary>
        private void CacheRig()
        {
            if (bodyColliders != null) return;
            if (hips == null && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            hipsBody = hips.GetComponent<Rigidbody>(); chestBody = chest.GetComponent<Rigidbody>();
            bodyColliders = new Collider[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) bodyColliders[i] = bodies[i].GetComponent<Collider>();
            endBoneAnimationParents = new Transform[detachedEndBones.Length];
            endBoneRestPositions = new Vector3[detachedEndBones.Length];
            endBoneRestRotations = new Quaternion[detachedEndBones.Length];
            for (int i = 0; i < detachedEndBones.Length; i++)
            {
                endBoneAnimationParents[i] = detachedEndBones[i].parent;
                endBoneRestPositions[i] = detachedEndBones[i].localPosition;
                endBoneRestRotations[i] = detachedEndBones[i].localRotation;
            }
        }

        /// <summary>短时模拟后关闭求解和碰撞，爆炸多留落地时间，保留最后可见姿态直到回池。</summary>
        private void Freeze()
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = true; body.detectCollisions = false;
                bodyColliders[i].enabled = false;
            }
            simulating = false;
        }

        /// <summary>物理预算超额时保留当前可见姿态并立即停止求解，取消尚未执行的失衡交接。</summary>
        public void StopDeathSimulation()
        {
            if (!dead) return;
            // 密集击杀可能尚未倒下或仍在空中；仅骨骼回退到贴地姿态，不增加求解或移动战斗根。
            if (staggering || hips.position.y - transform.position.y > .65f)
            {
                var direction = deathDirection;
                ResetForReuse();
                dead = true;
                deathDirection = direction;
                animator.enabled = false;
                hips.rotation = Quaternion.AngleAxis(90f, Vector3.Cross(Vector3.up, direction)) * hips.rotation;
                hips.position += Vector3.up * (transform.position.y + .25f - hips.position.y);
                return;
            }
            offsetApplied = hipsOffsetApplied = staggering = false;
            animator.enabled = false;
            Freeze();
        }

        /// <summary>禁用、切图和新用途统一清除速度、回弹及骨骼位姿，下一次激活可立即播放动画。</summary>
        public void ResetForReuse()
        {
            herdDeathVisual.Restore();
            herdDeathInitialProgress=0;
            RemoveOffset();
            if (IsReady)
            {
                CacheRig();
                Freeze();
                for (int i = 0; i < detachedEndBones.Length; i++)
                {
                    detachedEndBones[i].SetParent(endBoneAnimationParents[i], false);
                    detachedEndBones[i].localPosition = endBoneRestPositions[i];
                    detachedEndBones[i].localRotation = endBoneRestRotations[i];
                }
                for (int i = 0; i < bodies.Length; i++)
                {
                    bodies[i].transform.localPosition = restPositions[i];
                    bodies[i].transform.localRotation = restRotations[i];
                }
                animator.enabled = true;
            }
            dead = simulating = staggering = false; hitRemaining = deathAge = 0f;
            deathDirection = Vector3.zero;
            deathImpactKind = ArcadeDeathImpactRules.Default;
        }

        private void RemoveOffset()
        {
            if (offsetApplied && chest != null) chest.localRotation = chestBeforeOffset;
            if (hipsOffsetApplied && hips != null)
            { hips.localPosition = hipsBeforeOffsetPosition; hips.localRotation = hipsBeforeOffsetRotation; }
            offsetApplied = hipsOffsetApplied = false;
        }
    }
}
