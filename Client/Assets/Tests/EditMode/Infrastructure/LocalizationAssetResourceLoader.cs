using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>仅从当前工程真实资源读取本地化，供街机与通用UI测试共用。</summary>
    internal sealed class LocalizationAssetResourceLoader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : Object
        {
            var extension = typeof(T) == typeof(TextAsset) ? ".json" : typeof(T) == typeof(Sprite) ? ".png" : ".prefab";
            // 本地化运行时地址是语言文件名；测试必须落到真实语言目录，且不能重复追加扩展名。
            if (typeof(T) == typeof(TextAsset) && path.EndsWith(".json") && !path.Contains("/"))
                path = "Config/Localization/" + path;
            return AssetDatabase.LoadAssetAtPath<T>("Assets/Game/" + path + (path.EndsWith(extension) ? string.Empty : extension));
        }
        public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> callback) where T : Object { callback?.Invoke(LoadAsset<T>(path)); return ResourceLoadHandle.Empty; }
        public void Release(ResourceLoadHandle handle) { }
    }
}
