// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Stubs;
using Xunit;

namespace SharpEmu.Libs.Tests.Stubs;

public sealed class GameLiveStreamingExportTests
{
    private const int InvalidArgument = (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void InitializeIsRegisteredForBothGenerations(Generation generation)
    {
        GameServiceStubs.ResetServiceLifetimeForTests();
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("kvYEw2lBndk", out var export));
        Assert.Equal("sceGameLiveStreamingInitialize", export.Name);

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(0, GameServiceStubs.GameLiveStreamingInitialize(context));
        Assert.True(GameServiceStubs.IsGameLiveStreamingInitialized);

        // Re-init is the boot gate staying open: still success.
        Assert.Equal(0, GameServiceStubs.GameLiveStreamingInitialize(context));
        Assert.True(GameServiceStubs.IsGameLiveStreamingInitialized);
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void SharePlayInitializeIsRegisteredForBothGenerations(Generation generation)
    {
        GameServiceStubs.ResetServiceLifetimeForTests();
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("isruqthpYcw", out var export));
        Assert.Equal("sceSharePlayInitialize", export.Name);

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(0, GameServiceStubs.SharePlayInitialize(context));
        Assert.True(GameServiceStubs.IsSharePlayInitialized);

        // Re-init is the boot gate staying open: still success.
        Assert.Equal(0, GameServiceStubs.SharePlayInitialize(context));
        Assert.True(GameServiceStubs.IsSharePlayInitialized);
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void LiveStreamingTerminateIsRegisteredForBothGenerations(Generation generation)
    {
        GameServiceStubs.ResetServiceLifetimeForTests();
        var manager = new ModuleManager();
        manager.RegisterExports(SharpEmu.Generated.SysAbiExportRegistry.CreateExports(generation));

        Assert.True(manager.TryGetExport("9yK6Fk8mKOQ", out var export));
        Assert.Equal("sceGameLiveStreamingTerminate", export.Name);

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(0, GameServiceStubs.GameLiveStreamingInitialize(context));
        Assert.Equal(0, GameServiceStubs.GameLiveStreamingTerminate(context));
        Assert.False(GameServiceStubs.IsGameLiveStreamingInitialized);
    }

    [Theory]
    [InlineData(Generation.Gen4)]
    [InlineData(Generation.Gen5)]
    public void LiveStreamingTerminateWithoutInitializeReportsError(Generation generation)
    {
        GameServiceStubs.ResetServiceLifetimeForTests();

        var context = new CpuContext(new FakeCpuMemory(0x1_0000_0000, 0x1000), generation);
        Assert.Equal(InvalidArgument, GameServiceStubs.GameLiveStreamingTerminate(context));
        Assert.False(GameServiceStubs.IsGameLiveStreamingInitialized);
    }
}
