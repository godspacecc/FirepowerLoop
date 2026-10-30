using MongoDB.Bson;

namespace ET
{
    [EnableClass]
    public abstract class Object
    {
        public override string ToString()
        {
            // 这里不能用MongoHelper.ToJson，因为单步调试会调用ToString来显示数据
            // 如果MongoHelper.ToJson会调用BeginInit,就出大事了
            // return MongoHelper.ToJson(this);
            // MongoDB 3.x 不再允许任意 object；按实际已知类型序列化，不放宽全局白名单。
            return MongoDB.Bson.BsonExtensionMethods.ToJson(this, GetType());
        }
        
        public string ToJson()
        {
            return MongoHelper.ToJson(this);
        }
        
        public byte[] ToBson()
        {
            return MongoHelper.Serialize(this);
        }
    }
}