using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Tests
{
    /// <summary>开始菜单任务入口必须有正式序列化按钮，不依赖编辑器窗口。</summary>
    public sealed class MissionMenuTests
    {
        [Test] public void TaskButtonIsSerializedBetweenArcadeAndSurvivor()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
            var ui=prefab.GetComponent<StartUI>();var field=typeof(StartUI).GetField("BtnMission");
            Assert.That(field,Is.Not.Null,"正式任务入口字段缺失");var button=(Button)field.GetValue(ui);
            Assert.That(button,Is.Not.Null,"任务按钮未序列化");Assert.That(button.transform.parent,Is.EqualTo(ui.BtnNewGame.transform.parent));
            Assert.That(button.transform.GetSiblingIndex(),Is.EqualTo(ui.BtnNewGame.transform.GetSiblingIndex()+1));
            Assert.That(ui.BtnSurvivor.transform.GetSiblingIndex(),Is.GreaterThan(button.transform.GetSiblingIndex()));
        }
    }
}
