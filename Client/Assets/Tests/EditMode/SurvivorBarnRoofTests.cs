using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>在正式仓房实例上验证门洞遮挡、独立淡化与材质回收。</summary>
    public sealed class SurvivorBarnRoofTests
    {
        [Test]
        public void OutsideDoorRevealsOnlyVisibleRoofPixels()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");
            var farm = Object.Instantiate(prefab);
            try
            {
                var roof = farm.GetComponentInChildren<SurvivorBarnRoof>();
                var renderer = roof.GetComponent<MeshRenderer>();
                var original = renderer.sharedMaterial;
                var center = renderer.bounds.center;
                var opening = new Vector3(center.x, 1.2f, center.z - 4.5f);
                float wallZ = center.z - 3.5f;
                var walls = new[] {
                    new Bounds(new Vector3(center.x - 3,1.5f,wallZ), new Vector3(3,3,.2f)),
                    new Bounds(new Vector3(center.x + 3,1.5f,wallZ), new Vector3(3,3,.2f)),
                    new Bounds(new Vector3(center.x, .2f,center.z-2), new Vector3(.5f,.4f,.5f))
                };
                roof.TickVisibility(opening, 6f, walls, 1f);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                var mask = properties.GetTexture("_RoofVisibilityMask") as Texture2D;
                Assert.That(mask, Is.Not.Null);
                Assert.That(mask.GetPixelBilinear(.5f,.3f).r, Is.GreaterThan(.9f), "门洞与低箱后方可见");
                Assert.That(mask.GetPixelBilinear(.06f,.12f).r, Is.LessThan(.1f), "端墙后方保持遮蔽");
                Assert.That(mask.GetPixelBilinear(.5f,.95f).r, Is.LessThan(.1f), "超出圆形距离保持遮蔽");
                walls[2] = new Bounds(new Vector3(center.x, 1.5f,center.z-2), new Vector3(.5f,3,.5f));
                roof.TickVisibility(opening + Vector3.right * .03f, 6f, walls, 1f);
                Assert.That(mask.GetPixelBilinear(.5f,.3f).r, Is.LessThan(.1f), "高箱后方保持遮蔽");
                roof.Restore();
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
            }
            finally { Object.DestroyImmediate(farm); }
        }

        [Test]
        public void FarmWallsControlBothRoofsAndReleaseRestoresMaterials()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");
            var farm = Object.Instantiate(prefab);
            try
            {
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(ArcadeCombatViewController);
                var view = new ArcadeCombatViewController();
                type.GetField("mapRoot", hidden).SetValue(view, farm.transform);
                type.GetMethod("PrepareSurvivorRoofVisibility", hidden).Invoke(view, new object[] { farm });
                var blockers = (System.Collections.Generic.List<Bounds>)type.GetField("survivorRoofBlockers", hidden).GetValue(view);
                Assert.That(blockers[0].max.y, Is.LessThan(1.55f), "正式低石墙低于视线");
                Assert.That(blockers[8].max.y, Is.GreaterThan(3f), "正式仓房墙壁高于视线");

                var roofs = farm.GetComponentsInChildren<SurvivorBarnRoof>();
                Assert.That(roofs.Length, Is.EqualTo(2));
                var renderers = roofs.Select(roof => roof.GetComponent<MeshRenderer>()).ToArray();
                var originals = renderers.Select(renderer => renderer.sharedMaterial).ToArray();
                for (int i = 0; i < roofs.Length; i++)
                {
                    var center = renderers[i].bounds.center;
                    view.SetSurvivorVisibility(1, 5f, new Vector2(center.x + 5.1f, center.z));
                    view.TickSurvivorRoofVisibility(1f);
                    Assert.That(roofs[i].Opacity, Is.EqualTo(1f), "侧墙不能从屋外看到室内");

                    view.SetSurvivorVisibility(1, 5f, new Vector2(center.x, center.z + 4.4f));
                    view.TickSurvivorRoofVisibility(.05f);
                    Assert.That(roofs[i].Opacity, Is.InRange(.11f, .99f));
                    Assert.That(roofs[1-i].Opacity, Is.EqualTo(1f));
                    view.TickSurvivorRoofVisibility(1f);
                    Assert.That(roofs[i].Opacity, Is.EqualTo(.1f).Within(.001f));
                    Assert.That(renderers[i].sharedMaterial.shader.name, Is.EqualTo("Game/Survivor/Barn Roof Visibility"));
                    Assert.That(renderers[1-i].sharedMaterial, Is.SameAs(originals[1-i]));
                }

                type.GetMethod("ReleaseEarlyStageLoad", hidden).Invoke(view, null);
                for (int i = 0; i < roofs.Length; i++)
                {
                    Assert.That(roofs[i].Opacity, Is.EqualTo(1f));
                    Assert.That(renderers[i].sharedMaterial, Is.SameAs(originals[i]));
                    var properties = new MaterialPropertyBlock();
                    renderers[i].GetPropertyBlock(properties);
                    Assert.That(properties.GetTexture("_RoofVisibilityMask"), Is.Null);
                }
                Assert.That((SurvivorBarnRoof[])type.GetField("survivorBarnRoofs", hidden).GetValue(view), Is.Empty);
                Assert.That(blockers, Is.Empty);
            }
            finally { Object.DestroyImmediate(farm); }
        }
    }
}
