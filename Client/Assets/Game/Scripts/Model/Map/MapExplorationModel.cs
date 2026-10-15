using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>按稳定地图 ID 保存已探索网格；只拥有探索事实，不加载地图纹理、不接受界面拖动作为探索。</summary>
    public sealed class MapExplorationModel : AbstractModel, IScopedSaveSectionProvider
    {
        /// <summary>每轴 128 格，位图为每区域 2048 字节；世界范围取正式地图展示配置。</summary>
        public const int Resolution = 128;
        private const int ByteCount = Resolution * Resolution / 8;
        private readonly Dictionary<string, Record> maps = new Dictionary<string, Record>(StringComparer.Ordinal);
        public string SectionId => "another-day.map-exploration";
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        /// <summary>内存版本用于展示刷新，恢复和重置也递增；不写入存档。</summary>
        public int Revision { get; private set; }
        protected override void OnInit() { }
        /// <summary>新远征提交时只清除此地图的旧发现；不比较种子，不影响基地和其它地图。</summary>
        internal bool ForgetMap(string mapId)
        {if(mapId==null||!maps.Remove(mapId))return false;Revision++;return true;}

        /// <summary>观察当前地图前同步布局身份；同局读档不清空，新布局只重置本区域，空身份代表固定地图。</summary>
        public bool SynchronizeLayoutIdentity(MapDisplayDefinition display, string layoutIdentity)
        {
            layoutIdentity = layoutIdentity ?? string.Empty;
            if (maps.TryGetValue(display.MapId, out var record) && record.Matches(display) &&
                string.Equals(record.LayoutIdentity, layoutIdentity, StringComparison.Ordinal)) return false;
            maps[display.MapId] = new Record(display, new byte[ByteCount], layoutIdentity);
            Revision++;
            return true;
        }

        /// <summary>揭开实际位置周围的圆形区域，半径单位为世界米；重复探索返回 false，不产生重复脏存档。</summary>
        public bool Reveal(MapDisplayDefinition display, Vector3 position, float radius)
        {
            if (string.IsNullOrWhiteSpace(display.MapId) || !Finite(radius) || radius <= 0 ||
                !MapDisplayProjection.TryWorldToNormalized(display, position, out var uv)) return false;
            if (!maps.TryGetValue(display.MapId, out var record) || !record.Matches(display))
                maps[display.MapId] = record = new Record(display, new byte[ByteCount]);
            var centerX = Mathf.Min(Resolution - 1, Mathf.FloorToInt(uv.x * Resolution));
            var centerY = Mathf.Min(Resolution - 1, Mathf.FloorToInt(uv.y * Resolution));
            var rangeX = Mathf.Min(Resolution, Mathf.CeilToInt(radius / display.Width * Resolution) + 1);
            var rangeY = Mathf.Min(Resolution, Mathf.CeilToInt(radius / display.Height * Resolution) + 1);
            var changed = false;
            for (var y = Mathf.Max(0, centerY - rangeY); y <= Mathf.Min(Resolution - 1, centerY + rangeY); y++)
            for (var x = Mathf.Max(0, centerX - rangeX); x <= Mathf.Min(Resolution - 1, centerX + rangeX); x++)
            {
                // 以格子边界到玩家的距离判断，边缘与地图最大坐标不会留出未揭开的玩家脚下格。
                var minX = display.MinX + x * display.Width / Resolution;
                var minZ = display.MinZ + y * display.Height / Resolution;
                var dx = position.x - Mathf.Clamp(position.x, minX, minX + display.Width / Resolution);
                var dz = position.z - Mathf.Clamp(position.z, minZ, minZ + display.Height / Resolution);
                if (dx * dx + dz * dz > radius * radius) continue;
                var index = y * Resolution + x;
                var mask = (byte)(1 << (index & 7));
                if ((record.Bits[index >> 3] & mask) != 0) continue;
                record.Bits[index >> 3] |= mask;
                changed = true;
            }
            if (changed) Revision++;
            return changed;
        }

        public bool IsExplored(MapDisplayDefinition display, Vector3 position)
        {
            if (!MapDisplayProjection.TryWorldToNormalized(display, position, out var uv)) return false;
            return IsCellExplored(display, Mathf.Min(Resolution - 1, Mathf.FloorToInt(uv.x * Resolution)),
                Mathf.Min(Resolution - 1, Mathf.FloorToInt(uv.y * Resolution)));
        }

        public bool IsCellExplored(MapDisplayDefinition display, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Resolution || y >= Resolution ||
                !maps.TryGetValue(display.MapId, out var record) || !record.Matches(display)) return false;
            var index = y * Resolution + x;
            return (record.Bits[index >> 3] & (1 << (index & 7))) != 0;
        }

        public float GetExploredFraction(MapDisplayDefinition display)
        {
            if (!maps.TryGetValue(display.MapId, out var record) || !record.Matches(display)) return 0;
            var count = 0;
            foreach (var value in record.Bits)
                for (var bit = 0; bit < 8; bit++) if ((value & (1 << bit)) != 0) count++;
            return (float)count / (Resolution * Resolution);
        }

        public JToken Capture()
        {
            var records = new JObject();
            foreach (var pair in maps.OrderBy(p => p.Key, StringComparer.Ordinal))
                records[pair.Key] = new JObject { ["centerX"] = pair.Value.CenterX, ["centerZ"] = pair.Value.CenterZ,
                    ["width"] = pair.Value.Width, ["height"] = pair.Value.Height, ["bits"] = Convert.ToBase64String(pair.Value.Bits),
                    ["layoutIdentity"] = pair.Value.LayoutIdentity };
            return new JObject { ["version"] = 1, ["maps"] = records };
        }

        /// <summary>缺失分区视为旧存档；损坏版本、范围和位图在恢复事务前拒绝，保留原状态。</summary>
        public bool Validate(JToken data, out string error)
        {
            error = null;
            if (data == null || data.Type == JTokenType.Null) return true;
            try
            {
                if (!(data is JObject obj) || (int?)obj["version"] != 1 || !(obj["maps"] is JObject records))
                    throw new FormatException();
                foreach (var pair in records.Properties())
                {
                    if (string.IsNullOrWhiteSpace(pair.Name) || !(pair.Value is JObject record)) throw new FormatException();
                    var cx = Number(record, "centerX"); var cz = Number(record, "centerZ");
                    var width = Number(record, "width"); var height = Number(record, "height");
                    if (!Finite(cx) || !Finite(cz) || !Finite(width) || !Finite(height) || width <= 0 || height <= 0 ||
                        record["bits"]?.Type != JTokenType.String ||
                        Convert.FromBase64String((string)record["bits"]).Length != ByteCount) throw new FormatException();
                    if (record["layoutIdentity"] != null && record["layoutIdentity"].Type != JTokenType.String)
                        throw new FormatException();
                }
                return true;
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException || e is ArgumentException)
            { error = "地图探索分区无效"; return false; }
        }

        public void Restore(JToken data)
        {
            if (!Validate(data, out var error)) throw new ArgumentException(error);
            maps.Clear();
            if (data != null && data.Type != JTokenType.Null && data["maps"] is JObject records)
                foreach (var pair in records.Properties())
                {
                    var record = (JObject)pair.Value;
                    var definition = new MapDisplayDefinition(pair.Name, string.Empty, Number(record, "centerX"),
                        Number(record, "centerZ"), Number(record, "width"), Number(record, "height"));
                    maps[pair.Name] = new Record(definition, Convert.FromBase64String((string)record["bits"]),
                        (string)record["layoutIdentity"] ?? string.Empty);
                }
            Revision++;
        }

        public void Reset() { maps.Clear(); Revision++; }
        private static float Number(JObject obj, string key)
        {
            var token = obj[key];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new FormatException();
            return token.Value<float>();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private sealed class Record
        {
            public readonly float CenterX, CenterZ, Width, Height;
            public readonly byte[] Bits;
            /// <summary>生成版本、种子及候选索引组成的稳定身份；固定地图与旧档为空。</summary>
            public readonly string LayoutIdentity;
            public Record(MapDisplayDefinition d, byte[] bits, string layoutIdentity = "")
            { CenterX = d.CenterX; CenterZ = d.CenterZ; Width = d.Width; Height = d.Height; Bits = bits; LayoutIdentity = layoutIdentity; }
            public bool Matches(MapDisplayDefinition d) => CenterX == d.CenterX && CenterZ == d.CenterZ && Width == d.Width && Height == d.Height;
        }
    }
}
