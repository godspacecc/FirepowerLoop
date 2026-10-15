using UnityEngine;

namespace Game
{
    /// <summary>
    /// 使用 Unity Object.Instantiate 和 Object.Destroy 管理表现实例。
    /// </summary>
    public sealed class UnityViewInstanceProvider : IViewInstanceProvider
    {
        public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
        {
            return Object.Instantiate(prefab, position, rotation, parent);
        }

        public void Release(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(instance);
            }
            else
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
