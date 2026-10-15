using UnityEngine;
namespace Game
{
    /// <summary>保留旧交互资源的序列化引用，街机不为旧物件登记焦点或交互。</summary>
    [RequireComponent(typeof(BoxCollider))]
    public abstract class PlayerInteractableView : MonoBehaviour
    {
        [SerializeField] private string displayName;
        [SerializeField] private GameObject nameRoot;
        public virtual string DisplayName => displayName;
        public virtual bool CanInteract => false;
        protected GameObject FocusedPresentationRoot => nameRoot;
        protected virtual bool ShouldShowFocusedPresentation => false;
        public abstract void Interact();
        protected void RefreshFocusedPresentation() { if (nameRoot != null) nameRoot.SetActive(false); }
        protected virtual void Awake() => RefreshFocusedPresentation();
    }
}
