using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 由地图系统持有的地形渲染兼容状态：Android 当前地图沿用非实例化兼容路径。
    /// 只管理当前地图的运行时开关及恢复，不修改地形数据、资源材质、配置或存档。
    /// </summary>
    public sealed class TerrainRenderingCompatibility
    {
        /// <summary>首次显示前捕获的开关；重复激活不覆盖原值，停用或销毁时释放引用。</summary>
        private readonly Dictionary<Terrain, bool> originalInstancing = new Dictionary<Terrain, bool>();
        private IMapContentHandle owner;
        /// <summary>显示前应用兼容策略，隐藏后恢复；无关候选的停用不影响当前地图。</summary>
        public void SetActive(IMapContentHandle handle, bool active, RuntimePlatform platform)
        {
            if (!active)
            {
                if (ReferenceEquals(owner, handle))
                    Restore();
                return;
            }

            if (platform != RuntimePlatform.Android || handle == null)
                return;
            if (ReferenceEquals(owner, handle))
                return;
            Restore();
            var children = handle is MapContentGroupHandle group ? group.Children : new[]
            {
                handle
            };
            foreach (var child in children)
            {
                var root = child.Context ? child.Context.MapContentRoot : null;
                if (!root)
                    continue;
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                    if (!originalInstancing.ContainsKey(terrain))
                        originalInstancing.Add(terrain, terrain.drawInstanced);
            }

            owner = handle;
            // 先捕获全部原值再写入，避免当前 Unity 版本地形开关联动影响后续快照。
            foreach (var terrain in originalInstancing.Keys)
                terrain.drawInstanced = false;
        }

        /// <summary>地图隐藏、卸载或架构关闭时恢复原开关；对象已销毁时仅清理引用，可重复调用。</summary>
        public void Restore()
        {
            foreach (var pair in originalInstancing)
                if (pair.Key)
                    pair.Key.drawInstanced = pair.Value;
            originalInstancing.Clear();
            owner = null;
        }
    }
}
