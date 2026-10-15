using System.Net;

namespace ET.Server
{
    [Invoke((long)SceneType.GameGate)]
    public class FiberInit_GameGate: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<MailBoxComponent, MailBoxType>(MailBoxType.OrderedMessage);
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<ProcessInnerSender>();
            root.AddComponent<MessageSender>();

            StartSceneConfig startSceneConfig = ServerConfig.Instance.GetScene((int)root.Id);
            root.AddComponent<NetComponent, IPEndPoint, NetworkProtocol>(startSceneConfig.InnerIPPort, NetworkProtocol.TCP);
            await ETTask.CompletedTask;
        }
    }
}

