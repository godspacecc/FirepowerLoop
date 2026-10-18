using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>仅更新正式出口子模板和专属材质；不重建资源库其余内容或场景。</summary>
public static class ArcadeExitAuthoring
{
    private const string Bank = "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    private const string Folder = "Assets/Game/Art/Arcade/Environment/Content";

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("仅在编辑态制作出口");
        var cyan = Material("ExitCyan", new Color(.15f, 1.6f, 1.1f, 1));
        var gold = Material("ExitGold", new Color(2f, 1.3f, .15f, 1));
        var dark = Material("ExitLabel", new Color(.035f, .065f, .07f, 1));
        var prefab = PrefabUtility.LoadPrefabContents(Bank);
        try
        {
            var gate = prefab.transform.Find("Content/RouteGate");
            if (gate == null) throw new InvalidOperationException("正式出口模板缺失");
            var old = gate.Find("ExitCue");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var cue = Node(gate, "ExitCue", Vector3.zero);
            var points = new Vector3[64];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = 2 * Mathf.PI * i / points.Length;
                points[i] = new Vector3(Mathf.Cos(angle) * 1.45f, 0, Mathf.Sin(angle) * 1.45f);
            }
            Line(cue, "GroundRing", new Vector3(0, .22f, 0), points, .12f, cyan, true);
            var arrow = Node(cue, "Arrow", new Vector3(0, 4.6f, 0));
            arrow.localRotation = Quaternion.Euler(60, 0, 0);
            for (int i = 0; i < 2; i++)
                Line(arrow, "Chevron" + i, Vector3.up * i * .65f,
                    new[] { new Vector3(-.8f, .45f, 0), new Vector3(0, -.2f, 0), new Vector3(.8f, .45f, 0) }, .2f, gold, false);
            var guidePoints = new[] { new Vector3(-.6f, 0, -.4f), new Vector3(0, 0, .4f), new Vector3(.6f, 0, -.4f) };
            Line(cue, "ApproachArrow", new Vector3(0, .28f, -3), guidePoints, .16f, gold, false);
            Line(cue.Find("ApproachArrow"), "Outline", Vector3.down * .04f, guidePoints, .3f, dark, false);
            cue.Find("ApproachArrow").gameObject.SetActive(false);
            var label = gate.Find("Destination").GetComponent<TextMeshPro>();
            label.transform.localPosition = new Vector3(0, 2.8f, -1.1f);
            label.transform.localRotation = Quaternion.Euler(60, 0, 0);
            label.rectTransform.sizeDelta = new Vector2(6.4f, 1.5f);
            label.fontSize = 5; label.fontSizeMin = 3; label.fontSizeMax = 5;
            label.text = ""; // 目的地为动态文字，由表现控制器从本地化表提供。
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "LabelPlate";
            plate.transform.SetParent(cue, false);
            plate.transform.localPosition = label.transform.localPosition + label.transform.forward * .06f;
            plate.transform.localRotation = label.transform.localRotation;
            plate.transform.localScale = new Vector3(6.4f, 1.2f, .05f);
            plate.GetComponent<Renderer>().sharedMaterial = dark;
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            var sparks = Node(cue, "Sparks", new Vector3(0, .3f, 0)).gameObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.duration = 2; main.loop = true; main.startLifetime = 1.4f; main.startSpeed = .8f;
            main.startSize = .07f; main.startColor = new Color(.35f, 1, .75f); main.maxParticles = 32;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var shape = sparks.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 1.25f;
            shape.rotation = new Vector3(90, 0, 0);
            var emission = sparks.emission; emission.rateOverTime = 12;
            var velocity = sparks.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = .7f;
            sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = cyan;
            PrefabUtility.SaveAsPrefabAsset(prefab, Bank);
            AssetDatabase.SaveAssetIfDirty(cyan);
            AssetDatabase.SaveAssetIfDirty(gold);
            AssetDatabase.SaveAssetIfDirty(dark);
            return Bank;
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    private static Transform Node(Transform parent, string name, Vector3 position)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go.transform; }

    private static void Line(Transform parent, string name, Vector3 position, Vector3[] points, float width, Material material, bool loop)
    {
        var line = Node(parent, name, position).gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.loop = loop; line.positionCount = points.Length; line.SetPositions(points);
        line.widthMultiplier = width; line.numCapVertices = 3; line.numCornerVertices = 3;
        line.alignment = LineAlignment.View; line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
    }

    private static Material Material(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }
}
