using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>本局详情框的切角边框与线形图标；只绘制装饰，不响应输入或持有本局数据。</summary>
    [AddComponentMenu("UI/FirepowerLoop/Resume Decoration")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArcadeResumeDecoration : MaskableGraphic
    {
        /// <summary>详情框使用的固定装饰类型。</summary>
        public enum DecorationShape { Frame, Trophy, Heart, Coins, Arrow }
        /// <summary>由 Prefab 选择的装饰图形。</summary>
        public DecorationShape Shape;
        /// <summary>边框内部填色；图标不使用此颜色。</summary>
        public Color FillColor = Color.clear;
        /// <summary>以当前 UI 坐标计量的描边宽度。</summary>
        public float StrokeWidth = 1.5f;
        /// <summary>切角边框的裁角尺寸。</summary>
        public float CornerCut = 18f;

        /// <summary>仅按当前矩形重建切角面片或图标折线，不分配 Unity 资源。</summary>
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (Shape == DecorationShape.Frame)
            {
                float cut = Mathf.Min(CornerCut, Mathf.Min(rect.width, rect.height) * .25f);
                var points = new[] {
                    new Vector2(rect.xMin + cut, rect.yMin), new Vector2(rect.xMax - cut, rect.yMin),
                    new Vector2(rect.xMax, rect.yMin + cut), new Vector2(rect.xMax, rect.yMax - cut),
                    new Vector2(rect.xMax - cut, rect.yMax), new Vector2(rect.xMin + cut, rect.yMax),
                    new Vector2(rect.xMin, rect.yMax - cut), new Vector2(rect.xMin, rect.yMin + cut) };
                if (FillColor.a > 0)
                {
                    mesh.AddVert(rect.center, FillColor, Vector2.zero);
                    foreach (var point in points) mesh.AddVert(point, FillColor, Vector2.zero);
                    for (int i = 0; i < points.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
                }
                for (int i = 0; i < points.Length; i++) Line(mesh, points[i], points[(i + 1) % points.Length]);
                return;
            }
            if (Shape == DecorationShape.Heart)
                Path(mesh, rect, new[] { new Vector2(50, 15), new Vector2(13, 53), new Vector2(10, 71),
                    new Vector2(24, 85), new Vector2(39, 85), new Vector2(50, 74), new Vector2(61, 85),
                    new Vector2(76, 85), new Vector2(90, 71), new Vector2(87, 53), new Vector2(50, 15) });
            else if (Shape == DecorationShape.Trophy)
            {
                Path(mesh, rect, new[] { new Vector2(29, 87), new Vector2(71, 87), new Vector2(68, 50),
                    new Vector2(50, 35), new Vector2(32, 50), new Vector2(29, 87) });
                Path(mesh, rect, new[] { new Vector2(29, 77), new Vector2(13, 77), new Vector2(16, 57), new Vector2(33, 50) });
                Path(mesh, rect, new[] { new Vector2(71, 77), new Vector2(87, 77), new Vector2(84, 57), new Vector2(67, 50) });
                Path(mesh, rect, new[] { new Vector2(50, 35), new Vector2(50, 16), new Vector2(30, 16), new Vector2(70, 16) });
            }
            else if (Shape == DecorationShape.Coins)
            {
                for (int row = 0; row < 3; row++)
                {
                    float y = 70 - row * 22;
                    Vector2 previous = Point(rect, 82, y);
                    for (int segment = 1; segment <= 20; segment++)
                    {
                        float angle = segment * Mathf.PI * 2 / 20;
                        var next = Point(rect, 50 + Mathf.Cos(angle) * 32, y + Mathf.Sin(angle) * 13);
                        Line(mesh, previous, next); previous = next;
                    }
                }
                Line(mesh, Point(rect, 18, 70), Point(rect, 18, 26));
                Line(mesh, Point(rect, 82, 70), Point(rect, 82, 26));
            }
            else if (Shape == DecorationShape.Arrow)
            {
                Path(mesh, rect, new[] { new Vector2(10, 50), new Vector2(88, 50), new Vector2(60, 78) });
                Line(mesh, Point(rect, 88, 50), Point(rect, 60, 22));
            }
        }

        /// <summary>将图标的百分比坐标映射到本组件矩形。</summary>
        private static Vector2 Point(Rect rect, float x, float y) =>
            new Vector2(rect.xMin + rect.width * x / 100, rect.yMin + rect.height * y / 100);

        /// <summary>按固定拐点绘制连续折线。</summary>
        private void Path(VertexHelper mesh, Rect rect, Vector2[] points)
        {
            for (int i = 1; i < points.Length; i++)
                Line(mesh, Point(rect, points[i - 1].x, points[i - 1].y), Point(rect, points[i].x, points[i].y));
        }

        /// <summary>使用窄四边形绘制一条线，避免依赖外部纹理或运行时材质。</summary>
        private void Line(VertexHelper mesh, Vector2 from, Vector2 to)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < .0001f) return;
            Vector2 offset = new Vector2(-direction.y, direction.x).normalized * StrokeWidth * .5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(from - offset, color, Vector2.zero); mesh.AddVert(from + offset, color, Vector2.zero);
            mesh.AddVert(to + offset, color, Vector2.zero); mesh.AddVert(to - offset, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
