// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Audio;
using Xunit;

namespace SharpEmu.Libs.Tests.Audio;

public sealed class AudioInExportsTests
{
    private const int NotOpened = unchecked((int)0x80260109);
    private const int InvalidArgument = (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
    private const ulong ParamsAddress = 0x1_0000_0000;

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void Open_WithValidArguments_ReportsNotOpened(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        context[CpuRegister.Rcx] = ParamsAddress;

        Assert.Equal(NotOpened, AudioInExports.Open(context));
    }

    [Theory]
    [InlineData(Generation.Gen4, 0)]
    [InlineData(Generation.Gen4, 1)]
    [InlineData(Generation.Gen4, 2)]
    [InlineData(Generation.Gen4, 3)]
    [InlineData(Generation.Gen5, 0)]
    [InlineData(Generation.Gen5, 1)]
    [InlineData(Generation.Gen5, 2)]
    [InlineData(Generation.Gen5, 3)]
    public void Open_RejectsInvalidArguments(Generation generation, int badField)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        context[CpuRegister.Rdi] = 0;
        context[CpuRegister.Rsi] = 0;
        context[CpuRegister.Rdx] = 0;
        context[CpuRegister.Rcx] = ParamsAddress;

        // Device, port and priority ids are never negative (poisoned via the
        // sign bit) and the params pointer must be provided.
        switch (badField)
        {
            case 0:
                context[CpuRegister.Rdi] = ulong.MaxValue;
                break;
            case 1:
                context[CpuRegister.Rsi] = ulong.MaxValue;
                break;
            case 2:
                context[CpuRegister.Rdx] = ulong.MaxValue;
                break;
            default:
                context[CpuRegister.Rcx] = 0;
                break;
        }

        Assert.Equal(InvalidArgument, AudioInExports.Open(context));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void Close_WithInvalidHandle_ReportsInvalidArgument(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        context[CpuRegister.Rdi] = 0;

        Assert.Equal(InvalidArgument, AudioInExports.Close(context));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void Close_WithWellFormedHandle_ReportsNotOpened(Generation generation)
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        context[CpuRegister.Rdi] = 1;

        Assert.Equal(NotOpened, AudioInExports.Close(context));
    }
}
