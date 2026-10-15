using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>用正式摆件配置和真实建筑控制器验证恢复、旋转、消费、拆除及跨地图门禁。</summary>
    public sealed class BaseCampBuildingFlowTests
    {
        [TestCase("temp_campfire", "GrayboxCampfire", .45f)]
        [TestCase("camp_rpgfxcampfire", "Camp_RPGFXCampfire", .25f)]
        public void CampfireFlameCanBeSelectedAndDemolishedWithRefund(string configId, string prefabName, float flameHeight)
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            FakeMapHandle handle = null;
            try
            {
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort(), useRealBuilding: true);
                var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
                Game.Interface.RegisterUtility<IRuntimeConfigProvider>(config);
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, handle);
                Assert.That(config.TryGetTempBuilding(configId, out var definition, out var error), Is.True, error);
                foreach (var material in definition.Materials) TemporaryBuildingBehaviorTestSupport.AddBagItem(material.ItemId, material.Amount);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                var model = Game.Interface.GetModel<TemporaryBuildingModel>();
                var position = Vector3.right * 2;
                Assert.That(system.BeginBuildPreview(configId, out error), Is.True, error);
                Assert.That(system.UpdateBuildPreview(position, Quaternion.identity), Is.True);
                var previewView = handle.Context.DynamicObjectRoot.GetComponentInChildren<TemporaryBuildingView>();
                Assert.That(previewView.name, Does.Contain(prefabName));
                var previewSelection = previewView.CommittedEffectRoot.GetComponentInChildren<BoxCollider>(true);
                Assert.That(previewSelection.gameObject.activeInHierarchy, Is.False, "预览不能启用火焰选择体");
                Assert.That(system.ConfirmBuild(out error), Is.True, error);
                system.TickForTests(definition.BuildSeconds + .1f);
                var built = model.Buildings.Values.Single();
                var builtView = handle.Context.DynamicObjectRoot.GetComponentInChildren<TemporaryBuildingView>();
                var selection = builtView.CommittedEffectRoot.GetComponentInChildren<BoxCollider>();
                Assert.That(selection.isTrigger, Is.True, "火焰选择体不能阻挡角色");
                Assert.That(selection.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
                Physics.SyncTransforms();
                Assert.That(Physics.Raycast(position + Vector3.up * 3, Vector3.down, out var groundHit), Is.True);
                Assert.That(groundHit.collider, Is.Not.SameAs(selection), "默认地面射线不能命中火焰选择体");
                Assert.That(system.BeginDestroyMode(out error), Is.True, error);
                // 射线穿过可见火焰，刻意不经过原本贴地的占地体。
                system.RefreshDestroyTarget(new Ray(position + Vector3.up * flameHeight + Vector3.back * 3, Vector3.forward));
                Assert.That(model.DestroyTargetBuildingId, Is.EqualTo(built.RuntimeId));
                var tint = new Color32(0x71, 0x3B, 0x3B, 0xFF);
                Assert.That(builtView.GetComponentsInChildren<Renderer>().Any(renderer =>
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    return block.GetColor("_BaseColor") == (Color)tint || block.GetColor("_Color") == (Color)tint;
                }), Is.True, "选中的火堆必须染红");
                Assert.That(system.ConfirmDestroy(out error), Is.True, error);
                Assert.That(model.Buildings, Is.Empty);
                var refund = (FakeBuildingRefundDropPort)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                Assert.That(refund.LastDrops, Is.Not.Empty);
                foreach (var material in definition.Materials)
                    Assert.That(refund.LastDrops.Where(drop => drop.ItemId == material.ItemId).Sum(drop => drop.Amount), Is.EqualTo(material.Amount));
                Assert.That(handle.Context.DynamicObjectRoot.GetComponentsInChildren<TemporaryBuildingView>(), Is.Empty);
            }
            finally
            {
                if (handle?.Root != null) UnityEngine.Object.DestroyImmediate(handle.Root);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [TestCase(15f)]
        [TestCase(45f)]
        public void RealCampPropCanStraddleOperationCircleUntilFootprintFullyLeaves(float yaw)
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            FakeMapHandle handle = null;
            try
            {
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort(), useRealBuilding: true);
                var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
                Game.Interface.RegisterUtility<IRuntimeConfigProvider>(config);
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, handle);
                Assert.That(config.TryGetTempBuilding("camp_crate_01", out var definition, out var error), Is.True, error);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                var model = Game.Interface.GetModel<TemporaryBuildingModel>();
                var position = new Vector3(10f, 0f, 5f);
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                TemporaryBuildingBehaviorTestSupport.MovePlayer(position - Vector3.right * (3f + definition.FootprintRadius - .01f));
                Assert.That(system.BeginBuildPreview(definition.Id, out error), Is.True, error);
                Assert.That(system.UpdateBuildPreview(position, rotation), Is.True);
                Assert.That(model.PreviewValid, Is.True, model.PreviewReason);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player), Is.True);
                Assert.That(Vector3.Distance(position, player.Position), Is.GreaterThan(3f));
                TemporaryBuildingBehaviorTestSupport.MovePlayer(position - Vector3.right * (3f + definition.FootprintRadius + .01f));
                system.UpdateBuildPreview(position, rotation);
                Assert.That(model.PreviewValid, Is.False);
                Assert.That(model.PreviewReason, Is.EqualTo("放置位置距离过远"));
            }
            finally
            {
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                if (handle?.Root != null) UnityEngine.Object.DestroyImmediate(handle.Root);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }

        [Test]
        public void RealCampRestoresThenBuildsRotatesAndDemolishesWithoutLeavingColliders()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            GameObject ground = null;
            FakeMapHandle handle = null;
            try
            {
                TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), new FakeTemporaryBuildingViewPort(), new FakeProjectilePort(), useRealBuilding: true);
                var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
                Game.Interface.RegisterUtility<IRuntimeConfigProvider>(config);
                TemporaryBuildingBehaviorTestSupport.EnterBase();
                ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
                handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, handle);
                var snapshot = new BaseSaveData();
                Assert.That(config.TryGetBaseCampLayout(out var layout, out var error), Is.True, error);
                Assert.That(BaseCampLayoutInitializer.TryInitialize(snapshot.World, layout,
                    id => config.TryGetTempBuilding(id, out var definition, out _) ? definition : null, out error), Is.True, error);
                var ctrl = (TemporaryBuildingCtrl)Game.Interface.GetUtility<ITemporaryBuildingViewPort>();
                var prepared = ctrl.PrepareMap(new MapPreparationContext(MapIds.Base, TemporaryBuildingBehaviorTestSupport.MapInstanceId,
                    handle.Context, Array.Empty<MapSpawnDefinition>(), baseSnapshot: snapshot));
                Assert.That(prepared.Success, Is.True, prepared.Error);
                Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
                Assert.That(handle.Context.DynamicObjectRoot.childCount, Is.EqualTo(44));
                var model = Game.Interface.GetModel<TemporaryBuildingModel>();
                Assert.That(model.Buildings.Count, Is.EqualTo(44));
                Assert.That(model.Buildings[1].UniformScale, Is.EqualTo(snapshot.World.TemporaryBuildings[0].UniformScale));
                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 5);
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                Assert.That(system.BeginBuildPreview("camp_table_01", out error), Is.True, error);
                var position = new Vector3(3,0,-4);
                TemporaryBuildingBehaviorTestSupport.MovePlayer(position + Vector3.left * 1.5f);
                Assert.That(system.UpdateBuildPreview(position, Quaternion.identity), Is.True);
                system.RotatePreview(90);
                Assert.That(model.PreviewRotation.eulerAngles.y, Is.EqualTo(90).Within(.001f));
                Assert.That(model.PreviewPosition, Is.EqualTo(position));
                Assert.That(system.ConfirmBuild(out error), Is.True, error);
                system.TickForTests(1.1f);
                var built = model.Buildings.Values.OrderByDescending(b => b.RuntimeId).First();
                Assert.That(built.ConfigId, Is.EqualTo("camp_table_01"));
                Assert.That(built.Rotation.eulerAngles.y, Is.EqualTo(90).Within(.001f));
                Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Wood), Is.Zero);
                TemporaryBuildingBehaviorTestSupport.MovePlayer(position + Vector3.left * 1.5f);
                Physics.SyncTransforms();
                Assert.That(system.BeginDestroyMode(out error), Is.True, error);
                system.RefreshDestroyTarget(new Ray(position + Vector3.up * 4, Vector3.down));
                Assert.That(model.DestroyTargetBuildingId, Is.EqualTo(built.RuntimeId));
                var selected = handle.Context.DynamicObjectRoot.GetComponentsInChildren<TemporaryBuildingView>()
                    .Single(view => Vector3.Distance(view.transform.position, position) < .01f);
                var renderer = selected.transform.Find("Model").GetComponent<Renderer>();
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                var tint = block.GetColor(Shader.PropertyToID("_BaseColor"));
                Assert.That(tint.r, Is.EqualTo(0x71 / 255f).Within(.00001f),
                    "正式摆件被选中后必须有红色拆除高亮");
                Assert.That(tint.g, Is.EqualTo(0x3B / 255f).Within(.00001f));
                Assert.That(tint.b, Is.EqualTo(0x3B / 255f).Within(.00001f));
                Assert.That(system.ConfirmDestroy(out error), Is.True, error);
                Assert.That(model.Buildings.ContainsKey(built.RuntimeId), Is.False);
                Assert.That(handle.Context.DynamicObjectRoot.GetComponentsInChildren<TemporaryBuildingView>().Length, Is.EqualTo(44));
                var refund = (FakeBuildingRefundDropPort)Game.Interface.GetUtility<IBuildingRefundDropPort>();
                Assert.That(refund.LastDrops.Single().Amount, Is.EqualTo(5));
                system.CancelCurrentMode();
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
                Assert.That(system.BeginBuildPreview("camp_table_01", out error), Is.False);
            }
            finally
            {
                if (handle?.Root != null) UnityEngine.Object.DestroyImmediate(handle.Root);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                TemporaryBuildingBehaviorTestSupport.Reset();
            }
        }
    }
}
