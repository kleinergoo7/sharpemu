// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Reflection;
using SharpEmu.HLE;
using SharpEmu.Libs.Kernel;
using Xunit;

namespace SharpEmu.Libs.Tests.Pthread;

/// <summary>
/// Pins the guest-observable behavior of the hottest HLE synchronization
/// exports (lock/unlock/trylock/yield) while they are hammered the way the
/// game job-system spins on them. Any per-call overhead optimization must
/// keep every return code, state transition and ordering guarantee recorded
/// here identical.
/// </summary>
public sealed class PthreadHotPathParityTests
{
    private const ulong MemoryBase = 0x7_1000_0000;

    private static int Ok => (int)OrbisGen2Result.ORBIS_GEN2_OK;
    private static int Busy => (int)OrbisGen2Result.ORBIS_GEN2_ERROR_BUSY;
    private static int InvalidArgument => (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
    private static int NotFound => (int)OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND;

    [Fact]
    public void LockUnlock_HammerReturnsZeroAndLeavesMutexReleased()
    {
        const ulong mutexAddress = MemoryBase + 0x100;
        var memory = new PthreadMutexSemanticsTests.AllocatingCpuMemory(MemoryBase, 0x4000);
        var context = new CpuContext(memory, Generation.Gen5);

        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexInit, context, mutexAddress));
        const int iterations = 2000;
        for (var i = 0; i < iterations; i++)
        {
            Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexLock, context, mutexAddress));
            Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress));
        }

        // Behaviorally released: a fresh trylock acquires it immediately.
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexTrylock, context, mutexAddress));
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress));
        Assert.Equal((0UL, 0, 0), GetMutexState(mutexAddress));

        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexDestroy, context, mutexAddress));
    }

    [Fact]
    public void LockUnlockTrylock_ReplayedSequenceReturnsIdenticalCodes()
    {
        var first = RunScriptedSequence(MemoryBase + 0x200);
        var second = RunScriptedSequence(MemoryBase + 0x400);

        Assert.Equal(first.Codes, second.Codes);
        Assert.Equal(first.FinalState, second.FinalState);

        // Explicit pins for the straightforward steps, so a drift cannot hide
        // behind self-consistency.
        Assert.Equal(Ok, first.Codes[0]); // init
        Assert.Equal(Ok, first.Codes[1]); // lock free mutex
        Assert.Equal(Busy, first.Codes[2]); // self trylock on errorcheck mutex
        Assert.Equal(Ok, first.Codes[3]); // unlock
        Assert.Equal(Ok, first.Codes[4]); // trylock free mutex
        Assert.Equal(Ok, first.Codes[5]); // unlock
        Assert.Equal(InvalidArgument, first.Codes[6]); // unlock unowned
        Assert.Equal(Ok, first.Codes[7]); // destroy idle
    }

    [Fact]
    public void Yield_HammerAlwaysReturnsZero()
    {
        var memory = new PthreadMutexSemanticsTests.AllocatingCpuMemory(MemoryBase + 0x1000, 0x1000);
        var context = new CpuContext(memory, Generation.Gen5);
        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(Ok, KernelPthreadCompatExports.PthreadYield(context));
        }
    }

    [Fact]
    public void Trylock_HeldByOtherThreadReturnsBusyUntilRelease()
    {
        const ulong mutexAddress = MemoryBase + 0x600;
        var memory = new PthreadMutexSemanticsTests.AllocatingCpuMemory(MemoryBase, 0x4000);
        var context = new CpuContext(memory, Generation.Gen5);
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexInit, context, mutexAddress));
        // Pre-touch so the second thread never races implicit creation.
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexLock, context, mutexAddress));
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress));

        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? failure = null;
        var holder = new Thread(() =>
        {
            try
            {
                var holderContext = new CpuContext(memory, Generation.Gen5);
                Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexLock, holderContext, mutexAddress));
                held.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexUnlock, holderContext, mutexAddress));
            }
            catch (Exception error)
            {
                failure = error;
            }
        })
        { IsBackground = true };
        holder.Start();
        try
        {
            Assert.True(held.Wait(TimeSpan.FromSeconds(10)));
            for (var i = 0; i < 2000; i++)
            {
                Assert.Equal(Busy, Call(KernelPthreadCompatExports.PthreadMutexTrylock, context, mutexAddress));
            }

            // The spinning trylocks never queued, so the handoff is clean.
            var heldState = GetMutexState(mutexAddress);
            Assert.NotEqual(0UL, heldState.Owner);
            Assert.Equal(1, heldState.Recursion);
            Assert.Equal(0, heldState.Waiters);
        }
        finally
        {
            release.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(10)));
        }

        Assert.Null(failure);
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexTrylock, context, mutexAddress));
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress));
        Assert.Equal((0UL, 0, 0), GetMutexState(mutexAddress));
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexDestroy, context, mutexAddress));
    }

    [Fact]
    public void HotPathValidation_RejectsBadHandlesWithoutSideEffects()
    {
        var memory = new PthreadMutexSemanticsTests.AllocatingCpuMemory(MemoryBase + 0x2000, 0x4000);
        var context = new CpuContext(memory, Generation.Gen5);

        Assert.Equal(InvalidArgument, Call(KernelPthreadCompatExports.PthreadMutexLock, context, 0));
        Assert.Equal(InvalidArgument, Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, 0));
        Assert.Equal(InvalidArgument, Call(KernelPthreadCompatExports.PthreadMutexTrylock, context, 0));
        Assert.Equal(InvalidArgument, Call(KernelPthreadCompatExports.PthreadMutexDestroy, context, 0));

        // Unlocking a zero-initialized slot resolves (implicit init) and then
        // fails as an unowned unlock, exactly as before.
        const ulong freshAddress = MemoryBase + 0x2100;
        Assert.Equal(InvalidArgument, Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, freshAddress));
        Assert.Equal(Ok, Call(KernelPthreadCompatExports.PthreadMutexDestroy, context, freshAddress));

        // Destroying a slot that points at nothing known reports NOT_FOUND.
        const ulong garbageAddress = MemoryBase + 0x2200;
        Assert.True(context.TryWriteUInt64(garbageAddress, 0xDEAD_BEEF_1234_5678));
        Assert.Equal(NotFound, Call(KernelPthreadCompatExports.PthreadMutexDestroy, context, garbageAddress));
    }

    private sealed record SequenceResult(List<int> Codes, (ulong Owner, int Recursion, int Waiters) FinalState);

    private static SequenceResult RunScriptedSequence(ulong mutexAddress)
    {
        var memoryBase = mutexAddress & ~0xFFFUL;
        var memory = new PthreadMutexSemanticsTests.AllocatingCpuMemory(memoryBase, 0x4000);
        var context = new CpuContext(memory, Generation.Gen5);
        var codes = new List<int>
        {
            Call(KernelPthreadCompatExports.PthreadMutexInit, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexLock, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexTrylock, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexTrylock, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexUnlock, context, mutexAddress),
            Call(KernelPthreadCompatExports.PthreadMutexDestroy, context, mutexAddress),
        };
        var state = GetMutexStateOrDefault(mutexAddress);
        return new SequenceResult(codes, state);
    }

    private static int Call(Func<CpuContext, int> export, CpuContext context, ulong rdi)
    {
        context[CpuRegister.Rdi] = rdi;
        return export(context);
    }

    private static (ulong Owner, int Recursion, int Waiters) GetMutexState(ulong key)
    {
        var states = (System.Collections.IDictionary)typeof(KernelPthreadCompatExports)
            .GetField("_mutexStates", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var state = states[key];
        Assert.NotNull(state);
        var stateType = state.GetType();
        var owner = (ulong)stateType.GetProperty("OwnerThreadId", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(state)!;
        var recursion = (int)stateType.GetProperty("RecursionCount", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(state)!;
        var waiters = (int)stateType.GetProperty("QueuedWaiterCount", BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(state)!;
        return (owner, recursion, waiters);
    }

    private static (ulong Owner, int Recursion, int Waiters) GetMutexStateOrDefault(ulong key)
    {
        var states = (System.Collections.IDictionary)typeof(KernelPthreadCompatExports)
            .GetField("_mutexStates", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        return states.Contains(key) ? GetMutexState(key) : (0UL, 0, 0);
    }
}
