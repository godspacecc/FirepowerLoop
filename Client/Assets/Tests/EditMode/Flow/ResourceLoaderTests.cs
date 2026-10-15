using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class ResourceLoaderTests
    {
        [TearDown]
        public void TearDown()
        {
            ResourceLoader.ResetForTests();
        }

        [Test]
        public void UseInjectedLoader_LoadsWithOriginalAddress()
        {
            var asset = new GameObject("InjectedAsset");
            var loader = new FakeResourceLoader(asset);
            ResourceLoader.SetLoaderForTests(loader);

            Assert.AreSame(asset, ResourceLoader.LoadAsset<GameObject>("Prefabs/UI/Home/Start"));
            Assert.That(loader.LastPath, Is.EqualTo("Prefabs/UI/Home/Start"));

            Object.DestroyImmediate(asset);
        }

        [Test]
        public void ResetForTests_RestoresDefaultLoader()
        {
            ResourceLoader.SetLoaderForTests(new FakeResourceLoader(null));
            ResourceLoader.ResetForTests();

            Assert.That(ResourceLoader.Current, Is.Not.SameAs(null));
            var errors = CaptureErrors(() => Assert.IsNull(ResourceLoader.LoadAsset<GameObject>("Missing")));
            AssertHasError(errors, "DefaultPackage 为空");
        }

        [Test]
        public void YooAssetResourceLoader_ReturnsNull_WhenPackageIsMissing()
        {
            var loaderType = typeof(ResourceLoader).Assembly.GetType("Game.YooAssetResourceLoader");
            var loader = (IResourceLoader)System.Activator.CreateInstance(loaderType, new object[] { null });

            var errors = CaptureErrors(() => Assert.IsNull(loader.LoadAsset<GameObject>("Prefabs/UI/Home/Missing")));
            AssertHasError(errors, "DefaultPackage 为空");
        }

        [Test]
        public void ResourceLoader_DoesNotContainRuntimeFallbacks()
        {
            var root = Path.Combine(Application.dataPath, "Game", "Scripts", "Utility", "Res");
            var source = string.Join("\n", Directory.GetFiles(root, "*.cs").Select(File.ReadAllText));

            Assert.That(source, Does.Not.Contain("Resources.Load"));
            Assert.That(source, Does.Not.Contain("ResourcesResourceLoader"));
            Assert.That(source, Does.Not.Contain("AssetDatabase.LoadAssetAtPath"));
        }

        [Test]
        public void Release_ForwardsToCurrentLoader()
        {
            var loader = new FakeResourceLoader(null);
            var handle = new ResourceLoadHandle(new object());
            ResourceLoader.SetLoaderForTests(loader);

            ResourceLoader.Release(handle);

            Assert.That(loader.ReleasedHandle.RawHandle, Is.SameAs(handle.RawHandle));
        }

        [Test]
        public void SuccessfulSyncAsset_ReturnsOriginalObject()
        {
            var asset = new TextAsset("hello");
            ResourceLoader.SetLoaderForTests(new FakeResourceLoader(asset));

            Assert.AreSame(asset, ResourceLoader.LoadAsset<TextAsset>("languages.json"));
        }

        private sealed class FakeResourceLoader : IResourceLoader
        {
            private readonly Object _asset;

            public FakeResourceLoader(Object asset)
            {
                _asset = asset;
            }

            public string LastPath { get; private set; }
            public ResourceLoadHandle ReleasedHandle { get; private set; }

            public T LoadAsset<T>(string path) where T : Object
            {
                LastPath = path;
                return _asset as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                LastPath = path;
                onLoaded?.Invoke(_asset as T);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
                ReleasedHandle = handle;
            }
        }

        private static List<string> CaptureErrors(Action action)
        {
            var errors = new List<string>();
            using (Log.CaptureForTests((type, message) =>
            {
                if (type == LogType.Error)
                {
                    errors.Add(message);
                }
            }))
            {
                action();
            }

            return errors;
        }

        private static void AssertHasError(List<string> errors, string pattern)
        {
            Assert.IsTrue(errors.Exists(message => Regex.IsMatch(message, pattern)), $"Expected log matching: {pattern}");
        }
    }
}
