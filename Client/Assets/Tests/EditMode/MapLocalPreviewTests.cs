using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MapLocalPreviewTests
    {
        [Test]
        public void PreviewBoundsContainConfirmedSatelliteImage()
        {
            var type = Type.GetType("Game.MapLocalPreviewGenerator, Assembly-CSharp-Editor", true);
            var originX = (float)type.GetField("OriginX").GetRawConstantValue();
            var originZ = (float)type.GetField("OriginZ").GetRawConstantValue();
            var size = (float)type.GetField("WorldSize").GetRawConstantValue();
            var corners = new[]
            {
                new Vector2(805.817f, 1725.722f), new Vector2(1397.749f, 1732.609f),
                new Vector2(812.924f, 989.646f), new Vector2(1404.891f, 996.532f)
            };
            foreach (var corner in corners)
            {
                Assert.That(corner.x, Is.InRange(originX, originX + size));
                Assert.That(corner.y, Is.InRange(originZ, originZ + size));
            }
        }

        [Test]
        public void PreviewWindowHasSeparateLocalMenuEntry()
        {
            var type = Type.GetType("Game.MapLocalPreviewWindow, Assembly-CSharp-Editor", true);
            var open = type.GetMethod("OpenWindow", BindingFlags.Static | BindingFlags.Public);
            Assert.That(open, Is.Not.Null);
            var menu = open.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Single();
            Assert.That(menu.menuItem, Is.EqualTo("Tools/地图/局部散图制作/生成预览地图"));
        }

        [Test]
        public void GeneratedPreviewAssetsHaveUniqueExtensionlessAddresses()
        {
            const string previewRoot = "Assets/Game/Scenes/GeneratedMaps/HeJiaVillageLocalPreview";
            var addresses = AssetDatabase.FindAssets(string.Empty, new[] { previewRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Select(path => Path.GetDirectoryName(path)?.Replace('\\', '/') + "/" +
                    Path.GetFileNameWithoutExtension(path))
                .ToArray();

            Assert.That(addresses, Is.Unique,
                "预览资源同目录同名时，YooAsset 的无扩展名地址会冲突。");
        }
    }
}
