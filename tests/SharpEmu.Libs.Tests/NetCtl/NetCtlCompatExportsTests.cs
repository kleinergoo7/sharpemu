// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.NetCtl;
using Xunit;

namespace SharpEmu.Libs.Tests.NetCtl;

public sealed class NetCtlCompatExportsTests
{
    private const ulong MemoryBase = 0x1_0000_0000;

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void UnregisterCallback_RejectsIdsThatWereNeverIssued(int id)
    {
        var context = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        context[CpuRegister.Rdi] = unchecked((ulong)id);

        Assert.Equal(unchecked((int)0x80412107), NetCtlCompatExports.NetCtlUnregisterCallback(context));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void UnregisterCallback_ReportsNotRegisteredWithoutAStore(int id)
    {
        var context = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        context[CpuRegister.Rdi] = unchecked((ulong)(uint)id);

        Assert.Equal(unchecked((int)0x80550009), NetCtlCompatExports.NetCtlUnregisterCallback(context));
    }
}
