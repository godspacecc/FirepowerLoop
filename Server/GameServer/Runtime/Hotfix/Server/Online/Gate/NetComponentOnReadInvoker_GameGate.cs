using System;

namespace ET.Server
{
    [Invoke((long)SceneType.GameGate)]
    public class NetComponentOnReadInvoker_GameGate: AInvokeHandler<NetComponentOnRead>
    {
        public override void Handle(NetComponentOnRead args)
        {
            HandleAsync(args).Coroutine();
        }

        private static async ETTask HandleAsync(NetComponentOnRead args)
        {
            Session session = args.Session;
            object message = args.Message;
            switch (message)
            {
                case C2G_Register:
                case C2G_Login:
                case C2G_Logout:
                case C2G_CreateRoom:
                case C2G_MatchArcadeRoom:
                case C2G_JoinRoom:
                case C2G_LeaveRoom:
                case C2G_GetCurrentRoom:
                case C2G_SendRoomChat:
                case C2G_GetRoomChatHistory:
                case C2G_Ping:
                case C2G_GetServerTestCmdCatalog:
                case C2G_ExecuteServerTestCmd:
                {
                    MessageSessionDispatcher.Instance.Handle(session, message);
                    break;
                }
                case ISessionMessage:
                {
                    if (session.GetComponent<OnlineSessionComponent>() == null)
                    {
                        throw new RpcException(ErrorCode.ERR_OnlineSessionNotLoggedIn, "未登录");
                    }
                    MessageSessionDispatcher.Instance.Handle(session, message);
                    break;
                }
                default:
                {
                    throw new Exception($"not found online gate handler: {message}");
                }
            }

            await ETTask.CompletedTask;
        }
    }
}
