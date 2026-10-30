using System.IO;
using System.Linq;
using Game.Network;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证目标项目外网帧的字节兼容、半包、粘包及长度边界，不依赖账号或玩法。</summary>
    public sealed class EtNetworkFrameTests
    {
        [Test]
        public void AppendCopiesImmediatelyAndInvalidArgumentsPreservePendingBytes()
        {
            var parser = new TcpFrameAssembler();
            parser.Append(new byte[] { 2 }, 1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => parser.Append(new byte[] { 0 }, 2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => parser.Append(new byte[] { 0 }, -1));
            Assert.Throws<System.ArgumentNullException>(() => parser.Append(null, 0));
            var tail = new byte[] { 0, 1, 2 };
            var frames = parser.Append(tail, tail.Length);
            tail[1] = 99;
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, frames.Single());
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, frames.Single());
            Assert.IsEmpty(parser.Append(new byte[0], 0));
        }

        [Test]
        public void KnownWireBytesUseLittleEndianLength()
        {
            var body = new byte[258];
            body[0] = 0xE1;
            body[1] = 0x2E;
            var wire = TcpFrameCodec.WriteFrame(body);
            Assert.AreEqual(2, wire[0]);
            Assert.AreEqual(1, wire[1]);
            CollectionAssert.AreEqual(body, wire.Skip(2));
        }

        [Test]
        public void EverySplitPointAndCoalescedFramesPreserveBodies()
        {
            byte[] first = { 0xE1, 0x2E, 9, 8 };
            byte[] second = { 0xE2, 0x2E };
            var wire = TcpFrameCodec.WriteFrame(first).Concat(TcpFrameCodec.WriteFrame(second)).ToArray();
            for (int split = 0; split <= wire.Length; split++)
            {
                var parser = new TcpFrameAssembler();
                var frames = parser.Append(wire, split).ToList();
                var rest = wire.Skip(split).ToArray();
                frames.AddRange(parser.Append(rest, rest.Length));
                Assert.AreEqual(2, frames.Count);
                CollectionAssert.AreEqual(first, frames[0]);
                CollectionAssert.AreEqual(second, frames[1]);
            }
        }

        [Test]
        public void InvalidLengthsAreRejectedAndMaximumRoundTrips()
        {
            Assert.Throws<InvalidDataException>(() => TcpFrameCodec.WriteFrame(new byte[1]));
            Assert.Throws<InvalidDataException>(() => TcpFrameCodec.WriteFrame(new byte[65536]));
            Assert.Throws<InvalidDataException>(() => new TcpFrameAssembler().Append(new byte[] { 1, 0 }, 2).ToArray());
            var body = Enumerable.Repeat((byte)0xA5, 65535).ToArray();
            var wire = TcpFrameCodec.WriteFrame(body);
            CollectionAssert.AreEqual(body, new TcpFrameAssembler().Append(wire, wire.Length).Single());
        }
    }
}
