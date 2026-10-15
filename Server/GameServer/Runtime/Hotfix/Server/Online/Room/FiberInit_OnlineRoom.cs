namespace ET.Server
{
    [Invoke((long)SceneType.OnlineRoom)]
    public class FiberInit_OnlineRoom: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<ProcessInnerSender>();
            root.AddComponent<MessageSender>();
            root.AddComponent<LocationProxyComponent>();
            root.AddComponent<OnlineRoomManagerComponent>();
            OnlineArcadeRunManagerComponent arcade = root.AddComponent<OnlineArcadeRunManagerComponent>();
            arcade.LoadStore(Options.Instance.OnlineArcadeStore);
            await ETTask.CompletedTask;
        }
    }
}
