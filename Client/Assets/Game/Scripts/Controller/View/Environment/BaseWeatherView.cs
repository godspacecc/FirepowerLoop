using UnityEngine;
namespace Game
{
    /// <summary>保留旧天气资源的原序列化引用；街机采用默认晴天，不创建雨效或注册天气事件。</summary>
    [DisallowMultipleComponent]
    public sealed class BaseWeatherView : MonoBehaviour
    {
        [SerializeField] private Renderer[] wetSurfaces = new Renderer[0];
        [SerializeField] private Collider[] rainBlockers = new Collider[0];
        [SerializeField] private Renderer[] rainRoofs = new Renderer[0];
        [SerializeField] private Material rainMaterial;
        [SerializeField] private Material splashMaterial;
        [SerializeField] private AudioClip rainClip;
        [SerializeField, Min(4)] private float radius = 18;
    }
}
