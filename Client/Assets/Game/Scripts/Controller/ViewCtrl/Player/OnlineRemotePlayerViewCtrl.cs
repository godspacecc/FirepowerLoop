using UnityEngine;

namespace Game
{
    /// <summary>插值联机远端玩家表现，并把快照位移换算为 Humanoid 移动速度。</summary>
    public sealed class OnlineRemotePlayerViewCtrl
    {
        private const float InterpolationDurationSeconds = 0.1f;

        private readonly GameObject instance;
        private Vector3 startPosition;
        private Vector3 targetPosition;
        private Quaternion startRotation;
        private Quaternion targetRotation;
        private float elapsed;
        private bool interpolating;

        public OnlineRemotePlayerViewCtrl(GameObject instance)
        {
            this.instance = instance;
        }
        public bool IsValid => instance != null;
        public GameObject Instance => instance;

        public void SetVisible(bool visible) { if (instance != null) instance.SetActive(visible); }
        public void SetAlive(bool alive,bool playTransition=true)
        {
            var view = instance != null ? instance.GetComponent<PlayerView>() : null;
            view?.SetAlivePresentation(alive,playTransition);
            // 远端由快照插值定位，存活表现不能重新启用本机运动碰撞权威。
            if (view != null && view.CharacterController != null) view.CharacterController.enabled = false;
        }
        public void SetArcadePresentationActive(bool active) { instance?.GetComponent<PlayerView>()?.SetArcadePresentationActive(active); }

        public void Snap(Vector3 localPosition, float yaw)
        {
            if (instance == null) return;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            targetPosition = localPosition;
            targetRotation = instance.transform.localRotation;
            interpolating = false;
            instance.GetComponent<PlayerView>()?.SetMovementSpeed(0f);
        }

        public void SetTarget(Vector3 localPosition, float yaw)
        {
            if (instance == null) return;
            startPosition = instance.transform.localPosition;
            startRotation = instance.transform.localRotation;
            targetPosition = localPosition;
            targetRotation = Quaternion.Euler(0f, yaw, 0f);
            elapsed = 0f;
            interpolating = true;
            var horizontalDelta = targetPosition - startPosition;
            horizontalDelta.y = 0f;
            instance.GetComponent<PlayerView>()?.SetMovementSpeed(horizontalDelta.magnitude / InterpolationDurationSeconds);
        }

        public void PlayMeleeFeedback(int weaponItemId)
        {
            if (instance == null) return;
            PlayerView view = instance.GetComponent<PlayerView>();
            if (view == null) return;
            if (weaponItemId == 5) view.PlayWeaponSwingFeedback();
            else view.PlayAttackFeedback();
        }

        public void PlayProjectileFeedback() { instance?.GetComponent<PlayerView>()?.PlayProjectileFireFeedback(); }
        public void PlayHurtFeedback() { instance?.GetComponent<PlayerView>()?.PlayHurtFeedback(); }
        public void ShowDamage(int damage) { instance?.GetComponent<PlayerView>()?.ShowDamage(damage, true); }

        public void Tick(float deltaTime)
        {
            if (instance == null) return;
            PlayerView view = instance.GetComponent<PlayerView>();
            view?.TickCombatFeedback(deltaTime);
            if (!interpolating) return;
            elapsed += deltaTime;
            float t = Mathf.Clamp01(elapsed / InterpolationDurationSeconds);
            instance.transform.localPosition = Vector3.Lerp(startPosition, targetPosition, t);
            instance.transform.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
            if (t < 1f) return;
            interpolating = false;
            view?.SetMovementSpeed(0f);
        }
    }
}
