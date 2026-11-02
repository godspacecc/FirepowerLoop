using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图持有的有界实例池；与规则上限一致，其他模式不会提前创建实例。</summary>
        private readonly Dictionary<int, GameObject> slimeBottleBottles = new();
        private readonly Dictionary<int, SurvivorSlimeZoneVisual> slimeBottleZones = new();
        private readonly Stack<GameObject> idleSlimeBottleBottles = new();
        private readonly Stack<SurvivorSlimeZoneVisual> idleSlimeBottleZones = new();
        private Transform slimeBottleBottleTemplate;
        private SurvivorSlimeZoneVisual slimeBottleZoneTemplate;
        private bool IsSurvivorSlimeBottleReady => root != null && slimeBottleBottleTemplate != null && slimeBottleZoneTemplate != null;
        public string SurvivorSlimeBottleLoadError => contentLoadFailed ? "粘液瓶公共资源加载失败，请重试。" : null;

        /// <summary>在 Content 事务通过代次检查后校验模板；失败沿用地图加载错误出口。</summary>
        private void BindSlimeBottleVisuals(Transform template)
        {
            ClearSlimeBottleVisuals();
            slimeBottleBottleTemplate = template == null ? null : template.Find("Bottle");
            slimeBottleZoneTemplate = template == null ? null : template.Find("SlimeZone")?.GetComponent<SurvivorSlimeZoneVisual>();
            if (slimeBottleBottleTemplate == null || slimeBottleZoneTemplate == null || !slimeBottleZoneTemplate.IsValid)
                throw new InvalidOperationException("粘液瓶瓶子或粘液区模板不完整");
        }

        /// <summary>位置与旋转来自规则轨迹；池容量只约束表现，不反向驱动规则。</summary>
        public void SetSurvivorSlimeBottle(int id, Vector2 position, float height, float rotation, bool active)
        {
            if (!active)
            {
                if (slimeBottleBottles.TryGetValue(id, out var removed))
                { slimeBottleBottles.Remove(id); removed.SetActive(false); idleSlimeBottleBottles.Push(removed); }
                return;
            }
            if (root == null || slimeBottleBottleTemplate == null) return;
            if (!slimeBottleBottles.TryGetValue(id, out var bottle))
            {
                if (slimeBottleBottles.Count >= 2) return;
                bottle = idleSlimeBottleBottles.Count > 0 ? idleSlimeBottleBottles.Pop() :
                    UnityEngine.Object.Instantiate(slimeBottleBottleTemplate.gameObject, root.transform);
                slimeBottleBottles.Add(id, bottle); bottle.SetActive(true);
            }
            bottle.transform.localPosition = new Vector3(position.x, height, position.y);
            bottle.transform.localRotation = Quaternion.Euler(30 + rotation, 30, 30);
        }

        /// <summary>有界粘液区按权威年龄推进，退出和暂停不残留自主播放的粒子。</summary>
        public void SetSurvivorSlimeZone(int id, Vector2 position, float radius, float age, bool active)
        {
            if (!active)
            {
                if (slimeBottleZones.TryGetValue(id, out var removed))
                { slimeBottleZones.Remove(id); removed.ResetVisual(); removed.gameObject.SetActive(false); idleSlimeBottleZones.Push(removed); }
                return;
            }
            if (root == null || slimeBottleZoneTemplate == null) return;
            if (!slimeBottleZones.TryGetValue(id, out var zone))
            {
                if (slimeBottleZones.Count >= 4) return;
                zone = idleSlimeBottleZones.Count > 0 ? idleSlimeBottleZones.Pop() :
                    UnityEngine.Object.Instantiate(slimeBottleZoneTemplate, root.transform);
                slimeBottleZones.Add(id, zone); zone.gameObject.SetActive(true); zone.ResetVisual();
            }
            zone.transform.localPosition = new Vector3(position.x, 0, position.y);
            zone.Sample(radius, age);
        }

        /// <summary>先销毁地图实例和缓存，再由 Content 释放其唯一资源句柄。</summary>
        private void ClearSlimeBottleVisuals()
        {
            foreach (var bottle in slimeBottleBottles.Values) DestroyObject(bottle);
            foreach (var bottle in idleSlimeBottleBottles) DestroyObject(bottle);
            foreach (var zone in slimeBottleZones.Values) DestroyObject(zone.gameObject);
            foreach (var zone in idleSlimeBottleZones) DestroyObject(zone.gameObject);
            slimeBottleBottles.Clear(); idleSlimeBottleBottles.Clear(); slimeBottleZones.Clear(); idleSlimeBottleZones.Clear();
            slimeBottleBottleTemplate = null; slimeBottleZoneTemplate = null;
        }
    }
}
