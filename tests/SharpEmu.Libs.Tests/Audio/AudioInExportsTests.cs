// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Audio;
using Xunit;

namespace SharpEmu.Libs.Tests.Audio;

public sealed class AudioInExportsTests
{
    private const int NotOpened = unchecked((int)0x80260109);

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void OpenAndClose_ReportNotOpened(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);

        Assert.Equal(NotOpened, AudioInExports.Open(context));
        Assert.Equal(NotOpened, AudioInExports.Close(context));
    }
}
