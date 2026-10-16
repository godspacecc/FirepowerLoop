using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
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
        /// <summary>零沿用联机原图，单机首场由权威传入一至四；等待资源期间不允许开战。</summary>
        private int islandRound, islandMeshRound = -1;
        private bool islandCampfireWarning;
        private Mesh islandWreckMesh;
        private GameObject islandWreck;
        private Material islandWarningMaterial;
        /// <summary>专用地标跟随同源碰撞根，生成网格由本岛岸句柄唯一释放。</summary>
        private readonly List<GameObject> islandLandmarks = new();
        private readonly List<Mesh> islandLandmarkMeshes = new();
        private bool islandLandmarksReady;
        public bool IsIslandRoundReady => islandRound == 0
            ? root != null && obstacleVisualReady && !contentLoadFailed && (contentArena > 3 || contentTemplates != null)
            :
            (island != null && islandSandMesh != null && islandMeshRound == islandRound && !islandLoadFailed &&
                obstacleVisualReady && contentTemplates != null && !contentLoadFailed && islandLandmarksReady);
        public bool IsIslandTreasureReady => contentTemplates != null && contentEnvironment != null && !contentLoadFailed;
        public string IslandRoundLoadError => islandLoadFailed ? "海岛地图资源加载失败，请重试。" :
            contentLoadFailed ? "地图地标资源加载失败，请重试。" : obstacleVisualError;

        /// <summary>仅选择布局及重绘，不修改轮次、角色或效果状态。</summary>
        public void SetIslandRound(int roundNumber)
        {
            if (roundNumber < 0 || roundNumber > 4) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            islandRound = roundNumber;
            if (island != null) ConfigureIsland();
        }

        /// <summary>失败后重试同一轮资源；句柄及代次仍走既有唯一释放路径。</summary>
        public void RetryIslandRoundLoad()
        {
            islandLoadFailed = false;
            SetOpening(islandParameters, islandFireActive);
        }

        /// <summary>预警仅显示危险边界，正式启用前不播放火焰。</summary>
        public void SetIslandCampfireWarning(bool warning)
        {
            islandCampfireWarning = warning;
            if (island != null) ConfigureIsland();
        }
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
            try
            {
                state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(IslandAddress, p => completion.TrySetResult(p));
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
                DestroyObject(islandWarningMaterial); islandWarningMaterial = null;
                ReleaseObstacleVisualLoad(state);
                if (ReferenceEquals(islandLoad, state)) islandLoad = null;
            }
        }

        private void ConfigureIsland()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetArcadeCatalog(out var catalog, out _)) return;
            float w = catalog.Rule.ArenaHalfWidth, h = catalog.Rule.ArenaHalfHeight;
            if (islandSandMesh == null || islandMeshWidth != w || islandMeshHeight != h || islandMeshRound != islandRound)
            {
                ReleaseIslandMeshes();
                var region = new ArcadeIslandRegion(w, h, islandRound);
                islandSandMesh = ArcadeIslandMesh.Sand(region); islandWaterMesh = ArcadeIslandMesh.Water(region);
                islandShorelineMesh = ArcadeIslandMesh.Shoreline(region); islandMeshWidth = w; islandMeshHeight = h;
                islandMeshRound = islandRound;
                island.transform.Find("Sand").GetComponent<MeshFilter>().sharedMesh = islandSandMesh;
                island.transform.Find("Water").GetComponent<MeshFilter>().sharedMesh = islandWaterMesh;
                island.transform.Find("Shoreline").GetComponent<MeshFilter>().sharedMesh = islandShorelineMesh;
                if (islandRound == 3)
                {
                    var ship = ArcadeIslandLayout.GetObstacles(3, w, h)[0];
                    islandWreckMesh = ArcadeIslandMesh.Wreck(ship.HalfExtents.x * 2, ship.HalfExtents.y * 2);
                    islandWreck = new GameObject("WreckLandmark", typeof(MeshFilter), typeof(MeshRenderer));
                    islandWreck.transform.SetParent(island.transform, false);
                    islandWreck.transform.localPosition = new Vector3(ship.Center.x,.03f,ship.Center.y);
                    islandWreck.GetComponent<MeshFilter>().sharedMesh = islandWreckMesh;
                    islandWreck.GetComponent<MeshRenderer>().sharedMaterial = island.transform.Find("Campfire/Logs").GetComponentInChildren<Renderer>(true).sharedMaterial;
                }
                BuildIslandLandmarks(w, h);
            }
            // 专用地标遮盖通用栅栏；异步通用资源晚到时，下一份快照也不会保留栅栏外观。
            if (islandRound > 0)
                foreach (var landmark in islandLandmarks)
                    if (landmark != null) landmark.transform.parent.Find("Visual")?.gameObject.SetActive(false);
            if (islandRound == 3) root.transform.Find("Obstacle_1/Visual")?.gameObject.SetActive(false);
            // 旧岸边棕榈、岩石仅作原图装饰；新图使用带真实同源阻挡的地标，避免装饰穿行误导。
            island.transform.Find("ShoreDecor")?.gameObject.SetActive(islandRound == 0);
            foreach (var name in new[] { "Sand", "Water", "Shoreline" })
            {
                var surface = island.transform.Find(name); surface.localScale = Vector3.one;
                surface.localPosition = new Vector3(0, name == "Shoreline" ? .024f : .02f, 0);
            }
            // 海面延伸到不可见边界之外，避免露出旧测试地板；不扩大实际可行走区域。
            island.transform.Find("Ocean").localScale = new Vector3(w * 4, .05f, h * 4);
            var fire = island.transform.Find("Campfire");
            fire.gameObject.SetActive(islandRound == 0 || islandRound == 4);
            fire.localPosition = new Vector3(islandParameters.CampfireX, .12f, islandParameters.CampfireY);
            var flame = fire.Find("Flame");
            flame.localScale = Vector3.one * (islandParameters.CampfireRadius / islandFlameBaseRadius);
            var danger = fire.Find("DangerBoundary");
            // 提示的是玩家中心何时受伤；实际接触判定包含玩家半径，火焰外延不作判定依据。
            float dangerScale = (islandParameters.CampfireRadius + catalog.Rule.PlayerRadius) / islandDangerMeshRadius;
            danger.localScale = Vector3.one * dangerScale;
            danger.gameObject.SetActive(islandFireActive || islandCampfireWarning);
            var dangerRenderer = danger.GetComponent<Renderer>();
            if (dangerRenderer != null && islandRound > 0)
            {
                if (islandWarningMaterial == null) islandWarningMaterial = new Material(dangerRenderer.sharedMaterial);
                islandWarningMaterial.SetColor("_BaseColor", islandCampfireWarning && !islandFireActive
                    ? new Color(1f,.8f,.15f,.6f) : new Color(1f,.22f,.05f,.65f));
                dangerRenderer.sharedMaterial = islandWarningMaterial;
            }
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
            DestroyObject(islandWarningMaterial); islandWarningMaterial = null;
            ReleaseObstacleVisualLoad(islandLoad); islandLoad = null;
            if (root != null && root.activeSelf && contentEnvironment != null) HideIslandBaseArena();
        }

        private void ReleaseIslandMeshes()
        {
            islandLandmarksReady = false;
            foreach (var landmark in islandLandmarks)
            {
                if (landmark != null) landmark.transform.parent.Find("Visual")?.gameObject.SetActive(true);
                DestroyObject(landmark);
            }
            islandLandmarks.Clear();
            foreach (var mesh in islandLandmarkMeshes) DestroyObject(mesh);
            islandLandmarkMeshes.Clear();
            // 岛岸根可能已先销毁船实例；由仍归本控制器持有的网格识别船的恢复责任。
            if (islandWreckMesh != null && root != null)
                root.transform.Find("Obstacle_1/Visual")?.gameObject.SetActive(true);
            DestroyObject(islandWreck); islandWreck = null;
            foreach (var mesh in new[] { islandSandMesh, islandWaterMesh, islandShorelineMesh, islandWreckMesh })
                if (mesh != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                }
            islandSandMesh = islandWaterMesh = islandShorelineMesh = null;
            islandWreckMesh = null; islandMeshRound = -1;
        }

        /// <summary>礁石可见底缘严格覆盖阻挡矩形；岩块与棕榈均收在该范围内，不额外制造碰撞。</summary>
        private void BuildIslandLandmarks(float width, float height)
        {
            if (islandRound == 0) { islandLandmarksReady = true; return; }
            var rock = island.transform.Find("ShoreDecor/Rock0");
            var palm = island.transform.Find("ShoreDecor/Palm0");
            var rockMaterial = rock != null ? rock.GetComponentInChildren<Renderer>(true)?.sharedMaterial : null;
            if (rock == null || palm == null || rockMaterial == null)
                throw new InvalidOperationException("岛岸礁石或棕榈地标模板缺失");
            var obstacles = ArcadeIslandLayout.GetObstacles(islandRound, width, height);
            for (int i = 0; i < obstacles.Length; i++)
            {
                if (islandRound == 3 && i == 0) continue;
                var owner = root.transform.Find("Obstacle_" + (i + 1));
                var collision = owner != null ? owner.GetComponent<BoxCollider>() : null;
                if (collision == null || !collision.enabled)
                    throw new InvalidOperationException("岛岸地标缺少同源阻挡根");
                var landmark = new GameObject("IslandLandmark");
                landmark.transform.SetParent(owner, false); islandLandmarks.Add(landmark);
                float w = obstacles[i].HalfExtents.x * 2, h = obstacles[i].HalfExtents.y * 2;
                var mesh = CreateIslandRockFootprint(w, h); islandLandmarkMeshes.Add(mesh);
                var baseRock = new GameObject("ReefFootprint", typeof(MeshFilter), typeof(MeshRenderer));
                baseRock.transform.SetParent(landmark.transform, false);
                baseRock.GetComponent<MeshFilter>().sharedMesh = mesh;
                baseRock.GetComponent<MeshRenderer>().sharedMaterial = rockMaterial;
                FitIslandLandmark(rock, landmark.transform, "IslandRock", w * .84f, 1.35f, h * .84f, .35f);
                if (i == 1 && (islandRound == 1 || islandRound == 4))
                    FitIslandLandmark(palm, landmark.transform, "IslandPalm", w * .75f, 3.7f, h * .75f, .6f);
            }
            islandLandmarksReady = true;
        }

        /// <summary>生成收角礁石；底缘四角和矩形规则一致，不能把阻挡画成细栅栏或零散装饰。</summary>
        private static Mesh CreateIslandRockFootprint(float width, float length)
        {
            var vertices = new Vector3[8];
            for (int i = 0; i < vertices.Length; i++)
            {
                bool top = (i & 2) != 0; float scale = top ? .78f : 1f;
                vertices[i] = new Vector3(((i & 1) == 0 ? -1 : 1) * width * .5f * scale,
                    top ? .8f : .025f, ((i & 4) == 0 ? -1 : 1) * length * .5f * scale);
            }
            var mesh = new Mesh { name = "IslandReefFootprint", vertices = vertices,
                triangles = new[] { 0,2,1,1,2,3,4,5,6,5,7,6,0,4,2,2,4,6,1,3,5,3,7,5,0,1,4,1,5,4,2,6,3,3,6,7 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        /// <summary>依据模板实际渲染包围盒分别缩放三轴；冠幅也必须位于真实阻挡内部。</summary>
        private static void FitIslandLandmark(Transform template, Transform parent, string name,
            float width, float height, float length, float bottom)
        {
            var instance = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
            instance.name = name; instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity; instance.transform.localScale = Vector3.one;
            instance.SetActive(true);
            bool hasBounds = false; var bounds = new Bounds();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    p = instance.transform.InverseTransformPoint(renderer.transform.TransformPoint(p));
                    if (hasBounds) bounds.Encapsulate(p); else { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                }
            }
            if (!hasBounds || bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                throw new InvalidOperationException("岛岸地标渲染尺寸无效：" + name);
            var scale = new Vector3(width / bounds.size.x, height / bounds.size.y, length / bounds.size.z);
            instance.transform.localScale = scale;
            instance.transform.localPosition = new Vector3(0, bottom, 0) - Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
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
