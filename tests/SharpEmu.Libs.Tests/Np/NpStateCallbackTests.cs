// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using SharpEmu.Libs.Np;
using Xunit;

namespace SharpEmu.Libs.Tests.Np;

[Collection("GuestThreadFlowProfile")]
public sealed class NpStateCallbackTests
{
    private const ulong MemoryBase = 0x10_0000_0000;
    private const int NotRegistered = unchecked((int)0x80550009);

    public NpStateCallbackTests()
    {
        NpStateCallbackState.ResetForTests();
    }

    [Fact]
    public void CheckCallback_DeliversQueuedSignedOutStateToLegacyCallback()
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        var scheduler = new RecordingScheduler();
        var previous = GuestThreadExecution.Scheduler;
        try
        {
            GuestThreadExecution.Scheduler = scheduler;
            ctx[CpuRegister.Rdi] = 0x1000;
            ctx[CpuRegister.Rsi] = 0x2000;
            Assert.Equal(0, NpManagerExports.NpRegisterStateCallback(ctx));

            NpStateCallbackState.QueueState(0x1000_0000, NpStateCallbackState.StateSignedOut);
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));

            var call = Assert.Single(scheduler.Calls);
            Assert.Equal(0x1000UL, call.EntryPoint);
            Assert.Equal(0x1000_0000UL, call.Arg0);
            Assert.Equal((ulong)NpStateCallbackState.StateSignedOut, call.Arg1);
            Assert.Equal(0UL, call.Arg2);
            Assert.Equal(0x2000UL, call.Arg3);

            scheduler.Calls.Clear();
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));
            Assert.Empty(scheduler.Calls);
        }
        finally
        {
            GuestThreadExecution.Scheduler = previous;
            NpStateCallbackState.ResetForTests();
        }
    }

    [Fact]
    public void UnregisterStateCallback_StopsFurtherDelivery()
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        var scheduler = new RecordingScheduler();
        var previous = GuestThreadExecution.Scheduler;
        try
        {
            GuestThreadExecution.Scheduler = scheduler;
            ctx[CpuRegister.Rdi] = 0x1000;
            ctx[CpuRegister.Rsi] = 0x2000;
            Assert.Equal(0, NpManagerExports.NpRegisterStateCallback(ctx));
            // The legacy slot carries no id, so the id-based unregister reports
            // NOT_REGISTERED while still clearing the slot for teardown.
            Assert.Equal(NotRegistered, NpStateCallbackExports.NpUnregisterStateCallback(ctx));

            NpStateCallbackState.QueueState(0x1000_0000, NpStateCallbackState.StateSignedOut);
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));
            Assert.Empty(scheduler.Calls);
        }
        finally
        {
            GuestThreadExecution.Scheduler = previous;
            NpStateCallbackState.ResetForTests();
        }
    }

    [Fact]
    public void CheckCallback_DeliversToCallbackA_AndUnregisterById()
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        var scheduler = new RecordingScheduler();
        var previous = GuestThreadExecution.Scheduler;
        try
        {
            GuestThreadExecution.Scheduler = scheduler;
            ctx[CpuRegister.Rdi] = 0x3000;
            ctx[CpuRegister.Rsi] = 0x4000;
            var id = NpManagerExports.NpRegisterStateCallbackA(ctx);
            Assert.True(id > 0);

            NpStateCallbackState.QueueState(123, NpStateCallbackState.StateSignedOut);
            Assert.Equal(0, NpManagerExports.NpCheckCallbackForLib(ctx));
            var call = Assert.Single(scheduler.Calls);
            Assert.Equal(0x3000UL, call.EntryPoint);
            Assert.Equal(123UL, call.Arg0);
            Assert.Equal((ulong)NpStateCallbackState.StateSignedOut, call.Arg1);
            Assert.Equal(0x4000UL, call.Arg2);

            ctx[CpuRegister.Rdi] = unchecked((ulong)id);
            Assert.Equal(0, NpManagerExports.NpUnregisterStateCallbackA(ctx));
            NpStateCallbackState.QueueState(123, NpStateCallbackState.StateSignedOut);
            scheduler.Calls.Clear();
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));
            Assert.Empty(scheduler.Calls);
        }
        finally
        {
            GuestThreadExecution.Scheduler = previous;
            NpStateCallbackState.ResetForTests();
        }
    }

    [Fact]
    public void DoubleRegister_ReturnsAlreadyRegistered()
    {
        // 0x80550008 == ORBIS_NP_ERROR_CALLBACK_ALREADY_REGISTERED (np_error.h:17);
        // shadPS4 rejects a second legacy registration (np_manager.cpp:981-995).
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        ctx[CpuRegister.Rdi] = 0x1000;
        ctx[CpuRegister.Rsi] = 0x2000;
        Assert.Equal(0, NpManagerExports.NpRegisterStateCallback(ctx));
        ctx[CpuRegister.Rdi] = 0x3000;
        ctx[CpuRegister.Rsi] = 0x4000;
        Assert.Equal(unchecked((int)0x80550008), NpManagerExports.NpRegisterStateCallback(ctx));
    }

    [Fact]
    public void UnregisterA_UnknownId_ReturnsNotRegistered()
    {
        // 0x80550009 == ORBIS_NP_ERROR_CALLBACK_NOT_REGISTERED (np_error.h:18);
        // shadPS4 returns it for an id with no live entry (np_manager.cpp:887-901).
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        ctx[CpuRegister.Rdi] = 0xDEAD;
        Assert.Equal(unchecked((int)0x80550009), NpManagerExports.NpUnregisterStateCallbackA(ctx));
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("a")]
    [InlineData("reachability")]
    [InlineData("toolkit")]
    public void NullCallback_ReturnsInvalidArgument(string kind)
    {
        // 0x80550003 == ORBIS_NP_ERROR_INVALID_ARGUMENT (np_error.h:11); shadPS4
        // rejects a null callback for every state/reachability registration
        // (np_manager.cpp:981-995, 857-860, 1017-1022, 1049-1056).
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        ctx[CpuRegister.Rdi] = 0;
        ctx[CpuRegister.Rsi] = 0x2000;
        int result = kind switch
        {
            "legacy" => NpManagerExports.NpRegisterStateCallback(ctx),
            "a" => NpManagerExports.NpRegisterStateCallbackA(ctx),
            "reachability" => NpManagerExports.NpRegisterNpReachabilityStateCallback(ctx),
            _ => NpManagerExports.NpRegisterStateCallbackForToolkit(ctx),
        };
        Assert.Equal(unchecked((int)0x80550003), result);
    }

    [Fact]
    public void CheckCallback_DeliversToolkitPerEvent_AndReachabilityOnce()
    {
        // Reachability is edge-triggered per user in shadPS4: it fires only when
        // the derived state changes (np_manager.cpp:922-933, 958-961). The toolkit
        // callback fires per state event (np_manager.cpp:953-955).
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        var scheduler = new RecordingScheduler();
        var previous = GuestThreadExecution.Scheduler;
        try
        {
            GuestThreadExecution.Scheduler = scheduler;
            ctx[CpuRegister.Rdi] = 0x5000;
            ctx[CpuRegister.Rsi] = 0x6000;
            Assert.Equal(0, NpManagerExports.NpRegisterStateCallbackForToolkit(ctx));
            ctx[CpuRegister.Rdi] = 0x7000;
            ctx[CpuRegister.Rsi] = 0x8000;
            Assert.Equal(0, NpManagerExports.NpRegisterNpReachabilityStateCallback(ctx));

            NpStateCallbackState.QueueState(0x1000_0000, NpStateCallbackState.StateSignedOut);
            NpStateCallbackState.QueueState(0x1000_0000, NpStateCallbackState.StateSignedOut);
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));

            var toolkitCalls = scheduler.Calls.FindAll(c => c.EntryPoint == 0x5000);
            var reachabilityCalls = scheduler.Calls.FindAll(c => c.EntryPoint == 0x7000);
            Assert.Equal(2, toolkitCalls.Count);
            Assert.All(toolkitCalls, c =>
            {
                Assert.Equal(0x1000_0000UL, c.Arg0);
                Assert.Equal((ulong)NpStateCallbackState.StateSignedOut, c.Arg1);
                // shadPS4 calls A/toolkit/reachability callbacks with three
                // args (user_id, state, userdata); only the legacy slot gets
                // the four-arg (user_id, state, 0, userdata) shape
                // (shadPS4 np_manager.cpp CheckCallback dispatch).
                Assert.Equal(0x6000UL, c.Arg2);
            });
            Assert.Single(reachabilityCalls);
            Assert.Equal(0x1000_0000UL, reachabilityCalls[0].Arg0);
            Assert.Equal((ulong)NpStateCallbackState.ReachabilityUnavailable, reachabilityCalls[0].Arg1);
            Assert.Equal(0x8000UL, reachabilityCalls[0].Arg2);
        }
        finally
        {
            GuestThreadExecution.Scheduler = previous;
            NpStateCallbackState.ResetForTests();
        }
    }

    [Fact]
    public void UnregisterStateCallback_UnknownId_ReportsNotRegistered()
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        var scheduler = new RecordingScheduler();
        var previous = GuestThreadExecution.Scheduler;
        try
        {
            GuestThreadExecution.Scheduler = scheduler;
            ctx[CpuRegister.Rdi] = 0x1234;
            Assert.Equal(NotRegistered, NpStateCallbackExports.NpUnregisterStateCallback(ctx));

            NpStateCallbackState.QueueState(123, NpStateCallbackState.StateSignedOut);
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));
            Assert.Empty(scheduler.Calls);
        }
        finally
        {
            GuestThreadExecution.Scheduler = previous;
            NpStateCallbackState.ResetForTests();
        }
    }

    [Fact]
    public void UnregisterStateCallback_RemovesRegisteredCallbackA()
    {
        var ctx = new CpuContext(new FakeCpuMemory(MemoryBase, 0x1000), Generation.Gen4);
        var scheduler = new RecordingScheduler();
        var previous = GuestThreadExecution.Scheduler;
        try
        {
            GuestThreadExecution.Scheduler = scheduler;
            ctx[CpuRegister.Rdi] = 0x3000;
            ctx[CpuRegister.Rsi] = 0x4000;
            var id = NpManagerExports.NpRegisterStateCallbackA(ctx);
            Assert.True(id > 0);

            NpStateCallbackState.QueueState(123, NpStateCallbackState.StateSignedOut);
            Assert.Equal(0, NpManagerExports.NpCheckCallbackForLib(ctx));
            Assert.Single(scheduler.Calls);

            ctx[CpuRegister.Rdi] = unchecked((ulong)id);
            Assert.Equal(0, NpStateCallbackExports.NpUnregisterStateCallback(ctx));

            NpStateCallbackState.QueueState(123, NpStateCallbackState.StateSignedOut);
            scheduler.Calls.Clear();
            Assert.Equal(0, NpManagerExports.NpCheckCallback(ctx));
            Assert.Empty(scheduler.Calls);
        }
        finally
        {
            GuestThreadExecution.Scheduler = previous;
            NpStateCallbackState.ResetForTests();
        }
    }

    private sealed class RecordingScheduler : IGuestThreadScheduler
    {
        public List<(ulong EntryPoint, ulong Arg0, ulong Arg1, ulong Arg2, ulong Arg3)> Calls { get; } = [];
        public bool SupportsGuestContextTransfer => false;

        public bool TryCallGuestFunction(CpuContext callerContext, ulong entryPoint, ulong firstArgument, ulong secondArgument,
            ulong stackAddress, ulong stackSize, string reason, out string? error)
        {
            Calls.Add((entryPoint, firstArgument, secondArgument, 0, 0));
            error = null;
            return true;
        }

        public bool TryCallGuestFunction(CpuContext callerContext, ulong entryPoint, ulong firstArgument, ulong secondArgument,
            ulong thirdArgument, ulong stackAddress, ulong stackSize, string reason, out ulong returnValue, out string? error)
        {
            Calls.Add((entryPoint, firstArgument, secondArgument, thirdArgument, 0));
            returnValue = 0;
            error = null;
            return true;
        }

        public bool TryCallGuestFunction(CpuContext callerContext, ulong entryPoint, ulong firstArgument, ulong secondArgument,
            ulong thirdArgument, ulong fourthArgument, ulong stackAddress, ulong stackSize, string reason,
            out ulong returnValue, out string? error)
        {
            Calls.Add((entryPoint, firstArgument, secondArgument, thirdArgument, fourthArgument));
            returnValue = 0;
            error = null;
            return true;
        }

        public void RegisterGuestThreadContext(ulong threadHandle, CpuContext context) => throw new NotSupportedException();
        public bool TryStartThread(CpuContext creatorContext, GuestThreadStartRequest request, out string? error) => throw new NotSupportedException();
        public bool TryJoinThread(CpuContext callerContext, ulong threadHandle, out ulong returnValue, out string? error) => throw new NotSupportedException();
        public void Pump(CpuContext callerContext, string reason) => throw new NotSupportedException();
        public int WakeBlockedThreads(string wakeKey, int maxCount = int.MaxValue) => throw new NotSupportedException();
        public bool HasPendingGuestExceptionForCurrentThread() => false;
        public bool TrySetGuestThreadPriority(ulong guestThreadHandle, int guestPriority) => throw new NotSupportedException();
        public bool TrySetGuestThreadAffinity(ulong guestThreadHandle, ulong affinityMask) => throw new NotSupportedException();
        public IReadOnlyList<GuestThreadSnapshot> SnapshotThreads() => throw new NotSupportedException();
        public bool TryCallGuestContinuation(CpuContext callerContext, GuestCpuContinuation continuation, string reason,
            out string? error) => throw new NotSupportedException();
        public bool TryRaiseGuestException(CpuContext callerContext, ulong threadHandle, ulong handler, int exceptionType,
            out string? error) => throw new NotSupportedException();
    }
}
