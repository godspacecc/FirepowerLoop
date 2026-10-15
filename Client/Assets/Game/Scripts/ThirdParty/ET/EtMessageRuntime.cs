using System;
using System.Collections.Concurrent;
using MemoryPack;

namespace ET
{
    public abstract class BaseAttribute : Attribute
    {
    }

    public sealed class MessageAttribute : BaseAttribute
    {
        public MessageAttribute(ushort opcode = 0)
        {
            Opcode = opcode;
        }

        public ushort Opcode { get; }
    }

    public sealed class ResponseTypeAttribute : BaseAttribute
    {
        public ResponseTypeAttribute(string type)
        {
            Type = type;
        }

        public string Type { get; }
    }

    public interface IMessage
    {
    }

    public interface IRequest : IMessage
    {
        int RpcId { get; set; }
    }

    public interface IResponse : IMessage
    {
        int Error { get; set; }
        string Message { get; set; }
        int RpcId { get; set; }
    }

    public interface ILocationMessage : ILocationRequest
    {
    }

    public interface ILocationRequest : IRequest
    {
    }

    public interface ILocationResponse : IResponse
    {
    }

    public interface ISessionMessage : IMessage
    {
    }

    public interface ISessionRequest : ISessionMessage, IRequest
    {
    }

    public interface ISessionResponse : ISessionMessage, IResponse
    {
    }

    public interface IRoomMessage : IMessage
    {
    }

    public interface IPool
    {
        bool IsFromPool { get; set; }
    }

    public abstract class ProtoObject
    {
    }

    public abstract class MessageObject : ProtoObject, IMessage, IDisposable, IPool
    {
        public bool IsFromPool { get; set; }

        public virtual void Dispose()
        {
        }
    }

    public sealed class ObjectPool
    {
        public static readonly ObjectPool Instance = new ObjectPool();

        private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> pools = new ConcurrentDictionary<Type, ConcurrentQueue<object>>();

        private ObjectPool()
        {
        }

        public object Fetch(Type type, bool isFromPool = true)
        {
            if (!isFromPool)
            {
                return Activator.CreateInstance(type);
            }

            ConcurrentQueue<object> queue = pools.GetOrAdd(type, _ => new ConcurrentQueue<object>());
            if (!queue.TryDequeue(out object instance))
            {
                instance = Activator.CreateInstance(type);
            }

            if (instance is IPool pooled)
            {
                pooled.IsFromPool = true;
            }

            return instance;
        }

        public void Recycle(object instance)
        {
            if (instance == null)
            {
                return;
            }

            if (instance is IPool pooled)
            {
                if (!pooled.IsFromPool)
                {
                    return;
                }

                pooled.IsFromPool = false;
            }

            pools.GetOrAdd(instance.GetType(), _ => new ConcurrentQueue<object>()).Enqueue(instance);
        }
    }

    [MemoryPackable]
    public partial struct Address
    {
        [MemoryPackOrder(0)]
        public int Process;

        [MemoryPackOrder(1)]
        public int Fiber;

        public Address(int process, int fiber)
        {
            Process = process;
            Fiber = fiber;
        }
    }

    [MemoryPackable]
    public partial struct ActorId
    {
        [MemoryPackOrder(0)]
        public Address Address;

        [MemoryPackOrder(1)]
        public long InstanceId;

        public ActorId(int process, int fiber, long instanceId = 1)
        {
            Address = new Address(process, fiber);
            InstanceId = instanceId;
        }
    }

    [MemoryPackable]
    public partial struct LSInput
    {
        [MemoryPackOrder(0)]
        public TrueSync.TSVector2 V;

        [MemoryPackOrder(1)]
        public int Button;
    }
}

namespace TrueSync
{
    [MemoryPackable]
    public partial struct TSVector2
    {
        [MemoryPackOrder(0)]
        public int x;

        [MemoryPackOrder(1)]
        public int y;

        public static bool operator ==(TSVector2 left, TSVector2 right)
        {
            return left.x == right.x && left.y == right.y;
        }

        public static bool operator !=(TSVector2 left, TSVector2 right)
        {
            return !(left == right);
        }

        public override bool Equals(object obj)
        {
            return obj is TSVector2 other && this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, y);
        }
    }

    [MemoryPackable]
    public partial struct TSVector
    {
        [MemoryPackOrder(0)]
        public int x;

        [MemoryPackOrder(1)]
        public int y;

        [MemoryPackOrder(2)]
        public int z;
    }

    [MemoryPackable]
    public partial struct TSQuaternion
    {
        [MemoryPackOrder(0)]
        public int x;

        [MemoryPackOrder(1)]
        public int y;

        [MemoryPackOrder(2)]
        public int z;

        [MemoryPackOrder(3)]
        public int w;
    }
}

namespace MongoDB.Bson.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class BsonDictionaryOptionsAttribute : Attribute
    {
        public BsonDictionaryOptionsAttribute(MongoDB.Bson.Serialization.Options.DictionaryRepresentation representation)
        {
            Representation = representation;
        }

        public MongoDB.Bson.Serialization.Options.DictionaryRepresentation Representation { get; }
    }
}

namespace MongoDB.Bson.Serialization.Options
{
    public enum DictionaryRepresentation
    {
        ArrayOfArrays
    }
}
