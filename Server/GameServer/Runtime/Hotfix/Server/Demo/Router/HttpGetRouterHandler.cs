using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ET.Server
{
    [HttpHandler(SceneType.RouterManager, "/get_router")]
    public class HttpGetRouterHandler : IHttpHandler
    {
        public async ETTask Handle(Scene scene, HttpListenerContext context)
        {
            HttpGetRouterResponse response = HttpGetRouterResponse.Create();
            foreach (StartSceneConfig startSceneConfig in ServerConfig.Instance.Realms)
            {
                // 杩欓噷鏄鐢↖nnerIP锛屽洜涓轰簯鏈嶅姟鍣ㄤ笂realm缁戝畾涓嶄簡OuterIP鐨?鎵€浠ealm鐨勫唴缃戝缃戠殑socket閮芥槸鐩戝惉鍐呯綉鍦板潃
                response.Realms.Add(startSceneConfig.InnerIPPort.ToString());
            }
            foreach (StartSceneConfig startSceneConfig in ServerConfig.Instance.Routers)
            {
                response.Routers.Add($"{startSceneConfig.StartProcessConfig.OuterIP}:{startSceneConfig.Port}");
            }
            HttpHelper.Response(context, response);
            await ETTask.CompletedTask;
        }
    }
}
