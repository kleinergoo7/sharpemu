// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Audio;
using Xunit;

namespace SharpEmu.Libs.Tests.Audio;

public sealed class Audio3dExportsTests
{
    private const ulong MemoryBase = 0x1_0000_0000;
    private const uint NotReady = unchecked((uint)0x80EA0007);
    private const int InvalidParameter = unchecked((int)0x80EA0004);
    private const int InvalidPort = unchecked((int)0x80EA0002);
    private const int InvalidObject = unchecked((int)0x80EA0003);
    private const int OutOfResources = unchecked((int)0x80EA0006);

    private static CpuContext NewContext(out FakeCpuMemory memory)
    {
        memory = new FakeCpuMemory(MemoryBase, 0x4000);
        return new CpuContext(memory, Generation.Gen4);
    }

    private static void WriteParams(FakeCpuMemory memory, ulong address, ulong sizeThis = 0x28,
        uint granularity = 0x100, uint rate = 0, uint maxObjects = 512,
        uint queueDepth = 2, uint bufferMode = 2, uint numBeds = 2)
    {
        Span<byte> bytes = stackalloc byte[0x28];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, sizeThis);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], granularity);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], rate);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[16..], maxObjects);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[20..], queueDepth);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[24..], bufferMode);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[32..], numBeds);
        Assert.True(memory.TryWrite(address, bytes));
    }

    private static uint OpenDefaultPort(CpuContext context, FakeCpuMemory memory, out ulong idAddress)
    {
        const ulong paramsAddress = MemoryBase + 0x100;
        idAddress = MemoryBase + 0x200;
        WriteParams(memory, paramsAddress);
        context[CpuRegister.Rdi] = 0xFF;
        context[CpuRegister.Rsi] = paramsAddress;
        context[CpuRegister.Rdx] = idAddress;
        Assert.Equal(0, Audio3dExports.PortOpen(context));
        Span<byte> id = stackalloc byte[4];
        Assert.True(memory.TryRead(idAddress, id));
        return BinaryPrimitives.ReadUInt32LittleEndian(id);
    }

    [Fact]
    public void Initialize_ValidatesReservedAndDoubleInit()
    {
        var context = NewContext(out _);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            Assert.Equal(unchecked((int)0x80EA0007), Audio3dExports.Initialize(context));

            Assert.Equal(0, Audio3dExports.Terminate(context));

            context[CpuRegister.Rdi] = 1;
            Assert.Equal(InvalidParameter, Audio3dExports.Initialize(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Terminate_RequiresInitialization()
    {
        var context = NewContext(out _);
        try
        {
            Assert.Equal(unchecked((int)NotReady), Audio3dExports.Terminate(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void PortOpen_ValidatesUserAndPointers()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            const ulong paramsAddress = MemoryBase + 0x100;
            const ulong idAddress = MemoryBase + 0x200;
            WriteParams(memory, paramsAddress);

            context[CpuRegister.Rdi] = 0xFE;
            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = idAddress;
            Assert.Equal(InvalidParameter, Audio3dExports.PortOpen(context));
            Assert.Equal(0xFFFFFFFFu, ReadU32(memory, idAddress));

            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = 0;
            Assert.Equal(InvalidParameter, Audio3dExports.PortOpen(context));

            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = 0;
            Assert.Equal(InvalidParameter, Audio3dExports.PortOpen(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Theory]
    // size_this, granularity, rate, maxObjects, queueDepth, bufferMode, numBeds, expectedOk
    [InlineData(0x10ul, 0x100u, 0u, 512u, 2u, 0u, 2u, true)]
    [InlineData(0x28ul, 0x100u, 0u, 512u, 2u, 2u, 2u, true)]
    [InlineData(0x28ul, 0x100u, 0u, 512u, 2u, 2u, 3u, true)]
    [InlineData(0x08ul, 0x100u, 0u, 512u, 2u, 2u, 2u, false)]
    [InlineData(0x28ul, 0x100u, 1u, 512u, 2u, 2u, 2u, false)]
    [InlineData(0x28ul, 0x80u, 0u, 512u, 2u, 2u, 2u, false)]
    [InlineData(0x28ul, 0x180u, 0u, 512u, 2u, 2u, 2u, false)]
    [InlineData(0x28ul, 0x100u, 0u, 0u, 2u, 2u, 2u, false)]
    [InlineData(0x28ul, 0x100u, 0u, 512u, 0u, 2u, 2u, false)]
    [InlineData(0x28ul, 0x100u, 0u, 512u, 2u, 3u, 2u, false)]
    [InlineData(0x28ul, 0x100u, 0u, 512u, 2u, 2u, 4u, false)]
    [InlineData(0x28ul, 0x100u, 0u, 512u, 0x41u, 2u, 2u, false)]
    [InlineData(0x30ul, 0x100u, 0u, 512u, 2u, 2u, 2u, false)]
    public void PortOpen_ValidatesParameters(ulong sizeThis, uint granularity, uint rate,
        uint maxObjects, uint queueDepth, uint bufferMode, uint numBeds, bool expectedOk)
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            const ulong paramsAddress = MemoryBase + 0x100;
            const ulong idAddress = MemoryBase + 0x200;
            WriteParams(memory, paramsAddress, sizeThis, granularity, rate,
                maxObjects, queueDepth, bufferMode, numBeds);

            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = idAddress;
            Assert.Equal(expectedOk ? 0 : InvalidParameter, Audio3dExports.PortOpen(context));
            Assert.Equal(expectedOk ? 0u : 0xFFFFFFFFu, ReadU32(memory, idAddress));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void PortOpen_EnforcesFourPortLimit()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            for (var i = 0; i < 4; i++)
            {
                var paramsAddress = MemoryBase + 0x100 + (ulong)i * 0x40;
                var idAddress = MemoryBase + 0x300 + (ulong)i * 4;
                WriteParams(memory, paramsAddress);
                context[CpuRegister.Rdi] = 0xFF;
                context[CpuRegister.Rsi] = paramsAddress;
                context[CpuRegister.Rdx] = idAddress;
                Assert.Equal(0, Audio3dExports.PortOpen(context));
                Assert.Equal((uint)i, ReadU32(memory, idAddress));
            }

            WriteParams(memory, MemoryBase + 0x500);
            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = MemoryBase + 0x500;
            context[CpuRegister.Rdx] = MemoryBase + 0x340;
            Assert.Equal(OutOfResources, Audio3dExports.PortOpen(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void PortClose_AllowsSlotReuse()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            var port = OpenDefaultPort(context, memory, out _);

            context[CpuRegister.Rdi] = 99;
            Assert.Equal(InvalidPort, Audio3dExports.PortClose(context));

            context[CpuRegister.Rdi] = port;
            Assert.Equal(0, Audio3dExports.PortClose(context));
            Assert.Equal(InvalidPort, Audio3dExports.PortClose(context));

            Assert.Equal(port, OpenDefaultPort(context, memory, out _));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void ObjectReserve_EnforcesPerPortLimit()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            const ulong paramsAddress = MemoryBase + 0x100;
            const ulong idAddress = MemoryBase + 0x200;
            const ulong objectAddress = MemoryBase + 0x300;
            WriteParams(memory, paramsAddress, maxObjects: 1, queueDepth: 2);

            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = idAddress;
            Assert.Equal(0, Audio3dExports.PortOpen(context));
            var port = ReadU32(memory, idAddress);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = objectAddress;
            Assert.Equal(0, Audio3dExports.ObjectReserve(context));
            var first = ReadU32(memory, objectAddress);
            Assert.NotEqual(0u, first);
            Assert.NotEqual(0xFFFFFFFFu, first);

            Assert.Equal(OutOfResources, Audio3dExports.ObjectReserve(context));

            context[CpuRegister.Rdi] = 99;
            Assert.Equal(InvalidPort, Audio3dExports.ObjectReserve(context));

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = 0;
            Assert.Equal(InvalidParameter, Audio3dExports.ObjectReserve(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void ObjectUnreserve_RemovesEmptyObjectsAndMarksQueuedOnes()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);

            const ulong objectAddress = MemoryBase + 0x300;
            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = objectAddress;
            Assert.Equal(0, Audio3dExports.ObjectReserve(context));
            var handle = ReadU32(memory, objectAddress);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = 0x4242;
            Assert.Equal(InvalidObject, Audio3dExports.ObjectUnreserve(context));

            context[CpuRegister.Rsi] = handle;
            Assert.Equal(0, Audio3dExports.ObjectUnreserve(context));
            Assert.Equal(InvalidObject, Audio3dExports.ObjectUnreserve(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    private static uint ReadU32(FakeCpuMemory memory, ulong address)
    {
        Span<byte> bytes = stackalloc byte[4];
        Assert.True(memory.TryRead(address, bytes));
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static void WriteU32(FakeCpuMemory memory, ulong address, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        Assert.True(memory.TryWrite(address, bytes));
    }

    private static void WriteU64(FakeCpuMemory memory, ulong address, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        Assert.True(memory.TryWrite(address, bytes));
    }

    private static void SetObjectPcm(CpuContext context, FakeCpuMemory memory, uint port, uint handle,
        uint format, byte[] samples, float gain)
    {
        const ulong descAddress = MemoryBase + 0x400;
        const ulong samplesAddress = MemoryBase + 0x500;
        const ulong gainAddress = MemoryBase + 0x600;
        const ulong arrayAddress = MemoryBase + 0x700;

        Assert.True(memory.TryWrite(samplesAddress, samples));
        Span<byte> gainBytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(gainBytes, gain);
        Assert.True(memory.TryWrite(gainAddress, gainBytes));

        Span<byte> descriptor = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, format);
        BinaryPrimitives.WriteUInt64LittleEndian(descriptor.Slice(8, 8), samplesAddress);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(16, 4), (uint)(samples.Length / (format == 1 ? 4 : 2)));
        Assert.True(memory.TryWrite(descAddress, descriptor));

        Span<byte> entries = stackalloc byte[48];
        BinaryPrimitives.WriteUInt32LittleEndian(entries, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(entries.Slice(8, 8), descAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(entries.Slice(16, 8), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(entries.Slice(24, 4), 5);
        BinaryPrimitives.WriteUInt64LittleEndian(entries.Slice(32, 8), gainAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(entries.Slice(40, 8), 4);
        Assert.True(memory.TryWrite(arrayAddress, entries));

        context[CpuRegister.Rdi] = port;
        context[CpuRegister.Rsi] = handle;
        context[CpuRegister.Rdx] = 2;
        context[CpuRegister.Rcx] = arrayAddress;
        Assert.Equal(0, Audio3dExports.ObjectSetAttributes(context));
    }

    [Fact]
    public void Mixer_FoldsMonoToBothChannels()
    {
        var samples = new byte[] { 0x00, 0x40, 0x00, 0xC0, 0x00, 0x00, 0xFF, 0x7F };
        var sources = new List<(byte[] Data, uint Format, float Gain)> { (samples, 0, 1.0f) };
        var mixed = Audio3dExports.MixStereoFrames(sources, 4);

        Assert.Equal(new byte[]
        {
            0xFF, 0x3F, 0xFF, 0x3F,
            0x01, 0xC0, 0x01, 0xC0,
            0x00, 0x00, 0x00, 0x00,
            0xFE, 0x7F, 0xFE, 0x7F,
        }, mixed);
    }

    [Fact]
    public void Mixer_SilentGainDropsTheFrame()
    {
        var samples = new byte[] { 0x00, 0x40, 0x00, 0x40 };
        var sources = new List<(byte[] Data, uint Format, float Gain)> { (samples, 0, 0.0f) };
        Assert.Equal(new byte[8], Audio3dExports.MixStereoFrames(sources, 2));
    }

    [Fact]
    public void Mixer_SumsSourcesAndClamps()
    {
        var loud = new byte[] { 0xFF, 0x7F, 0xFF, 0x7F };
        var sources = new List<(byte[] Data, uint Format, float Gain)>
        {
            (loud, 0, 1.0f),
            (loud, 0, 1.0f),
        };
        var mixed = Audio3dExports.MixStereoFrames(sources, 2);

        Assert.Equal(new byte[] { 0xFF, 0x7F, 0xFF, 0x7F, 0xFF, 0x7F, 0xFF, 0x7F }, mixed);
    }

    [Fact]
    public void Mixer_FoldsFloatSamples()
    {
        var samples = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(samples, 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(samples.AsSpan(4), -0.25f);
        var sources = new List<(byte[] Data, uint Format, float Gain)> { (samples, 1, 1.0f) };
        var mixed = Audio3dExports.MixStereoFrames(sources, 2);

        var expected = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(expected, 16383);
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(2), 16383);
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(4), -8191);
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(6), -8191);
        Assert.Equal(expected, mixed);
    }

    [Fact]
    public void Advance_RejectsPortsWithoutAdvanceCapability()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            const ulong paramsAddress = MemoryBase + 0x100;
            const ulong idAddress = MemoryBase + 0x200;
            WriteParams(memory, paramsAddress, bufferMode: 0);
            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = idAddress;
            Assert.Equal(0, Audio3dExports.PortOpen(context));
            var port = ReadU32(memory, idAddress);

            context[CpuRegister.Rdx] = 99;
            Assert.Equal(unchecked((int)0x80EA0002), Audio3dExports.PortAdvance(context));

            context[CpuRegister.Rdi] = port;
            Assert.Equal(unchecked((int)0x80EA0008), Audio3dExports.PortAdvance(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Advance_RefusesWhenMixedQueueFull()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);

            context[CpuRegister.Rdi] = port;
            Assert.Equal(0, Audio3dExports.PortAdvance(context));
            Assert.Equal(0, Audio3dExports.PortAdvance(context));
            Assert.Equal(unchecked((int)NotReady), Audio3dExports.PortAdvance(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Push_RequiresPushCapabilityAndDepth()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);

            context[CpuRegister.Rdi] = 99;
            context[CpuRegister.Rsi] = 0;
            Assert.Equal(unchecked((int)0x80EA0002), Audio3dExports.PortPush(context));

            context[CpuRegister.Rdi] = port;
            Assert.Equal(0, Audio3dExports.PortPush(context));
            Assert.Equal(0, Audio3dExports.PortPush(context));

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = 1;
            Assert.Equal(0, Audio3dExports.PortPush(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Flush_DrainsMixedFrames()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);

            context[CpuRegister.Rdi] = 99;
            Assert.Equal(unchecked((int)0x80EA0002), Audio3dExports.PortFlush(context));

            context[CpuRegister.Rdi] = port;
            Assert.Equal(0, Audio3dExports.PortFlush(context));
            Assert.Equal(0, Audio3dExports.PortAdvance(context));
            Assert.Equal(0, Audio3dExports.PortFlush(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void UnreservedObject_IsReapedAfterMixDrainsItsQueue()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            const ulong paramsAddress = MemoryBase + 0x100;
            const ulong idAddress = MemoryBase + 0x200;
            const ulong objectAddress = MemoryBase + 0x300;
            WriteParams(memory, paramsAddress, maxObjects: 1);
            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = idAddress;
            Assert.Equal(0, Audio3dExports.PortOpen(context));
            var port = ReadU32(memory, idAddress);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = objectAddress;
            Assert.Equal(0, Audio3dExports.ObjectReserve(context));
            var handle = ReadU32(memory, objectAddress);

            var samples = new byte[0x100 * 2];
            samples[0] = 0x00;
            samples[1] = 0x40;
            SetObjectPcm(context, memory, port, handle, 0, samples, 1.0f);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = handle;
            Assert.Equal(0, Audio3dExports.ObjectUnreserve(context));

            context[CpuRegister.Rsi] = objectAddress;
            Assert.Equal(unchecked((int)0x80EA0006), Audio3dExports.ObjectReserve(context));

            context[CpuRegister.Rdi] = port;
            Assert.Equal(0, Audio3dExports.PortAdvance(context));

            context[CpuRegister.Rsi] = objectAddress;
            Assert.Equal(0, Audio3dExports.ObjectReserve(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void ObjectSetAttributes_UnknownObjectReturnsInvalidObject()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = 0x4242;
            context[CpuRegister.Rdx] = 1;
            context[CpuRegister.Rcx] = MemoryBase + 0x700;
            Assert.Equal(InvalidObject, Audio3dExports.ObjectSetAttributes(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void ObjectSetAttributes_ShortPcmDescriptorReturnsInvalidParameter()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);
            var handle = ReserveObject(context, memory, port);

            const ulong descAddress = MemoryBase + 0x400;
            const ulong arrayAddress = MemoryBase + 0x700;
            Span<byte> descriptor = stackalloc byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 0);
            BinaryPrimitives.WriteUInt64LittleEndian(descriptor.Slice(8, 8), MemoryBase + 0x500);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(16, 4), 0x100);
            Assert.True(memory.TryWrite(descAddress, descriptor));
            WritePcmAttribute(memory, arrayAddress, descAddress, 8);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = handle;
            context[CpuRegister.Rdx] = 1;
            context[CpuRegister.Rcx] = arrayAddress;
            Assert.Equal(InvalidParameter, Audio3dExports.ObjectSetAttributes(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void ObjectSetAttributes_UnknownFormatReturnsInvalidParameter()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));
            var port = OpenDefaultPort(context, memory, out _);
            var handle = ReserveObject(context, memory, port);

            const ulong descAddress = MemoryBase + 0x400;
            const ulong samplesAddress = MemoryBase + 0x500;
            const ulong arrayAddress = MemoryBase + 0x700;
            Assert.True(memory.TryWrite(samplesAddress, new byte[0x100 * 2]));
            Span<byte> descriptor = stackalloc byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 99);
            BinaryPrimitives.WriteUInt64LittleEndian(descriptor.Slice(8, 8), samplesAddress);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(16, 4), 0x100);
            Assert.True(memory.TryWrite(descAddress, descriptor));
            WritePcmAttribute(memory, arrayAddress, descAddress, 24);

            context[CpuRegister.Rdi] = port;
            context[CpuRegister.Rsi] = handle;
            context[CpuRegister.Rdx] = 1;
            context[CpuRegister.Rcx] = arrayAddress;
            Assert.Equal(InvalidParameter, Audio3dExports.ObjectSetAttributes(context));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void PortOpen_MaxObjectsExceedsLimitReturnsOutOfResources()
    {
        var context = NewContext(out var memory);
        try
        {
            context[CpuRegister.Rdi] = 0;
            Assert.Equal(0, Audio3dExports.Initialize(context));

            const ulong paramsAddress = MemoryBase + 0x100;
            const ulong idAddress = MemoryBase + 0x200;
            WriteParams(memory, paramsAddress, maxObjects: 0x201);

            context[CpuRegister.Rdi] = 0xFF;
            context[CpuRegister.Rsi] = paramsAddress;
            context[CpuRegister.Rdx] = idAddress;
            Assert.Equal(OutOfResources, Audio3dExports.PortOpen(context));
            Assert.Equal(0xFFFFFFFFu, ReadU32(memory, idAddress));
        }
        finally
        {
            Audio3dExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Mixer_FoldsCenterWith7071Coefficient()
    {
        // 3 channels: FL=0, FR=0, C=0.5 (0x4000). Center folds to both outputs.
        var samples = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x40 };
        var sources = new List<(byte[] Data, uint Format, float Gain)> { (samples, 0, 1.0f) };
        var mixed = Audio3dExports.MixStereoFrames(sources, 1);

        Assert.Equal(new byte[] { 0x40, 0x2D, 0x40, 0x2D }, mixed);
    }

    [Fact]
    public void Mixer_FoldsSurroundAndSkipsLfe()
    {
        // 6 channels: FL=0, FR=0, C=0, LFE=max (must be skipped), SL=0.5, SR=0.
        var samples = new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0x7F, 0x00, 0x40, 0x00, 0x00,
        };
        var sources = new List<(byte[] Data, uint Format, float Gain)> { (samples, 0, 1.0f) };
        var mixed = Audio3dExports.MixStereoFrames(sources, 1);

        Assert.Equal(new byte[] { 0x40, 0x2D, 0x00, 0x00 }, mixed);
    }

    [Fact]
    public void Mixer_FoldsRearSurroundPair()
    {
        // 8 channels: only RL=0.5 drives left, only RR=0.5 drives right.
        var leftOnly = new byte[16];
        leftOnly[12] = 0x00;
        leftOnly[13] = 0x40;
        var rightOnly = new byte[16];
        rightOnly[14] = 0x00;
        rightOnly[15] = 0x40;

        var leftMixed = Audio3dExports.MixStereoFrames(
            new List<(byte[] Data, uint Format, float Gain)> { (leftOnly, 0, 1.0f) }, 1);
        var rightMixed = Audio3dExports.MixStereoFrames(
            new List<(byte[] Data, uint Format, float Gain)> { (rightOnly, 0, 1.0f) }, 1);

        Assert.Equal(new byte[] { 0x40, 0x2D, 0x00, 0x00 }, leftMixed);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x40, 0x2D }, rightMixed);
    }

    private static uint ReserveObject(CpuContext context, FakeCpuMemory memory, uint port)
    {
        const ulong objectAddress = MemoryBase + 0x300;
        context[CpuRegister.Rdi] = port;
        context[CpuRegister.Rsi] = objectAddress;
        Assert.Equal(0, Audio3dExports.ObjectReserve(context));
        return ReadU32(memory, objectAddress);
    }

    private static void WritePcmAttribute(FakeCpuMemory memory, ulong arrayAddress, ulong descAddress, ulong valueSize)
    {
        Span<byte> entry = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(entry, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(8, 8), descAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(16, 8), valueSize);
        Assert.True(memory.TryWrite(arrayAddress, entry));
    }
}
