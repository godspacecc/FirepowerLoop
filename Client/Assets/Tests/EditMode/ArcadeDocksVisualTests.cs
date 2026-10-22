using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式码头独立环境的资源归属与低批次数，不用美术物体裁决通行。</summary>
    public sealed class ArcadeDocksVisualTests
    {
        private const string Path="Assets/Game/Prefabs/SceneObjects/ArcadeDocksVisual.prefab";
        [Test] public void ConfirmedDockHasNoActorsCollidersOrBusinessScripts()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);Assert.That(prefab,Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<Animator>(true),Is.Empty);
            var renderers=prefab.GetComponentsInChildren<MeshRenderer>(true);Assert.That(renderers.Length,Is.InRange(1,12));
            Assert.That(prefab.GetComponentsInChildren<LineRenderer>(true),Is.Empty,"静态绳索应并入网格，避免每条绳索独立绘制或固定在世界原点");
        }
        [Test] public void EveryMeshMaterialTextureAndShaderIsPersistedWithoutBackupLibraryDependency()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);Assert.That(prefab,Is.Not.Null);
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true)){Assert.That(filter.sharedMesh,Is.Not.Null);Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh),Does.StartWith("Assets/Game/Art/Arcade/Docks/"));}
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials){Assert.That(material,Is.Not.Null);Assert.That(material.shader,Is.Not.Null);Assert.That(material.shader.name,Does.Not.Contain("Hidden/Temp"));}
            Assert.That(AssetDatabase.GetDependencies(Path,true).Where(p=>p.StartsWith("Assets/Art/")),Is.Empty);
        }
        [Test] public void EntireDockFollowsOwningMapTransform()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);Assert.That(prefab,Is.Not.Null);
            var owner=new GameObject("OtherMapOwner");var go=Object.Instantiate(prefab,owner.transform);
            try
            {
                var renderers=go.GetComponentsInChildren<MeshRenderer>(true);var old=renderers.Select(r=>r.bounds.center).ToArray();
                owner.transform.position=new Vector3(100,0,-30);
                for(int i=0;i<renderers.Length;i++)Assert.That(Vector3.Distance(renderers[i].bounds.center-old[i],owner.transform.position),Is.LessThan(.001f));
            }
            finally{Object.DestroyImmediate(owner);}
        }
    }
}
