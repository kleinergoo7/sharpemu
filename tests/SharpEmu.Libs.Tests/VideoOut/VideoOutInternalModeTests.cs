// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class VideoOutInternalModeTests
{
    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void InternalModeHelpers_ReturnSuccess(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);

        Assert.Equal(0, VideoOutExports.VideoOutModeSetAny(context));
        Assert.Equal(0, VideoOutExports.VideoOutConfigureOutputMode(context));
    }
}
