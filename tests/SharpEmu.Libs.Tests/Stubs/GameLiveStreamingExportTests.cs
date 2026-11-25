// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Stubs;
using Xunit;

namespace SharpEmu.Libs.Tests.Stubs;

public sealed class GameLiveStreamingExportTests
{
    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void InitializeIsRegisteredForBothGenerations(Generation generation)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("kvYEw2lBndk", out var export));
        Assert.Equal("sceGameLiveStreamingInitialize", export.Name);

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(0, GameServiceStubs.GameLiveStreamingInitialize(context));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void SharePlayInitializeIsRegisteredForBothGenerations(Generation generation)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("isruqthpYcw", out var export));
        Assert.Equal("sceSharePlayInitialize", export.Name);

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(0, GameServiceStubs.SharePlayInitialize(context));
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void LiveStreamingTerminateIsRegisteredForBothGenerations(Generation generation)
    {
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("9yK6Fk8mKOQ", out var export));
        Assert.Equal("sceGameLiveStreamingTerminate", export.Name);

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(0, GameServiceStubs.GameLiveStreamingTerminate(context));
    }
}
