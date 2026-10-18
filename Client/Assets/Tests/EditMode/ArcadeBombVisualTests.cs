using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式特效模板的阶段、复用与离场生命周期；不执行伤害或存档。</summary>
    public sealed class ArcadeBombVisualTests
    {
        private GameObject root;
        private ArcadeBombBlastPresentation presentation;
        [SetUp] public void Setup()
        {
            root=new GameObject("BombTestRoot");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/Bomb/BombBlast.prefab");
            Assert.NotNull(prefab);presentation=new ArcadeBombBlastPresentation(root.transform,prefab.transform);
        }
        [TearDown] public void Cleanup(){presentation?.Dispose();Object.DestroyImmediate(root);}
        [Test] public void FlashExpandsThenLeavesClearGround()
        {
            presentation.Play(new Vector2(3,-4),25);presentation.Tick(.05f);
            var instance=root.transform.GetChild(0);var ring=instance.Find("Shockwave");
            Assert.AreEqual(new Vector3(3,.18f,-4),instance.localPosition);
            Assert.True(instance.Find("Flash").gameObject.activeSelf);float early=ring.localScale.x;
            presentation.Tick(.20f);Assert.Greater(ring.localScale.x,early);Assert.False(instance.Find("Flash").gameObject.activeSelf);
            presentation.Tick(.55f);Assert.False(ring.gameObject.activeSelf);
            presentation.Tick(.25f);Assert.False(instance.gameObject.activeSelf);
        }
        [Test] public void RapidUsesStayBoundedAndDisposeReleasesAll()
        {
            for(int i=0;i<20;i++)presentation.Play(new Vector2(i,0),25);
            Assert.LessOrEqual(root.transform.childCount,4);
            Assert.True(root.GetComponentsInChildren<Transform>().Any(t=>t.localPosition.x==19));
            presentation.Dispose();Assert.AreEqual(0,root.transform.childCount);
        }
        [Test] public void PausedTickDoesNotAdvanceShockwave()
        {
            presentation.Play(Vector2.zero,25);presentation.Tick(.05f);
            var ring=root.transform.GetChild(0).Find("Shockwave");float radius=ring.localScale.x;
            presentation.Tick(0);Assert.AreEqual(radius,ring.localScale.x);
        }
        [Test] public void TemplateHasNoGameplayComponentsAndIsBoundToContent()
        {
            var kit=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            var template=kit.transform.Find("Content/BombBlast");Assert.NotNull(template);
            Assert.IsEmpty(template.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(template.GetComponentsInChildren<MonoBehaviour>(true));
            foreach(var r in template.GetComponentsInChildren<Renderer>(true))foreach(var m in r.sharedMaterials){Assert.NotNull(m);Assert.NotNull(m.shader);}
        }
    }
}
