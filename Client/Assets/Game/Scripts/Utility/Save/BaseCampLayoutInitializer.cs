using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>为候选基地快照一次性写入默认营地；不创建场景对象，不读写磁盘。</summary>
    public static class BaseCampLayoutInitializer
    {
        /// <summary>进入基地候选准备时调用；全部记录验证成功才追加，失败不改列表和初始化标记。</summary>
        public static bool TryInitialize(BaseWorldSaveData world, IReadOnlyList<BaseCampLayoutDefinition> layout,
            Func<string, TempBuildingDefinition> resolve, out string error)
        {
            error = null;
            if (world == null || world.TemporaryBuildings == null || layout == null || resolve == null)
            { error = "默认营地初始化参数缺失"; return false; }
            if (world.CampLayoutInitialized) return true;
            var candidates = new List<ExpeditionTemporaryBuildingSaveData>();
            var original = world.TemporaryBuildings.ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var nextId = original.Length == 0 ? 1 : checked(original.Max(b => b.RuntimeId) + 1);
                foreach (var row in layout)
                {
                    if (string.IsNullOrWhiteSpace(row.Id) || !ids.Add(row.Id) || row.Scale <= 0 || !Finite(row.Scale) ||
                        !Finite(row.Yaw) || !Finite(row.Position.x) || !Finite(row.Position.y) || !Finite(row.Position.z))
                        throw new InvalidOperationException("默认营地记录无效：" + row.Id);
                    var definition = resolve(row.BuildingId) ?? throw new InvalidOperationException("默认摆件配置不存在：" + row.BuildingId);
                    // 只与原有玩家建筑比较；默认配置中组合摆放的箱桶、棚桌仍按已验收布局恢复。
                    var conflict = original.Any(b => {
                        var other = resolve(b.ConfigId) ?? throw new InvalidOperationException("已有建筑配置不存在：" + b.ConfigId);
                        var offset = row.Position - new Vector3(b.Pose.X, b.Pose.Y, b.Pose.Z);
                        var radius = definition.FootprintRadius * row.Scale + other.FootprintRadius * b.UniformScale;
                        return new Vector2(offset.x, offset.z).sqrMagnitude < radius * radius;
                    });
                    if (conflict) continue;
                    if (nextId <= 0 || nextId == int.MaxValue) throw new InvalidOperationException("基地建筑身份已耗尽");
                    candidates.Add(new ExpeditionTemporaryBuildingSaveData { RuntimeId = nextId++, ConfigId = row.BuildingId,
                        Pose = new ExpeditionPoseData { X = row.Position.x, Y = row.Position.y, Z = row.Position.z, RotationY = row.Yaw }, UniformScale = row.Scale });
                }
                world.TemporaryBuildings.AddRange(candidates);
                world.CampLayoutInitialized = true;
                return true;
            }
            catch (Exception exception) { error = "默认营地初始化失败：" + exception.Message; return false; }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
