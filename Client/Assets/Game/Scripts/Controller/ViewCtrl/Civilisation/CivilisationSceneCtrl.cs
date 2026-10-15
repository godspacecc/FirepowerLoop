using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>在地图正式提交后绑定文明静态交互物并转发搜索反馈；不生成科技或修改物品。</summary>
    public sealed class CivilisationSceneCtrl : IController, IDisposable
    {
        private readonly List<CivilisationPointView> views = new List<CivilisationPointView>();
        /// <summary>当前已提交地图的 View 索引；不保存跨地图对象，采集反馈只刷新对应点位。</summary>
        private readonly Dictionary<string, CivilisationPointView> viewsById = new Dictionary<string, CivilisationPointView>(StringComparer.Ordinal);
        private readonly HashSet<CivilisationPointView> boundViews = new HashSet<CivilisationPointView>();
        private readonly List<IUnRegister> subscriptions = new List<IUnRegister>();
        private long mapInstanceId;
        public IArchitecture GetArchitecture() => Game.Interface;
        public void Initialize()
        {
            LifeCycle.RegisterLateUpdate(RefreshPrompts);
            subscriptions.Add(this.RegisterEvent<MapLoadingCompletedEvent>(_ => BindCommittedMap()));
            subscriptions.Add(this.RegisterEvent<MapCurrentReleasedEvent>(e => { if (e.MapInstanceId == mapInstanceId) Clear(); }));
            subscriptions.Add(this.RegisterEvent<CivilisationSearchFinishedEvent>(OnSearchFinished));
            subscriptions.Add(this.RegisterEvent<CivilisationHarvestStateRestoredEvent>(_ => RefreshHarvestStates()));
        }
        /// <summary>只在成功保存后更新耗尽资源；可重复水源保持可见，失败或回滚不隐藏碰撞。</summary>
        private void OnSearchFinished(CivilisationSearchFinishedEvent e)
        {
            var repeatable = IsRepeatable(e.PointId);
            if (e.Error == null && !repeatable)
                if (viewsById.TryGetValue(e.PointId, out var view) && view != null) view.RefreshHarvestPresentation(true);
            CivilisationSearchPointDefinition point = null;
            if (e.Error == null && this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _) &&
                CivilisationMapPointResolver.TryGetPoint(this.GetModel<MapSceneModel>().CurrentHandle, catalog, e.PointId, out point) && !string.IsNullOrEmpty(point.ChapterId))
            {
                Notify.ShowFloatTips(Loc.T("civilisation.story.recorded"));
                return;
            }
            if (e.Error == null && point != null && !string.IsNullOrEmpty(point.TechnologyId) && point.NameKey != "civilisation.point.fire")
            {
                Notify.ShowFloatTips(Loc.T("civilisation.reading.recorded"));
                return;
            }
            Notify.ShowFloatTips(e.Error == null ? Loc.T(e.RewardDropped ? "civilisation.harvest.done" :
                repeatable ? "civilisation.collect.done" : "civilisation.search.done") : Loc.T(e.Error));
        }
        private bool IsRepeatable(string id) => this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _) &&
            CivilisationMapPointResolver.TryGetPoint(this.GetModel<MapSceneModel>().CurrentHandle, catalog, id, out var point) && point.Repeatable;
        /// <summary>领域恢复或新远征重置后重新投影资源状态；不重新创建交互控制器。</summary>
        private void RefreshHarvestStates()
        {
            if (mapInstanceId != this.GetSystem<MapSceneSystem>().CurrentMapInstanceId) return;
            foreach (var view in views)
                if (view != null) view.RefreshHarvestPresentation(!IsRepeatable(view.PointId) && this.GetModel<CivilisationExpeditionModel>().HasVisited(view.PointId));
        }
        private void RefreshPrompts()
        {
            var focused = this.GetSystem<PlayerInteractionSystem>().FocusedTarget as CivilisationPointView;
            if (focused == null || !boundViews.Contains(focused)) return;
            var camera = this.GetSystem<PlayerCameraSystem>();
            if (camera == null || !camera.TryGetCameraPose(out var position, out var up)) return;
            focused.RefreshPrompt(position, up);
        }
        private void BindCommittedMap()
        {
            Clear();
            var map = this.GetModel<MapSceneModel>();
            if (map.CurrentMode != MapPreparationMode.OfflineFull || map.CurrentHandle?.Context == null) return;
            mapInstanceId = this.GetSystem<MapSceneSystem>().CurrentMapInstanceId;
            var generated = map.CurrentHandle as GeneratedValleyContentHandle;
            var root = generated != null ? generated.GeneratedRoot : map.CurrentHandle.Context.MapContentRoot;
            foreach (var view in root.GetComponentsInChildren<CivilisationPointView>(true))
            {
                // 生成资源中的隐藏原型和旧场景仅用于模板，不能获得当前地图交互权限。
                if (generated?.KitInstance != null && view.transform.IsChildOf(generated.KitInstance.transform)) continue;
                if (view.Kind == CivilisationPointKind.Search && this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _) &&
                    CivilisationMapPointResolver.TryGetPoint(map.CurrentHandle, catalog, view.PointId, out var definition))
                    view.ConfigureGeneratedPrompt(definition.NameKey);
                view.Bind(new CivilisationPointViewCtrl(view)); views.Add(view);
                boundViews.Add(view);
                if (view.Kind == CivilisationPointKind.Search && !string.IsNullOrEmpty(view.PointId)) viewsById.Add(view.PointId, view);
                view.RefreshHarvestPresentation(!IsRepeatable(view.PointId) && this.GetModel<CivilisationExpeditionModel>().HasVisited(view.PointId));
            }
        }
        private void Clear()
        {
            foreach (var view in views) if (view != null) view.Bind(null);
            views.Clear(); viewsById.Clear(); boundViews.Clear(); mapInstanceId = 0;
        }
        public void Dispose()
        {
            LifeCycle.UnregisterLateUpdate(RefreshPrompts);
            foreach (var subscription in subscriptions) subscription.UnRegister();
            subscriptions.Clear(); Clear();
        }
    }
}
