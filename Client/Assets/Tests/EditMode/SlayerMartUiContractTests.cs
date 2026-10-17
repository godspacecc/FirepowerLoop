using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>反序列化正式宿主与外部卡片，不以YAML文本存在代替可用引用。</summary>
    public sealed class SlayerMartUiContractTests
    {
        private const string Host="Assets/Game/Prefabs/UI/Arcade/ArcadeShop.prefab";
        private const string Box="Assets/Game/Prefabs/UI/Arcade/Box/ArcadeShopOfferBox.prefab";
        private static GameObject Prefab=>AssetDatabase.LoadAssetAtPath<GameObject>(Host);
        private static T Ref<T>(string key) where T:Object
        {var e=Prefab.GetComponent<ReferenceCollector>().data.SingleOrDefault(x=>x.key==key);Assert.That(e,Is.Not.Null,"正式引用缺失："+key);Assert.That(e.gameObject,Is.InstanceOf<T>(),key);return (T)e.gameObject;}
        [Test] public void HostAndBoxKeepOriginalResourceGuids()
        {Assert.That(AssetDatabase.AssetPathToGUID(Host),Is.EqualTo("43e180362c86df749bddcd61ccad2813"));Assert.That(AssetDatabase.AssetPathToGUID(Box),Is.EqualTo("3cb2c0a56a29d55468fd843429a6d47a"));}
        [Test] public void CardsComeFromExternalGeneratedBox()
        {var list=Ref<CustomListView>("OfferList");Assert.That(AssetDatabase.GetAssetPath(list.itemPrefab),Is.EqualTo(Box));Assert.That(list.itemPrefab.GetComponent<ArcadeShopOfferBoxUI>(),Is.Not.Null);}
        [Test] public void GridShowsFourColumnsOfFourCards()
        {var grid=Ref<CustomListView>("OfferList").GetComponent<GridLayoutGroup>();Assert.That(grid,Is.Not.Null);Assert.That(grid.constraint,Is.EqualTo(GridLayoutGroup.Constraint.FixedColumnCount));Assert.That(grid.constraintCount,Is.EqualTo(4));Assert.That(grid.cellSize.x,Is.GreaterThanOrEqualTo(240));Assert.That(grid.cellSize.y,Is.GreaterThanOrEqualTo(145));}
        [Test] public void HostContainsExplicitChoiceLoadingAndShoppingRoots()
        {foreach(var key in new[]{"ChoiceRoot","LoadingRoot","ShoppingRoot"})Assert.That(Ref<GameObject>(key),Is.Not.Null);}
        [Test] public void HostContainsSeparateRerollFinishCloseAndRetryActions()
        {foreach(var key in new[]{"BtnEnter","BtnSkip","BtnReroll","BtnFinish","BtnCloseMenu","BtnRetry","BtnBuy"})Assert.That(Ref<Button>(key),Is.Not.Null);}
        [Test] public void OldSettlementAbandonAndChallengeButtonsAreRemoved()
        {foreach(var key in new[]{"BtnAbandon","BtnSettle","BtnChallenge","BtnContinue"})Assert.That(Prefab.GetComponentsInChildren<Transform>(true).Any(t=>t.name==key),Is.False,key);}
        [Test] public void QuotesHaveDedicatedMoneyTokensDeadlineAndMemberStatus()
        {
            foreach(var key in new[]{"TextCash","TextTokens","TextDeadline","TextMembers","TextMessage","TextDetailName","TextDetailEffect"})Assert.That(Ref<TextMeshProUGUI>(key).fontSize,Is.GreaterThanOrEqualTo(22));
            var name=Ref<TextMeshProUGUI>("TextDetailName").rectTransform;var effect=Ref<TextMeshProUGUI>("TextDetailEffect").rectTransform;
            Assert.That(effect.anchoredPosition.y+(1-effect.pivot.y)*effect.rect.height,Is.LessThanOrEqualTo(name.anchoredPosition.y-name.pivot.y*name.rect.height-20),"详情首行不能覆盖商品名");
        }
        [Test] public void AllGeneratedHostReferencesAreSerializedAndNonNull()
        {var view=Prefab.GetComponent<ArcadeShopUI>();Assert.That(view,Is.Not.Null);foreach(var f in typeof(ArcadeShopUI).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly))Assert.That(f.GetValue(view),Is.Not.Null,f.Name);}
        [Test] public void AllGeneratedBoxReferencesAreSerializedAndNonNull()
        {var view=AssetDatabase.LoadAssetAtPath<GameObject>(Box).GetComponent<ArcadeShopOfferBoxUI>();Assert.That(view,Is.Not.Null);foreach(var f in typeof(ArcadeShopOfferBoxUI).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.DeclaredOnly))Assert.That(f.GetValue(view),Is.Not.Null,f.Name);}
        [Test] public void GridHasEnoughSpaceForSixteenVisibleCards()
        {var list=Ref<CustomListView>("OfferList");var grid=list.GetComponent<GridLayoutGroup>();Assert.That(grid,Is.Not.Null);var r=(RectTransform)list.transform;Assert.That(r.rect.width,Is.GreaterThanOrEqualTo(grid.cellSize.x*4+grid.spacing.x*3));Assert.That(r.rect.height,Is.GreaterThanOrEqualTo(grid.cellSize.y*4+grid.spacing.y*3));Assert.That(list.transform.childCount,Is.Zero);}
        [Test] public void ListRefreshReusesExactlySixteenCards()
        {var root=Object.Instantiate(Prefab);try{var list=root.GetComponentInChildren<CustomListView>(true);list.totalCount=16;list.RefreshCells();var ids=list.GetComponentsInChildren<ArcadeShopOfferBoxUI>(true).Select(v=>v.GetInstanceID()).ToArray();list.RefreshCells();Assert.That(ids.Length,Is.EqualTo(16));Assert.That(list.GetComponentsInChildren<ArcadeShopOfferBoxUI>(true).Select(v=>v.GetInstanceID()),Is.EqualTo(ids));}finally{Object.DestroyImmediate(root);}}
        [Test] public void StaticLabelsProvideReadableDefaultLanguage()
        {
            var labels=Prefab.GetComponentsInChildren<TextMeshProUGUI>(true);Assert.That(labels.Any(t=>t.text.Contains("进入商店")),Is.True);Assert.That(labels.Any(t=>t.text.Contains("重抽")),Is.True);Assert.That(labels.Any(t=>t.text.Contains("完成购物")),Is.True);Assert.That(labels.All(t=>!t.text.StartsWith("ui.arcade.")),Is.True);
            foreach(var text in labels.Where(t=>t.gameObject.activeSelf))Assert.That(text.GetPreferredValues(text.text,text.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.1f),text.name+"默认字体行高超出文本框会导致空白");
            foreach(var text in AssetDatabase.LoadAssetAtPath<GameObject>(Box).GetComponentsInChildren<TextMeshProUGUI>().Where(t=>t.gameObject.activeSelf))Assert.That(text.GetPreferredValues(text.text,text.rectTransform.rect.width,float.PositiveInfinity).y,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.1f),text.name+"商品卡字体必须可见");
            var message=Ref<TextMeshProUGUI>("TextMessage").rectTransform;var button=(RectTransform)Ref<Button>("BtnReroll").transform;
            Assert.That(message.anchoredPosition.y-message.pivot.y*message.rect.height,Is.GreaterThanOrEqualTo(button.anchoredPosition.y+(1-button.pivot.y)*button.rect.height+5),"反馈不能被底部按钮遮盖");
        }
    }
}
