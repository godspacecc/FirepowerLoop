using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 定义表现对象创建和释放的抽象入口，便于运行时和测试替换。
    /// </summary>
    public interface IViewInstanceProvider : IUtility
    {
        T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component;
        void Release(GameObject instance);
    }
}
