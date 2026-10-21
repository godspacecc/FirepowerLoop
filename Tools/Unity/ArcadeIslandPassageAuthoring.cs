using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Luban;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>制作用户已确认的四张固定海岸；外围山体退至场边，通道和阻挡读取正式配置。</summary>
public static class ArcadeIslandPassageAuthoring
{
    private const string Island = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab";
    private const string Bank = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    private const string Folder = "Assets/Game/Art/Arcade/Island/FixedRounds";
    private const string Study = "Assets/Art/CoastStudy/Generated/";
    private static Material rock, sand, wood;
    private static Mesh[] palms;
    private static Material[] palmMaterials;

    public static string Run()
    {
        if (Application.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在FirepowerLoop编辑态制作");
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        rock = AssetDatabase.LoadAssetAtPath<Material>(Study + "CoastalRock.mat");
        sand = AssetDatabase.LoadAssetAtPath<Material>(Study + "Sand.mat");
        wood = AssetDatabase.LoadAssetAtPath<Material>(Study + "WeatheredWood.mat");
        palms = Enumerable.Range(0, 3).Select(i => AssetDatabase.LoadAssetAtPath<Mesh>(Study + "DetailedPalm" + i + ".asset")).ToArray();
        palmMaterials = new[] { "PalmBark", "PalmGreen", "PalmSunlit", "PalmDry" }
            .Select(n => AssetDatabase.LoadAssetAtPath<Material>(Study + n + ".mat")).ToArray();
        if (rock == null || sand == null || wood == null || palms.Any(p => p == null) || palmMaterials.Any(m => m == null))
            throw new InvalidOperationException("已有海岸样板资源不完整");
        var tiledSand = new Material(sand) { name = "FixedCoastSand" };
        tiledSand.mainTextureScale = new Vector2(40f / 6, 40f / 6);
        SaveAsset(tiledSand, Folder + "/FixedCoastSand.mat");
        sand = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/FixedCoastSand.mat");
        var provider = new Game.LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + n + ".bytes"))));
        if (!provider.TryGetArcadeCatalog(out var catalog, out var error)) throw new InvalidOperationException(error);
        var prefab = PrefabUtility.LoadPrefabContents(Island);
        try
        {
            var prior = prefab.transform.Find("FixedRounds"); if (prior != null) Object.DestroyImmediate(prior.gameObject);
            var rounds = new GameObject("FixedRounds").transform; rounds.SetParent(prefab.transform, false);
            for (int round = 1; round <= 4; round++)
            {
                var owner = new GameObject("Round" + round).transform; owner.SetParent(rounds, false);
                var layout = catalog.RoundLayouts.Single(p => p.ArenaConfigNumber == 1 && p.RoundNumber == round);
                var sea = new Material(Shader.Find("FirepowerLoop/IslandCoastWater")) { name = "IslandSea" + round };
                sea.SetFloat("_CoastRound", round); SaveAsset(sea, Folder + "/IslandSea" + round + ".mat");
                var palette = new GameObject("WaterPalette", typeof(MeshRenderer)); palette.transform.SetParent(owner, false);
                palette.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/IslandSea" + round + ".mat");
                palette.GetComponent<MeshRenderer>().enabled = false;
                Terrain(owner, round, layout);
                Forest(owner, round);
                CoastRocks(owner, round);
                if (round == 2) Boat(owner);
                owner.gameObject.SetActive(false);
            }
            prefab.transform.Find("Sand").GetComponent<Renderer>().sharedMaterial = sand;
            prefab.transform.Find("Water").GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Study + "Sea.mat");
            prefab.transform.Find("Ocean").GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Study + "Sea.mat");
            PrefabUtility.SaveAsPrefabAsset(prefab, Island);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var bank = PrefabUtility.LoadPrefabContents(Bank);
        try
        {
            var previous = bank.transform.Find("Island"); int index = previous.GetSiblingIndex();
            Object.DestroyImmediate(previous.gameObject);
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Island), bank.transform);
            copy.name = "Island"; copy.SetActive(false); copy.transform.SetSiblingIndex(index);
            PrefabUtility.SaveAsPrefabAsset(bank, Bank);
        }
        finally { PrefabUtility.UnloadPrefabContents(bank); }
        AssetDatabase.SaveAssets();
        return "已制作四张固定海岸；中央30米见方无山体侵占；第二轮有码头停船；未修改未确认的武器或特效";
    }

    /// <summary>山体由同源边界向外生长；平路直通镜头外，不能用一圈薄石墙代替连续地形。</summary>
    private static bool Corridor(int round, float x, float z)
    {
        for (int i = 0; i < ArcadeIslandRegion.PassageCount; i++)
        {
            int side = ArcadeIslandRegion.PassageSide(round, i);
            if (side == 0 && z > 15 && Math.Abs(x) < 3.5f || side == 2 && z < -15 && Math.Abs(x) < 3.5f ||
                side == 1 && x > 15 && Math.Abs(z) < 3.5f || side == 3 && x < -15 && Math.Abs(z) < 3.5f) return true;
        }
        return false;
    }
    private static int Side(float x, float z) => Math.Abs(x) > Math.Abs(z) ? (x > 0 ? 1 : 3) : (z > 0 ? 0 : 2);
    private static float Height(int round, float x, float z)
    {
        float outside = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - 17.9f;
        if (outside <= 0 || ArcadeIslandRegion.SeaSide(round, Side(x, z)) || Corridor(round, x, z)) return -.04f;
        float rise = Mathf.Clamp01(outside / 3.8f);
        return rise * (3.2f + 2.8f * Mathf.PerlinNoise(x * .14f + round * 13, z * .14f + 29));
    }
    private static void Terrain(Transform owner, int round, ArcadeRoundLayoutParameters layout)
    {
        const int count = 87; const float step = 1.5f;
        var vertices = new Vector3[count * count]; var uv = new Vector2[vertices.Length];
        var triangles = new List<int>();
        for (int z = 0; z < count; z++) for (int x = 0; x < count; x++)
        {
            int i = z * count + x; float px = (x - 43) * step, pz = (z - 43) * step;
            vertices[i] = new Vector3(px, Height(round, px, pz), pz); uv[i] = new Vector2(px / 6, pz / 6);
        }
        for (int z = 0; z < count - 1; z++) for (int x = 0; x < count - 1; x++)
        {
            int a = z * count + x;
            if (vertices[a].y <= 0 && vertices[a + 1].y <= 0 && vertices[a + count].y <= 0 && vertices[a + count + 1].y <= 0) continue;
            triangles.AddRange(new[] { a, a + count, a + 1, a + 1, a + count, a + count + 1 });
        }
        MeshObject(owner, "ContinuousMountain", SaveMesh("Mountain" + round, vertices, uv, triangles.ToArray()), rock, false);
        // 配置山脚被实体岩面覆盖，内缘严格等于权威阻挡，不把可见山脚放进无碰撞的战斗区。
        int footIndex = 0;
        foreach (var o in layout.Obstacles)
        {
            float w = o.HalfWidth * 2, h = o.HalfHeight * 2;
            var verticesFoot = new Vector3[8];
            for (int v = 0; v < 8; v++)
            {
                bool top = (v & 2) != 0; float bevel = top ? .82f : 1;
                verticesFoot[v] = new Vector3(((v & 1) == 0 ? -1 : 1) * w * .5f * bevel,
                    top ? .7f : -.02f, ((v & 4) == 0 ? -1 : 1) * h * .5f * bevel);
            }
            var mesh = SaveMesh("Foot" + round + "_" + footIndex++, verticesFoot,
                verticesFoot.Select(p => new Vector2(p.x / 6, (p.z + p.y) / 6)).ToArray(),
                new[] { 0,2,1,1,2,3,4,5,6,5,7,6,0,4,2,2,4,6,1,3,5,3,7,5,0,1,4,1,5,4,2,6,3,3,6,7 });
            var foot = MeshObject(owner, "SolidRockFoot", mesh, rock, false);
            foot.transform.localPosition = new Vector3(o.X, 0, o.Y);
            var boulder = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/CoastStudy/PolyHaven/boulder_01.fbx")
                .GetComponentsInChildren<MeshFilter>(true).Single(f => f.name.EndsWith("LOD3", StringComparison.Ordinal)).sharedMesh;
            for (int stone = 0; stone < 5; stone++)
            {
                float along = -6.5f + stone * 3.2f;
                var detail = MeshObject(owner, "MountainBoulder", boulder, rock, false);
                detail.transform.localPosition = o.HalfWidth > o.HalfHeight
                    ? new Vector3(o.X + along, .95f, Math.Sign(o.Y) * 19.4f)
                    : new Vector3(Math.Sign(o.X) * 19.4f, .95f, o.Y + along);
                detail.transform.localScale = new Vector3(2.6f / boulder.bounds.size.x, 2.2f / boulder.bounds.size.y, 2.6f / boulder.bounds.size.z);
                detail.transform.localRotation = Quaternion.Euler(0, stone * 137 + round * 47, 0);
            }
        }
        // 内陆地面延伸填满通道，海面朝向保持露海；运行时中心地表覆盖这层低地。
        var region = new ArcadeIslandRegion(19.5f, 19.5f, round);
        var inland = ArcadeIslandMesh.Inland(region);
        inland.name = "Inland" + round; SaveAsset(inland, Folder + "/Inland" + round + ".asset");
        MeshObject(owner, "DryLandOutside", AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Inland" + round + ".asset"), sand, false);
    }

    /// <summary>固定种子只用于离线制作摆放，正式地图不随机生成；复用已有低成本棕榈网格。</summary>
    private static void Forest(Transform owner, int round)
    {
        var random = new System.Random(20261022 + round * 113);
        for (int z = -8; z <= 8; z++) for (int x = -8; x <= 8; x++)
        {
            float px = x * 6 + (float)random.NextDouble() * 2 - 1, pz = z * 6 + (float)random.NextDouble() * 2 - 1;
            float height = Height(round, px, pz);
            if (height < .1f || Corridor(round, px * .94f, pz * .94f)) continue;
            var tree = new GameObject("FixedPalm", typeof(MeshFilter), typeof(MeshRenderer)); tree.transform.SetParent(owner, false);
            tree.transform.localPosition = new Vector3(px, height - .12f, pz);
            tree.transform.localRotation = Quaternion.Euler(0, random.Next(360), 0);
            tree.transform.localScale = Vector3.one * (.65f + (float)random.NextDouble() * .25f);
            tree.GetComponent<MeshFilter>().sharedMesh = palms[random.Next(palms.Length)];
            tree.GetComponent<MeshRenderer>().sharedMaterials = palmMaterials;
        }
    }

    /// <summary>海边用不连续低礁石表现可见岸线，保留第二轮正东的码头平路。</summary>
    private static void CoastRocks(Transform owner, int round)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/CoastStudy/PolyHaven/boulder_01.fbx")
            .GetComponentsInChildren<MeshFilter>(true).Single(f => f.name.EndsWith("LOD3", StringComparison.Ordinal)).sharedMesh;
        var region = new ArcadeIslandRegion(19.5f, 19.5f, round);
        for (int i = 0; i < region.Outer.Count; i += 4)
        {
            var p = region.Outer[i]; int side = Side(p.X, p.Y);
            if (!ArcadeIslandRegion.SeaSide(round, side) || Corridor(round, p.X, p.Y)) continue;
            var go = MeshObject(owner, "CoastalReef", source, rock, false);
            var size = source.bounds.size;
            go.transform.localScale = new Vector3(2.7f / size.x, .8f / size.y, 2.7f / size.z);
            go.transform.localPosition = new Vector3(p.X * 1.015f, .15f, p.Y * 1.015f);
            go.transform.localRotation = Quaternion.Euler(0, i * 137, 0);
        }
    }
    private static void Boat(Transform owner)
    {
        var dock = new GameObject("DockedBoatPassage").transform; dock.SetParent(owner, false);
        // 敌人起点23.5米落在甲板内，木板平路直连沙地，不把码头做成场内阻挡。
        for (int i = 0; i < 26; i++) Box(dock, "GangwayPlank", new Vector3(17 + i * .3f, .04f, 0), new Vector3(.27f, .10f, 4.8f), wood);
        foreach (float z in new[] { -2.7f, 2.7f }) foreach (float x in new[] { 18f, 21f, 24f })
            Box(dock, "DockPost", new Vector3(x, .24f, z), new Vector3(.26f, 1.2f, .26f), wood);
        var ship = ArcadeIslandMesh.Wreck(7.5f, 4.8f); ship.name = "DockedBoat"; SaveAsset(ship, Folder + "/DockedBoat.asset");
        var hull = MeshObject(dock, "MooredHull", AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/DockedBoat.asset"), wood, false);
        hull.transform.localPosition = new Vector3(25.3f, -.12f, 0); hull.transform.localRotation = Quaternion.Euler(0, 90, 0);
        Box(dock, "BoatCabin", new Vector3(24.5f, 1.15f, 0), new Vector3(2, 1.8f, 2.8f), wood);
    }
    private static GameObject Box(Transform owner, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(owner, false);
        go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    private static GameObject MeshObject(Transform owner, string name, Mesh mesh, Material material, bool solid)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(owner, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh; return go;
    }
    private static Mesh SaveMesh(string name, Vector3[] vertices, Vector2[] uv, int[] triangles)
    {
        var mesh = new Mesh { name = name, vertices = vertices, uv = uv, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); SaveAsset(mesh, Folder + "/" + name + ".asset");
        return AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/" + name + ".asset");
    }
    private static void SaveAsset(Object asset, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (old == null) AssetDatabase.CreateAsset(asset, path);
        else { EditorUtility.CopySerialized(asset, old); Object.DestroyImmediate(asset); EditorUtility.SetDirty(old); }
    }
}
