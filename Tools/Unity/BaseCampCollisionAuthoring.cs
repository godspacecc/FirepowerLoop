using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>离线补齐基地静态阻挡并检查主要通路；不改角色移动、NPC 配置或第三方原始 Prefab。</summary>
public static class BaseCampCollisionAuthoring
{
    private const string ScenePath = "Assets/Game/Scenes/BaseMap.unity";

    /// <summary>在场景实例添加碰撞，重复调用不叠加组件；现有手工碰撞保持原样。</summary>
    public static string Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var root = Decoration(scene);
            var objects = root.Cast<Transform>().SelectMany(g => g.Cast<Transform>()).ToArray();
            foreach (var t in objects)
            {
                if (t.GetComponentsInChildren<Collider>(true).Length > 0) continue;
                var filter = t.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault();
                if (filter == null) continue;
                if (t.name.Contains("棚") && !t.name.Contains("工具"))
                {
                    // 开放结构保持真实支柱和棚顶，非凸静态网格不会把入口填成实心盒体。
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                }
                else if (t.name.Contains("松树"))
                {
                    // 树冠不阻挡角色，仅包住树干；尺寸为模型局部米，随实例缩放。
                    var collider = t.gameObject.AddComponent<CapsuleCollider>();
                    collider.radius = .22f;
                    collider.height = 3;
                    collider.center = new Vector3(-.03f, 1.5f, .01f);
                    collider.direction = 1;
                }
                else if (t.name.Contains("箱") || t.name.Contains("桶") || t.name.Contains("桌") ||
                    t.name.Contains("长凳") || t.name.Contains("木栅栏") || t.name.Contains("岩石") ||
                    t.name.Contains("麻袋") || t.name.Contains("物资袋") || t.name.Contains("木料") ||
                    t.name.Contains("柴堆") || t.name.Contains("石圈"))
                {
                    var collider = filter.gameObject.AddComponent<BoxCollider>();
                    collider.center = filter.sharedMesh.bounds.center;
                    collider.size = filter.sharedMesh.bounds.size;
                }
            }
            // 原来没有碰撞的入口岩石在中线；补阻挡时挪到步道右侧，保留胶囊通行净空。
            var entryRock = root.Find("木栅栏与自然边界/外围岩石18");
            entryRock.localPosition = new Vector3(3, entryRock.localPosition.y, entryRock.localPosition.z);
            var report = Inspect(scene);
            if (report.Errors.Count != 0) throw new InvalidOperationException(string.Join("\n", report.Errors));
            // 物理验收临时启用内容；落盘保持地图加载事务负责显隐的初始隐藏状态。
            root.parent.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("基地碰撞场景保存失败");
            return "Saved. " + report;
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static Transform Decoration(UnityEngine.SceneManagement.Scene scene) => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<MapContext>(true)).Single().MapContentRoot.transform.Find("BaseCampDecoration");

    /// <summary>直接用场景物理几何检查玩家半径 0.45 米、高度 2 米的空间；不启动 Test Runner 或玩法。</summary>
    public static string Validate()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try { return Inspect(scene).ToString(); }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    /// <summary>临时创建与正式玩家同尺寸的 CharacterController 验证实际 Move 阻挡，结束销毁，不保存探针。</summary>
    public static string ProbePlayerMovement()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        GameObject actor = null;
        try
        {
            Decoration(scene).parent.gameObject.SetActive(true);
            actor = new GameObject("基地碰撞临时玩家探针");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor, scene);
            var controller = actor.AddComponent<CharacterController>();
            controller.height = 2; controller.radius = .45f; controller.center = Vector3.up;
            controller.stepOffset = .3f; controller.skinWidth = .08f; controller.minMoveDistance = 0;
            controller.enabled = false; actor.transform.position = new Vector3(4, .02f, 0); controller.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 80; i++) controller.Move(new Vector3(.05f, -.02f, 0));
            var blocked = actor.transform.position;
            if (blocked.x >= 6.1f) throw new InvalidOperationException("玩家穿过出发准备桌：" + blocked);
            controller.enabled = false; actor.transform.position = new Vector3(0, .02f, -13); controller.enabled = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 390; i++) controller.Move(new Vector3(0, -.02f, .05f));
            var arrival = actor.transform.position;
            if (Mathf.Abs(arrival.z - 6.5f) > .15f) throw new InvalidOperationException("入口主路玩家受阻：" + arrival);
            return "CharacterController.Move: stopped before table at " + blocked + "; entry road reached " + arrival + ".";
        }
        finally
        {
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private sealed class Report
    {
        public readonly List<string> Errors = new List<string>();
        public int Solids;
        public override string ToString() => "Decoration colliders=" + Solids + "; errors=" + Errors.Count +
            (Errors.Count == 0 ? "; solid props, shelter openings, entry road and five NPC approaches clear." : "\n" + string.Join("\n", Errors));
    }

    private static Report Inspect(UnityEngine.SceneManagement.Scene scene)
    {
        var root = Decoration(scene);
        root.parent.gameObject.SetActive(true);
        Physics.SyncTransforms();
        var report = new Report();
        var solids = root.GetComponentsInChildren<Collider>(true);
        report.Solids = solids.Length;
        foreach (var c in solids)
            if (!c.enabled || c.isTrigger || c.attachedRigidbody != null) report.Errors.Add("非静态实体碰撞：" + c.name);
        foreach (var path in new[] { "仓库物资区/仓库木棚", "仓库物资区/仓库大箱", "制作工作区/制作棚", "制作工作区/制作木桌",
            "商人物资摊位/商人布棚", "商人物资摊位/商人货桌", "出发导览区/出发准备桌", "街机休闲角/街机休闲棚",
            "街机休闲角/街机长凳", "营火休息区/营火侧长凳", "木栅栏与自然边界/外围岩石0", "木栅栏与自然边界/外围松树0" })
            if (root.Find(path).GetComponentsInChildren<Collider>(true).Length == 0) report.Errors.Add("缺少实体阻挡：" + path);
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("灌木") || t.name.Contains("草簇")))
            if (t.GetComponents<Collider>().Length != 0) report.Errors.Add("小植被不应阻挡：" + t.name);
        var table = root.Find("出发导览区/出发准备桌");
        if (!Physics.CapsuleCastAll(new Vector3(4, .45f, 0), new Vector3(4, 1.55f, 0), .45f, Vector3.right, 4, ~0, QueryTriggerInteraction.Ignore)
            .Any(h => h.collider.transform.IsChildOf(table))) report.Errors.Add("玩家走向出发桌没有命中阻挡");
        foreach (var path in new[] { "仓库物资区/仓库木棚", "制作工作区/制作棚", "商人物资摊位/商人布棚", "街机休闲角/街机休闲棚" })
        {
            var t = root.Find(path);
            var from = t.TransformPoint(new Vector3(0, .9f, 1.2f));
            var to = t.TransformPoint(new Vector3(0, .9f, -.4f));
            if (Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore)
                .Any(h => h.collider.transform.IsChildOf(t))) report.Errors.Add("棚子前开口受阻：" + path);
        }
        var routes = new[] {
            new[] { new Vector3(0, 0, -13), new Vector3(0, 0, 6.5f) },
            new[] { Vector3.zero, new Vector3(5, 0, 0) },
            new[] { Vector3.zero, new Vector3(-4.9f, 0, -.2f) },
            new[] { new Vector3(0, 0, 4.5f), new Vector3(-4.9f, 0, 4.5f) },
            new[] { new Vector3(0, 0, 4.2f), new Vector3(4.9f, 0, 4.2f) },
            new[] { new Vector3(0, 0, 5), new Vector3(0, 0, 7.1f) }
        };
        foreach (var route in routes)
        {
            var motion = route[1] - route[0];
            var hits = Physics.CapsuleCastAll(route[0] + Vector3.up * .45f, route[0] + Vector3.up * 1.55f, .45f,
                motion.normalized, motion.magnitude, ~0, QueryTriggerInteraction.Ignore).Where(h => h.collider.transform.IsChildOf(root));
            foreach (var hit in hits) report.Errors.Add("通路受阻 " + route[0] + " → " + route[1] + "：" + hit.collider.name);
            foreach (var hit in Physics.OverlapCapsule(route[1] + Vector3.up * .45f, route[1] + Vector3.up * 1.55f, .45f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c.transform.IsChildOf(root))) report.Errors.Add("交互点重叠：" + hit.name);
        }
        return report;
    }
}
