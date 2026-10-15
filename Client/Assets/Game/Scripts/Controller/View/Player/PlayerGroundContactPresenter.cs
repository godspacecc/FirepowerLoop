using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 动画求值后校准玩家可见脚底，只移动 Humanoid 子根；不改变碰撞、Unit 位置、重力或移动规则。
    /// 死亡、离地或表现停用时恢复 Prefab 姿态，不承担坡地逐脚 IK。
    /// </summary>
    internal sealed class PlayerGroundContactPresenter : IDisposable
    {
        /// <summary>脚底与支撑面的表现间隙，单位米，避免平面精度误差造成闪烁。</summary>
        private const float SoleClearance = .003f;
        private readonly Animator animator;
        private readonly CharacterController controller;
        private readonly float authoredLocalY;
        private readonly SkinnedMeshRenderer[] renderers;
        /// <summary>实例独占的 CPU 蒙皮采样缓冲；复用网格和顶点列表，View 销毁时释放。</summary>
        private readonly Mesh sampledMesh = new Mesh { name = "PlayerGroundContactSample", hideFlags = HideFlags.HideAndDontSave };
        private readonly List<Vector3> vertices = new List<Vector3>();

        public PlayerGroundContactPresenter(Animator animator, CharacterController controller)
        {
            this.animator = animator;
            this.controller = controller;
            authoredLocalY = animator.transform.localPosition.y;
            renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        /// <summary>在 LateUpdate 动画求值后调用；仅修正已落地、存活且启用的玩家模型，重复调用不累积偏移。</summary>
        public void Apply(bool alive)
        {
            if (animator == null || controller == null || animator.transform == controller.transform) return;
            if (!alive || !animator.isActiveAndEnabled || !controller.enabled || !controller.isGrounded)
            {
                Restore();
                return;
            }

            var soles = FindVisibleSoles();
            if (soles == null)
            {
                Restore();
                return;
            }

            // BakeMesh 支持未启用 Read/Write 的导入网格；只采样可见鞋脚，整套造型才回退到全身网格。
            soles.BakeMesh(sampledMesh);
            sampledMesh.GetVertices(vertices);
            if (vertices.Count == 0) { Restore(); return; }
            var soleY = float.PositiveInfinity;
            var matrix = soles.transform.localToWorldMatrix;
            foreach (var vertex in vertices) soleY = Mathf.Min(soleY, matrix.MultiplyPoint3x4(vertex).y);

            // 胶囊实际接触面比其几何底部低一个 Skin Width；保持物理皮肤宽度，只补偿可见模型。
            var supportY = controller.bounds.min.y - controller.skinWidth * Mathf.Abs(controller.transform.lossyScale.y);
            var offset = supportY + SoleClearance - soleY;
            animator.transform.position += Vector3.up * offset;
        }

        /// <summary>优先使用正式分部位鞋脚；九套全身造型使用各自网格，隐藏的旧外观不参与测量。</summary>
        private SkinnedMeshRenderer FindVisibleSoles()
        {
            SkinnedMeshRenderer wholeBody = null;
            foreach (var skin in renderers)
            {
                if (skin == null || !skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                if (skin.name == "Base_Feet" || skin.name.StartsWith("Feet_", StringComparison.Ordinal)) return skin;
                if (skin.name.StartsWith("Character_", StringComparison.Ordinal)) wholeBody = skin;
            }
            return wholeBody;
        }

        private void Restore()
        {
            var position = animator.transform.localPosition;
            position.y = authoredLocalY;
            animator.transform.localPosition = position;
        }

        /// <summary>随所属 View 销毁，释放唯一采样网格；不销毁源美术网格。</summary>
        public void Dispose()
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(sampledMesh);
            else UnityEngine.Object.DestroyImmediate(sampledMesh);
        }
    }
}
