using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存基地已解锁的探索地图列表，并通过存档分区持久化最小地图成长进度。
    /// </summary>
    public sealed class BaseMapProgressModel : AbstractModel, IScopedSaveSectionProvider
    {
        private const string UnlockedMapIdsKey = "unlockedMapIds";
        private readonly HashSet<string> unlockedMapIds = new HashSet<string>();

        public string SectionId => "another-day.base-map-progress";
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;

        protected override void OnInit()
        {
            ResetProgress();
        }

        public bool IsMapUnlocked(string mapId)
        {
            if (!MapCatalog.TryGet(mapId, out var definition))
            {
                return false;
            }

            return definition.UnlockedByDefault || unlockedMapIds.Contains(mapId);
        }

        public void UnlockMap(string mapId)
        {
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                unlockedMapIds.Add(mapId);
            }
        }

        public void ResetProgress()
        {
            unlockedMapIds.Clear();
            foreach (var definition in MapCatalog.GetExpeditionMaps())
            {
                if (definition.UnlockedByDefault)
                {
                    unlockedMapIds.Add(definition.MapId);
                }
            }
        }

        public bool Validate(JToken data, out string error)
        {
            error = string.Empty;
            if (data == null)
            {
                return true;
            }

            var value = data[UnlockedMapIdsKey];
            if (value == null || value.Type == JTokenType.Array)
            {
                return true;
            }

            error = "基地地图解锁列表无效";
            return false;
        }

        public JToken Capture()
        {
            return JObject.FromObject(new
            {
                unlockedMapIds = unlockedMapIds
            });
        }

        public void Restore(JToken data)
        {
            ResetProgress();
            var array = data?[UnlockedMapIdsKey] as JArray;
            if (array == null)
            {
                return;
            }

            foreach (var item in array)
            {
                var mapId = item.Value<string>();
                if (!string.IsNullOrWhiteSpace(mapId))
                {
                    unlockedMapIds.Add(mapId);
                }
            }
        }

        public void Reset()
        {
            ResetProgress();
        }
    }
}
