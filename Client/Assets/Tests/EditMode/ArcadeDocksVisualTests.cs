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
            // 已采用的岸边层次必须有正式线性纹理，不能依赖演示脚本临时生成。
            var sea=prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Single(m=>m.shader.name=="FirepowerLoop/DockWater");
            Assert.That(ShaderUtil.ShaderHasError(sea.shader),Is.False);
            Assert.That(sea.HasProperty("_ShoreDistance"),Is.True,"正式水面缺少岸距数据入口");
            var shore=sea.GetTexture("_ShoreDistance") as Texture2D;
            Assert.That(shore,Is.Not.Null,"正式水面必须持有岸距资源");
            var shorePath=AssetDatabase.GetAssetPath(shore);
            Assert.That(shorePath,Does.StartWith("Assets/Game/Art/Arcade/Docks/"));
            var importer=AssetImporter.GetAtPath(shorePath) as TextureImporter;
            Assert.That(importer,Is.Not.Null);
            Assert.That(importer.sRGBTexture,Is.False,"岸距为米数，不能经过颜色伽马转换");
            Assert.That(importer.textureCompression,Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(shore.mipmapCount,Is.EqualTo(1),"米数编码不使用有损缩小采样");
            Assert.That(shore.wrapMode,Is.EqualTo(TextureWrapMode.Clamp));
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
