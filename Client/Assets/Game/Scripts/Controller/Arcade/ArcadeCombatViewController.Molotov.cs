using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图持有的有界实例池；与规则上限一致，其他模式不会提前创建实例。</summary>
        private readonly Dictionary<int, GameObject> molotovBottles = new();
        private readonly Dictionary<int, SurvivorFireZoneVisual> molotovZones = new();
        private readonly Stack<GameObject> idleMolotovBottles = new();
        private readonly Stack<SurvivorFireZoneVisual> idleMolotovZones = new();
        private Transform molotovBottleTemplate;
        private SurvivorFireZoneVisual molotovZoneTemplate;
        public bool IsSurvivorMolotovReady => root != null && molotovBottleTemplate != null && molotovZoneTemplate != null;
        public string SurvivorMolotovLoadError => contentLoadFailed ? "燃烧弹公共资源加载失败，请重试。" : null;

        /// <summary>在 Content 事务通过代次检查后校验模板；失败沿用地图加载错误出口。</summary>
        private void BindMolotovVisuals(Transform template)
        {
            ClearMolotovVisuals();
            molotovBottleTemplate = template == null ? null : template.Find("Bottle");
            molotovZoneTemplate = template == null ? null : template.Find("FireZone")?.GetComponent<SurvivorFireZoneVisual>();
            if (molotovBottleTemplate == null || molotovZoneTemplate == null || !molotovZoneTemplate.IsValid)
                throw new InvalidOperationException("燃烧弹瓶子或火区模板不完整");
        }

        /// <summary>位置与旋转来自规则轨迹；池容量只约束表现，不反向驱动规则。</summary>
        public void SetSurvivorMolotov(int id, Vector2 position, float height, float rotation, bool active)
        {
            if (!active)
            {
                if (molotovBottles.TryGetValue(id, out var removed))
                { molotovBottles.Remove(id); removed.SetActive(false); idleMolotovBottles.Push(removed); }
                return;
            }
            if (root == null || molotovBottleTemplate == null) return;
            if (!molotovBottles.TryGetValue(id, out var bottle))
            {
                if (molotovBottles.Count >= 2) return;
                bottle = idleMolotovBottles.Count > 0 ? idleMolotovBottles.Pop() :
                    UnityEngine.Object.Instantiate(molotovBottleTemplate.gameObject, root.transform);
                molotovBottles.Add(id, bottle); bottle.SetActive(true);
            }
            bottle.transform.localPosition = new Vector3(position.x, height, position.y);
            bottle.transform.localRotation = Quaternion.Euler(30 + rotation, 30, 30);
        }

        /// <summary>有界火区按权威年龄推进，退出和暂停不残留自主播放的粒子。</summary>
        public void SetSurvivorFireZone(int id, Vector2 position, float radius, float age, bool active)
        {
            if (!active)
            {
                if (molotovZones.TryGetValue(id, out var removed))
                { molotovZones.Remove(id); removed.ResetVisual(); removed.gameObject.SetActive(false); idleMolotovZones.Push(removed); }
                return;
            }
            if (root == null || molotovZoneTemplate == null) return;
            if (!molotovZones.TryGetValue(id, out var zone))
            {
                if (molotovZones.Count >= 4) return;
                zone = idleMolotovZones.Count > 0 ? idleMolotovZones.Pop() :
                    UnityEngine.Object.Instantiate(molotovZoneTemplate, root.transform);
                molotovZones.Add(id, zone); zone.gameObject.SetActive(true); zone.ResetVisual();
            }
            zone.transform.localPosition = new Vector3(position.x, 0, position.y);
            zone.Sample(radius, age);
        }

        /// <summary>先销毁地图实例和缓存，再由 Content 释放其唯一资源句柄。</summary>
        private void ClearMolotovVisuals()
        {
            foreach (var bottle in molotovBottles.Values) DestroyObject(bottle);
            foreach (var bottle in idleMolotovBottles) DestroyObject(bottle);
            foreach (var zone in molotovZones.Values) DestroyObject(zone.gameObject);
            foreach (var zone in idleMolotovZones) DestroyObject(zone.gameObject);
            molotovBottles.Clear(); idleMolotovBottles.Clear(); molotovZones.Clear(); idleMolotovZones.Clear();
            molotovBottleTemplate = null; molotovZoneTemplate = null;
        }
    }
}
