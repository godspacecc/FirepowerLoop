namespace ET.Server
{
    [MessageHandler(SceneType.Account)]
    [FriendOf(typeof(OnlineAccountComponent))]
    public class G2A_RegisterHandler: MessageHandler<Scene, G2A_Register, A2G_Register>
    {
        protected override async ETTask Run(Scene scene, G2A_Register request, A2G_Register response)
        {
            OnlineAccountComponent accountComponent = scene.GetComponent<OnlineAccountComponent>();
            try
            {
            var result = await accountComponent.Authentication.RegisterAsync(request.UserName, request.Password, default);
            response.Error = result.Error;
            response.Message = string.Empty;
            response.AccountId = result.AccountId;
            }
            catch (System.Exception exception)
            {
                Log.Warning($"账号注册失败：{exception.GetType().Name}");
                response.Error = ErrorCode.ERR_OnlineServiceUnavailable;
            }
            finally { request.Password = string.Empty; }
            await ETTask.CompletedTask;
        }
    }
}
