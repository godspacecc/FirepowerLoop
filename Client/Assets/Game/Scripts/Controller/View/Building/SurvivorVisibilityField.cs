using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>同一份前方视线用于建筑淡出、角色和道具显示以及自动瞄准。</summary>
    internal sealed class SurvivorVisibilityField : IDisposable
    {
        private const int RayCount = 2048;
        private const float FullCircle = 2f * Mathf.PI;
        private const float HalfAngle = 48f;
        private const float OwnFootprintRadius = .55f;
        private static readonly float AngleCosine = Mathf.Cos(HalfAngle * Mathf.Deg2Rad);
        private static readonly Vector2[] Directions = BuildDirections();
        private readonly IReadOnlyList<Bounds> blockers;
        private readonly List<Bounds> inRange = new();
        private readonly Color32[] distances = new Color32[RayCount];
        private Texture2D texture;
        private Vector3 eye;
        private float radius;
        private Vector2 heading;

        internal Texture2D Texture => texture;
        internal Vector4 Origin => new(eye.x, eye.z, radius, eye.y);
        internal Vector4 Cone => new(heading.x, heading.y, AngleCosine, OwnFootprintRadius * OwnFootprintRadius);
        internal float GroundHeight { get; private set; }

        internal SurvivorVisibilityField(IReadOnlyList<Bounds> blockers) => this.blockers = blockers;

        /// <summary>实体足迹在本局固定，只在玩家移动或昼夜范围变化时刷新。</summary>
        internal void Update(Vector3 nextEye, float nextRadius, Vector2 nextHeading, float groundHeight)
        {
            heading = nextHeading.normalized;
            GroundHeight = groundHeight;
            if (texture != null && (eye - nextEye).sqrMagnitude < .0004f && Mathf.Abs(radius - nextRadius) < .02f) return;
            eye = nextEye;
            radius = nextRadius;
            inRange.Clear();
            foreach (var bounds in blockers)
                if (bounds.min.y <= eye.y && eye.y <= bounds.max.y && bounds.SqrDistance(eye) <= radius * radius)
                    inRange.Add(bounds);

            for (int i = 0; i < RayCount; i++)
            {
                var direction = Directions[i];
                float nearest = radius;
                foreach (var bounds in inRange)
                {
                    var low = bounds.min;
                    var high = bounds.max;
                    float enter = 0f, exit = radius;
                    if (!ClipAxis(eye.x, direction.x, low.x, high.x, ref enter, ref exit) ||
                        !ClipAxis(eye.z, direction.y, low.z, high.z, ref enter, ref exit) || enter >= nearest) continue;
                    nearest = enter;
                }
                // 以 16 位编码记录首次命中，CPU 和 GPU 使用同一份距离。
                int start = Mathf.RoundToInt(nearest / radius * 65535f);
                distances[i] = new Color32((byte)(start >> 8), (byte)start, 0, 255);
            }

            texture ??= new Texture2D(RayCount, 1, TextureFormat.RGBA32, false, true)
            {
                name = "幸存者建筑视线", filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave
            };
            texture.SetPixelData(distances, 0);
            texture.Apply(false, false);
        }

        internal bool IsVisible(Vector3 point)
        {
            if (texture == null) return true;
            var offset = new Vector2(point.x - eye.x, point.z - eye.z);
            float squared = offset.sqrMagnitude;
            if (squared > radius * radius) return false;
            if (squared < .000001f) return true;
            float distance = Mathf.Sqrt(squared);
            if (heading != Vector2.zero && Vector2.Dot(heading, offset) < distance * AngleCosine) return false;
            float angle = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) / FullCircle, 1f);
            var sample = distances[Mathf.Min(RayCount - 1, Mathf.FloorToInt(angle * RayCount))];
            return distance <= (sample.r * 256 + sample.g) * (radius / 65535f);
        }

        private static bool ClipAxis(float origin, float direction, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(direction) < .000001f) return origin >= min && origin <= max;
            float a = (min - origin) / direction;
            float b = (max - origin) / direction;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        private static Vector2[] BuildDirections()
        {
            var directions = new Vector2[RayCount];
            for (int i = 0; i < directions.Length; i++)
            {
                float radians = (i + .5f) * FullCircle / RayCount;
                directions[i] = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            }
            return directions;
        }

        public void Dispose()
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
