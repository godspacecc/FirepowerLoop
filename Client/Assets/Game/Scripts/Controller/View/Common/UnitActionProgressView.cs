using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    [Serializable]
    public sealed class UnitActionProgressView
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private Image radialFill;
        [SerializeField] private Image centerIcon;
        [NonSerialized] private Sprite defaultSprite;
        [NonSerialized] private string loadedAddress;

        public void Initialize()
        {
            if (centerIcon != null && defaultSprite == null) defaultSprite = centerIcon.sprite;
            Hide();
        }

        public void Show(PlayerActionSnapshot snapshot)
        {
            if (root == null) return;
            if (centerIcon != null && !string.Equals(loadedAddress, snapshot.IconAddress, StringComparison.Ordinal))
            {
                loadedAddress = snapshot.IconAddress;
                centerIcon.sprite = string.IsNullOrWhiteSpace(snapshot.IconAddress)
                    ? defaultSprite
                    : ResourceLoader.LoadAsset<Sprite>(snapshot.IconAddress) ?? defaultSprite;
            }
            if (radialFill != null) radialFill.fillAmount = Mathf.Clamp01(snapshot.Progress01);
            if (!root.activeSelf) root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        internal void FaceWorld(Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (worldCanvas == null) return;
            var direction = worldCanvas.transform.position - cameraPosition;
            if (direction.sqrMagnitude > 0.0001f) worldCanvas.transform.rotation = Quaternion.LookRotation(direction, cameraUp);
        }
    }
}
