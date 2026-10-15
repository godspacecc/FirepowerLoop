using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class NpcControllerTests
    {
        private TestResourceLoader loader;
        private CountingProvider provider;
        private FakePlayerRuntimePorts cameraPorts;
        private TestConfigProvider config;
        private FakeOptionalGameplayResourceSystem optionalResources;
        private FakeExpeditionServerProbe expeditionProbe;
        private NpcCtrl npcCtrl;
        private PlayerCtrl playerCtrl;
        private ProjectileCtrl projectileCtrl;
        private EnemyCtrl enemyCtrl;
        private LootCtrl lootCtrl;
        private ExtractionCtrl extractionCtrl;
        private string tempSaveRoot;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            UIHelper.ResetForTests();
            loader = new TestResourceLoader();
            provider = new CountingProvider();
            cameraPorts = new FakePlayerRuntimePorts();
            config = new TestConfigProvider();
            optionalResources = new FakeOptionalGameplayResourceSystem();
            expeditionProbe = new FakeExpeditionServerProbe();
            ResourceLoader.SetLoaderForTests(loader);
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            npcCtrl = new NpcCtrl(architecture);
            CreateArchitecture(architecture);
            tempSaveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayNpcControllerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSaveRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempSaveRoot);
            CreateUIRoot();
            var floatPrefab = CreateFloatPrefab();
            UIHelper.SetPrefabLoaderForTests(path =>
                path == "Prefabs/UI/Notify/Float" ? floatPrefab : null);
            InstallLocalization();
        }

        [TearDown]
        public void TearDown()
        {
            npcCtrl?.ReleaseAll();
            playerCtrl?.ReleaseAll();
            projectileCtrl?.ReleaseAll();
            enemyCtrl?.ReleaseAll();
            lootCtrl?.ReleaseAll();
            extractionCtrl?.ReleaseAll();
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            TestReset.ResetArchitecture();
            DestroySceneObjects();
            if (!string.IsNullOrEmpty(tempSaveRoot) && Directory.Exists(tempSaveRoot))
            {
                Directory.Delete(tempSaveRoot, true);
            }

            tempSaveRoot = null;
        }

        [Test]
        public void NpcViewCtrlRegistersSignalsAndReleasesOnce()
        {
            var registry = new ViewCtrlRegistry();
            var localProvider = new CountingProvider();
            var view = CreateView();
            var interactions = 0;
            var ctrl = new NpcViewCtrl(view, 1001, 7, registry, localProvider, _ => interactions++);

            Assert.That(registry.Count, Is.EqualTo(1));
            ctrl.SetPromptText("Press [E]");
            Assert.That(view.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("Press [E]"));
            view.Interact();
            Assert.That(interactions, Is.EqualTo(1));

            ctrl.Dispose();
            ctrl.Dispose();
            ctrl.FacePrompt(Vector3.back, Vector3.up);
            Assert.That(registry.Count, Is.EqualTo(0));
            Assert.That(localProvider.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void SharedPrefabPreparesHiddenCandidatesAndCommitRegistersIndependentNpcUnits()
        {
            config.UseFourBaseNpcs("npc/shared");
            loader.SetAsset("npc/shared", CreatePrefab());
            var context = CreateContext(MapIds.Base, 88, config.Spawns);

            var prepare = npcCtrl.PrepareMapAsync(context, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(prepare.Success, Is.True, prepare.Error);
            Assert.That(loader.LoadCount("npc/shared"), Is.EqualTo(1));
            Assert.That(provider.Instances, Has.Count.EqualTo(4));
            Assert.That(provider.Instances.Select(instance => instance.GetInstanceID()).Distinct().Count(), Is.EqualTo(4));
            Assert.That(provider.Instances.All(instance => !instance.gameObject.activeSelf), Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.Zero);
            Assert.That(CurrentFocus(), Is.Null);

            var commit = npcCtrl.CommitPreparedMap();

            Assert.That(commit.Success, Is.True, commit.Error);
            var units = Game.Interface.GetModel<UnitModel>().Units.Values
                .Where(unit => unit.UnitType == UnitType.Npc)
                .OrderBy(unit => unit.ConfigId)
                .ToList();
            Assert.That(units, Has.Count.EqualTo(4));
            foreach (var unit in units)
            {
                Assert.That(unit.UnitSource, Is.EqualTo(UnitSource.MapPlaced));
                Assert.That(unit.AuthorityMode, Is.EqualTo(UnitAuthorityMode.Local));
                Assert.That(unit.MapInstanceId, Is.EqualTo(88));
                Assert.That(unit.IsActive, Is.True);
                Assert.That(unit.ConfigId, Does.StartWith("npc."));
            }

            CollectionAssert.AreEquivalent(config.Spawns.Select(spawn => spawn.ConfigId), units.Select(unit => unit.ConfigId));
            Assert.That(provider.Instances.All(instance => instance.gameObject.activeSelf), Is.True);
        }

        [Test]
        public void CommitFailureRollsBackAlreadyCommittedNpcUnits()
        {
            config.UseFourBaseNpcs("npc/shared");
            loader.SetAsset("npc/shared", CreatePrefab());
            var context = CreateContext(MapIds.Base, 89, config.Spawns);

            Assert.That(npcCtrl.PrepareMapAsync(context, CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                1002,
                89,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                "occupied",
                Vector3.zero,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);

            var commit = npcCtrl.CommitPreparedMap();

            Assert.That(commit.Success, Is.False);
            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Values.Count(unit => unit.UnitType == UnitType.Npc), Is.Zero);
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.Zero);
            Assert.That(provider.ReleaseCount, Is.EqualTo(4));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(1002, out var occupied), Is.True);
            Assert.That(occupied.ConfigId, Is.EqualTo("occupied"));
        }

        [Test]
        public void PrepareReleaseCancellationAndLateCallbacksReleaseHandlesOnce()
        {
            config.UseSingleNpc("npc.prepare", "npc/slow");
            loader.HoldCallbacks = true;
            var preparing = npcCtrl.PrepareMapAsync(CreateContext(MapIds.Base, 90, config.Spawns), CancellationToken.None);

            npcCtrl.ReleaseAll();
            loader.Deliver("npc/slow");
            var released = preparing.GetAwaiter().GetResult();

            Assert.That(released.Success, Is.False);
            Assert.That(loader.ReleaseCount("npc/slow"), Is.EqualTo(1));
            Assert.That(provider.ReleaseCount, Is.Zero);

            ResetRuntime();
            config.UseSingleNpc("npc.prepare", "npc/cancel");
            loader.HoldCallbacks = true;
            using var cts = new CancellationTokenSource();
            var canceling = npcCtrl.PrepareMapAsync(CreateContext(MapIds.Base, 91, config.Spawns), cts.Token);
            cts.Cancel();
            loader.Deliver("npc/cancel");
            var canceled = canceling.GetAwaiter().GetResult();

            Assert.That(canceled.Success, Is.False);
            Assert.That(loader.ReleaseCount("npc/cancel"), Is.EqualTo(1));

            ResetRuntime();
            config.UseSingleNpc("npc.prepare", "npc/instant");
            loader.SetAsset("npc/instant", CreatePrefab());
            var prepared = npcCtrl.PrepareMapAsync(CreateContext(MapIds.Base, 92, config.Spawns), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(prepared.Success, Is.True, prepared.Error);
            npcCtrl.RollbackPreparedMap();
            npcCtrl.ReleaseAll();
            npcCtrl.ReleaseAll();
            Assert.That(loader.ReleaseCount("npc/instant"), Is.EqualTo(1));
            Assert.That(provider.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void ActiveLifecycleDeactivateRestoreReleaseCurrentAndReleaseAllAreIdempotent()
        {
            config.UseSingleNpc("npc.life", "npc/life");
            loader.SetAsset("npc/life", CreatePrefab());
            Assert.That(npcCtrl.PrepareMapAsync(CreateContext(MapIds.Base, 93, config.Spawns), CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(npcCtrl.CommitPreparedMap().Success, Is.True);
            var unit = Game.Interface.GetModel<UnitModel>().Units.Values.Single(item => item.UnitType == UnitType.Npc);
            var instance = provider.Instances.Single();

            npcCtrl.SetCurrentMapActive(false);
            Assert.That(instance.gameObject.activeSelf, Is.False);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unit.UnitId, out var inactive), Is.True);
            Assert.That(inactive.IsActive, Is.False);

            npcCtrl.SetCurrentMapActive(true);
            Assert.That(instance.gameObject.activeSelf, Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unit.UnitId, out var restored), Is.True);
            Assert.That(restored.IsActive, Is.True);

            npcCtrl.ReleaseCurrentMap();
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(unit.UnitId, out _), Is.False);
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.Zero);
            Assert.That(provider.ReleaseCount, Is.EqualTo(1));

            npcCtrl.ReleaseAll();
            npcCtrl.ReleaseAll();
            Assert.That(loader.ReleaseCount("npc/life"), Is.EqualTo(1));
            Assert.That(provider.ReleaseCount, Is.EqualTo(1));
        }

        [Test]
        public void VisibleNpcPromptFacesPlayerCameraPoseOnLateUpdate()
        {
            config.UseSingleNpc("npc.life", "npc/life");
            loader.SetAsset("npc/life", CreatePrefab());
            Assert.That(npcCtrl.PrepareMapAsync(CreateContext(MapIds.Base, 95, config.Spawns), CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(npcCtrl.CommitPreparedMap().Success, Is.True);
            var instance = provider.Instances.Single();
            var prompt = instance.transform.Find("prompt");
            Assert.That(prompt, Is.Not.Null);
            instance.GetComponent<NpcView>().SetFocusedForTests(true);
            RegisterPlayerForCamera(95);

            cameraPorts.CameraPosition = new Vector3(0f, 1f, -5f);
            cameraPorts.CameraUp = Vector3.up;
            LifeCycle.LateUpdate();

            var expected = Quaternion.LookRotation(prompt.position - cameraPorts.CameraPosition, cameraPorts.CameraUp);
            Assert.That(Quaternion.Angle(prompt.rotation, expected), Is.LessThan(0.01f));

            cameraPorts.CameraPosition = new Vector3(5f, 2f, 0f);
            LifeCycle.LateUpdate();

            expected = Quaternion.LookRotation(prompt.position - cameraPorts.CameraPosition, cameraPorts.CameraUp);
            Assert.That(Quaternion.Angle(prompt.rotation, expected), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator MapGuideInteractionOpensSelectionWithoutEntering()
        {
            var guide = PrepareMapGuideInteraction();
            guide.GetComponent<NpcView>().Interact();
            yield return null;
            var mapSelection = UIHelper.GetUICtrl<MapSelectionUICtrl>();
            Assert.That(mapSelection, Is.Not.Null);
            Assert.That(mapSelection.IsShow(), Is.True);
            Assert.That(loader.MapLoadRequests, Is.Empty);
            Assert.That(expeditionProbe.Calls, Is.Zero, "本地资源已就绪时无需联网");
        }

        [UnityTest]
        public IEnumerator MapGuideOpensSelectionWithoutDownloadingOtherMaps()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            var guide = PrepareMapGuideInteraction();
            guide.GetComponent<NpcView>().Interact();
            yield return null;
            Assert.That(UIHelper.GetUICtrl<MapSelectionUICtrl>()?.IsShow(), Is.True);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
            Assert.That(loader.MapLoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator MissingOptionalMapCannotBypassPreparationThroughMapSystem()
        {
            PrepareMapGuideInteraction();
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            var task = Game.Interface.GetSystem<MapSceneSystem>().LoadMapAsync(MapIds.MissionSample).AsTask();
            yield return null;
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.Success, Is.False);
            Assert.That(task.Result.Error, Does.Contain("资源"));
            Assert.That(loader.MapLoadRequests, Is.Empty);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SelectedMapDownloadCompletesWithoutCreatingWorld()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            var task = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.confirm"));
            yield return null;
            Assert.That(optionalResources.DownloadStarts, Is.EqualTo(1));
            Assert.That(optionalResources.LastMapId, Is.EqualTo(MapIds.MissionSample));
            optionalResources.PendingDownload.TrySetResult(new OptionalGameplayDownloadResult(true, string.Empty));
            yield return null;
            Assert.That(task.Result, Is.True);
            Assert.That(loader.MapLoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ReplacedDownloadConfirmationReleasesOccupancyWithoutHidingNewMessage()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            var task = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            var nextButtons = new System.Collections.Generic.List<MessageButtonConfig>
                { new MessageButtonConfig { ButtonText = "Next" } };
            Notify.ShowMessage("Next", "Next", nextButtons);
            yield return null;
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result, Is.False);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>().IsShowingButtons(nextButtons), Is.True);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SelectedMapCancelDoesNotStartDownload()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            var task = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.cancel"));
            yield return null;
            Assert.That(task.Result, Is.False);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
            Assert.That(loader.MapLoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator MissingDownloadProgressUiDoesNotStartHiddenDownload()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            var task = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            UIHelper.SetPrefabLoaderForTests(_ => null);
            LogAssert.Expect(LogType.Error, "[BaseUICtrl] UI Prefab 未找到：Prefabs/UI/Common/Loading");
            ClickMessageButton(Loc.T("ui.common.confirm"));
            yield return null;
            Assert.That(task.Result, Is.False);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator StaleMapPreparationDoesNotContinueAfterDownload()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            bool current = true;
            var task = preparation.PrepareAsync(MapIds.MissionSample, () => current).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.confirm"));
            yield return null;
            current = false;
            optionalResources.PendingDownload.TrySetResult(new OptionalGameplayDownloadResult(true, string.Empty));
            yield return null;
            Assert.That(task.Result, Is.False);
            Assert.That(loader.MapLoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DownloadProgressCannotHideBeforeTerminal()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            var task = preparation.PrepareAsync(MapIds.Arcade).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.confirm"));
            yield return null;
            UIHelper.HideUI<LoadingUICtrl>();
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>().IsShow(), Is.True);
            optionalResources.PendingDownload.TrySetResult(new OptionalGameplayDownloadResult(true, string.Empty));
            yield return null;
            Assert.That(task.Result, Is.True);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>().IsShow(), Is.False);
        }

        [UnityTest]
        public IEnumerator FailedMapDownloadCanRetryWithoutCreatingWorld()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            var task = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.confirm"));
            yield return null;
            optionalResources.PendingDownload.TrySetResult(new OptionalGameplayDownloadResult(false, "controlled failure"));
            yield return null;
            Assert.That(task.Result, Is.False);
            var retry = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.confirm"));
            yield return null;
            Assert.That(optionalResources.DownloadStarts, Is.EqualTo(2));
            optionalResources.PendingDownload.TrySetResult(new OptionalGameplayDownloadResult(true, string.Empty));
            yield return null;
            Assert.That(retry.Result, Is.True);
            Assert.That(loader.MapLoadRequests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator StaleConfirmationReleasesPreparationOccupancy()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            bool current = true;
            var task = preparation.PrepareAsync(MapIds.MissionSample, () => current).AsTask();
            yield return null;
            current = false;
            yield return null;
            Assert.That(task.Result, Is.False);
            UIHelper.HideUI<MessageUICtrl>();
            var retry = preparation.PrepareAsync(MapIds.Arcade).AsTask();
            yield return null;
            ClickMessageButton(Loc.T("ui.common.cancel"));
            yield return null;
            Assert.That(retry.Result, Is.False);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator UnreachableResourceServerDoesNotOpenDownloadConfirmationOrProgress()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            expeditionProbe.Next = RemoteResourceVersionProbeResult.Fail("offline");
            var task = preparation.PrepareAsync(MapIds.MissionSample).AsTask();
            yield return null;
            Assert.That(task.Result, Is.False);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PendingPreparationRejectsDuplicateAndStaleCompletion()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            PrepareMapGuideInteraction();
            var preparation = new GameplayResourcePreparationCtrl(Game.Interface);
            bool current = true;
            expeditionProbe.Pending = new UniTaskCompletionSource<RemoteResourceVersionProbeResult>();
            var first = preparation.PrepareAsync(MapIds.MissionSample, () => current).AsTask();
            var duplicate = preparation.PrepareAsync(MapIds.Arcade).AsTask();
            Assert.That(duplicate.Result, Is.False);
            current = false;
            expeditionProbe.Pending.TrySetResult(RemoteResourceVersionProbeResult.Ok("test-version"));
            yield return null;
            Assert.That(first.Result, Is.False);
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
        }

        private static void ClickMessageButton(string label)
        {
            var ctrl = UIHelper.GetUICtrl<MessageUICtrl>();
            var ui = (MessageUI)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ctrl);
            Assert.That(ui, Is.Not.Null);
            var button = ui.ButtonRoot.GetComponentsInChildren<MessageButtonBoxUI>(false)
                .FirstOrDefault(item => item.ButtonText.text == label);
            Assert.That(button, Is.Not.Null, label);
            button.BtnButton.onClick.Invoke();
        }

        [UnityTest]
        public IEnumerator RealConnectionRefusalReturnsFailureBeforeOpeningDownloadUi()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Game.Interface.RegisterUtility<IRemoteResourceVersionProbe>(new UnityWebRequestResourceVersionProbe());
            Game.Interface.GetSystem<ResourceUpdateSystem>().InitializeStartupInfo(new Boot.BootResourceStartupInfo(
                Boot.BootResourceSource.HostLatest, $"http://127.0.0.1:{port}", "test-version", "test-version"));
            var task = optionalResources.CheckExpeditionAsync().AsTask();
            float started = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - started < 10f) yield return null;
            Assert.That(task.IsCompleted, Is.True, "真实拒绝连接必须有界结束");
            Assert.That(task.Result.State, Is.EqualTo(OptionalGameplayResourceState.ConnectionFailed));
            Assert.That(optionalResources.DownloadStarts, Is.Zero);
            Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [UnityTest]
        public IEnumerator BaseFunctionNpcsOpenWithoutMissingExpeditionResources()
        {
            foreach (var npcId in new[] { "npc.warehouse", "npc.craft", "npc.shop" })
            {
                optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
                var npc = PrepareMapGuideInteraction(npcId);
                RegisterPlayerForCamera(94);
                npc.GetComponent<NpcView>().Interact();
                yield return null;
                Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False, npcId);
                Assert.That(UIHelper.GetUICtrl<LoadingUICtrl>()?.IsShow() ?? false, Is.False, npcId);
                var visible = npcId == "npc.warehouse" ? UIHelper.GetUICtrl<WarehouseUICtrl>()?.IsShow()
                    : npcId == "npc.craft" ? UIHelper.GetUICtrl<CraftUICtrl>()?.IsShow()
                    : UIHelper.GetUICtrl<ShopUICtrl>()?.IsShow();
                Assert.That(visible, Is.True, npcId);
                UIHelper.HideUI<WarehouseUICtrl>();
                UIHelper.HideUI<BagUICtrl>();
                UIHelper.HideUI<CraftUICtrl>();
                UIHelper.HideUI<ShopUICtrl>();
                npcCtrl.ReleaseCurrentMap();
                Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.MapInstanceReleased);
                provider.Instances.Clear();
            }
        }

        [Test]
        public void InteractingNpcFacesPlayerHorizontallyAndUpdatesUnitPose()
        {
            optionalResources.State = OptionalGameplayResourceState.NeedsDownload;
            var npc = PrepareMapGuideInteraction();
            RegisterPlayerForCamera(94);
            var units = Game.Interface.GetSystem<UnitSystem>();
            units.TryUpdateTransform(PlayerModel.LocalPlayerUnitId, npc.transform.position + new Vector3(3f, 7f, 0f), Quaternion.identity);
            npc.GetComponent<NpcView>().Interact();
            Assert.That(Vector3.Dot(npc.transform.forward, Vector3.right), Is.GreaterThan(0.999f));
            var snapshot = Game.Interface.GetModel<UnitModel>().Units.Values.Single(unit => unit.UnitType == UnitType.Npc && unit.Position == npc.transform.position);
            Assert.That(Quaternion.Angle(snapshot.Rotation, npc.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void MissingCraftUiReturnsFailure()
        {
            PrepareMapGuideInteraction();
            UIHelper.SetPrefabLoaderForTests(_ => null);
            using (Log.CaptureForTests((_, __) => { }))
                Assert.That(CraftUICtrl.TryShow(out var error), Is.False, error);
        }

        [Test]
        public void MissingShopUiReturnsFailureAndClosesSession()
        {
            PrepareMapGuideInteraction("npc.shop");
            var unit = Game.Interface.GetModel<UnitModel>().Units.Values.Single(value => value.ConfigId == "npc.shop");
            UIHelper.SetPrefabLoaderForTests(_ => null);
            using (Log.CaptureForTests((_, __) => { }))
                Assert.That(ShopUICtrl.TryShow("npc.shop", unit.UnitId, "shop.base.supplies", out var error), Is.False, error);
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);
        }

        private GameObject PrepareMapGuideInteraction(string selectedNpcId = "npc.map-guide")
        {
            config.UseRouteNpcs();
            loader.SetAsset("npc/shared", CreatePrefab());
            var context = CreateContext(MapIds.Base, 94, config.Spawns);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new MapHandle(MapIds.Base, context.Scene));
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var createdSave, out var saveError), Is.True, saveError);
            Assert.That(save.CommitBaseRuntime(createdSave, out saveError), Is.True, saveError);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 1, 1f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var floatPrefab = CreateFloatPrefab();
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path == "Prefabs/UI/Notify/Float") return floatPrefab;
                if (path == "Prefabs/UI/Notify/Message")
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Game/Prefabs/UI/Notify/Message.prefab");
                if (path == "Prefabs/UI/Common/Loading")
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Game/Prefabs/UI/Common/Loading.prefab");
                return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab");
            });

            Assert.That(npcCtrl.PrepareMapAsync(context, CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(npcCtrl.CommitPreparedMap().Success, Is.True);
            var spawnIds = config.Spawns.Select(spawn => spawn.ConfigId).ToArray();
            var byConfig = provider.Instances
                .Select((instance, index) => (instance, id: spawnIds[index]))
                .ToDictionary(pair => pair.id, pair => pair.instance);

            Assert.That(MapSelectionUICtrl.TryShowOffline(out var openError), Is.True, openError);
            UIHelper.HideUI<MapSelectionUICtrl>();
            return byConfig[selectedNpcId];
        }

        [Test]
        public void ShopNpcInteractionDispatchesThroughViewAndLifecycleClosesSession()
        {
            var view = PrepareShopInteraction("shop.base.supplies", true, 120);
            view.Interact();
            var shopUi = UIHelper.GetUICtrl<ShopUICtrl>();
            Assert.That(shopUi, Is.Not.Null);
            Assert.That(shopUi.IsShow(), Is.True);
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.EqualTo("shop.base.supplies"));

            npcCtrl.SetCurrentMapActive(false);
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(shopUi.IsShow(), Is.False);

            ResetRuntime();
            view = PrepareShopInteraction("shop.base.supplies", true, 121);
            view.Interact();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.EqualTo("shop.base.supplies"));
            npcCtrl.ReleaseCurrentMap();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(UIHelper.GetUICtrl<ShopUICtrl>().IsShow(), Is.False);

            ResetRuntime();
            var capturedErrors = new List<string>();
            using (Log.CaptureForTests((type, message) =>
                   {
                       if (type == LogType.Error) capturedErrors.Add(message);
                   }))
                PrepareShopInteraction(string.Empty, false, 122).Interact();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(UIHelper.GetUICtrl<ShopUICtrl>(), Is.Null);
            Assert.That(capturedErrors, Is.EqualTo(new[]
            {
                "[NpcCtrl] 商店界面打开失败：npc=npc.shop reason=TbShop[] 不存在"
            }));

            ResetRuntime();
            capturedErrors.Clear();
            using (Log.CaptureForTests((type, message) =>
                   {
                       if (type == LogType.Error) capturedErrors.Add(message);
                   }))
                PrepareShopInteraction("shop.missing", false, 123).Interact();
            Assert.That(Game.Interface.GetSystem<ShopSystem>().Snapshot.ShopId, Is.Null.Or.Empty);
            Assert.That(UIHelper.GetUICtrl<ShopUICtrl>(), Is.Null);
            Assert.That(capturedErrors, Is.EqualTo(new[]
            {
                "[NpcCtrl] 商店界面打开失败：npc=npc.shop reason=TbShop[shop.missing] 不存在"
            }));
        }

        [UnityTest]
        public IEnumerator BaseMapPortalSuccessfulEntryShowsHudAfterSourceObjectIsDestroyed()
        {
            PrepareExpeditionEntry(MapIds.SurvivalSample);

            var portalObject = new GameObject("BaseMapPortal", typeof(BoxCollider), typeof(BaseMapPortal));
            portalObject.GetComponent<BoxCollider>().isTrigger = true;
            var portal = portalObject.GetComponent<BaseMapPortal>();
            var portalSerialized = new UnityEditor.SerializedObject(portal);
            portalSerialized.FindProperty("targetMapId").stringValue = MapIds.SurvivalSample;
            portalSerialized.ApplyModifiedPropertiesWithoutUndo();

            var playerObject = new GameObject("PortalPlayer", typeof(CharacterController), typeof(PlayerView));
            var onTriggerEnter = typeof(BaseMapPortal).GetMethod(
                "OnTriggerEnter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(onTriggerEnter, Is.Not.Null);
            onTriggerEnter.Invoke(portal, new object[] { playerObject.GetComponent<CharacterController>() });
            yield return null;
            Assert.That(loader.MapLoadRequests, Is.EqualTo(new[] { MapIds.SurvivalSample }));

            Object.DestroyImmediate(portalObject);
            CompleteSuccessfulMapLoad(MapIds.SurvivalSample);
            yield return WaitForExpeditionActivation();

            Assert.That(Game.Interface.GetModel<ExpeditionModel>().IsActive, Is.True,
                $"mapStatus={Game.Interface.GetModel<MapSceneModel>().Status.Value} " +
                $"currentMap={Game.Interface.GetModel<MapSceneModel>().CurrentMapId} " +
                $"bagRisk={Game.Interface.GetModel<PlayerBagModel>().RiskState}");
            AssertExpeditionHudIsShownAndReturnIsDisabled();
        }

        private IEnumerator AssertMapSelectionSuccessShowsExpeditionHud(string npcId, string mapId)
        {
            config.UseMapSelectionNpc(npcId, mapId);
            loader.SetAsset("npc/" + npcId.Substring("npc.map.".Length), CreatePrefab());
            PrepareExpeditionEntry(mapId);
            Assert.That(npcCtrl.PrepareMapAsync(CreateContext(MapIds.Base, 97, config.Spawns), CancellationToken.None)
                .GetAwaiter().GetResult().Success, Is.True);
            Assert.That(npcCtrl.CommitPreparedMap().Success, Is.True);

            provider.Instances.Single().GetComponent<NpcView>().Interact();
            yield return null;
            Assert.That(loader.MapLoadRequests, Is.EqualTo(new[] { mapId }));

            string mapFailure = null;
            var failureUnregister = Game.Interface.RegisterEvent<MapLoadingFailedEvent>(evt => mapFailure = evt.Error);
            CompleteSuccessfulMapLoad(mapId);
            yield return WaitForExpeditionActivation();
            failureUnregister.UnRegister();

            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Values.Any(unit => unit.UnitType == UnitType.Npc), Is.False);
            Assert.That(provider.ReleaseCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().IsActive, Is.True,
                $"mapStatus={Game.Interface.GetModel<MapSceneModel>().Status.Value} " +
                $"currentMap={Game.Interface.GetModel<MapSceneModel>().CurrentMapId} " +
                $"bagRisk={Game.Interface.GetModel<PlayerBagModel>().RiskState} mapFailure={mapFailure}");
            AssertExpeditionHudIsShownAndReturnIsDisabled();
        }

        private static IEnumerator WaitForExpeditionActivation()
        {
            for (var frame = 0; frame < 10 && !Game.Interface.GetModel<ExpeditionModel>().IsActive; frame++)
            {
                yield return null;
            }
        }

        private void PrepareExpeditionEntry(string mapId)
        {
            loader.SetAsset("Prefabs/Characters/GrayboxPlayer", UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            loader.SetAsset("Prefabs/Combat/GrayboxArrow", UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"));
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path == "Prefabs/UI/Notify/Float") return CreateFloatPrefab();
                return path == "Prefabs/UI/Expedition/ExpeditionHud"
                    ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab")
                    : null;
            });
            CreateMainCamera();
            var baseContext = CreateContext(MapIds.Base, 96, Array.Empty<MapSpawnDefinition>());
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new MapHandle(MapIds.Base, baseContext.Scene));
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var saveError), Is.True, saveError);
            Assert.That(save.CommitBaseRuntime(created, out saveError), Is.True, saveError);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", "Prefabs/Characters/GrayboxPlayer", 100, 1, 1f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Game.Interface.GetSystem<BaseMapSelectionSystem>().UnlockMapForTests(mapId);
        }

        private NpcView PrepareShopInteraction(string shopId, bool defineShop, long mapInstanceId)
        {
            config.UseShopNpc("npc.shop", shopId, defineShop);
            loader.SetAsset("npc/shared", CreatePrefab());
            var floatPrefab = CreateFloatPrefab();
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path == "Prefabs/UI/Notify/Float") return floatPrefab;
                return path == "Prefabs/UI/Shop/Shop"
                    ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Shop/Shop.prefab")
                    : null;
            });
            var context = CreateContext(MapIds.Base, mapInstanceId, config.Spawns);
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new MapHandle(MapIds.Base, context.Scene));
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var saveError), Is.True, saveError);
            Assert.That(save.CommitBaseRuntime(created, out saveError), Is.True, saveError);
            Assert.That(npcCtrl.PrepareMapAsync(context, CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(npcCtrl.CommitPreparedMap().Success, Is.True);
            return provider.Instances.Single().GetComponent<NpcView>();
        }

        private void CompleteSuccessfulMapLoad(string mapId)
        {
            loader.CompleteMapLoad(MapContentLoadResult.Ok(new MapHandle(
                mapId,
                CreateContext(mapId, 98, Array.Empty<MapSpawnDefinition>()).Scene)));
        }

        private static void AssertExpeditionHudIsShownAndReturnIsDisabled()
        {
            var ctrl = UIHelper.GetUICtrl<ExpeditionHudUICtrl>();
            Assert.That(ctrl, Is.Not.Null);
            Assert.That(ctrl.IsShow(), Is.True);
            var ui = (ExpeditionHudUI)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
            Assert.That(ui, Is.Not.Null);
            var returnButton = Game.Interface.GetSystem<MobileInputSystem>().IsMobileInputMode
                ? ui.MobileBtnReturn : ui.BtnReturn;
            Assert.That(returnButton.interactable, Is.False);
        }

        private static void CreateMainCamera()
        {
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
        }

        private void ResetRuntime()
        {
            TearDown();
            SetUp();
        }

        private void PrepareAndCommitMapSelectionNpc(string npcId)
        {
            var context = CreateContext(MapIds.Base, 96, config.Spawns);
            Assert.That(npcCtrl.PrepareMapAsync(context, CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(npcCtrl.CommitPreparedMap().Success, Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Values.Single(unit => unit.UnitType == UnitType.Npc).ConfigId,
                Is.EqualTo(npcId));
        }

        private static void AssertLatestMessage(string expectedTitle, string expectedContent)
        {
            var ctrl = UIHelper.GetUICtrl<MessageUICtrl>();
            Assert.That(ctrl, Is.Not.Null);
            var config = (MessageConfig)typeof(MessageUICtrl)
                .GetField("currentConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Title, Is.EqualTo(expectedTitle));
            Assert.That(config.Content, Is.EqualTo(expectedContent));
        }

        private void CreateArchitecture(Game architecture)
        {
            architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new ResourceUpdateModel());
            architecture.RegisterUtility<IRemoteResourceVersionProbe>(expeditionProbe);
            var resourceUpdate = new ResourceUpdateSystem();
            architecture.RegisterSystem(resourceUpdate);
            resourceUpdate.InitializeStartupInfo(new Boot.BootResourceStartupInfo(
                Boot.BootResourceSource.HostLatest, "http://127.0.0.1:8000", "test-version", "test-version"));
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new PlayerModel());
            architecture.RegisterModel(new PlayerBasePoseModel());
            architecture.RegisterModel(new PlayerCameraModeModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new CurrencyModel());
            architecture.RegisterModel(new SearchContainerModel());
            architecture.RegisterModel(new ExpeditionModel());
            architecture.RegisterModel(new ExpeditionTaskModel());
            architecture.RegisterModel(new CombatModel());
            architecture.RegisterModel(new ProjectileModel());
            architecture.RegisterModel(new LootModel());
            architecture.RegisterModel(new ExtractionModel());
            architecture.RegisterModel(new TemporaryBuildingModel());
            architecture.RegisterModel(new BaseMapProgressModel());
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new WeaponEquipmentModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(config);
            architecture.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            architecture.RegisterUtility(new ViewCtrlRegistry());
            architecture.RegisterUtility<IViewInstanceProvider>(provider);
            architecture.RegisterUtility<IMapContentLoader>(loader);
            architecture.RegisterUtility<IPlayerRuntimePortProvider>(cameraPorts);
            architecture.RegisterUtility<IItemTransferPort>(new ItemTransferCtrl(architecture));
            playerCtrl = new PlayerCtrl(architecture);
            projectileCtrl = new ProjectileCtrl(architecture);
            enemyCtrl = new EnemyCtrl(architecture);
            lootCtrl = new LootCtrl(architecture);
            extractionCtrl = new ExtractionCtrl(architecture);
            var saveSystem = new SaveSystem();
            architecture.RegisterSystem(saveSystem);
            ((ICanInit)saveSystem).Init();
            saveSystem.Initialized = true;
            architecture.RegisterSystem(new AppStateSystem());
            // 真实远征 HUD 根据输入模式选择按钮，测试架构也需提供该只读表现依赖。
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new UnitSystem());
            var playerCameraSystem = new PlayerCameraSystem();
            architecture.RegisterSystem(playerCameraSystem);
            ((ICanInit)playerCameraSystem).Init();
            playerCameraSystem.Initialized = true;
            architecture.RegisterSystem(new PlayerActionSystem());
            architecture.RegisterSystem(new InventorySystem());
            // 存档恢复会重新校验装备来源，夹具必须提供同正式架构一致的装备系统。
            architecture.RegisterSystem(new WeaponEquipmentSystem());
            var shopSystem = new ShopSystem();
            architecture.RegisterSystem(shopSystem);
            ((ICanInit)shopSystem).Init();
            shopSystem.Initialized = true;
            architecture.RegisterSystem(new CraftSystem());
            architecture.RegisterSystem(new SearchContainerSystem());
            architecture.RegisterSystem(new ExpeditionSystem());
            var expeditionTaskSystem = new ExpeditionTaskSystem();
            architecture.RegisterSystem(expeditionTaskSystem);
            ((ICanInit)expeditionTaskSystem).Init();
            expeditionTaskSystem.Initialized = true;
            architecture.RegisterSystem(new ExpeditionFlowSystem());
            architecture.RegisterSystem(new AudioSystem());
            architecture.RegisterSystem(new ProjectileSystem());
            var mapSceneSystem = new MapSceneSystem(
                projectileCtrl,
                playerCtrl,
                npcCtrl,
                enemyCtrl,
                lootCtrl,
                extractionCtrl);
            architecture.RegisterSystem(mapSceneSystem);
            ((ICanInit)mapSceneSystem).Init();
            mapSceneSystem.Initialized = true;
            architecture.RegisterSystem(new BaseMapSelectionSystem());
            architecture.RegisterSystem<OptionalGameplayResourceSystem>(optionalResources);
            architecture.RegisterSystem(new PlayerInteractionSystem());
        }

        private static void RegisterPlayerForCamera(long mapInstanceId)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                mapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                "player",
                Vector3.zero,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, mapInstanceId));
        }

        private static void InstallLocalization()
        {
            var utility = Game.Interface.GetUtility<LocalizationUtility>();
            utility.LoadFromJson("zh-CN",
                "{\"npc.prepare\":\"准备员\",\"npc.life\":\"生命周期\",\"npc.map.survival\":\"生存远征\",\"npc.map.task\":\"任务远征\",\"npc.warehouse\":\"仓库管理员\",\"npc.craft\":\"工匠\",\"ui.interaction.prompt\":\"按 E 交互\",\"ui.inventory.operation_failed\":\"仓库打开失败\",\"ui.craft.failed\":\"制作失败\",\"ui.map.interaction.title\":\"地图交互\",\"ui.map.interaction.unavailable\":\"当前地图不可用\",\"ui.map.interaction.unlock_cost_insufficient\":\"解锁材料不足\",\"ui.map.interaction.enter_failed\":\"地图进入失败\",\"ui.map.interaction.failed\":\"地图交互失败\",\"ui.map.interaction.unlocked\":\"地图已解锁\",\"ui.map.enter_failed.title\":\"地图进入失败\",\"ui.map.unlock_failed.title\":\"地图解锁失败\",\"ui.map.unlock_success.title\":\"地图已解锁\",\"ui.map.unlock_success.content\":\"地图已解锁\",\"ui.map.enter_failed.content\":\"地图进入失败：{0}\"}");
        }

        private static object CurrentFocus()
        {
            return typeof(PlayerInteractionSystem)
                .GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<PlayerInteractionSystem>());
        }

        private static MapPreparationContext CreateContext(string mapId, long mapInstanceId, IReadOnlyList<MapSpawnDefinition> spawns)
        {
            var root = new GameObject($"Map-{mapInstanceId}");
            var presentation = new GameObject("Presentation");
            presentation.transform.SetParent(root.transform, false);
            var unitRoot = new GameObject("DynamicObjectRoot");
            unitRoot.transform.SetParent(presentation.transform, false);
            var context = root.AddComponent<MapContext>();
            var so = new UnityEditor.SerializedObject(context);
            so.FindProperty("mapContentRoot").objectReferenceValue = presentation;
            so.FindProperty("dynamicObjectRoot").objectReferenceValue = unitRoot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            return new MapPreparationContext(mapId, mapInstanceId, context, spawns);
        }

        private static NpcView CreateView()
        {
            var root = new GameObject("npc", typeof(BoxCollider), typeof(NpcView));
            root.SetActive(false);
            root.GetComponent<BoxCollider>().isTrigger = true;
            var prompt = new GameObject("prompt", typeof(RectTransform));
            prompt.transform.SetParent(root.transform, false);
            var text = new GameObject("text", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(prompt.transform, false);
            var view = root.GetComponent<NpcView>();
            view.ConfigureForTests(prompt, "npc");
            var so = new UnityEditor.SerializedObject(view);
            so.FindProperty("promptRoot").objectReferenceValue = prompt;
            so.FindProperty("promptText").objectReferenceValue = text.GetComponent<TMP_Text>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        private static GameObject CreatePrefab()
        {
            return CreateView().gameObject;
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = new GameObject("Float", typeof(RectTransform), typeof(FloatUI));
            var ui = root.GetComponent<FloatUI>();
            var tipsRoot = new GameObject("TipsRoot", typeof(RectTransform));
            tipsRoot.transform.SetParent(root.transform, false);
            ui.TipsRoot = tipsRoot.GetComponent<RectTransform>();
            var tip = new GameObject("TipTemplate", typeof(RectTransform), typeof(FloatTextBoxUI));
            tip.transform.SetParent(ui.TipsRoot, false);
            var text = new GameObject("TextTips", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(tip.transform, false);
            tip.SetActive(false);
            ui.TipTemplate = tip.GetComponent<FloatTextBoxUI>();
            ui.TipTemplate.TextTips = text.GetComponent<TextMeshProUGUI>();
            return root;
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject(
                "UIRoot",
                typeof(RectTransform),
                typeof(UnityEngine.Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster),
                typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }

            var inputModuleType = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType, Is.Not.Null);
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            eventSystem.AddComponent(inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
            new GameObject("PoolRoot");
        }

        private static void DestroySceneObjects()
        {
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
            {
                if (obj.scene.IsValid() && obj.scene.isLoaded && string.IsNullOrEmpty(obj.scene.path))
                {
                    Object.DestroyImmediate(obj);
                }
            }
        }

        private sealed class FakeOptionalGameplayResourceSystem : OptionalGameplayResourceSystem
        {
            public OptionalGameplayResourceState State = OptionalGameplayResourceState.Ready;
            public int DownloadStarts;
            public UniTaskCompletionSource<OptionalGameplayDownloadResult> PendingDownload;

            public string LastMapId;
            public override OptionalGameplayResourceQuery QueryMap(string mapId)
            {
                LastMapId = mapId;
                return QueryExpedition();
            }
            public override UniTask<OptionalGameplayDownloadResult> DownloadMapAsync(string mapId, string version,
                Action<OptionalGameplayDownloadProgress> progress, CancellationToken cancellationToken = default)
            {
                LastMapId = mapId;
                return DownloadExpeditionAsync(version, progress);
            }
            public override OptionalGameplayResourceQuery QueryExpedition() =>
                new OptionalGameplayResourceQuery(State, "test-version", State == OptionalGameplayResourceState.NeedsDownload ? 2 : 0,
                    State == OptionalGameplayResourceState.NeedsDownload ? 1024L : 0L, string.Empty);

            public override UniTask<OptionalGameplayDownloadResult> DownloadExpeditionAsync(
                string expectedVersion, Action<OptionalGameplayDownloadProgress> onProgress)
            {
                DownloadStarts++;
                PendingDownload = new UniTaskCompletionSource<OptionalGameplayDownloadResult>();
                return PendingDownload.Task;
            }
        }

        private sealed class FakeExpeditionServerProbe : IRemoteResourceVersionProbe
        {
            public int Calls;
            public RemoteResourceVersionProbeResult Next = RemoteResourceVersionProbeResult.Ok("test-version");
            public UniTaskCompletionSource<RemoteResourceVersionProbeResult> Pending;
            public UniTask<RemoteResourceVersionProbeResult> ProbeAsync(string server, int timeoutSeconds)
            {
                Calls++;
                Assert.That(timeoutSeconds, Is.GreaterThan(0).And.LessThanOrEqualTo(5));
                return Pending == null ? UniTask.FromResult(Next) : Pending.Task;
            }
        }

        private sealed class CountingProvider : IViewInstanceProvider
        {
            public readonly List<GameObject> Instances = new List<GameObject>();
            public int ReleaseCount { get; private set; }

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = Object.Instantiate(prefab, position, rotation, parent);
                Instances.Add(instance.gameObject);
                return instance;
            }

            public void Release(GameObject instance)
            {
                ReleaseCount++;
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private sealed class TestResourceLoader : IResourceLoader, IMapContentLoader
        {
            private readonly Dictionary<string, GameObject> assets = new Dictionary<string, GameObject>();
            private readonly Dictionary<string, int> loadCounts = new Dictionary<string, int>();
            private readonly Dictionary<string, int> releaseCounts = new Dictionary<string, int>();
            private readonly Dictionary<string, Action<GameObject>> pendingAssetCallbacks = new Dictionary<string, Action<GameObject>>();
            private UniTaskCompletionSource<MapContentLoadResult> pendingMapLoad;

            public bool HoldCallbacks { get; set; }
            public List<string> MapLoadRequests { get; } = new List<string>();

            public void SetAsset(string path, GameObject asset)
            {
                assets[path] = asset;
            }

            public int LoadCount(string path) => loadCounts.TryGetValue(path, out var count) ? count : 0;
            public int ReleaseCount(string path) => releaseCounts.TryGetValue(path, out var count) ? count : 0;

            public void Deliver(string path)
            {
                if (pendingAssetCallbacks.TryGetValue(path, out var callback))
                {
                    pendingAssetCallbacks.Remove(path);
                    callback(assets.TryGetValue(path, out var asset) ? asset : null);
                }
            }

            public T LoadAsset<T>(string path) where T : Object
            {
                return assets.TryGetValue(path, out var asset) ? asset as T : null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                loadCounts[path] = LoadCount(path) + 1;
                var handle = new ResourceLoadHandle(path);
                if (HoldCallbacks)
                {
                    pendingAssetCallbacks[path] = asset => onLoaded(asset as T);
                    return handle;
                }

                onLoaded(LoadAsset<T>(path));
                return handle;
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                var key = handle.RawHandle as string ?? handle.RawHandle.ToString();
                releaseCounts[key] = ReleaseCount(key) + 1;
            }

            public UniTask<MapContentLoadResult> LoadAsync(
                string mapId,
                string address,
                Action<float> onProgress,
                CancellationToken cancellationToken = default)
            {
                MapLoadRequests.Add(mapId);
                pendingMapLoad = new UniTaskCompletionSource<MapContentLoadResult>();
                return pendingMapLoad.Task;
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle, CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }

            public void CompleteMapLoad(MapContentLoadResult result)
            {
                pendingMapLoad?.TrySetResult(result);
                pendingMapLoad = null;
            }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            private readonly Dictionary<string, NpcConfigDefinition> npcs = new Dictionary<string, NpcConfigDefinition>();
            private readonly Dictionary<string, ShopDefinition> shops = new Dictionary<string, ShopDefinition>();

            public IReadOnlyList<MapSpawnDefinition> Spawns { get; private set; } = Array.Empty<MapSpawnDefinition>();

            public void UseSingleNpc(string id, string address)
            {
                npcs.Clear();
                npcs[id] = new NpcConfigDefinition(id, id, address, NpcInteractionKind.Warehouse, string.Empty);
                Spawns = new[] { Spawn("spawn." + id, MapIds.Base, id, 0f) };
            }

            public void UseFourBaseNpcs(string address)
            {
                npcs.Clear();
                var ids = new[] { "npc.map.survival", "npc.map.task", "npc.warehouse", "npc.craft" };
                foreach (var id in ids)
                {
                    npcs[id] = new NpcConfigDefinition(id, id, address, NpcInteractionKind.Warehouse, string.Empty);
                }

                Spawns = ids.Select((id, index) => Spawn("spawn." + id, MapIds.Base, id, index)).ToArray();
            }

            public void UseRouteNpcs()
            {
                npcs.Clear();
                npcs["npc.map-guide"] = new NpcConfigDefinition("npc.map-guide", "npc.map-guide", "npc/shared", NpcInteractionKind.MapSelection, string.Empty);
                npcs["npc.warehouse"] = new NpcConfigDefinition("npc.warehouse", "npc.warehouse", "npc/shared", NpcInteractionKind.Warehouse, string.Empty);
                npcs["npc.craft"] = new NpcConfigDefinition("npc.craft", "npc.craft", "npc/shared", NpcInteractionKind.Craft, string.Empty);
                npcs["npc.shop"] = new NpcConfigDefinition("npc.shop", "npc.shop", "npc/shared", NpcInteractionKind.Shop, "shop.base.supplies");
                shops["shop.base.supplies"] = new ShopDefinition("shop.base.supplies", "shop.base.supplies.name");
                Spawns = npcs.Keys.Select((id, index) => Spawn("spawn." + id, MapIds.Base, id, index)).ToArray();
            }

            public void UseMapSelectionNpc(string id, string mapId)
            {
                npcs.Clear();
                npcs[id] = new NpcConfigDefinition(id, id, "npc/" + id.Substring("npc.map.".Length), NpcInteractionKind.MapSelection, mapId);
                Spawns = new[] { Spawn("spawn." + id, MapIds.Base, id, 0f) };
            }

            public void UseShopNpc(string id, string shopId, bool defineShop)
            {
                npcs.Clear();
                shops.Clear();
                npcs[id] = new NpcConfigDefinition(id, id, "npc/shared", NpcInteractionKind.Shop, shopId);
                if (defineShop && !string.IsNullOrWhiteSpace(shopId))
                    shops[shopId] = new ShopDefinition(shopId, "shop.base.supplies.name");
                Spawns = new[] { Spawn("spawn." + id, MapIds.Base, id, 0f) };
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, "map/" + id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = mapId == MapIds.SurvivalSample || mapId == MapIds.MissionSample
                    ? new[] { new MapSpawnDefinition("player." + mapId, mapId, MapSpawnObjectType.Player, "player", 0f, 0f, 0f, 0f) }
                    : Spawns;
                error = null;
                return true;
            }

            public bool TryGetExpeditionTasks(string mapId, out IReadOnlyList<ExpeditionTaskDefinition> values, out string error)
            {
                values = mapId == MapIds.SurvivalSample
                    ? new[]
                    {
                        new ExpeditionTaskDefinition("expedition.survival-sample.survive", mapId, "Survive",
                            "task.expedition.survival_sample.survive.name", TaskObjectiveType.Survive,
                            PlayerConditionRules.GameMinutesPerDay, 100)
                    }
                    : mapId == MapIds.MissionSample
                        ? new[]
                        {
                            new ExpeditionTaskDefinition("expedition.mission-sample.kill", mapId, "Kill",
                                "task.expedition.mission_sample.kill.name", TaskObjectiveType.Kill, 2f, 100)
                        }
                        : Array.Empty<ExpeditionTaskDefinition>();
                error = null;
                return true;
            }

            public bool TryGetNpc(string id, out NpcConfigDefinition value, out string error)
            {
                if (npcs.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                error = "missing npc";
                return false;
            }

            public bool TryGetShop(string shopId, out ShopDefinition definition, out string error)
            {
                if (shops.TryGetValue(shopId ?? string.Empty, out definition))
                {
                    error = null;
                    return true;
                }
                error = $"TbShop[{shopId}] 不存在";
                return false;
            }

            public bool TryGetShopItems(string shopId, out IReadOnlyList<ShopItemDefinition> definitions, out string error)
            {
                if (!shops.ContainsKey(shopId ?? string.Empty))
                {
                    definitions = Array.Empty<ShopItemDefinition>();
                    error = $"TbShop[{shopId}] 不存在";
                    return false;
                }
                definitions = new[]
                {
                    new ShopItemDefinition("shop.test.arrow", shopId, ItemIds.Arrow, 1, 2, 1)
                };
                error = null;
                return true;
            }

            public bool TryGetShopItem(string shopId, int itemId, out ShopItemDefinition definition, out string error)
            {
                if (TryGetShopItems(shopId, out var definitions, out error) && itemId == ItemIds.Arrow)
                {
                    definition = definitions[0];
                    return true;
                }
                definition = default;
                error ??= $"TbShopItem[{shopId}/{itemId}] 不存在";
                return false;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition(id, "Prefabs/Characters/GrayboxPlayer", 100, 1, 1f, 1f, 1f, 1f, 1f);
                error = null;
                return true;
            }

            public bool TryGetProjectiles(out IReadOnlyList<ProjectileConfigDefinition> values, out string error)
            {
                values = new[]
                {
                    new ProjectileConfigDefinition("projectile.arrow.basic", "Prefabs/Combat/GrayboxArrow", 14f, 14f)
                };
                error = null;
                return true;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing extraction";
                return false;
            }

            private static MapSpawnDefinition Spawn(string id, string mapId, string configId, float x)
            {
                return new MapSpawnDefinition(id, mapId, MapSpawnObjectType.Npc, configId, x, 0f, 0f, 0f);
            }
        }

        private sealed class MapHandle : IMapContentHandle
        {
            public MapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => true;
        }

        private sealed class FakePlayerRuntimePorts : IPlayerRuntimePortProvider, IPlayerCameraPort, ICharacterMotorPort
        {
            public Vector3 CameraPosition { get; set; }
            public Vector3 CameraUp { get; set; } = Vector3.up;
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId { get; private set; }
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                MapInstanceId = mapInstanceId;
                port = this;
                return true;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                MapInstanceId = mapInstanceId;
                port = this;
                return true;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = new PlayerMapLocalPoseSnapshot(UnitId, MapInstanceId, Vector3.zero, 0f, true);
                error = null;
                return MapInstanceId > 0;
            }
            public void ApplyProfile() { }
            public void LateUpdate() { }

            public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
            {
                position = CameraPosition;
                up = CameraUp;
                return true;
            }

            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public void SetWeaponPresentation(WeaponSlot activeSlot, bool showShortSword, bool showBow) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Vector3.zero;
                return false;
            }

            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayBowFireFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }
    }
}
