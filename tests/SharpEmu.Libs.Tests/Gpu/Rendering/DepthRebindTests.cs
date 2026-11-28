// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.Libs.Gpu.GpuCommands.Registers;
using SharpEmu.Libs.Gpu.Rendering;
using SharpEmu.Libs.Tests.Gpu.Images;
using SharpEmu.Libs.Tests.Gpu.Scheduling;
using Xunit;
using static SharpEmu.Libs.Tests.Gpu.Rendering.RenderExecutorFixtures;

namespace SharpEmu.Libs.Tests.Gpu.Rendering;

// A guest depth-target rebind mid-frame ends the pass and resolves the new target again
// instead of aborting the recording.
[Collection(SchedulingStateCollection.Name)]
public sealed class DepthRebindTests : IDisposable
{
    private const ulong SecondDepthBase = RecordingRenderHost.MemoryBase + 0x70_0000;

    private readonly RecordingRenderHost _host = new();
    private readonly FakePipelineProvider _pipelines = new();
    private readonly RenderExecutor _executor;
    private readonly FatalScope _fatal = new();

    public DepthRebindTests()
    {
        _pipelines.Graphics = Programs(vertexBuffers: [new VertexInputBuffer(VertexBase, 16, 4)]);
        _executor = new RenderExecutor(_host, _pipelines);
    }

    public void Dispose() => _fatal.Dispose();

    private static RegisterBanks DepthBanks(ulong depthBase)
    {
        var banks = Banks(withDepth: true);
        banks.Context.DepthTarget = RegisterWords.Depth(depthBase, 64, 64);
        banks.MarkWritten(1u << (int)RegisterStamp.DepthTargets);
        return banks;
    }

    private static bool IsDepthFind(string call) =>
        call.StartsWith("find_image", StringComparison.Ordinal) && call.Contains("DepthTarget", StringComparison.Ordinal);

    // Two different depth addresses across one recording resolve two attachments without a fatal.
    [Fact]
    public void DifferentDepthTargetsAcrossOneRecordingResolveTwoAttachments()
    {
        _executor.DrawIndexed(7, DepthBanks(DepthBase), Indexed(6));
        _executor.DrawIndexed(8, DepthBanks(SecondDepthBase), Indexed(6));

        Assert.Empty(_fatal.Messages);
        Assert.Contains(_host.Calls, call => call.StartsWith($"acquire_depth {DepthBase:X}", StringComparison.Ordinal));
        Assert.Contains(_host.Calls, call => call.StartsWith($"acquire_depth {SecondDepthBase:X}", StringComparison.Ordinal));
        Assert.DoesNotContain(_host.Calls, call => call.StartsWith("reresolve_depth", StringComparison.Ordinal));
        Assert.DoesNotContain(_host.Calls, call => call == "end_rendering");
        Assert.Equal(2, _host.BegunRenderings.Count);
        Assert.NotEqual(
            _host.BegunRenderings[0].DepthStencilAttachment.View.Handle,
            _host.BegunRenderings[1].DepthStencilAttachment.View.Handle);
    }

    // The retired discovery image is resolved again after a pass split, and no later step
    // consumes the stale identifier.
    [Fact]
    public void ARetiredDepthImageIsResolvedAgainAfterAPassSplit()
    {
        _host.Scope = new DrawScopeToken(1, 1, 1);
        _host.RetiredDepthBases.Add(DepthBase);
        var banks = DepthBanks(DepthBase);
        _executor.DrawIndexed(7, banks, Indexed(6));
        _executor.DrawIndexed(8, banks, Indexed(6));

        Assert.Empty(_fatal.Messages);
        Assert.Single(_host.Calls, call => call.StartsWith("reresolve_depth", StringComparison.Ordinal));
        Assert.Single(_host.Calls, call => call == "end_rendering");
        Assert.Single(_host.Calls, IsDepthFind);

        var resolved = _host.ImageOf(DepthBase);
        Assert.Contains(_host.Calls, call => call.StartsWith($"transition_depth {resolved.Index} ", StringComparison.Ordinal));
        foreach (var call in _host.Calls.Where(call => call.StartsWith("keep_depth", StringComparison.Ordinal)))
        {
            Assert.Equal($"keep_depth {resolved.Index}", call);
        }

        Assert.Single(_host.Calls, call => call.StartsWith("acquire_depth", StringComparison.Ordinal));
    }

    // An unchanged depth target behaves exactly as before: the second draw reuses the
    // acquisition without another lookup, another acquisition or a pass split.
    [Fact]
    public void AnUnchangedDepthTargetKeepsItsAcquisition()
    {
        _host.Scope = new DrawScopeToken(1, 1, 1);
        var banks = DepthBanks(DepthBase);
        _executor.DrawIndexed(7, banks, Indexed(6));
        _host.Calls.Clear();
        _executor.DrawIndexed(8, banks, Indexed(6));

        Assert.Empty(_fatal.Messages);
        Assert.DoesNotContain(_host.Calls, call => call.StartsWith("find_image", StringComparison.Ordinal) || call.StartsWith("acquire_", StringComparison.Ordinal));
        Assert.DoesNotContain(_host.Calls, call => call == "end_rendering");
        Assert.Contains(_host.Calls, call => call.StartsWith("keep_depth", StringComparison.Ordinal));
        Assert.Contains(_host.Calls, call => call.StartsWith("begin_rendering", StringComparison.Ordinal));
        Assert.Contains(_host.Calls, call => call.StartsWith("draw_indexed 6 1 0 0 0", StringComparison.Ordinal));
    }
}
