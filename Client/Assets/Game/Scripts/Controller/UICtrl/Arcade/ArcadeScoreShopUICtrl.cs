using System;
using System.Globalization;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>整关后个人三选一窗口；展示权威积分和商品，按钮只提交交易意图。</summary>
    public sealed class ArcadeScoreShopUICtrl : BaseUICtrl
    {
        private const string Key="ui.arcade.score_shop.";
        /// <summary>正式只读目录，价格与效果不由界面自行决定。</summary>
        private ArcadeConfigCatalogDefinition catalog;
        /// <summary>本次绘制的个人快照，先赋值再刷新列表。</summary>
        private ArcadeScoreShopState quote;
        /// <summary>显示代次及请求等待，防止旧回调修改重开的窗口。</summary>
        private int displayGeneration;
        private bool requestBusy;
        private long shownScore;
        private string lastError;
        private ArcadeScoreShopUI UI=>BaseUI as ArcadeScoreShopUI;
        private OnlineArcadeRunSystem Online=>this.GetSystem<OnlineArcadeRunSystem>();
        private bool OnlineMode=>Online?.Snapshot.HasRun==true;
        protected override string Path()=>"Arcade/ArcadeScoreShop";
        public override UILayer Layer()=>UILayer.Dialog;
        public override bool BlocksGame=>true;
        /// <summary>只绑定一次正式按钮和快照事件，重复打开不会追加交易回调。</summary>
        protected override void OnInit()
        {
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out catalog,out var error)||catalog.ScoreShop==null)
                throw new InvalidOperationException(error??"积分商店配置不可用");
            UI.OfferList.AddItemRefreshListener(RenderCard);
            UI.BtnRefresh.onClick.AddListener(()=>OperateClicked(ScoreShopAction.Refresh,null));
            UI.BtnSkip.onClick.AddListener(()=>OperateClicked(ScoreShopAction.Skip,null));
            this.RegisterEvent<ArcadeRunChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow(){displayGeneration++;requestBusy=false;lastError=null;Refresh();}
        /// <summary>隐藏释放焦点，网络结果仍由System接收，不再更新旧View。</summary>
        protected override void OnHide()
        {
            displayGeneration++;
            if(EventSystem.current?.currentSelectedGameObject is { } selected&&selected.transform.IsChildOf(UI.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        protected override void OnUnload(){displayGeneration++;requestBusy=false;}
        private ArcadeScoreShopState ReadQuote()=>OnlineMode?Online.GetScoreShopState():this.GetModel<ArcadeRunModel>().Current?.ScoreShop;
        /// <summary>结果不确定的联机交易只能重试原意图，不能借重开窗口改换商品。</summary>
        private bool CanSend(ScoreShopAction action,string id)
            =>!requestBusy&&(!OnlineMode||!Online.HasPendingScoreShopOperation||
                Online.PendingScoreShopAction==action&&(Online.PendingScoreShopDropId??string.Empty)==(id??string.Empty));
        private void Refresh()
        {
            quote=ReadQuote();
            bool safe=OnlineMode?Online.Snapshot.Phase==OnlineArcadeRunPhase.SafeCheckpoint:
                this.GetModel<ArcadeRunModel>().Phase==ArcadeRunPhase.SafeCheckpoint;
            if(quote==null||quote.Completed||!safe){Hide();return;}
            shownScore=OnlineMode?Online.Snapshot.Players.FirstOrDefault(p=>p.PlayerId==this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId).Score:
                this.GetModel<ArcadeRunModel>().Current.Score;
            UI.TextScore.text=shownScore.ToString("N0",CultureInfo.InvariantCulture);
            UI.TextRefresh.text=Loc.T(Key+"refresh",catalog.ScoreShop.RefreshCost);
            UI.TextMessage.text=requestBusy?Loc.T(Key+"waiting"):lastError??string.Empty;
            bool retryRefresh=OnlineMode&&Online.HasPendingScoreShopOperation&&Online.PendingScoreShopAction==ScoreShopAction.Refresh;
            UI.BtnRefresh.interactable=CanSend(ScoreShopAction.Refresh,null)&&(retryRefresh||shownScore>=catalog.ScoreShop.RefreshCost&&
                catalog.ScoreShop.Items.Any(i=>i.Eligible(quote.NextArena)&&!quote.Offers.Contains(i.DropId)));
            UI.BtnSkip.interactable=CanSend(ScoreShopAction.Skip,null);
            UI.OfferList.totalCount=quote.Offers.Count;UI.OfferList.RefreshCells();
        }
        /// <summary>复用卡片完整重绑文字、图标和按钮，不残留上一组商品及监听。</summary>
        private void RenderCard(Transform transform,int index)
        {
            string id=quote.Offers[index];var item=catalog.ScoreShop.Items.First(i=>i.DropId==id);
            var drop=catalog.Drops.First(d=>d.Id==id);bool drone=drop.Kind==global::ArcadeDropKind.Support;
            var card=transform.GetComponent<ArcadeScoreShopOfferBoxUI>();
            card.TextType.text=Loc.T(Key+(drone?"support":"weapon"),drop.DurationSeconds.ToString("0.#",CultureInfo.InvariantCulture));
            card.TextName.text=Loc.T("ui.arcade.product."+id.Replace("arcade.drop.","arcade.")+".name");
            card.TextDescription.text=Loc.T(Key+(drone?"drone_description":"weapon_description"));
            card.TextPrice.text=Loc.T(Key+"price",item.Price.ToString("N0",CultureInfo.InvariantCulture));
            bool affordable=shownScore>=item.Price;card.TextBuy.text=Loc.T(Key+(affordable?"buy":"insufficient"));
            card.Icon.Drone=drone;card.Icon.SetVerticesDirty();
            bool retry=OnlineMode&&Online.HasPendingScoreShopOperation&&Online.PendingScoreShopAction==ScoreShopAction.Buy&&Online.PendingScoreShopDropId==id;
            card.BtnBuy.interactable=CanSend(ScoreShopAction.Buy,id)&&(affordable||retry);
            card.BtnBuy.onClick.RemoveAllListeners();card.BtnBuy.onClick.AddListener(()=>OperateClicked(ScoreShopAction.Buy,id));
        }
        /// <summary>捕获报价版本后提交，失败保留积分和商品，成功事件关闭已完成访问。</summary>
        private async void OperateClicked(ScoreShopAction action,string id)
        {
            if(!IsShow()||quote==null||quote.Completed||!CanSend(action,id))return;
            int revision=quote.Revision,generation=displayGeneration;var owner=BaseUI;
            requestBusy=true;lastError=null;Refresh();
            try
            {
                bool accepted;
                if(OnlineMode)
                {
                    var result=await (Online.HasPendingScoreShopOperation?Online.RetryPendingScoreShopAsync():Online.OperateScoreShopAsync(action,id));
                    accepted=result.Success;
                }
                else accepted=this.GetSystem<ArcadeRunSystem>().TryOperateScoreShop(action,id,revision,out _,out _);
                if(generation==displayGeneration&&ReferenceEquals(owner,BaseUI)&&!accepted)lastError=Loc.T(Key+"failed");
            }
            catch(Exception exception)
            {
                Log.Error("[ArcadeScoreShopUICtrl] 积分交易未完成："+exception);
                if(generation==displayGeneration&&ReferenceEquals(owner,BaseUI))lastError=Loc.T(Key+"failed");
            }
            finally
            {
                if(generation==displayGeneration&&ReferenceEquals(owner,BaseUI)){requestBusy=false;if(IsShow())Refresh();}
            }
        }
    }
}
