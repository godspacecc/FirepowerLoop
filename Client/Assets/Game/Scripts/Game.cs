using QFramework;
using Game.Network;

namespace Game
{
    /// <summary>
    /// FirepowerLoop 街机架构入口类型 Game，负责连接启动顺序与业务容器初始化。
    /// </summary>
    public class Game : Architecture<Game>
    {
        private PlayerCtrl playerCtrl;
        private EnemyCtrl enemyCtrl;
        private LootCtrl lootCtrl;
        private ProjectileCtrl projectileCtrl;
        private INetworkRuntime networkRuntime;
        private OnlineAccountLifecycleController networkLifecycle;
        private OnlineAccountSystem onlineAccountSystem;
        private ArcadeCombatViewController arcadeCombatView;

        protected override void Init()
        {
            RegisterModels();
            RegisterUtilities();
            RegisterNetworkRuntime();
            CreateMapControllers();
            RegisterControllerPorts();
            RegisterSystems();
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
            RegisterModel(new ArcadeRunModel());
            RegisterModel(new ArcadeCombatModel());
            RegisterModel(new MapSceneModel());
            RegisterModel(new UnitModel());
            RegisterModel(new PlayerModel());
            RegisterModel(new PlayerCameraModeModel());
            RegisterModel(new TestCmdModel());
            RegisterModel(new ResourceUpdateModel());
            RegisterModel(new OnlineAccountModel());
            RegisterModel(new OnlineRoomModel());
            RegisterModel(new OnlineRoomChatModel());
            RegisterModel(new OnlineArcadeRunModel());
        }

        private void RegisterUtilities()
        {
            RegisterUtility<ISettingsFileStore>(new SettingsFileStore());
            RegisterUtility<IWeaponPoseFileStore>(new WeaponPoseFileStore());
            RegisterUtility<IDisplayResolutionApplier>(new UnityDisplayResolutionApplier());
            RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            RegisterUtility(new HotKeyActionRegistry());
            RegisterUtility<IMapContentLoader>(new AdditiveSceneMapLoader());
            RegisterUtility<IViewInstanceProvider>(new UnityViewInstanceProvider());
            RegisterUtility(new ViewCtrlRegistry());
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

        private void CreateMapControllers()
        {
            playerCtrl = new PlayerCtrl(this);
            projectileCtrl = new ProjectileCtrl(this);
            enemyCtrl = new EnemyCtrl(this);
            lootCtrl = new LootCtrl(this);
            arcadeCombatView = new ArcadeCombatViewController();
        }

        private void RegisterControllerPorts()
        {
            RegisterUtility<IPlayerRuntimePortProvider>(playerCtrl);
            RegisterUtility<IArcadeProjectilePresentationPort>(projectileCtrl);
            RegisterUtility<IArcadeEnemyPresentationPort>(enemyCtrl);
            RegisterUtility<IArcadeLootPresentationPort>(lootCtrl);
            RegisterUtility<IArcadeCombatViewPort>(arcadeCombatView);
        }

        private void RegisterSystems()
        {
            RegisterSystem(new SettingsSystem());
            RegisterSystem(new WeaponPoseSettingsSystem());
            RegisterSystem(new ArcadeRunSystem());
            RegisterSystem(new ArcadeCombatSystem());
            RegisterSystem(new AppStateSystem());
            RegisterSystem(new UnitSystem());
            RegisterSystem(new MobileInputSystem());
            RegisterSystem(new PlayerCameraSystem());
            RegisterSystem(new RuntimeLogSystem());
            RegisterSystem(new AudioSystem());
            RegisterSystem(new CombatSoundSystem());
            RegisterSystem(new HotKeySystem());
            RegisterSystem(new MapSceneSystem(projectileCtrl, playerCtrl, enemyCtrl, lootCtrl));
            RegisterSystem(new ArcadeSceneFlowSystem());
            RegisterSystem(new ServerTestCmdSystem());
            RegisterSystem(new TestCmdSystem());
            RegisterSystem(new RedPointSystem());
            RegisterSystem(new ResourceUpdateSystem());
            RegisterSystem(new OptionalGameplayResourceSystem());
            RegisterUtility<IGameplayResourcePreparation>(new GameplayResourcePreparationCtrl(this));
            onlineAccountSystem = new OnlineAccountSystem();
            RegisterSystem(onlineAccountSystem);
            RegisterSystem(new OnlineRoomSystem());
            RegisterSystem(new OnlineRoomChatSystem());
            RegisterSystem(new OnlineArcadeRunSystem());
            RegisterSystem(new OnlineArcadePresentationSystem());
        }

        private void InitializeNetworkLifecycle()
        {
            networkLifecycle = new OnlineAccountLifecycleController();
            networkLifecycle.Initialize(networkRuntime, onlineAccountSystem);
        }

        /// <summary>按表现与网络所有权释放当前街机资源；不保存或读取基地、远征数据。</summary>
        protected override void OnDeinit()
        {
            networkLifecycle?.Dispose();
            networkLifecycle = null;
            onlineAccountSystem = null;
            networkRuntime?.Dispose();
            networkRuntime = null;
            lootCtrl?.ReleaseAll(); lootCtrl = null;
            enemyCtrl?.ReleaseAll(); enemyCtrl = null;
            playerCtrl?.ReleaseAll(); playerCtrl = null;
            projectileCtrl?.ReleaseAll(); projectileCtrl = null;
            arcadeCombatView?.Dispose(); arcadeCombatView = null;
            GetUtility<ViewCtrlRegistry>()?.DisposeAll();
        }
    }
}
