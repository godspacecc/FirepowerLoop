using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>任务沿用闯关单人入口，主菜单保留独立的普通幸存者入口。</summary>
    public sealed class MissionMenuTests
    {
        [Test]
        public void ChapterUsesArcadeSoloWithoutSeparateMissionButton()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var menu = prefab.GetComponent<StartUI>();
            var buttons = menu.GetComponentsInChildren<Button>(true);
            Assert.That(buttons.Any(b => b.name == "BtnMission"), Is.False);
            Assert.That(menu.BtnNewGame.transform.IsChildOf(menu.MainMenuPanel.transform), Is.True);
            Assert.That(menu.BtnArcadeSolo.transform.IsChildOf(menu.ArcadeModePanel.transform), Is.True);
            Assert.That(menu.BtnSurvivor.transform.IsChildOf(menu.MainMenuPanel.transform), Is.True);
        }
    }
}
