using TMPro;
using UnityEngine;

namespace Game
{
    public sealed class DamageNumberView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private CanvasGroup canvasGroup;

        public void Bind(int damage, Color color)
        {
            label.text = $"-{Mathf.Max(0, damage)}";
            label.color = color;
            canvasGroup.alpha = 1f;
        }

        public void SetAlpha(float alpha) => canvasGroup.alpha = Mathf.Clamp01(alpha);

        public void FaceCamera(Camera camera)
        {
            if (camera == null) return;
            Vector3 direction = transform.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction, camera.transform.up);
        }

        public bool Validate(out string error)
        {
            if (label == null) { error = "伤害飘字缺少 TMP 标签"; return false; }
            if (canvasGroup == null) { error = "伤害飘字缺少 CanvasGroup"; return false; }
            error = null;
            return true;
        }
    }
}
