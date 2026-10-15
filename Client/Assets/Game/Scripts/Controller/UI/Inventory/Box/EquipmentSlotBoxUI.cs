using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    public sealed class EquipmentSlotBoxUI : BaseBoxUI, IPointerClickHandler
    {
        public Button ClickButton;
        public Image IconImage;
        public GameObject EmptyIconRoot;
        public TMP_Text AmountText;
        public TMP_Text StateText;
        public GameObject SelectedFrame;
        public GameObject ActiveFrame;

        private WeaponSlot slot;
        private bool disabled;

        public WeaponSlot Slot => slot;

        public event Action<WeaponSlot> Clicked;

        public void Bind(
            WeaponSlot nextSlot,
            Sprite icon,
            bool hasItem,
            bool showUnarmed,
            string unarmedText,
            bool selected,
            bool active,
            bool disabledSlot)
        {
            slot = nextSlot;
            disabled = disabledSlot;

            if (IconImage != null)
            {
                IconImage.sprite = icon;
                IconImage.gameObject.SetActive(hasItem && icon != null);
            }

            if (EmptyIconRoot != null)
            {
                EmptyIconRoot.SetActive(!hasItem || icon == null);
            }

            if (AmountText != null)
            {
                AmountText.text = hasItem ? "1" : string.Empty;
                AmountText.gameObject.SetActive(hasItem);
            }

            if (StateText != null)
            {
                StateText.text = showUnarmed ? unarmedText ?? string.Empty : string.Empty;
                StateText.gameObject.SetActive(showUnarmed);
                StateText.raycastTarget = false;
            }

            if (SelectedFrame != null)
            {
                SelectedFrame.SetActive(selected);
            }

            if (ActiveFrame != null)
            {
                ActiveFrame.SetActive(active);
            }

            if (ClickButton != null)
            {
                ClickButton.interactable = !disabledSlot;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (disabled)
            {
                return;
            }

            Clicked?.Invoke(slot);
        }

        public void ClearCallbacks()
        {
            Clicked = null;
        }
    }
}
