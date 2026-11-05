using System;
using System.IO;
using Content.Server._Misfits.Security;
using NetSerializer;
using NUnit.Framework;
using Robust.Shared.Log;

namespace Content.Tests.Server._Misfits;

[TestFixture]
[TestOf(typeof(NetSerializerGuard))]
public sealed class NetSerializerGuardTest
{
    [Serializable]
    public sealed class GuardTestMsg
    {
        public byte[]? Bytes;
        public string? Text;
        public string? Empty;
    }

    private Serializer _serializer = default!;

    [OneTimeSetUp]
    public void Setup()
    {
        _serializer = new Serializer(new[] { typeof(GuardTestMsg) });
        NetSerializerGuard.Apply(new LogManager().GetSawmill("netguard"));
    }

    [Test]
    public void ValidDataRoundTrips()
    {
        var msg = new GuardTestMsg
        {
            Bytes = new byte[] { 1, 2, 3 },
            Text = "héllo wörld ☢ " + new string('x', 5000),
            Empty = "",
        };

        var stream = new MemoryStream();
        _serializer.Serialize(stream, msg);
        stream.Position = 0;
        var back = (GuardTestMsg) _serializer.Deserialize(stream);

        Assert.Multiple(() =>
        {
            Assert.That(back.Bytes, Is.EqualTo(msg.Bytes));
            Assert.That(back.Text, Is.EqualTo(msg.Text));
            Assert.That(back.Empty, Is.EqualTo(""));
        });

        var nulls = new MemoryStream();
        _serializer.Serialize(nulls, new GuardTestMsg());
        nulls.Position = 0;
        var backNulls = (GuardTestMsg) _serializer.Deserialize(nulls);
        Assert.That(backNulls.Text, Is.Null);
        Assert.That(backNulls.Bytes, Is.Null);
    }

    [Test]
    public void OversizedStringLengthIsRejected()
    {
        var stream = new MemoryStream();
        Primitives.WritePrimitive(stream, 0x0FFFFFF0u);
        Primitives.WritePrimitive(stream, 0x0FFFFFE0u);
        stream.WriteByte((byte) 'A');
        stream.Position = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Primitives.ReadPrimitive(stream, out string _));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1024 * 1024));
    }

    [Test]
    public void StringWithMoreCharsThanBytesIsRejected()
    {
        var stream = new MemoryStream();
        Primitives.WritePrimitive(stream, 3u);
        Primitives.WritePrimitive(stream, 0x0FFFFFE0u);
        stream.WriteByte((byte) 'A');
        stream.WriteByte((byte) 'A');
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => Primitives.ReadPrimitive(stream, out string _));
    }

    [Test]
    public void OversizedByteArrayLengthIsRejected()
    {
        var stream = new MemoryStream();
        Primitives.WritePrimitive(stream, 0x0FFFFFF0u);
        stream.WriteByte(1);
        stream.Position = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Primitives.ReadPrimitive(stream, out byte[] _));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1024 * 1024));
    }
}
