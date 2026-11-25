// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Np;
using Xunit;

namespace SharpEmu.Libs.Tests.Np;

public sealed class NpPartyExportsTests
{
    [Fact]
    public void RegisterHandler_ReportsNotInParty()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);

        Assert.Equal(unchecked((int)0x80552506), NpPartyExports.NpPartyRegisterHandler(context));
    }
}
