using TMPro;
using UnityEngine;
namespace Game
{
    /// <summary>保留旧撤离资源引用；街机不注册撤离触发或结算。</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TaskExitView : MonoBehaviour
    {
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private TMP_Text promptText;
        private void Awake() { if (promptRoot != null) promptRoot.SetActive(false); }
    }
}
