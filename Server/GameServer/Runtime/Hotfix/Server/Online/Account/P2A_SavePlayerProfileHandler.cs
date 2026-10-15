namespace ET.Server
{
    [MessageHandler(SceneType.Account)]
    public class P2A_SavePlayerProfileHandler: MessageHandler<Scene, P2A_SavePlayerProfile, A2P_SavePlayerProfile>
    {
        protected override async ETTask Run(Scene scene, P2A_SavePlayerProfile request, A2P_SavePlayerProfile response)
        {
            var result = scene.GetComponent<OnlineAccountComponent>().SaveProfile(
                request.AccountId,
                request.PlayerId,
                request.DisplayName,
                request.OperationId,
                request.ExpectedRevision,
                OnlineInventoryRecordHelper.FromMessage(request.OnlineInventory),
                request.HasReceivedOnlineStarterSupplies);
            response.Error = result.Error;
            response.Message = result.Message;
            response.Revision = result.Revision;
            await ETTask.CompletedTask;
        }
    }
}
