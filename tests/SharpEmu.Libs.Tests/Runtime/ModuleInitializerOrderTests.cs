// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Core.Loader;
using SharpEmu.Core.Memory;
using SharpEmu.Core.Runtime;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.Libs.Tests.Runtime;

public sealed class ModuleInitializerOrderTests
{
    [Fact]
    public void ResolveInitializerDependencies_StartsImportedModuleBeforeItsImporter()
    {
        var javascriptCore = Module("JavaScriptCore.prx", 11);
        var eaWebKit = Module("EAWebKit.prx", 8, "JavaScriptCore");
        var modulesByName = new Dictionary<string, SharpEmuRuntime.LoadedModuleImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["JavaScriptCore.prx"] = javascriptCore,
            ["EAWebKit.prx"] = eaWebKit,
        };

        var dependencies = SharpEmuRuntime.ResolveInitializerDependencies(eaWebKit, modulesByName);

        Assert.Equal(new[] { 11 }, dependencies);
    }

    [Fact]
    public void ResolveInitializerDependencies_HandlesTransitiveImportsAndCycles()
    {
        var baseModule = Module("base.prx", 1);
        var middle = Module("middle.prx", 2, "base");
        var top = Module("top.prx", 3, "middle", "unused.sprx");
        var modulesByName = new Dictionary<string, SharpEmuRuntime.LoadedModuleImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["base.prx"] = baseModule,
            ["middle.prx"] = middle,
            ["top.prx"] = top,
        };

        Assert.Equal(new[] { 1, 2 }, SharpEmuRuntime.ResolveInitializerDependencies(top, modulesByName));

        var cyclicA = Module("a.prx", 4, "b.prx");
        var cyclicB = Module("b.prx", 5, "a.prx");
        modulesByName["a.prx"] = cyclicA;
        modulesByName["b.prx"] = cyclicB;
        Assert.Equal(new[] { 5 }, SharpEmuRuntime.ResolveInitializerDependencies(cyclicA, modulesByName));
    }

    [Fact]
    public void TryResolveModuleByName_AddsPrxAndSprxExtensions()
    {
        var module = Module("codec.sprx", 7);
        var modulesByName = new Dictionary<string, SharpEmuRuntime.LoadedModuleImage>(StringComparer.OrdinalIgnoreCase)
        {
            ["codec.sprx"] = module,
        };

        Assert.True(SharpEmuRuntime.TryResolveModuleByName("codec", modulesByName, out var resolved));
        Assert.Equal(7, resolved.Handle);
        Assert.True(SharpEmuRuntime.TryResolveModuleByName("codec.sprx", modulesByName, out resolved));
        Assert.Equal(7, resolved.Handle);

        var jsc = Module("JavaScriptCore.prx", 9);
        modulesByName["JavaScriptCore.prx"] = jsc;
        Assert.True(SharpEmuRuntime.TryResolveModuleByName("libJavaScriptCore.prx", modulesByName, out resolved));
        Assert.Equal(9, resolved.Handle);
        Assert.True(SharpEmuRuntime.TryResolveModuleByName("libJavaScriptCore", modulesByName, out resolved));
        Assert.Equal(9, resolved.Handle);
        Assert.False(SharpEmuRuntime.TryResolveModuleByName("missing", modulesByName, out _));
    }

    [Theory]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_OK, 1UL, false)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_IMPLEMENTED, 1UL, true)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_IMPLEMENTED, ulong.MaxValue, false)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT, 1UL, false)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_CPU_TRAP, 0UL, false)]
    [InlineData(OrbisGen2Result.ORBIS_GEN2_ERROR_CPU_TRAP, 1UL, true)]
    public void ShouldTolerateInitializerResult_MatchesOnlyBenignStubValue(
        OrbisGen2Result result,
        ulong rawReturnValue,
        bool expected) =>
        Assert.Equal(expected, SharpEmuRuntime.ShouldTolerateInitializerResult(result, rawReturnValue));

    private static SharpEmuRuntime.LoadedModuleImage Module(
        string name,
        int handle,
        params string[] importedModuleNames) =>
        new(
            "/game/" + name,
            new SelfImage(
                isSelf: false,
                elfHeader: default,
                programHeaders: Array.Empty<ProgramHeader>(),
                mappedRegions: Array.Empty<VirtualMemoryRegion>(),
                importedModuleNames: importedModuleNames),
            handle,
            StartAtBoot: false);
}
