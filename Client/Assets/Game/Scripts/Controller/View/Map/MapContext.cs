using UnityEngine;

namespace Game
{
    /// <summary>
    /// 保存单个地图场景的运行时上下文，包括地图内容根和动态对象根。
    /// </summary>
    public sealed class MapContext : MonoBehaviour
    {
        [SerializeField]
        private GameObject mapContentRoot;

        [SerializeField]
        private Transform dynamicObjectRoot;

        public GameObject MapContentRoot => mapContentRoot;
        public Transform DynamicObjectRoot => dynamicObjectRoot;

        public void SetMapContentActive(bool active)
        {
            if (mapContentRoot != null && mapContentRoot.activeSelf != active)
            {
                mapContentRoot.SetActive(active);
            }
        }

        public bool Validate(out string error)
        {
            if (mapContentRoot == null)
            {
                error = "地图缺少 MapContentRoot";
                return false;
            }

            if (dynamicObjectRoot == null)
            {
                error = "地图缺少 DynamicObjectRoot";
                return false;
            }

            if (!dynamicObjectRoot.IsChildOf(mapContentRoot.transform))
            {
                error = "地图 DynamicObjectRoot 必须位于 MapContentRoot 下";
                return false;
            }

            error = null;
            return true;
        }
    }
}
