// 独立文件检查只替代引擎分析标记与线程调度；正式JSON、克隆和原子写入源码直接参与编译。
namespace UnityEngine { public static class Application { public static string persistentDataPath => throw new System.InvalidOperationException("Select isolated test storage"); } }
namespace Unity.Profiling
{
    public readonly struct ProfilerMarker
    {
        public ProfilerMarker(string name) { }
        public Sample Auto() => new Sample();
        public readonly struct Sample : System.IDisposable { public void Dispose() { } }
    }
}
namespace Cysharp.Threading.Tasks
{
    public readonly struct UniTask<T> { }
    public static class UniTask { public static UniTask<T> RunOnThreadPool<T>(System.Func<T> action) => throw new System.InvalidOperationException("Asynchronous Unity dispatch is outside these file checks"); }
}
