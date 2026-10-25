using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>已采用码头攀爬骨骼片段，由本实例Prefab依赖持有。</summary>
        [SerializeField] private AnimationClip dockSeaEntryClip;
        /// <summary>预制资源持有泡沫材质；每个池中演员只复用一张三环网格。</summary>
        [SerializeField] private Material dockSeaEntryRippleMaterial;
        private ArcadeDockSeaEntryState dockSeaEntry;
        private Transform dockVisualRoot, dockHips;
        private Vector3 dockRestPosition, dockForward, dockUp;
        private Quaternion dockRestRotation;
        private bool dockSampling, dockAnimatorWasEnabled;
        private GameObject dockRippleObject;
        private Mesh dockRippleMesh;
        private MeshRenderer dockRippleRenderer;
        private Vector3[] dockRippleVertices;
        private const int DockRippleSegments = 28;

        /// <summary>消费单调递增的权威进度；重连直达当前姿态，不靠本地时钟补播或重新进海。</summary>
        public void SetDockSeaEntry(ArcadeDockSeaEntryState state, Vector3 forward, Vector3 up)
        {
            if (animationDead || isCorpse || !state.Started || !DockFinite(state.Age) ||
                state.Age < 0 || state.Age > ArcadeDockSeaEntryRules.TotalSeconds ||
                !DockFinite(state.Lane) || Mathf.Min(Mathf.Abs(state.Lane + 3),
                    Mathf.Min(Mathf.Abs(state.Lane + 4.1f), Mathf.Abs(state.Lane + 5.2f))) > .001f ||
                dockSeaEntry.Started && state.Age < dockSeaEntry.Age ||
                !DockFinite(forward.sqrMagnitude) || !DockFinite(up.sqrMagnitude) ||
                forward.sqrMagnitude < .001f || up.sqrMagnitude < .001f) return;
            dockSeaEntry = state;
            if (!state.Active) { FinishDockSeaEntry(); return; }
            if (animator == null || dockSeaEntryClip == null) return;
            if (dockVisualRoot == null)
            {
                dockVisualRoot = animator.transform.parent;
                if (dockVisualRoot == null || dockVisualRoot == transform) { dockVisualRoot = null; return; }
                foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "Hips") { dockHips = bone; break; }
                if (dockHips == null) { dockVisualRoot = null; return; }
                dockRestPosition = dockVisualRoot.localPosition; dockRestRotation = dockVisualRoot.localRotation;
            }
            if (!dockSampling)
            {
                ResetMeleeAttack(); ResetHerdImpact();
                dockAnimatorWasEnabled = animator.enabled; dockSampling = true;
            }
            dockForward = forward.normalized; dockUp = up.normalized;
            animator.enabled = false;
        }

        /// <summary>局部骨骼先采样，视觉根绕髋部倾斜；战斗根只调整朝向，不改平面位置。</summary>
        private void ApplyDockSeaEntry()
        {
            if (!dockSampling || animationDead || isCorpse) return;
            dockVisualRoot.SetLocalPositionAndRotation(dockRestPosition, dockRestRotation);
            transform.rotation = Quaternion.LookRotation(dockForward, dockUp);
            dockSeaEntryClip.SampleAnimation(animator.gameObject, dockSeaEntry.Age);
            var hips = transform.InverseTransformPoint(dockHips.position);
            var pose = ArcadeDockSeaEntryRules.Sample(dockSeaEntry.Age);
            var lean = Quaternion.Euler(pose.Tilt, 0, 0);
            dockVisualRoot.localRotation = lean * dockRestRotation;
            dockVisualRoot.localPosition = hips - lean * (hips - dockRestPosition) + Vector3.up * pose.Height;
            SampleDockRipples();
        }

        /// <summary>安全走入平台后恢复正常动画；接在已经采用的地面步态当前脚步上。</summary>
        private void FinishDockSeaEntry()
        {
            if (!dockSampling) return;
            RestoreDockVisualRoot(); dockSampling = false;
            animator.enabled = dockAnimatorWasEnabled;
            if (animator.isActiveAndEnabled)
            {
                animator.SetFloat(MoveSpeedAnimatorParameter, .9f);
                animator.Play("Run", 0, Mathf.Repeat((ArcadeDockSeaEntryRules.TotalSeconds - ArcadeDockSeaEntryRules.ClimbSeconds) / .82f, 1));
                animator.Update(0);
            }
            if (dockRippleObject != null) dockRippleObject.SetActive(false);
        }

        /// <summary>死亡继承水中或攀爬现场姿态，不能先跳回地面再启动骨骼物理。</summary>
        private void StopDockSeaEntryForDeath()
        {
            if (!dockSampling) return;
            dockSampling = false; animator.enabled = dockAnimatorWasEnabled;
            if (dockRippleObject != null) dockRippleObject.SetActive(false);
        }

        /// <summary>回池、隐藏与新用途统一还原；清除终态记录才允许下一只敌人重新上岸。</summary>
        private void ResetDockSeaEntry()
        {
            if (dockSampling && animator != null) animator.enabled = dockAnimatorWasEnabled;
            dockSampling = false; dockSeaEntry = default;
            RestoreDockVisualRoot();
            if (dockRippleObject != null) dockRippleObject.SetActive(false);
        }

        private void RestoreDockVisualRoot()
        {
            if (dockVisualRoot != null) dockVisualRoot.SetLocalPositionAndRotation(dockRestPosition, dockRestRotation);
        }

        /// <summary>背桶按原地面挂点换算到视觉根的静止空间；迟到绑定也立即随当前攀爬姿态。</summary>
        public void AttachCarriedVisual(Transform item)
        {
            var parent = animator != null ? animator.transform.parent : null;
            if (item == null || parent == null || parent == transform) return;
            var position = transform.InverseTransformPoint(item.position);
            var rotation = Quaternion.Inverse(transform.rotation) * item.rotation;
            var restPosition = dockVisualRoot == parent ? dockRestPosition : parent.localPosition;
            var restRotation = dockVisualRoot == parent ? dockRestRotation : parent.localRotation;
            item.SetParent(parent, true);
            item.localPosition = Matrix4x4.TRS(restPosition, restRotation, parent.localScale).inverse.MultiplyPoint3x4(position);
            item.localRotation = Quaternion.Inverse(restRotation) * rotation;
        }

        /// <summary>三条细水纹共用一个可复用网格；只在海面阶段按同一权威年龄展开。</summary>
        private void SampleDockRipples()
        {
            if (dockSeaEntryRippleMaterial == null || dockSeaEntry.Age >= 3.05f)
            { if (dockRippleObject != null) dockRippleObject.SetActive(false); return; }
            if (dockRippleMesh == null)
            {
                dockRippleObject = new GameObject("DockSeaEntryRipples", typeof(MeshFilter), typeof(MeshRenderer));
                dockRippleObject.layer = gameObject.layer; dockRippleObject.transform.SetParent(transform, false);
                dockRippleMesh = new Mesh { name = "DockSeaEntryRipples" }; dockRippleMesh.MarkDynamic();
                dockRippleVertices = new Vector3[3 * DockRippleSegments * 2];
                var triangles = new int[3 * DockRippleSegments * 6];
                for (int ring = 0; ring < 3; ring++) for (int j = 0; j < DockRippleSegments; j++)
                {
                    int a = (ring * DockRippleSegments + j) * 2;
                    int b = (ring * DockRippleSegments + (j + 1) % DockRippleSegments) * 2;
                    int k = (ring * DockRippleSegments + j) * 6;
                    triangles[k] = a; triangles[k + 1] = b; triangles[k + 2] = a + 1;
                    triangles[k + 3] = a + 1; triangles[k + 4] = b; triangles[k + 5] = b + 1;
                }
                dockRippleMesh.vertices = dockRippleVertices; dockRippleMesh.triangles = triangles;
                dockRippleObject.GetComponent<MeshFilter>().sharedMesh = dockRippleMesh;
                dockRippleRenderer = dockRippleObject.GetComponent<MeshRenderer>();
                dockRippleRenderer.sharedMaterial = dockSeaEntryRippleMaterial;
                dockRippleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dockRippleRenderer.receiveShadows = false;
            }
            dockRippleObject.SetActive(true);
            var east = -dockForward; var side = Vector3.Cross(east, dockUp);
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = .26f + Mathf.Repeat(dockSeaEntry.Age * .6f + ring * .32f, 1) * .7f;
                for (int j = 0; j < DockRippleSegments; j++) for (int edge = 0; edge < 2; edge++)
                {
                    float angle = j * Mathf.PI * 2 / DockRippleSegments, r = radius + (edge == 0 ? -.009f : .009f);
                    var world = transform.position - dockUp * .332f + east * (Mathf.Cos(angle) * r) + side * (Mathf.Sin(angle) * r * .6f);
                    dockRippleVertices[(ring * DockRippleSegments + j) * 2 + edge] = transform.InverseTransformPoint(world);
                }
            }
            dockRippleMesh.vertices = dockRippleVertices; dockRippleMesh.RecalculateBounds();
        }

        /// <summary>最终释放显式归还运行时网格，不销毁共享正式材质。</summary>
        private void ReleaseDockSeaEntryResources()
        {
            if (dockRippleObject != null)
            { if (Application.isPlaying) Destroy(dockRippleObject); else DestroyImmediate(dockRippleObject); }
            if (dockRippleMesh != null)
            { if (Application.isPlaying) Destroy(dockRippleMesh); else DestroyImmediate(dockRippleMesh); }
            dockRippleObject = null; dockRippleMesh = null; dockRippleRenderer = null; dockRippleVertices = null;
        }
        private static bool DockFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
