using System;
using System.Collections.Generic;
using AnotherDay.Arcade;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        // 沿用已登记的场地表现资源入口，岛岸模板作为集合子项，避免新增地址要求本机先打包。
        private const string IslandAddress = ObstacleVisualAddress;
        private GameObject island;
        private ObstacleVisualLoadState islandLoad;
        private ArcadeOpeningParameters islandParameters;
        private bool islandFireActive;
        private int islandGeneration;
        private bool islandLoadFailed;
        private float islandFlameBaseRadius;
        private float islandDangerMeshRadius;
        private Mesh islandSandMesh, islandWaterMesh, islandShorelineMesh;
        private float islandMeshWidth, islandMeshHeight;
        /// <summary>当前地图 Context 的静态场地根；与动态根分开，不能跨地图查找同名物件。</summary>
        private Transform islandBaseArenaRoot;
        /// <summary>本控制器临时覆盖的 Renderer 原始状态；岛岸释放时逐项恢复，不改变碰撞。</summary>
        private readonly Dictionary<Renderer, bool> islandBaseVisibility = new();

        public void SetOpening(ArcadeOpeningParameters parameters, bool fireActive)
        {
            islandParameters = parameters; islandFireActive = fireActive;
            if (!parameters.Enabled) { ReleaseIsland(); return; }
            if (island != null) { ConfigureIsland(); return; }
            if (!islandLoadFailed && islandLoad == null && root != null) LoadIslandAsync(++islandGeneration).Forget();
        }

        /// <summary>候选资源只在同一场地根及加载代次存活时提交；晚回调由已释放句柄兜底。</summary>
        private async UniTaskVoid LoadIslandAsync(int generation)
        {
            var state = new ObstacleVisualLoadState(); islandLoad = state;
            var expectedRoot = root;
            var completion = new UniTaskCompletionSource<GameObject>();
            state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(IslandAddress, p => completion.TrySetResult(p));
            try
            {
                var prefab = await completion.Task;
                if (state.Released || generation != islandGeneration || root != expectedRoot || root == null) return;
                var template = prefab != null ? prefab.transform.Find("Island") : null;
                if (template == null || template.Find("Water") == null || template.Find("Campfire") == null ||
                    template.Find("Sand") == null || template.Find("Ocean") == null || template.Find("Shoreline") == null ||
                    template.Find("Campfire/Flame/Smoke")?.GetComponent<ParticleSystem>() == null ||
                    template.GetComponentsInChildren<Collider>(true).Length > 0)
                    throw new InvalidOperationException("岛岸 Prefab 的地面、水域或营火合同无效");
                var boundary = template.Find("Campfire/DangerBoundary");
                var mesh = boundary != null ? boundary.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh == null) throw new InvalidOperationException("营火缺少范围显示网格");
                islandFlameBaseRadius = mesh.bounds.extents.x * boundary.localScale.x;
                islandDangerMeshRadius = mesh.bounds.extents.x;
                if (islandFlameBaseRadius <= 0) throw new InvalidOperationException("营火范围网格尺寸无效");
                island = UnityEngine.Object.Instantiate(template.gameObject, root.transform, false);
                island.SetActive(true);
                ConfigureIsland();
                island.SetActive(!contentInRoom);
                HideIslandBaseArena();
            }
            catch (Exception e)
            {
                if (!state.Released) Log.Error("[ArcadeCombatViewController] 岛岸加载失败：" + e.Message);
                if (generation == islandGeneration) islandLoadFailed = true;
                RestoreIslandBaseArena();
                DestroyObject(island); island = null;
                ReleaseIslandMeshes();
                ReleaseObstacleVisualLoad(state);
                if (ReferenceEquals(islandLoad, state)) islandLoad = null;
            }
        }

        private void ConfigureIsland()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetArcadeCatalog(out var catalog, out _)) return;
            float w = catalog.Rule.ArenaHalfWidth, h = catalog.Rule.ArenaHalfHeight;
            if (islandSandMesh == null || islandMeshWidth != w || islandMeshHeight != h)
            {
                ReleaseIslandMeshes();
                var region = new ArcadeIslandRegion(w, h);
                islandSandMesh = ArcadeIslandMesh.Sand(region); islandWaterMesh = ArcadeIslandMesh.Water(region);
                islandShorelineMesh = ArcadeIslandMesh.Shoreline(region); islandMeshWidth = w; islandMeshHeight = h;
                island.transform.Find("Sand").GetComponent<MeshFilter>().sharedMesh = islandSandMesh;
                island.transform.Find("Water").GetComponent<MeshFilter>().sharedMesh = islandWaterMesh;
                island.transform.Find("Shoreline").GetComponent<MeshFilter>().sharedMesh = islandShorelineMesh;
            }
            foreach (var name in new[] { "Sand", "Water", "Shoreline" })
            {
                var surface = island.transform.Find(name); surface.localScale = Vector3.one;
                surface.localPosition = new Vector3(0, name == "Shoreline" ? .024f : .02f, 0);
            }
            // 海面延伸到不可见边界之外，避免露出旧测试地板；不扩大实际可行走区域。
            island.transform.Find("Ocean").localScale = new Vector3(w * 4, .05f, h * 4);
            var fire = island.transform.Find("Campfire");
            fire.localPosition = new Vector3(islandParameters.CampfireX, .12f, islandParameters.CampfireY);
            var flame = fire.Find("Flame");
            flame.localScale = Vector3.one * (islandParameters.CampfireRadius / islandFlameBaseRadius);
            var danger = fire.Find("DangerBoundary");
            // 提示的是玩家中心何时受伤；实际接触判定包含玩家半径，火焰外延不作判定依据。
            float dangerScale = (islandParameters.CampfireRadius + catalog.Rule.PlayerRadius) / islandDangerMeshRadius;
            danger.localScale = Vector3.one * dangerScale;
            danger.gameObject.SetActive(islandFireActive);
            if (flame.gameObject.activeSelf != islandFireActive)
            {
                flame.gameObject.SetActive(islandFireActive);
                foreach (var ps in flame.GetComponentsInChildren<ParticleSystem>(true))
                    if (islandFireActive) ps.Play(); else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ReleaseIsland()
        {
            islandGeneration++;
            islandLoadFailed = false;
            RestoreIslandBaseArena();
            DestroyObject(island); island = null;
            ReleaseIslandMeshes();
            ReleaseObstacleVisualLoad(islandLoad); islandLoad = null;
            if (root != null && root.activeSelf && contentEnvironment != null) HideIslandBaseArena();
        }

        private void ReleaseIslandMeshes()
        {
            foreach (var mesh in new[] { islandSandMesh, islandWaterMesh, islandShorelineMesh })
                if (mesh != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                }
            islandSandMesh = islandWaterMesh = islandShorelineMesh = null;
        }

        /// <summary>仅在岛岸候选加载成功后隐藏当前场地的旧地板与四面墙；重复调用不覆盖原始状态。</summary>
        private void HideIslandBaseArena()
        {
            if (islandBaseArenaRoot == null || islandBaseVisibility.Count > 0) return;
            foreach (var renderer in islandBaseArenaRoot.GetComponentsInChildren<Renderer>(true))
            {
                switch (renderer.gameObject.name)
                {
                    case "Ground":
                    case "NorthBoundary":
                    case "SouthBoundary":
                    case "EastBoundary":
                    case "WestBoundary":
                        islandBaseVisibility.Add(renderer, renderer.enabled);
                        renderer.enabled = false;
                        break;
                }
            }
        }

        /// <summary>区域切换、离场或失败回滚时恢复本次覆盖；地图已销毁的对象直接跳过。</summary>
        private void RestoreIslandBaseArena()
        {
            foreach (var pair in islandBaseVisibility)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            islandBaseVisibility.Clear();
        }
    }
}
