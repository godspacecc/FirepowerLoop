using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Game.Tests
{
    internal static class TemporaryBuildingBehaviorTestSupport
    {
        public const long MapInstanceId = 901;
        private static TemporaryBuildingCtrl realBuildingController;

        public static void BeginIsolatedScene()
        {
            EditModeSceneIsolation.Enter();
            EditModeSceneIsolation.RequireActiveTestScene();
        }

        public static void EndIsolatedScene()
        {
            EditModeSceneIsolation.Exit();
        }

        public static void InstallArchitecture(TestRuntimeConfigProvider config, FakeTemporaryBuildingViewPort viewPort, FakeProjectilePort projectile, bool installPlayerInput = false, bool registerRefundPort = true, FakeBuildingRefundDropPort refundPort = null, bool useRealLoot = false, bool useRealBuilding = false)
        {
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new TestLocalizationResourceLoader());
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new AudioSettingsModel());
            architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new PlayerModel());
            architecture.RegisterModel(new PlayerCameraModeModel());
            architecture.RegisterModel(new PlayerAttributeModel());
            architecture.RegisterModel(new PlayerBasePoseModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new CombatModel());
            architecture.RegisterModel(new ProjectileModel());
            architecture.RegisterModel(new TestCmdModel());
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterModel(new SearchContainerModel());
            architecture.RegisterModel(new LootModel());
            architecture.RegisterModel(new TemporaryBuildingModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(config);
            architecture.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            if (useRealBuilding)
            {
                realBuildingController = new TemporaryBuildingCtrl(architecture);
                architecture.RegisterUtility<ITemporaryBuildingViewPort>(realBuildingController);
            }
            else
            {
                architecture.RegisterUtility<ITemporaryBuildingViewPort>(viewPort);
            }
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            architecture.RegisterUtility(new ViewCtrlRegistry());
            architecture.RegisterUtility<IPlayerRuntimePortProvider>(new FakePlayerRuntimePortProvider());
            architecture.RegisterUtility<IProjectileRuntimePortProvider>(projectile);
            var realLoot = useRealLoot ? new LootCtrl(architecture) : null;
            if (registerRefundPort)
            {
                architecture.RegisterUtility<IBuildingRefundDropPort>(realLoot ?? (IBuildingRefundDropPort)(refundPort ?? new FakeBuildingRefundDropPort()));
            }
            architecture.RegisterUtility<IEnemyColliderResolver>(new FakeEnemyResolver());
            architecture.RegisterSystem(new SaveSystem());
            architecture.RegisterSystem(new AppStateSystem());
            architecture.RegisterSystem(new UnitSystem());
            architecture.RegisterSystem(new PlayerActionSystem());
            architecture.RegisterSystem(new InventorySystem());
            // 材料消费会校验当前手持来源，建筑夹具必须安装正式库存事务依赖。
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            architecture.RegisterSystem(new LootSystem());
            architecture.RegisterSystem(new CombatSystem());
            architecture.RegisterSystem(new SearchContainerSystem());
            architecture.RegisterSystem(new ProjectileSystem());
            if (installPlayerInput)
            {
                architecture.RegisterSystem(new PlayerInputSystem());
            }
            architecture.RegisterSystem(new TemporaryBuildingSystem());
        }

        public static void EnterExpedition()
        {
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData
            {
                ExpeditionId = Guid.NewGuid().ToString("N"),
                MapId = MapIds.SurvivalSample,
                Status = ExpeditionStatus.Playing
            });
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.InExpedition);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId, MapInstanceId, UnitType.Player, UnitSource.Player, "player.graybox.default", Vector3.zero,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }

        public static void EnterBase()
        {
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 5f), false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId, MapInstanceId, UnitType.Player, UnitSource.Player, "player.graybox.default", Vector3.zero,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }

        public static GameObject CreateGround()
        {
            var ground = new GameObject("TemporaryBuildingTestGround");
            var collider = ground.AddComponent<BoxCollider>();
            collider.size = new Vector3(30f, 0.2f, 30f);
            collider.center = new Vector3(0f, -0.1f, 0f);
            return ground;
        }

        public static void AddBagItem(int itemId, int amount)
        {
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(itemId, amount, 20, out _, out var error), Is.True, error);
        }

        public static void MovePlayer(Vector3 position, long mapInstanceId = MapInstanceId)
        {
            var unitSystem = Game.Interface.GetSystem<UnitSystem>();
            Assert.That(unitSystem.Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit), Is.True);
            Assert.That(unitSystem.TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId, mapInstanceId, UnitType.Player, UnitSource.Player, "player.graybox.default", position,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }

        public static string BagSnapshot() => string.Join("|", Game.Interface.GetModel<PlayerBagModel>().Container.Slots.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.ItemId}:{p.Value.Amount}"));

        public static int Build(string configId, Vector3 position)
        {
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            Assert.That(system.BeginBuildPreview(configId, out var error), Is.True, error);
            Assert.That(system.UpdateBuildPreview(position, Quaternion.identity), Is.True);
            var confirmed = system.ConfirmBuild(out error);
            Assert.That(confirmed, Is.True, error + "；碰撞现场：" + string.Join(";", Physics.OverlapSphere(position, 1f)
                .Select(c => c.name + "@" + c.gameObject.scene.name + ", center=" + c.bounds.center + ", trigger=" + c.isTrigger)));
            system.TickForTests(1.1f);
            return Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.Keys.Max();
        }

        public static GameObject CreateUiRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var child = new GameObject(layer, typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
            }
            var poolRoot = new GameObject("PoolRoot", typeof(RectTransform));
            poolRoot.transform.SetParent(root.transform, false);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null) eventSystem.AddComponent(inputModuleType);
            root.AddComponent<UIRoot>();
            return root;
        }

        public static void Reset()
        {
            FakePlayerRuntimePortProvider.PlayerCollider = null;
            EndIsolatedScene();
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            if (realBuildingController != null)
            {
                realBuildingController.ReleaseAll();
                realBuildingController = null;
            }
            TestReset.ResetArchitecture();
        }

        public static MapSceneSystem RegisterMapSystem(FakeMapContentLoader loader, LootCtrl existingLoot = null)
        {
            var architecture = (Game)Game.Interface;
            architecture.RegisterUtility<IMapContentLoader>(loader);
            architecture.RegisterUtility(new ViewCtrlRegistry());
            architecture.RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            var projectile = new ProjectileCtrl(architecture);
            var player = new PlayerCtrl(architecture);
            var npc = new NpcCtrl(architecture);
            var enemy = new EnemyCtrl(architecture);
            var loot = existingLoot ?? new LootCtrl(architecture);
            var extraction = new ExtractionCtrl(architecture);
            architecture.RegisterSystem(new AudioSystem());
            var system = new MapSceneSystem(projectile, player, npc, enemy, loot, extraction);
            architecture.RegisterSystem(system);
            ((ICanInit)system).Init();
            system.Initialized = true;
            return system;
        }

        public static FakeMapHandle CreateMapHandle(string mapId, long mapInstanceId)
        {
            var root = new GameObject("Map-" + mapId + "-" + mapInstanceId);
            var presentation = new GameObject("MapContentRoot");
            presentation.transform.SetParent(root.transform, false);
            var runtime = new GameObject("DynamicObjectRoot");
            runtime.transform.SetParent(presentation.transform, false);
            var context = root.AddComponent<MapContext>();
            typeof(MapContext).GetField("mapContentRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(context, presentation);
            typeof(MapContext).GetField("dynamicObjectRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(context, runtime.transform);
            return new FakeMapHandle(mapId, context);
        }
    }

    internal sealed class TestRuntimeConfigProvider : IRuntimeConfigProvider
    {
        private readonly bool includeMapPlayerSpawn;
        private readonly IReadOnlyList<string> mapSceneAddresses;

        public TestRuntimeConfigProvider(bool includeMapPlayerSpawn = false, string arrowIconAddress = null, IReadOnlyList<string> mapSceneAddresses = null)
        {
            this.includeMapPlayerSpawn = includeMapPlayerSpawn;
            this.mapSceneAddresses = mapSceneAddresses;
            buildings = new[]
            {
                new TempBuildingDefinition("temp_arrow_tower", "固定箭塔", "temp_building.arrow_tower.name", arrowIconAddress ?? string.Empty, "Prefabs/Building/GrayboxArrowTower", new[] { new ItemCostDefinition(ItemIds.MetalParts, 2), new ItemCostDefinition(ItemIds.Wood, 3) }, 1f, .75f, 20f, 1.2f, true, 100),
                new TempBuildingDefinition("temp_roadblock_wood", "木质路障", "temp_building.roadblock_wood.name", string.Empty, "Prefabs/Building/GrayboxRoadblock", new[] { new ItemCostDefinition(ItemIds.Wood, 3) }, 1f, .9f, 20f, 1.2f, true, 200),
                new TempBuildingDefinition("temp_campfire", "火堆", "temp_building.campfire.name", string.Empty, "Prefabs/Building/GrayboxCampfire", new[] { new ItemCostDefinition(ItemIds.Wood, 2) }, .5f, .6f, 20f, 0f, false, 300)
            };
        }

        private readonly IReadOnlyList<TempBuildingDefinition> buildings;

        public bool TryGetItem(int id, out ItemDefinition value, out string error)
        {
            if (id == ItemIds.Medkit)
            {
                value = new ItemDefinition(id, "急救包", string.Empty, "item.medkit.name", string.Empty, "Art/UI/Items/IconMedkit", ItemType.Consumable, 5, ItemUseEffect.RestoreHealth, 30, 1f);
                error = null;
                return true;
            }

            if (id == ItemIds.MetalParts || id == ItemIds.Wood || id == ItemIds.Arrow)
            {
                var nameKey = id == ItemIds.MetalParts ? "item.metal_parts.name" : id == ItemIds.Wood ? "item.wood.name" : "item.ammunition.arrow.name";
                value = new ItemDefinition(id, id.ToString(), string.Empty, nameKey, string.Empty, string.Empty, id == ItemIds.Arrow ? ItemType.Ammunition : ItemType.Material, 20, ItemUseEffect.None, 0);
                error = null;
                return true;
            }
            value = default;
            error = "missing item";
            return false;
        }

        public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
        {
            if (id == "projectile.arrow.basic")
            {
                value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                error = null;
                return true;
            }
            value = default;
            error = "missing projectile";
            return false;
        }

        public bool TryGetTempBuilding(string id, out TempBuildingDefinition value, out string error)
        {
            value = buildings.FirstOrDefault(v => v.Id == id);
            if (value != null) { error = null; return true; }
            error = "missing building";
            return false;
        }

        public bool TryGetTempBuildings(out IReadOnlyList<TempBuildingDefinition> values, out string error) { values = buildings; error = null; return true; }
        public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error) { values = Array.Empty<CraftRecipeDefinition>(); error = null; return true; }
        public bool TryGetTempBuildingManualAttack(string id, out TempBuildingManualAttackDefinition value)
        {
            if (id == "temp_arrow_tower") { value = new TempBuildingManualAttackDefinition(id, 2.2f, .8f, 24, "projectile.arrow.basic", ItemIds.Arrow, 1, 5, 2f, 180f); return true; }
            value = null;
            return false;
        }
        public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
        {
            value = string.Equals(id, MapIds.HeJiaVillage, StringComparison.Ordinal) && mapSceneAddresses != null
                ? new MapAssetDefinition(id, mapSceneAddresses)
                : new MapAssetDefinition(id, "Scenes/FakeMap");
            error = null;
            return true;
        }
        public bool TryGetMapSpawns(string id, out IReadOnlyList<MapSpawnDefinition> values, out string error)
        {
            values = includeMapPlayerSpawn
                ? new[] { new MapSpawnDefinition("player.spawn", id, MapSpawnObjectType.Player, "player", 0f, 0.1f, 0f, 0f) }
                : Array.Empty<MapSpawnDefinition>();
            error = null;
            return true;
        }
        public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error) { value = new PlayerConfigDefinition(id, "Prefabs/Characters/GrayboxPlayer", 100, 1, 1f, 1f, 1f, 1f, 5f, 3, 1.4f, .6f, 75f, 3f); error = null; return true; }
        public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) { value = default; error = "unused"; return false; }
        public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
        {
            if (id == "loot.wood") { value = new LootConfigDefinition(id, "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Wood, 3); error = null; return true; }
            if (id == "loot.metal-parts") { value = new LootConfigDefinition(id, "Prefabs/SceneObjects/TestResourcePickup", ItemIds.MetalParts, 2); error = null; return true; }
            if (id == "loot.arrow") { value = new LootConfigDefinition(id, "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Arrow, 4); error = null; return true; }
            value = default;
            error = "unused";
            return false;
        }
        public bool TryGetLootByItemId(int itemId, out LootConfigDefinition value, out string error)
        {
            if (itemId == ItemIds.Wood)
            {
                value = new LootConfigDefinition("loot.wood", "Prefabs/SceneObjects/TestResourcePickup", itemId, 1);
                error = null;
                return true;
            }
            if (itemId == ItemIds.MetalParts)
            {
                value = new LootConfigDefinition("loot.metal-parts", "Prefabs/SceneObjects/TestResourcePickup", itemId, 1);
                error = null;
                return true;
            }
            if (itemId == ItemIds.Arrow)
            {
                value = new LootConfigDefinition("loot.arrow", "Prefabs/SceneObjects/TestResourcePickup", itemId, 1);
                error = null;
                return true;
            }
            value = default;
            error = "missing loot";
            return false;
        }
        public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) { value = default; error = "unused"; return false; }
    }

    internal sealed class FakeTemporaryBuildingViewPort : ITemporaryBuildingViewPort
    {
        public int PreviewCreateCount { get; private set; }
        public int PreviewReleaseCount { get; private set; }
        public int BuildingCreateCount { get; private set; }
        public int BuildingReleaseCount { get; private set; }
        public readonly Dictionary<int, TemporaryBuildingRuntimeState> Buildings = new Dictionary<int, TemporaryBuildingRuntimeState>();
        public readonly Dictionary<int, TempBuildingDefinition> Definitions = new Dictionary<int, TempBuildingDefinition>();
        public readonly Dictionary<long, long> ReleasedMaps = new Dictionary<long, long>();
        public readonly Dictionary<int, Vector3> ManualAimDirections = new Dictionary<int, Vector3>();
        public int PickedRuntimeId { get; set; }
        public int LastHudLoadedAmmo { get; private set; }
        public int LastHudCapacity { get; private set; }
        public bool LastHudOperating { get; private set; }
        public PlayerActionSnapshot? LastReloadSnapshot { get; private set; }
        public int HighlightedRuntimeId { get; private set; }
        public int EmptyAmmoFeedbackCount { get; private set; }
        public int LastEmptyAmmoFeedbackRuntimeId { get; private set; }
        public bool CreateBuildingSucceeds { get; set; } = true;
        public bool TryCreatePreview(long map, TempBuildingDefinition definition, bool manual, Vector3 position, Quaternion rotation, out string error) { PreviewCreateCount++; error = null; return true; }
        public void UpdatePreview(Vector3 position, Quaternion rotation, bool valid) { }
        public void ReleasePreview() { PreviewReleaseCount++; }
        public bool TryCreateBuilding(TemporaryBuildingRuntimeState state, TempBuildingDefinition definition, bool manual, out string error) { BuildingCreateCount++; if (!CreateBuildingSucceeds) { error = "create blocked"; return false; } Buildings[state.RuntimeId] = state; Definitions[state.RuntimeId] = definition; error = null; return true; }
        public bool TryGetManualAttackPose(int runtimeId, out Vector3 origin, out Vector3 direction) { origin = Buildings.TryGetValue(runtimeId, out var b) ? b.Position + Vector3.up : Vector3.up; direction = ManualAimDirections.TryGetValue(runtimeId, out var aim) ? aim : Vector3.forward; return Buildings.ContainsKey(runtimeId); }
        public void SetManualAttackAimDirection(int runtimeId, Vector3 direction) { ManualAimDirections[runtimeId] = direction; }
        public void SetManualAttackHud(int runtimeId, bool operating, int ammoItemId, int loadedAmmo, int capacity, PlayerActionSnapshot? reloadSnapshot) { LastHudOperating = operating; LastHudLoadedAmmo = loadedAmmo; LastHudCapacity = capacity; LastReloadSnapshot = reloadSnapshot; }
        public void PlayManualAttackEmptyAmmoFeedback(int runtimeId) { EmptyAmmoFeedbackCount++; LastEmptyAmmoFeedbackRuntimeId = runtimeId; }
        public bool TryPickCommittedBuilding(Ray pointerRay, float maxDistance, out int runtimeId) { runtimeId = PickedRuntimeId; return runtimeId > 0 && Buildings.ContainsKey(runtimeId); }
        public void FaceManualAttackWorldHuds(Vector3 cameraPosition, Vector3 cameraUp) { }
        public void SetOperating(int runtimeId, bool active) { }
        public void SetDestroyHighlight(int runtimeId, bool active) { if (active) HighlightedRuntimeId = runtimeId; else if (HighlightedRuntimeId == runtimeId) HighlightedRuntimeId = 0; }
        public void ReleaseBuilding(int runtimeId) { BuildingReleaseCount++; Buildings.Remove(runtimeId); Definitions.Remove(runtimeId); }
        public void ReleaseMap(long mapInstanceId) { foreach (var id in Buildings.Where(p => p.Value.MapInstanceId == mapInstanceId).Select(p => p.Key).ToArray()) ReleaseBuilding(id); ReleasedMaps[mapInstanceId] = mapInstanceId; }
        public void ReleaseAll() { ReleasePreview(); foreach (var id in Buildings.Keys.ToArray()) ReleaseBuilding(id); }
    }

    internal sealed class FakeProjectilePort : IProjectileRuntimePortProvider
    {
        public int CreateCount { get; private set; }
        public string LastPrefabAddress { get; private set; }
        public Vector3 LastDirection { get; private set; }
        public bool CreateSucceeds { get; set; } = true;
        public void ReleaseProjectile(int id) { }
        public bool TryCreateProjectile(int id, long map, Vector3 pos, Vector3 direction, string prefab, out string error) { CreateCount++; LastPrefabAddress = prefab; LastDirection = direction; error = CreateSucceeds ? null : "projectile create blocked"; return CreateSucceeds; }
        public void MoveProjectile(int id, Vector3 pos, Vector3 direction) { }
        public void CollectMovementHits(int id, Vector3 from, Vector3 to, List<ProjectileMovementHit> results) { results.Clear(); }
    }

    internal sealed class FakeBuildingRefundDropPort : IBuildingRefundDropPort
    {
        public bool CreateSucceeds { get; set; } = true;
        public int CreateCount { get; private set; }
        public int RollbackCount { get; private set; }
        public long LastMapInstanceId { get; private set; }
        public Vector3 LastPosition { get; private set; }
        public Quaternion LastRotation { get; private set; }
        public IReadOnlyList<BuildingRefundDrop> LastDrops { get; private set; }
        public Action AfterSuccessfulCreate { get; set; }

        public bool TryCreateBuildingRefundDrops(long mapInstanceId, Vector3 position, Quaternion rotation,
            IReadOnlyList<BuildingRefundDrop> drops, out BuildingRefundDropReceipt receipt, out string error)
        {
            CreateCount++;
            LastMapInstanceId = mapInstanceId;
            LastPosition = position;
            LastRotation = rotation;
            LastDrops = drops?.ToArray() ?? Array.Empty<BuildingRefundDrop>();
            receipt = new BuildingRefundDropReceipt(new[] { 10000 + CreateCount });
            error = CreateSucceeds ? null : "refund blocked";
            if (CreateSucceeds) AfterSuccessfulCreate?.Invoke();
            return CreateSucceeds;
        }

        public void RollbackBuildingRefundDrops(BuildingRefundDropReceipt receipt)
        {
            RollbackCount++;
        }
    }

    internal sealed class TestLocalizationResourceLoader : IResourceLoader
    {
        public T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            if (typeof(T) == typeof(GameObject) && !string.IsNullOrWhiteSpace(path))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab") as T;
            }
            if (typeof(T) != typeof(TextAsset)) return null;
            var assetPath = path == "languages.json"
                ? "Assets/Game/Config/Localization/languages.json"
                : "Assets/Game/Config/Localization/" + path;
            return AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath) as T;
        }

        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
        {
            onLoaded?.Invoke(LoadAsset<T>(path));
            return ResourceLoadHandle.Empty;
        }

        public void Release(ResourceLoadHandle handle) { }
    }

    internal sealed class FakePlayerRuntimePortProvider : IPlayerRuntimePortProvider
    {
        public static Collider PlayerCollider { get; set; }

        public bool TryGetMotorPort(int unitId, long map, out ICharacterMotorPort port)
        {
            port = Game.Interface.GetSystem<UnitSystem>().TryGet(unitId, out var unit) && unit.MapInstanceId == map
                ? new FakeCharacterMotorPort(unit)
                : null;
            return port != null;
        }
        public bool TryGetCameraPort(int unitId, long map, out IPlayerCameraPort port) { port = null; return false; }
        public bool IsPlayerCollider(int unitId, long map, Collider collider) => collider != null && collider == PlayerCollider;
    }

    internal sealed class FakeCharacterMotorPort : ICharacterMotorPort
    {
        public static Func<Vector3, Vector3> MotionResolverForTests { get; set; }
        private Vector3 position;
        private Quaternion rotation;

        public FakeCharacterMotorPort(UnitSnapshot unit) { UnitId = unit.UnitId; MapInstanceId = unit.MapInstanceId; position = unit.Position; rotation = unit.Rotation; }
        public int UnitId { get; }
        public long MapInstanceId { get; }
        public bool IsValid => true;
        public bool IsEnabled => true;
        public Vector3 Position => position;
        public Quaternion Rotation => rotation;
        public float MovementYawDegrees => rotation.eulerAngles.y;
        public bool IsGrounded => true;
        public void Move(Vector3 motion) => position += MotionResolverForTests != null ? MotionResolverForTests(motion) : motion;
        public void RotateTowards(Quaternion target, float maxDegreesDelta) => rotation = Quaternion.RotateTowards(rotation, target, maxDegreesDelta);
        public void FaceDirection(Vector3 direction) { if (direction.sqrMagnitude > .0001f) rotation = Quaternion.LookRotation(direction, Vector3.up); }
        public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
        public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = position + Vector3.up; return true; }
        public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
        public void PlayAttackFeedback() { }
        public void PlayWeaponSwingFeedback() { }
        public void PlayHurtFeedback() { }
        public void TickCombatFeedback(float deltaTime) { }
    }

    internal sealed class FakeEnemyResolver : IEnemyColliderResolver
    {
        public bool TryResolveEnemyUnitId(Collider collider, out int unitId) { unitId = 0; return false; }
    }

    internal sealed class FakeMapContentLoader : IMapContentLoader
    {
        public bool FailLoad { get; set; }
        public bool FailNextLoad { get; set; }
        public bool FailNextUnload { get; set; }
        public bool InvalidateBeforeUnloadFailure { get; set; }
        public System.Action OnUnloadSuccess { get; set; }
        public int UnloadCount { get; private set; }
        public IMapContentHandle LastUnloaded { get; private set; }
        public readonly List<FakeMapHandle> LoadedHandles = new List<FakeMapHandle>();
        public readonly List<string> LoadedAddresses = new List<string>();
        public readonly List<IMapContentHandle> UnloadedHandles = new List<IMapContentHandle>();
        public readonly List<IMapContentHandle> ReleasedHandles = new List<IMapContentHandle>();
        public readonly List<bool> UnloadTokensCanBeCanceled = new List<bool>();
        public int FailLoadAtIndex { get; set; } = -1;
        public int FailUnloadAtCall { get; set; } = -1;
        public IMapContentHandle PersistentlyFailUnloadHandle { get; set; }
        public int PersistentlyFailLoadedHandleIndex { get; set; } = -1;
        public Action<int, IMapContentHandle, CancellationToken> OnUnloadCall { get; set; }
        public int DuplicateTerrainCoordinateAtIndex { get; set; } = -1;
        public void ResetObservations()
        {
            LoadedHandles.Clear();
            LoadedAddresses.Clear();
            UnloadedHandles.Clear();
            ReleasedHandles.Clear();
            UnloadTokensCanBeCanceled.Clear();
            UnloadCount = 0;
            LastUnloaded = null;
            FailLoadAtIndex = -1;
            FailUnloadAtCall = -1;
            PersistentlyFailUnloadHandle = null;
            PersistentlyFailLoadedHandleIndex = -1;
            OnUnloadCall = null;
            DuplicateTerrainCoordinateAtIndex = -1;
        }
        public UniTask<MapContentLoadResult> LoadAsync(string mapId, string address, Action<float> onProgress, CancellationToken cancellationToken = default)
        {
            var loadIndex = LoadedAddresses.Count;
            LoadedAddresses.Add(address);
            onProgress?.Invoke(0.5f);
            if (FailLoad || FailNextLoad || loadIndex == FailLoadAtIndex)
            {
                FailNextLoad = false;
                return UniTask.FromResult(MapContentLoadResult.Fail("candidate load failed"));
            }

            var handle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(mapId, 77 + LoadedHandles.Count);
            if (string.Equals(mapId, MapIds.HeJiaVillage, StringComparison.Ordinal))
            {
                var coordinates = new[] { (1, 1), (1, 2), (2, 1), (2, 2) };
                var coordinate = coordinates[Math.Min(loadIndex, coordinates.Length - 1)];
                if (loadIndex == DuplicateTerrainCoordinateAtIndex) coordinate = (1, 1);
                AddTerrainTile(handle.Context, coordinate.Item1, coordinate.Item2);
            }
            LoadedHandles.Add(handle);
            onProgress?.Invoke(1f);
            return UniTask.FromResult(MapContentLoadResult.Ok(handle));
        }
        public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
        {
            UnloadCount++;
            LastUnloaded = handle;
            UnloadedHandles.Add(handle);
            UnloadTokensCanBeCanceled.Add(cancellationToken.CanBeCanceled);
            OnUnloadCall?.Invoke(UnloadCount, handle, cancellationToken);
            var persistentlyFailsByIndex = PersistentlyFailLoadedHandleIndex >= 0 &&
                                           PersistentlyFailLoadedHandleIndex < LoadedHandles.Count &&
                                           ReferenceEquals(handle, LoadedHandles[PersistentlyFailLoadedHandleIndex]);
            if (ReferenceEquals(handle, PersistentlyFailUnloadHandle) || persistentlyFailsByIndex ||
                FailNextUnload || UnloadCount == FailUnloadAtCall)
            {
                FailNextUnload = false;
                if (InvalidateBeforeUnloadFailure && handle is FakeMapHandle failedHandle && failedHandle.Root != null)
                {
                    failedHandle.Invalidate();
                    UnityEngine.Object.DestroyImmediate(failedHandle.Root);
                    ReleasedHandles.Add(handle);
                }
                return UniTask.FromResult(MapContentUnloadResult.Fail("old map unload failed"));
            }

            OnUnloadSuccess?.Invoke();
            if (handle is FakeMapHandle fake && fake.Root != null) UnityEngine.Object.DestroyImmediate(fake.Root);
            ReleasedHandles.Add(handle);
            return UniTask.FromResult(MapContentUnloadResult.Ok());
        }

        private static void AddTerrainTile(MapContext context, int row, int column)
        {
            var go = new GameObject($"GeneratedTerrainTile-{row}-{column}");
            go.transform.SetParent(context.MapContentRoot.transform, false);
            var data = new TerrainData { heightmapResolution = 513, size = new Vector3(1024f, 330f, 1024f) };
            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            var tile = go.AddComponent<GeneratedTerrainTile>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(GeneratedTerrainTile).GetField("mapName", flags)?.SetValue(tile, "HeJiaVillage");
            typeof(GeneratedTerrainTile).GetField("row", flags)?.SetValue(tile, row);
            typeof(GeneratedTerrainTile).GetField("column", flags)?.SetValue(tile, column);
            typeof(GeneratedTerrainTile).GetField("totalRows", flags)?.SetValue(tile, 2);
            typeof(GeneratedTerrainTile).GetField("totalColumns", flags)?.SetValue(tile, 2);
            typeof(GeneratedTerrainTile).GetField("tileWorldSize", flags)?.SetValue(tile, 1024f);
            typeof(GeneratedTerrainTile).GetField("heightmapResolution", flags)?.SetValue(tile, 513);
            typeof(GeneratedTerrainTile).GetField("terrain", flags)?.SetValue(tile, terrain);
        }
    }

    internal sealed class FakeMapHandle : IMapContentHandle
    {
        private bool invalidated;
        public FakeMapHandle(string mapId, MapContext context) { MapId = mapId; Context = context; }
        public string MapId { get; }
        public MapContext Context { get; }
        public bool IsValid => !invalidated && Context != null;
        public GameObject Root => Context != null ? Context.gameObject : null;
        public void Invalidate() => invalidated = true;
    }

    internal sealed class TemporaryBuildingInputReader : PlayerInputSystem.IPlayerInputReader
    {
        public Vector2 MoveInput { get; set; }
        public bool IsEnabled { get; private set; }
        public bool AttackPressedThisFrame { get; set; }
        public bool HolsterPressedThisFrame { get; set; }
        public bool MeleeSlotPressedThisFrame => false;
        public bool RangedSlotPressedThisFrame => false;
        public bool InteractPressedThisFrame { get; set; }
        public bool SprintHeld { get; set; }
        public bool BuildPressedThisFrame => false;
        public bool DestroyPressedThisFrame { get; set; }
        public bool CancelPressedThisFrame { get; set; }
        public bool ReloadPressedThisFrame { get; set; }
        public void Enable() => IsEnabled = true;
        public void Disable() => IsEnabled = false;
        public void Dispose() { }
        public void ClearPressed()
        {
            AttackPressedThisFrame = false;
            InteractPressedThisFrame = false;
            DestroyPressedThisFrame = false;
            CancelPressedThisFrame = false;
            ReloadPressedThisFrame = false;
        }
    }

    internal sealed class LocalizationAssetResourceLoader : IResourceLoader
    {
        public string IconAddress { get; set; }
        public Sprite IconSprite { get; set; }

        public T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            if (typeof(T) == typeof(Sprite) && path == IconAddress)
            {
                return IconSprite as T;
            }

            var full = Path.Combine("Assets/Game/Config/Localization", path);
            if (typeof(T) == typeof(TextAsset) && File.Exists(full)) return new TextAsset(File.ReadAllText(full)) as T;
            return AssetDatabase.LoadAssetAtPath<T>(Path.Combine("Assets/Game", path + ".prefab").Replace("\\", "/"));
        }
        public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object { onLoaded?.Invoke(LoadAsset<T>(path)); return new ResourceLoadHandle(new object()); }
        public void Release(ResourceLoadHandle handle) { }
    }
}
