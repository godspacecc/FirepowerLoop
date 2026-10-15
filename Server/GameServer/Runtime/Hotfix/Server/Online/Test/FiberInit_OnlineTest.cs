using System.Net.Sockets;

namespace ET.Server
{
    [Invoke((long)SceneType.OnlineTest)]
    public class FiberInit_OnlineTest: AInvokeHandler<FiberInit, ETTask>
    {
        public override async ETTask Handle(FiberInit fiberInit)
        {
            Scene root = fiberInit.Fiber.Root;
            root.AddComponent<TimerComponent>();
            root.AddComponent<CoroutineLockComponent>();
            root.AddComponent<NetComponent, AddressFamily, NetworkProtocol>(AddressFamily.InterNetwork, NetworkProtocol.TCP);
            root.AddComponent<OnlineTestComponent>();
            await ETTask.CompletedTask;
        }
    }
}
