namespace ET.Server
{
    [MessageSessionHandler(SceneType.GameGate)]
    public class C2G_RegisterHandler: MessageSessionHandler<C2G_Register, G2C_Register>
    {
        protected override async ETTask Run(Session session, C2G_Register request, G2C_Register response)
        {
            ActorId accountActorId = session.Root().GetOnlineSceneActorId("Account");
            G2A_Register register = G2A_Register.Create();
            register.UserName = request.UserName;
            register.Password = request.Password;
            using A2G_Register result = await session.Root().GetComponent<MessageSender>().Call(accountActorId, register, false) as A2G_Register;
            response.Error = result.Error;
            response.Message = string.Empty;
            response.AccountId = result.AccountId;
            if (result.Error != ErrorCode.ERR_Success)
            {
                Log.Warning($"online register rejected error={result.Error}");
            }
        }
    }
}
