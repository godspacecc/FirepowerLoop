using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    internal sealed class InventoryDragVisualPresenter : IDisposable
    {
        private const string VisualName = "InventoryDragVisual";
        private const float VisualAlpha = 0.8f;

        private Image sourceImage;
        private bool sourceImageWasEnabled;
        private GameObject visualObject;
        private RectTransform visualRect;
        private RectTransform dialogRect;

        public bool IsVisible => visualObject != null;

        public bool TryShow(Image source, PointerEventData eventData)
        {
            Clear();
            if (source == null || source.sprite == null || eventData == null)
            {
                return false;
            }

            GameObject candidate = null;
            try
            {
                var root = UIRoot.Instance;
                var dialog = root != null ? root.GetLayerObj(UILayer.Dialog) as RectTransform : null;
                if (dialog == null)
                {
                    return false;
                }

                candidate = new GameObject(VisualName, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                var candidateRect = candidate.GetComponent<RectTransform>();
                candidateRect.SetParent(dialog, false);
                candidateRect.anchorMin = new Vector2(0.5f, 0.5f);
                candidateRect.anchorMax = new Vector2(0.5f, 0.5f);
                candidateRect.pivot = new Vector2(0.5f, 0.5f);
                candidateRect.sizeDelta = source.rectTransform.rect.size;
                candidateRect.localScale = Vector3.one;

                var group = candidate.GetComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;

                var image = candidate.GetComponent<Image>();
                var color = source.color;
                image.sprite = source.sprite;
                image.color = new Color(color.r, color.g, color.b, VisualAlpha);
                image.type = source.type;
                image.preserveAspect = source.preserveAspect;
                image.fillCenter = source.fillCenter;
                image.fillMethod = source.fillMethod;
                image.fillAmount = source.fillAmount;
                image.fillClockwise = source.fillClockwise;
                image.fillOrigin = source.fillOrigin;
                image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                image.raycastTarget = false;

                if (!TrySetPosition(dialog, candidateRect, eventData))
                {
                    DestroyVisual(candidate);
                    return false;
                }

                candidateRect.SetAsLastSibling();
                sourceImage = source;
                sourceImageWasEnabled = source.enabled;
                dialogRect = dialog;
                visualObject = candidate;
                visualRect = candidateRect;
                source.enabled = false;
                return true;
            }
            catch (Exception exception)
            {
                if (sourceImage != null)
                {
                    sourceImage.enabled = sourceImageWasEnabled;
                }

                if (candidate != null)
                {
                    DestroyVisual(candidate);
                }

                sourceImage = null;
                visualObject = null;
                visualRect = null;
                dialogRect = null;
                Debug.LogError($"[{nameof(InventoryDragVisualPresenter)}] {exception.Message}");
                return false;
            }
        }

        public void UpdatePosition(PointerEventData eventData)
        {
            if (!IsVisible || visualRect == null || dialogRect == null || eventData == null)
            {
                return;
            }

            if (TrySetPosition(dialogRect, visualRect, eventData))
            {
                visualRect.SetAsLastSibling();
            }
        }

        public void Clear()
        {
            if (sourceImage != null)
            {
                sourceImage.enabled = sourceImageWasEnabled;
            }

            sourceImage = null;
            if (visualObject != null)
            {
                DestroyVisual(visualObject);
            }

            visualObject = null;
            visualRect = null;
            dialogRect = null;
        }

        public void Dispose()
        {
            Clear();
        }

        private static bool TrySetPosition(
            RectTransform dialog,
            RectTransform target,
            PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    dialog, eventData.position, eventData.pressEventCamera, out var localPoint))
            {
                return false;
            }

            target.anchoredPosition = localPoint;
            return true;
        }

        private static void DestroyVisual(GameObject target)
        {
            target.SetActive(false);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
