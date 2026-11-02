using UnityEngine;

namespace Game
{
    /// <summary>由幸存者战场驱动，仅淡化挡住主角的仓房屋面。</summary>
    public sealed class SurvivorBarnRoof : MonoBehaviour
    {
        /// <summary>沿用正式透明表面参数，保留屋顶原纹理。</summary>
        public Material TransparentMaterial;
        private MeshRenderer roof;
        private Transform leftPlane, rightPlane;
        private CameraOccluderView.FadeLease fade;
        /// <summary>当前不透明度，组件停用时恢复。</summary>
        public float Opacity { get; private set; } = 1f;

        /// <summary>从主角实际屏幕位置生成视线，兼容正交及透视镜头。</summary>
        public void Tick(Camera camera, Vector3 player, float seconds)
        {
            if (!isActiveAndEnabled) return;
            if (roof == null) roof = GetComponent<MeshRenderer>();
            if (roof == null || TransparentMaterial == null) return;
            bool covered = false;
            if (camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(player);
                if (viewport.z > camera.nearClipPlane)
                {
                    Ray ray = camera.ViewportPointToRay(viewport);
                    float distance = Vector3.Dot(player - ray.origin, ray.direction);
                    if (leftPlane == null) leftPlane = transform.Find("RoofPlaneLeft");
                    if (rightPlane == null) rightPlane = transform.Find("RoofPlaneRight");
                    covered = CrossesPlane(leftPlane, ray, distance) || CrossesPlane(rightPlane, ray, distance);
                }
            }
            Opacity = Mathf.MoveTowards(Opacity, covered ? .32f : 1f, Mathf.Max(0,seconds) * 3f);
            if (Opacity >= 1f) { Restore(); return; }
            fade ??= new CameraOccluderView.FadeLease(roof, TransparentMaterial);
            fade.Apply(Opacity);
        }

        /// <summary>视线与实际屋面矩形相交且交点在主角前方时才淡化。</summary>
        private static bool CrossesPlane(Transform plane, Ray ray, float distance)
        {
            if (plane == null) return false;
            Vector3 origin = plane.InverseTransformPoint(ray.origin);
            Vector3 direction = plane.worldToLocalMatrix.MultiplyVector(ray.direction);
            var localRay = new Ray(origin, direction.normalized);
            var box = new Bounds(Vector3.zero, Vector3.one);
            if (box.Contains(origin)) return distance > 0;
            if (!box.IntersectRay(localRay, out float hit)) return false;
            Vector3 worldHit = plane.TransformPoint(localRay.GetPoint(hit));
            return Vector3.Dot(worldHit - ray.origin, ray.direction) < distance;
        }

        /// <summary>恢复不透明材质与阴影，并释放临时材质。</summary>
        public void Restore()
        {
            fade?.Dispose();
            fade = null;
            Opacity = 1f;
        }

        private void OnDisable() => Restore();
    }
}
