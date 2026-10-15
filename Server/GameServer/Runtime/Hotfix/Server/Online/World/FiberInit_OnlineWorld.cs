namespace ET.Server
{
    [Invoke((long)SceneType.OnlineWorld)]
    [FriendOf(typeof(OnlineWorldComponent))]
    public class FiberInit_OnlineWorld: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<ProcessInnerSender>();
            root.AddComponent<MessageSender>();
            OnlineWorldComponent world = root.AddComponent<OnlineWorldComponent>();
            world.FailedLeaveCount = Options.Instance.OnlineWorldFailLeaveCount;
            await ETTask.CompletedTask;
        }
    }
}
