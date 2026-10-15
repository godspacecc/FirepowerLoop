using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>仅表现弓弦与弓臂形变，由角色 Presenter 在动画求值后刷新；不读取输入、不裁决蓄力或发射。</summary>
    public sealed class BowVisualView : MonoBehaviour, System.IDisposable
    {
        [SerializeField] private MeshFilter bowMesh;
        /// <summary>本模型握柄为原点、弓弦位于正 X；数值均为米，与正式弓模型配套。</summary>
        private const float StringRestX = .17f;
        private const float HalfHeight = .61f;
        private Mesh sourceMesh;
        private Mesh instanceMesh;
        private Vector3[] restVertices;
        private Vector3[] vertices;
        private bool[] stringVertices;
        private Matrix4x4 viewToMesh;
        private bool released;
        private Vector3 nockLocal = new Vector3(StringRestX, 0f, 0f);
        private Vector3 upperTipLocal = new Vector3(StringRestX, HalfHeight, 0f);

        public Vector3 NockWorldPosition => transform.TransformPoint(nockLocal);
        public Vector3 UpperTipWorldPosition => transform.TransformPoint(upperTipLocal);

        /// <summary>动画采样后调用；权重 0 为松弦，1 为跟随拉弦手，重复调用不累计形变。</summary>
        public void Refresh(Transform drawingHand, float drawWeight)
        {
            if (drawingHand == null || released || !Finite(drawingHand.position) || !float.IsFinite(drawWeight)) return;
            // Humanoid 手腕局部轴会随放箭翻转；只校准弓实例，保持竖直且弦朝拉弦手，不改手骨。
            var pullDirection = Vector3.ProjectOnPlane(drawingHand.position - transform.position, Vector3.up);
            if (pullDirection.sqrMagnitude > .01f)
                transform.rotation = Quaternion.LookRotation(Vector3.Cross(pullDirection.normalized, Vector3.up), Vector3.up);
            if (!EnsureMesh()) return;
            var hand = transform.InverseTransformPoint(drawingHand.position);
            if (!Finite(hand) || !float.IsFinite(drawWeight)) return;
            // 取箭、垂手时不把弓弦拖到身体下方；放箭阶段由已有动作的表现包络撤销拉弦。
            var reachWeight = 1f - Mathf.InverseLerp(.25f, .45f, Mathf.Abs(hand.y));
            var weight = Mathf.Clamp01(drawWeight) * reachWeight;
            var restNock = new Vector3(StringRestX, 0f, 0f);
            nockLocal = Vector3.Lerp(restNock, hand, weight);
            var draw = Mathf.Clamp01((nockLocal.x - StringRestX) / .65f);
            upperTipLocal = Bend(new Vector3(StringRestX, HalfHeight, 0f), draw);
            var lowerTip = Bend(new Vector3(StringRestX, -HalfHeight, 0f), draw);
            for (var i = 0; i < vertices.Length; i++)
            {
                var rest = restVertices[i];
                Vector3 point;
                if (stringVertices[i])
                {
                    var t = Mathf.Clamp01(Mathf.Abs(rest.y) / HalfHeight);
                    var center = Vector3.Lerp(nockLocal, rest.y >= 0f ? upperTipLocal : lowerTip, t);
                    point = center + new Vector3(rest.x - StringRestX, 0f, rest.z);
                }
                else point = Bend(rest, draw);
                vertices[i] = viewToMesh.MultiplyPoint3x4(point);
            }
            instanceMesh.vertices = vertices;
            instanceMesh.RecalculateNormals();
            instanceMesh.RecalculateBounds();
        }

        /// <summary>现有放箭片段的视觉包络；只控制弦形状，不改变任何攻击时机。</summary>
        public static float AttackDrawWeight(float normalizedTime)
        {
            if (normalizedTime < .5f) return Mathf.InverseLerp(.42f, .5f, normalizedTime);
            if (normalizedTime < .7f) return 1f;
            return 1f - Mathf.InverseLerp(.7f, .75f, normalizedTime);
        }

        private static Vector3 Bend(Vector3 rest, float draw)
        {
            var along = Mathf.Clamp01(Mathf.Abs(rest.y) / HalfHeight);
            rest.x += .16f * draw * along * along;
            rest.y -= Mathf.Sign(rest.y) * .09f * draw * along * along;
            return rest;
        }

        /// <summary>惰性创建实例网格；源 FBX 永不修改，无可读网格时保持原模型。</summary>
        private bool EnsureMesh()
        {
            if (released) return false;
            if (instanceMesh != null) return true;
            if (bowMesh == null || bowMesh.sharedMesh == null || !bowMesh.sharedMesh.isReadable) return false;
            sourceMesh = bowMesh.sharedMesh;
            var source = sourceMesh.vertices;
            restVertices = new Vector3[source.Length];
            vertices = new Vector3[source.Length];
            stringVertices = new bool[source.Length];
            var meshToView = transform.worldToLocalMatrix * bowMesh.transform.localToWorldMatrix;
            viewToMesh = meshToView.inverse;
            var cordIndices = new HashSet<int>();
            var materials = bowMesh.GetComponent<Renderer>().sharedMaterials;
            for (var sub = 0; sub < sourceMesh.subMeshCount; sub++)
                if (sub < materials.Length && materials[sub] != null && materials[sub].name.StartsWith("HanCord"))
                    foreach (var index in sourceMesh.GetTriangles(sub)) cordIndices.Add(index);
            for (var i = 0; i < source.Length; i++)
            {
                var point = meshToView.MultiplyPoint3x4(source[i]);
                restVertices[i] = point;
                // 弦与弓背使用同材质，按正式模型的直弦截面区分，避免将背衬一起拉成 V 形。
                stringVertices[i] = cordIndices.Contains(i) && Mathf.Abs(point.x - StringRestX) < .006f && Mathf.Abs(point.z) < .006f;
            }
            instanceMesh = Instantiate(sourceMesh);
            instanceMesh.name = sourceMesh.name + "_BowInstance";
            instanceMesh.MarkDynamic();
            bowMesh.sharedMesh = instanceMesh;
            return true;
        }

        /// <summary>换武器或地图释放时还原引用并销毁自有网格；不释放共享资源。</summary>
        public void Dispose()
        {
            released = true;
            if (instanceMesh == null) return;
            if (bowMesh != null && bowMesh.sharedMesh == instanceMesh) bowMesh.sharedMesh = sourceMesh;
            if (Application.isPlaying) Destroy(instanceMesh);
            else DestroyImmediate(instanceMesh);
            instanceMesh = null;
        }

        private void OnDestroy() => Dispose();

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
