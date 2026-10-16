using System.Collections.Generic;
using UnityEngine;

namespace FirepowerLoop.Arcade
{
    /// <summary>将同一规则轮廓转成无碰撞表面；调用者拥有生成 Mesh 并在离场时释放。</summary>
    public static class ArcadeIslandMesh
    {
        public static Mesh Sand(ArcadeIslandRegion region)
        {
            var points = region.Sand; var vertices = new Vector3[points.Count + 1]; var triangles = new int[points.Count * 3];
            for (int i = 0; i < points.Count; i++)
            {
                vertices[i + 1] = new Vector3(points[i].X, 0, points[i].Y);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % points.Count + 1; triangles[i * 3 + 2] = i + 1;
            }
            return Build("IslandSand", vertices, triangles);
        }

        public static Mesh Water(ArcadeIslandRegion region) => Band("IslandShallowWater", region.Sand, region.Outer);

        /// <summary>沉船的矩形船底与破损舷板；底板覆盖同源阻挡范围，避免可见洞口却不可通行。</summary>
        public static Mesh Wreck(float width, float length)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            void Box(Vector3 center, Vector3 size)
            {
                int first = vertices.Count;
                for (int i = 0; i < 8; i++) vertices.Add(center + new Vector3(
                    (i & 1) == 0 ? -size.x * .5f : size.x * .5f,
                    (i & 2) == 0 ? -size.y * .5f : size.y * .5f,
                    (i & 4) == 0 ? -size.z * .5f : size.z * .5f));
                int[] faces = { 0,2,1,1,2,3, 4,5,6,5,7,6, 0,4,2,2,4,6,
                    1,3,5,3,7,5, 0,1,4,1,5,4, 2,6,3,3,6,7 };
                foreach (int i in faces) triangles.Add(first + i);
            }
            Box(new Vector3(0,.25f,0),new Vector3(width,.5f,length));
            Box(new Vector3(-width*.46f,.9f,0),new Vector3(width*.08f,1.3f,length));
            Box(new Vector3(width*.46f,.7f,-length*.12f),new Vector3(width*.08f,.9f,length*.76f));
            Box(new Vector3(0,.85f,length*.46f),new Vector3(width,1.2f,length*.08f));
            Box(new Vector3(0,1.75f,0),new Vector3(.22f,3,.22f));
            Box(new Vector3(0,2.65f,0),new Vector3(width*.72f,.17f,.17f));
            return Build("IslandWreck", vertices.ToArray(), triangles.ToArray());
        }
        public static Mesh Shoreline(ArcadeIslandRegion region)
        {
            var edge = new ArcadeRegionPoint[region.Sand.Count];
            for (int i = 0; i < edge.Length; i++)
            {
                var s = region.Sand[i]; var o = region.Outer[i];
                float d = Vector2.Distance(new Vector2(s.X, s.Y), new Vector2(o.X, o.Y));
                float t = Mathf.Min(1, .12f / d);
                edge[i] = new ArcadeRegionPoint(Mathf.Lerp(s.X, o.X, t), Mathf.Lerp(s.Y, o.Y, t));
            }
            return Band("IslandShoreline", region.Sand, edge);
        }

        private static Mesh Band(string name, IReadOnlyList<ArcadeRegionPoint> inner, IReadOnlyList<ArcadeRegionPoint> outer)
        {
            var vertices = new Vector3[inner.Count * 2]; var triangles = new int[inner.Count * 6];
            for (int i = 0; i < inner.Count; i++)
            {
                vertices[i * 2] = new Vector3(inner[i].X, 0, inner[i].Y);
                vertices[i * 2 + 1] = new Vector3(outer[i].X, 0, outer[i].Y);
                int n = (i + 1) % inner.Count;
                triangles[i * 6] = i * 2; triangles[i * 6 + 1] = n * 2; triangles[i * 6 + 2] = i * 2 + 1;
                triangles[i * 6 + 3] = n * 2; triangles[i * 6 + 4] = n * 2 + 1; triangles[i * 6 + 5] = i * 2 + 1;
            }
            return Build(name, vertices, triangles);
        }

        private static Mesh Build(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(vertices[i].x / 40 + .5f, vertices[i].z / 40 + .5f);
            mesh.uv = uv; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
