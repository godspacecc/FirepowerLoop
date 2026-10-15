namespace ET.Server
{
    [MessageHandler(SceneType.Account)]
    public class G2A_RegisterHandler: MessageHandler<Scene, G2A_Register, A2G_Register>
    {
        protected override async ETTask Run(Scene scene, G2A_Register request, A2G_Register response)
        {
            OnlineAccountComponent accountComponent = scene.GetComponent<OnlineAccountComponent>();
            var result = accountComponent.Register(request.UserName, request.Password);
            response.Error = result.Error;
            response.Message = result.Message;
            response.AccountId = result.AccountId;
            await ETTask.CompletedTask;
        }
    }
}
