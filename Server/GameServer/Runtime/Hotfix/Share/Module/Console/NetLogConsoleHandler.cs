using System;

namespace ET
{
    [ConsoleHandler(ConsoleMode.NetLog)]
    public class NetLogConsoleHandler: IConsoleHandler
    {
        private const string Usage = "用法：netlog on | netlog off | netlog status";

        public async ETTask Run(Fiber fiber, ModeContex contex, string content)
        {
            try
            {
                string[] arguments = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (arguments.Length != 2 || !string.Equals(arguments[0], ConsoleMode.NetLog, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Console(Usage);
                    return;
                }

                string action = arguments[1];
                if (string.Equals(action, "on", StringComparison.OrdinalIgnoreCase))
                {
                    LogMsg.Instance.SetFullProtocolLogEnabled(true);
                    Log.Console("完整协议日志：已开启。");
                    Log.Console("风险提醒：登录/注册密码等凭据将以原文同时进入当前窗口与 Logs/*.Debug.log；netlog off 不会删除历史日志。");
                }
                else if (string.Equals(action, "off", StringComparison.OrdinalIgnoreCase))
                {
                    LogMsg.Instance.SetFullProtocolLogEnabled(false);
                    Log.Console("完整协议日志：已关闭。历史日志不会被删除。");
                }
                else if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Console($"完整协议日志：{(LogMsg.Instance.FullProtocolLogEnabled ? "已开启" : "已关闭")}。");
                }
                else
                {
                    Log.Console(Usage);
                }
            }
            catch (Exception e)
            {
                Log.Console($"netlog 命令处理失败：{e.GetType().Name}");
            }
            finally
            {
                contex.Parent.RemoveComponent<ModeContex>();
            }

            await ETTask.CompletedTask;
        }
    }
}
