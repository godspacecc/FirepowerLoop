using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class OnlineWorldInventoryTests
    {
        private const string MapId = "map.another-day.survival-sample";
        [Test]
        public void InventoryAndCorpseSnapshots_CopyCallerOwnedCollections()
        {
            var slots=new[]{new OnlineItemSlotSnapshot(0,5,1)};
            var inventory=new OnlineInventorySnapshot(12,slots,new OnlineEquipmentSnapshot(5,0,1,0),7);
            var corpses=new[]{new OnlineWorldCorpseSnapshot(9,1,10,0,"enemy",1,"map",Vector3.one,45,1000,true,3)};
            var snapshot=new OnlineWorldSnapshot(true,1,"map",20,4,Array.Empty<OnlineWorldPlayerSnapshot>(),Array.Empty<OnlineWorldEnemySnapshot>(),0,0,0,false,OnlineWorldOperationState.Idle,OnlineWorldResultCode.Success,inventory,corpses);
            slots[0]=default;corpses[0]=default;
            Assert.That(snapshot.SelfInventory.Slots[0].ItemId,Is.EqualTo(5));
            Assert.That(snapshot.Corpses[0].CorpseId,Is.EqualTo(9));
            Assert.That(snapshot.SelfInventory.Revision,Is.EqualTo(7));
        }

        [Test]
        public void InventorySlotRef_PreservesLongRuntimeContainerIdentity()
        {
            long id=(long)int.MaxValue+123;
            var slot=new InventorySlotRef(InventoryContainerKind.Search,4,id);
            Assert.That(slot.RuntimeContainerId,Is.EqualTo(id));
            Assert.That(slot.ContainerKind,Is.EqualTo(InventoryContainerKind.Search));
        }

        [Test]
        public void OnlineCorpseView_CreatesDedicatedSearchTriggerAndReleasesOwnership()
        {
            TestReset.ResetArchitecture();var root=new GameObject("OnlineCorpseViewTest");
            try
            {
                var view=root.AddComponent<OnlineCorpseView>();view.Configure(77,1);
                var trigger=root.GetComponent<SphereCollider>();
                Assert.That(trigger,Is.Not.Null);Assert.That(trigger.isTrigger,Is.True);Assert.That(trigger.radius,Is.EqualTo(1.5f));
                Assert.DoesNotThrow(view.ClearOwnership);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);TestReset.ResetArchitecture();}
        }

        [Test]
        public void CorpseController_RejectsDuplicateIdsBeforeLoadingAndRollsBack()
        {
            TestReset.ResetArchitecture();var root=new GameObject("CorpseDuplicateMap");var content=new GameObject("MapContent");content.transform.SetParent(root.transform,false);var dynamicRoot=new GameObject("DynamicObjectRoot");dynamicRoot.transform.SetParent(content.transform,false);var map=root.AddComponent<MapContext>();var serialized=new SerializedObject(map);serialized.FindProperty("mapContentRoot").objectReferenceValue=content;serialized.FindProperty("dynamicObjectRoot").objectReferenceValue=dynamicRoot.transform;serialized.ApplyModifiedPropertiesWithoutUndo();
            var corpse=new OnlineWorldCorpseSnapshot(9,1,10,0,"enemy",1,"map",Vector3.zero,0,1000,true,1);var world=new OnlineWorldSnapshot(true,1,"map",20,1,Array.Empty<OnlineWorldPlayerSnapshot>(),Array.Empty<OnlineWorldEnemySnapshot>(),0,0,0,false,OnlineWorldOperationState.Idle,OnlineWorldResultCode.Success,OnlineInventorySnapshot.Empty,new[]{corpse,corpse});var controller=new OnlineWorldCorpseCtrl(Game.Interface);
            try
            {
                MapControllerResult result=controller.PrepareMapAsync(new MapPreparationContext("map",1,map,Array.Empty<MapSpawnDefinition>(),world),CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Success,Is.False);Assert.That(result.Error,Does.Contain("重复"));Assert.That(controller.CommitPreparedMap().Success,Is.False);Assert.DoesNotThrow(controller.ReleaseCurrentMap);
            }
            finally{controller.Dispose();UnityEngine.Object.DestroyImmediate(root);TestReset.ResetArchitecture();}
        }

        [UnityTest]
        public IEnumerator SelfUnitPush_TransitionsOldToZeroToNewAndRestoresLocalPlayerFromConvergence()
        {
            InventoryNetwork network=BeginOnlineEnvironment(out InventoryMapLoader maps);
            try
            {
                OnlineWorldSystem system=Game.Interface.GetSystem<OnlineWorldSystem>();
                ET.OnlineWorldSnapshot initial=WireWorld(20,1,Inventory(5),Array.Empty<WorldCorpseInfo>(),WirePlayer(20,new Vector3(1,0,1),80,100));
                network.EnqueueCurrent(initial);network.EnqueueCurrent(initial);
                Task<OnlineWorldOperationResult> restore=system.RestoreCurrentWorldAsync();yield return Await(restore);
                Assert.That(restore.Result.Code,Is.EqualTo(OnlineWorldResultCode.Success));
                PlayerView player=maps.Handle.Context.DynamicObjectRoot.GetComponentInChildren<PlayerView>(true);
                Assert.That(player,Is.Not.Null);Assert.That(player.CharacterController.enabled,Is.True);
                Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryBegin(new PlayerActionRequest(PlayerActionKind.Search,1,string.Empty),out _,out _),Is.True);

                network.Emit(SelfUnitPush(2,20,0));
                Assert.That(system.Snapshot.SelfUnitId,Is.Zero);Assert.That(player.CharacterController.enabled,Is.False);
                Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _),Is.False);
                network.Emit(SelfUnitPush(3,20,999));
                Assert.That(system.Snapshot.SelfUnitId,Is.Zero,"late old-unit transition must be rejected");

                WorldCorpseInfo corpse=WireCorpse(20,4);
                ET.OnlineWorldSnapshot respawned=WireWorld(21,3,Inventory(0),new[]{corpse},WirePlayer(21,new Vector3(7,0,9),100,100));
                network.EnqueueCurrent(respawned);
                int before=network.CurrentRequests;
                network.Emit(SelfUnitPush(3,0,21));
                yield return WaitUntil(()=>system.Snapshot.SelfUnitId==21&&system.Snapshot.Players.Any(x=>x.UnitId==21),"respawn convergence");
                Assert.That(network.CurrentRequests,Is.EqualTo(before+1),"new identity must trigger exactly one convergence request");
                Assert.That(system.Snapshot.SelfInventory.Slots,Is.Empty);Assert.That(system.Snapshot.Corpses.Any(x=>x.CorpseId==20),Is.True);
                Assert.That(system.Snapshot.Players.Single(x=>x.UnitId==21).CurrentHP,Is.EqualTo(100));
                Assert.That(player.CharacterController.enabled,Is.True);Assert.That(player.transform.localPosition,Is.EqualTo(new Vector3(7,0,9)));
                // 空手重生允许备用武器与第一人称手臂保持隐藏，只验当前身体可见表现。
                Assert.That(player.GetComponentsInChildren<Renderer>().Where(x=>x.gameObject.activeInHierarchy).Any(x=>x.enabled),Is.True);
                long mapInstanceId=Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
                Assert.That(Game.Interface.GetUtility<IPlayerRuntimePortProvider>().TryGetMotorPort(PlayerModel.LocalPlayerUnitId,mapInstanceId,out _),Is.True);
            }
            finally{EndOnlineEnvironment();}
        }

        [UnityTest]
        public IEnumerator OnlineSearch_CancelsForDamageMapSwitchAndDisconnect_WhileInventoryRequestRemainsExclusive()
        {
            InventoryNetwork network=BeginOnlineEnvironment(out _);
            try
            {
                OnlineWorldSystem world=Game.Interface.GetSystem<OnlineWorldSystem>();
                WorldCorpseInfo corpse=WireCorpse(90,1);
                ET.OnlineWorldSnapshot initial=WireWorld(20,1,Inventory(5,new OnlineItemSlotInfoValue(0,5,1)),new[]{corpse},WirePlayer(20,Vector3.zero,100,100));
                network.EnqueueCurrent(initial);network.EnqueueCurrent(initial);
                Task<OnlineWorldOperationResult> restore=world.RestoreCurrentWorldAsync();yield return Await(restore);
                OnlineWorldInventorySystem inventory=Game.Interface.GetSystem<OnlineWorldInventorySystem>();
                Assert.That(inventory.TryBeginCorpseSearch(90,out _),Is.True);
                Game.Interface.SendEvent(new OnlineWorldSelfDamagedEvent(20,1,1));Assert.That(inventory.IsSearching,Is.False);
                Assert.That(inventory.TryBeginCorpseSearch(90,out _),Is.True);
                Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.MissionSample));Assert.That(inventory.IsSearching,Is.False);
                Assert.That(inventory.TryBeginCorpseSearch(90,out _),Is.True);

                Task<bool> first=inventory.MoveWholeStackAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.PlayerBag,1));
                yield return null;Assert.That(network.OperateRequests,Is.EqualTo(1));
                Task<bool> overlap=inventory.MoveWholeStackAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.PlayerBag,2));
                Assert.That(overlap.IsCompleted&&overlap.Result,Is.False);Assert.That(network.OperateRequests,Is.EqualTo(1));
                network.RaiseDisconnected();Assert.That(inventory.IsSearching,Is.False);
                network.SetConnected();
                Task<bool> afterReconnect=inventory.MoveWholeStackAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.PlayerBag,3));
                Assert.That(afterReconnect.IsCompleted&&afterReconnect.Result,Is.False,"disconnect must not release ownership of an unfinished request");
                Assert.That(network.OperateRequests,Is.EqualTo(1));
                network.CompleteOperate(OperateResponse(Inventory(6,new OnlineItemSlotInfoValue(1,5,1))));yield return Await(first);
                Assert.That(first.Result,Is.False,"a response owned by the pre-disconnect generation must be rejected");
                Assert.That(world.Snapshot.SelfInventory.Revision,Is.EqualTo(5));
                Assert.That(world.Snapshot.SelfInventory.Slots.Single().SlotIndex,Is.Zero,"the late response must not change the inventory snapshot");

                Task<bool> whileWorldLost=inventory.MoveWholeStackAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),new InventorySlotRef(InventoryContainerKind.PlayerBag,3));
                Assert.That(whileWorldLost.IsCompleted&&whileWorldLost.Result,Is.False,"socket reconnection alone must not enable inventory operations");
                Assert.That(network.OperateRequests,Is.EqualTo(1));
                Task<bool> corpseWhileWorldLost=world.OpenWorldCorpseAsync(90);
                Assert.That(corpseWhileWorldLost.IsCompleted&&corpseWhileWorldLost.Result,Is.False);
                Assert.That(network.OpenCorpseRequests,Is.Zero);
                Assert.That(inventory.TryBeginCorpseSearch(90,out _),Is.False);
                Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _),Is.False);

                network.EnqueueCurrent(initial);network.EnqueueCurrent(initial);
                Task<OnlineWorldOperationResult> restored=world.RestoreCurrentWorldAsync();yield return Await(restored);
                Assert.That(restored.Result.Code,Is.EqualTo(OnlineWorldResultCode.Success));
                Assert.That(world.Snapshot.ConnectionLost,Is.False);
                Assert.That(InventoryOpenRules.CanOpenPlayerBag(out _),Is.True);
                Task<bool> next=inventory.MoveWholeStackAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,1),new InventorySlotRef(InventoryContainerKind.PlayerBag,2));
                yield return null;Assert.That(network.OperateRequests,Is.EqualTo(2));network.CompleteOperate(OperateResponse(Inventory(6,new OnlineItemSlotInfoValue(2,5,1))));yield return Await(next);
                Assert.That(next.Result,Is.True);
            }
            finally{EndOnlineEnvironment();}
        }

        [UnityTest]
        public IEnumerator OnlineItemTransfer_EquipsConfiguredMeleeAndRangedWeapons_WhileRejectingUnsupportedActions()
        {
            InventoryNetwork network=BeginOnlineEnvironment(out _,includeShortSwordConfig:true);
            GameObject uiRoot=null;
            try
            {
                ET.OnlineWorldSnapshot initial=WireWorld(20,1,Inventory(5,
                    new OnlineItemSlotInfoValue(0,ItemIds.Medkit,1),new OnlineItemSlotInfoValue(1,ItemIds.SurvivalSampleMap,1),
                    new OnlineItemSlotInfoValue(2,ItemIds.Bow,1),new OnlineItemSlotInfoValue(3,ItemIds.ShortSword,1)),Array.Empty<WorldCorpseInfo>(),WirePlayer(20,Vector3.zero,100,100));
                network.EnqueueCurrent(initial);network.EnqueueCurrent(initial);
                Task<OnlineWorldOperationResult> restore=Game.Interface.GetSystem<OnlineWorldSystem>().RestoreCurrentWorldAsync();yield return Await(restore);
                uiRoot=CreateFormalUiRootForTest();
                IItemTransferPort transfer=Game.Interface.GetUtility<IItemTransferPort>();
                Assert.That(transfer.IsOnlineMode,Is.True);Assert.That(transfer.CanExecute(InventoryUiAction.Use),Is.True);
                Assert.That(transfer.CanExecute(InventoryUiAction.Drop),Is.False);Assert.That(transfer.CanExecute(InventoryUiAction.Split),Is.False);Assert.That(transfer.CanExecute(InventoryUiAction.Ranged),Is.False);
                foreach(int slot in new[]{0,1}){transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,slot));transfer.UseSelected();}
                transfer.BeginSplit();transfer.DropSelectedToGround();
                Assert.That(network.OperateRequests,Is.Zero,"unsupported direct UI entries sent an online RPC");
                transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,2));transfer.UseSelected();yield return null;
                Assert.That(network.OperateRequests,Is.EqualTo(1));
                network.CompleteOperate(OperateResponse(Inventory(6,new[]{new OnlineItemSlotInfoValue(2,ItemIds.Bow,1),new OnlineItemSlotInfoValue(3,ItemIds.ShortSword,1)},0,ItemIds.Bow,2,0)));
                yield return WaitUntil(()=>Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Equipment.RangedWeaponItemId==ItemIds.Bow,"online bow equip convergence");
                transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,3));transfer.UseSelected();yield return null;
                Assert.That(network.OperateRequests,Is.EqualTo(2));
                network.CompleteOperate(OperateResponse(Inventory(7,new[]{new OnlineItemSlotInfoValue(2,ItemIds.Bow,1),new OnlineItemSlotInfoValue(3,ItemIds.ShortSword,1)},ItemIds.ShortSword,0,1,0)));
                yield return WaitUntil(()=>Game.Interface.GetSystem<OnlineWorldSystem>().Snapshot.SelfInventory.Equipment.MeleeWeaponItemId==ItemIds.ShortSword,"online sword equip convergence");
            }
            finally
            {
                UIHelper.ResetForTests();
                UIRoot.ClearForTests();
                GameObjectPoolHelper.ClearForTests();
                if(uiRoot!=null)Object.DestroyImmediate(uiRoot);
                EditModeSceneIsolation.DestroyNamedObjectsInActiveTestScene(SceneRoot.PoolRootName);
                EndOnlineEnvironment();
            }
        }

        [UnityTest]
        public IEnumerator ClothingWaitsForAuthorityAndPreservesInitialRemoteSnapshot()
        {
            var network=BeginOnlineEnvironment(out _);
            Game.Interface.RegisterSystem<OptionalGameplayResourceSystem>(new ClothingReadyResources());
            try
            {
                var world=Game.Interface.GetSystem<OnlineWorldSystem>();
                var inventory=Inventory(5,new OnlineItemSlotInfoValue(0,38,1));inventory.LegsItemId=40;
                inventory.HeadItemId=42;inventory.HandsItemId=43;inventory.FeetItemId=44;inventory.BackItemId=45;inventory.ArmorItemId=46;
                var remote=WirePlayer(30,new Vector3(3,0,1),100,100);remote.TorsoItemId=39;remote.LegsItemId=41;
                remote.HeadItemId=42;remote.HandsItemId=43;remote.FeetItemId=44;remote.BackItemId=45;remote.ArmorItemId=46;
                var initial=WireWorld(20,1,inventory,Array.Empty<WorldCorpseInfo>(),WirePlayer(20,Vector3.zero,100,100),remote);
                network.EnqueueCurrent(initial);network.EnqueueCurrent(initial);
                var restore=world.RestoreCurrentWorldAsync();yield return Await(restore);Assert.That(restore.Result.Code,Is.EqualTo(OnlineWorldResultCode.Success));
                Assert.That(world.Snapshot.Players.Single(x=>x.UnitId==30).TorsoItemId,Is.EqualTo(39));
                Assert.That(world.Snapshot.SelfInventory.LegsItemId,Is.EqualTo(40));
                Assert.That(world.Snapshot.Players.Single(x=>x.UnitId==30).ClothingItems(),Is.EqualTo(new[]{39,41,42,43,44,45,46}));
                Assert.That(world.Snapshot.SelfInventory.ClothingItems(),Is.EqualTo(new[]{0,40,42,43,44,45,46}));
                var system=Game.Interface.GetSystem<OnlineWorldInventorySystem>();
                var rejected=system.EquipClothingAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),4);yield return Await(rejected);
                Assert.That(rejected.Result,Is.False);Assert.That(network.OperateRequests,Is.Zero);
                var equip=system.EquipClothingAsync(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),5);yield return null;
                Assert.That(world.Snapshot.SelfInventory.Slots.Single().ItemId,Is.EqualTo(38));Assert.That(world.Snapshot.SelfInventory.TorsoItemId,Is.Zero);
                var accepted=Inventory(6);accepted.TorsoItemId=38;accepted.LegsItemId=40;
                accepted.HeadItemId=42;accepted.HandsItemId=43;accepted.FeetItemId=44;accepted.BackItemId=45;accepted.ArmorItemId=46;
                network.CompleteOperate(OperateResponse(accepted));yield return Await(equip);
                Assert.That(equip.Result,Is.True);Assert.That(world.Snapshot.SelfInventory.TorsoItemId,Is.EqualTo(38));Assert.That(world.Snapshot.SelfInventory.Slots,Is.Empty);
                Assert.That(world.Snapshot.SelfInventory.ClothingItems(),Is.EqualTo(new[]{38,40,42,43,44,45,46}));
                Assert.That(Game.Interface.GetModel<ClothingEquipmentModel>().GetItemId(global::ClothingSlot.Torso),Is.Zero,"online state must not overwrite offline save");
            }
            finally{EndOnlineEnvironment();}
        }

        // 此用例验证库存协议；地图下载由独立资源准备测试覆盖。
        private sealed class ClothingReadyResources : OptionalGameplayResourceSystem
        {
            public override OptionalGameplayResourceQuery QueryMap(string mapId)=>new(OptionalGameplayResourceState.Ready,"test",0,0,null);
            public override UniTask<OptionalGameplayResourceQuery> CheckMapAsync(string mapId)=>UniTask.FromResult(QueryMap(mapId));
        }

        private static GameObject CreateFormalUiRootForTest()
        {
            var root=new GameObject("UIRoot",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            new GameObject("HUD", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Dialog", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Popup", typeof(RectTransform)).transform.SetParent(root.transform, false);
            new GameObject("Tips",typeof(RectTransform)).transform.SetParent(root.transform,false);
            new GameObject("Top",typeof(RectTransform)).transform.SetParent(root.transform,false);
            var eventSystem=new GameObject("EventSystem",typeof(EventSystem));eventSystem.transform.SetParent(root.transform,false);
            Type inputModuleType=Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModuleType,Is.Not.Null,"formal UIRoot requires InputSystemUIInputModule");
            eventSystem.AddComponent(inputModuleType);
            root.AddComponent<UIRoot>();
            new GameObject(SceneRoot.PoolRootName);
            return root;
        }

        private static InventoryNetwork BeginOnlineEnvironment(out InventoryMapLoader maps,bool includeShortSwordConfig=false)
        {
            TestReset.ResetArchitecture();LifeCycle.ClearForTests();EditModeSceneIsolation.Enter();EditModeSceneIsolation.RequireActiveTestScene();
            var network=new InventoryNetwork();maps=new InventoryMapLoader();ResourceLoader.SetLoaderForTests(new InventoryResourceLoader());
            InventoryMapLoader capturedMaps=maps;
            Game.OnRegisterPatch=architecture=>{architecture.RegisterUtility<INetworkRuntime>(network);architecture.RegisterUtility<IMapContentLoader>(capturedMaps);IRuntimeConfigProvider config=new TestRuntimeConfigProvider(includeMapPlayerSpawn:true);if(includeShortSwordConfig)config=new OnlineInventoryTestConfigProvider(config);architecture.RegisterUtility(config);};
            var camera=new GameObject("OnlineWorldInventoryCamera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";
            _=Game.Interface.GetSystem<OnlineWorldInventorySystem>();return network;
        }

        private static void EndOnlineEnvironment()
        {
            if(Game.Interface is IArchitecture architecture)architecture.Deinit();Game.OnRegisterPatch=_=>{};ResourceLoader.ResetForTests();LifeCycle.ClearForTests();EditModeSceneIsolation.Exit();
        }

        private static IEnumerator Await(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception;}
        private static IEnumerator WaitUntil(Func<bool> condition,string label){float end=Time.realtimeSinceStartup+5;while(!condition()){Assert.That(Time.realtimeSinceStartup,Is.LessThan(end),label);yield return null;}}
        private readonly struct OnlineItemSlotInfoValue{public OnlineItemSlotInfoValue(int slot,int item,int amount){Slot=slot;Item=item;Amount=amount;}public int Slot{get;}public int Item{get;}public int Amount{get;}}
        private static OnlineInventoryInfo Inventory(long revision,params OnlineItemSlotInfoValue[] slots)=>Inventory(revision,slots,0);
        private static OnlineInventoryInfo Inventory(long revision,OnlineItemSlotInfoValue[] slots,int equipment){OnlineInventoryInfo value=OnlineInventoryInfo.Create();value.Capacity=12;value.Revision=revision;value.Equipment=OnlineEquipmentInfo.Create();value.Equipment.SchemaVersion=1;value.Equipment.SourceSlotIndex=-1;value.Equipment.MeleeWeaponItemId=equipment;value.Equipment.ItemId=equipment;value.Equipment.SourceSlotIndex=equipment==0?-1:slots.FirstOrDefault(x=>x.Item==equipment).Slot;value.Equipment.ActiveSlot=equipment==0?0:1;foreach(OnlineItemSlotInfoValue item in slots){OnlineItemSlotInfo slot=OnlineItemSlotInfo.Create();slot.SlotIndex=item.Slot;slot.ItemId=item.Item;slot.Amount=item.Amount;value.Slots.Add(slot);}return value;}
        private static OnlineInventoryInfo Inventory(long revision,OnlineItemSlotInfoValue[] slots,int melee,int ranged,int active,int loaded){OnlineInventoryInfo value=Inventory(revision,slots,melee);value.Equipment.RangedWeaponItemId=ranged;value.Equipment.ItemId=active==2?ranged:melee;value.Equipment.SourceSlotIndex=value.Equipment.ItemId==0?-1:slots.FirstOrDefault(x=>x.Item==value.Equipment.ItemId).Slot;value.Equipment.ActiveSlot=active;value.Equipment.RangedLoadedAmmo=loaded;return value;}
        private static OnlineInventoryInfo Inventory(long revision,int equipment)=>Inventory(revision,Array.Empty<OnlineItemSlotInfoValue>(),equipment);
        private static WorldPlayerInfo WirePlayer(long unit,Vector3 position,int hp,int max){WorldPlayerInfo value=WorldPlayerInfo.Create();value.PlayerId=2;value.UnitId=unit;value.DisplayName="P2";value.WorldId=1;value.MapId=MapId;value.PlayerConfigId="player.graybox.default";value.PositionX=position.x;value.PositionY=position.y;value.PositionZ=position.z;value.ConnectionState=1;value.StateRevision=1;value.CurrentHP=hp;value.MaxHP=max;value.LifecycleRevision=1;return value;}
        private static WorldCorpseInfo WireCorpse(long id,long revision){WorldCorpseInfo value=WorldCorpseInfo.Create();value.CorpseId=id;value.SourceKind=2;value.SourceUnitId=id;value.SourcePlayerId=2;value.SourceConfigId="player.graybox.default";value.WorldId=1;value.MapId=MapId;value.HasContents=true;value.StateRevision=revision;value.ExpiresAtServerTime=long.MaxValue;return value;}
        private static ET.OnlineWorldSnapshot WireWorld(long self,long revision,OnlineInventoryInfo inventory,IEnumerable<WorldCorpseInfo> corpses,params WorldPlayerInfo[] players){ET.OnlineWorldSnapshot value=ET.OnlineWorldSnapshot.Create();value.WorldId=1;value.MapId=MapId;value.SelfUnitId=self;value.WorldRevision=revision;value.SelfInventory=inventory;foreach(WorldPlayerInfo player in players)value.Players.Add(player);foreach(WorldCorpseInfo corpse in corpses)value.Corpses.Add(corpse);return value;}
        private static G2C_WorldSelfUnitChanged SelfUnitPush(long revision,long previous,long current){G2C_WorldSelfUnitChanged value=G2C_WorldSelfUnitChanged.Create();value.WorldId=1;value.MapId=MapId;value.WorldRevision=revision;value.PreviousUnitId=previous;value.SelfUnitId=current;return value;}
        private static G2C_GetCurrentWorld Current(ET.OnlineWorldSnapshot world){G2C_GetCurrentWorld value=G2C_GetCurrentWorld.Create();value.Error=0;value.HasWorld=true;value.World=world;return value;}
        private static G2C_OperateWorldInventory OperateResponse(OnlineInventoryInfo inventory){G2C_OperateWorldInventory value=G2C_OperateWorldInventory.Create();value.Error=0;value.Accepted=true;value.SelfInventory=inventory;return value;}

        private sealed class OnlineInventoryTestConfigProvider:IRuntimeConfigProvider
        {
            private readonly IRuntimeConfigProvider inner;
            public OnlineInventoryTestConfigProvider(IRuntimeConfigProvider inner)=>this.inner=inner;
            public bool TryGetItem(int id,out ItemDefinition value,out string error)
            {
                if(id==ItemIds.ShortSword||id==ItemIds.Bow){value=new ItemDefinition(id,id==ItemIds.Bow?"Bow":"Short Sword",string.Empty,string.Empty,string.Empty,string.Empty,ItemType.Weapon,1,ItemUseEffect.None,0);error=null;return true;}
                return inner.TryGetItem(id,out value,out error);
            }
            public bool TryGetItems(out IReadOnlyList<ItemDefinition> values,out string error)=>inner.TryGetItems(out values,out error);
            public bool TryGetWeapon(int itemId,out WeaponConfigDefinition value,out string error)
            {
                if(itemId==ItemIds.ShortSword){value=new WeaponConfigDefinition(itemId,WeaponKind.Melee,10,0.45f,2.2f,90f,string.Empty,0);error=null;return true;}
                if(itemId==ItemIds.Bow){value=new WeaponConfigDefinition(itemId,WeaponKind.Projectile,10,0.75f,14f,0f,"projectile.arrow.basic",ItemIds.Arrow,1,1f);error=null;return true;}
                return inner.TryGetWeapon(itemId,out value,out error);
            }
            public bool TryGetProjectile(string id,out ProjectileConfigDefinition value,out string error)=>inner.TryGetProjectile(id,out value,out error);
            public bool TryGetProjectiles(out IReadOnlyList<ProjectileConfigDefinition> values,out string error)=>inner.TryGetProjectiles(out values,out error);
            public bool TryGetPlayerWeaponAppearance(int itemId,out PlayerWeaponAppearanceDefinition value,out string error)=>inner.TryGetPlayerWeaponAppearance(itemId,out value,out error);
            public bool TryGetCraftRecipe(string id,out CraftRecipeDefinition value,out string error)=>inner.TryGetCraftRecipe(id,out value,out error);
            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values,out string error)=>inner.TryGetCraftRecipes(out values,out error);
            public bool TryGetTempBuilding(string id,out TempBuildingDefinition value,out string error)=>inner.TryGetTempBuilding(id,out value,out error);
            public bool TryGetMap(string id,out MapAssetDefinition value,out string error)=>inner.TryGetMap(id,out value,out error);
            public bool TryGetMapDisplay(string mapId,out MapDisplayDefinition value,out string error)=>inner.TryGetMapDisplay(mapId,out value,out error);
            public bool TryGetExpeditionTasks(string mapId,out IReadOnlyList<ExpeditionTaskDefinition> values,out string error)=>inner.TryGetExpeditionTasks(mapId,out values,out error);
            public bool TryGetMapSpawns(string mapId,out IReadOnlyList<MapSpawnDefinition> values,out string error)=>inner.TryGetMapSpawns(mapId,out values,out error);
            public bool TryGetNpc(string id,out NpcConfigDefinition value,out string error)=>inner.TryGetNpc(id,out value,out error);
            public bool TryGetShop(string shopId,out ShopDefinition definition,out string error)=>inner.TryGetShop(shopId,out definition,out error);
            public bool TryGetShopItems(string shopId,out IReadOnlyList<ShopItemDefinition> definitions,out string error)=>inner.TryGetShopItems(shopId,out definitions,out error);
            public bool TryGetShopItem(string shopId,int itemId,out ShopItemDefinition definition,out string error)=>inner.TryGetShopItem(shopId,itemId,out definition,out error);
            public bool TryGetPlayer(string id,out PlayerConfigDefinition value,out string error)=>inner.TryGetPlayer(id,out value,out error);
            public bool TryGetEnemy(string id,out EnemyConfigDefinition value,out string error)=>inner.TryGetEnemy(id,out value,out error);
            public bool TryGetLoot(string id,out LootConfigDefinition value,out string error)=>inner.TryGetLoot(id,out value,out error);
            public bool TryGetLootByItemId(int itemId,out LootConfigDefinition value,out string error)=>inner.TryGetLootByItemId(itemId,out value,out error);
            public bool TryGetTempBuildings(out IReadOnlyList<TempBuildingDefinition> values,out string error)=>inner.TryGetTempBuildings(out values,out error);
            public bool TryGetTempBuildingManualAttack(string buildingId,out TempBuildingManualAttackDefinition value)=>inner.TryGetTempBuildingManualAttack(buildingId,out value);
            public bool TryGetExtraction(string id,out ExtractionConfigDefinition value,out string error)=>inner.TryGetExtraction(id,out value,out error);
            public bool TryGetTestCmdCatalog(out IReadOnlyList<TestCmdCategoryDefinition> categories,out IReadOnlyList<TestCmdEntryDefinition> entries,out string error)=>inner.TryGetTestCmdCatalog(out categories,out entries,out error);
        }

        private sealed class InventoryNetwork:INetworkRuntime
        {
            private readonly Dictionary<Type,List<Delegate>> pushes=new();private readonly List<Action<NetworkDisconnected>> disconnected=new();private readonly Queue<G2C_GetCurrentWorld> currents=new();private TaskCompletionSource<IResponse> pendingOperate;
            public bool IsInitialized=>true;public NetworkConnectionState ConnectionState{get;private set;}=NetworkConnectionState.Connected;public int ConnectionGeneration{get;private set;}=1;public int CurrentRequests{get;private set;}public int OperateRequests{get;private set;}public int OpenCorpseRequests{get;private set;}
            public void Initialize(){}public void Update(){}public Task ConnectAsync(NetworkEndpoint endpoint,int timeout,CancellationToken token){SetConnected();return Task.CompletedTask;}public void Disconnect(NetworkDisconnectReason reason)=>ConnectionState=NetworkConnectionState.Disconnected;public void Dispose()=>ConnectionState=NetworkConnectionState.Disposed;
            public async Task<TResponse> CallAsync<TResponse>(IRequest request,int timeout,CancellationToken token) where TResponse:class,IResponse
            {if(request is C2G_GetCurrentWorld){CurrentRequests++;return currents.Dequeue() as TResponse;}if(request is C2G_OperateWorldInventory){OperateRequests++;pendingOperate=new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);return (TResponse)await pendingOperate.Task;}if(request is C2G_OpenWorldCorpse){OpenCorpseRequests++;throw new InvalidOperationException("unexpected corpse request");}throw new InvalidOperationException(request.GetType().Name);}
            public IDisposable RegisterPushHandler<T>(Action<T> handler) where T:class,IMessage{if(!pushes.TryGetValue(typeof(T),out List<Delegate> list))pushes[typeof(T)]=list=new List<Delegate>();list.Add(handler);return new Subscription(()=>list.Remove(handler));}
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler){disconnected.Add(handler);return new Subscription(()=>disconnected.Remove(handler));}public IDisposable RegisterResponseHandler<T>(Action<T> handler) where T:class,IResponse=>new Subscription(()=>{});
            public void EnqueueCurrent(ET.OnlineWorldSnapshot world)=>currents.Enqueue(Current(world));public void Emit<T>(T message) where T:class,IMessage{if(pushes.TryGetValue(typeof(T),out List<Delegate> list))foreach(Delegate handler in list.ToArray())((Action<T>)handler)(message);}public void CompleteOperate(IResponse response){pendingOperate.SetResult(response);pendingOperate=null;}
            public void RaiseDisconnected(){ConnectionState=NetworkConnectionState.Disconnected;var value=new NetworkDisconnected(ConnectionGeneration,NetworkDisconnectReason.RemoteClosed,"test");foreach(Action<NetworkDisconnected> handler in disconnected.ToArray())handler(value);}public void SetConnected(){ConnectionState=NetworkConnectionState.Connected;ConnectionGeneration++;}
        }
        private sealed class Subscription:IDisposable{private Action release;public Subscription(Action value)=>release=value;public void Dispose(){release?.Invoke();release=null;}}
        private sealed class InventoryMapLoader:IMapContentLoader
        {public IMapContentHandle Handle{get;private set;}public UniTask<MapContentLoadResult> LoadAsync(string mapId,string address,Action<float> progress,CancellationToken token=default){Handle=TemporaryBuildingBehaviorTestSupport.CreateMapHandle(mapId,500);return UniTask.FromResult(MapContentLoadResult.Ok(Handle));}public UniTask<MapContentUnloadResult> UnloadAsync(IMapContentHandle handle,CancellationToken token=default){if(handle is FakeMapHandle fake&&fake.Root!=null)Object.DestroyImmediate(fake.Root);return UniTask.FromResult(MapContentUnloadResult.Ok());}}
        private sealed class InventoryResourceLoader:IResourceLoader
        {public T LoadAsset<T>(string path) where T:Object=>Load(path) as T;public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:Object{loaded?.Invoke(Load(path) as T);return new ResourceLoadHandle(new object());}public void Release(ResourceLoadHandle handle){}private static Object Load(string path){if(path.StartsWith("Prefabs/",StringComparison.Ordinal))return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab");string candidate=path=="languages.json"?"Assets/Game/Config/Localization/languages.json":$"Assets/Game/Config/Localization/{path}";return AssetDatabase.LoadAssetAtPath<TextAsset>(candidate);}}
    }
}
