using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class UiPrefabBindingTests
    {
        private const string UiPrefabRoot = "Assets/Game/Prefabs/UI";

        [Test]
        public void AllUiAndBoxUiObjectFields_AreBoundInPrefabs()
        {
            var violations = new List<string>();
            ResourceLoader.SetLoaderForTests(new BindingLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabRoot }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        {
                            if (component == null ||
                                (!(component is BaseUI) && !(component is BaseBoxUI)))
                            {
                                continue;
                            }

                            var fields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                            foreach (var field in fields)
                            {
                                if (field.IsNotSerialized ||
                                    !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                                {
                                    continue;
                                }

                                var value = field.GetValue(component) as UnityEngine.Object;
                                if (value == null)
                                {
                                    violations.Add($"{path} -> {component.GetType().Name}.{field.Name}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                ResourceLoader.ResetForTests();
                LocalizationLanguages.ResetForTests();
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        private sealed class BindingLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset("{}") as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}
