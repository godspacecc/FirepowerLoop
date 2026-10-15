using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>
    /// 按需读取指定地图根下的地形渲染快照；不持有场景引用，不修改材质、贴图或地形数据。
    /// Shader 查询与材质属性只反映当前可见状态，不独立判定裁剪或灰地根因。
    /// </summary>
    public static class TerrainDiagnosticReport
    {
        private const int MaxTerrains = 16;
        private const int MaxLayers = 16;

        /// <summary>由测试指令在主线程同步调用；单块地形读取失败仍继续报告其它地形。</summary>
        /// <param name="root">当前已提交地图的内容根，只检查其子对象，包括未激活地形。</param>
        /// <param name="playerPosition">玩家世界坐标；缺失时不猜测脚下位置，不采样权重。</param>
        public static string Build(GameObject root, Vector3? playerPosition)
        {
            var text = new StringBuilder("[TerrainDiagnostic]\n");
            text.AppendLine($"platform={Application.platform} graphics={SystemInfo.graphicsDeviceType} shaderLevel={SystemInfo.graphicsShaderLevel}");
            text.AppendLine($"quality={QualitySettings.GetQualityLevel()} pipeline={Name(GraphicsSettings.currentRenderPipeline)} ambient={RenderSettings.ambientLight}");
            text.AppendLine($"PlayerPosition={(playerPosition.HasValue ? playerPosition.Value.ToString("F3") : "unavailable")}");
            foreach (var shaderName in new[] {
                "Universal Render Pipeline/Terrain/Lit",
                "Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)",
                "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)",
                "Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)" })
            {
                var shader = Shader.Find(shaderName);
                text.AppendLine($"ShaderQuery={shaderName} found={shader != null} supported={(shader ? shader.isSupported.ToString() : "unknown")}");
            }
            text.AppendLine("注意：Shader.Find 未找到、Material 纹理为空都不能单独证明漏打包；Terrain 可通过内部渲染属性绑定贴图。");
            var terrains = root ? root.GetComponentsInChildren<Terrain>(true) : Array.Empty<Terrain>();
            text.AppendLine($"root={Name(root)} TerrainCount={terrains.Length}");
            for (var index = 0; index < Math.Min(terrains.Length, MaxTerrains); index++)
            {
                try { AppendTerrain(text, terrains[index], playerPosition); }
                catch (Exception exception) { text.AppendLine($"TerrainReadFailed={index} {exception.GetType().Name}: {exception.Message}"); }
            }
            if (terrains.Length > MaxTerrains) text.AppendLine("TerrainReportTruncated=true");
            return text.ToString();
        }

        private static void AppendTerrain(StringBuilder text, Terrain terrain, Vector3? playerPosition)
        {
            var data = terrain.terrainData;
            var collider = terrain.GetComponent<TerrainCollider>();
            text.AppendLine($"Terrain={terrain.name} scene={terrain.gameObject.scene.name} active={terrain.gameObject.activeInHierarchy} enabled={terrain.enabled} position={terrain.transform.position} scale={terrain.transform.lossyScale}");
            text.AppendLine($"drawHeightmap={terrain.drawHeightmap} instanced={terrain.drawInstanced} basemapDistance={terrain.basemapDistance}");
            text.AppendLine($"TerrainCollider={Name(collider)} enabled={(collider && collider.enabled)} sameData={(collider && collider.terrainData == data)}");
            var material = terrain.materialTemplate;
            text.AppendLine($"Material={Name(material)} Shader={(material ? Name(material.shader) : "default")} supported={(material && material.shader ? material.shader.isSupported.ToString() : "unknown")}");
            if (material)
            {
                text.AppendLine($"keywords={string.Join(",", material.shaderKeywords)}");
                foreach (var property in new[] { "_Control", "_Splat0", "_Splat1", "_Splat2", "_Splat3", "_MainTex" })
                    if (material.HasProperty(property)) text.AppendLine($"Material.{property}={TextureInfo(material.GetTexture(property))}");
            }
            text.AppendLine($"TerrainData={Name(data)}");
            if (!data) return;
            var layers = data.terrainLayers;
            text.AppendLine($"size={data.size} heightResolution={data.heightmapResolution} alphamap={data.alphamapWidth}x{data.alphamapHeight} layers={layers.Length} alphaTextures={data.alphamapTextures.Length}");
            var alphaTextures = data.alphamapTextures;
            for (var index = 0; index < Math.Min(alphaTextures.Length, MaxLayers); index++)
                text.AppendLine($"alphaTexture[{index}]={TextureInfo(alphaTextures[index])}");
            for (var index = 0; index < Math.Min(layers.Length, MaxLayers); index++)
            {
                var layer = layers[index];
                text.AppendLine($"layer[{index}]={Name(layer)} diffuse={TextureInfo(layer ? layer.diffuseTexture : null)} normal={TextureInfo(layer ? layer.normalMapTexture : null)} mask={TextureInfo(layer ? layer.maskMapTexture : null)}");
                if (layer) text.AppendLine($"tileSize={layer.tileSize} diffuseRemap={layer.diffuseRemapMin}/{layer.diffuseRemapMax}");
            }
            if (!playerPosition.HasValue) return;
            var local = terrain.transform.InverseTransformPoint(playerPosition.Value);
            if (data.size.x <= 0 || data.size.z <= 0 || local.x < 0 || local.z < 0 || local.x > data.size.x || local.z > data.size.z)
            {
                text.AppendLine("PlayerOutsideTerrain=true");
                return;
            }
            var u = local.x / data.size.x;
            var v = local.z / data.size.z;
            var ground = terrain.transform.TransformPoint(new Vector3(local.x, data.GetInterpolatedHeight(u, v), local.z));
            text.AppendLine($"groundY={F(ground.y)} playerAboveGround={F(playerPosition.Value.y - ground.y)}");
            if (data.alphamapLayers == 0) { text.AppendLine("NoAlphamapLayers=true"); return; }
            // 只读取脚下一个像素，不复制整幅控制图；越界位置不夹到边缘伪装成脚下数据。
            var x = Mathf.Clamp((int)(u * data.alphamapWidth), 0, data.alphamapWidth - 1);
            var z = Mathf.Clamp((int)(v * data.alphamapHeight), 0, data.alphamapHeight - 1);
            var weights = data.GetAlphamaps(x, z, 1, 1);
            for (var index = 0; index < Math.Min(weights.GetLength(2), MaxLayers); index++)
                text.AppendLine($"weight[{index}]={F(weights[0, 0, index])}");
        }

        private static string Name(UnityEngine.Object value) => value ? value.name : "null";
        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string TextureInfo(Texture value) => value
            ? $"{value.name} {value.width}x{value.height}" + (value is Texture2D texture ? $" format={texture.format} readable={texture.isReadable}" : "")
            : "null";
    }
}
