using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>管理宿主内的当前地图分页；要求携带当前区域地图，拖动缩放不写探索或移动玩家。</summary>
    public sealed class MapManagementPageCtrl : BasePageCtrl
    {
        private MapManagementPageUI UI => (MapManagementPageUI)BaseUI;
        private MapViewportScrollRect Viewport => (MapViewportScrollRect)UI.MapScroll;
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();
        private MapOpenRequest request;
        private MapDisplaySnapshot snapshot;
        private LoadOwnership load;
        /// <summary>切换区域、隐藏或重绑时递增；晚到加载回调不得修改新区域或已隐藏页面。</summary>
        private int generation;
        private Texture2D fog;
        /// <summary>资源句柄提供的原始资料图；当前实例的动态缓存由地图 Controller 独立持有。</summary>
        private Texture2D sourceImage;
        private int fogRevision = -1;
        private float zoom = 1;
        private bool showExtractions = true;
        private Vector2 viewportSize;
        public string SelectedMapId => request.MapId;

        protected override void OnInit()
        {
            UI.ResetButton.onClick.AddListener(() => { SetZoom(1); UI.MapContentRect.anchoredPosition = Vector2.zero; });
            UI.ExtractionFilterButton.onClick.AddListener(() => { showExtractions = !showExtractions; RefreshMarkers(); });
            UI.ExtractionList.AddItemRefreshListener(RefreshExtraction);
        }

        protected override void OnShow()
        {
            Viewport.ZoomRequested += ZoomAt;
            subscriptions.Add(this.RegisterEvent<InventoryChangedEvent>(_ => ResolveCurrentMap()));
            subscriptions.Add(this.RegisterEvent<MapLoadingCompletedEvent>(_ => ResolveCurrentMap()));
            subscriptions.Add(this.RegisterEvent<MapCurrentReleasedEvent>(_ => ResolveCurrentMap()));
            subscriptions.Add(this.RegisterEvent<MapExplorationChangedEvent>(e => { if (e.MapId == request.MapId) RefreshSnapshot(); }));
            subscriptions.Add(this.RegisterEvent<UnitAddedEvent>(_ => RefreshSnapshot()));
            subscriptions.Add(this.RegisterEvent<UnitRemovedEvent>(_ => RefreshSnapshot()));
            subscriptions.Add(this.RegisterEvent<UnitChangedEvent>(e => { if (e.Current.UnitType == UnitType.Player || e.Current.UnitType == UnitType.ExtractionPoint) RefreshSnapshot(); }));
            subscriptions.Add(this.RegisterEvent<ExtractionAvailabilityChangedEvent>(_ => RefreshSnapshot()));
            subscriptions.Add(this.RegisterEvent<ExtractionCreatedEvent>(_ => RefreshSnapshot()));
            subscriptions.Add(this.RegisterEvent<LanguageChangedEvent>(_ => { RefreshTitle(); RefreshSnapshot(); }));
            LifeCycle.RegisterUpdate(RefreshViewport);
            ResolveCurrentMap();
        }

        /// <summary>解除显示订阅，先清空纹理引用，再释放本页句柄和运行时迷雾纹理；不清除探索记录。</summary>
        protected override void OnHide()
        {
            foreach (var item in subscriptions) item.UnRegister();
            subscriptions.Clear(); LifeCycle.UnregisterUpdate(RefreshViewport);
            Viewport.ZoomRequested -= ZoomAt; Viewport.ResetGesture(); ClearLoad();
            if (fog != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(fog);
                else UnityEngine.Object.DestroyImmediate(fog);
            }
            fog = null; fogRevision = -1;
        }

        /// <summary>背包或场景变化时只解析当前地图；缺少对应道具时显示占位，不回退到其他地图。</summary>
        private void ResolveCurrentMap()
        {
            if (!IsShow()) return;
            if (!this.GetSystem<MapDisplaySystem>().TryResolveCurrentMap(out var selected, out var code, out _))
            {
                ClearLoad(); request = default; UI.RegionText.text = Loc.T("ui.management.map"); UI.ProgressText.text = string.Empty;
                SetMessage(code == MapOpenFailureCode.NoCurrentMapItem
                    ? "ui.map.error.no_current_map_item" : "ui.map.error.unavailable"); return;
            }
            if (load != null && selected.MapId == request.MapId) { request = selected; RefreshSnapshot(); }
            else LoadRegion(selected);
        }

        /// <summary>只接受当前区域地图道具；不消耗物品，异地道具和隐藏页面不改变当前显示。</summary>
        public bool TrySelectItem(int itemId, out string error)
        {
            if (!this.GetSystem<MapDisplaySystem>().TryResolveCarriedItem(itemId, out var next, out _, out error)) return false;
            if (next.MapId != this.GetModel<MapSceneModel>().CurrentMapId)
            { error = Loc.T("ui.map.error.not_current_region"); return false; }
            if (!IsShow()) { error = "地图分页未显示"; return false; }
            if (request.MapId == next.MapId && load != null) { request = next; RefreshSnapshot(); }
            else LoadRegion(next);
            error = null;
            return true;
        }

        private void LoadRegion(MapOpenRequest next)
        {
            ClearLoad(); request = next; zoom = 1; viewportSize = Vector2.zero; fogRevision = -1;
            UI.MapContentRect.anchoredPosition = Vector2.zero;
            if (!this.GetSystem<MapDisplaySystem>().TryBuildSnapshot(request, out snapshot, out _, out _))
            { SetMessage("ui.map.error.unavailable"); return; }
            RefreshTitle(); SetMessage("ui.map.loading");
            var owner = new LoadOwnership(ResourceLoader.Current); load = owner; var expected = generation;
            try
            {
                owner.Start(snapshot.Display.ImageAddress, texture =>
                {
                    if (!IsShow() || expected != generation || !ReferenceEquals(load, owner)) { owner.Release(); return; }
                    if (texture == null) { owner.Release(); SetMessage("ui.map.error.unavailable"); return; }
                    sourceImage = texture;
                    UI.MapImage.texture = this.GetUtility<MapImageRefreshCtrl>()?.ResolveTexture(request.MapId, texture) ?? texture;
                    UI.MessageText.gameObject.SetActive(false); RefreshSnapshot();
                });
            }
            catch (Exception) { owner.Release(); SetMessage("ui.map.error.unavailable"); }
        }

        private void RefreshSnapshot()
        {
            if (!IsShow() || string.IsNullOrEmpty(request.MapId)) return;
            if (!this.GetSystem<MapDisplaySystem>().TryBuildSnapshot(request, out snapshot, out _, out _))
            { ClearLoad(); SetMessage("ui.map.error.unavailable"); return; }
            // 实例缓存可能已在卸载事件中销毁，仍用有效的资料图恢复界面。
            RefreshTitle(); if (sourceImage == null) return;
            UI.MapContentRect.gameObject.SetActive(true); RefreshLayout(); RefreshFog(); RefreshMarkers();
        }

        private void RefreshTitle()
        {
            if (string.IsNullOrEmpty(request.MapId)) return;
            if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(request.MapItemId, out var item, out _))
                UI.RegionText.text = string.IsNullOrEmpty(item.NameKey) ? item.Name : Loc.T(item.NameKey);
            UI.ProgressText.text = snapshot == null ? string.Empty : Loc.T("ui.map.explored",
                Mathf.RoundToInt(this.GetModel<MapExplorationModel>().GetExploredFraction(snapshot.Display) * 100));
        }

        /// <summary>探索位图仅转为表现纹理；未探索区域不透明，已探索区域透明，纹理由本分页释放。</summary>
        private void RefreshFog()
        {
            var model = this.GetModel<MapExplorationModel>();
            if (fog != null && fogRevision == model.Revision) return;
            var size = MapExplorationModel.Resolution;
            if (fog == null) fog = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "MapExplorationFog", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(27, 32, 35, model.IsCellExplored(snapshot.Display, x, y) ? (byte)0 : (byte)255);
            fog.SetPixels32(pixels); fog.Apply(false); UI.FogImage.texture = fog; fogRevision = model.Revision;
        }

        private void SetZoom(float value) { zoom = Mathf.Clamp(value, 1, 4); viewportSize = Vector2.zero; RefreshLayout(); }

        /// <summary>在指针或双指中点缩放，保持该位置的地图内容不动；关闭、加载中和非有限倍率忽略。</summary>
        private void ZoomAt(float factor, Vector2 screenPosition)
        {
            if (!IsShow() || sourceImage == null || snapshot == null || factor <= 0 || float.IsNaN(factor) || float.IsInfinity(factor)) return;
            var canvas = UI.GetComponentInParent<UnityEngine.Canvas>();
            var camera = canvas != null && canvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!UnityEngine.RectTransformUtility.ScreenPointToLocalPointInRectangle(UI.MapScroll.viewport, screenPosition, camera, out var point)) return;
            var previous = zoom;
            var position = UI.MapContentRect.anchoredPosition;
            UI.MapScroll.StopMovement();
            SetZoom(zoom * factor);
            UI.MapContentRect.anchoredPosition = point + (position - point) * (zoom / previous);
        }

        private void RefreshViewport()
        { RefreshLayout(); if (sourceImage != null) Viewport.PollPinch(); }
        private void RefreshLayout()
        {
            if (!IsShow() || snapshot == null || sourceImage == null) return;
            // 切换、卸载或关闭后不得继续引用已经释放的实例缓存。
            UI.MapImage.texture = this.GetUtility<MapImageRefreshCtrl>()?.ResolveTexture(request.MapId, sourceImage) ?? sourceImage;
            var size = UI.MapScroll.viewport.rect.size;
            if (size.x <= 0 || size.y <= 0 || size == viewportSize) return;
            viewportSize = size;
            var fit = Mathf.Min(size.x / snapshot.Display.Width, size.y / snapshot.Display.Height);
            UI.MapContentRect.sizeDelta = new Vector2(snapshot.Display.Width, snapshot.Display.Height) * fit * zoom;
            RefreshMarkers();
        }

        private void RefreshMarkers()
        {
            if (snapshot == null || UnityObjectState.IsMissing(UI)) return;
            var player = snapshot.Player;
            var visible = player.HasValue && MapDisplayProjection.TryWorldToNormalized(snapshot.Display, player.Value.WorldPosition, out _);
            UI.PlayerArrowRoot.SetActive(visible);
            if (visible)
            {
                MapDisplayProjection.TryWorldToNormalized(snapshot.Display, player.Value.WorldPosition, out var uv);
                UI.PlayerArrowRect.anchoredPosition = ToAnchored(uv); UI.PlayerArrowRect.localRotation = Quaternion.Euler(0, 0, -player.Value.Yaw);
            }
            UI.ExtractionList.totalCount = snapshot.Extractions.Count; UI.ExtractionList.RefreshCells();
            UI.ExtractionFilterButton.targetGraphic.color = showExtractions ? new Color(.32f,.38f,.32f) : new Color(.19f,.23f,.24f);
        }
        private void RefreshExtraction(Transform item, int index)
        {
            var box = item.GetComponent<MapMarkerBoxUI>(); var marker = snapshot.Extractions[index];
            var visible = showExtractions && this.GetModel<MapExplorationModel>().IsExplored(snapshot.Display, marker.WorldPosition) &&
                MapDisplayProjection.TryWorldToNormalized(snapshot.Display, marker.WorldPosition, out _);
            box.MarkerRoot.SetActive(visible); box.MarkerImage.enabled = visible; box.MarkerImage.raycastTarget = false;
            box.MarkerImage.color = marker.IsLive ? new Color(.28f,.83f,.41f) : new Color(.69f,.71f,.74f);
            if (visible) { MapDisplayProjection.TryWorldToNormalized(snapshot.Display, marker.WorldPosition, out var uv); ((RectTransform)item).anchoredPosition = ToAnchored(uv); }
        }
        private Vector2 ToAnchored(Vector2 uv) => (uv - new Vector2(.5f,.5f)) * UI.MapContentRect.rect.size;
        private void SetMessage(string key)
        { UI.MapContentRect.gameObject.SetActive(false); UI.MessageText.gameObject.SetActive(true); UI.MessageText.text = Loc.T(key); }
        private void ClearLoad()
        {
            generation++;
            sourceImage = null;
            if (!UnityObjectState.IsMissing(UI))
            { UI.MapImage.texture = null; UI.FogImage.texture = null; UI.ExtractionList.ClearCells(); UI.PlayerArrowRoot.SetActive(false); }
            var owner = load; load = null; owner?.Release(); snapshot = null;
        }
        private sealed class LoadOwnership
        {
            private readonly IResourceLoader loader; private ResourceLoadHandle handle;
            private bool returned, releaseRequested, released;
            public LoadOwnership(IResourceLoader loader) { this.loader = loader; }
            public void Start(string address, Action<Texture2D> callback)
            { try { handle = loader.LoadAssetAsync(address, callback); } finally { returned = true; if (releaseRequested) Release(); } }
            public void Release()
            { if (released) return; if (!returned) { releaseRequested = true; return; } released = true; if (handle.IsValid) loader.Release(handle); }
        }
    }
}
