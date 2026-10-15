using System;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>基地累计日历与发放日权威；金币仍归CurrencyModel，不负责远征计时或界面。</summary>
    public sealed class BasePreparationModel : AbstractModel,IScopedSaveSectionProvider
    {
        public const string Section="another-day.base-preparation";
        public string SectionId=>Section;
        public SaveSectionScope Scope=>SaveSectionScope.BaseOnly;
        public bool Initialized {get;private set;}
        /// <summary>从第1天00:00计起的游戏毫秒，跨地图和存档继续累计。</summary>
        public long TotalMilliseconds {get;internal set;}
        public long LastGrantedDay {get;internal set;}
        protected override void OnInit(){}
        public JToken Capture()=>Initialized?new JObject{["schemaVersion"]=1,["totalMilliseconds"]=TotalMilliseconds,["lastGrantedDay"]=LastGrantedDay}:null;
        public bool Validate(JToken data,out string error)
        {
            error=null;if(data==null)return true;
            try {if(data["schemaVersion"]?.Type!=JTokenType.Integer||data["schemaVersion"].Value<int>()!=1||data["totalMilliseconds"]?.Type!=JTokenType.Integer||data["lastGrantedDay"]?.Type!=JTokenType.Integer||data["totalMilliseconds"].Value<long>()<0||data["lastGrantedDay"].Value<long>()<1||data["lastGrantedDay"].Value<long>()!=PreparationMath.Day(data["totalMilliseconds"].Value<long>()))throw new FormatException();return true;}
            catch {error="基地准备日历或发放记录无效";return false;}
        }
        public void Restore(JToken data){if(!Validate(data,out var error))throw new InvalidOperationException(error);Initialized=data!=null;TotalMilliseconds=data?["totalMilliseconds"].Value<long>()??0;LastGrantedDay=data?["lastGrantedDay"].Value<long>()??0;}
        public void Reset()=>Restore(null);
    }
    /// <summary>永久商品开放集合，仅属于当前基地存档；基础开放从配置读取。</summary>
    public sealed class ShopUnlockModel : AbstractModel,IScopedSaveSectionProvider
    {
        public const string Section="another-day.shop-unlocks";
        public string SectionId=>Section;
        public SaveSectionScope Scope=>SaveSectionScope.BaseOnly;
        private JObject state;
        protected override void OnInit(){}
        public bool IsUnlocked(int id)=>state?["items"] is JArray ids&&System.Linq.Enumerable.Any(ids,x=>x.Value<int>()==id);
        public JToken Capture()=>state?.DeepClone();
        public bool Validate(JToken data,out string error)
        {
            error=null;if(data==null)return true;
            try {if(data["schemaVersion"]?.Type!=JTokenType.Integer||data["schemaVersion"].Value<int>()!=1||data["items"] is not JArray ids)throw new FormatException();var seen=new System.Collections.Generic.HashSet<int>();foreach(var id in ids)if(id.Type!=JTokenType.Integer||id.Value<int>()<=0||!seen.Add(id.Value<int>()))throw new FormatException();return true;}
            catch{error="永久商品开放记录无效";return false;}
        }
        public void Restore(JToken data){if(!Validate(data,out var error))throw new InvalidOperationException(error);state=data==null?null:(JObject)data.DeepClone();}
        public void Reset()=>Restore(null);
    }
    /// <summary>本趟日历锚点，只保存远征增量，不把中途玩家数据写入基地。</summary>
    public sealed class ExpeditionPreparationModel : AbstractModel,IScopedSaveSectionProvider
    {
        public const string Section="another-day.expedition-preparation";
        public string SectionId=>Section;
        public SaveSectionScope Scope=>SaveSectionScope.ExpeditionOnly;
        public string ExpeditionId {get;private set;}
        public long BaseAnchor {get;private set;}
        public double ElapsedAnchor {get;private set;}
        protected override void OnInit(){}
        public void Bind(string id,long clock,double elapsed){ExpeditionId=id;BaseAnchor=clock;ElapsedAnchor=elapsed;}
        public long Project(double elapsed)=>checked(BaseAnchor+(long)Math.Round(Math.Max(0,elapsed-ElapsedAnchor)*60000));
        public JToken Capture()=>ExpeditionId==null?null:new JObject{["schemaVersion"]=1,["expeditionId"]=ExpeditionId,["baseAnchor"]=BaseAnchor,["elapsedAnchor"]=ElapsedAnchor};
        public bool Validate(JToken data,out string error)
        {
            error=null;if(data==null)return true;
            try
            {
                if(data["schemaVersion"]?.Type!=JTokenType.Integer||data["schemaVersion"].Value<int>()!=1||data["expeditionId"]?.Type!=JTokenType.String||data["baseAnchor"]?.Type!=JTokenType.Integer||data["elapsedAnchor"]?.Type is not (JTokenType.Integer or JTokenType.Float))throw new FormatException();
                var id=data["expeditionId"].Value<string>();var e=data["elapsedAnchor"].Value<double>();
                if(id.Length!=32||!System.Linq.Enumerable.All(id,Uri.IsHexDigit)||data["baseAnchor"].Value<long>()<0||double.IsNaN(e)||double.IsInfinity(e)||e<0)throw new FormatException();return true;
            }
            catch{error="远征基地计时锚点无效";return false;}
        }
        public void Restore(JToken data){if(!Validate(data,out var error))throw new InvalidOperationException(error);if(data==null){ExpeditionId=null;BaseAnchor=0;ElapsedAnchor=0;}else Bind(data["expeditionId"].Value<string>(),data["baseAnchor"].Value<long>(),data["elapsedAnchor"].Value<double>());}
        public void Reset()=>Restore(null);
    }
    public readonly struct BasePreparationChangedEvent {}
}
