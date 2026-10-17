using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FirepowerLoop.Arcade;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>保留原资源身份的SlayerMart界面；只展示本人报价并发意图，不拥有经济或计时。</summary>
    public sealed class ArcadeShopUICtrl : BaseUICtrl
    {
        private const string Key="ui.arcade.slayermart.";
        private ArcadeShopUI UI=>BaseUI as ArcadeShopUI;
        private ArcadeSlayerMartConfiguration config;
        /// <summary>选择与等待属于本次显示；真实报价及原请求重试由System保存。</summary>
        private ArcadeSlayerMartSnapshot quote;
        private string selectedItem,regionIdentity,lastError;
        private bool updateRegistered,requestBusy,menuCloseConfirmed,finishAfterClose,hideAfterClose;
        private int displayGeneration;
        private float nextRefresh;
        private OnlineArcadeRunSystem Online=>this.GetSystem<OnlineArcadeRunSystem>();
        private bool OnlineMode=>Online?.Snapshot.HasRun==true;
        private bool Waiting=>requestBusy||OnlineMode&&Online.HasPendingSlayerMartOperation;
        protected override string Path()=>"Arcade/ArcadeShop";
        public override UILayer Layer()=>UILayer.Dialog;
        public override bool BlocksGame=>false;
        protected override bool CanHide(){var snapshot=ReadSnapshot();return snapshot==null||snapshot.Stage==ArcadeSlayerMartStage.Closing||!Waiting;}
        /// <summary>一次绑定正式按钮和只读快照事件，不在重复Show时叠加监听。</summary>
        protected override void OnInit()
        {
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerMart(out config,out var error))throw new InvalidOperationException(error);
            UI.OfferList.AddItemRefreshListener(RenderCard);
            UI.BtnBuy.onClick.AddListener(BuyClicked);UI.BtnReroll.onClick.AddListener(()=>OperateClicked(SlayerMartAction.Reroll));
            UI.BtnEnter.onClick.AddListener(()=>OperateClicked(SlayerMartAction.SelectEnter));UI.BtnSkip.onClick.AddListener(()=>OperateClicked(SlayerMartAction.SelectSkip));
            UI.BtnCloseMenu.onClick.AddListener(CloseClicked);UI.BtnFinish.onClick.AddListener(FinishClicked);UI.BtnRetry.onClick.AddListener(RetryClicked);
            this.RegisterEvent<LanguageChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<ArcadeCombatHudChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        /// <summary>重开显示只重置界面代次，保留System的原交易及本人报价。</summary>
        protected override void OnShow()
        {
            bool first=!updateRegistered;if(first){displayGeneration++;requestBusy=false;LifeCycle.RegisterUpdate(OnUpdate);updateRegistered=true;}
            Refresh();if(first&&quote?.Stage==ArcadeSlayerMartStage.Shopping&&!OwnMenuOpen()&&!Waiting)OperateClicked(SlayerMartAction.SetMenuOpen,true);
        }
        /// <summary>释放刷新与本窗口焦点；外部关闭只提交关菜单，不标记完成。</summary>
        protected override void OnHide()
        {
            displayGeneration++;if(updateRegistered){LifeCycle.UnregisterUpdate(OnUpdate);updateRegistered=false;}
            if(EventSystem.current?.currentSelectedGameObject is { } selected&&selected.transform.IsChildOf(UI.transform))EventSystem.current.SetSelectedGameObject(null);
            if(!menuCloseConfirmed&&quote?.Stage==ArcadeSlayerMartStage.Shopping&&OwnMenuOpen()&&!Waiting)OperateClicked(SlayerMartAction.SetMenuOpen,false);
        }
        protected override void OnUnload(){if(updateRegistered)LifeCycle.UnregisterUpdate(OnUpdate);updateRegistered=false;}
        private void OnUpdate(){if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.1f;Refresh();}
        private ArcadeSlayerMartSnapshot ReadSnapshot()=>OnlineMode?Online.GetSlayerMartSnapshot():this.GetSystem<ArcadeCombatSystem>()?.GetSlayerMartSnapshot();
        private bool OwnMenuOpen()=>quote?.Members.Any(m=>m.PlayerId==quote.PlayerId&&m.MenuOpen)==true;
        /// <summary>切区清理选择；开关、语言及快照刷新不会发送重抽。</summary>
        private void Refresh()
        {
            quote=ReadSnapshot();if(quote==null||quote.Stage==ArcadeSlayerMartStage.Closing){Hide();return;}
            string identity=quote.RegionInstanceId+":"+quote.Generation;
            if(identity!=regionIdentity){if(regionIdentity!=null){displayGeneration++;requestBusy=false;}regionIdentity=identity;selectedItem=null;lastError=null;finishAfterClose=hideAfterClose=false;}
            UI.ChoiceRoot.SetActive(quote.Stage==ArcadeSlayerMartStage.Choosing);UI.LoadingRoot.SetActive(quote.Stage==ArcadeSlayerMartStage.Loading);UI.ShoppingRoot.SetActive(quote.Stage==ArcadeSlayerMartStage.Shopping);
            ApplyStaticLabels();
            UI.TextCash.text=Loc.T(Key+"cash",quote.Cash.ToString("N0"));UI.TextTokens.text=Loc.T(Key+"tokens",quote.Tokens);UI.TextDeadline.text=Loc.T(Key+"deadline",Math.Max(0,(int)Math.Ceiling(quote.RemainingSeconds)));
            UI.TextMembers.text=string.Join(" | ",quote.Members.Select(m=>Loc.T(Key+"member",MemberName(m.PlayerId),Loc.T(Key+MemberStatus(m)))));
            foreach(var pair in new[]{(UI.BtnBuy,"buy"),(UI.BtnReroll,"reroll"),(UI.BtnEnter,"enter"),(UI.BtnSkip,"skip"),(UI.BtnFinish,"finish"),(UI.BtnCloseMenu,"close"),(UI.BtnRetry,"retry")})pair.Item1.GetComponentInChildren<TextMeshProUGUI>(true).text=Loc.T(Key+pair.Item2);
            string resourceError=string.IsNullOrEmpty(quote.Error)?this.GetUtility<IArcadeCombatViewPort>()?.SlayerMartRegionLoadError:quote.Error;
            UI.TextMessage.text=quote.Stage==ArcadeSlayerMartStage.Shopping&&quote.RemainingSeconds<=5?Loc.T(Key+"closing_soon"):Waiting?Loc.T(Key+"waiting_receipt"):!string.IsNullOrEmpty(resourceError)?resourceError:!string.IsNullOrEmpty(lastError)?ErrorText(lastError):Loc.T(Key+(quote.Stage==ArcadeSlayerMartStage.Choosing?"choose_help":quote.Stage==ArcadeSlayerMartStage.Loading?"loading_help":"shopping_help"));
            UI.BtnEnter.interactable=UI.BtnSkip.interactable=!Waiting;UI.BtnRetry.gameObject.SetActive(OnlineMode&&Online.HasPendingSlayerMartOperation||!string.IsNullOrEmpty(resourceError));UI.BtnRetry.interactable=!requestBusy;
            bool shopping=quote.Stage==ArcadeSlayerMartStage.Shopping;UI.BtnReroll.interactable=shopping&&!Waiting&&quote.Tokens>0;UI.BtnCloseMenu.interactable=UI.BtnFinish.interactable=shopping&&!Waiting;
            if(!shopping)return;if(!quote.Offers.Any(o=>o.Item.Id==selectedItem))selectedItem=quote.Offers.FirstOrDefault()?.Item.Id;
            UI.OfferList.totalCount=quote.Offers.Count;UI.OfferList.RefreshCells();RenderDetail();
        }
        private string MemberName(long id)=>id==quote.PlayerId?Loc.T(Key+"you"):Online?.Snapshot.Players.FirstOrDefault(p=>p.PlayerId==id).DisplayName??Loc.T(Key+"player",id);
        private static string MemberStatus(ArcadeSlayerMartMemberSnapshot m)=>m.Finished?"member_finished":m.MenuOpen?"member_shopping":m.Choice==1?"member_enter":m.Choice==2?"member_skip":"member_choosing";
        private string Scope(ArcadeSlayerMartItem item)=>Loc.T(Key+(item.Effect==SlayerMartEffect.NextRoundLife?"scope_team":"scope_personal"));
        private string Limit(ArcadeSlayerMartItem item)=>Loc.T(Key+(item.Effect==SlayerMartEffect.Plate?"repeatable":item.OncePerRun?"limit_run":"limit_visit"));
        /// <summary>固定标题沿正式Prefab的状态根定位，语言变化只替换文字，不重新生成报价。</summary>
        private void ApplyStaticLabels()
        {
            UI.ChoiceRoot.transform.Find("ChoiceTitle").GetComponent<TextMeshProUGUI>().text=Loc.T(Key+"choice_title");
            UI.ChoiceRoot.transform.Find("ChoiceBody").GetComponent<TextMeshProUGUI>().text=Loc.T(Key+"choice_body");
            UI.LoadingRoot.transform.Find("LoadingTitle").GetComponent<TextMeshProUGUI>().text=Loc.T(Key+"loading_title");
        }
        /// <summary>列表组件管理实例，卡片只绑定本人报价和选择意图。</summary>
        private void RenderCard(Transform transform,int index)
        {
            var offer=quote.Offers[index];var card=transform.GetComponent<ArcadeShopOfferBoxUI>();string id=offer.Item.Id;
            card.TextName.text=Loc.T("ui.arcade."+id+".name");card.TextType.text=Scope(offer.Item)+" · "+Limit(offer.Item);card.TextPrice.text=offer.Item.Price.ToString("N0");
            card.TextState.text=Loc.T(Key+(string.IsNullOrEmpty(offer.Error)?"available":"unavailable"));card.TextEffect.gameObject.SetActive(false);card.SelectedRoot.SetActive(id==selectedItem);card.DisabledRoot.SetActive(!string.IsNullOrEmpty(offer.Error));
            card.BtnSelect.onClick.RemoveAllListeners();card.BtnSelect.onClick.AddListener(()=>{selectedItem=id;UI.OfferList.RefreshCells();RenderDetail();});
        }
        /// <summary>容量及加命预览只解释权威库存，不代替System前置裁决。</summary>
        private void RenderDetail()
        {
            var offer=quote.Offers.FirstOrDefault(o=>o.Item.Id==selectedItem);UI.BtnBuy.interactable=offer!=null&&!Waiting&&string.IsNullOrEmpty(offer.Error);
            if(offer==null){UI.TextDetailName.text=UI.TextDetailEffect.text=string.Empty;return;}
            var item=offer.Item;UI.TextDetailName.text=Loc.T("ui.arcade."+item.Id+".name");string details=Loc.T("ui.arcade."+item.Id+".desc")+"\n\n"+Scope(item)+" · "+Limit(item);
            if(item.Effect!=SlayerMartEffect.Plate)details+="\n"+Loc.T(Key+"remaining_limit",offer.Error is "already_owned" or "visit_limit" or "team_life_unavailable" or "vest_owned"?0:1);
            if(item.Effect==SlayerMartEffect.Bomb||item.Effect==SlayerMartEffect.Dash)details+="\n"+Loc.T(Key+"amount_preview",ActualAmount(item),item.Amount);
            if(item.Effect==SlayerMartEffect.NextRoundLife)details+="\n"+string.Join("\n",quote.Members.Select(m=>Loc.T(Key+(m.Lives>=config.Catalog.MaxLives?"life_capped":"life_next"),MemberName(m.PlayerId))));
            if(!string.IsNullOrEmpty(offer.Error))details+="\n\n"+ErrorText(offer.Error);UI.TextDetailEffect.text=details;
        }
        /// <summary>仅预览炸弹和冲刺容量，付款前仍由领域确认实际数量。</summary>
        private int ActualAmount(ArcadeSlayerMartItem item)=>item.Effect==SlayerMartEffect.Bomb?Math.Min(item.Amount,Math.Max(0,config.Catalog.MaxBombs-quote.Bombs)):item.Effect==SlayerMartEffect.Dash?Math.Min(item.Amount,Math.Max(0,config.Catalog.MaxDashes-quote.Dashes)):item.Amount;
        /// <summary>容量截断弹窗捕获原报价，确认时不能偷换成后来的商品或价格。</summary>
        private async void BuyClicked()
        {
            if(Waiting||quote?.Stage!=ArcadeSlayerMartStage.Shopping)return;var offer=quote.Offers.FirstOrDefault(o=>o.Item.Id==selectedItem);if(offer==null||!string.IsNullOrEmpty(offer.Error))return;
            var snapshot=quote;int amount=ActualAmount(offer.Item);string itemId=offer.Item.Id;
            if((offer.Item.Effect==SlayerMartEffect.Bomb||offer.Item.Effect==SlayerMartEffect.Dash)&&amount<offer.Item.Amount)
            {
                Notify.ShowMessage(Loc.T(Key+"confirm_title"),Loc.T(Key+"confirm_truncated",amount,offer.Item.Amount,offer.Item.Price.ToString("N0")),new List<MessageButtonConfig>{new(){ButtonText=Loc.T("ui.common.confirm"),Role=MessageButtonRole.Confirm,OnClick=()=>ConfirmBuy(snapshot,itemId,amount)},new(){ButtonText=Loc.T("ui.common.cancel"),Role=MessageButtonRole.Cancel}});return;
            }
            await SendAsync(SlayerMartAction.Buy,false,itemId,amount,snapshot);
        }
        private async void ConfirmBuy(ArcadeSlayerMartSnapshot snapshot,string item,int amount){await SendAsync(SlayerMartAction.Buy,false,item,amount,snapshot);}
        private async void OperateClicked(SlayerMartAction action,bool value=false){await SendAsync(action,value);}
        /// <summary>关菜单须获权威确认，不能把未确认状态当作已完成购物。</summary>
        private async void CloseClicked(){if(Waiting)return;hideAfterClose=true;if(await SendAsync(SlayerMartAction.SetMenuOpen,false))FinishUiFlow();}
        /// <summary>先关菜单，再提交独立完成意图；每一步可按原ID重试。</summary>
        private async void FinishClicked(){if(Waiting)return;finishAfterClose=true;if(await SendAsync(SlayerMartAction.SetMenuOpen,false))await CompleteAfterClose();}
        /// <summary>完成标记若超时，原请求重试成功后仍须关闭界面；关菜单不会自动标记完成。</summary>
        private async Task CompleteAfterClose(){finishAfterClose=false;hideAfterClose=true;if(await SendAsync(SlayerMartAction.SetFinished,true))FinishUiFlow();}
        private void FinishUiFlow(){finishAfterClose=hideAfterClose=false;menuCloseConfirmed=true;try{Hide();}finally{menuCloseConfirmed=false;}}
        /// <summary>未知结果复用System中的原载荷，确认后继续尚未完成的界面步骤。</summary>
        private async void RetryClicked()
        {
            if(requestBusy)return;
            if(OnlineMode&&Online.HasPendingSlayerMartOperation)
            {
                requestBusy=true;int generation=displayGeneration;bool accepted=false;
                try{var result=await Online.RetryPendingSlayerMartAsync();if(generation!=displayGeneration)return;accepted=result.Accepted;lastError=result.Error??(result.Code==OnlineArcadeResultCode.Success?null:"generic");}
                finally{if(generation==displayGeneration){requestBusy=false;if(IsShow())Refresh();}}
                if(generation!=displayGeneration||!accepted)return;if(finishAfterClose)await CompleteAfterClose();else if(hideAfterClose)FinishUiFlow();
            }
            else if(OnlineMode)this.GetSystem<OnlineArcadePresentationSystem>()?.TryRetrySlayerMartPreparation();else this.GetSystem<ArcadeCombatSystem>()?.TryRetrySlayerMartPreparation();
        }
        /// <summary>复制区域身份并发送；过期异步回调不能更新新显示，未知结果保留原请求。</summary>
        private async Task<bool> SendAsync(SlayerMartAction action,bool value=false,string item=null,int amount=0,ArcadeSlayerMartSnapshot source=null)
        {
            if(Waiting)return false;var state=source??ReadSnapshot();if(state==null||state.Stage==ArcadeSlayerMartStage.Closing)return false;var current=ReadSnapshot();if(current==null||current.RegionInstanceId!=state.RegionInstanceId||current.Generation!=state.Generation)return false;
            var request=new ArcadeSlayerMartRequest{Action=action,PlayerId=state.PlayerId,RegionInstanceId=state.RegionInstanceId,Generation=state.Generation,ExpectedRevision=state.Revision,OperationId=Guid.NewGuid().ToString("N"),ItemId=item,ConfirmedAmount=amount,Value=value};
            requestBusy=true;int generation=displayGeneration;if(IsShow())Refresh();
            try
            {
                bool accepted;if(OnlineMode){var result=await Online.OperateSlayerMartAsync(request);if(generation!=displayGeneration)return false;accepted=result.Accepted;lastError=result.Error??(result.Code==OnlineArcadeResultCode.Success?null:"generic");}else{accepted=this.GetSystem<ArcadeCombatSystem>().TryOperateSlayerMart(request,out var error);lastError=error;}
                if(!accepted&&(!OnlineMode||!Online.HasPendingSlayerMartOperation))finishAfterClose=hideAfterClose=false;return accepted&&generation==displayGeneration;
            }
            finally{if(generation==displayGeneration){requestBusy=false;if(IsShow())Refresh();}}
        }
        private static string ErrorText(string error)=>Loc.T(Key+"error."+(KnownErrors.Contains(error)?error:"generic"));
        private static readonly HashSet<string> KnownErrors=new(StringComparer.Ordinal){"already_owned","cash_unavailable","chicken_capacity","confirm_actual_amount","insufficient_cash","invalid_identity","invalid_player","menu_still_open","no_inventory_change","no_token","not_offered","not_ready","numeric_overflow","operation_payload_changed","plate_capacity","player_dead","stale_revision","supply_full","team_life_unavailable","vest_owned","visit_limit","expired","shop_not_active"};
    }
}
