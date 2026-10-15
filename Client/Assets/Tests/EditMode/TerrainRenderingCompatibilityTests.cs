using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class TerrainRenderingCompatibilityTests
    {
        [Test]
        public void AndroidHeJiaCompatibilityRestoresBeforeSwitchAndLeavesOtherPlatformsUnchanged()
        {
            var type = Type.GetType("Game.TerrainRenderingCompatibility, Game");
            Assert.That(type, Is.Not.Null, "正式切图需要持有可恢复的渲染兼容状态");
            var compatibility = Activator.CreateInstance(type);
            var setActive = type.GetMethod("SetActive");
            var restore = type.GetMethod("Restore");
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(10, 10, 10) };
            var root = Terrain.CreateTerrainGameObject(data);
            try
            {
                var context = root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, root);
                var handle = new TerrainTestHandle(MapIds.HeJiaVillage, context);
                var terrain = root.GetComponent<Terrain>();
                terrain.drawInstanced = true;
                setActive.Invoke(compatibility, new object[] { handle, true, RuntimePlatform.WindowsPlayer });
                Assert.That(terrain.drawInstanced, Is.True);
                setActive.Invoke(compatibility, new object[] { new TerrainTestHandle(MapIds.SurvivalSample, context), true, RuntimePlatform.Android });
                Assert.That(terrain.drawInstanced, Is.True);
                setActive.Invoke(compatibility, new object[] { handle, true, RuntimePlatform.Android });
                Assert.That(terrain.drawInstanced, Is.False);
                setActive.Invoke(compatibility, new object[] { handle, true, RuntimePlatform.Android });
                setActive.Invoke(compatibility, new object[] { handle, false, RuntimePlatform.Android });
                Assert.That(terrain.drawInstanced, Is.True, "重复激活不能覆盖原始开关");
                terrain.drawInstanced = false;
                setActive.Invoke(compatibility, new object[] { handle, true, RuntimePlatform.Android });
                restore.Invoke(compatibility, null);
                Assert.That(terrain.drawInstanced, Is.False, "原本关闭实例化的地形不得被恢复为开启");
                terrain.drawInstanced = true;
                setActive.Invoke(compatibility, new object[] { handle, true, RuntimePlatform.Android });
                UnityEngine.Object.DestroyImmediate(root);
                Assert.DoesNotThrow(() => restore.Invoke(compatibility, null), "销毁后的地图也必须可清理");
            }
            finally
            {
                restore.Invoke(compatibility, null);
                if (root) UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        private sealed class TerrainTestHandle : IMapContentHandle
        {
            public TerrainTestHandle(string mapId, MapContext context) { MapId = mapId; Context = context; }
            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => Context != null;
        }

    }
}
