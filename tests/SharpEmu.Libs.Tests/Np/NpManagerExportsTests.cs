// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.Libs.Np;
using Xunit;

namespace SharpEmu.Libs.Tests.Np;

public sealed class NpManagerExportsTests
{
    private const ulong MemoryBase = 0x1_0000_0000;

    [Fact]
    public void CheckNpReachability_ReportsSignedOutDirectlyAndThroughPolling()
    {
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var requestId = NpManagerExports.NpCreateAsyncRequest(ctx);

        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        ctx[CpuRegister.Rsi] = 1;
        Assert.Equal(unchecked((int)0x80550006), NpManagerExports.NpCheckNpReachability(ctx));

        var resultAddress = MemoryBase + 0x100;
        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        ctx[CpuRegister.Rsi] = resultAddress;
        Assert.Equal(0, NpManagerExports.NpPollAsync(ctx));
        Span<byte> resultBytes = stackalloc byte[4];
        Assert.True(memory.TryRead(resultAddress, resultBytes));
        Assert.Equal(unchecked((int)0x80550006), BinaryPrimitives.ReadInt32LittleEndian(resultBytes));

        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        Assert.Equal(0, NpManagerExports.NpDeleteRequest(ctx));
        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        ctx[CpuRegister.Rsi] = resultAddress;
        Assert.Equal(0, NpManagerExports.NpPollAsync(ctx));
        Assert.True(memory.TryRead(resultAddress, resultBytes));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(resultBytes));
    }

    [Theory]
    [InlineData("sceNpGetAccountLanguage2")]
    [InlineData("sceNpGetAccountAge")]
    public void AccountRequests_ReportSignedOutDirectlyAndThroughPolling(string name)
    {
        Func<CpuContext, int> query = name == "sceNpGetAccountAge"
            ? NpManagerExports.NpGetAccountAge
            : NpManagerExports.NpGetAccountLanguage2;
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var requestId = NpManagerExports.NpCreateAsyncRequest(ctx);
        var outputAddress = MemoryBase + 0x200;
        Assert.True(memory.TryWrite(outputAddress, Enumerable.Repeat((byte)0xCC, 48).ToArray()));

        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        ctx[CpuRegister.Rsi] = 1;
        ctx[CpuRegister.Rdx] = outputAddress;
        Assert.Equal(unchecked((int)0x80550006), query(ctx));
        Span<byte> output = stackalloc byte[48];
        Assert.True(memory.TryRead(outputAddress, output));
        Assert.True(output.IndexOfAnyExcept((byte)0xCC) < 0);

        var resultAddress = MemoryBase + 0x100;
        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        ctx[CpuRegister.Rsi] = resultAddress;
        Assert.Equal(0, NpManagerExports.NpPollAsync(ctx));
        Span<byte> resultBytes = stackalloc byte[4];
        Assert.True(memory.TryRead(resultAddress, resultBytes));
        Assert.Equal(unchecked((int)0x80550006), BinaryPrimitives.ReadInt32LittleEndian(resultBytes));

        ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
        Assert.Equal(0, NpManagerExports.NpDeleteRequest(ctx));
    }

    [Theory]
    [InlineData(0, 1, 0x1_0000_0200UL)]
    [InlineData(1, -1, 0x1_0000_0200UL)]
    [InlineData(1, 1, 0UL)]
    public void AccountRequests_RejectInvalidArguments(int requestId, int userId, ulong outputAddress)
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen5);
        foreach (var query in new Func<CpuContext, int>[] { NpManagerExports.NpGetAccountLanguage2, NpManagerExports.NpGetAccountAge })
        {
            ctx[CpuRegister.Rdi] = unchecked((uint)requestId);
            ctx[CpuRegister.Rsi] = unchecked((uint)userId);
            ctx[CpuRegister.Rdx] = outputAddress;
            Assert.Equal(unchecked((int)0x80550003), query(ctx));
        }
    }

    [Theory]
    [InlineData("3Tcz5bNCfZQ", "sceNpGetAccountLanguage2")]
    [InlineData("+4DegjBqV1g", "sceNpGetAccountAge")]
    public void AccountRequests_RegisterPs5Nids(string nid, string name)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(Generation.Gen5));

        Assert.True(manager.TryGetExport(nid, out var export));
        Assert.Equal(name, export.Name);
        Assert.Equal("libSceNpManager", export.LibraryName);
    }

    [Fact]
    public void CheckNpReachability_RejectsAnInvalidRequest()
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen5);
        ctx[CpuRegister.Rdi] = 0;
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpCheckNpReachability(ctx));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void ProfileQueries_ReportSignedOutAndLeaveBuffersUntouched(Generation generation)
    {
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        var ctx = new CpuContext(memory, generation);
        ctx[CpuRegister.Rdi] = 0x10000000;

        // shadPS4 returns SIGNED_OUT without writing the output when PSN is
        // unavailable (np_manager.cpp:618-636 for GetOnlineId, 579-592 for
        // GetAccountIdA, 487-501 for GetAccountCountryA); the buffer keeps
        // whatever the guest left there.
        var accountIdAddress = MemoryBase + 0x100;
        Assert.True(memory.TryWrite(accountIdAddress, Enumerable.Repeat((byte)0xCC, 8).ToArray()));
        ctx[CpuRegister.Rsi] = accountIdAddress;
        Assert.Equal(unchecked((int)0x80550006), NpManagerExports.NpGetAccountIdA(ctx));
        Span<byte> accountId = stackalloc byte[8];
        Assert.True(memory.TryRead(accountIdAddress, accountId));
        Assert.All(accountId.ToArray(), b => Assert.Equal(0xCC, b));

        var onlineIdAddress = MemoryBase + 0x200;
        Assert.True(memory.TryWrite(onlineIdAddress, Enumerable.Repeat((byte)0xCC, 20).ToArray()));
        ctx[CpuRegister.Rsi] = onlineIdAddress;
        Assert.Equal(unchecked((int)0x80550006), NpManagerExports.NpGetOnlineId(ctx));
        Span<byte> onlineId = stackalloc byte[20];
        Assert.True(memory.TryRead(onlineIdAddress, onlineId));
        Assert.All(onlineId.ToArray(), b => Assert.Equal(0xCC, b));

        var countryAddress = MemoryBase + 0x300;
        Assert.True(memory.TryWrite(countryAddress, Enumerable.Repeat((byte)0xCC, 4).ToArray()));
        ctx[CpuRegister.Rsi] = countryAddress;
        Assert.Equal(unchecked((int)0x80550006), NpManagerExports.NpGetAccountCountryA(ctx));
        Span<byte> country = stackalloc byte[4];
        Assert.True(memory.TryRead(countryAddress, country));
        Assert.All(country.ToArray(), b => Assert.Equal(0xCC, b));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void ProfileQueries_RejectInvalidArguments(Generation generation)
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), generation);
        ctx[CpuRegister.Rdi] = unchecked((ulong)0xFFFFFFFF);
        ctx[CpuRegister.Rsi] = MemoryBase + 0x100;
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpGetAccountIdA(ctx));
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpGetOnlineId(ctx));
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpGetAccountCountryA(ctx));

        ctx[CpuRegister.Rdi] = 0x10000000;
        ctx[CpuRegister.Rsi] = 0;
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpGetAccountIdA(ctx));
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpGetOnlineId(ctx));
        Assert.Equal(unchecked((int)0x80550003), NpManagerExports.NpGetAccountCountryA(ctx));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void ValueQueries_ReturnDataWhileOffline(Generation generation)
    {
        // GetState reports SignedOut (1 == SignedOut per np_manager.h:22-26;
        // np_manager.cpp:657-678), HasSignedUp reports false
        // (np_manager.cpp:726-746), and GetNpReachabilityState reports
        // Unavailable (0 per np_manager.h:28-32; np_manager.cpp:641-655):
        // data + OK, unlike the SIGNED_OUT profile queries above.
        var memory = new FakeCpuMemory(MemoryBase, 0x1000);
        var ctx = new CpuContext(memory, generation);
        ctx[CpuRegister.Rdi] = 0x10000000;

        var stateAddress = MemoryBase + 0x100;
        ctx[CpuRegister.Rsi] = stateAddress;
        Assert.Equal(0, NpManagerExports.NpGetState(ctx));
        Span<byte> state = stackalloc byte[4];
        Assert.True(memory.TryRead(stateAddress, state));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(state));

        var signedUpAddress = MemoryBase + 0x200;
        ctx[CpuRegister.Rsi] = signedUpAddress;
        Assert.Equal(0, NpManagerExports.NpHasSignedUp(ctx));
        Span<byte> signedUp = stackalloc byte[1];
        Assert.True(memory.TryRead(signedUpAddress, signedUp));
        Assert.Equal(0, signedUp[0]);

        var reachabilityAddress = MemoryBase + 0x300;
        ctx[CpuRegister.Rsi] = reachabilityAddress;
        Assert.Equal(0, NpManagerExports.NpGetNpReachabilityState(ctx));
        Span<byte> reachability = stackalloc byte[4];
        Assert.True(memory.TryRead(reachabilityAddress, reachability));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(reachability));
    }
}
