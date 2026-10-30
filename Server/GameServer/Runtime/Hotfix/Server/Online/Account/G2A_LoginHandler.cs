namespace ET.Server
{
    [MessageHandler(SceneType.Account)]
    [FriendOf(typeof(OnlineAccountComponent))]
    public class G2A_LoginHandler: MessageHandler<Scene, G2A_Login, A2G_Login>
    {
        protected override async ETTask Run(Scene scene, G2A_Login request, A2G_Login response)
        {
            OnlineAccountComponent accountComponent = scene.GetComponent<OnlineAccountComponent>();
            try
            {
            var result = await accountComponent.Authentication.LoginAsync(request.UserName, request.Password, default);
            response.Error = result.Error;
            response.Message = string.Empty;
            response.AccountId = result.AccountId;
            response.PlayerId = result.PlayerId;
            response.DisplayName = result.DisplayName;
            response.Revision = result.ProfileRevision;
            response.IsNewAccount = false;
            response.SessionEpoch = result.SessionEpoch;
            }
            catch (System.Exception exception)
            {
                Log.Warning($"账号登录失败：{exception.GetType().Name}");
                response.Error = ErrorCode.ERR_OnlineServiceUnavailable;
            }
            finally { request.Password = string.Empty; }
            await ETTask.CompletedTask;
        }
    }
}
