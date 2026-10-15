using QFramework;
using Game.Network;

namespace Game
{
    /// <summary>
    /// Another Day 架构入口类型 Game，负责连接启动顺序与业务容器初始化。
    /// </summary>
    public class Game : Architecture<Game>
    {
        private PlayerCtrl playerCtrl;
        private EnemyCtrl enemyCtrl;
        private LootCtrl lootCtrl;
        private ExtractionCtrl extractionCtrl;
        private ProjectileCtrl projectileCtrl;
        private NpcCtrl npcCtrl;
        private TemporaryBuildingCtrl temporaryBuildingCtrl;
        private ItemTransferCtrl itemTransferCtrl;
        private INetworkRuntime networkRuntime;
        private OnlineAccountLifecycleController networkLifecycle;
        private OnlineAccountSystem onlineAccountSystem;
        private OnlineWorldPlayerCtrl onlineWorldPlayerCtrl;
        private OnlineWorldEnemyCtrl onlineWorldEnemyCtrl;
        private OnlineWorldCorpseCtrl onlineWorldCorpseCtrl;
        private ArcadeCombatViewController arcadeCombatView;
        private CivilisationSceneCtrl civilisationSceneCtrl;

        protected override void Init()
        {
            RegisterModels();
            RegisterUtilities();
            RegisterItemTransfer();
            RegisterNetworkRuntime();
            CreateMapControllers();
            RegisterControllerPorts();
            RegisterSystems();
            civilisationSceneCtrl = new CivilisationSceneCtrl();
            civilisationSceneCtrl.Initialize();
            GetUtility<MapImageRefreshCtrl>().Initialize();
            InitializeNetworkLifecycle();
        }

        private void RegisterModels()
        {
            RegisterModel(new LocalizationModel());
            RegisterModel(new DisplaySettingsModel());
            RegisterModel(new AudioSettingsModel());
            RegisterModel(new HotKeyModel());
            RegisterModel(new TestToolSettingsModel());
            RegisterModel(new MobileControlLayoutModel());
            RegisterModel(new WeaponPoseSettingsModel());
            RegisterModel(new AppStateModel());
            RegisterModel(new RuntimeLogModel());
            RegisterModel(new SaveModel());
            RegisterModel(new ArcadeRunModel());
            RegisterModel(new ArcadeCombatModel());
            RegisterModel(new MapSceneModel());
            RegisterModel(new MapExplorationModel());
            RegisterModel(new UnitModel());
            RegisterModel(new PlayerModel());
            RegisterModel(new PlayerCameraModeModel());
            RegisterModel(new PlayerBasePoseModel());
            RegisterModel(new PlayerConditionModel());
            RegisterModel(new PlayerAttributeModel());
            RegisterModel(new CombatModel());
            RegisterModel(new LootModel());
            RegisterModel(new ExtractionModel());
            RegisterModel(new ExpeditionModel());
            RegisterModel(new BaseDayNightModel());
            RegisterModel(new ExpeditionTaskModel());
            RegisterModel(new PlayerBagModel());
            RegisterModel(new WarehouseModel());
            RegisterModel(new CurrencyModel());
            RegisterModel(new BasePreparationModel());
            RegisterModel(new ShopUnlockModel());
            RegisterModel(new ExpeditionPreparationModel());
            RegisterModel(new CivilisationTechnologyModel());
            RegisterModel(new CivilisationExpeditionModel());
            RegisterModel(new SearchContainerModel());
            RegisterModel(new WeaponEquipmentModel());
            RegisterModel(new ClothingEquipmentModel());
            RegisterModel(new ProjectileModel());
            RegisterModel(new TemporaryBuildingModel());
            RegisterModel(new TestCmdModel());
            RegisterModel(new BaseMapProgressModel());
            RegisterModel(new ResourceUpdateModel());
            RegisterModel(new OnlineAccountModel());
            RegisterModel(new OnlineRoomModel());
            RegisterModel(new OnlineRoomChatModel());
            RegisterModel(new OnlineArcadeRunModel());
            RegisterModel(new OnlineWorldModel());
        }

        private void RegisterUtilities()
        {
            RegisterUtility<ISettingsFileStore>(new SettingsFileStore());
            RegisterUtility<IWeaponPoseFileStore>(new WeaponPoseFileStore());
            RegisterUtility<IDisplayResolutionApplier>(new UnityDisplayResolutionApplier());
            RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            RegisterUtility(new HotKeyActionRegistry());
            RegisterUtility<IMapContentLoader>(new AdditiveSceneMapLoader());
            RegisterUtility<IValleyMapBuilder>(new ValleyMapBuilder());
            RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            RegisterUtility(new ViewCtrlRegistry());
            RegisterUtility(new MapImageRefreshCtrl());
            RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider());
            RegisterUtility<IResourceUpdateRuntimeChecker>(new YooAssetResourceUpdateRuntimeChecker());
            RegisterUtility<IRemoteResourceVersionProbe>(new UnityWebRequestResourceVersionProbe());
            RegisterUtility<IResourceUpdateNoticeClient>(new UnityWebRequestResourceUpdateNoticeClient());
            RegisterUtility<IApplicationUpdateChecker>(new ApplicationUpdateChecker());
            RegisterUtility<IWindowsApplicationUpdateChecker>(new WindowsApplicationUpdateChecker());
            RegisterUtility<IResourceServerConfigProvider>(new ResourceServerConfigProvider());
            RegisterUtility<IApplicationRestartUtility>(new ApplicationRestartUtility());
            RegisterUtility<ICursorLockPort>(new UnityCursorLockPort());
        }

        private void RegisterNetworkRuntime()
        {
            networkRuntime = new NetworkRuntime();
            networkRuntime.Initialize();
            RegisterUtility<INetworkRuntime>(networkRuntime);
            RegisterUtility<INetworkConfigProvider>(new StreamingAssetsNetworkConfigProvider());
            RegisterUtility<IOnlineAccountPreferenceStore>(new OnlineAccountPreferenceStore());
        }

        private void RegisterItemTransfer()
        {
            itemTransferCtrl = new ItemTransferCtrl(this);
            RegisterUtility<IItemTransferPort>(itemTransferCtrl);
        }

        private void CreateMapControllers()
        {
            playerCtrl = new PlayerCtrl(this);
            projectileCtrl = new ProjectileCtrl(this);
            temporaryBuildingCtrl = new TemporaryBuildingCtrl(this);
            enemyCtrl = new EnemyCtrl(this);
            lootCtrl = new LootCtrl(this);
            extractionCtrl = new ExtractionCtrl(this);
            npcCtrl = new NpcCtrl(this);
            onlineWorldPlayerCtrl = new OnlineWorldPlayerCtrl(this);
            onlineWorldEnemyCtrl=new OnlineWorldEnemyCtrl(this);
            onlineWorldCorpseCtrl=new OnlineWorldCorpseCtrl(this);
            arcadeCombatView = new ArcadeCombatViewController();
        }

        private void RegisterControllerPorts()
        {
            RegisterUtility<IPlayerRuntimePortProvider>(playerCtrl);
            RegisterUtility<IProjectileRuntimePortProvider>(projectileCtrl);
            RegisterUtility<IArcadeProjectilePresentationPort>(projectileCtrl);
            RegisterUtility<IOnlineWorldProjectilePresentationPort>(projectileCtrl);
            RegisterUtility<ITemporaryBuildingViewPort>(temporaryBuildingCtrl);
            RegisterUtility<IEnemyColliderResolver>(enemyCtrl);
            RegisterUtility<IArcadeEnemyPresentationPort>(enemyCtrl);
            RegisterUtility<IArcadeLootPresentationPort>(lootCtrl);
            RegisterUtility<ICraftOverflowDropPort>(lootCtrl);
            RegisterUtility<IPlayerInventoryDropPort>(lootCtrl);
            RegisterUtility<IBuildingRefundDropPort>(lootCtrl);
            RegisterUtility<ICivilisationHarvestDropPort>(lootCtrl);
            RegisterUtility<IArcadeCombatViewPort>(arcadeCombatView);
        }

        private void RegisterSystems()
        {
            RegisterSystem(new SettingsSystem());
            RegisterSystem(new WeaponPoseSettingsSystem());
            RegisterSystem(new SaveSystem());
            RegisterSystem(new ArcadeRunSystem());
            RegisterSystem(new ArcadeCombatSystem());
            RegisterSystem(new AppStateSystem());
            RegisterSystem(new UnitSystem());
            RegisterSystem(new MapDisplaySystem());
            RegisterSystem(new MapExplorationSystem());
            RegisterSystem(new PlayerBasePoseSystem());
            RegisterSystem(new PlayerAttributeSystem());
            RegisterSystem(new PlayerActionSystem());
            RegisterSystem(new MobileInputSystem());
            RegisterSystem(new PlayerInputSystem());
            RegisterSystem(new PlayerMoveSystem());
            RegisterSystem(new PlayerCameraSystem());
            RegisterSystem(new PlayerFlashlightSystem());
            RegisterSystem(new RuntimeLogSystem());
            RegisterSystem(new AudioSystem());
            RegisterSystem(new CombatSoundSystem());
            RegisterSystem(new HotKeySystem());
            RegisterSystem(new MapSceneSystem(projectileCtrl, playerCtrl, npcCtrl, enemyCtrl, lootCtrl, extractionCtrl, onlineWorldPlayerCtrl,onlineWorldEnemyCtrl,onlineWorldCorpseCtrl));
            RegisterSystem(new ArcadeSceneFlowSystem());
            RegisterUtility<IGameplayResourcePreparation>(new GameplayResourcePreparationCtrl(this));
            RegisterSystem(new ExpeditionTaskSystem());
            RegisterSystem(new ExpeditionSystem());
            RegisterSystem(new DayNightVisualSystem());
            RegisterSystem(new BaseWeatherSystem());
            RegisterSystem(new PlayerConditionSystem());
            RegisterSystem(new ExpeditionFlowSystem());
            RegisterSystem(new InventorySystem());
            RegisterSystem(new ShopSystem());
            RegisterSystem(new BasePreparationSystem());
            RegisterSystem(new CarryLoadSystem());
            RegisterSystem(new CivilisationProductionSystem());
            RegisterSystem(new CivilisationSearchSystem());
            RegisterSystem(new CivilisationExplorationSystem());
            RegisterSystem(new CraftSystem());
            RegisterSystem(new SearchContainerSystem());
            RegisterSystem(new WeaponEquipmentSystem());
            RegisterSystem(new ClothingEquipmentSystem());
            RegisterSystem(new ServerTestCmdSystem());
            RegisterSystem(new TestCmdSystem());
            RegisterSystem(new AppearanceGallerySceneFlowSystem());
            RegisterSystem(new BaseMapSelectionSystem());
            RegisterSystem(new PlayerInteractionSystem());
            RegisterSystem(new DialogueSystem());
            RegisterSystem(new WeaponSystem());
            RegisterSystem(new ProjectileSystem());
            RegisterSystem(new TemporaryBuildingSystem());
            RegisterSystem(new CombatSystem());
            RegisterSystem(new EnemySystem());
            RegisterSystem(new EnemyDropSystem());
            RegisterSystem(new LootSystem());
            RegisterSystem(new ExtractionSystem());
            RegisterSystem(new CombatFlowSystem());
            RegisterSystem(new GameSessionFlowSystem());
            RegisterSystem(new RedPointSystem());
            RegisterSystem(new ResourceUpdateSystem());
            RegisterSystem(new OptionalGameplayResourceSystem());
            onlineAccountSystem = new OnlineAccountSystem();
            RegisterSystem(onlineAccountSystem);
            RegisterSystem(new OnlineRoomSystem());
            RegisterSystem(new OnlineRoomChatSystem());
            RegisterSystem(new OnlineArcadeRunSystem());
            RegisterSystem(new OnlineArcadePresentationSystem());
            RegisterSystem(new OnlineWorldSystem());
            RegisterSystem(new OnlineWorldProjectilePresentationSystem());
            RegisterSystem(new OnlineWorldInventorySystem());
        }

        private void InitializeNetworkLifecycle()
        {
            networkLifecycle = new OnlineAccountLifecycleController();
            networkLifecycle.Initialize(networkRuntime, onlineAccountSystem);
        }

        protected override void OnDeinit()
        {
            GetUtility<MapImageRefreshCtrl>()?.Dispose();
            civilisationSceneCtrl?.Dispose();
            civilisationSceneCtrl = null;
            itemTransferCtrl?.Dispose();
            itemTransferCtrl = null;
            networkLifecycle?.Dispose();
            networkLifecycle = null;
            onlineAccountSystem = null;
            networkRuntime?.Dispose();
            networkRuntime = null;
            extractionCtrl?.ReleaseAll();
            extractionCtrl = null;
            lootCtrl?.ReleaseAll();
            lootCtrl = null;
            enemyCtrl?.ReleaseAll();
            enemyCtrl = null;
            npcCtrl?.ReleaseAll();
            npcCtrl = null;
            playerCtrl?.ReleaseAll();
            playerCtrl = null;
            projectileCtrl?.ReleaseAll();
            projectileCtrl = null;
            temporaryBuildingCtrl?.ReleaseAll();
            temporaryBuildingCtrl = null;
            onlineWorldPlayerCtrl?.Dispose();
            onlineWorldPlayerCtrl = null;
            onlineWorldCorpseCtrl?.Dispose();onlineWorldCorpseCtrl=null;
            onlineWorldEnemyCtrl?.Dispose();onlineWorldEnemyCtrl=null;
            arcadeCombatView?.Dispose();
            arcadeCombatView = null;
            GetUtility<ViewCtrlRegistry>()?.DisposeAll();
        }
    }
}
