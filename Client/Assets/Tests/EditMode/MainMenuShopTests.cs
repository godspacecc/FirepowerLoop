using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>开始页商店正式Prefab与生成引用契约。</summary>
    public sealed class MainMenuShopTests
    {
        [Test] public void ShopHasGeneratedReferencesAndMenuEntry()
        {
            var shop=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MainMenuShop.prefab");
            Assert.That(shop,Is.Not.Null,"商店必须为独立正式资源");
            Assert.That(shop.GetComponent<ReferenceCollector>(),Is.Not.Null);
            var view=shop.GetComponent("MainMenuShopUI");
            Assert.That(view,Is.Not.Null,"正式生成器必须附加View");
            foreach(var name in new[]{"BalanceText","ErrorText","BtnBack","BtnClose"})
                Assert.That(view.GetType().GetField(name).GetValue(view),Is.Not.Null,name);
            var start=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>();
            var field=typeof(StartUI).GetField("BtnShop");
            Assert.That(field,Is.Not.Null);Assert.That(field.GetValue(start),Is.Not.Null);
        }
    }
}
