using UnityEngine;
namespace Game
{
    /// <summary>旧地图资源的序列化契约，街机不启用基地传送。</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BaseMapPortal : MonoBehaviour
    {
        [SerializeField] private string targetMapId = "map.another-day.survival-sample";
    }
}
