using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Game
{
    /// <summary>纯数据差异协议；以稳定身份规范集合，不访问领域模型或文件，不序列化河谷地形。</summary>
    internal static class IncrementalSaveCodec
    {
        private sealed class MutableResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization serialization)
            {
                var properties = base.CreateProperties(type, serialization);
                return type == typeof(ExpeditionSaveData) ? properties.Where(p => p.PropertyName != "ValleyLayout").ToList() : properties;
            }
        }
        private static readonly MutableResolver Resolver = new MutableResolver();
        private static readonly HashSet<string> Entities = new HashSet<string>(StringComparer.Ordinal)
        { "Players", "Enemies", "Loot", "SearchContainers", "TemporaryBuildings" };
        private static readonly HashSet<string> Sets = new HashSet<string>(StringComparer.Ordinal)
        { "visited", "technologies", "clues", "shortcuts", "SettledExpeditionIds" };

        internal static JObject Capture<T>(T data) => (JObject)Normalize(JObject.FromObject(data, JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = Resolver })), "");
        internal static T Restore<T>(JObject state) => Denormalize(state).ToObject<T>();

        private static JToken Normalize(JToken token, string name)
        {
            if (token is JObject obj)
            {
                var result = new JObject();
                foreach (var property in obj.Properties()) result.Add(property.Name, Normalize(property.Value, property.Name));
                return result;
            }
            if (!(token is JArray array)) return token.DeepClone();
            var identity = Entities.Contains(name) ? "RuntimeId" : name == "slots" ? "index" : name == "Slots" ? "Index" : Sets.Contains(name) ? "$set" : "$index";
            var items = new JObject();
            for (var i = 0; i < array.Count; i++)
            {
                var key = identity == "$index" ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                    identity == "$set" ? array[i].Value<string>() : array[i][identity]?.ToString();
                if (key == null || items.Property(key) != null) throw new InvalidDataException("增量集合身份缺失或重复：" + name);
                items.Add(key, Normalize(array[i], ""));
            }
            return new JObject { ["$array"] = identity, ["items"] = items };
        }
        private static JToken Denormalize(JToken token)
        {
            if (!(token is JObject obj)) return token.DeepClone();
            if (obj["$array"] is JValue marker && obj["items"] is JObject items && obj.Count == 2)
            {
                var identity = marker.Value<string>();
                var properties = identity == "$set" ? items.Properties().OrderBy(p => p.Name, StringComparer.Ordinal) :
                    items.Properties().OrderBy(p => int.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture));
                return new JArray(properties.Select(p => Denormalize(p.Value)));
            }
            var result = new JObject(); foreach (var p in obj.Properties()) result.Add(p.Name, Denormalize(p.Value)); return result;
        }

        /// <summary>忽略单独变化的时间戳；无业务差异不产生磁盘记录。</summary>
        internal static JArray Diff(JObject previous, JObject next)
        {
            var changes = new JArray();
            foreach (var name in previous.Properties().Select(p => p.Name).Union(next.Properties().Select(p => p.Name)).Where(n => n != "UpdatedAtUtc"))
                DiffNode(previous.Property(name), next.Property(name), new[] { name }, changes);
            if (changes.Count != 0) DiffNode(previous.Property("UpdatedAtUtc"), next.Property("UpdatedAtUtc"), new[] { "UpdatedAtUtc" }, changes);
            return changes;
        }
        private static void DiffNode(JProperty old, JProperty next, string[] path, JArray changes)
        {
            if (old != null && next != null && JToken.DeepEquals(old.Value, next.Value)) return;
            if (old?.Value is JObject a && next?.Value is JObject b)
            {
                foreach (var name in a.Properties().Select(p => p.Name).Union(b.Properties().Select(p => p.Name)))
                    DiffNode(a.Property(name), b.Property(name), path.Concat(new[] { name }).ToArray(), changes);
                return;
            }
            var change = new JObject { ["Path"] = new JArray(path), ["HadOld"] = old != null, ["Remove"] = next == null };
            if (old != null) change["Old"] = old.Value.DeepClone();
            if (next != null) change["Value"] = next.Value.DeepClone();
            changes.Add(change);
        }
        /// <summary>所有旧值校验在候选副本上进行；任一失败不污染已提交状态。</summary>
        internal static JObject Apply(JObject previous, JArray changes)
        {
            var result = (JObject)previous.DeepClone();
            foreach (var change in changes.Cast<JObject>())
            {
                var path = change["Path"]?.Values<string>().ToArray();
                if (path == null || path.Length == 0 || path[0] == "ValleyLayout") throw new InvalidDataException("变化路径无效");
                var parent = result;
                for (var i = 0; i < path.Length - 1; i++) parent = parent[path[i]] as JObject ?? throw new InvalidDataException("变化路径父级不存在");
                var property = parent.Property(path[path.Length - 1]);
                if ((bool)change["HadOld"] != (property != null) || property != null && !JToken.DeepEquals(property.Value, change["Old"]))
                    throw new InvalidDataException("变化记录与已提交基准不一致");
                if ((bool)change["Remove"]) property?.Remove();
                else parent[path[path.Length - 1]] = change["Value"]?.DeepClone() ?? throw new InvalidDataException("变化新值缺失");
            }
            return result;
        }
    }
}
