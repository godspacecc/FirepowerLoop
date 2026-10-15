using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>验证装备分页真实库存操作与占位边界；复用既有隔离 UI 夹具，不修改正式资产。</summary>
    public sealed class EquipmentManagementUiTests
    {
        private InventoryIndependentUiTests environment;
        private BagUICtrl host;
        private BagUI bagUI;
        private GameObject poolRoot;

        [SetUp]
        public void SetUp()
        {
            environment = new InventoryIndependentUiTests();
            environment.SetUp();
            poolRoot = new GameObject("PoolRoot");
            var localization = Game.Interface.GetUtility<LocalizationUtility>();
            localization.InitializeConfiguredTables();
            foreach (var language in new[] { "zh-CN", "en-US" })
                localization.LoadFromJson(language, System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Game/Config/Localization/" + language + ".json")));
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>((IRuntimeConfigProvider)
                typeof(ItemInventoryFoundationTests).GetMethod("CreateRealConfigProvider", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null));
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            host = UIHelper.GetUICtrl<BagUICtrl>();
            bagUI = (BagUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
        }

        [TearDown] public void TearDown()
        {
            environment.TearDown();
            Object.DestroyImmediate(poolRoot);
        }


        [Test] public void BackpackPreviewShowsCurrentClothesAndReleasesWhenHidden()
        {
            var preview = BackpackPreview();
            Assert.That(preview.texture, Is.Not.Null);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(42, 1, 1, out _, out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0));
            bagUI.FullBtnUse.onClick.Invoke();
            var actor = System.Linq.Enumerable.Single(PreviewActors());
            Assert.That(actor.transform.Find("PlayerHumanoid/ClothingParts/Head_OliveCap").gameObject.activeSelf, Is.True);
            host.Hide(); Assert.That(preview.texture, Is.Null);
            Assert.That(PreviewActors().Length, Is.Zero);
        }
        [Test] public void PreviewDragAndPageSwitchPreserveRotationWithOneActor()
        {
            var preview = BackpackPreview();
            var actor = System.Linq.Enumerable.Single(PreviewActors());
            var initial = actor.transform.localEulerAngles.y;
            var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, pointerId = 0, delta = new Vector2(80, 0) };
            UnityEngine.EventSystems.ExecuteEvents.Execute(preview.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(preview.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
            var yaw = actor.transform.localEulerAngles.y;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(initial, yaw)), Is.GreaterThan(10));
            bagUI.BtnPageEquipment.onClick.Invoke();
            Assert.That(preview.texture, Is.Null);
            Assert.That(PreviewActors().Length, Is.EqualTo(1));
            Assert.That(System.Linq.Enumerable.Single(PreviewActors()).transform.localEulerAngles.y, Is.EqualTo(yaw).Within(.01f));
            bagUI.BtnPageBackpack.onClick.Invoke();
            Assert.That(bagUI.EquipmentPage.CharacterPreview.texture, Is.Null);
            Assert.That(System.Linq.Enumerable.Single(PreviewActors()).transform.localEulerAngles.y, Is.EqualTo(yaw).Within(.01f));
            bagUI.BtnPageStatus.onClick.Invoke();
            Assert.That(preview.texture, Is.Null);
            Assert.That(PreviewActors().Length, Is.Zero);
        }
        [Test] public void PreviewOrbitAndZoomSurvivePageSwitchAndClamp()
        {
            var raw = BackpackPreview();
            var camera = PreviewCamera();
            var forward = camera.transform.forward;
            var size = camera.orthographicSize;
            var data = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, pointerId = -1, delta = new Vector2(60, -100) };
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
            Assert.That(Vector3.Angle(forward, camera.transform.forward), Is.GreaterThan(5), "纵向拖动必须改变观察俯仰");
            data.scrollDelta = new Vector2(0, 120);
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
            Assert.That(camera.orthographicSize, Is.LessThan(size), "滚轮向上必须拉近");
            var expectedForward = camera.transform.forward;
            var expectedSize = camera.orthographicSize;
            bagUI.BtnPageEquipment.onClick.Invoke();
            Assert.That(Vector3.Angle(expectedForward, PreviewCamera().transform.forward), Is.LessThan(.01f));
            Assert.That(PreviewCamera().orthographicSize, Is.EqualTo(expectedSize).Within(.001f));
            bagUI.BtnPageBackpack.onClick.Invoke();
            Assert.That(Vector3.Angle(expectedForward, PreviewCamera().transform.forward), Is.LessThan(.01f));
            Assert.That(PreviewCamera().orthographicSize, Is.EqualTo(expectedSize).Within(.001f));
            data.scrollDelta = new Vector2(0, 100000);
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
            Assert.That(PreviewCamera().orthographicSize, Is.GreaterThanOrEqualTo(.55f));
            data.scrollDelta = new Vector2(0, -100000);
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
            Assert.That(PreviewCamera().orthographicSize, Is.LessThanOrEqualTo(1.5f));
        }
        [Test] public void PreviewPinchZoomDoesNotRotateAndHideClearsPointers()
        {
            var raw = BackpackPreview(); var yaw = System.Linq.Enumerable.Single(PreviewActors()).transform.localEulerAngles.y;
            var size = PreviewCamera().orthographicSize;
            var first = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, pointerId = 11, position = new Vector2(100, 100) };
            var second = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, pointerId = 12, position = new Vector2(200, 100) };
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, first, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, second, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
            second.position = new Vector2(300, 100); second.delta = new Vector2(100, 0);
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, second, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
            Assert.That(PreviewCamera().orthographicSize, Is.LessThan(size));
            Assert.That(System.Linq.Enumerable.Single(PreviewActors()).transform.localEulerAngles.y, Is.EqualTo(yaw).Within(.01f));
            host.Hide(); BagUICtrl.TryShow(out _); raw = BackpackPreview();
            var before = PreviewCamera().orthographicSize;
            UnityEngine.EventSystems.ExecuteEvents.Execute(raw.gameObject, second, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
            Assert.That(PreviewCamera().orthographicSize, Is.EqualTo(before), "隐藏后旧触点不能继续缩放");
        }
        [Test] public void CharacterPanelsUseManagementBackground()
        {
            var background = bagUI.transform.Find("ManagementRoot").GetComponent<Image>().color;
            Assert.That(BackpackPreview().transform.parent.GetComponent<Image>().color, Is.EqualTo(background));
            Assert.That(bagUI.EquipmentPage.CharacterPreview.transform.parent.GetComponent<Image>().color, Is.EqualTo(background));
        }
        private Camera PreviewCamera() => System.Linq.Enumerable.Single(System.Linq.Enumerable.Where(Object.FindObjectsOfType<Camera>(), c => c.name == "PortraitCamera"));
        [Test] public void DestroyingHostReleasesBackpackPreviewBeforeRebinding()
        {
            BackpackPreview(); Object.DestroyImmediate(bagUI.gameObject);
            Assert.That(PreviewActors().Length, Is.Zero);
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            host = UIHelper.GetUICtrl<BagUICtrl>();
            bagUI = (BagUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            Assert.That(BackpackPreview().texture, Is.Not.Null);
            Assert.That(PreviewActors().Length, Is.EqualTo(1));
        }
        private ClothingAppearanceView[] PreviewActors() => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(Object.FindObjectsOfType<ClothingAppearanceView>(), x => x.transform.root.name == "ClothingPagePreview"));
        private RawImage BackpackPreview()
        {
            var field = typeof(BagUI).GetField("BackpackCharacterPreview");
            Assert.That(field, Is.Not.Null, "背包页必须绑定实际人物预览");
            return (RawImage)field.GetValue(bagUI);
        }
        [Test] public void WholeBodyCommandRefreshesBothPagesAndResetRestoresOutfit()
        {
            var commands=Game.Interface.GetSystem<TestCmdSystem>();
            var result=commands.ExecuteAsync("#appearance 8",System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Success,Is.True,result.Message);
            var actor=System.Linq.Enumerable.Single(PreviewActors());
            Assert.That(actor.GetComponentsInChildren<SkinnedMeshRenderer>().Length,Is.EqualTo(1));
            Assert.That(actor.GetComponentInChildren<SkinnedMeshRenderer>().name,Is.EqualTo("Character_Male_Jacket"));
            bagUI.BtnPageEquipment.onClick.Invoke();
            actor=System.Linq.Enumerable.Single(PreviewActors());
            Assert.That(actor.GetComponentInChildren<SkinnedMeshRenderer>().name,Is.EqualTo("Character_Male_Jacket"));
            result=commands.ExecuteAsync("#appearance 3",System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Success,Is.True,result.Message);
            Assert.That(actor.GetComponentInChildren<SkinnedMeshRenderer>().name,Is.EqualTo("Character_BusinessWoman"));
            commands.ExecuteAsync("#appearance reset",System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(actor.GetComponentsInChildren<SkinnedMeshRenderer>().Length,Is.GreaterThan(1));
            host.Hide();
            Assert.That(PreviewActors().Length,Is.Zero);
            Assert.DoesNotThrow(()=>Game.Interface.SendEvent(new PlayerTestAppearanceChangedEvent()));
        }

        [Test] public void BothPagesFollowHeldWeaponAndReleaseWithHost()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            bag.Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            var weapons = Game.Interface.GetSystem<WeaponEquipmentSystem>();
            Assert.That(weapons.TryHold(new InventorySlotRef(InventoryContainerKind.Pocket, 0), ItemIds.ShortSword, out var error), Is.True, error);
            var presenter = System.Linq.Enumerable.Single(PreviewActors()).GetComponent<CharacterWeaponPresenter>();
            Assert.That(presenter, Is.Not.Null, "预览必须接入共享武器表现");
            Assert.That(presenter.Current.WorldItemId, Is.EqualTo(ItemIds.ShortSword));
            Assert.That(presenter.Visual, Is.Not.Null);
            Assert.That(presenter.Visual.transform.parent, Is.EqualTo(presenter.BodyAnimator.GetBoneTransform(presenter.Current.MainHand)));
            Assert.That(presenter.Visual.layer, Is.EqualTo(31));
            bagUI.BtnPageEquipment.onClick.Invoke();
            presenter = System.Linq.Enumerable.Single(PreviewActors()).GetComponent<CharacterWeaponPresenter>();
            Assert.That(presenter.Current.WorldItemId, Is.EqualTo(ItemIds.ShortSword));
            bag.Pocket.TryAdd(ItemIds.Bow, 1, 1, out _, out _);
            Assert.That(weapons.TryHold(new InventorySlotRef(InventoryContainerKind.Pocket, 1), ItemIds.Bow, out error), Is.True, error);
            Assert.That(presenter.Current.WorldItemId, Is.EqualTo(ItemIds.Bow));
            bagUI.BtnPageBackpack.onClick.Invoke();
            presenter = System.Linq.Enumerable.Single(PreviewActors()).GetComponent<CharacterWeaponPresenter>();
            Assert.That(presenter.Current.WorldItemId, Is.EqualTo(ItemIds.Bow));
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.Pocket, 1));
            bagUI.FullBtnUse.onClick.Invoke();
            Assert.That(presenter.Visual, Is.Null);
            host.Hide();
            Assert.That(PreviewActors().Length, Is.Zero);
            Assert.DoesNotThrow(() => Game.Interface.SendEvent(new WeaponEquipmentChangedEvent()));
        }

        [Test] public void OnlinePreviewUsesConfirmedWeaponInsteadOfLocalHeldItem()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>();
            bag.Pocket.TryAdd(ItemIds.ShortSword, 1, 1, out _, out _);
            Game.Interface.GetSystem<WeaponEquipmentSystem>().TryHold(new InventorySlotRef(InventoryContainerKind.Pocket, 0), ItemIds.ShortSword, out _);
            typeof(MapSceneModel).GetProperty("CurrentMode").SetValue(Game.Interface.GetModel<MapSceneModel>(), MapPreparationMode.OnlinePlayersOnly);
            var inventory = new OnlineInventorySnapshot(12, System.Array.Empty<OnlineItemSlotSnapshot>(),
                new OnlineEquipmentSnapshot(0, ItemIds.Bow, 0, WeaponSlot.Ranged), 1);
            var snapshot = new OnlineWorldSnapshot(true, 1, "test", 1, 1, System.Array.Empty<OnlineWorldPlayerSnapshot>(),
                System.Array.Empty<OnlineWorldEnemySnapshot>(), 0, 0, 0, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.None, inventory);
            var world = Game.Interface.GetSystem<OnlineWorldSystem>();
            typeof(OnlineWorldSystem).GetField("snapshot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(world, snapshot);
            Game.Interface.SendEvent(new OnlineWorldChangedEvent(snapshot));
            var presenter = System.Linq.Enumerable.Single(PreviewActors()).GetComponent<CharacterWeaponPresenter>();
            Assert.That(presenter.Current.WorldItemId, Is.EqualTo(ItemIds.Bow));
            bagUI.BtnPageEquipment.onClick.Invoke();
            presenter = System.Linq.Enumerable.Single(PreviewActors()).GetComponent<CharacterWeaponPresenter>();
            Assert.That(presenter.Current.WorldItemId, Is.EqualTo(ItemIds.Bow));
            typeof(OnlineWorldSystem).GetField("snapshot", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(world, OnlineWorldSnapshot.Empty());
            Game.Interface.SendEvent(new OnlineWorldInventoryChangedEvent(2, 0, 0));
            Assert.That(presenter.Visual, Is.Null, "服务端空手不能回退显示本地武器");
            host.Hide();
            Assert.DoesNotThrow(() => Game.Interface.SendEvent(new OnlineWorldChangedEvent(OnlineWorldSnapshot.Empty())));
        }

        [Test] public void EquipmentTabContainsClothingOnly()
        {
            bagUI.BtnPageEquipment.onClick.Invoke(); var page=bagUI.EquipmentPage;
            Assert.That(page.gameObject.activeSelf,Is.True);page.BtnHead.onClick.Invoke();
            Assert.That(page.DetailDescriptionText.text,Is.EqualTo(Loc.T("ui.clothing.empty_detail")));
            Assert.That(page.GetType().GetField("BtnMelee"),Is.Null);
            Assert.That(page.GetType().GetField("BtnRanged"),Is.Null);
            Assert.That(page.GetType().GetField("CandidateList"),Is.Null);
        }
        [Test] public void ActualBackpackButtonTakesOutAndHolstersPocketWeaponInPlace()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>();bag.Pocket.TryAdd(ItemIds.ShortSword,1,1,out _,out _);
            var transfer=Game.Interface.GetUtility<IItemTransferPort>();transfer.ClickSlot(new InventorySlotRef(InventoryContainerKind.Pocket,0));
            bagUI.FullBtnUse.onClick.Invoke();Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.EqualTo(ItemIds.ShortSword));
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(1));
            Assert.That(bagUI.FullBtnUse.GetComponentInChildren<TMP_Text>().text,Is.EqualTo("收起"));
            bagUI.FullBtnUse.onClick.Invoke();Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.Zero);
            Assert.That(bag.Pocket.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(1));
        }
        [Test] public void PickerUsesOnlySupportedCarriedItemsAndPreservesSlots()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>();bag.Container.TryAdd(ItemIds.ShortSword,1,1,out _,out _);bag.Container.TryAdd(ItemIds.Wood,1,20,out _,out _);bag.Pocket.TryAdd(ItemIds.Bow,1,1,out _,out _);
            UIHelper.ShowUI<HeldItemPickerUICtrl>();var ui=Picker();Assert.That(ui.CandidateList.totalCount,Is.EqualTo(2));
            ui.CandidateList.GetDraggableItems()[0].GetComponent<InventorySlotBoxUI>().ClickButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.EqualTo(ItemIds.Bow));Assert.That(bag.Pocket.Slots.Count,Is.EqualTo(1));
            Assert.That(UIHelper.GetUICtrl<HeldItemPickerUICtrl>().IsShow(),Is.False);
        }
        [Test] public void FullBagCanHolsterThroughPicker()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>();bag.Container.TryAdd(ItemIds.ShortSword,12,1,out _,out _);
            Game.Interface.GetSystem<WeaponEquipmentSystem>().TryHold(new InventorySlotRef(InventoryContainerKind.PlayerBag,0),ItemIds.ShortSword,out _);
            UIHelper.ShowUI<HeldItemPickerUICtrl>();var ui=Picker();Assert.That(ui.CandidateList.totalCount,Is.EqualTo(12));ui.BtnHolster.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.Zero);Assert.That(bag.Container.Slots.Count,Is.EqualTo(12));
        }
        [Test] public void StalePickerCandidateCannotHoldSameTypeReplacement()
        {
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(ItemIds.ShortSword,1,1,out _,out _);UIHelper.ShowUI<HeldItemPickerUICtrl>();
            var oldButton=Picker().CandidateList.GetDraggableItems()[0].GetComponent<InventorySlotBoxUI>().ClickButton;
            bag.TryRemove(0,1,out _,out _);bag.TryAdd(ItemIds.ShortSword,1,1,out _,out _);oldButton.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.Zero);
        }
        [Test] public void GeneratedPrefabReferencesAndLocalizationResolve()
        {
            var translations=JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath,"Game/Config/Localization/zh-CN.json")));
            foreach(var path in new[]{"Inventory/Page/EquipmentManagement","Inventory/HeldItemPicker","Inventory/Bag","Player/PlayerConditionHud"})
            {
                var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/"+path+".prefab");Assert.That(root.GetComponent<ReferenceCollector>(),Is.Not.Null);
                var ui=root.GetComponent<BaseUI>();foreach(var field in ui.GetType().GetFields())if(typeof(Object).IsAssignableFrom(field.FieldType))Assert.That((Object)field.GetValue(ui),Is.Not.Null,path+":"+field.Name);
                foreach(var loc in root.GetComponentsInChildren<LocalizedText>(true)){Assert.That(translations[loc.Key],Is.Not.Null,loc.Key);Assert.That(loc.GetComponent<TMP_Text>().text,Is.EqualTo(translations[loc.Key].Value<string>()),path+":"+loc.name);}
            }
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/HeldItemPicker.prefab").GetComponent<HeldItemPickerUI>().CandidateList.itemPrefab),Is.True);
        }
        [Test] public void LanguageChangeRetainsClothingSelection()
        {
            bagUI.BtnPageEquipment.onClick.Invoke();bagUI.EquipmentPage.BtnFeet.onClick.Invoke();Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN","en-US"));
            Assert.That(bagUI.EquipmentPage.DetailNameText.text,Is.EqualTo(Loc.T("ui.equipment.part.feet")));
        }
        [Test] public void HostDestroyAndReopenRebindsBodyPage()
        {
            bagUI.BtnPageEquipment.onClick.Invoke();Object.DestroyImmediate(bagUI.gameObject);Assert.DoesNotThrow(()=>Game.Interface.SendEvent(new InventoryChangedEvent()));Assert.That(BagUICtrl.TryShow(out var error),Is.True,error);
            host=UIHelper.GetUICtrl<BagUICtrl>();bagUI=(BagUI)typeof(BaseUICtrl).GetField("BaseUI",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);bagUI.BtnPageEquipment.onClick.Invoke();
            Assert.That(bagUI.EquipmentPage.DetailNameText.text,Is.EqualTo(Loc.T("ui.equipment.part.head")));
        }
        [Test] public void BackpackWearAndEquipmentRemoveTransferRealItem()
        {
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(38,1,1,out _,out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,0));
            Assert.That(bagUI.FullBtnUse.GetComponentInChildren<TMP_Text>().text,Is.EqualTo("穿戴"));
            bagUI.FullBtnUse.onClick.Invoke();Assert.That(Game.Interface.GetModel<ClothingEquipmentModel>().GetItemId(ClothingSlot.Torso),Is.EqualTo(38));Assert.That(bag.GetTotalAmount(38),Is.Zero);
            bagUI.BtnPageEquipment.onClick.Invoke();bagUI.EquipmentPage.BtnTorso.onClick.Invoke();
            Assert.That(bagUI.EquipmentPage.DetailNameText.text,Is.EqualTo("红色连帽衫"));
            Assert.That(bagUI.EquipmentPage.CharacterPreview.texture,Is.Not.Null);
            bagUI.EquipmentPage.BtnUnequip.onClick.Invoke();Assert.That(bag.GetTotalAmount(38),Is.EqualTo(1));Assert.That(Game.Interface.GetModel<ClothingEquipmentModel>().GetItemId(ClothingSlot.Torso),Is.Zero);
        }
        [Test] public void StaleBackpackSelectionCannotWearSameTypeReplacement()
        {
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(38,1,1,out _,out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,0));
            bag.TryRemove(0,1,out _,out _);bag.TryAdd(38,1,1,out _,out _);bagUI.FullBtnUse.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<ClothingEquipmentModel>().GetItemId(ClothingSlot.Torso),Is.Zero);Assert.That(bag.GetTotalAmount(38),Is.EqualTo(1));
        }
        [Test] public void RepeatedShowHideReleasesPreviewAndDoesNotDuplicateUnequip()
        {
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(40,1,1,out _,out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,0));bagUI.FullBtnUse.onClick.Invoke();
            for(int i=0;i<3;i++){bagUI.BtnPageEquipment.onClick.Invoke();host.Hide();Assert.That(bagUI.EquipmentPage.CharacterPreview.texture,Is.Null);host.Show();}
            bagUI.BtnPageEquipment.onClick.Invoke();bagUI.EquipmentPage.BtnLegs.onClick.Invoke();bagUI.EquipmentPage.BtnUnequip.onClick.Invoke();
            Assert.That(bag.GetTotalAmount(40),Is.EqualTo(1));
        }
        [Test] public void StaleClothingSelectionCannotTakeOutReplacementWeapon()
        {
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;bag.TryAdd(38,1,1,out _,out _);
            Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,0));
            bag.TryRemove(0,1,out _,out _);bag.TryAdd(ItemIds.ShortSword,1,1,out _,out _);bagUI.FullBtnUse.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().HeldItemId,Is.Zero);Assert.That(bag.GetTotalAmount(ItemIds.ShortSword),Is.EqualTo(1));
        }
        [Test] public void NewPartButtonsWearAndRemoveRealBackpackItems()
        {
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var bag=Game.Interface.GetModel<PlayerBagModel>().Container;
            var page=bagUI.EquipmentPage;
            var buttons=new[]{page.BtnHead,page.BtnHands,page.BtnFeet,page.BtnBack,page.BtnAccessory};
            for(int i=0;i<buttons.Length;i++)
            {
                int id=42+i;bag.TryAdd(id,1,1,out _,out _);
                int index=System.Linq.Enumerable.Single(bag.Slots,p=>p.Value.ItemId==id).Key;
                Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,index));
                bagUI.FullBtnUse.onClick.Invoke();bagUI.BtnPageEquipment.onClick.Invoke();buttons[i].onClick.Invoke();
                Assert.That(page.BtnUnequip.gameObject.activeSelf,Is.True);
                Assert.That(page.DetailSourceText.text,Is.EqualTo(Loc.T("ui.clothing.equipped")));
                Assert.That(Game.Interface.GetModel<ClothingEquipmentModel>().GetItemId((ClothingSlot)(i+3)),Is.EqualTo(id));
                page.BtnUnequip.onClick.Invoke();Assert.That(bag.GetTotalAmount(id),Is.EqualTo(1));
            }
        }
        private HeldItemPickerUI Picker()=>(HeldItemPickerUI)typeof(BaseUICtrl).GetField("BaseUI",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(UIHelper.GetUICtrl<HeldItemPickerUICtrl>());
    }
}
