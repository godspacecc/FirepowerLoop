using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>检查正式Prefab反序列化后的引用，不把YAML非零fileID当作绑定成功。</summary>
    public sealed class ScoreShopUiTests
    {
        private const string Root="Assets/Game/Prefabs/UI/Arcade/";
        /// <summary>验证真实采集结果包含商店运行时地址，防止Prefab存在却无法加载。</summary>
        [Test] public void Dialog_IsCollectedAtRuntimeAddressWithArcadeTag()
        {
            var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
            var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
            var assets=(System.Collections.IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result);
            var matches=assets.Cast<object>().Where(asset=>(string)asset.GetType().GetProperty("Address").GetValue(asset)=="Prefabs/UI/Arcade/ArcadeScoreShop").ToArray();
            Assert.That(matches,Has.Length.EqualTo(1),"积分商店必须能够按运行时地址加载");
            Assert.That(matches[0].GetType().GetProperty("AssetTags").GetValue(matches[0]),Does.Contain("arcade"));
        }
        [Test] public void Dialog_HasGeneratedViewAndEveryRequiredReference()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"ArcadeScoreShop.prefab");Assert.That(prefab,Is.Not.Null);
            Assert.That(prefab.GetComponent<ReferenceCollector>(),Is.Not.Null);
            var view=prefab.GetComponents<Component>().SingleOrDefault(c=>c!=null&&c.GetType().Name=="ArcadeScoreShopUI");Assert.That(view,Is.Not.Null);
            foreach(var name in new[]{"OfferList","BtnRefresh","BtnSkip","TextScore","TextMessage","TextRefresh"})
                Assert.That(view.GetType().GetField(name)?.GetValue(view),Is.Not.Null,name);
        }
        [Test] public void Offers_UseExternalBoxWithGeneratedBuyAndTextBindings()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"ArcadeScoreShop.prefab");Assert.That(prefab,Is.Not.Null);
            var list=prefab.GetComponentInChildren<CustomListView>(true);Assert.That(list,Is.Not.Null);Assert.That(list.itemPrefab,Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(list.itemPrefab),Is.EqualTo(Root+"Box/ArcadeScoreShopOfferBox.prefab"));
            Assert.That(list.itemPrefab.GetComponent<ReferenceCollector>(),Is.Not.Null);
            var view=list.itemPrefab.GetComponents<Component>().SingleOrDefault(c=>c!=null&&c.GetType().Name=="ArcadeScoreShopOfferBoxUI");Assert.That(view,Is.Not.Null);
            foreach(var name in new[]{"BtnBuy","TextType","TextName","TextDescription","TextPrice","TextBuy","Icon"})
                Assert.That(view.GetType().GetField(name)?.GetValue(view),Is.Not.Null,name);
        }
        [Test] public void FixedLabels_AreReadableAndDynamicFieldsHaveNoLocalizationWriter()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"ArcadeScoreShop.prefab");Assert.That(prefab,Is.Not.Null);
            foreach(var text in prefab.GetComponentsInChildren<TMP_Text>(true))Assert.That(text.text.StartsWith("ui."),Is.False,text.name);
            var view=prefab.GetComponents<Component>().Single(c=>c!=null&&c.GetType().Name=="ArcadeScoreShopUI");
            foreach(var field in view.GetType().GetFields().Where(f=>typeof(TMP_Text).IsAssignableFrom(f.FieldType)))
                Assert.That(((TMP_Text)field.GetValue(view)).GetComponent<LocalizedText>(),Is.Null,field.Name);
            Assert.That(prefab.GetComponentsInChildren<LocalizedText>(true).Length,Is.GreaterThanOrEqualTo(4));
            foreach(var localized in prefab.GetComponentsInChildren<LocalizedText>(true))
            {
                foreach(var language in new[]{"zh-CN","en-US"})
                {
                    var table=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"Game/Config/Localization",language+".json")));
                    Assert.That(table[localized.Key]?.Value<string>(),Is.Not.Null.And.Not.Empty,language+":"+localized.Key);
                    if(language=="zh-CN")Assert.That(localized.GetComponent<TMP_Text>().text,Is.EqualTo(table[localized.Key].Value<string>()),localized.Key);
                }
            }
        }
    }
}
