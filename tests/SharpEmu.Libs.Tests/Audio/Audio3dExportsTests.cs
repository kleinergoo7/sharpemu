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
}
