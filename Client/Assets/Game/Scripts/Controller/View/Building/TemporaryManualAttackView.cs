using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed class TemporaryManualAttackView : MonoBehaviour
    {
        public Collider InteractCollider;
        public Transform YawRoot;
        public Transform Muzzle;
        public GameObject ReloadProgressRoot;
        public Image ReloadProgressFill;
        public Image ReloadProgressIcon;
        public GameObject MagazineRoot;
        public Canvas MagazineCanvas;
        public TMP_Text MagazineText;

        private const float EmptyAmmoFeedbackHalfDuration = 0.1f;
        private const float EmptyAmmoFeedbackDuration = EmptyAmmoFeedbackHalfDuration * 2f;
        private Vector3 magazineBaseScale;
        private bool hasMagazineBaseScale;
        private float emptyAmmoFeedbackElapsed = -1f;

        public bool ValidateReferences(out string error)
        {
            if (InteractCollider == null)
            {
                error = "临时建筑手动攻击视图缺少交互碰撞器";
                return false;
            }

            if (YawRoot == null)
            {
                error = "临时建筑手动攻击视图缺少偏航根节点";
                return false;
            }

            if (Muzzle == null)
            {
                error = "临时建筑手动攻击视图缺少枪口";
                return false;
            }

            if (ReloadProgressRoot == null || ReloadProgressFill == null || ReloadProgressIcon == null || MagazineRoot == null || MagazineCanvas == null || MagazineText == null)
            {
                error = "临时建筑手动攻击视图缺少世界 HUD 引用";
                return false;
            }

            InteractCollider.isTrigger = true;
            ReloadProgressRoot.SetActive(false);
            MagazineRoot.SetActive(false);
            error = null;
            return true;
        }

        public void SetOperating(bool active)
        {
            if (YawRoot != null) YawRoot.localScale = active ? Vector3.one * 1.08f : Vector3.one;
            if (!active)
            {
                StopEmptyAmmoFeedback();
            }
        }

        public bool TryGetPose(out Vector3 origin, out Vector3 forward)
        {
            origin = Muzzle != null ? Muzzle.position : transform.position + Vector3.up;
            forward = YawRoot != null ? YawRoot.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.000001f) forward = Vector3.forward;
            forward.Normalize();
            return true;
        }

        public void SetAimDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (YawRoot == null || direction.sqrMagnitude < 0.000001f)
            {
                return;
            }

            YawRoot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        public void FaceWorldHuds(Vector3 cameraPosition, Vector3 cameraUp)
        {
            Face(ReloadProgressRoot != null ? ReloadProgressRoot.transform : null, cameraPosition, cameraUp);
            Face(MagazineCanvas != null ? MagazineCanvas.transform : null, cameraPosition, cameraUp);
        }

        public void PlayEmptyAmmoFeedback()
        {
            if (!EnsureMagazineBaseScale())
            {
                return;
            }

            MagazineText.transform.localScale = magazineBaseScale;
            emptyAmmoFeedbackElapsed = 0f;
        }

        public void TickEmptyAmmoFeedbackForTests(float unscaledDeltaTime)
        {
            TickEmptyAmmoFeedback(unscaledDeltaTime);
        }

        private void OnDisable()
        {
            StopEmptyAmmoFeedback();
        }

        private void OnDestroy()
        {
            StopEmptyAmmoFeedback();
        }

        private bool EnsureMagazineBaseScale()
        {
            if (MagazineText == null)
            {
                return false;
            }

            if (!hasMagazineBaseScale)
            {
                magazineBaseScale = MagazineText.transform.localScale;
                hasMagazineBaseScale = true;
            }

            return true;
        }

        internal void TickEmptyAmmoFeedback(float unscaledDeltaTime)
        {
            if (emptyAmmoFeedbackElapsed < 0f || !EnsureMagazineBaseScale())
            {
                return;
            }

            emptyAmmoFeedbackElapsed += Mathf.Max(0f, unscaledDeltaTime);
            if (emptyAmmoFeedbackElapsed >= EmptyAmmoFeedbackDuration)
            {
                StopEmptyAmmoFeedback();
                return;
            }

            var factor = emptyAmmoFeedbackElapsed <= EmptyAmmoFeedbackHalfDuration
                ? Mathf.Lerp(1f, 1.2f, emptyAmmoFeedbackElapsed / EmptyAmmoFeedbackHalfDuration)
                : Mathf.Lerp(1.2f, 1f, (emptyAmmoFeedbackElapsed - EmptyAmmoFeedbackHalfDuration) / EmptyAmmoFeedbackHalfDuration);
            MagazineText.transform.localScale = magazineBaseScale * factor;
        }

        private void StopEmptyAmmoFeedback()
        {
            emptyAmmoFeedbackElapsed = -1f;
            if (EnsureMagazineBaseScale())
            {
                MagazineText.transform.localScale = magazineBaseScale;
            }
        }

        private static void Face(Transform target, Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (target == null)
            {
                return;
            }

            var forward = target.position - cameraPosition;
            if (forward.sqrMagnitude > 0.000001f)
            {
                target.rotation = Quaternion.LookRotation(forward.normalized, cameraUp.sqrMagnitude > 0.000001f ? cameraUp.normalized : Vector3.up);
            }
        }
    }
}
