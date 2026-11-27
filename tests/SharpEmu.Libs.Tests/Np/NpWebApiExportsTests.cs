// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Np;
using Xunit;

namespace SharpEmu.Libs.Tests.Np;

public sealed class NpWebApiExportsTests
{
    private const ulong MemoryBase = 0x1_0000_0000;
    private const int InvalidArgument = unchecked((int)0x80553402);

    [Fact]
    public void CreateHandle_ReturnsDistinctHandlesAcrossCalls()
    {
        var context = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen5);
        NpWebApiExports.ResetRuntimeState();
        try
        {
            var first = NpWebApiExports.NpWebApiCreateHandle(context);
            var second = NpWebApiExports.NpWebApiCreateHandle(context);
            Assert.True(first > 0);
            Assert.True(second > first);
        }
        finally
        {
            NpWebApiExports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Initialize_RejectsInvalidHttpContextIdAndPoolSize()
    {
        var context = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen5);
        NpWebApiExports.ResetRuntimeState();
        NpWebApi2Exports.ResetRuntimeState();
        try
        {
            context[CpuRegister.Rdi] = 0;
            context[CpuRegister.Rsi] = 0x1000;
            Assert.Equal(InvalidArgument, NpWebApiExports.NpWebApiInitialize(context));

            context[CpuRegister.Rdi] = 1;
            context[CpuRegister.Rsi] = 0;
            Assert.Equal(InvalidArgument, NpWebApiExports.NpWebApiInitialize(context));

            context[CpuRegister.Rdi] = 1;
            context[CpuRegister.Rsi] = 0x1000;
            Assert.True(NpWebApiExports.NpWebApiInitialize(context) > 0);
        }
        finally
        {
            NpWebApiExports.ResetRuntimeState();
            NpWebApi2Exports.ResetRuntimeState();
        }
    }

    [Fact]
    public void Reset_ClearsTheHandleCounter()
    {
        var context = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen5);
        NpWebApiExports.ResetRuntimeState();
        try
        {
            var first = NpWebApiExports.NpWebApiCreateHandle(context);
            Assert.True(first > 0);
            NpWebApiExports.ResetRuntimeState();
            Assert.Equal(first, NpWebApiExports.NpWebApiCreateHandle(context));
        }
        finally
        {
            NpWebApiExports.ResetRuntimeState();
        }
    }
}
