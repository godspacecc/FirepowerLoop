using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
namespace Game
{
    /// <summary>独立金币账本；仅保留活动局及领取序号，磁盘大小不随历史拾取增长。</summary>
    public sealed class SurvivorGoldFileStore
    {
        /// <summary>进程内串行化同路径读改写。</summary>
        private static readonly object Gate=new object();
        /// <summary>当前玩家专用金币文件。</summary>
        private readonly string path;
        private sealed class Document
        {
            [JsonProperty(Required=Required.Always)] public int Version=1;
            [JsonProperty(Required=Required.Always)] public long Balance;
            [JsonProperty(Required=Required.Always)] public string ActiveRun="";
            [JsonProperty(Required=Required.Always)] public long Sequence;
        }
        /// <summary>使用独立目录；测试可注入临时根目录。</summary>
        public SurvivorGoldFileStore(long playerId,string root=null)
        {
            if(playerId<=0)throw new ArgumentOutOfRangeException(nameof(playerId));
            path=Path.Combine(root??Application.persistentDataPath,"Saves","survivor-gold-"+playerId+".json");
        }
        /// <summary>缺文件返回零；坏文件拒绝覆盖。</summary>
        public bool TryRead(out long balance,out string error)
        {
            lock(Gate){balance=0;error=null;try{balance=Read().Balance;return true;}catch(Exception ex){error=ex.Message;return false;}}
        }
        /// <summary>新局显式切换活动令牌，保留余额并废弃旧局写权限；同令牌重试不重置序号。</summary>
        public bool TryBeginRun(string run,out long balance,out string error)
        {
            lock(Gate){balance=0;error=null;try{
                if(string.IsNullOrWhiteSpace(run)||run.Length>96||run.Contains(":"))throw new ArgumentException("金币局编号无效");
                var d=Read();balance=d.Balance;if(d.ActiveRun==run)return true;
                d.ActiveRun=run;d.Sequence=0;Write(d);return true;
            }catch(Exception ex){error=ex.Message;return false;}}
        }
        /// <summary>领取编号为活动令牌:递增领取序号；成功后调用者才移除金币并推进序号。</summary>
        public bool TryCollect(string receipt,out long balance,out string error)
        {
            lock(Gate){balance=0;error=null;try{
                var parts=(receipt??"").Split(':');
                if(parts.Length!=2||!long.TryParse(parts[1],out var seq)||seq<=0)throw new ArgumentException("金币领取编号无效");
                var d=Read();balance=d.Balance;
                if(d.ActiveRun!=parts[0]||string.IsNullOrEmpty(d.ActiveRun))throw new InvalidOperationException("金币领取所属局已失效");
                if(seq<=d.Sequence)return true;
                if(seq!=checked(d.Sequence+1))throw new InvalidOperationException("金币领取序号不连续");
                d.Balance=checked(d.Balance+1);d.Sequence=seq;Write(d);balance=d.Balance;return true;
            }catch(Exception ex){error=ex.Message;return false;}}
        }
        /// <summary>先刷新临时文件到磁盘，再原子替换旧账本。</summary>
        private void Write(Document document)
        {
            string temporary=null;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                var bytes=System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document));
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }finally{try{if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);}catch{}}
        }
        /// <summary>格式、余额、活动局和序号一起验证，不静默创建替代坏档。</summary>
        private Document Read()
        {
            if(!File.Exists(path))return new Document();
            var d=JsonConvert.DeserializeObject<Document>(File.ReadAllText(path));
            if(d==null||d.Version!=1||d.Balance<0||d.Sequence<0||d.Sequence>d.Balance||d.ActiveRun==null||d.ActiveRun.Length>96||d.ActiveRun.Contains(":")||(d.ActiveRun.Length==0&&d.Sequence!=0))throw new InvalidDataException("幸存者金币文件无效");
            return d;
        }
    }
}
