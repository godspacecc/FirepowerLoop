using System;

namespace ET.Server
{
    [Invoke((long)SceneType.Account)]
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
                accountComponent.LoadStore(Options.Instance.OnlineStore);
            }
            catch (Exception e)
            {
                Log.Error($"online account store init failed: {e.GetType().Name}: {e.Message}");
                Environment.Exit(2);
            }

            await ETTask.CompletedTask;
        }
    }
}
