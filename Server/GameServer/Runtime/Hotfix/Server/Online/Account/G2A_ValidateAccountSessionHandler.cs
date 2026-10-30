namespace ET.Server
{
    [MessageHandler(SceneType.Account)]
    [FriendOf(typeof(OnlineAccountComponent))]
    public class G2A_ValidateAccountSessionHandler : MessageHandler<Scene,G2A_ValidateAccountSession,A2G_ValidateAccountSession>
    {
        protected override async ETTask Run(Scene scene,G2A_ValidateAccountSession request,A2G_ValidateAccountSession response)
        {
            try { response.Error = await scene.GetComponent<OnlineAccountComponent>().Authentication.ValidateSessionAsync(request.AccountId,request.SessionEpoch,default); }
            catch (System.Exception exception) { Log.Warning($"账号会话校验失败：{exception.GetType().Name}"); response.Error=ErrorCode.ERR_OnlineServiceUnavailable; }
        }
    }
}
