using System;

namespace ET.Server
{
    [Invoke((long)SceneType.Account)]
    [FriendOf(typeof(OnlineAccountComponent))]
    public class FiberInit_Account: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<ProcessInnerSender>();
            root.AddComponent<MessageSender>();

            OnlineAccountComponent accountComponent = root.AddComponent<OnlineAccountComponent>();
            try
            {
                string uri = Environment.GetEnvironmentVariable("FIREPOWERLOOP_MONGODB_URI");
                if (string.IsNullOrWhiteSpace(uri)) throw new InvalidOperationException("缺少 FIREPOWERLOOP_MONGODB_URI，账号服务不能启动。");
                string database = Environment.GetEnvironmentVariable("FIREPOWERLOOP_MONGODB_DATABASE") ?? "firepowerloop_accounts";
                if (System.IO.File.Exists(Options.Instance.OnlineStore))
                    throw new InvalidOperationException("检测到本项目旧账号文件，请先完成显式迁移并归档源文件；不自动覆盖或删除旧身份。");
                var settings = MongoDB.Driver.MongoClientSettings.FromConnectionString(uri);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(8);
                settings.ConnectTimeout = TimeSpan.FromSeconds(8);
                settings.SocketTimeout = TimeSpan.FromSeconds(10);
                var store = new FirepowerLoop.Accounts.MongoIdentityStore(new MongoDB.Driver.MongoClient(settings).GetDatabase(database));
                await store.InitializeAsync(default);
                accountComponent.Authentication = new FirepowerLoop.Accounts.AccountAuthenticationService(store);
            }
            catch (Exception e)
            {
                // 驱动异常可能包含连接信息，只记录异常类型，不打印URI或凭据。
                Log.Error($"账号存储初始化失败：{e.GetType().Name}；请检查Mongo配置与本项目迁移记录。");
                Environment.Exit(2);
            }

            await ETTask.CompletedTask;
        }
    }
}
