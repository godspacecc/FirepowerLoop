using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 审计和家村 1_1 道路中心线的当前 Terrain 坡度与池塘交叉风险。
/// 本工具只读取权威来源并写出派生报告，不修改 Terrain、场景或人工核对 JSON。
/// </summary>
public static class HeJiaVillageStageBRouteAudit
{
    private const string TerrainPath =
        "Assets/Game/Scenes/GeneratedMaps/HeJiaVillage/TerrainData/HeJiaVillage_1_1.asset";
    private const float TileSize = 1024f;
    private const float SampleSpacingMeters = 2f;
    private const float CrossHalfWidthMeters = 2f;
    private const float PondBufferMeters = 5f;
    private const float ReviewGradePercent = 15f;
    private const float BlockedGradePercent = 20f;

    private sealed class Pond
    {
        public string Id;
        public Vector2 Center;
        public Vector2 Radius;
        public bool IsDisputed;
    }

    private sealed class SegmentAudit
    {
        public string RoadId;
        public string RoadKind;
        public bool IsDisputedRoad;
        public int SegmentIndex;
        public Vector2 Start;
        public Vector2 End;
        public float Length;
        public float MaxLongitudinalGrade;
        public float MaxCrossGrade;
        public readonly List<string> PondInteriors = new List<string>();
        public readonly List<string> PondBuffers = new List<string>();
        public string Status;
    }

    /// <summary>
    /// 从当前 TerrainData 生成稳定 JSON 与俯视标色图。重复执行会覆盖派生产物，
    /// 但不会覆盖任何源资料或场景手工内容。
    /// </summary>
    public static void Main()
    {
        var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        if (terrain == null)
        {
            throw new FileNotFoundException("缺少和家村 1_1 TerrainData", TerrainPath);
        }

        var repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        var sourceRoot = Path.Combine(repositoryRoot, "Tools", "MapTerrainData", "HeJiaVillage");
        var roadsPath = Path.Combine(sourceRoot, "HeJiaVillage_VerifiedRoads.json");
        var surfacePath = Path.Combine(sourceRoot, "HeJiaVillage_VerifiedSurface.json");
        var roads = JObject.Parse(File.ReadAllText(roadsPath));
        var ponds = ReadPonds(JObject.Parse(File.ReadAllText(surfacePath)));
        var audits = AuditSegments(terrain, roads, ponds);

        var jsonPath = Path.Combine(sourceRoot, "HeJiaVillage_StageB_RouteAudit_1_1.json");
        File.WriteAllText(jsonPath, BuildReport(audits, roadsPath, surfacePath).ToString(Formatting.Indented));

        var imagePath = Path.Combine(repositoryRoot, "Docs", "Plans", "Images",
            "HeJiaVillage_StageB_RouteAudit_1_1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
        RenderAuditImage(terrain, audits, ponds, imagePath);

        int safe = audits.FindAll(x => x.Status == "safe").Count;
        int review = audits.FindAll(x => x.Status == "review").Count;
        int blocked = audits.FindAll(x => x.Status == "blocked").Count;
        int disputed = audits.FindAll(x => x.Status == "disputed").Count;
        Debug.Log($"HeJiaVillage route audit saved: segments={audits.Count}, safe={safe}, " +
                  $"review={review}, blocked={blocked}, disputed={disputed}, " +
                  $"json={jsonPath.Replace('\\', '/')}, " +
                  $"image={imagePath.Replace('\\', '/')}");
    }

    private static List<Pond> ReadPonds(JObject surface)
    {
        var result = new List<Pond>();
        foreach (var token in surface["waterBodies"] ?? new JArray())
        {
            var center = token["centerLocalMeters"];
            var radius = token["radiusMeters"];
            var pond = new Pond
            {
                Id = (string)token["id"],
                Center = new Vector2((float)center[0] + TileSize, (float)center[1] + TileSize),
                Radius = new Vector2((float)radius[0], (float)radius[1]),
                IsDisputed = string.Equals((string)token["verificationStatus"], "disputed",
                    StringComparison.OrdinalIgnoreCase)
            };
            if (InsideTile(pond.Center))
            {
                result.Add(pond);
            }
        }

        return result;
    }

    private static List<SegmentAudit> AuditSegments(TerrainData terrain, JObject roads,
        List<Pond> ponds)
    {
        var result = new List<SegmentAudit>();
        foreach (var road in roads["roads"] ?? new JArray())
        {
            var points = (JArray)road["localMeters"];
            for (int index = 0; index < points.Count - 1; index++)
            {
                var start = ToUnityPoint(points[index]);
                var end = ToUnityPoint(points[index + 1]);
                if (!ClipToTile(ref start, ref end))
                {
                    continue;
                }

                var audit = new SegmentAudit
                {
                    RoadId = (string)road["id"],
                    RoadKind = (string)road["kind"],
                    IsDisputedRoad = string.Equals((string)road["verificationStatus"], "disputed",
                        StringComparison.OrdinalIgnoreCase),
                    SegmentIndex = index,
                    Start = start,
                    End = end,
                    Length = Vector2.Distance(start, end)
                };
                SampleGrades(terrain, audit);
                if (!audit.IsDisputedRoad)
                {
                    CheckPonds(audit, ponds);
                }
                audit.Status = audit.IsDisputedRoad ? "disputed" : Classify(audit);
                result.Add(audit);
            }
        }

        return result;
    }

    /// <summary>
    /// 纵坡沿线约每 2 米采样；横坡只比较中心线左右各 2 米的 Terrain 高差，
    /// 用于发现风险，不代表道路设计宽度或现实横坡。
    /// </summary>
    private static void SampleGrades(TerrainData terrain, SegmentAudit audit)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(audit.Length / SampleSpacingMeters));
        var direction = (audit.End - audit.Start).normalized;
        var across = new Vector2(-direction.y, direction.x);
        var previous = audit.Start;
        var previousHeight = HeightAt(terrain, previous);
        for (int step = 0; step <= steps; step++)
        {
            var point = Vector2.Lerp(audit.Start, audit.End, (float)step / steps);
            var height = HeightAt(terrain, point);
            if (step > 0)
            {
                var distance = Vector2.Distance(previous, point);
                audit.MaxLongitudinalGrade = Mathf.Max(audit.MaxLongitudinalGrade,
                    Mathf.Abs(height - previousHeight) / distance * 100f);
            }

            var left = point - across * CrossHalfWidthMeters;
            var right = point + across * CrossHalfWidthMeters;
            if (InsideTile(left) && InsideTile(right))
            {
                var crossGrade = Mathf.Abs(HeightAt(terrain, right) - HeightAt(terrain, left)) /
                                 (CrossHalfWidthMeters * 2f) * 100f;
                audit.MaxCrossGrade = Mathf.Max(audit.MaxCrossGrade, crossGrade);
            }

            previous = point;
            previousHeight = height;
        }
    }

    private static void CheckPonds(SegmentAudit audit, List<Pond> ponds)
    {
        foreach (var pond in ponds)
        {
            // 已被清晰在线影像推翻的历史描线只保留可追溯显示，不参与道路阻断。
            if (pond.IsDisputed) continue;
            if (IntersectsEllipse(audit.Start, audit.End, pond.Center, pond.Radius))
            {
                audit.PondInteriors.Add(pond.Id);
            }
            else if (IntersectsEllipse(audit.Start, audit.End, pond.Center,
                         pond.Radius + Vector2.one * PondBufferMeters))
            {
                audit.PondBuffers.Add(pond.Id);
            }
        }
    }

    private static string Classify(SegmentAudit audit)
    {
        if (audit.PondInteriors.Count > 0 || audit.MaxLongitudinalGrade > BlockedGradePercent ||
            audit.MaxCrossGrade > BlockedGradePercent)
        {
            return "blocked";
        }

        if (audit.PondBuffers.Count > 0 || audit.MaxLongitudinalGrade > ReviewGradePercent ||
            audit.MaxCrossGrade > ReviewGradePercent)
        {
            return "review";
        }

        return "safe";
    }

    private static JObject BuildReport(List<SegmentAudit> audits, string roadsPath, string surfacePath)
    {
        var segments = new JArray();
        foreach (var audit in audits)
        {
            segments.Add(new JObject
            {
                ["roadId"] = audit.RoadId,
                ["roadKind"] = audit.RoadKind,
                ["roadVerificationStatus"] = audit.IsDisputedRoad ? "disputed" : "active",
                ["segmentIndex"] = audit.SegmentIndex,
                ["startUnity"] = PointJson(audit.Start),
                ["endUnity"] = PointJson(audit.End),
                ["lengthMeters"] = Round(audit.Length),
                ["maxLongitudinalGradePercent"] = Round(audit.MaxLongitudinalGrade),
                ["maxCrossGradePercent"] = Round(audit.MaxCrossGrade),
                ["pondInteriors"] = new JArray(audit.PondInteriors),
                ["pondBuffers"] = new JArray(audit.PondBuffers),
                ["status"] = audit.Status
            });
        }

        return new JObject
        {
            ["schemaVersion"] = 1,
            ["mapName"] = "HeJiaVillage",
            ["tile"] = "1_1",
            ["generatedAt"] = DateTimeOffset.Now.ToString("O"),
            ["authority"] = new JObject
            {
                ["terrainData"] = TerrainPath,
                ["roads"] = ToRepositoryPath(roadsPath),
                ["ponds"] = ToRepositoryPath(surfacePath)
            },
            ["sampling"] = new JObject
            {
                ["longitudinalSpacingMeters"] = SampleSpacingMeters,
                ["crossHalfWidthMeters"] = CrossHalfWidthMeters,
                ["pondBufferMeters"] = PondBufferMeters,
                ["reviewGradePercent"] = ReviewGradePercent,
                ["blockedGradePercent"] = BlockedGradePercent
            },
            ["summary"] = new JObject
            {
                ["segments"] = audits.Count,
                ["safe"] = audits.FindAll(x => x.Status == "safe").Count,
                ["review"] = audits.FindAll(x => x.Status == "review").Count,
                ["blocked"] = audits.FindAll(x => x.Status == "blocked").Count,
                ["disputed"] = audits.FindAll(x => x.Status == "disputed").Count,
                ["totalLengthMeters"] = Round(SumLength(audits)),
                ["safeLengthMeters"] = Round(SumLength(audits.FindAll(x => x.Status == "safe"))),
                ["reviewLengthMeters"] = Round(SumLength(audits.FindAll(x => x.Status == "review"))),
                ["blockedLengthMeters"] = Round(SumLength(audits.FindAll(x => x.Status == "blocked"))),
                ["disputedLengthMeters"] = Round(SumLength(audits.FindAll(x => x.Status == "disputed")))
            },
            ["segments"] = segments
        };
    }

    /// <summary>
    /// 使用当前 TerrainData 生成中性地形阴影底图，避免旧卫星影像被误读为现实判定依据。
    /// 地形底图只表达当前工程高程；道路与池塘颜色仍来自审计输入。
    /// </summary>
    private static void RenderAuditImage(TerrainData terrain, List<SegmentAudit> audits,
        List<Pond> ponds, string path)
    {
        Texture2D output = null;
        try
        {
            output = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            DrawTerrainBackdrop(terrain, output);
            foreach (var pond in ponds)
            {
                var bufferColor = pond.IsDisputed
                    ? new Color32(125, 125, 125, 255)
                    : new Color32(0, 150, 180, 255);
                var interiorColor = pond.IsDisputed
                    ? new Color32(180, 180, 180, 255)
                    : new Color32(0, 235, 255, 255);
                DrawEllipse(output, pond.Center, pond.Radius + Vector2.one * PondBufferMeters,
                    bufferColor, 2);
                DrawEllipse(output, pond.Center, pond.Radius, interiorColor, 3);
            }

            foreach (var audit in audits)
            {
                var color = audit.Status == "disputed"
                    ? new Color32(145, 145, 145, 255)
                    : audit.Status == "safe"
                    ? new Color32(45, 210, 90, 255)
                    : audit.Status == "review"
                        ? new Color32(255, 190, 30, 255)
                        : new Color32(245, 65, 55, 255);
                DrawLine(output, audit.Start, audit.End, color, 5);
            }

            // 出生点以黄色十字标示，便于与既有样板位置对照。
            DrawLine(output, new Vector2(758, 512), new Vector2(778, 512), Color.yellow, 3);
            DrawLine(output, new Vector2(768, 502), new Vector2(768, 522), Color.yellow, 3);
            output.Apply(false, false);
            File.WriteAllBytes(path, output.EncodeToPNG());
        }
        finally
        {
            if (output != null) UnityEngine.Object.DestroyImmediate(output);
        }
    }

    private static void DrawTerrainBackdrop(TerrainData terrain, Texture2D output)
    {
        int resolution = terrain.heightmapResolution;
        var heights = terrain.GetHeights(0, 0, resolution, resolution);
        var colors = new Color[output.width * output.height];
        var light = new Vector3(-0.55f, 0.75f, 0.35f).normalized;
        float metresPerPixel = TileSize / (output.width - 1f);
        for (int y = 0; y < output.height; y++)
        for (int x = 0; x < output.width; x++)
        {
            int hx = Mathf.Clamp(Mathf.RoundToInt((float)x / (output.width - 1) * (resolution - 1)),
                0, resolution - 1);
            int hy = Mathf.Clamp(Mathf.RoundToInt((float)y / (output.height - 1) * (resolution - 1)),
                0, resolution - 1);
            int left = Mathf.Max(0, hx - 1);
            int right = Mathf.Min(resolution - 1, hx + 1);
            int down = Mathf.Max(0, hy - 1);
            int up = Mathf.Min(resolution - 1, hy + 1);
            float dx = (heights[hy, right] - heights[hy, left]) * terrain.size.y /
                       Mathf.Max(metresPerPixel, right - left);
            float dz = (heights[up, hx] - heights[down, hx]) * terrain.size.y /
                       Mathf.Max(metresPerPixel, up - down);
            var normal = new Vector3(-dx, 1f, -dz).normalized;
            float hillshade = Mathf.Clamp01(Vector3.Dot(normal, light) * 0.65f + 0.35f);
            float elevation = heights[hy, hx];
            float value = Mathf.Clamp01(0.12f + hillshade * 0.58f + elevation * 0.22f);
            colors[y * output.width + x] = new Color(value * 0.78f, value * 0.84f,
                value * 0.80f, 1f);
        }

        output.SetPixels(colors);
    }

    private static void DrawLine(Texture2D texture, Vector2 start, Vector2 end, Color color, int width)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) * 2f));
        int radius = Mathf.Max(1, width / 2);
        for (int step = 0; step <= steps; step++)
        {
            var point = Vector2.Lerp(start, end, (float)step / steps);
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                SetPixelSafe(texture, Mathf.RoundToInt(point.x) + x, Mathf.RoundToInt(point.y) + y, color);
            }
        }
    }

    private static void DrawEllipse(Texture2D texture, Vector2 center, Vector2 radius, Color color,
        int width)
    {
        const int steps = 180;
        var previous = center + new Vector2(radius.x, 0f);
        for (int step = 1; step <= steps; step++)
        {
            float angle = Mathf.PI * 2f * step / steps;
            var current = center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
            DrawLine(texture, previous, current, color, width);
            previous = current;
        }
    }

    private static void SetPixelSafe(Texture2D texture, int x, int y, Color color)
    {
        if (x >= 0 && x < texture.width && y >= 0 && y < texture.height)
        {
            texture.SetPixel(x, y, color);
        }
    }

    private static bool IntersectsEllipse(Vector2 start, Vector2 end, Vector2 center, Vector2 radius)
    {
        var a = new Vector2((start.x - center.x) / radius.x, (start.y - center.y) / radius.y);
        var b = new Vector2((end.x - center.x) / radius.x, (end.y - center.y) / radius.y);
        var delta = b - a;
        float denominator = delta.sqrMagnitude;
        float t = denominator <= Mathf.Epsilon ? 0f : Mathf.Clamp01(-Vector2.Dot(a, delta) / denominator);
        return (a + delta * t).sqrMagnitude <= 1f;
    }

    private static bool ClipToTile(ref Vector2 start, ref Vector2 end)
    {
        float t0 = 0f, t1 = 1f;
        var delta = end - start;
        if (!Clip(-delta.x, start.x, ref t0, ref t1) ||
            !Clip(delta.x, TileSize - start.x, ref t0, ref t1) ||
            !Clip(-delta.y, start.y, ref t0, ref t1) ||
            !Clip(delta.y, TileSize - start.y, ref t0, ref t1))
        {
            return false;
        }

        var originalStart = start;
        start = originalStart + delta * t0;
        end = originalStart + delta * t1;
        return Vector2.Distance(start, end) > 0.01f;
    }

    private static bool Clip(float p, float q, ref float t0, ref float t1)
    {
        if (Mathf.Approximately(p, 0f)) return q >= 0f;
        float r = q / p;
        if (p < 0f)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }

        return true;
    }

    private static Vector2 ToUnityPoint(JToken token)
    {
        return new Vector2((float)token[0] + TileSize, (float)token[1] + TileSize);
    }

    private static float HeightAt(TerrainData terrain, Vector2 point)
    {
        return terrain.GetInterpolatedHeight(Mathf.Clamp01(point.x / TileSize),
            Mathf.Clamp01(point.y / TileSize));
    }

    private static bool InsideTile(Vector2 point)
    {
        return point.x >= 0f && point.x <= TileSize && point.y >= 0f && point.y <= TileSize;
    }

    private static JArray PointJson(Vector2 point)
    {
        return new JArray(Round(point.x), Round(point.y));
    }

    private static float Round(float value)
    {
        return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static float SumLength(List<SegmentAudit> audits)
    {
        float sum = 0f;
        foreach (var audit in audits) sum += audit.Length;
        return sum;
    }

    private static string ToRepositoryPath(string absolutePath)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        return absolutePath.Substring(repositoryRoot.Length + 1).Replace('\\', '/');
    }
}
