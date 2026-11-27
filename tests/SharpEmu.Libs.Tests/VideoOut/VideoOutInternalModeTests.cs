// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.VideoOut;
using Xunit;

namespace SharpEmu.Libs.Tests.VideoOut;

public sealed class VideoOutInternalModeTests
{
    private const int InvalidValue = unchecked((int)0x80290001);

    public VideoOutInternalModeTests()
    {
        VideoOutExports.ResetInternalModeForTests();
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void InternalModeHelpers_RecordRequestedSelection(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);

        Assert.Equal(0, VideoOutExports.VideoOutModeSetAny(context));
        Assert.Equal(0UL, VideoOutExports.ModeSetAnySelection);

        context[CpuRegister.Rdi] = 1;
        Assert.Equal(0, VideoOutExports.VideoOutModeSetAny(context));
        Assert.Equal(1UL, VideoOutExports.ModeSetAnySelection);

        context[CpuRegister.Rdi] = 0xF;
        Assert.Equal(0, VideoOutExports.VideoOutConfigureOutputMode(context));
        Assert.Equal(0xFUL, VideoOutExports.ConfigureOutputModeSelection);
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void InternalModeHelpers_RejectUnrepresentableMode(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        context[CpuRegister.Rdi] = 0x1_0000_0001;

        Assert.Equal(InvalidValue, VideoOutExports.VideoOutModeSetAny(context));
        Assert.Equal(0UL, VideoOutExports.ModeSetAnySelection);

        Assert.Equal(InvalidValue, VideoOutExports.VideoOutConfigureOutputMode(context));
        Assert.Equal(0UL, VideoOutExports.ConfigureOutputModeSelection);
    }
}
