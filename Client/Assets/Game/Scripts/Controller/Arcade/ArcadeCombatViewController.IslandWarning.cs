using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前地图障碍集合已完整附加可见表现。</summary>
        private bool obstacleVisualReady;
        /// <summary>当前加载代次的失败原因，用于冻结开战和显式重试。</summary>
        private string obstacleVisualError;
        /// <summary>无碰撞方向预告实例，随当前地图根释放。</summary>
        private GameObject islandEntryWarning;
        /// <summary>方向预告独占材质，不修改共享正式资源。</summary>
        private Material islandEntryWarningMaterial;

        /// <summary>权威预告期间显示入场方向；无碰撞，不拥有入口时钟或生成预算。</summary>
        public void SetIslandEntryWarning(Vector2 position, bool visible)
        {
            if (!visible || root == null)
            { if (islandEntryWarning != null) islandEntryWarning.SetActive(false); return; }
            // 资源未就绪时等下一份快照；不得让缺失 Shader 的异常打断后续出口和角色同步。
            if (contentTemplates == null) return;
            if (islandEntryWarning == null)
            {
                // 保留资源包中的直接 Shader 引用及渲染设置，颜色只修改本地图独占副本。
                islandEntryWarningMaterial = new Material(contentTemplates.Find("RouteGate/ExitCue/GroundRing").GetComponent<LineRenderer>().sharedMaterial);
                islandEntryWarningMaterial.SetColor("_BaseColor", new Color(1, .72f, .08f, 1));
                islandEntryWarning = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                islandEntryWarning.name = "IslandEntryWarning";
                islandEntryWarning.transform.SetParent(root.transform, false);
                islandEntryWarning.transform.localScale = new Vector3(2.2f, .07f, 2.2f);
                islandEntryWarning.GetComponent<Renderer>().sharedMaterial = islandEntryWarningMaterial;
                DestroyObject(islandEntryWarning.GetComponent<Collider>());
            }
            islandEntryWarning.transform.localPosition = new Vector3(position.x, .08f, position.y);
            islandEntryWarning.SetActive(true);
        }

        private void ReleaseIslandEntryWarning()
        {
            DestroyObject(islandEntryWarning); islandEntryWarning = null;
            DestroyObject(islandEntryWarningMaterial); islandEntryWarningMaterial = null;
        }
    }
}
