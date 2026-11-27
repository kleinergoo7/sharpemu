// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Np;
using Xunit;

namespace SharpEmu.Libs.Tests.Np;

public sealed class NpPartyExportsTests
{
    private const int AlreadyInitialized = unchecked((int)0x80552501);
    private const int NotInitialized = unchecked((int)0x80552502);
    private const int InvalidArgument = unchecked((int)0x80552503);
    private const int NotInParty = unchecked((int)0x80552506);

    public NpPartyExportsTests()
    {
        NpPartyExports.ResetRuntimeState();
    }

    [Fact]
    public void Initialize_Terminate_LifecycleSucceeds()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);

        Assert.Equal(0, NpPartyExports.NpPartyInitialize(context));
        Assert.Equal(0, NpPartyExports.NpPartyCheckCallback(context));
        Assert.Equal(0, NpPartyExports.NpPartyTerminate(context));
    }

    [Fact]
    public void Initialize_Twice_ReportsAlreadyInitialized()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);

        Assert.Equal(0, NpPartyExports.NpPartyInitialize(context));
        Assert.Equal(AlreadyInitialized, NpPartyExports.NpPartyInitialize(context));
    }

    [Fact]
    public void Terminate_WithoutInitialize_ReportsNotInitialized()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);

        Assert.Equal(NotInitialized, NpPartyExports.NpPartyTerminate(context));
    }

    [Fact]
    public void Terminate_Twice_ReportsNotInitialized()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);

        Assert.Equal(0, NpPartyExports.NpPartyInitialize(context));
        Assert.Equal(0, NpPartyExports.NpPartyTerminate(context));
        Assert.Equal(NotInitialized, NpPartyExports.NpPartyTerminate(context));
    }

    [Fact]
    public void CheckCallback_WithoutInitialize_ReportsNotInitialized()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);

        Assert.Equal(NotInitialized, NpPartyExports.NpPartyCheckCallback(context));
    }

    [Fact]
    public void RegisterHandler_ReportsNotInPartyAndStoresRegistration()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);
        context[CpuRegister.Rdi] = 0x1000;
        context[CpuRegister.Rsi] = 0x2000;

        Assert.Equal(NotInParty, NpPartyExports.NpPartyRegisterHandler(context));
        Assert.Equal(0x1000UL, NpPartyExports.RegisteredHandlerCallback);
        Assert.Equal(0x2000UL, NpPartyExports.RegisteredHandlerUserData);
    }

    [Fact]
    public void RegisterHandler_WithNullCallback_ReportsInvalidArgument()
    {
        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), Generation.Gen4);
        context[CpuRegister.Rdi] = 0;
        context[CpuRegister.Rsi] = 0x2000;

        Assert.Equal(InvalidArgument, NpPartyExports.NpPartyRegisterHandler(context));
        Assert.Equal(0UL, NpPartyExports.RegisteredHandlerCallback);
    }
}
