using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Game;
namespace ET.Server
{
    /// <summary>加载随服务器部署的场景碰撞数据，单进程缓存不可变合同；失败只拒绝新弓箭，不改变近战规则。</summary>
    [EnableClass] public sealed class OnlineWorldCollisionCatalog
    {
        private readonly Dictionary<string,WorldProjectileCollisionData> data=new(StringComparer.Ordinal);
        public bool TryGet(string mapId,out WorldProjectileCollisionData value,out string error)
        {
            value=null;error="地图碰撞数据不可用";
            if(string.IsNullOrWhiteSpace(mapId)||mapId.IndexOfAny(new[]{'/', '\\', ':'})>=0)return false;
            if(data.TryGetValue(mapId,out value)){error=null;return true;}
            try
            {
                string path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../Config/WorldCollision",mapId+".json"));
                if(!File.Exists(path))return false;
                value=JsonSerializer.Deserialize<WorldProjectileCollisionData>(File.ReadAllText(path),new JsonSerializerOptions{IncludeFields=true});
                if(!WorldProjectileCollisionValidation.TryValidate(value,out error)||!string.Equals(value.MapId,mapId,StringComparison.Ordinal)){value=null;return false;}
                data.Add(mapId,value);error=null;return true;
            }
            catch(Exception e){error="地图碰撞数据加载失败："+e.Message;value=null;return false;}
        }
    }
}
