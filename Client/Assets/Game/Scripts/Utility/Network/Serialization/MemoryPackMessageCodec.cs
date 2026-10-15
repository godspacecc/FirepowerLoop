using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ET;
using MemoryPack;

namespace Game.Network
{
    internal interface IMessageCodec
    {
        byte[] Encode(IMessage message);
        DecodedMessage Decode(byte[] packet);
        Type GetResponseType(Type requestType);
    }

    internal sealed class MemoryPackMessageCodec : IMessageCodec
    {
        private const int HeaderSize = 2;
        private readonly Dictionary<ushort, Type> typeByOpcode = new Dictionary<ushort, Type>();
        private readonly Dictionary<Type, ushort> opcodeByType = new Dictionary<Type, ushort>();
        private readonly Dictionary<Type, Type> responseByRequest = new Dictionary<Type, Type>();

        public MemoryPackMessageCodec()
        {
            RegisterGeneratedMessages();
        }

        public byte[] Encode(IMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            Type type = message.GetType();
            if (!opcodeByType.TryGetValue(type, out ushort opcode))
            {
                throw new InvalidOperationException($"消息类型未注册：type={type.FullName}");
            }

            byte[] payload = MemoryPackSerializer.Serialize(type, message);
            byte[] packet = new byte[HeaderSize + payload.Length];
            packet[0] = (byte)opcode;
            packet[1] = (byte)(opcode >> 8);
            Buffer.BlockCopy(payload, 0, packet, HeaderSize, payload.Length);
            return packet;
        }

        public DecodedMessage Decode(byte[] packet)
        {
            if (packet == null || packet.Length < HeaderSize)
            {
                throw new InvalidDataException("消息头过短。");
            }

            ushort opcode = (ushort)(packet[0] | (packet[1] << 8));
            if (!typeByOpcode.TryGetValue(opcode, out Type type))
            {
                throw new InvalidDataException($"未知 Opcode：opcode={opcode}");
            }

            object value = MemoryPackSerializer.Deserialize(type, packet.AsSpan(HeaderSize)) ??
                           throw new InvalidDataException($"反序列化失败：opcode={opcode}");
            int rpcId = value is IRequest request ? request.RpcId : value is IResponse response ? response.RpcId : 0;
            return new DecodedMessage(opcode, rpcId, (IMessage)value);
        }

        public Type GetResponseType(Type requestType)
        {
            if (requestType == null)
            {
                throw new ArgumentNullException(nameof(requestType));
            }

            if (!responseByRequest.TryGetValue(requestType, out Type responseType))
            {
                throw new InvalidOperationException($"请求缺少响应类型：request={requestType.FullName}");
            }

            return responseType;
        }

        private void RegisterGeneratedMessages()
        {
            Type messageAttributeType = typeof(MessageAttribute);
            Type responseTypeAttributeType = typeof(ResponseTypeAttribute);
            IEnumerable<Type> messageTypes = typeof(IMessage).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute(messageAttributeType) != null && typeof(IMessage).IsAssignableFrom(type));

            foreach (Type type in messageTypes)
            {
                var messageAttribute = (MessageAttribute)type.GetCustomAttribute(messageAttributeType);
                if (messageAttribute.Opcode == 0)
                {
                    continue;
                }

                typeByOpcode.Add(messageAttribute.Opcode, type);
                opcodeByType.Add(type, messageAttribute.Opcode);
            }

            foreach (Type type in opcodeByType.Keys)
            {
                var responseTypeAttribute = (ResponseTypeAttribute)type.GetCustomAttribute(responseTypeAttributeType);
                if (responseTypeAttribute == null)
                {
                    continue;
                }

                Type responseType = opcodeByType.Keys.FirstOrDefault(candidate => candidate.Name == responseTypeAttribute.Type);
                if (responseType == null)
                {
                    throw new InvalidOperationException($"响应类型缺失：response={responseTypeAttribute.Type}");
                }

                responseByRequest.Add(type, responseType);
            }
        }

    }

    internal readonly struct DecodedMessage
    {
        public DecodedMessage(ushort opcode, int rpcId, IMessage message)
        {
            Opcode = opcode;
            RpcId = rpcId;
            Message = message;
        }

        public ushort Opcode { get; }
        public int RpcId { get; }
        public IMessage Message { get; }
    }
}
