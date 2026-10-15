using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class MapManagementPageTests
    {
        private GameObject root;
        private MapManagementPageUI view;
        private MapManagementPageCtrl page;
        private Loader loader;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            LocalizationLanguages.ResetForTests();
            loader = new Loader(); ResourceLoader.SetLoaderForTests(loader);
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            architecture.RegisterModel(new MapSceneModel()); architecture.RegisterModel(new UnitModel());
            architecture.RegisterModel(new ExtractionModel()); architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new MapExplorationModel()); architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
                new Tables(name => new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")))));
            var loc = new LocalizationUtility(loadConfiguredTables: false); architecture.RegisterUtility(loc);
            loc.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            loc.LoadFromJson("en-US", File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            architecture.GetModel<LocalizationModel>().SetLanguageCode("zh-CN");
            architecture.RegisterSystem(new UnitSystem()); architecture.RegisterSystem(new MapDisplaySystem());
            architecture.RegisterSystem(new SaveSystem()); architecture.RegisterSystem(new MapSceneSystem(null,null,null,null,null,null));
            architecture.RegisterSystem(new MapExplorationSystem());
            architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            root = new GameObject("MapPageTestCanvas", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/MapManagement.prefab"), root.transform, false);
            view = instance.GetComponent<MapManagementPageUI>(); page = new MapManagementPageCtrl(); page.SetUI(view);
        }

        [TearDown]
        public void TearDown()
        {
            page.SetUI(null);
            Object.DestroyImmediate(root);
            Game.Interface.GetModel<SaveModel>().SetBase(null, false);
            TestReset.ResetArchitecture(); ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [Test]
        public void GeneratedReferencesResolveAndNoMapShowsAnHonestPlaceholder()
        {
            Assert.That(view.GetComponent<ReferenceCollector>(), Is.Not.Null);
            foreach (var field in typeof(MapManagementPageUI).GetFields())
                if (typeof(Object).IsAssignableFrom(field.FieldType)) Assert.That(field.GetValue(view), Is.Not.Null, field.Name);
            page.Show();
            Assert.That(view.MessageText.text, Is.EqualTo(Loc.T("ui.map.error.no_current_map_item")));
            Assert.That(loader.Requests.Count, Is.Zero);
        }

        [Test]
        public void NearbyImageSurvivesPageHideAndIsReleasedWithItsMapInstance()
        {
            var architecture = Game.Interface;
            var refresh = new MapImageRefreshCtrl(); architecture.RegisterUtility(refresh); refresh.Initialize();
            var content = new GameObject("ObservedMapFixture");
            var dynamicRoot = new GameObject("Dynamic"); dynamicRoot.transform.SetParent(content.transform);
            var context = content.AddComponent<MapContext>();
            typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, content);
            typeof(MapContext).GetField("dynamicObjectRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, dynamicRoot.transform);
            try
            {
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, new ObservedMapHandle(context));
                architecture.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(PlayerModel.LocalPlayerUnitId, 42,
                    UnitType.Player, UnitSource.Player, "", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _);
                Carry(ItemIds.BaseMap); page.Show(); loader.Complete(0);
                var remembered = view.MapImage.texture;
                Assert.That(remembered, Is.Not.SameAs(loader.Requests[0].Texture), "当前实例应显示独立的局部图片缓存");
                content.SetActive(false); LifeCycle.LateUpdate();
                Assert.That(view.MapImage.texture, Is.SameAs(remembered), "临时隐藏场景不应丢失图片记忆");
                content.SetActive(true);
                page.Hide(); page.Show(); loader.Complete(1);
                Assert.That(view.MapImage.texture, Is.SameAs(remembered), "关闭地图不应遗忘本次实例图片");
                architecture.GetModel<MapSceneModel>().Clear();
                architecture.SendEvent(new MapCurrentReleasedEvent(MapIds.Base, 42));
                Assert.That(remembered == null, Is.True, "卸载地图必须销毁旧实例纹理");
                Assert.That(view.MapImage.texture == null, Is.True, "卸载后的 Unity 纹理已失效");
                Assert.That(view.MapContentRect.gameObject.activeSelf, Is.False, "卸载后隐藏旧地图内容");
            }
            finally { page.Hide(); refresh.Dispose(); Object.DestroyImmediate(content); }
        }

        private sealed class ObservedMapHandle : IMapContentHandle
        {
            public ObservedMapHandle(MapContext context) { Context = context; }
            public string MapId => MapIds.Base;
            public MapContext Context { get; }
            public bool IsValid => Context != null;
        }

        [Test]
        public void ZoomAndPanNeverExploreAndHiddenPageReleasesExactlyOnce()
        {
            Carry(ItemIds.BaseMap); page.Show(); loader.Complete(0);
            Canvas.ForceUpdateCanvases(); LifeCycle.Update();
            var model = Game.Interface.GetModel<MapExplorationModel>(); var before = model.Capture().ToString();
            var size = view.MapContentRect.sizeDelta;
            view.MapScroll.OnScroll(new PointerEventData(null) { scrollDelta = Vector2.up,
                position = RectTransformUtility.WorldToScreenPoint(null, view.MapScroll.viewport.position) });
            Assert.That(view.MapContentRect.sizeDelta.x, Is.GreaterThan(size.x));
            view.MapContentRect.anchoredPosition = new Vector2(50, 50);
            Assert.That(model.Capture().ToString(), Is.EqualTo(before));
            Assert.That(view.FogImage.texture, Is.Not.Null);
            page.Hide(); page.Hide();
            Assert.That(loader.Releases[0], Is.EqualTo(1));
            Assert.That(view.MapImage.texture, Is.Null);
        }

        [Test]
        public void CurrentMapChangeAndLateCallbacksCannotPaintWrongOrHiddenPage()
        {
            Carry(ItemIds.BaseMap); Carry(ItemIds.SurvivalSampleMap); page.Show();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.SurvivalSample));
            Assert.That(page.SelectedMapId, Is.EqualTo(MapIds.SurvivalSample));
            loader.Complete(0); Assert.That(view.MapImage.texture, Is.Null);
            Assert.That(loader.Releases[0], Is.EqualTo(1));
            loader.Complete(1); Assert.That(view.MapImage.texture, Is.SameAs(loader.Requests[1].Texture));
            page.Hide(); Assert.That(loader.Releases[1], Is.EqualTo(1));
            page.Show(); page.Hide(); loader.Complete(2);
            Assert.That(view.MapImage.texture, Is.Null); Assert.That(loader.Releases[2], Is.EqualTo(1));
        }

        [Test]
        public void UnexploredMarkersStayHiddenUntilPlayerActuallyVisitsAndRecordsPersist()
        {
            Carry(ItemIds.MissionSampleMap);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            provider.TryGetMapDisplay(MapIds.MissionSample, out var display, out _);
            provider.TryGetMapSpawns(MapIds.MissionSample, out var spawns, out _);
            var position = spawns.First(s => s.ObjectType == MapSpawnObjectType.Extraction).LocalPosition;
            var units = Game.Interface.GetSystem<UnitSystem>();
            units.TryRegister(new UnitSnapshot(PlayerModel.LocalPlayerUnitId, 42, UnitType.Player, UnitSource.Player,
                "", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _);
            units.TryRegister(new UnitSnapshot(5001, 42, UnitType.ExtractionPoint, UnitSource.MapPlaced,
                "exit", position, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _);
            Game.Interface.GetModel<ExtractionModel>().Set(new ExtractionSnapshot(5001, 42, true, false));
            page.Show(); loader.Complete(0); Canvas.ForceUpdateCanvases(); LifeCycle.Update();
            Assert.That(view.ExtractionList.GetComponentsInChildren<MapMarkerBoxUI>(true).Length, Is.GreaterThan(0));
            Assert.That(view.ExtractionList.GetComponentsInChildren<MapMarkerBoxUI>(true).All(t => !t.MarkerRoot.activeSelf), Is.True);
            var model = Game.Interface.GetModel<MapExplorationModel>(); model.Reveal(display, position, 3);
            Game.Interface.SendEvent(new MapExplorationChangedEvent(display.MapId));
            Assert.That(view.ExtractionList.GetComponentsInChildren<MapMarkerBoxUI>(true).Any(t => t.MarkerRoot.activeSelf), Is.True);
            var fog = (Texture2D)view.FogImage.texture;
            MapDisplayProjection.TryWorldToNormalized(display, position, out var uv);
            Assert.That(fog.GetPixel(Mathf.Min(127,(int)(uv.x*128)),Mathf.Min(127,(int)(uv.y*128))).a, Is.Zero);
            page.Hide(); page.Show(); loader.Complete(1);
            Assert.That(model.IsExplored(display, position), Is.True);
        }

        [Test]
        public void MovementRecordsExplorationWithoutAnOpenPageAndRejectsOtherUnitsAndOldInstances()
        {
            var architecture = Game.Interface; var map = architecture.GetModel<MapSceneModel>();
            map.SetLoaded(MapIds.Base,null); architecture.GetModel<SaveModel>().SetBase(new BaseSaveData(),true);
            var unit = new UnitSnapshot(PlayerModel.LocalPlayerUnitId,42,UnitType.Player,UnitSource.Player,"",Vector3.zero,
                Quaternion.identity,UnitAuthorityMode.Local,0,true);
            architecture.GetSystem<UnitSystem>().TryRegister(unit,out _);
            var provider = architecture.GetUtility<IRuntimeConfigProvider>(); provider.TryGetMapDisplay(MapIds.Base,out var display,out _);
            var model = architecture.GetModel<MapExplorationModel>(); Assert.That(model.IsExplored(display,Vector3.zero),Is.True);
            var revision = model.Revision;
            var stale = new UnitSnapshot(PlayerModel.LocalPlayerUnitId,41,UnitType.Player,UnitSource.Player,"",new Vector3(10,0,10),
                Quaternion.identity,UnitAuthorityMode.Local,0,true);
            architecture.SendEvent(new UnitChangedEvent(unit,stale,UnitChangeMask.Position));
            var other = new UnitSnapshot(999,42,UnitType.Player,UnitSource.Player,"",new Vector3(10,0,10),
                Quaternion.identity,UnitAuthorityMode.Local,0,true);
            architecture.SendEvent(new UnitAddedEvent(other)); Assert.That(model.Revision,Is.EqualTo(revision));
            architecture.GetSystem<UnitSystem>().TryUpdateTransform(unit.UnitId,new Vector3(10,0,10),Quaternion.identity);
            Assert.That(model.IsExplored(display,new Vector3(10,0,10)),Is.True);
        }

        private static void Carry(int id) => Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(id,1,1,out _,out _);
        private sealed class Loader : IResourceLoader
        {
            public sealed class Request { public Action<Texture2D> Callback; public Texture2D Texture; }
            public readonly List<Request> Requests = new List<Request>();
            public readonly Dictionary<int,int> Releases = new Dictionary<int,int>();
            public T LoadAsset<T>(string path) where T : Object => typeof(T) == typeof(TextAsset)
                ? AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Config/Localization/" + path)
                : null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> callback) where T : Object
            {
                var index=Requests.Count; Requests.Add(new Request { Callback=t=>callback(t as T) }); Releases[index]=0;
                return new ResourceLoadHandle(index);
            }
            public void Complete(int index)
            {
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/UI/Map/MapBase.png");
                Requests[index].Texture=texture; Requests[index].Callback(texture);
            }
            public void Release(ResourceLoadHandle handle) { Releases[(int)handle.RawHandle]++; }
        }
    }
}
