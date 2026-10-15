namespace ET.Server
{
    [MessageHandler(SceneType.Account)]
    public class G2A_LoginHandler: MessageHandler<Scene, G2A_Login, A2G_Login>
    {
        protected override async ETTask Run(Scene scene, G2A_Login request, A2G_Login response)
        {
            OnlineAccountComponent accountComponent = scene.GetComponent<OnlineAccountComponent>();
            var result = accountComponent.Login(request.UserName, request.Password);
            response.Error = result.Error;
            response.Message = result.Message;
            response.AccountId = result.AccountId;
            response.PlayerId = result.PlayerId;
            response.DisplayName = result.DisplayName;
            response.Revision = result.Revision;
            response.IsNewAccount = result.IsNewAccount;
            if (result.Error == ErrorCode.ERR_Success)
            {
                OnlinePlayerProfileRecord profile = accountComponent.GetProfile(result.AccountId, result.PlayerId);
                response.OnlineInventory = OnlineInventoryRecordHelper.ToMessage(profile.OnlineInventory);
                response.HasReceivedOnlineStarterSupplies = profile.HasReceivedOnlineStarterSupplies;
            }
            await ETTask.CompletedTask;
        }
    }
}
