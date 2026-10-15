using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class UiCollectionComponentsTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var go in Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.StartsWith("UiCollectionTest"))
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void CustomGrid_RefreshReusesOwnedItemsAndPreservesStaticChildren()
        {
            var root = new GameObject("UiCollectionTestGrid", typeof(RectTransform), typeof(CustomGridLayoutGroup));
            var prefab = new GameObject("UiCollectionTestGridItem", typeof(RectTransform));
            var staticItem = new GameObject("UiCollectionTestStatic", typeof(RectTransform));
            staticItem.transform.SetParent(root.transform, false);
            var grid = root.GetComponent<CustomGridLayoutGroup>();
            var refreshed = new List<int>();
            grid.gridPrefab = prefab;
            grid.totalCount = 3;
            grid.AddItemRefreshListener((_, index) => refreshed.Add(index));

            grid.RefreshCells();
            var first = grid.GetDraggableItems().ToArray();
            var foreignItem = new GameObject("UiCollectionTestForeign", typeof(RectTransform));
            foreignItem.transform.SetParent(root.transform, false);
            foreignItem.SetActive(false);

            refreshed.Clear();
            grid.RefreshCells();
            var second = grid.GetDraggableItems().ToArray();
            Assert.That(second.Select(item => item.GetInstanceID()),
                Is.EqualTo(first.Select(item => item.GetInstanceID())));
            Assert.That(refreshed, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(root.transform.Cast<Transform>().Take(2),
                Is.EqualTo(new[] { staticItem.transform, foreignItem.transform }));
            Assert.That(staticItem.activeSelf, Is.True);
            Assert.That(foreignItem.activeSelf, Is.False);

            grid.totalCount = 1;
            grid.RefreshCells();
            Assert.That(grid.GetDraggableItems(), Is.EqualTo(new[] { first[0] }));
            Assert.That(root.transform.childCount, Is.EqualTo(5));

            grid.totalCount = 3;
            grid.RefreshCells();
            Assert.That(grid.GetDraggableItems().Select(item => item.GetInstanceID()),
                Is.EqualTo(first.Select(item => item.GetInstanceID())));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomGrid_LoadAndClearClampCountAndReuseCache()
        {
            var root = new GameObject("UiCollectionTestGridLoad", typeof(RectTransform), typeof(CustomGridLayoutGroup));
            var prefab = new GameObject("UiCollectionTestGridLoadItem", typeof(RectTransform));
            var grid = root.GetComponent<CustomGridLayoutGroup>();
            grid.gridPrefab = prefab;
            grid.AddItemRefreshListener((_, _) => { });

            grid.LoadAndFillGrid(2);
            var createdIds = grid.GetDraggableItems().Select(item => item.GetInstanceID()).ToArray();
            Assert.That(grid.totalCount, Is.EqualTo(2));

            grid.ClearGrid();
            grid.ClearGrid();
            Assert.That(grid.totalCount, Is.Zero);
            Assert.That(grid.GetDraggableItems(), Is.Empty);
            Assert.That(root.transform.childCount, Is.EqualTo(2));

            grid.LoadAndFillGrid(2);
            Assert.That(grid.GetDraggableItems().Select(item => item.GetInstanceID()), Is.EqualTo(createdIds));

            grid.LoadAndFillGrid(-4);
            Assert.That(grid.totalCount, Is.Zero);
            Assert.That(grid.GetDraggableItems(), Is.Empty);
            Assert.That(root.transform.childCount, Is.EqualTo(2));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomGrid_InvalidConfigurationPreservesVisibleContentAndHierarchy()
        {
            var root = new GameObject("UiCollectionTestGridInvalid", typeof(RectTransform), typeof(CustomGridLayoutGroup));
            var staticItem = new GameObject("UiCollectionTestGridInvalidStatic", typeof(RectTransform));
            staticItem.transform.SetParent(root.transform, false);
            var prefab = new GameObject("UiCollectionTestGridInvalidItem", typeof(RectTransform));
            var grid = root.GetComponent<CustomGridLayoutGroup>();
            grid.gridPrefab = prefab;
            grid.totalCount = 2;
            grid.AddItemRefreshListener((_, _) => { });
            grid.RefreshCells();

            var childIds = ChildIds(root.transform);
            var activeStates = ChildActiveStates(root.transform);
            grid.gridPrefab = null;
            grid.totalCount = 1;
            LogAssert.Expect(LogType.Error, "[CustomGridLayoutGroup] 网格预制体缺失。");
            grid.RefreshCells();
            Assert.That(ChildIds(root.transform), Is.EqualTo(childIds));
            Assert.That(ChildActiveStates(root.transform), Is.EqualTo(activeStates));

            grid.gridPrefab = prefab;
            grid.AddItemRefreshListener(null);
            LogAssert.Expect(LogType.Error, "[CustomGridLayoutGroup] 刷新监听缺失。");
            grid.RefreshCells();
            Assert.That(ChildIds(root.transform), Is.EqualTo(childIds));
            Assert.That(ChildActiveStates(root.transform), Is.EqualTo(activeStates));
            Assert.That(staticItem.transform.GetSiblingIndex(), Is.Zero);
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomGrid_DraggableItemsContainOnlyActiveLiveOwnedItems()
        {
            var root = new GameObject("UiCollectionTestGridDrag", typeof(RectTransform), typeof(CustomGridLayoutGroup));
            var staticItem = new GameObject("UiCollectionTestGridDragStatic", typeof(RectTransform));
            staticItem.transform.SetParent(root.transform, false);
            var prefab = new GameObject("UiCollectionTestGridDragItem", typeof(RectTransform));
            var grid = root.GetComponent<CustomGridLayoutGroup>();
            grid.gridPrefab = prefab;
            grid.totalCount = 3;
            grid.AddItemRefreshListener((_, _) => { });
            grid.RefreshCells();
            var owned = grid.GetDraggableItems().ToArray();

            grid.totalCount = 2;
            grid.RefreshCells();
            Assert.That(grid.GetDraggableItems(), Is.EqualTo(owned.Take(2)));
            Assert.That(grid.GetDraggableItems().Contains(staticItem.transform), Is.False);

            Object.DestroyImmediate(owned[0].gameObject);
            Assert.That(grid.GetDraggableItems(), Is.EqualTo(new[] { owned[1] }));

            grid.ClearGrid();
            Assert.That(grid.GetDraggableItems(), Is.Empty);
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomGrid_DestroyingComponentDestroysOnlyOwnedItems()
        {
            var root = new GameObject("UiCollectionTestGridDestroy", typeof(RectTransform), typeof(CustomGridLayoutGroup));
            var staticItem = new GameObject("UiCollectionTestGridDestroyStatic", typeof(RectTransform));
            staticItem.transform.SetParent(root.transform, false);
            var foreignItem = new GameObject("UiCollectionTestGridDestroyForeign", typeof(RectTransform), typeof(CanvasGroup));
            foreignItem.transform.SetParent(root.transform, false);
            var prefab = new GameObject("UiCollectionTestGridDestroyItem", typeof(RectTransform));
            var grid = root.GetComponent<CustomGridLayoutGroup>();
            grid.gridPrefab = prefab;
            grid.totalCount = 2;
            grid.AddItemRefreshListener((_, _) => { });
            grid.RefreshCells();
            var owned = grid.GetDraggableItems().Select(item => item.gameObject).ToArray();

            Object.DestroyImmediate(grid);

            Assert.That(owned.All(item => item == null), Is.True);
            Assert.That(staticItem, Is.Not.Null);
            Assert.That(foreignItem, Is.Not.Null);
            Assert.That(foreignItem.GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(root.transform.Cast<Transform>(),
                Is.EqualTo(new[] { staticItem.transform, foreignItem.transform }));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomGrid_InspectorIsRuntimeOnlyAndProductionPrefabsDoNotUseGrid()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var inspectorSource = File.ReadAllText(Path.Combine(
                projectRoot, "Assets/Editor/UI/CustomGridLayoutGroupEditor.cs"));

            Assert.That(inspectorSource, Does.Not.Contain("GUILayout.Button"));
            Assert.That(inspectorSource, Does.Not.Contain(".LoadAndFillGrid("));
            Assert.That(inspectorSource, Does.Not.Contain(".RefreshCells("));
            Assert.That(inspectorSource, Does.Not.Contain(".ClearGrid("));
            Assert.That(inspectorSource, Does.Not.Contain("Instantiate("));
            Assert.That(inspectorSource, Does.Not.Contain("Destroy("));
            Assert.That(inspectorSource, Does.Not.Contain("DestroyImmediate("));
            Assert.That(inspectorSource, Does.Contain("列表项仅在运行时创建和复用"));

            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/UI" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            Assert.That(prefabPaths, Is.Not.Empty);
            var grids = prefabPaths
                .Select(path => new
                {
                    Path = path,
                    Grid = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                        .GetComponentInChildren<CustomGridLayoutGroup>(true)
                })
                .Where(entry => entry.Grid != null)
                .Select(entry => entry.Path)
                .ToArray();
            Assert.That(grids, Is.Empty,
                "生产 UI Prefab 不得挂接 CustomGridLayoutGroup: " + string.Join(", ", grids));
        }

        [Test]
        public void CustomList_RefreshReusesCreatedItems()
        {
            var root = new GameObject("UiCollectionTestList", typeof(RectTransform));
            var prefab = new GameObject("UiCollectionTestListItem", typeof(RectTransform));
            var list = root.AddComponent<CustomListView>();
            list.itemPrefab = prefab;
            list.totalCount = 3;
            list.AddItemRefreshListener((_, _) => { });

            list.RefreshCells();
            var first = ActiveChildren(root.transform);
            Assert.That(first.Length, Is.EqualTo(3));

            list.RefreshCells();
            var second = ActiveChildren(root.transform);
            Assert.That(second.Select(item => item.GetInstanceID()),
                Is.EqualTo(first.Select(item => item.GetInstanceID())));
            Assert.That(root.transform.childCount, Is.EqualTo(3));

            list.totalCount = 1;
            list.RefreshCells();
            Assert.That(ActiveChildren(root.transform).Length, Is.EqualTo(1));
            Assert.That(root.transform.childCount, Is.EqualTo(3));

            list.totalCount = 3;
            list.RefreshCells();
            Assert.That(ActiveChildren(root.transform).Length, Is.EqualTo(3));
            Assert.That(root.transform.childCount, Is.EqualTo(3));

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomList_ClearDeactivatesCacheAndNegativeCountMeansZero()
        {
            var root = new GameObject("UiCollectionTestListClear", typeof(RectTransform));
            var prefab = new GameObject("UiCollectionTestListClearItem", typeof(RectTransform));
            var list = root.AddComponent<CustomListView>();
            list.itemPrefab = prefab;
            list.totalCount = 2;
            list.RefreshCells();

            list.ClearCells();
            Assert.That(list.totalCount, Is.Zero);
            Assert.That(ActiveChildren(root.transform), Is.Empty);
            Assert.That(root.transform.childCount, Is.EqualTo(2));

            list.totalCount = -5;
            list.RefreshCells();
            Assert.That(ActiveChildren(root.transform), Is.Empty);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void CustomList_MissingPrefabPreservesCurrentActiveItems()
        {
            var root = new GameObject("UiCollectionTestListMissing", typeof(RectTransform));
            var prefab = new GameObject("UiCollectionTestListMissingItem", typeof(RectTransform));
            var list = root.AddComponent<CustomListView>();
            list.itemPrefab = prefab;
            list.totalCount = 2;
            list.RefreshCells();

            list.itemPrefab = null;
            list.totalCount = 1;
            LogAssert.Expect(LogType.Error, "[CustomListView] 列表元素预制体缺失。");
            list.RefreshCells();

            Assert.That(ActiveChildren(root.transform).Length, Is.EqualTo(2));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void LoopScroll_Exposes2DVirtualizedContract()
        {
            var type = typeof(LoopScrollRectBase);

            Assert.That(typeof(IInitializePotentialDragHandler).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(IBeginDragHandler).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(IEndDragHandler).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(IDragHandler).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(IScrollHandler).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(ICanvasElement).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(ILayoutElement).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(ILayoutGroup).IsAssignableFrom(type), Is.True);
            Assert.That(type.GetMethod("RefillCells"), Is.Not.Null);
            Assert.That(type.GetMethod("RefillCellsFromEnd"), Is.Not.Null);
            Assert.That(type.GetMethod("SrollToCell"), Is.Not.Null);
            Assert.That(type.GetMethod("SrollToCellWithinTime"), Is.Not.Null);
            Assert.That(type.GetMethod("GetFirstItem"), Is.Not.Null);
            Assert.That(type.GetMethod("GetLastItem"), Is.Not.Null);
            Assert.That(type.GetProperty("normalizedPosition"), Is.Not.Null);
        }

        [Test]
        public void LoopScroll_EditorCompanionTypesArePresent()
        {
            Assert.That(System.Type.GetType("UnityEngine.UI.LoopScrollRectDefaultControls, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("UnityEngine.UI.SGDefaultControls, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("UnityEditor.UI.SGMenuOptions, Assembly-CSharp-Editor"), Is.Not.Null);
            Assert.That(System.Type.GetType("LoopScrollRectInspector, Assembly-CSharp-Editor"), Is.Not.Null);
        }

        [Test]
        public void LoopGrid_MissingWiringStopsWithoutCreatingObjectsOrComponents()
        {
            var root = new GameObject("UiCollectionTestLoopMissing", typeof(RectTransform));
            root.SetActive(false);
            var grid = root.AddComponent<LoopGridLayoutGroup>();

            root.SetActive(true);
            LogAssert.Expect(LogType.Error,
                "[LoopGridLayoutGroup] prefabSource.prefab 为空，无法刷新网格。");
            grid.RefreshCells();

            Assert.That(root.transform.childCount, Is.Zero);
            Assert.That(root.GetComponents<Component>().Length, Is.EqualTo(2));
        }

        [Test]
        public void LoopGridAndInspector_DoNotMutateStaticChildrenForEditPreview()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var gridSource = File.ReadAllText(Path.Combine(
                projectRoot, "Assets/Game/Scripts/Utility/UI/LoopScrollRect/LoopGridLayoutGroup.cs"));
            var inspectorSource = File.ReadAllText(Path.Combine(
                projectRoot, "Assets/Editor/UI/LoopScroll/LoopScrollRectInspector.cs"));

            Assert.That(gridSource, Does.Not.Contain("Instantiate(prefabSource.prefab"));
            Assert.That(gridSource, Does.Not.Contain("DestroyImmediate"));
            Assert.That(gridSource, Does.Not.Contain("new GameObject"));
            Assert.That(gridSource, Does.Not.Contain(".AddComponent<"));
            Assert.That(inspectorSource, Does.Not.Contain("ClearGrid"));
            Assert.That(inspectorSource, Does.Not.Contain("RefreshGrid"));
        }

        [Test]
        public void LoopGrid_RefreshWithoutRefillPreservesContentPosition()
        {
            var root = new GameObject("UiCollectionTestLoopPosition", typeof(RectTransform));
            root.SetActive(false);
            var grid = root.AddComponent<LoopGridLayoutGroup>();
            grid.prefabSource.prefab = new GameObject("UiCollectionTestLoopItem", typeof(RectTransform));
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchoredPosition = new Vector2(0f, 137f);

            SetLoopGridWiring(grid, viewport.GetComponent<RectTransform>(), contentRect);
            root.SetActive(true);
            contentRect.anchoredPosition = new Vector2(0f, 137f);
            grid.RefreshCells(false);

            Assert.That(contentRect.anchoredPosition, Is.EqualTo(new Vector2(0f, 137f)));
            Object.DestroyImmediate(grid.prefabSource.prefab);
        }

        private static void SetLoopGridWiring(
            LoopGridLayoutGroup grid, RectTransform viewport, RectTransform content)
        {
            grid.viewport = viewport;
            grid.content = content;
        }

        private static GameObject[] ActiveChildren(Transform root)
        {
            return root.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .Select(child => child.gameObject)
                .ToArray();
        }

        private static int[] ChildIds(Transform root)
        {
            return root.Cast<Transform>()
                .Select(child => child.GetInstanceID())
                .ToArray();
        }

        private static bool[] ChildActiveStates(Transform root)
        {
            return root.Cast<Transform>()
                .Select(child => child.gameObject.activeSelf)
                .ToArray();
        }
    }
}
