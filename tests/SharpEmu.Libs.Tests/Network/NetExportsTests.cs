// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using SharpEmu.HLE;
using SharpEmu.Libs.Network;
using Xunit;

namespace SharpEmu.Libs.Tests.Network;

// The libSceNet byte-order helpers take their operand in Rdi and return the converted value in
// Rax. They swap endianness unconditionally, which is correct on the little-endian hosts (and
// little-endian guest) the emulator targets, so network (big-endian) order is always a byte swap.
public sealed class NetExportsTests
{
    private readonly CpuContext _ctx = new(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen5);

    [Fact]
    public void SocketDescriptorsFitGuestFdSet()
    {
        _ctx[CpuRegister.Rsi] = 2; // AF_INET
        _ctx[CpuRegister.Rdx] = 2; // SOCK_DGRAM
        _ctx[CpuRegister.Rcx] = 17; // UDP

        Assert.Equal(0, NetExports.NetSocket(_ctx));
        var first = _ctx[CpuRegister.Rax];
        Assert.Equal(0, NetExports.NetSocket(_ctx));
        var second = _ctx[CpuRegister.Rax];

        try
        {
            Assert.InRange(first, 256UL, 1023UL);
            Assert.InRange(second, 256UL, 1023UL);
            Assert.NotEqual(first, second);
        }
        finally
        {
            _ctx[CpuRegister.Rdi] = first;
            NetExports.NetSocketClose(_ctx);
            _ctx[CpuRegister.Rdi] = second;
            NetExports.NetSocketClose(_ctx);
        }
    }

    [Fact]
    public void NonblockingReceiveWithoutDataReturnsWouldBlock()
    {
        _ctx[CpuRegister.Rsi] = 2;
        _ctx[CpuRegister.Rdx] = 2;
        _ctx[CpuRegister.Rcx] = 17;
        Assert.Equal(0, NetExports.NetSocket(_ctx));
        var socket = _ctx[CpuRegister.Rax];

        try
        {
            _ctx[CpuRegister.Rdi] = socket;
            _ctx[CpuRegister.Rsi] = 0x1_0000_0000;
            _ctx[CpuRegister.Rdx] = 1;
            _ctx[CpuRegister.Rcx] = 0x80;
            Assert.Equal(unchecked((int)0x80410123), NetExports.NetRecv(_ctx));
        }
        finally
        {
            _ctx[CpuRegister.Rdi] = socket;
            NetExports.NetSocketClose(_ctx);
        }
    }

    [Fact]
    public void Htonl_SwapsAllFourBytes()
    {
        _ctx[CpuRegister.Rdi] = 0x01020304;

        Assert.Equal(0, NetExports.NetHtonl(_ctx));
        Assert.Equal(0x04030201UL, _ctx[CpuRegister.Rax]);
    }

    private static ulong OpenDatagramSocket(CpuContext context)
    {
        context[CpuRegister.Rsi] = 2; // AF_INET
        context[CpuRegister.Rdx] = 2; // SOCK_DGRAM
        context[CpuRegister.Rcx] = 17; // UDP
        Assert.Equal(0, NetExports.NetSocket(context));
        return context[CpuRegister.Rax];
    }

    private static void CloseSocket(CpuContext context, ulong socket)
    {
        context[CpuRegister.Rdi] = socket;
        NetExports.NetSocketClose(context);
    }

    [Fact]
    public void Shutdown_ValidatesArgumentsAndIsIdempotent()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        try
        {
            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = 1; // SHUT_WR
            Assert.Equal(0, NetExports.NetShutdown(context));
            Assert.Equal(0, NetExports.NetShutdown(context));

            context[CpuRegister.Rsi] = 3;
            Assert.Equal(unchecked((int)0x80410116), NetExports.NetShutdown(context));

            context[CpuRegister.Rdi] = 0;
            context[CpuRegister.Rsi] = 1;
            Assert.Equal(unchecked((int)0x80410109), NetExports.NetShutdown(context));
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }

    [Fact]
    public void Shutdown_ClosesDropRecordedDirections()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        context[CpuRegister.Rdi] = socket;
        context[CpuRegister.Rsi] = 0; // SHUT_RD
        Assert.Equal(0, NetExports.NetShutdown(context));
        CloseSocket(context, socket);

        context[CpuRegister.Rdi] = socket;
        context[CpuRegister.Rsi] = 0;
        Assert.Equal(unchecked((int)0x80410109), NetExports.NetShutdown(context));
    }

    [Fact]
    public void Recv_AfterReadShutdownReturnsEndOfStream()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        try
        {
            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = 0; // SHUT_RD
            Assert.Equal(0, NetExports.NetShutdown(context));

            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = 0x1_0000_0000;
            context[CpuRegister.Rdx] = 64;
            context[CpuRegister.Rcx] = 0;
            Assert.Equal(0, NetExports.NetRecv(context));
            Assert.Equal(0UL, context[CpuRegister.Rax]);
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }

    [Fact]
    public void Getsockname_ReportsUnboundEndpoint()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        try
        {
            const ulong nameAddress = 0x1_0000_0100;
            const ulong namelenAddress = 0x1_0000_0200;
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, 16);
            Assert.True(memory.TryWrite(namelenAddress, length));

            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = nameAddress;
            context[CpuRegister.Rdx] = namelenAddress;
            Assert.Equal(0, NetExports.NetGetsockname(context));

            var result = new byte[16];
            Assert.True(memory.TryRead(nameAddress, result));
            Assert.Equal(16, result[0]);
            Assert.Equal(2, result[1]);
            Assert.Equal(0, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(2, 2)));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, result.AsSpan(4, 4).ToArray());

            var writtenLength = new byte[4];
            Assert.True(memory.TryRead(namelenAddress, writtenLength));
            Assert.Equal(16, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(writtenLength));
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }

    [Fact]
    public void Getsockname_ValidatesArguments()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        try
        {
            const ulong namelenAddress = 0x1_0000_0200;
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, 16);
            Assert.True(memory.TryWrite(namelenAddress, length));

            context[CpuRegister.Rdi] = 0;
            context[CpuRegister.Rsi] = 0x1_0000_0100;
            context[CpuRegister.Rdx] = namelenAddress;
            Assert.Equal(unchecked((int)0x80410109), NetExports.NetGetsockname(context));

            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = 0;
            Assert.Equal(unchecked((int)0x8041010E), NetExports.NetGetsockname(context));

            Span<byte> small = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(small, 4);
            Assert.True(memory.TryWrite(namelenAddress, small));
            context[CpuRegister.Rsi] = 0x1_0000_0100;
            Assert.Equal(unchecked((int)0x80410116), NetExports.NetGetsockname(context));
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }

    [Fact]
    public void Ntohl_SwapsAllFourBytes()
    {
        _ctx[CpuRegister.Rdi] = 0x01020304;

        Assert.Equal(0, NetExports.NetNtohl(_ctx));
        Assert.Equal(0x04030201UL, _ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void Htons_SwapsLowTwoBytesOnly()
    {
        // High bits above the 16-bit short must be ignored, not folded into the result.
        _ctx[CpuRegister.Rdi] = 0xFFFF_0102;

        Assert.Equal(0, NetExports.NetHtons(_ctx));
        Assert.Equal(0x0201UL, _ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void Ntohs_SwapsLowTwoBytesOnly()
    {
        _ctx[CpuRegister.Rdi] = 0xFFFF_0102;

        Assert.Equal(0, NetExports.NetNtohs(_ctx));
        Assert.Equal(0x0201UL, _ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void Htonl_IgnoresBitsAboveThe32BitWord()
    {
        _ctx[CpuRegister.Rdi] = 0xDEADBEEF_01020304;

        Assert.Equal(0, NetExports.NetHtonl(_ctx));
        Assert.Equal(0x04030201UL, _ctx[CpuRegister.Rax]);
    }

    [Theory]
    [InlineData(0xDEADBEEFUL)]
    [InlineData(0x00000000UL)]
    [InlineData(0xFFFFFFFFUL)]
    [InlineData(0x00000001UL)]
    public void HtonlThenNtohl_RoundTripsToOriginal(ulong value)
    {
        _ctx[CpuRegister.Rdi] = value;
        NetExports.NetHtonl(_ctx);

        _ctx[CpuRegister.Rdi] = _ctx[CpuRegister.Rax];
        NetExports.NetNtohl(_ctx);

        Assert.Equal(value, _ctx[CpuRegister.Rax]);
    }

    // Regression guard: a non-palindromic value must not come back as 0. The functions previously
    // computed the swap into Rax and then called SetReturn(0), which overwrote Rax, so every
    // sceNetHtonl/Htons/Ntohl/Ntohs call returned 0 regardless of input.
    [Fact]
    public void ByteOrderConversions_DoNotReturnZeroForNonZeroInput()
    {
        _ctx[CpuRegister.Rdi] = 0x01020304;
        NetExports.NetHtonl(_ctx);
        Assert.NotEqual(0UL, _ctx[CpuRegister.Rax]);

        _ctx[CpuRegister.Rax] = 0;
        _ctx[CpuRegister.Rdi] = 0x0102;
        NetExports.NetHtons(_ctx);
        Assert.NotEqual(0UL, _ctx[CpuRegister.Rax]);
    }

    [Fact]
    public void Getsockname_Ipv6ReturnsExplicitError()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        context[CpuRegister.Rdi] = 0;
        context[CpuRegister.Rsi] = 28; // AF_INET6
        context[CpuRegister.Rdx] = 2; // SOCK_DGRAM
        context[CpuRegister.Rcx] = 17; // UDP
        Assert.Equal(0, NetExports.NetSocket(context));
        var socket = context[CpuRegister.Rax];

        try
        {
            const ulong nameAddress = 0x1_0000_0100;
            const ulong namelenAddress = 0x1_0000_0200;
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, 16);
            Assert.True(memory.TryWrite(namelenAddress, length));

            var previousLog = Environment.GetEnvironmentVariable("SHARPEMU_LOG_NET");
            Environment.SetEnvironmentVariable("SHARPEMU_LOG_NET", "1");
            var writer = new StringWriter();
            var previousError = Console.Error;
            Console.SetError(writer);
            try
            {
                context[CpuRegister.Rdi] = socket;
                context[CpuRegister.Rsi] = nameAddress;
                context[CpuRegister.Rdx] = namelenAddress;
                Assert.Equal(unchecked((int)0x80410116), NetExports.NetGetsockname(context));
                Assert.Contains("getsockname.unsupported", writer.ToString());
            }
            finally
            {
                Console.SetError(previousError);
                Environment.SetEnvironmentVariable("SHARPEMU_LOG_NET", previousLog);
            }
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }

    [Fact]
    public void Getsockname_DisposedSocketReturnsErrorAndLogs()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        var field = typeof(NetExports).GetField("_sockets", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var sockets = (ConcurrentDictionary<int, Socket>)field.GetValue(null)!;
        Assert.True(sockets.TryGetValue(unchecked((int)socket), out var host));
        host!.Dispose();

        try
        {
            const ulong nameAddress = 0x1_0000_0100;
            const ulong namelenAddress = 0x1_0000_0200;
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, 16);
            Assert.True(memory.TryWrite(namelenAddress, length));

            var previousLog = Environment.GetEnvironmentVariable("SHARPEMU_LOG_NET");
            Environment.SetEnvironmentVariable("SHARPEMU_LOG_NET", "1");
            var writer = new StringWriter();
            var previousError = Console.Error;
            Console.SetError(writer);
            try
            {
                context[CpuRegister.Rdi] = socket;
                context[CpuRegister.Rsi] = nameAddress;
                context[CpuRegister.Rdx] = namelenAddress;
                Assert.Equal(unchecked((int)0x80410109), NetExports.NetGetsockname(context));
                Assert.Contains("getsockname.disposed", writer.ToString());
            }
            finally
            {
                Console.SetError(previousError);
                Environment.SetEnvironmentVariable("SHARPEMU_LOG_NET", previousLog);
            }
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }

    [Fact]
    public void Send_AfterShutdownWriteReturnsError()
    {
        var memory = new FakeCpuMemory(0x1_0000_0000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        var socket = OpenDatagramSocket(context);

        try
        {
            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = 1; // SHUT_WR
            Assert.Equal(0, NetExports.NetShutdown(context));

            context[CpuRegister.Rdi] = socket;
            context[CpuRegister.Rsi] = 0x1_0000_0100;
            context[CpuRegister.Rdx] = 4;
            Assert.Equal(unchecked((int)0x80410116), NetExports.PosixSend(context));

            // Zero-length sends still succeed; only real payloads are rejected.
            context[CpuRegister.Rdx] = 0;
            Assert.Equal(0, NetExports.PosixSend(context));
        }
        finally
        {
            CloseSocket(context, socket);
        }
    }
}
