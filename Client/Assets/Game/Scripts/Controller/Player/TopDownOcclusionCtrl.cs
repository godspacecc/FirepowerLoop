using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>地图玩家相机拥有的俯视遮挡检测器；只刷新同 Scene 中带显式表现标记的物件，不拥有物件或主相机。</summary>
    public sealed class TopDownOcclusionCtrl : IDisposable
    {
        private readonly Camera camera;
        private readonly Transform player;
        private readonly HashSet<CameraOccluderView> tracked = new();
        private readonly List<CameraOccluderView> entries = new();
        private readonly List<CameraOccluderView> previous = new();
        /// <summary>静止环境按32米格登记完整树冠范围；缓存仅属于此相机，地图卸载时释放。</summary>
        private const float CellMeters = 32f;
        private readonly Dictionary<Vector2Int, List<CameraOccluderView>> cells = new();
        private readonly List<CameraOccluderView> dynamicViews = new();
        private readonly HashSet<CameraOccluderView> candidates = new();
        private int registryVersion = -1;
        private UnityEngine.SceneManagement.Scene indexedScene;
        public TopDownOcclusionCtrl(Camera camera, Transform player) { this.camera = camera; this.player = player; }

        /// <summary>相机跟随完成后调用；显式物件的渲染包围盒覆盖树冠，排除其他地图实例。</summary>
        public void Tick(float deltaTime)
        {
            if (camera == null || player == null) { Dispose(); return; }
            var from = camera.transform.position;
            var offset = player.position + Vector3.up * 1.2f - from;
            var sight = new Ray(from, offset.normalized);
            RebuildIndexIfNeeded();
            candidates.Clear();
            var end = player.position + Vector3.up * 1.2f;
            var min = Cell(Vector3.Min(from, end) - Vector3.one);
            var max = Cell(Vector3.Max(from, end) + Vector3.one);
            // 视线所跨分区只取一次；宽树冠可占多个格，使用集合去重。
            for (var x = min.x; x <= max.x; x++)
                for (var z = min.y; z <= max.y; z++)
                    if (cells.TryGetValue(new Vector2Int(x, z), out var bucket))
                        foreach (var view in bucket) candidates.Add(view);
            foreach (var view in dynamicViews) candidates.Add(view);
            previous.Clear(); previous.AddRange(tracked); tracked.Clear();
            // 已开始淡化的对象离开候选格后仍须完成恢复，不能留下透明材质。
            foreach (var view in previous) candidates.Add(view);
            entries.Clear();
            foreach (var view in candidates)
                if (view != null && view.isActiveAndEnabled &&
                    view.gameObject.scene == player.gameObject.scene && !view.transform.IsChildOf(player)) entries.Add(view);
            foreach (var view in entries)
            {
                view.Refresh(offset.sqrMagnitude > .001f && view.Obstructs(sight, offset.magnitude), player.position, deltaTime);
                if (!view.FullyVisible) tracked.Add(view);
            }
            foreach (var view in previous)
                if (view != null && !entries.Contains(view)) view.Restore();
        }

        private static Vector2Int Cell(Vector3 p) => new(Mathf.FloorToInt(p.x / CellMeters), Mathf.FloorToInt(p.z / CellMeters));

        /// <summary>只在活动登记或所属场景变化时重建；大型环境物件保持精确检查，避免分区膨胀。</summary>
        private void RebuildIndexIfNeeded()
        {
            var scene = player.gameObject.scene;
            if (registryVersion == CameraOccluderView.RegistryVersion && indexedScene == scene) return;
            cells.Clear(); dynamicViews.Clear(); indexedScene = scene;
            foreach (var view in CameraOccluderView.ActiveViews)
            {
                if (view == null || view.gameObject.scene != scene || view.transform.IsChildOf(player)) continue;
                var bounds = view.GetOcclusionBounds(); var min = Cell(bounds.min); var max = Cell(bounds.max);
                // 极大范围物件保持精确路径，避免为巨型屋顶分配过多分区。
                if ((long)(max.x - min.x + 1) * (max.y - min.y + 1) > 256) { dynamicViews.Add(view); continue; }
                for (var x = min.x; x <= max.x; x++)
                    for (var z = min.y; z <= max.y; z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<CameraOccluderView>());
                        bucket.Add(view);
                    }
            }
            registryVersion = CameraOccluderView.RegistryVersion;
        }
        /// <summary>切换到其他镜头或卸载地图前恢复全部外观；可重复调用。</summary>
        public void Dispose()
        {
            foreach (var view in tracked) if (view != null) view.Restore();
            tracked.Clear(); entries.Clear(); previous.Clear(); candidates.Clear(); cells.Clear(); dynamicViews.Clear(); registryVersion = -1;
        }
    }
}
