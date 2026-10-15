using System;
using System.Collections.Generic;
using AnotherDay.Arcade;
using UnityEngine;

namespace Game
{
    /// <summary>场地机关的功能模型和预警表现；只读取权威时间，无碰撞伤害、无 Update，由战斗 Controller 唯一释放。</summary>
    internal sealed class ArcadeHazardPresentation : IDisposable
    {
        private readonly Transform owner;
        private readonly GameObject towerTemplate;
        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        public ArcadeHazardPresentation(Transform owner, GameObject towerTemplate = null) { this.owner = owner; this.towerTemplate = towerTemplate; }

        /// <summary>重复快照只改状态和角度，不重建对象；空目录清理本场所有机关。</summary>
        public void Sync(IReadOnlyList<ArcadeHazardParameters> definitions, double elapsed)
        {
            if (definitions == null || definitions.Count == 0) { Dispose(); return; }
            foreach (var p in definitions)
            {
                if (!entries.TryGetValue(p.Id, out var entry))
                {
                    entry = Create(p);
                    entries.Add(p.Id, entry);
                }
                ArcadeHazardRules.Evaluate(p, elapsed, out int phase, out float angle);
                // 相位之外还通过亮度和横臂运动区分状态，俯视视角无需依赖文字。
                bool bright = phase != 1 || Math.Sin(elapsed * Math.PI * 6) >= 0;
                Color color = phase == 0 ? new Color(0.25f, 0.45f, 0.4f) : phase == 1
                    ? (bright ? new Color(1f, 0.18f, 0.05f) : new Color(0.35f, 0.05f, 0.02f))
                    : p.Kind == 1 ? new Color(0.1f, 0.9f, 1f) : new Color(1f, 0.5f, 0.05f);
                entry.Material.color = color;
                entry.Ring.widthMultiplier = phase == 2 ? 0.12f : 0.06f;
                entry.ActiveMark.SetActive(phase == 2 || (p.Kind == 1 && phase == 1 && bright));
                if (entry.Arm != null) entry.Arm.localRotation = Quaternion.Euler(0, -angle, 0);
            }
        }

        private Entry Create(ArcadeHazardParameters p)
        {
            var entry = new Entry();
            entry.Root = new GameObject($"Hazard_{p.Id}_{(p.Kind == 1 ? "ElectricTower" : "RotatingArm")}");
            entry.Root.transform.SetParent(owner, false);
            entry.Root.transform.localPosition = new Vector3(p.X, 0f, p.Y);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("机关表现缺少 Unlit Shader");
            entry.Material = new Material(shader) { name = "ArcadeHazard_Runtime" };
            entry.Material.enableInstancing = true;
            entry.Ring = Circle(entry.Root.transform, p.Extent, entry.Material);
            if (p.Kind == 1)
            {
                if(towerTemplate!=null)
                {
                    var tower=UnityEngine.Object.Instantiate(towerTemplate,entry.Root.transform,false);tower.SetActive(true);
                    // 静态设施复用正式材质；只有电弧与危险边界使用按权威相位变化的材质。
                }
                entry.ActiveMark = new GameObject("ElectricDanger");
                entry.ActiveMark.transform.SetParent(entry.Root.transform, false);
                Circle(entry.ActiveMark.transform, p.Radius * .66f, entry.Material);
                Circle(entry.ActiveMark.transform, p.Radius * .33f, entry.Material);
                for (int index = 0; index < 8; index++)
                {
                    float angle = index * Mathf.PI / 4;
                    var ray = new GameObject("ElectricArc").AddComponent<LineRenderer>();
                    ray.transform.SetParent(entry.ActiveMark.transform, false);
                    ray.useWorldSpace = false; ray.positionCount = 3; ray.sharedMaterial = entry.Material;
                    ray.widthMultiplier = .06f;
                    ray.SetPosition(0, new Vector3(0, 1.5f, 0));
                    ray.SetPosition(1, new Vector3(Mathf.Cos(angle) * p.Radius * .5f, .7f, Mathf.Sin(angle) * p.Radius * .5f));
                    ray.SetPosition(2, new Vector3(Mathf.Cos(angle) * p.Radius, .1f, Mathf.Sin(angle) * p.Radius));
                }
            }
            else
            {
                Primitive(entry.Root.transform, PrimitiveType.Cylinder, new Vector3(0f, .4f, 0f),
                    new Vector3(.4f, .4f, .4f), entry.Material);
                entry.Arm = new GameObject("RotatingArmPivot").transform;
                entry.Arm.SetParent(entry.Root.transform, false);
                Primitive(entry.Arm, PrimitiveType.Cube, new Vector3(p.ArmLength * .5f, .65f, 0f),
                    new Vector3(p.ArmLength, .25f, p.ArmHalfWidth * 2f), entry.Material);
                entry.ActiveMark = Primitive(entry.Arm, PrimitiveType.Cube, new Vector3(p.ArmLength * .5f, .08f, 0f),
                    new Vector3(p.ArmLength, .04f, p.ArmHalfWidth * 2f), entry.Material);
                entry.ActiveMark.name = "SweepingDanger";
            }
            return entry;
        }

        private static LineRenderer Circle(Transform root, float radius, Material material)
        {
            var line = new GameObject("DangerBoundary").AddComponent<LineRenderer>();
            line.transform.SetParent(root, false);
            line.useWorldSpace = false; line.loop = true; line.positionCount = 64;
            line.sharedMaterial = material; line.widthMultiplier = .06f;
            for (int index = 0; index < 64; index++)
            {
                float angle = index * Mathf.PI * 2 / 64;
                line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, .06f, Mathf.Sin(angle) * radius));
            }
            return line;
        }

        private static GameObject Primitive(Transform root, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        {
            var result = GameObject.CreatePrimitive(type);
            result.transform.SetParent(root, false);
            result.transform.localPosition = position;
            result.transform.localScale = size;
            var collider = result.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            result.GetComponent<Renderer>().sharedMaterial = material;
            return result;
        }

        public void Dispose()
        {
            foreach (var entry in entries.Values) { Destroy(entry.Root); Destroy(entry.Material); }
            entries.Clear();
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        private sealed class Entry
        {
            public GameObject Root;
            public Material Material;
            public LineRenderer Ring;
            public Transform Arm;
            public GameObject ActiveMark;
        }
    }
}
