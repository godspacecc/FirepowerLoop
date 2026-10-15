using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    public sealed class TestCmdButtonPointerRelay : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public event Action ClickRequested;
        public event Action<PointerEventData> BeginDragRequested;
        public event Action<PointerEventData> DragRequested;
        public event Action<PointerEventData> EndDragRequested;
        public event Action ParentRectChanged;

        private bool draggedDuringCurrentPress;

        public void OnPointerDown(PointerEventData eventData)
        {
            draggedDuringCurrentPress = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!draggedDuringCurrentPress)
            {
                ClickRequested?.Invoke();
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            draggedDuringCurrentPress = true;
            BeginDragRequested?.Invoke(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            DragRequested?.Invoke(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            EndDragRequested?.Invoke(eventData);
        }

        private void OnRectTransformDimensionsChange()
        {
            ParentRectChanged?.Invoke();
        }
    }
}
