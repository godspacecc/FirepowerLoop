using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class InventorySlotBoxGenerationMigrationTests
    {
        private const string BoxPrefabPath = "Assets/Game/Prefabs/UI/Inventory/Box/InventorySlotBox.prefab";
        private static readonly (string Path, Type ViewType)[] InventoryPrefabs =
        {
            ("Assets/Game/Prefabs/UI/Inventory/Bag.prefab", typeof(BagUI)),
            ("Assets/Game/Prefabs/UI/Inventory/Warehouse.prefab", typeof(WarehouseUI)),
            ("Assets/Game/Prefabs/UI/Inventory/SearchContainer.prefab", typeof(SearchContainerUI))
        };

        [Test]
        public void BoxPrefabCollectorGeneratesPureFieldView()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);
            Assert.NotNull(prefab);

            var collector = prefab.GetComponent<ReferenceCollector>();
            Assert.NotNull(collector);
            Assert.That(
                collector.data.Select(item => item.key).ToArray(),
                Is.EquivalentTo(new[]
                {
                    "AmountText",
                    "ExpansionContentRoot",
                    "ExpansionButton",
                    "ExpansionLabelText",
                    "ClickButton",
                    "DisabledOverlay",
                    "DragFeedback",
                    "EmptyIconRoot",
                    "IconImage",
                    "ItemContentRoot",
                    "SelectedFrame"
                }));
            Assert.That(collector.data.Single(item => item.key == "ClickButton").TypeName, Is.EqualTo(typeof(Button).FullName));
            Assert.That(collector.data.Single(item => item.key == "IconImage").TypeName, Is.EqualTo(typeof(Image).FullName));
            Assert.That(collector.data.Single(item => item.key == "EmptyIconRoot").TypeName, Is.EqualTo(typeof(GameObject).FullName));

            var type = typeof(InventorySlotBoxUI);
            Assert.That(type.GetInterfaces().Contains(typeof(IPointerClickHandler)), Is.False);
            Assert.That(type.GetInterfaces().Contains(typeof(IBeginDragHandler)), Is.False);
            Assert.That(type.GetInterfaces().Contains(typeof(IEndDragHandler)), Is.False);
            Assert.That(type.GetInterfaces().Contains(typeof(IDropHandler)), Is.False);
            Assert.That(type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), Is.Empty);
            Assert.That(type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly), Is.Empty);
            Assert.IsNull(type.GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            Assert.IsNull(type.GetMethod("ClearActiveDrag", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        }

        [Test]
        public void BoxPrefabUsesIndependentExpansionAndOrdinaryContentReferences()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);
            var view = prefab.GetComponent<InventorySlotBoxUI>();

            Assert.NotNull(view);
            Assert.NotNull(view.ItemContentRoot);
            Assert.NotNull(view.ExpansionContentRoot);
            Assert.NotNull(view.ClickButton);
            Assert.NotNull(view.ExpansionButton);
            Assert.NotNull(view.AmountText);
            Assert.NotNull(view.ExpansionLabelText);
            Assert.AreNotSame(view.ItemContentRoot, view.ExpansionContentRoot);
            Assert.AreNotSame(view.ClickButton, view.ExpansionButton);
            Assert.AreNotSame(view.AmountText, view.ExpansionLabelText);
            Assert.That(view.ExpansionButton.transform.IsChildOf(view.ExpansionContentRoot.transform), Is.True);
            Assert.That(view.ExpansionLabelText.transform.IsChildOf(view.ExpansionContentRoot.transform), Is.True);
            Assert.That(view.AmountText.transform.IsChildOf(view.ItemContentRoot.transform), Is.True);
            Assert.That(view.IconImage.transform.IsChildOf(view.ItemContentRoot.transform), Is.True);
            Assert.That(view.EmptyIconRoot.transform.IsChildOf(view.ItemContentRoot.transform), Is.True);
        }

        [Test]
        public void GeneratedBoxViewContainsOnlyPrefabFieldsAndReuseResetClearsLiveState()
        {
            var source = File.ReadAllText(Path.Combine(
                ProjectRoot, "Assets/Game/Scripts/Controller/UI/Inventory/Box/InventorySlotBoxUI.cs"));
            Assert.That(source, Does.Not.Contain("InventorySlotBoxReuse"));
            Assert.That(source, Does.Not.Contain("using QFramework"));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<InventorySlotBoxUI>();
                view.ItemContentRoot.SetActive(true);
                view.ExpansionContentRoot.SetActive(true);
                view.ClickButton.interactable = true;
                view.ExpansionButton.interactable = true;
                view.IconImage.enabled = false;
                view.IconImage.gameObject.SetActive(true);
                view.AmountText.gameObject.SetActive(true);
                view.ExpansionLabelText.gameObject.SetActive(true);
                view.EmptyIconRoot.SetActive(true);
                view.SelectedFrame.SetActive(true);
                view.DisabledOverlay.SetActive(true);
                view.DragFeedback.SetActive(true);

                var clickCount = 0;
                var expansionCount = 0;
                view.ClickButton.onClick.AddListener(() => clickCount++);
                view.ExpansionButton.onClick.AddListener(() => expansionCount++);
                InvokeReuseReset(view);
                view.ClickButton.onClick.Invoke();
                view.ExpansionButton.onClick.Invoke();

                Assert.That(view.ItemContentRoot.activeSelf, Is.False);
                Assert.That(view.ExpansionContentRoot.activeSelf, Is.False);
                Assert.That(view.ClickButton.interactable, Is.False);
                Assert.That(view.ExpansionButton.interactable, Is.False);
                Assert.That(view.IconImage.enabled, Is.True);
                Assert.That(view.IconImage.gameObject.activeSelf, Is.False);
                Assert.That(view.AmountText.gameObject.activeSelf, Is.False);
                Assert.That(view.ExpansionLabelText.gameObject.activeSelf, Is.False);
                Assert.That(view.EmptyIconRoot.activeSelf, Is.False);
                Assert.That(view.SelectedFrame.activeSelf, Is.False);
                Assert.That(view.DisabledOverlay.activeSelf, Is.False);
                Assert.That(view.DragFeedback.activeSelf, Is.False);
                Assert.That(clickCount, Is.Zero);
                Assert.That(expansionCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void InventoryCollectionsReferenceSharedSlotBoxPrefab()
        {
            var boxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefabPath);
            Assert.NotNull(boxPrefab);

            foreach (var contract in InventoryPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(contract.Path);
                Assert.NotNull(prefab, contract.Path);
                var collector = prefab.GetComponent<ReferenceCollector>();
                Assert.NotNull(collector, contract.Path);

                var view = prefab.GetComponent(contract.ViewType);
                Assert.NotNull(view, contract.Path);
                var fieldName = contract.ViewType == typeof(WarehouseUI)
                    ? "WarehouseList"
                    : contract.ViewType == typeof(SearchContainerUI)
                        ? "SearchList"
                        : "PlayerBagList";
                var field = contract.ViewType.GetField(fieldName);
                Assert.NotNull(field, contract.Path);
                var entry = collector.data.Single(item => item.key == fieldName);
                Assert.That(entry.TypeName, Is.EqualTo(field.FieldType.FullName), contract.Path);

                var collection = field.GetValue(view);
                Assert.NotNull(collection, contract.Path);
                if (collection is CustomListView customList)
                {
                    Assert.AreSame(boxPrefab, customList.itemPrefab, contract.Path);
                }
                else if (collection is LoopGridLayoutGroup loopGrid)
                {
                    Assert.AreSame(boxPrefab, loopGrid.prefabSource.prefab, contract.Path);
                }
                else
                {
                    Assert.Fail($"{contract.Path} inventory collection type is unsupported.");
                }
            }
        }

        [Test]
        public void GenericDragAndDropEventControllersDispatchAndClearIndependently()
        {
            var go = new GameObject("event-source", typeof(RectTransform), typeof(Button));
            try
            {
                var button = go.GetComponent<Button>();
                var received = string.Empty;
                var data = new PointerEventData(EventSystem.current);

                button.OnBeginDragEvent(_ => received += "begin;");
                button.OnBeginDragEvent((_, args) => received += "begin:" + args.GetArg<string>(0) + ";", "slot-a");
                button.OnDragEvent(_ => received += "drag;");
                button.OnDragEvent((_, args) => received += "drag:" + args.GetArg<string>(0) + ";", "slot-b");
                button.OnEndDragEvent(_ => received += "end;");
                button.OnEndDragEvent((_, args) => received += "end:" + args.GetArg<string>(0) + ";", "slot-c");
                button.OnDropEvent(_ => received += "drop;");
                button.OnDropEvent((_, args) => received += "drop:" + args.GetArg<string>(0) + ";", "slot-d");

                ExecuteEvents.Execute(go, data, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.endDragHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.dropHandler);
                Assert.That(received, Is.EqualTo("begin;begin:slot-a;drag;drag:slot-b;end;end:slot-c;drop;drop:slot-d;"));

                received = string.Empty;
                button.RemoveAllOnDragEvents();
                ExecuteEvents.Execute(go, data, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.endDragHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.dropHandler);
                Assert.That(received, Is.EqualTo("drop;drop:slot-d;"));

                received = string.Empty;
                button.RemoveAllOnDropEvents();
                ExecuteEvents.Execute(go, data, ExecuteEvents.dropHandler);
                Assert.That(received, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void InventoryControllerOwnsSlotEventsWithoutViewFallbacks()
        {
            foreach (var file in new[] { "BagUICtrl.cs", "WarehouseUICtrl.cs", "SearchContainerUICtrl.cs" })
            {
                var source = File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game/Scripts/Controller/UICtrl/Inventory", file));
                var argumentName = file == "BagUICtrl.cs" ? "slot" : "view";
                Assert.That(source, Does.Contain($"OnPointerClickEvent(OnSlotClicked, {argumentName})"), file);
                Assert.That(source, Does.Contain($"OnBeginDragEvent(OnBeginDrag, {argumentName})"), file);
                Assert.That(source, Does.Contain($"OnDragEvent(OnDrag, {argumentName})"), file);
                Assert.That(source, Does.Contain($"OnDropEvent(OnDrop, {argumentName})"), file);
                Assert.That(source, Does.Contain($"OnEndDragEvent(OnEndDrag, {argumentName})"), file);
                Assert.That(source, Does.Contain("TryShowDragVisual"), file);
                Assert.That(source, Does.Contain("UpdateDragVisual"), file);
                Assert.That(source, Does.Not.Contain("InventorySlotBoxUI.ClearActiveDrag"), file);
                Assert.That(source, Does.Contain("InventorySlotBoxReuse.Reset(view)"), file);
            }
        }

        [Test]
        public void SearchRefreshBindsOnlyTheCachedSnapshotAndSeparatesRefillPolicies()
        {
            var source = File.ReadAllText(Path.Combine(
                ProjectRoot, "Assets/Game/Scripts/Controller/UICtrl/Inventory/SearchContainerUICtrl.cs"));
            var refreshCellStart = source.IndexOf("private void RefreshSearchCell", StringComparison.Ordinal);
            var nextMethod = source.IndexOf("private void BindSlotEvents", refreshCellStart, StringComparison.Ordinal);
            var refreshCell = source.Substring(refreshCellStart, nextMethod - refreshCellStart);

            Assert.That(source, Does.Contain("private void RefreshAll(bool refillFromTop = false)"));
            Assert.That(source, Does.Contain("snapshotByIndex[entry.SlotIndex] = entry;"));
            Assert.That(source, Does.Contain("UI.SearchList.RefreshCells(refillFromTop);"));
            Assert.That(source, Does.Contain("RefreshAll(true);"));
            Assert.That(refreshCell, Does.Not.Contain("TryGetSnapshot"));
            Assert.That(refreshCell, Does.Contain("snapshotByIndex.TryGetValue"));
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static void InvokeReuseReset(InventorySlotBoxUI view)
        {
            var type = typeof(InventorySlotBoxUI).Assembly.GetType("Game.InventorySlotBoxReuse");
            Assert.NotNull(type);
            var method = type.GetMethod("Reset", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(null, new object[] { view });
        }

        private static GameObject ResolveGameObject(UnityEngine.Object obj)
        {
            return obj switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => null
            };
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
